/*
===============================================================================
Script:
GoapAgentController

Purpose:
Plans one observable high-level work cycle at a time and delegates execution to
the same shared task components used by the other decision architectures.

Folder:
Assets/Scripts/Agents/GOAP/

Scene Hierarchy:
_World
- Characters
  - GoapAgent

Attach To:
GoapAgent character root

Inspector References:
- worldTimeManager (optional; automatically resolved)
- strategy
===============================================================================
*/

using System.Collections.Generic;
using System.Text;
using AgentComparison.Goap;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(NpcAgentRuntime))]
[RequireComponent(typeof(NpcTaskController))]
[RequireComponent(typeof(AgentWorkProfile))]
public class GoapAgentController : MonoBehaviour
{
    private static readonly List<GoapAgentController> activeControllers = new List<GoapAgentController>();

    [SerializeField] private WorldTimeManager worldTimeManager;
    [SerializeField] private GoapStrategyConfig strategy = new GoapStrategyConfig();
    [SerializeField] private bool logPlanChanges = true;

    [Header("Runtime GOAP State")]
    [SerializeField] private string currentGoal = "None";
    [SerializeField] private string currentAction = "None";
    [SerializeField] private string currentPlan = "None";
    [SerializeField, TextArea(2, 4)] private string currentWorldState = "Not evaluated";
    [SerializeField, TextArea(2, 4)] private string currentStatus = "Waiting for play mode";

    private readonly List<GoapGoal> goals = new List<GoapGoal>(4);

    private Character character;
    private CharacterIdentity identity;
    private NpcAgentRuntime agentRuntime;
    private NpcTaskController taskController;
    private AgentWorkProfile workProfile;
    private SleepExecutor sleepExecutor;
    private NpcHealth npcHealth;
    private GoapPlanner planner;
    private IReadOnlyList<GoapAction> actions;
    private readonly List<GoapAction> availableActions = new List<GoapAction>();
    private readonly Dictionary<string, float> blockedUntil = new Dictionary<string, float>();
    private GoapAction pendingAction;
    private long foodBeforeAction;
    private long woodBeforeAction;
    private int revealedBeforeAction;
    private int builtBeforeAction;
    private ExploreFogExecutor exploreExecutor;
    private float nextDecisionTotalWorldHours;
    private int firstDecisionFrame;
    private bool missingWorldTimeManagerWarningLogged;
    private string lastLoggedPlan = string.Empty;

