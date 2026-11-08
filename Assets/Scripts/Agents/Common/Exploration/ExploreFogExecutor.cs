/*
===============================================================================
Script:
ExploreFogExecutor

Purpose:
Executes shared tile-fog exploration by selecting, moving to, working, and revealing a FogObject.

Folder:
Assets/Scripts/Agents/Common/Exploration/

Scene Hierarchy:
_World
- Characters
  - Character

Attach To:
NPC character root

Required Children:
None

Dependencies:
- Character
- FogObject
- WorldTimeManager
- AgentWorkProfile

Automatically Resolved:
- Required components on the same GameObject
- WorldTimeManager in the scene

Inspector References:
- requiredExploreWorldHours
- movementStuckWorldHours
- logExploreTravelDiagnostics
===============================================================================
*/

using System.Text;
using UnityEngine;
using UnityEngine.AI;

public enum ExploreFogState
{
    Idle,
    SelectingFog,
    Moving,
    Exploring,
    Completed,
    Failed
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
public class ExploreFogExecutor : MonoBehaviour
{
    private const float DefaultRequiredExploreWorldHours = 1f;
    private const float DefaultMovementStuckWorldHours = 0.5f;
    private const float MovementProgressDistance = 0.1f;

    [SerializeField] private float requiredExploreWorldHours = DefaultRequiredExploreWorldHours;
    [SerializeField] private float movementStuckWorldHours = DefaultMovementStuckWorldHours;
    [SerializeField] private bool logExploreTravelDiagnostics;

    private Character character;
    private CharacterIdentity identity;
    private NpcTaskController taskController;
    private AgentWorkProfile workProfile;
    private WorldTimeManager worldTimeManager;
    private NavMeshAgent navigationAgent;
    private FogObject activeFog;
    private Vector3 activeInteractionPosition;
    private Vector3 lastMovingPosition;
    private Vector3 initialDirectionOrigin;
    private float movementNoProgressWorldHours;
    private bool repeatUntilCancelled;
    private bool hasInitialDirection;
    private bool initialDirectionPending;
    private bool pathResolutionLogged;
    private ExploreDirection initialDirection = ExploreDirection.North;

    public ExploreFogState State { get; private set; } = ExploreFogState.Idle;
    public FogObject ActiveFog => activeFog;
    public bool IsRunning =>
        State == ExploreFogState.SelectingFog ||
        State == ExploreFogState.Moving ||
        State == ExploreFogState.Exploring;
    public bool IsCompleted => State == ExploreFogState.Completed;
    public int RevealedTargetCount { get; private set; }
    public bool HasFailed => State == ExploreFogState.Failed;

    private void Awake()
    {
        character = GetComponent<Character>();
        identity = GetComponent<CharacterIdentity>();
        taskController = GetComponent<NpcTaskController>();
        workProfile = GetComponent<AgentWorkProfile>();
        navigationAgent = GetComponent<NavMeshAgent>();
        ResolveWorldTimeManager();
    }

    private void OnDisable()
    {
        if (IsRunning)
            CancelExploration();
    }

    private void OnValidate()
    {
        requiredExploreWorldHours = Mathf.Max(0f, requiredExploreWorldHours);
        movementStuckWorldHours = Mathf.Max(0f, movementStuckWorldHours);
    }

    private void Update()
    {
        switch (State)
        {
            case ExploreFogState.Moving:
                UpdateMoving();
                break;
            case ExploreFogState.Exploring:
                UpdateExploring();
                break;
        }
    }

    public bool StartExploration(bool repeat)
    {
        return StartExploration(repeat, false, ExploreDirection.North);
    }

    public bool StartExploration(bool repeat, bool useInitialDirection, ExploreDirection assignedDirection)
    {
        if (IsRunning)
            return false;

        repeatUntilCancelled = repeat;
        hasInitialDirection = useInitialDirection;
        initialDirectionPending = useInitialDirection;
        initialDirection = assignedDirection;
        initialDirectionOrigin = transform.position;
        LogExploreTravelDiagnostic(
            "TaskStarted",
            null,
            transform.position,
            $"Repeat={repeat}; UseInitialDirection={useInitialDirection}; AssignedDirection={assignedDirection}."
        );
        return StartNextExploreStep(true);
    }

    public void CancelExploration()
    {
        LogExploreTravelDiagnostic(
            "TaskCancelled",
            activeFog,
            activeInteractionPosition,
            "Exploration was cancelled."
        );

        if (character != null)
            character.StopMovement();

        ClearInitialDirection();
        ClearActiveFog();
        State = ExploreFogState.Idle;
    }

