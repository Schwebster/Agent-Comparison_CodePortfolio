/*
===============================================================================
Script:
NoAgentManualController

Purpose:
Runs NoAgent NPCs from the shared manual barn plan without adding separate work execution.

Folder:
Assets/Scripts/Agents/Shared/

Scene Hierarchy:
_World
- Characters
  - Character

Attach To:
NoAgent NPC character root

Required Children:
None

Dependencies:
- Character
- NpcAgentRuntime
- NpcTaskController
- AgentWorkProfile
- CharacterControlRegistry
- WorldTimeManager

Automatically Resolved:
- Character, NpcTaskController, and AgentWorkProfile on the same GameObject
- WorldTimeManager in the scene, if not assigned

Inspector References:
- worldTimeManager
===============================================================================
*/

using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(NpcAgentRuntime))]
[RequireComponent(typeof(NpcTaskController))]
[RequireComponent(typeof(AgentWorkProfile))]
public class NoAgentManualController : MonoBehaviour
{
    private const float DefaultDecisionIntervalSeconds = 1f;
    private const int BarnPathResolutionGraceFrames = 10;
    private const float BarnReturnStuckWorldHours = 0.5f;
    private const float BarnReturnProgressDistance = 0.1f;
    private const float ExploreIdleDiagnosticIntervalSeconds = 5f;
    private const float StartupDiagnosticFirstDelaySeconds = 2f;
    private const float StartupDiagnosticIntervalSeconds = 3f;
    private const float StartupDiagnosticDurationSeconds = 20f;
    private const int StartupDiagnosticSampleLimit = 8;

    private static readonly List<NoAgentManualController> activeControllers = new List<NoAgentManualController>();
    private static float startupDiagnosticStartedAtRealtime = -1f;
    private static float nextStartupDiagnosticRealtime = -1f;

    [SerializeField] private WorldTimeManager worldTimeManager;
    [Tooltip("Real seconds between idle work decisions. This keeps decision cost stable when simulation speed changes.")]
    [SerializeField, Min(0.1f)] private float decisionIntervalSeconds = DefaultDecisionIntervalSeconds;
    [SerializeField] private bool logExploreIdleDiagnostics;
    [SerializeField] private bool logStartupMovementDiagnostics;
    [SerializeField] private bool enablePerformanceCounters;
    [SerializeField] private bool logPerformanceCounterSummaries;

    private Character character;
    private NavMeshAgent navigationAgent;
    private CharacterIdentity identity;
    private NpcAgentRuntime agentRuntime;
    private NpcTaskController taskController;
    private ExploreFogExecutor exploreFogExecutor;
    private SleepExecutor sleepExecutor;
    private AgentWorkProfile workProfile;
    private ResourceStorage startupBarn;
    private Vector3 startupBarnInteractionPosition;
    private Vector3 lastBarnReturnPosition;
    private float decisionSecondsRemaining;
    private float nextExploreIdleDiagnosticTime;
    private float barnReturnNoProgressWorldHours;
    private int barnPathValidationAllowedFrame = int.MinValue;
    private bool shouldReturnToBarnAfterWork;
    private bool shouldRequestWorkFromCurrentPosition;
    private bool wasTaskActiveLastFrame;
    private AgentTaskType lastActiveTaskType = AgentTaskType.None;
    private bool isMovingToStartupBarn;
    private bool missingWorldTimeManagerWarningLogged;
    private bool missingStartupBarnWarningLogged;
    private bool barnPathWarningLogged;
    private bool noRunnableWorkWarningLogged;
    private string lastStartupBarnRouteState = "NotRequested";
    private ResourceStorage.InteractionPositionDiagnostics lastStartupBarnInteractionDiagnostics;

    public string ArchitectureName => "NoAgent";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStartupDiagnostics()
    {
        activeControllers.Clear();
        startupDiagnosticStartedAtRealtime = -1f;
        nextStartupDiagnosticRealtime = -1f;
    }

    private void Awake()
    {
        character = GetComponent<Character>();
        navigationAgent = GetComponent<NavMeshAgent>();
        identity = GetComponent<CharacterIdentity>();
        agentRuntime = GetComponent<NpcAgentRuntime>();
        if (agentRuntime != null)
        {
            agentRuntime.EnsureInitialized();
            character = agentRuntime.Character;
            identity = agentRuntime.Identity;
            taskController = agentRuntime.TaskController;
            exploreFogExecutor = agentRuntime.ExploreFogExecutor;
            sleepExecutor = agentRuntime.SleepExecutor;
            workProfile = agentRuntime.WorkProfile;
        }

        if (taskController == null)
            taskController = GetComponent<NpcTaskController>();

        if (exploreFogExecutor == null)
            exploreFogExecutor = GetComponent<ExploreFogExecutor>();

        if (sleepExecutor == null)
            sleepExecutor = GetComponent<SleepExecutor>();

        if (workProfile == null)
            workProfile = GetComponent<AgentWorkProfile>();

        ResolveWorldTimeManager();
        ConfigurePerformanceCounters();

        Debug.Assert(
            character != null,
            $"Missing component: Character. Expected location: same GameObject as NoAgentManualController. Script: {nameof(NoAgentManualController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            taskController != null,
            $"Missing component: NpcTaskController. Expected location: same GameObject as NoAgentManualController. Script: {nameof(NoAgentManualController)}. GameObject: {gameObject.name}."
        );
    }