    public string ArchitectureName => "GOAP";
    public string CurrentGoal => currentGoal;
    public string CurrentAction => currentAction;
    public string CurrentPlan => currentPlan;
    public bool IsDead => npcHealth != null && npcHealth.IsDead;
    public int DailyFoodRequirement => npcHealth != null ? npcHealth.DailyFoodRequirement : 5;
    public int DailyWoodRequirement => npcHealth != null ? npcHealth.DailyWoodRequirement : 2;
    public SimulationBalanceConfig SimulationBalanceConfig => npcHealth != null ? npcHealth.BalanceConfig : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        activeControllers.Clear();
    }

    private void Awake()
    {
        agentRuntime = GetComponent<NpcAgentRuntime>();
        if (agentRuntime != null)
        {
            agentRuntime.EnsureInitialized();
            character = agentRuntime.Character;
            identity = agentRuntime.Identity;
            taskController = agentRuntime.TaskController;
            workProfile = agentRuntime.WorkProfile;
            sleepExecutor = agentRuntime.SleepExecutor;
        }

        if (character == null)
            character = GetComponent<Character>();

        if (identity == null)
            identity = GetComponent<CharacterIdentity>();

        if (taskController == null)
            taskController = GetComponent<NpcTaskController>();

        if (workProfile == null)
            workProfile = GetComponent<AgentWorkProfile>();

        if (sleepExecutor == null)
            sleepExecutor = GetComponent<SleepExecutor>();

        npcHealth = GetComponent<NpcHealth>();
        exploreExecutor = GetComponent<ExploreFogExecutor>();
        strategy.Validate();
        planner = new GoapPlanner();
        actions = GoapSurvivalDomain.CreateActions();
        ResolveWorldTimeManager();

        Debug.Assert(
            character != null,
            $"Missing component: Character. Expected location: same GameObject as {nameof(GoapAgentController)}. GameObject: {gameObject.name}."
        );
        Debug.Assert(
            identity != null,
            $"Missing component: CharacterIdentity. Expected location: same GameObject as {nameof(GoapAgentController)}. GameObject: {gameObject.name}."
        );
        Debug.Assert(
            taskController != null,
            $"Missing component: NpcTaskController. Expected location: same GameObject as {nameof(GoapAgentController)}. GameObject: {gameObject.name}."
        );
    }

    private void OnEnable()
    {
        if (!activeControllers.Contains(this))
            activeControllers.Add(this);

        firstDecisionFrame = Time.frameCount + 1;
    }

    private void OnDisable()
    {
        pendingAction = null;
        blockedUntil.Clear();
        activeControllers.Remove(this);

        if (taskController != null && taskController.IsTaskActive)
            taskController.CancelCurrentTask();

        if (character != null)
            character.StopMovement();

        CharacterControlRegistry.ReleaseAutonomousControl(character);
    }

    private void OnValidate()
    {
        if (strategy == null)
            strategy = new GoapStrategyConfig();

        strategy.Validate();
    }

    private void Update()
    {
        if (character == null || taskController == null || IsDead)
            return;

        ResolveWorldTimeManager();

        RunMorningDevelopment();

        if (CharacterControlRegistry.IsPlayerControlled(character))
        {
            StopForPlayerControl();
            return;
        }

        if (!CharacterControlRegistry.TryTakeAutonomousControl(character))
            return;

        ObserveActionOutcome();

        if (HandleSleepWindow())
            return;

        if (IsRequiredSleepStillRunning() || taskController.IsTaskActive)
            return;

        if (!IsWorkTime())
        {
            SetIdleStatus("Outside the configured work window.");
            return;
        }

        if (Time.frameCount < firstDecisionFrame || !IsWorldTimeReached(nextDecisionTotalWorldHours))
            return;

        PlanAndStartNextAction();
    }

    private void PlanAndStartNextAction()
    {
        GoapSettlementSnapshot snapshot = GoapSettlementSnapshot.Capture(activeControllers);
        bool storageAvailable = ResourceStorageRegistry.TryGetClosestStorage(transform.position, out _);
        GoapWorldState worldState = snapshot.CreateWorldState(
            strategy.SafeReserveDays, character.HasDepositLoad, storageAvailable);
        currentWorldState = snapshot.GetSummary();

        goals.Clear();
        if (!worldState.IsTrue(GoapFact.FoodReserveSafe))
            goals.Add(GoapSurvivalDomain.DeliverFood(strategy.FoodReservePriority));
        if (!worldState.IsTrue(GoapFact.WoodReserveSafe))
            goals.Add(GoapSurvivalDomain.DeliverWood(strategy.WoodReservePriority));
        if (snapshot.HasActiveConstructionSite)
            goals.Add(GoapSurvivalDomain.CompleteConstructionSite(strategy.ConstructionPriority));
        if (snapshot.UnrevealedFogCount > 0)
            goals.Add(GoapSurvivalDomain.RevealExplorationTarget(strategy.ExplorationPriority));
        // Recovery still works when there is no other reachable work. Higher-priority
        // work plans include Deposit as a causal prerequisite when hands are occupied.
        if (character.HasDepositLoad)
            goals.Add(GoapSurvivalDomain.ReturnLoad(int.MinValue));

        if (goals.Count == 0)
        {
            currentGoal = "None";
            currentAction = "Wait";
            currentPlan = "No current work goal";
            SetIdleStatus("Reserves are safe and no construction or exploration remains.");
            return;
        }

        availableActions.Clear();
        for (int i = 0; i < actions.Count; i++)
        {
            GoapAction action = actions[i];
            if (!blockedUntil.TryGetValue(action.Name, out float until))
            {
                availableActions.Add(action);
                continue;
            }

            if (!IsWorldTimeReached(until))
                continue;

            blockedUntil.Remove(action.Name);
            if (action.Name == GoapSurvivalDomain.ExploreActionName && identity != null)
                FogRegistry.ClearUnreachableFogForRequester(identity.CharacterId);

            availableActions.Add(action);
        }

        GoapPlan plan = planner.CreatePlan(worldState, goals, availableActions);
        if (!plan.IsSuccess)
        {
            SetNoPlanStatus($"{plan.FailureKind}: {plan.FailureReason}");
            return;
        }

        currentGoal = plan.Goal.Name;
        currentPlan = FormatPlan(plan.Actions);

        if (plan.Actions.Count == 0)
        {
            currentAction = "None";
            SetIdleStatus("All currently provided GOAP goals are satisfied.");
            return;
        }

        GoapAction nextAction = plan.Actions[0];
        currentAction = nextAction.Name;

        bool isDeposit = nextAction.Name == GoapSurvivalDomain.DepositActionName;
        bool hasRole = TryGetWorkRole(nextAction, out NpcWorkRole role);
        if (!isDeposit && !hasRole)
        {
            ScheduleDecisionRetry($"GOAP action {nextAction.Name} has no shared work-role mapping.");
            return;
        }

        bool started = isDeposit
            ? taskController.TryStartDepositResourcesTask()
            : taskController.TryStartWorkRole(role, false);
        if (!started)
        {
            BlockAction(nextAction.Name);
            ScheduleDecisionRetry($"Shared task controller could not start GOAP action {nextAction.Name}.");
            return;
        }

        pendingAction = nextAction;
        foodBeforeAction = character.TotalFoodDeposited;
        woodBeforeAction = character.TotalWoodDeposited;
        revealedBeforeAction = exploreExecutor != null ? exploreExecutor.RevealedTargetCount : 0;
        builtBeforeAction = taskController.CompletedConstructionCount;
        nextDecisionTotalWorldHours = 0f;
        currentStatus = $"Executing {nextAction.Name} for goal {plan.Goal.Name}.";
        LogPlanSelection(plan, snapshot);
    }

    private void RunMorningDevelopment()
    {
        if (identity == null)
            return;
        for (int i = 0; i < activeControllers.Count; i++)
        {
            GoapAgentController other = activeControllers[i];
            if (other != null && other.isActiveAndEnabled && !other.IsDead && other.identity != null &&
                other.identity.CharacterId < identity.CharacterId)
                return;
        }
        SettlementMorningDevelopment.TryRun(ArchitectureName, worldTimeManager, SimulationBalanceConfig,
            DailyFoodRequirement, DailyWoodRequirement, strategy.PopulationFoodReserveDays);
    }

    private void ObserveActionOutcome()
    {
        if (pendingAction == null || taskController.IsTaskActive)
            return;

        string actionName = pendingAction.Name;
        bool effectObserved = false;
        switch (actionName)
        {
            case GoapSurvivalDomain.GatherFoodActionName:
                effectObserved = character.TotalFoodDeposited > foodBeforeAction;
                break;
            case GoapSurvivalDomain.GatherWoodActionName:
                effectObserved = character.TotalWoodDeposited > woodBeforeAction;
                break;
            case GoapSurvivalDomain.ExploreActionName:
                effectObserved = exploreExecutor != null && exploreExecutor.RevealedTargetCount > revealedBeforeAction;
                break;
            case GoapSurvivalDomain.BuildActionName:
                effectObserved = taskController.CompletedConstructionCount > builtBeforeAction;
                break;
            case GoapSurvivalDomain.DepositActionName:
                effectObserved = !character.HasDepositLoad;
                break;
        }

        bool success = taskController.TaskState == AgentTaskState.Completed && effectObserved;
        pendingAction = null;
        SimulationEventLogger.LogPlanChange(success ? "GoapActionSucceeded" : "GoapActionFailed",
            $"NPC={identity.CharacterId}; Action={actionName}; TaskState={taskController.TaskState}; EffectObserved={effectObserved}");
        if (!success)
        {
            BlockAction(actionName);
            ScheduleDecisionRetry($"{actionName} did not produce its expected effect; observing again before retry.");
        }
    }

    private void BlockAction(string actionName)
    {
        blockedUntil[actionName] = GetTotalWorldHours() + strategy.FailedActionCooldownWorldHours;
    }

    private void InterruptPlan(string reason)
    {
        if (pendingAction == null)
            return;
        SimulationEventLogger.LogPlanChange("GoapActionInterrupted",
            $"NPC={identity.CharacterId}; Action={pendingAction.Name}; Reason={reason}");
        pendingAction = null;
    }

    private bool HandleSleepWindow()
    {
        if (!IsSleepTime())
            return false;

        InterruptPlan("Sleep window");

        AgentContext context = CreateContext();

        if (context.SleepState == SleepExecutionState.Completed)
        {
            if (taskController.IsTaskActive && taskController.CurrentTask != AgentTaskType.Sleep)
                taskController.CancelCurrentTask();

            taskController.SetIdle();
            currentAction = "SleepComplete";
            currentStatus = "Required sleep is complete; waiting for the work window.";
            return true;
        }

        if (IsCurrentTaskActive(AgentTaskType.Sleep) || IsCurrentTaskActive(AgentTaskType.GoHome))
            return true;

        if (taskController.IsTaskActive || taskController.HasAssignedWorkRole)
            taskController.CancelCurrentTask();

        if (character != null)
            character.StopMovement();

        currentGoal = "Sleep";
        currentPlan = context.IsAtOvernightDestination ? "Sleep" : "GoHome -> Sleep";

        if (context.IsAtOvernightDestination)
        {
            currentAction = "Sleep";
            currentStatus = taskController.TryStartSleepTask()
                ? "Sleeping at the overnight destination."
                : "Could not start sleep task.";
            return true;
        }

        currentAction = "GoHome";
        currentStatus = taskController.TryStartGoHomeTask()
            ? "Returning to the overnight destination."
            : "Could not start go-home task.";
        return true;
    }

    private bool IsRequiredSleepStillRunning()
    {
        if (!IsCurrentTaskActive(AgentTaskType.Sleep))
            return false;

        return sleepExecutor == null || !sleepExecutor.IsCompleted;
    }

    private void SetNoPlanStatus(string reason)
    {
        currentGoal = "None";
        currentAction = "Wait";
        currentPlan = "No reachable plan";
        ScheduleDecisionRetry(reason);
    }

    private void SetIdleStatus(string reason)
    {
        taskController.SetIdle();
        currentStatus = reason;
        nextDecisionTotalWorldHours = GetTotalWorldHours() + strategy.RetryIntervalWorldHours;
    }

    private void ScheduleDecisionRetry(string reason)
    {
        currentStatus = reason;
        nextDecisionTotalWorldHours = GetTotalWorldHours() + strategy.RetryIntervalWorldHours;

        if (logPlanChanges && lastLoggedPlan != reason)
        {
            Debug.LogWarning($"GOAP decision waiting: {reason} GameObject: {gameObject.name}.");
            lastLoggedPlan = reason;
        }
    }

    private void LogPlanSelection(GoapPlan plan, GoapSettlementSnapshot snapshot)
    {
        string planKey = $"{currentGoal}|{currentPlan}|{currentWorldState}";
        if (logPlanChanges && lastLoggedPlan != planKey)
        {
            Debug.Log(
                $"GOAP selected goal {currentGoal} and plan {currentPlan}. " +
                $"{snapshot.GetSummary()} GameObject: {gameObject.name}."
            );
            lastLoggedPlan = planKey;
        }

        SimulationEventLogger.LogPlanChange(
            "GoapPlanSelected",
            $"NPC={identity.CharacterId}; Goal={currentGoal}; Plan={currentPlan}; Cost={plan.TotalCost}; {snapshot.GetSummary()}"
        );
    }

    private void StopForPlayerControl()
    {
        InterruptPlan("Player control");
        if (taskController != null && (taskController.IsTaskActive || taskController.HasAssignedWorkRole))
            taskController.CancelCurrentTask();

        if (character != null)
            character.StopMovement();

        currentStatus = "Paused because the character is player-controlled.";
        CharacterControlRegistry.ReleaseAutonomousControl(character);
    }

    private AgentContext CreateContext()
    {
        return agentRuntime != null
            ? new AgentContext(agentRuntime)
            : new AgentContext(character);
    }

    private bool IsCurrentTaskActive(AgentTaskType taskType)
    {
        return taskController != null &&
            taskController.CurrentTask == taskType &&
            taskController.IsTaskActive;
    }

    private bool IsWorkTime()
    {
        return worldTimeManager != null &&
            workProfile != null &&
            workProfile.IsWorkTime(worldTimeManager.CurrentHour);
    }

    private bool IsSleepTime()
    {
        return worldTimeManager != null &&
            workProfile != null &&
            workProfile.IsSleepTime(worldTimeManager.CurrentHour);
    }

    private float GetTotalWorldHours()
    {
        return worldTimeManager != null ? worldTimeManager.TotalWorldHours : Time.time;
    }

    private bool IsWorldTimeReached(float targetTotalWorldHours)
    {
        return GetTotalWorldHours() >= targetTotalWorldHours;
    }

    private void ResolveWorldTimeManager()
    {
        if (worldTimeManager != null)
            return;

        worldTimeManager = FindFirstObjectByType<WorldTimeManager>();
        if (worldTimeManager != null || missingWorldTimeManagerWarningLogged)
            return;

        Debug.LogWarning(
            $"{nameof(GoapAgentController)} could not find a WorldTimeManager. " +
            $"The agent will wait until one becomes available. GameObject: {gameObject.name}."
        );
        missingWorldTimeManagerWarningLogged = true;
    }

    private static bool TryGetWorkRole(GoapAction action, out NpcWorkRole role)
    {
        role = NpcWorkRole.Food;
        if (action == null)
            return false;

        switch (action.Name)
        {
            case GoapSurvivalDomain.GatherFoodActionName:
                role = NpcWorkRole.Food;
                return true;
            case GoapSurvivalDomain.GatherWoodActionName:
                role = NpcWorkRole.Wood;
                return true;
            case GoapSurvivalDomain.ExploreActionName:
                role = NpcWorkRole.Explore;
                return true;
            case GoapSurvivalDomain.BuildActionName:
                role = NpcWorkRole.Build;
                return true;
            default:
                return false;
        }
    }

    private static string FormatPlan(IReadOnlyList<GoapAction> planActions)
    {
        if (planActions == null || planActions.Count == 0)
            return "Already satisfied";

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < planActions.Count; i++)
        {
            if (i > 0)
                builder.Append(" -> ");

            builder.Append(planActions[i].Name);
        }

        return builder.ToString();
    }
}