    private bool StartNextExploreStep(bool failIfNoTarget)
    {
        State = ExploreFogState.SelectingFog;
        ClearActiveFog();

        if (!TrySelectNextFog(out activeFog, out activeInteractionPosition))
        {
            SimulationPerformanceCounters.AddExploreNoTargetWait();
            LogExploreTravelDiagnostic(
                "NoTargetAvailable",
                null,
                transform.position,
                $"FailIfNoTarget={failIfNoTarget}; Repeat={repeatUntilCancelled}."
            );

            if (failIfNoTarget)
            {
                SetExploreLoopFlagBecauseNoReachableTarget();
                Fail("ExploreFog failed: no available reachable fog target could be found.");
            }

            return false;
        }

        LogExploreTravelDiagnostic(
            "TargetSelected",
            activeFog,
            activeInteractionPosition,
            "A reachable fog target and interaction position were selected."
        );
        LogExploreTravelDiagnostic(
            "RouteRequested",
            activeFog,
            activeInteractionPosition,
            "Requesting Character movement to the selected interaction position."
        );

        if (character == null || !character.TryMoveTo(activeInteractionPosition))
        {
            LogExploreTravelDiagnostic(
                "MovementPathFailed",
                activeFog,
                activeInteractionPosition,
                "Character.TryMoveTo returned false."
            );
            return HandleMovementFailure("ExploreFog failed: movement could not start.");
        }

        ResetMovementProgress();
        pathResolutionLogged = false;
        State = ExploreFogState.Moving;
        return true;
    }

    private void UpdateMoving()
    {
        if (ShouldStopExploring())
            return;

        if (!ValidateActiveFog())
            return;

        if (character == null)
        {
            Fail("ExploreFog failed: Character component is missing.");
            return;
        }

        if (character.IsMovementPathPending())
            return;

        TryLogResolvedPath();

        // Character's generic interaction tolerance is measured around the sampled
        // destination. For fog this can report arrival while the NPC is still outside
        // the tile's reveal radius. Start work from the authoritative fog range instead.
        if (activeFog.IsWithinRevealRange(transform.position))
        {
            BeginRevealWork();
            return;
        }

        if (character.HasMovementPathFailed())
        {
            LogExploreTravelDiagnostic(
                "MovementPathFailed",
                activeFog,
                activeInteractionPosition,
                "Character reported an invalid or partial movement path."
            );
            HandleMovementFailure("ExploreFog failed: movement path is invalid or partial.");
            return;
        }

        if (HasMovementStalled())
        {
            LogExploreTravelDiagnostic(
                "MovementStalled",
                activeFog,
                activeInteractionPosition,
                "Movement made no progress until the existing Explore stall threshold was reached."
            );
            HandleMovementFailure("ExploreFog failed: movement stalled before reaching the fog target.");
        }
    }

    private void UpdateExploring()
    {
        if (ShouldStopExploring())
            return;

        if (!ValidateActiveFog())
            return;

        if (!activeFog.IsWithinRevealRange(transform.position))
        {
            activeFog.EndRevealWork(identity);
            if (character == null || !character.TryMoveTo(activeInteractionPosition))
            {
                HandleMovementFailure("ExploreFog failed: return to reveal range could not start.");
                return;
            }

            ResetMovementProgress();
            pathResolutionLogged = false;
            State = ExploreFogState.Moving;
            LogExploreTravelDiagnostic(
                "RevealRangeRecoveryStarted",
                activeFog,
                activeInteractionPosition,
                "NPC left reveal range and is returning to its reserved interaction position."
            );
            return;
        }

        ResolveWorldTimeManager();
        if (worldTimeManager == null)
        {
            Fail("ExploreFog failed: WorldTimeManager is missing.");
            return;
        }

        bool revealed = activeFog.AdvanceRevealProgress(
            identity,
            Mathf.Max(0f, worldTimeManager.DeltaWorldHours),
            Mathf.Max(0f, requiredExploreWorldHours)
        );

        if (!revealed)
            return;

        RevealedTargetCount++;

        LogExploreTravelDiagnostic(
            "TargetRevealed",
            activeFog,
            activeInteractionPosition,
            "Fog reveal work completed."
        );
        CompleteOrRepeat();
    }