    private void OnEnable()
    {
        if (!activeControllers.Contains(this))
            activeControllers.Add(this);

        if (startupDiagnosticStartedAtRealtime < 0f)
        {
            startupDiagnosticStartedAtRealtime = Time.realtimeSinceStartup;
            nextStartupDiagnosticRealtime = startupDiagnosticStartedAtRealtime + StartupDiagnosticFirstDelaySeconds;
        }
    }

    private void OnDisable()
    {
        activeControllers.Remove(this);

        if (taskController != null && taskController.IsTaskActive)
            taskController.CancelCurrentTask();

        shouldRequestWorkFromCurrentPosition = false;
        wasTaskActiveLastFrame = false;
        lastActiveTaskType = AgentTaskType.None;
        ClearBarnMovementState();
        noRunnableWorkWarningLogged = false;
        CharacterControlRegistry.ReleaseAutonomousControl(character);
    }

    private void Update()
    {
        if (character == null || taskController == null)
            return;

        MaybeLogStartupMovementDiagnostic();
        ConfigurePerformanceCounters();
        ResolveWorldTimeManager();
        ManualDayPlanState.UpdateDailyLoopFlagReset(worldTimeManager);

        if (CharacterControlRegistry.IsPlayerControlled(character))
        {
            StopForPlayerControl();
            return;
        }

        if (!CharacterControlRegistry.TryTakeAutonomousControl(character))
            return;

        AgentContext context = null;
        if (IsSleepTime())
        {
            if (IsActiveNightTask())
            {
                SimulationPerformanceCounters.AddNightAgentContextBuildAvoided();
                return;
            }

            context = CreateContext();
            SimulationPerformanceCounters.AddNightAgentContextBuild();
            if (HandleSleepTime(context))
                return;
        }

        // Active sleep owns its full world-hour duration, even after the work window begins.
        if (taskController.IsTaskActive)
        {
            wasTaskActiveLastFrame = true;
            lastActiveTaskType = taskController.CurrentTask;
            MaybeLogExploreIdleDiagnostic("task active but not ExploreFog", false);
            return;
        }

        if (wasTaskActiveLastFrame && IsWorkTime())
        {
            if (context == null)
                context = CreateContext();

            decisionSecondsRemaining = 0f;
            shouldRequestWorkFromCurrentPosition = ShouldRequestWorkFromCurrentPositionAfterTask(context);
        }

        wasTaskActiveLastFrame = false;
        lastActiveTaskType = AgentTaskType.None;

        if (isMovingToStartupBarn)
        {
            decisionSecondsRemaining = 0f;
            if (!UpdateBarnTravel())
                return;
        }

        decisionSecondsRemaining = Mathf.Max(0f, decisionSecondsRemaining - Time.deltaTime);
        if (decisionSecondsRemaining > 0f)
        {
            MaybeLogExploreIdleDiagnostic("waiting for next decision tick", true);
            return;
        }

        if (context == null)
            context = CreateContext();

        decisionSecondsRemaining = Mathf.Max(0.1f, decisionIntervalSeconds);
        SimulationPerformanceCounters.AddAgentContextReuse();
        TickWorkDecision(context);
    }

    private AgentContext CreateContext()
    {
        if (agentRuntime != null)
            return new AgentContext(agentRuntime);

        return new AgentContext(character);
    }

    private bool IsActiveNightTask()
    {
        if (taskController == null || !taskController.IsTaskActive)
            return false;

        if (taskController.CurrentTask == AgentTaskType.GoHome)
            return true;

        if (taskController.CurrentTask != AgentTaskType.Sleep)
            return false;

        // Preserve HandleSleepTime's immediate transition for a completed sleep.
        return sleepExecutor == null || !sleepExecutor.IsCompleted;
    }

    private void MaybeLogStartupMovementDiagnostic()
    {
        if (!logStartupMovementDiagnostics ||
            activeControllers.Count <= 1 ||
            startupDiagnosticStartedAtRealtime < 0f ||
            Time.realtimeSinceStartup < nextStartupDiagnosticRealtime)
        {
            return;
        }

        if (Time.realtimeSinceStartup - startupDiagnosticStartedAtRealtime > StartupDiagnosticDurationSeconds)
            return;

        nextStartupDiagnosticRealtime = Time.realtimeSinceStartup + StartupDiagnosticIntervalSeconds;
        LogStartupMovementDiagnostic();
    }

