using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(NpcAgentRuntime))]
[RequireComponent(typeof(NpcTaskController))]
[RequireComponent(typeof(AgentWorkProfile))]
public class FsmAgentController : MonoBehaviour
{
    private const float WorkRetryIntervalWorldHours = 0.04f;
    private const float BarnReturnRetryIntervalWorldHours = 0.05f;
    private const float BarnReturnStuckWorldHours = 0.5f;

    private static readonly List<FsmAgentController> activeControllers = new List<FsmAgentController>();

    [SerializeField] private WorldTimeManager worldTimeManager;
    [SerializeField] private FsmStrategyConfig strategy = new FsmStrategyConfig();

    private Character character;
    private CharacterIdentity identity;
    private NpcAgentRuntime agentRuntime;
    private NpcTaskController taskController;
    private AgentWorkProfile workProfile;
    private SleepExecutor sleepExecutor;
    private NpcHealth npcHealth;

    private ResourceStorage reassignmentBarn;
    private Vector3 reassignmentBarnTarget;
    private float nextWorkAttemptTotalWorldHours;
    private float nextBarnReturnAttemptTotalWorldHours;
    private bool reassignmentRequested;
    private bool reassignmentDepositStarted;
    private bool travellingToBarnForReassignment;
    private bool hadActiveWorkTask;
    private bool missingWorldTimeManagerWarningLogged;

    public string ArchitectureName => "FSM";
    public FsmAgentState CurrentState => FsmStrategyCoordinator.CurrentState;
    public bool IsReassignmentRequested => reassignmentRequested;
    public int CharacterId => identity != null ? identity.CharacterId : int.MaxValue;
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
        strategy.Validate();
        ResolveWorldTimeManager();