    private void BeginRevealWork()
    {
        LogExploreTravelDiagnostic(
            "TargetReached",
            activeFog,
            activeInteractionPosition,
            "Character entered the fog tile's reveal range."
        );
        character.StopMovement();
        if (!activeFog.BeginRevealWork(identity))
        {
            Fail("ExploreFog failed: reveal work could not begin on the reserved target.");
            return;
        }

        State = ExploreFogState.Exploring;
        LogExploreTravelDiagnostic(
            "RevealStarted",
            activeFog,
            activeInteractionPosition,
            "Fog reveal work started."
        );
    }

    private bool ValidateActiveFog()
    {
        if (activeFog == null ||
            !activeFog.isActiveAndEnabled)
        {
            LogExploreTravelDiagnostic(
                "TargetInvalidated",
                activeFog,
                activeInteractionPosition,
                "The active fog reference is missing or inactive."
            );
            Fail("ExploreFog failed: target fog is no longer valid.");
            return false;
        }

        if (activeFog.IsRevealed)
        {
            SimulationPerformanceCounters.AddExploreTargetAlreadyRevealed();
            LogExploreTravelDiagnostic(
                "TargetInvalidated",
                activeFog,
                activeInteractionPosition,
                "The selected fog target was already revealed before this executor completed it."
            );
            CompleteOrRepeat();
            return false;
        }

        return true;
    }

    private void CompleteOrRepeat()
    {
        ClearActiveFog();

        if (!repeatUntilCancelled || ShouldCompleteForStopCondition())
        {
            ClearInitialDirection();
            State = ExploreFogState.Completed;
            LogExploreTravelDiagnostic(
                "TaskCompleted",
                null,
                transform.position,
                "Exploration completed without requesting another target."
            );
            return;
        }

        if (StartNextExploreStep(false))
            return;

        SetExploreLoopFlagBecauseNoReachableTarget();
        ClearInitialDirection();
        State = ExploreFogState.Completed;
        LogExploreTravelDiagnostic(
            "TaskCompleted",
            null,
            transform.position,
            "Exploration completed because no additional reachable target was available."
        );
    }

    private bool ShouldStopExploring()
    {
        if (!ShouldCompleteForStopCondition())
            return false;

        if (character != null)
            character.StopMovement();

        ClearInitialDirection();
        ClearActiveFog();
        State = ExploreFogState.Completed;
        LogExploreTravelDiagnostic(
            "TaskCompleted",
            null,
            transform.position,
            "Exploration completed because the work window or Explore loop ended."
        );
        return true;
    }

    private bool ShouldCompleteForStopCondition()
    {
        return !IsWorkTime();
    }

    private bool IsWorkTime()
    {
        ResolveWorldTimeManager();

        if (worldTimeManager == null)
            return true;

        if (workProfile != null)
            return workProfile.IsWorkTime(worldTimeManager.CurrentHour);

        AgentScheduleConfig schedule = new AgentScheduleConfig();
        return schedule.IsWorkTime(worldTimeManager.CurrentHour);
    }

    private void Fail(string message)
    {
        SimulationPerformanceCounters.AddExploreExecutionFailure();
        LogExploreTravelDiagnostic(
            "TaskFailed",
            activeFog,
            activeInteractionPosition,
            message
        );
        Debug.LogWarning($"{message} GameObject: {gameObject.name}.");

        if (character != null)
            character.StopMovement();

        ClearActiveFog();
        ClearInitialDirection();
        State = ExploreFogState.Failed;
    }

    private bool HandleMovementFailure(string message)
    {
        LogExploreTravelDiagnostic(
            "TargetRetryStarted",
            activeFog,
            activeInteractionPosition,
            message
        );
        FogRegistry.MarkFogUnreachableForPlanningCycle(activeFog, GetRequesterId());

        if (character != null)
            character.StopMovement();

        ClearActiveFog();

        if (repeatUntilCancelled && !ShouldCompleteForStopCondition())
        {
            if (StartNextExploreStep(false))
                return true;

            SetExploreLoopFlagBecauseNoReachableTarget();
            ClearInitialDirection();
            State = ExploreFogState.Completed;
            LogExploreTravelDiagnostic(
                "TaskCompleted",
                null,
                transform.position,
                "Exploration completed after a movement failure because no replacement target was available."
            );
            return true;
        }

        Fail(message);
        return false;
    }

    private void ClearActiveFog()
    {
        if (activeFog != null)
            activeFog.EndRevealWork(identity);

        activeFog = null;
        activeInteractionPosition = transform.position;
        lastMovingPosition = transform.position;
        movementNoProgressWorldHours = 0f;
        pathResolutionLogged = false;
    }