    private static void LogStartupMovementDiagnostic()
    {
        int total = 0;
        int enabledControllers = 0;
        int autonomous = 0;
        int playerControlled = 0;
        int idleRunning = 0;
        int taskActive = 0;
        int returningToBarn = 0;
        int atBarn = 0;
        int noBarn = 0;
        int tempBarn = 0;
        int pathPending = 0;
        int hasPath = 0;
        int noPath = 0;
        int invalidPath = 0;
        int partialPath = 0;
        int offNavMesh = 0;
        int speedZero = 0;
        int velocityMoving = 0;
        int startupDiagnosticsEnabled = 0;
        int pausedWorldTime = 0;
        int runningWorldTime = 0;
        int barnRouteCooldown = 0;
        int barnRouteStarted = 0;
        int barnRouteInteractionFailed = 0;
        int barnRouteMoveFailed = 0;
        int barnRoutePathFailed = 0;
        int barnRouteNoActiveBarn = 0;
        int barnRouteAtBarn = 0;
        StringBuilder samples = new StringBuilder();

        for (int i = activeControllers.Count - 1; i >= 0; i--)
        {
            NoAgentManualController controller = activeControllers[i];
            if (controller == null)
            {
                activeControllers.RemoveAt(i);
                continue;
            }

            total++;
            if (controller.isActiveAndEnabled)
                enabledControllers++;

            if (controller.logStartupMovementDiagnostics)
                startupDiagnosticsEnabled++;

            CharacterControlMode controlMode = CharacterControlRegistry.GetControlMode(controller.character);
            if (controlMode == CharacterControlMode.Autonomous)
                autonomous++;
            else if (controlMode == CharacterControlMode.Player)
                playerControlled++;

            if (controller.taskController != null && controller.taskController.IsTaskActive)
                taskActive++;

            if (controller.taskController != null &&
                controller.taskController.CurrentTask == AgentTaskType.Idle &&
                controller.taskController.TaskState == AgentTaskState.Running &&
                !controller.isMovingToStartupBarn)
            {
                idleRunning++;
            }

            if (controller.isMovingToStartupBarn)
                returningToBarn++;

            if (controller.IsAtNearestBarn())
                atBarn++;

            ResourceStorageLookupResult lookupResult = ResourceStorageRegistry.GetClosestStorage(
                controller.transform.position,
                controller.GetRequesterId(),
                out _);

            if (lookupResult != ResourceStorageLookupResult.Found)
            {
                noBarn++;

                if (lookupResult == ResourceStorageLookupResult.TemporarilyUnreachable)
                    tempBarn++;
            }

            NavMeshAgent agent = controller.GetComponent<NavMeshAgent>();
            string pathStatus = "NoAgent";
            bool isOnNavMesh = false;
            if (agent != null)
            {
                isOnNavMesh = agent.isOnNavMesh;
                if (!agent.isOnNavMesh)
                    offNavMesh++;

                if (agent.speed <= 0.001f)
                    speedZero++;

                if (agent.velocity.sqrMagnitude > 0.0001f)
                    velocityMoving++;

                if (agent.pathPending)
                    pathPending++;

                if (agent.hasPath)
                    hasPath++;
                else
                    noPath++;

                if (agent.pathStatus == NavMeshPathStatus.PathInvalid)
                    invalidPath++;
                else if (agent.pathStatus == NavMeshPathStatus.PathPartial)
                    partialPath++;

                pathStatus = agent.pathPending
                    ? "Pending"
                    : agent.hasPath
                        ? agent.pathStatus.ToString()
                        : "NoPath";
            }
            else
            {
                offNavMesh++;
                noPath++;
            }

            if (controller.worldTimeManager != null)
            {
                if (controller.worldTimeManager.IsPaused)
                    pausedWorldTime++;
                else
                    runningWorldTime++;
            }

            switch (controller.lastStartupBarnRouteState)
            {
                case "ManualRetryCooldown":
                    barnRouteCooldown++;
                    break;
                case "Started":
                    barnRouteStarted++;
                    break;
                case "InteractionPositionFailed":
                case "RestartInteractionPositionFailed":
                    barnRouteInteractionFailed++;
                    break;
                case "MoveStartFailed":
                case "RestartMoveStartFailed":
                    barnRouteMoveFailed++;
                    break;
                case "PathFailed":
                    barnRoutePathFailed++;
                    break;
                case "NoActiveBarn":
                    barnRouteNoActiveBarn++;
                    break;
                case "AtBarn":
                    barnRouteAtBarn++;
                    break;
            }

            bool shouldSample =
                samples.Length <= 0 ||
                (!isOnNavMesh ||
                    controlMode != CharacterControlMode.Autonomous ||
                    controller.isMovingToStartupBarn && (agent == null || !agent.hasPath && !agent.pathPending) ||
                    controller.taskController == null ||
                    controller.taskController.CurrentTask == AgentTaskType.Idle && !controller.isMovingToStartupBarn);

            if (shouldSample && CountSampleSeparators(samples) < StartupDiagnosticSampleLimit)
            {
                if (samples.Length > 0)
                    samples.Append(" || ");

                string taskText = controller.taskController != null
                    ? $"{controller.taskController.CurrentTask}/{controller.taskController.TaskState}"
                    : "MissingTaskController";

                string speedText = agent != null ? agent.speed.ToString("0.###") : "n/a";
                string remainingText = agent != null ? agent.remainingDistance.ToString("0.###") : "n/a";

                samples.Append(controller.gameObject.name)
                    .Append(" @ ").Append(controller.transform.position.ToString("F2"))
                    .Append(": control=").Append(controlMode)
                    .Append(", task=").Append(taskText)
                    .Append(", returning=").Append(controller.isMovingToStartupBarn)
                    .Append(", barnState=").Append(controller.lastStartupBarnRouteState)
                    .Append(", reason=").Append(controller.GetLastManualDayPlanFailureReason())
                    .Append(", atBarn=").Append(controller.IsAtNearestBarn())
                    .Append(", interaction=").Append(controller.lastStartupBarnInteractionDiagnostics.ToSummary())
                    .Append(", path=").Append(pathStatus)
                    .Append(", speed=").Append(speedText)
                    .Append(", remaining=").Append(remainingText)
                    .Append(", nav=").Append(BuildBarnNavMeshDiagnostic(controller, agent));
            }
        }

        Debug.Log(
            "NoAgentStartupMovementDiagnostic | " +
            $"total={total}, enabled={enabledControllers}, diagEnabled={startupDiagnosticsEnabled}, " +
            $"autonomous={autonomous}, player={playerControlled}, idleRunning={idleRunning}, taskActive={taskActive}, " +
            $"returningToBarn={returningToBarn}, atBarn={atBarn}, noBarn={noBarn}, tempBarn={tempBarn}, " +
            $"pathPending={pathPending}, hasPath={hasPath}, noPath={noPath}, invalidPath={invalidPath}, partialPath={partialPath}, " +
            $"offNavMesh={offNavMesh}, speedZero={speedZero}, velocityMoving={velocityMoving}, " +
            $"worldPaused={pausedWorldTime}, worldRunning={runningWorldTime}, " +
            $"barnRouteCooldown={barnRouteCooldown}, barnRouteStarted={barnRouteStarted}, " +
            $"barnRouteInteractionFailed={barnRouteInteractionFailed}, barnRouteMoveFailed={barnRouteMoveFailed}, " +
            $"barnRoutePathFailed={barnRoutePathFailed}, " +
            $"barnRouteNoActiveBarn={barnRouteNoActiveBarn}, barnRouteAtBarn={barnRouteAtBarn}, " +
            $"navMeshSurfaces={NavMeshSurface.activeSurfaces.Count}, " +
            $"storages={ResourceStorageRegistry.RegisteredStorageCount}, " +
            $"assignedRoles={NpcWorkRoleAssignmentRegistry.GetTotalAssignedCount()}, " +
            $"quotas=F{ManualDayPlanState.GetQuota(NpcWorkRole.Food)}/W{ManualDayPlanState.GetQuota(NpcWorkRole.Wood)}/E{ManualDayPlanState.GetQuota(NpcWorkRole.Explore)}/B{ManualDayPlanState.GetQuota(NpcWorkRole.Build)}, " +
            $"loopFlags=F{ManualDayPlanState.HasLoopFlag(NpcWorkRole.Food)}/W{ManualDayPlanState.HasLoopFlag(NpcWorkRole.Wood)}/E{ManualDayPlanState.HasLoopFlag(NpcWorkRole.Explore)}/B{ManualDayPlanState.HasLoopFlag(NpcWorkRole.Build)}, " +
            $"samples=[{samples}]"
        );
    }