        Debug.Assert(
            character != null,
            $"Missing component: Character. Expected location: same GameObject as {nameof(FsmAgentController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            identity != null,
            $"Missing component: CharacterIdentity. Expected location: same GameObject as {nameof(FsmAgentController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            taskController != null,
            $"Missing component: NpcTaskController. Expected location: same GameObject as {nameof(FsmAgentController)}. GameObject: {gameObject.name}."
        );
    }

    private void OnEnable()
    {
        if (!activeControllers.Contains(this))
            activeControllers.Add(this);
    }

    private void OnDisable()
    {
        activeControllers.Remove(this);

        if (taskController != null && taskController.IsTaskActive)
            taskController.CancelCurrentTask();

        if (character != null)
            character.StopMovement();

        ResetReassignmentState();
        CharacterControlRegistry.ReleaseAutonomousControl(character);
    }

    private void OnValidate()
    {
        if (strategy == null)
            strategy = new FsmStrategyConfig();

        strategy.Validate();
    }

    private void Update()
    {
        if (character == null || taskController == null || IsDead)
            return;

        ResolveWorldTimeManager();
        ManualDayPlanState.UpdateDailyLoopFlagReset(worldTimeManager);
        FsmSettlementDevelopmentCoordinator.TryRun(
            this,
            activeControllers,
            worldTimeManager,
            strategy
        );
        FsmStrategyCoordinator.TryEvaluate(this, activeControllers, worldTimeManager, strategy);

        if (CharacterControlRegistry.IsPlayerControlled(character))
        {
            StopForPlayerControl();
            return;
        }

        if (!CharacterControlRegistry.TryTakeAutonomousControl(character))
            return;

        if (HandleSleepWindow())
            return;

        if (IsRequiredSleepStillRunning())
            return;

        if (reassignmentRequested)
        {
            UpdateWorkReassignment();
            if (reassignmentRequested)
                return;
        }

        if (taskController.IsTaskActive)
        {
            hadActiveWorkTask = IsWorkTask(taskController.CurrentTask);
            return;
        }

        if (hadActiveWorkTask || taskController.HasAssignedWorkRole)
        {
            hadActiveWorkTask = false;
            RequestWorkReassignment();
            UpdateWorkReassignment();
            return;
        }

        TryStartPlannedWork();
    }

    public void RequestWorkReassignment()
    {
        if (reassignmentRequested || IsDead)
            return;

        reassignmentRequested = true;
        nextBarnReturnAttemptTotalWorldHours = 0f;

        SimulationEventLogger.LogRoleEvent(
            identity,
            "ReassignmentRequested",
            taskController != null && taskController.HasAssignedWorkRole
                ? taskController.AssignedWorkRole
                : (NpcWorkRole?)null,
            "FSM requested that the citizen stop current work, return to the barn, and receive a new assignment."
        );
    }

    public bool IsAssignedToRole(NpcWorkRole role)
    {
        return identity != null &&
            NpcWorkRoleAssignmentRegistry.TryGetAssignedRole(identity, out NpcWorkRole assignedRole) &&
            assignedRole == role;
    }

    private void TryStartPlannedWork()
    {
        if (!IsWorldTimeReached(nextWorkAttemptTotalWorldHours))
            return;

        if (taskController.TryStartManualDayPlanWorkTask(true, false))
        {
            hadActiveWorkTask = true;
            nextWorkAttemptTotalWorldHours = 0f;
            return;
        }

        nextWorkAttemptTotalWorldHours = GetTotalWorldHours() + WorkRetryIntervalWorldHours;
    }

    private bool HandleSleepWindow()
    {
        if (!IsSleepTime())
            return false;

        AgentContext context = CreateContext();

        if (context.SleepState == SleepExecutionState.Completed)
        {
            if (taskController.IsTaskActive && taskController.CurrentTask != AgentTaskType.Sleep)
                taskController.CancelCurrentTask();

            taskController.SetIdle();
            ResetReassignmentState();
            return true;
        }

        if (IsCurrentTaskActive(AgentTaskType.Sleep) || IsCurrentTaskActive(AgentTaskType.GoHome))
            return true;

        if (taskController.IsTaskActive || taskController.HasAssignedWorkRole)
            taskController.CancelCurrentTask();

        if (character != null)
            character.StopMovement();

        ResetReassignmentState();
        hadActiveWorkTask = false;

        if (context.IsAtOvernightDestination)
        {
            if (!taskController.TryStartSleepTask())
                taskController.SetIdle();

            return true;
        }

        if (!taskController.TryStartGoHomeTask())
            taskController.SetIdle();

        return true;
    }

    private bool IsRequiredSleepStillRunning()
    {
        if (!IsCurrentTaskActive(AgentTaskType.Sleep))
            return false;

        return sleepExecutor == null || !sleepExecutor.IsCompleted;
    }

    private void UpdateWorkReassignment()
    {
        if (!reassignmentRequested)
            return;

        if (reassignmentDepositStarted)
        {
            UpdateReassignmentDeposit();
            return;
        }

        if (travellingToBarnForReassignment)
        {
            UpdateBarnReturnTravel();
            return;
        }

        if (taskController.IsTaskActive || taskController.HasAssignedWorkRole)
            taskController.CancelCurrentTask();

        hadActiveWorkTask = false;

        if (character.HasDepositLoad)
        {
            if (taskController.TryStartDepositResourcesTask())
            {
                reassignmentDepositStarted = true;
                return;
            }

            ScheduleBarnReturnRetry();
            return;
        }

        TryStartBarnReturnTravel();
    }

    private void UpdateReassignmentDeposit()
    {
        if (taskController.IsTaskActive)
            return;

        reassignmentDepositStarted = false;

        if (taskController.TaskState == AgentTaskState.Completed && !character.HasDepositLoad)
        {
            CompleteWorkReassignment();
            return;
        }

        if (!IsWorldTimeReached(nextBarnReturnAttemptTotalWorldHours))
            return;

        if (character.HasDepositLoad && taskController.TryStartDepositResourcesTask())
        {
            reassignmentDepositStarted = true;
            return;
        }

        TryStartBarnReturnTravel();
    }

    private void TryStartBarnReturnTravel()
    {
        if (!IsWorldTimeReached(nextBarnReturnAttemptTotalWorldHours))
            return;

        ResourceStorageLookupResult lookupResult = ResourceStorageRegistry.GetClosestStorage(
            transform.position,
            CharacterId,
            out reassignmentBarn
        );

        if (lookupResult != ResourceStorageLookupResult.Found || reassignmentBarn == null)
        {
            ScheduleBarnReturnRetry();
            return;
        }

        if (reassignmentBarn.IsInInteractionRange(transform.position))
        {
            CompleteWorkReassignment();
            return;
        }

        if (!reassignmentBarn.TryGetInteractionPosition(
            transform.position,
            CharacterId,
            out reassignmentBarnTarget))
        {
            ScheduleBarnReturnRetry();
            return;
        }

        if (!character.TryMoveTo(reassignmentBarnTarget))
        {
            ScheduleBarnReturnRetry();
            return;
        }

        travellingToBarnForReassignment = true;
    }

    private void UpdateBarnReturnTravel()
    {
        if (reassignmentBarn == null)
        {
            StopBarnReturnTravel();
            ScheduleBarnReturnRetry();
            return;
        }

        if (reassignmentBarn.IsInInteractionRange(transform.position) ||
            character.HasReachedInteractionTarget(reassignmentBarnTarget))
        {
            CompleteWorkReassignment();
            return;
        }

        if (character.HasMovementPathFailed() || character.HasMovementStalled(BarnReturnStuckWorldHours))
        {
            StopBarnReturnTravel();
            ScheduleBarnReturnRetry();
        }
    }

    private void CompleteWorkReassignment()
    {
        if (character != null)
            character.StopMovement();

        SimulationEventLogger.LogRoleEvent(
            identity,
            "ReturnedForReassignment",
            null,
            "Citizen reached the barn and is ready to request a new role."
        );

        ResetReassignmentState();
        nextWorkAttemptTotalWorldHours = 0f;
        TryStartPlannedWork();
    }

    private void ScheduleBarnReturnRetry()
    {
        reassignmentBarn = null;
        reassignmentBarnTarget = Vector3.zero;
        travellingToBarnForReassignment = false;
        nextBarnReturnAttemptTotalWorldHours = GetTotalWorldHours() + BarnReturnRetryIntervalWorldHours;
    }

    private void StopBarnReturnTravel()
    {
        if (character != null)
            character.StopMovement();

        reassignmentBarn = null;
        reassignmentBarnTarget = Vector3.zero;
        travellingToBarnForReassignment = false;
    }

    private void ResetReassignmentState()
    {
        reassignmentRequested = false;
        reassignmentDepositStarted = false;
        travellingToBarnForReassignment = false;
        reassignmentBarn = null;
        reassignmentBarnTarget = Vector3.zero;
        nextBarnReturnAttemptTotalWorldHours = 0f;
    }

    private void StopForPlayerControl()
    {
        if (taskController != null && (taskController.IsTaskActive || taskController.HasAssignedWorkRole))
            taskController.CancelCurrentTask();

        if (character != null)
            character.StopMovement();

        ResetReassignmentState();
        hadActiveWorkTask = false;
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

    private static bool IsWorkTask(AgentTaskType taskType)
    {
        return taskType == AgentTaskType.GatherResource ||
            taskType == AgentTaskType.ExploreFog ||
            taskType == AgentTaskType.Build;
    }

    private bool IsSleepTime()
    {
        if (worldTimeManager == null)
            return false;

        return workProfile != null && workProfile.IsSleepTime(worldTimeManager.CurrentHour);
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
            $"{nameof(FsmAgentController)} could not find a WorldTimeManager. " +
            $"Fallback timing will use real time. GameObject: {gameObject.name}."
        );
        missingWorldTimeManagerWarningLogged = true;
    }
}