    private void ResetMovementProgress()
    {
        lastMovingPosition = transform.position;
        movementNoProgressWorldHours = 0f;
    }

    private bool HasMovementStalled()
    {
        ResolveWorldTimeManager();

        if (worldTimeManager == null || movementStuckWorldHours <= 0f)
            return false;

        float progressDistanceSquared = MovementProgressDistance * MovementProgressDistance;
        if ((transform.position - lastMovingPosition).sqrMagnitude > progressDistanceSquared)
        {
            ResetMovementProgress();
            return false;
        }

        movementNoProgressWorldHours += Mathf.Max(0f, worldTimeManager.DeltaWorldHours);
        return movementNoProgressWorldHours >= movementStuckWorldHours;
    }

    private void TryLogResolvedPath()
    {
        if (!logExploreTravelDiagnostics || pathResolutionLogged)
            return;

        pathResolutionLogged = true;
        LogExploreTravelDiagnostic(
            "PathResolved",
            activeFog,
            activeInteractionPosition,
            "The NavMeshAgent is no longer waiting for path resolution."
        );
    }

    private void LogExploreTravelDiagnostic(
        string eventName,
        FogObject diagnosticFog,
        Vector3 targetPosition,
        string reason)
    {
        if (!logExploreTravelDiagnostics)
            return;

        ResolveWorldTimeManager();

        if (navigationAgent == null)
            navigationAgent = GetComponent<NavMeshAgent>();

        if (taskController == null)
            taskController = GetComponent<NpcTaskController>();

        CharacterMovementProgressDiagnostics progress =
            character != null
                ? character.GetMovementProgressDiagnostics()
                : default;

        bool agentExists = navigationAgent != null;
        bool agentEnabled = agentExists && navigationAgent.enabled;
        bool isOnNavMesh = agentEnabled && navigationAgent.isOnNavMesh;
        bool canReadPath = agentEnabled && isOnNavMesh;
        bool pathPending = canReadPath && navigationAgent.pathPending;
        bool hasPath = canReadPath && navigationAgent.hasPath;
        string pathStatus = canReadPath
            ? navigationAgent.pathStatus.ToString()
            : "Unavailable";
        string remainingDistance = canReadPath
            ? navigationAgent.remainingDistance.ToString("0.###")
            : "Unavailable";
        float stoppingDistance = agentExists ? navigationAgent.stoppingDistance : 0f;
        float speed = agentExists ? navigationAgent.speed : 0f;
        float velocity = agentExists ? navigationAgent.velocity.magnitude : 0f;
        float desiredVelocity = agentExists ? navigationAgent.desiredVelocity.magnitude : 0f;
        bool isStopped = agentExists && navigationAgent.isStopped;

        Vector3 currentPosition = transform.position;
        Vector3 targetOffset = targetPosition - currentPosition;
        targetOffset.y = 0f;
        float targetDistance = targetOffset.magnitude;

        string fogName = diagnosticFog != null
            ? diagnosticFog.gameObject.name
            : "None";
        int fogInstanceId = diagnosticFog != null
            ? diagnosticFog.GetInstanceID()
            : 0;
        string fogGridCoordinate =
            diagnosticFog != null && diagnosticFog.HasGridCoordinate
                ? diagnosticFog.GridCoordinate.ToString()
                : "Unavailable";
        bool fogActive = diagnosticFog != null && diagnosticFog.isActiveAndEnabled;
        bool fogRevealed = diagnosticFog != null && diagnosticFog.IsRevealed;
        bool fogReserved = diagnosticFog != null && diagnosticFog.IsReserved;
        CharacterIdentity reservedBy = diagnosticFog != null ? diagnosticFog.ReservedBy : null;
        int reservedById = reservedBy != null ? reservedBy.CharacterId : 0;

        bool hasBarn = ResourceStorageRegistry.TryGetClosestStorageIgnoringTemporaryUnreachable(
            currentPosition,
            GetRequesterId(),
            out ResourceStorage nearestBarn
        );
        string barnName = hasBarn ? nearestBarn.gameObject.name : "None";
        bool insideBarnInteractionRange = hasBarn && nearestBarn.IsInInteractionRange(currentPosition);
        Vector3 barnTransformPosition = hasBarn ? nearestBarn.transform.position : currentPosition;
        Vector3 barnDepositPosition = hasBarn ? nearestBarn.GetDepositPosition() : currentPosition;
        float barnInteractionRadius = hasBarn ? nearestBarn.InteractionRadius : 0f;

        string detailedPathDiagnostic = BuildDetailedPathDiagnostic(
            eventName,
            canReadPath,
            hasPath,
            currentPosition,
            targetPosition
        );

        string task = taskController != null
            ? taskController.CurrentTask.ToString()
            : "MissingController";
        string taskState = taskController != null
            ? taskController.TaskState.ToString()
            : "MissingController";
        string assignedRole =
            taskController != null && taskController.HasAssignedWorkRole
                ? taskController.AssignedWorkRole.ToString()
                : "None";

        int worldDay = worldTimeManager != null ? worldTimeManager.CurrentDay : -1;
        float worldHour = worldTimeManager != null ? worldTimeManager.CurrentHour : -1f;
        float totalWorldHours = worldTimeManager != null
            ? worldTimeManager.TotalWorldHours
            : -1f;

        Debug.Log(
            $"ExploreTravelDiagnostic | Event={eventName} | NPC={gameObject.name} | " +
            $"CharacterId={GetRequesterId()} | Frame={Time.frameCount} | " +
            $"WorldDay={worldDay} | WorldHour={worldHour:0.###} | TotalWorldHours={totalWorldHours:0.###} | " +
            $"Task={task} | TaskState={taskState} | AssignedRole={assignedRole} | " +
            $"ExploreState={State} | Repeat={repeatUntilCancelled} | " +
            $"HasInitialDirection={hasInitialDirection} | InitialDirectionPending={initialDirectionPending} | " +
            $"InitialDirection={initialDirection} | Position={currentPosition.ToString("F3")} | " +
            $"Target={targetPosition.ToString("F3")} | TargetPlanarDistance={targetDistance:0.###} | " +
            $"Fog={fogName} | FogInstanceId={fogInstanceId} | FogGrid={fogGridCoordinate} | " +
            $"FogActive={fogActive} | FogRevealed={fogRevealed} | FogReserved={fogReserved} | " +
            $"FogReservedById={reservedById} | NearestBarn={barnName} | " +
            $"BarnTransformPosition={barnTransformPosition.ToString("F3")} | " +
            $"BarnDepositPosition={barnDepositPosition.ToString("F3")} | " +
            $"BarnInteractionRadius={barnInteractionRadius:0.###} | " +
            $"InsideBarnInteractionRange={insideBarnInteractionRange} | " +
            $"AgentExists={agentExists} | AgentEnabled={agentEnabled} | IsOnNavMesh={isOnNavMesh} | " +
            $"IsStopped={isStopped} | PathPending={pathPending} | HasPath={hasPath} | " +
            $"PathStatus={pathStatus} | RemainingDistance={remainingDistance} | " +
            $"StoppingDistance={stoppingDistance:0.###} | Speed={speed:0.###} | " +
            $"Velocity={velocity:0.###} | DesiredVelocity={desiredVelocity:0.###} | " +
            $"HasProgressSample={progress.HasProgressSample} | " +
            $"LastProgressPosition={progress.LastProgressPosition.ToString("F3")} | " +
            $"PlanarProgressDistance={progress.PlanarDistanceSinceLastSample:0.###} | " +
            $"CharacterNoProgressWorldHours={progress.NoProgressWorldHours:0.###} | " +
            $"ExploreNoProgressWorldHours={movementNoProgressWorldHours:0.###} | " +
            $"ExploreStallThresholdWorldHours={movementStuckWorldHours:0.###} | " +
            $"{detailedPathDiagnostic}" +
            $"ControlMode={CharacterControlRegistry.GetControlMode(character)} | Reason={reason}"
        );
    }