    private static int CountSampleSeparators(StringBuilder samples)
    {
        if (samples == null || samples.Length <= 0)
            return 0;

        int count = 1;
        for (int i = 0; i < samples.Length - 3; i++)
        {
            if (samples[i] == ' ' &&
                samples[i + 1] == '|' &&
                samples[i + 2] == '|' &&
                samples[i + 3] == ' ')
            {
                count++;
            }
        }

        return count;
    }

    private static string BuildBarnNavMeshDiagnostic(
        NoAgentManualController controller,
        NavMeshAgent agent)
    {
        if (controller == null)
            return "controller=null";

        ResourceStorageLookupResult lookupResult = ResourceStorageRegistry.GetClosestStorage(
            controller.transform.position,
            controller.GetRequesterId(),
            out ResourceStorage barn);

        if (lookupResult != ResourceStorageLookupResult.Found || barn == null)
            return $"barnLookup={lookupResult}";

        Vector3 npcPosition = controller.transform.position;
        Vector3 depositPosition = barn.GetDepositPosition();
        int areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas;

        bool npcSampled = NavMesh.SamplePosition(
            npcPosition,
            out NavMeshHit npcHit,
            2f,
            areaMask);

        bool depositSampled = NavMesh.SamplePosition(
            depositPosition,
            out NavMeshHit depositHit,
            Mathf.Max(0.5f, barn.InteractionRadius),
            areaMask);

        string agentText = agent != null
            ? $"agentType={agent.agentTypeID}/mask={agent.areaMask}/radius={agent.radius:0.###}/height={agent.height:0.###}/baseOffset={agent.baseOffset:0.###}/stopping={agent.stoppingDistance:0.###}"
            : "agent=null";

        if (!npcSampled || !depositSampled)
        {
            return
                $"{agentText}/npcSample={npcSampled}/depositSample={depositSampled}" +
                $"/deposit={depositPosition.ToString("F2")}";
        }

        NavMeshPath directPath = new NavMeshPath();
        bool calculated = NavMesh.CalculatePath(
            npcHit.position,
            depositHit.position,
            areaMask,
            directPath);

        Vector3 pathEnd = npcHit.position;
        if (directPath.corners != null && directPath.corners.Length > 0)
            pathEnd = directPath.corners[directPath.corners.Length - 1];

        float sampleOffset = Vector3.Distance(depositPosition, depositHit.position);
        float remainingToDeposit = Vector3.Distance(pathEnd, depositHit.position);
        float flatDistance = Vector2.Distance(
            new Vector2(npcHit.position.x, npcHit.position.z),
            new Vector2(depositHit.position.x, depositHit.position.z));

        return
            $"{agentText}/npcHit={npcHit.position.ToString("F2")}" +
            $"/deposit={depositPosition.ToString("F2")}/depositHit={depositHit.position.ToString("F2")}" +
            $"/depositSampleOffset={sampleOffset:0.###}/calc={calculated}/status={directPath.status}" +
            $"/corners={directPath.corners.Length}/pathEnd={pathEnd.ToString("F2")}" +
            $"/endToDeposit={remainingToDeposit:0.###}/flatDistance={flatDistance:0.###}";
    }

    private bool HandleSleepTime(AgentContext context)
    {
        if (!IsSleepTime())
            return false;

        if (context == null)
            return false;

        if (!context.HasHome && !context.IsSleeping)
            SimulationPerformanceCounters.AddVisibleHomelessNpcDuringSleep();

        if (context.SleepState == SleepExecutionState.Completed)
        {
            EnsureIdleTask();
            return true;
        }

        if (IsCurrentTaskActive(context, AgentTaskType.Sleep) ||
            IsCurrentTaskActive(context, AgentTaskType.GoHome))
        {
            return true;
        }

        if (taskController.IsTaskActive)
            taskController.CancelCurrentTask();

        shouldReturnToBarnAfterWork = false;
        shouldRequestWorkFromCurrentPosition = false;
        wasTaskActiveLastFrame = false;

        if (context.IsAtOvernightDestination)
        {
            if (!taskController.TryStartSleepTask())
                EnsureIdleTask();

            return true;
        }

        if (!taskController.TryStartGoHomeTask())
            EnsureIdleTask();

        return true;
    }

    private void TickWorkDecision(AgentContext context)
    {
        if (context == null)
            return;

        if (shouldRequestWorkFromCurrentPosition && IsWorkTime() && !context.HasDepositLoad)
        {
            shouldRequestWorkFromCurrentPosition = false;
            shouldReturnToBarnAfterWork = false;

            if (taskController.TryStartManualDayPlanWorkTask(true, false))
            {
                shouldReturnToBarnAfterWork = true;
                noRunnableWorkWarningLogged = false;
                return;
            }
        }

        if (shouldReturnToBarnAfterWork || context.HasDepositLoad)
        {
            shouldReturnToBarnAfterWork = false;

            if (context.HasDepositLoad)
            {
                if (taskController.TryStartDepositResourcesTask())
                    return;

                Debug.LogWarning($"NoAgent could not start return-to-barn deposit task. GameObject: {gameObject.name}.");
                EnsureIdleTask();
                return;
            }
        }

        if (!EnsureAtNearestBarnBeforeWork())
        {
            MaybeLogExploreIdleDiagnostic("moving to nearest barn before requesting work", false);
            return;
        }

        if (taskController.TryStartManualDayPlanWorkTask(true, false))
        {
            shouldReturnToBarnAfterWork = true;
            noRunnableWorkWarningLogged = false;
            return;
        }

        if (!noRunnableWorkWarningLogged)
        {
            Debug.LogWarning($"NoAgent found no runnable manual barn-plan work. GameObject: {gameObject.name}.");
            noRunnableWorkWarningLogged = true;
        }

        MaybeLogExploreIdleDiagnostic($"manual day plan did not start: {GetLastManualDayPlanFailureReason()}", false);
        EnsureIdleTask();
    }