    private string BuildDetailedPathDiagnostic(
        string eventName,
        bool canReadPath,
        bool hasPath,
        Vector3 currentPosition,
        Vector3 targetPosition)
    {
        if (eventName != "PathResolved" &&
            eventName != "MovementPathFailed" &&
            eventName != "MovementStalled")
        {
            return string.Empty;
        }

        if (navigationAgent == null)
            return "DetailedPath=AgentMissing | ";

        Vector3 nextPosition = canReadPath
            ? navigationAgent.nextPosition
            : currentPosition;
        Vector3 steeringTarget = canReadPath
            ? navigationAgent.steeringTarget
            : currentPosition;

        StringBuilder builder = new StringBuilder(512);
        builder.Append("NextPosition=").Append(nextPosition.ToString("F3"))
            .Append(" | TransformToNextPlanarDistance=")
            .Append(GetPlanarDistance(currentPosition, nextPosition).ToString("0.###"))
            .Append(" | SteeringTarget=").Append(steeringTarget.ToString("F3"))
            .Append(" | SteeringTargetPlanarDistance=")
            .Append(GetPlanarDistance(currentPosition, steeringTarget).ToString("0.###"))
            .Append(" | UpdatePosition=").Append(navigationAgent.updatePosition)
            .Append(" | UpdateRotation=").Append(navigationAgent.updateRotation)
            .Append(" | IsOnOffMeshLink=").Append(canReadPath && navigationAgent.isOnOffMeshLink)
            .Append(" | AutoRepath=").Append(navigationAgent.autoRepath)
            .Append(" | AgentRadius=").Append(navigationAgent.radius.ToString("0.###"))
            .Append(" | AgentHeight=").Append(navigationAgent.height.ToString("0.###"))
            .Append(" | AgentBaseOffset=").Append(navigationAgent.baseOffset.ToString("0.###"))
            .Append(" | ObstacleAvoidanceType=").Append(navigationAgent.obstacleAvoidanceType)
            .Append(" | AvoidancePriority=").Append(navigationAgent.avoidancePriority)
            .Append(" | ");

        if (!canReadPath || !hasPath)
        {
            builder.Append("PathCornerCount=0 | PathCorners=Unavailable | ");
            return builder.ToString();
        }

        Vector3[] corners = navigationAgent.path.corners;
        int nearestCornerIndex = -1;
        float nearestCornerDistance = float.PositiveInfinity;

        builder.Append("PathCornerCount=").Append(corners.Length)
            .Append(" | PathCorners=[");

        for (int i = 0; i < corners.Length; i++)
        {
            float cornerDistance = GetPlanarDistance(currentPosition, corners[i]);
            if (cornerDistance < nearestCornerDistance)
            {
                nearestCornerDistance = cornerDistance;
                nearestCornerIndex = i;
            }

            if (i > 0)
                builder.Append("; ");

            builder.Append(i).Append(':').Append(corners[i].ToString("F3"))
                .Append("/Distance=").Append(cornerDistance.ToString("0.###"));
        }

        builder.Append("] | NearestPathCornerIndex=").Append(nearestCornerIndex)
            .Append(" | NearestPathCornerDistance=")
            .Append(nearestCornerIndex >= 0 ? nearestCornerDistance.ToString("0.###") : "Unavailable");

        if (corners.Length > 0)
        {
            Vector3 finalCorner = corners[corners.Length - 1];
            builder.Append(" | FinalPathCorner=").Append(finalCorner.ToString("F3"))
                .Append(" | FinalCornerToTargetPlanarDistance=")
                .Append(GetPlanarDistance(finalCorner, targetPosition).ToString("0.###"));
        }
        else
        {
            builder.Append(" | FinalPathCorner=Unavailable | FinalCornerToTargetPlanarDistance=Unavailable");
        }

        builder.Append(" | ");
        return builder.ToString();
    }