    private bool EnsureAtNearestBarnBeforeWork()
    {
        int requesterId = GetRequesterId();
        ResourceStorageLookupResult lookupResult = ResourceStorageRegistry.GetClosestStorage(
            transform.position,
            requesterId,
            out ResourceStorage barn
        );

        if (lookupResult == ResourceStorageLookupResult.TemporarilyUnreachable)
        {
            lastStartupBarnRouteState = "TemporaryBarnCooldown";
            missingStartupBarnWarningLogged = false;
            ClearBarnMovementState(false);
            EnsureIdleTask();
            return false;
        }

        if (lookupResult == ResourceStorageLookupResult.NoActiveStorage)
        {
            lastStartupBarnRouteState = "NoActiveBarn";

            if (!missingStartupBarnWarningLogged)
            {
                Debug.LogWarning($"NoAgent could not find an active barn before requesting work. GameObject: {gameObject.name}.");
                missingStartupBarnWarningLogged = true;
            }

            ClearBarnMovementState(false);
            EnsureIdleTask();
            return false;
        }

        missingStartupBarnWarningLogged = false;

        if (barn.IsInInteractionRange(transform.position))
        {
            lastStartupBarnRouteState = "AtBarn";
            ClearBarnMovementState();
            return true;
        }

        startupBarn = barn;
        startupBarnInteractionPosition = GetBarnTravelTarget(startupBarn);

        if (!character.TryMoveTo(startupBarnInteractionPosition))
        {
            lastStartupBarnRouteState = "MoveStartFailed";

            if (!barnPathWarningLogged)
            {
                Debug.LogWarning($"NoAgent could not start movement to the nearest barn. GameObject: {gameObject.name}.");
                barnPathWarningLogged = true;
            }

            ClearBarnMovementState(false);
            EnsureIdleTask();
            return false;
        }

        isMovingToStartupBarn = true;
        lastStartupBarnRouteState = "Started";
        BeginBarnPathResolution();

        EnsureBarnTravelTaskState();
        return false;
    }

    private bool UpdateBarnTravel()
    {
        if (startupBarn == null || !startupBarn.isActiveAndEnabled)
        {
            lastStartupBarnRouteState = "BarnBecameInvalid";
            character.StopMovement();
            ClearBarnMovementState(false);
            EnsureIdleTask();
            return true;
        }

        if (startupBarn.IsInInteractionRange(transform.position))
        {
            lastStartupBarnRouteState = "AtBarn";
            character.StopMovement();
            ClearBarnMovementState();
            return true;
        }

        if (IsBarnPathStillResolving())
        {
            lastStartupBarnRouteState = "PathResolving";
            EnsureBarnTravelTaskState();
            return false;
        }

        if (character.HasReachedInteractionTarget(startupBarnInteractionPosition))
        {
            lastStartupBarnRouteState = "ReachedBarnTargetOutsideRange";
            character.StopMovement();
            ClearBarnMovementState(false);
            EnsureIdleTask();
            return true;
        }

        if (HasBarnMovementInvalidPath())
        {
            FailBarnReturnPath("movement path is invalid");
            return true;
        }

        if (HasBarnReturnStalled())
        {
            lastStartupBarnRouteState = "MovementStalled";
            character.StopMovement();
            ClearBarnMovementState(false);
            EnsureIdleTask();
            return true;
        }

        EnsureBarnTravelTaskState();
        return false;
    }

    private void ClearBarnMovementState(bool clearWarnings = true)
    {
        startupBarn = null;
        startupBarnInteractionPosition = Vector3.zero;
        isMovingToStartupBarn = false;
        lastBarnReturnPosition = transform.position;
        barnReturnNoProgressWorldHours = 0f;
        barnPathValidationAllowedFrame = int.MinValue;

        if (clearWarnings)
            barnPathWarningLogged = false;
    }

    private void BeginBarnPathResolution()
    {
        barnPathValidationAllowedFrame = Time.frameCount + BarnPathResolutionGraceFrames;
        ResetBarnReturnProgress();
    }

    private Vector3 GetBarnTravelTarget(ResourceStorage barn)
    {
        if (barn == null)
            return transform.position;

        lastStartupBarnInteractionDiagnostics = default;
        Vector3 depositPosition = barn.GetDepositPosition();
        if (NavMesh.SamplePosition(
            depositPosition,
            out NavMeshHit hit,
            0.5f,
            NavMesh.AllAreas))
        {
            lastStartupBarnRouteState = "UsingDepositPoint";
            return hit.position;
        }

        lastStartupBarnRouteState = "UsingDepositFallback";
        return depositPosition;
    }

    private bool IsBarnPathStillResolving()
    {
        return character != null && Time.frameCount <= barnPathValidationAllowedFrame;
    }

    private bool HasBarnMovementInvalidPath()
    {
        if (navigationAgent == null)
            navigationAgent = GetComponent<NavMeshAgent>();

        return navigationAgent != null &&
            navigationAgent.isOnNavMesh &&
            !navigationAgent.pathPending &&
            navigationAgent.pathStatus == NavMeshPathStatus.PathInvalid;
    }

    private void FailBarnReturnPath(string failureReason)
    {
        lastStartupBarnRouteState = "PathFailed";

        if (!barnPathWarningLogged)
        {
            Debug.LogWarning($"NoAgent could not reach the nearest barn because the {failureReason}. GameObject: {gameObject.name}.");
            barnPathWarningLogged = true;
        }

        character.StopMovement();
        ClearBarnMovementState(false);
        EnsureIdleTask();
    }

    private void ResetBarnReturnProgress()
    {
        lastBarnReturnPosition = transform.position;
        barnReturnNoProgressWorldHours = 0f;
    }

    private bool HasBarnReturnStalled()
    {
        float progressDistanceSquared = BarnReturnProgressDistance * BarnReturnProgressDistance;
        if ((transform.position - lastBarnReturnPosition).sqrMagnitude > progressDistanceSquared)
        {
            ResetBarnReturnProgress();
            return false;
        }

        ResolveWorldTimeManager();
        if (worldTimeManager == null)
            return false;

        barnReturnNoProgressWorldHours += Mathf.Max(0f, worldTimeManager.DeltaWorldHours);
        return barnReturnNoProgressWorldHours >= BarnReturnStuckWorldHours;
    }

    private void EnsureIdleTask()
    {
        if (taskController == null)
            return;

        if (taskController.CurrentTask == AgentTaskType.Idle &&
            taskController.TaskState == AgentTaskState.Running)
        {
            return;
        }

        taskController.SetIdle();
    }

    private void EnsureBarnTravelTaskState()
    {
        if (taskController == null || taskController.IsTaskActive)
            return;

        if (taskController.CurrentTask == AgentTaskType.None &&
            taskController.TaskState == AgentTaskState.Completed)
        {
            return;
        }

        taskController.TryStartTask(AgentTaskType.None);
    }

    private bool IsCurrentTaskActive(AgentContext context, AgentTaskType taskType)
    {
        return context != null &&
            context.CurrentTask == taskType &&
            (context.TaskState == AgentTaskState.Start || context.TaskState == AgentTaskState.Running);
    }

    private bool IsWorkTime()
    {
        if (worldTimeManager == null)
            return true;

        return workProfile == null || workProfile.IsWorkTime(worldTimeManager.CurrentHour);
    }

    private bool IsSleepTime()
    {
        if (worldTimeManager == null)
            return false;

        return workProfile != null && workProfile.IsSleepTime(worldTimeManager.CurrentHour);
    }

    private bool ShouldRequestWorkFromCurrentPositionAfterTask(AgentContext context)
    {
        return context != null &&
            lastActiveTaskType == AgentTaskType.GatherResource &&
            !context.HasDepositLoad &&
            taskController != null &&
            !taskController.IsTaskActive &&
            (taskController.TaskState == AgentTaskState.Completed ||
                taskController.TaskState == AgentTaskState.Failed);
    }