    private static float GetPlanarDistance(Vector3 first, Vector3 second)
    {
        Vector3 offset = second - first;
        offset.y = 0f;
        return offset.magnitude;
    }

    private static void SetExploreLoopFlagBecauseNoReachableTarget()
    {
        ManualDayPlanState.SetLoopFlag(NpcWorkRole.Explore);
    }

    private bool TrySelectNextFog(out FogObject fogObject, out Vector3 interactionPosition)
    {
        fogObject = null;
        interactionPosition = transform.position;

        if (hasInitialDirection && initialDirectionPending)
        {
            initialDirectionPending = false;
            return FogRegistry.TryGetClosestReachableFogInDirection(
                initialDirectionOrigin,
                initialDirection,
                transform.position,
                GetRequesterId(),
                out fogObject,
                out interactionPosition
            );
        }

        return FogRegistry.TryGetClosestAvailableFog(
            transform.position,
            GetRequesterId(),
            out fogObject,
            out interactionPosition
        );
    }

    private int GetRequesterId()
    {
        return identity != null ? identity.CharacterId : 0;
    }

    private void ClearInitialDirection()
    {
        hasInitialDirection = false;
        initialDirectionPending = false;
        initialDirection = ExploreDirection.North;
        initialDirectionOrigin = transform.position;
    }

    private void ResolveWorldTimeManager()
    {
        if (worldTimeManager != null)
            return;

        worldTimeManager = FindFirstObjectByType<WorldTimeManager>();
    }
}