    private void MaybeLogExploreIdleDiagnostic(string reason, bool waitingForNextDecision)
    {
        if (!ShouldLogExploreIdleDiagnostic())
            return;

        if (taskController != null &&
            taskController.CurrentTask == AgentTaskType.ExploreFog &&
            taskController.IsTaskActive)
        {
            return;
        }

        float interval = ExploreIdleDiagnosticIntervalSeconds;
        if (Time.time < nextExploreIdleDiagnosticTime)
            return;

        nextExploreIdleDiagnosticTime = Time.time + interval;
        SimulationPerformanceCounters.AddDiagnosticLogAttempt();

        CharacterControlMode controlMode = CharacterControlRegistry.GetControlMode(character);
        bool isPlayerControlled = controlMode == CharacterControlMode.Player;
        bool hasAutonomousControl = controlMode == CharacterControlMode.Autonomous;
        bool hasRegistryRole = NpcWorkRoleAssignmentRegistry.TryGetAssignedRole(identity, out NpcWorkRole registryRole);
        bool hasTaskRole = taskController != null && taskController.HasAssignedWorkRole;
        NpcWorkRole currentRole = hasTaskRole ? taskController.AssignedWorkRole : registryRole;
        string assignedRoleText = hasTaskRole || hasRegistryRole ? currentRole.ToString() : "None";
        string taskText = taskController != null ? $"{taskController.CurrentTask}/{taskController.TaskState}" : "None/Completed";
        string exploreStateText = exploreFogExecutor != null ? exploreFogExecutor.State.ToString() : "MissingExecutor";
        bool atBarn = IsAtNearestBarn();
        bool returningToBarn = isMovingToStartupBarn;

        FogObject activeFog = exploreFogExecutor != null ? exploreFogExecutor.ActiveFog : null;
        bool hasExploreTarget = activeFog != null;
        bool targetFogRevealed = activeFog != null && activeFog.IsRevealed;
        string activeFogName = activeFog != null ? activeFog.gameObject.name : "None";
        string lastReason = GetLastManualDayPlanFailureReason();

        int availableFrontierFog = FogRegistry.CountReachableFrontierFog();
        int reachableFrontierFog = FogRegistry.CountPathReachableFrontierFog(transform.position, false);

        Debug.Log(
            $"ExploreIdleDiagnostic | NPC={gameObject.name} | Role={assignedRoleText} | Task={taskText} | " +
            $"ExploreState={exploreStateText} | AutoControl={hasAutonomousControl} | PlayerControlled={isPlayerControlled} | " +
            $"Schedule={GetSchedulePhase()} | AtBarn={atBarn} | WaitingDecision={waitingForNextDecision} | " +
            $"ReturningToBarn={returningToBarn} | HasExploreTarget={hasExploreTarget} | ActiveFog={activeFogName} | " +
            $"TargetRevealed={targetFogRevealed} | FrontierAvailable={availableFrontierFog} | " +
            $"ReachableFrontier={reachableFrontierFog} | " +
            $"TryStartReason={lastReason} | IdleReason={reason}"
        );
    }

    private bool ShouldLogExploreIdleDiagnostic()
    {
        if (!logExploreIdleDiagnostics || taskController == null)
            return false;

        if (taskController.HasAssignedWorkRole && taskController.AssignedWorkRole == NpcWorkRole.Explore)
            return true;

        if (NpcWorkRoleAssignmentRegistry.TryGetAssignedRole(identity, out NpcWorkRole registryRole) &&
            registryRole == NpcWorkRole.Explore)
        {
            return true;
        }

        return ManualDayPlanState.GetQuota(NpcWorkRole.Explore) > 0;
    }

    private string GetLastManualDayPlanFailureReason()
    {
        if (taskController == null || string.IsNullOrWhiteSpace(taskController.LastManualDayPlanFailureReason))
            return "None";

        return taskController.LastManualDayPlanFailureReason;
    }

    private string GetSchedulePhase()
    {
        if (IsSleepTime())
            return "Sleep";

        if (IsWorkTime())
            return "Work";

        return "OffHours";
    }

    private bool IsAtNearestBarn()
    {
        if (!ResourceStorageRegistry.TryGetClosestStorageIgnoringTemporaryUnreachable(
            transform.position,
            GetRequesterId(),
            out ResourceStorage barn))
        {
            return false;
        }

        return barn != null && barn.IsInInteractionRange(transform.position);
    }

    private int GetRequesterId()
    {
        return identity != null ? identity.CharacterId : 0;
    }

    private void StopForPlayerControl()
    {
        if (taskController != null && taskController.IsTaskActive)
            taskController.CancelCurrentTask();

        shouldReturnToBarnAfterWork = false;
        shouldRequestWorkFromCurrentPosition = false;
        noRunnableWorkWarningLogged = false;
        wasTaskActiveLastFrame = false;
        lastActiveTaskType = AgentTaskType.None;
        ClearBarnMovementState();
        CharacterControlRegistry.ReleaseAutonomousControl(character);
    }

    private void ResolveWorldTimeManager()
    {
        if (worldTimeManager != null)
            return;

        worldTimeManager = FindFirstObjectByType<WorldTimeManager>();

        if (worldTimeManager == null && !missingWorldTimeManagerWarningLogged)
        {
            Debug.LogWarning($"NoAgentManualController is using work-time behavior because no WorldTimeManager is available. GameObject: {gameObject.name}.");
            missingWorldTimeManagerWarningLogged = true;
        }
    }

    private void ConfigurePerformanceCounters()
    {
        if (!enablePerformanceCounters)
            return;

        SimulationPerformanceCounters.Configure(true, logPerformanceCounterSummaries);
    }
}
