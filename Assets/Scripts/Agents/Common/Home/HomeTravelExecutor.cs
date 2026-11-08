/*
===============================================================================
Script:
HomeTravelExecutor

Purpose:
Executes movement to an NPC's assigned home or a temporary barn shelter.

Folder:
Assets/Scripts/Agents/Common/Home/

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
- NpcHomeAssignment
- CharacterIdentity
- AgentWorkProfile
- ResourceStorageRegistry

Automatically Resolved:
- Character on the same GameObject
- NpcHomeAssignment on the same GameObject

Inspector References:
- logBarnShelterDiagnostics
- logHomeTravelDiagnostics
===============================================================================
*/

using UnityEngine;
using UnityEngine.AI;

public enum HomeTravelState
{
    Idle,
    WaitingForShelter,
    Moving,
    Completed,
    Failed
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(NpcHomeAssignment))]
public class HomeTravelExecutor : MonoBehaviour
{
    private const float RequesterNavMeshSampleDistance = 2f;
    private const float MovementStuckWorldHours = 0.5f;
    private const float ShelterRetryRealSeconds = 1f;

    [SerializeField] private bool logBarnShelterDiagnostics = true;
    [SerializeField] private bool logHomeTravelDiagnostics = true;
    [SerializeField] private bool logBarnApproachStallDiagnostics;

    private Character character;
    private CharacterIdentity identity;
    private NpcHomeAssignment homeAssignment;
    private AgentWorkProfile workProfile;
    private WorldTimeManager worldTimeManager;
    private NavMeshAgent navigationAgent;
    private NavMeshPath homePath;
    private ResourceStorage activeBarnShelter;
    private Vector3 barnShelterPosition;
    private float nextShelterRetryTotalWorldHours;
    private float nextShelterRetryRealTime;
    private bool isUsingBarnShelter;
    private bool missingBarnWarningLogged;
    private bool barnPathResolutionLogged;
    private bool homePathResolutionLogged;
    private string barnShelterRetryReason;
    private BarnApproachStallDiagnosticTracker barnApproachDiagnostics;

    public HomeTravelState State { get; private set; } = HomeTravelState.Idle;
    public bool IsRunning =>
        State == HomeTravelState.WaitingForShelter ||
        State == HomeTravelState.Moving;
    public bool IsCompleted => State == HomeTravelState.Completed;
    public bool HasFailed => State == HomeTravelState.Failed;
    public bool IsUsingBarnShelter => isUsingBarnShelter;
    public bool HasActiveBarnShelter =>
        isUsingBarnShelter &&
        activeBarnShelter != null &&
        activeBarnShelter.isActiveAndEnabled;
    public bool IsAtBarnShelter =>
        HasActiveBarnShelter &&
        activeBarnShelter.IsInInteractionRange(transform.position);

    private void Awake()
    {
        character = GetComponent<Character>();
        identity = GetComponent<CharacterIdentity>();
        homeAssignment = GetComponent<NpcHomeAssignment>();
        workProfile = GetComponent<AgentWorkProfile>();
        navigationAgent = GetComponent<NavMeshAgent>();
        homePath = new NavMeshPath();
        barnApproachDiagnostics = new BarnApproachStallDiagnosticTracker();
        ResolveWorldTimeManager();

        Debug.Assert(
            character != null,
            $"Missing component: Character. Expected location: same GameObject as HomeTravelExecutor. Script: {nameof(HomeTravelExecutor)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            homeAssignment != null,
            $"Missing component: NpcHomeAssignment. Expected location: same GameObject as HomeTravelExecutor. Script: {nameof(HomeTravelExecutor)}. GameObject: {gameObject.name}."
        );
    }

    private void OnDisable()
    {
        if (IsRunning)
            CancelGoHome();
    }

    private void Update()
    {
        if (State == HomeTravelState.WaitingForShelter)
        {
            UpdateWaitingForShelter();
            return;
        }

        if (State != HomeTravelState.Moving)
            return;

        if (isUsingBarnShelter)
            UpdateMovingToBarnShelter();
        else
            UpdateMovingHome();
    }

    public bool ExecuteGoHome()
    {
        return StartGoHome();
    }

    public bool StartGoHome()
    {
        if (IsRunning)
            return false;

        SimulationPerformanceCounters.AddHomeTravelStart();
        ClearBarnShelter();
        homePathResolutionLogged = true;

        if (TryGetReachableHomePosition(out Vector3 homePosition) ||
            TryReassignAndGetReachableHomePosition(out homePosition))
        {
            if (!character.TryMoveTo(homePosition))
            {
                LogHomeTravelDiagnostic(
                    "TravelFailed",
                    homeAssignment != null ? homeAssignment.Home : null,
                    homePosition,
                    null,
                    "Character.TryMoveTo rejected the resolved home position."
                );
                homeAssignment.ReleaseHome();
                Fail("GoHome execution failed: movement could not start.");
                return false;
            }

            homePathResolutionLogged = false;
            State = HomeTravelState.Moving;
            LogHomeTravelDiagnostic(
                "RouteRequested",
                homeAssignment != null ? homeAssignment.Home : null,
                homePosition,
                null,
                "Character.TryMoveTo accepted the resolved home position."
            );
            return true;
        }

        if (!IsShelterSleepWindowActive())
        {
            Fail("GoHome execution failed: NPC has no reachable assigned home entry.");
            return false;
        }

        SimulationPerformanceCounters.AddHomelessShelterRequest();
        isUsingBarnShelter = true;
        return TryStartBarnShelterTravelOrWait();
    }

    public void CancelGoHome()
    {
        if (character != null)
            character.StopMovement();

        ClearBarnShelter();
        homePathResolutionLogged = true;
        State = HomeTravelState.Idle;
    }

    public void ClearBarnShelter()
    {
        barnApproachDiagnostics.Reset();
        activeBarnShelter = null;
        barnShelterPosition = transform.position;
        nextShelterRetryTotalWorldHours = 0f;
        nextShelterRetryRealTime = 0f;
        isUsingBarnShelter = false;
        missingBarnWarningLogged = false;
        barnPathResolutionLogged = false;
        barnShelterRetryReason = string.Empty;
    }

    private void UpdateMovingHome()
    {
        if (homeAssignment == null || !homeAssignment.HasHome)
        {
            LogHomeTravelDiagnostic(
                "TravelFailed",
                homeAssignment != null ? homeAssignment.AssignedHome : null,
                transform.position,
                null,
                "The assigned home became invalid during travel."
            );
            if (TryRestartTravelWithAlternativeHome())
                return;

            Fail("GoHome execution failed: assigned home became invalid.");
            return;
        }

        TryLogResolvedHomePath();

        if (homeAssignment.IsAtHome &&
            homeAssignment.TryGetHomePosition(out Vector3 homePosition) &&
            character.HasReachedInteractionTarget(homePosition))
        {
            LogHomeTravelDiagnostic(
                "Arrived",
                homeAssignment.Home,
                homePosition,
                null,
                "Home arrival conditions were satisfied."
            );
            character.StopMovement();
            State = HomeTravelState.Completed;
            SimulationPerformanceCounters.AddHomeArrival();
            return;
        }

        if (character.HasMovementPathFailed())
        {
            homeAssignment.TryGetHomePosition(out Vector3 failedPathTarget);
            LogHomeTravelDiagnostic(
                "PathFailed",
                homeAssignment.Home,
                failedPathTarget,
                null,
                "The resolved NavMesh path is invalid or partial."
            );
            if (TryRestartTravelWithAlternativeHome())
                return;

            Fail("GoHome execution failed: movement path is invalid.");
            return;
        }

        if (character.HasMovementStalled(MovementStuckWorldHours))
        {
            homeAssignment.TryGetHomePosition(out Vector3 stalledTarget);
            LogHomeTravelDiagnostic(
                "MovementStalled",
                homeAssignment.Home,
                stalledTarget,
                null,
                "Character.HasMovementStalled returned true."
            );
            SimulationPerformanceCounters.AddHomeMovementStall();

            if (TryRestartTravelWithAlternativeHome())
                return;

            Fail("GoHome execution failed: movement stalled before reaching home.");
            return;
        }
    }

    private void UpdateWaitingForShelter()
    {
        if (!IsShelterSleepWindowActive())
        {
            ClearBarnShelter();
            State = HomeTravelState.Failed;
            return;
        }

        if (!CanRetryBarnShelterNow())
            return;

        LogBarnShelterDiagnostic(
            "RetryStarted",
            null,
            transform.position,
            barnShelterRetryReason
        );
        TryStartBarnShelterTravelOrWait();
    }

    private void UpdateMovingToBarnShelter()
    {
        if (!IsShelterSleepWindowActive())
        {
            if (character != null)
                character.StopMovement();

            ClearBarnShelter();
            State = HomeTravelState.Failed;
            return;
        }

        if (!HasActiveBarnShelter)
        {
            ScheduleBarnShelterRetry("the selected barn became unavailable");
            return;
        }

        barnApproachDiagnostics.Update(
            logBarnApproachStallDiagnostics,
            character,
            identity,
            activeBarnShelter,
            barnShelterPosition,
            "BarnShelter",
            State.ToString());

        TryLogResolvedBarnPath();

        if (IsAtBarnShelter)
        {
            LogBarnShelterDiagnostic(
                "Arrived",
                activeBarnShelter,
                barnShelterPosition,
                "Barn shelter arrival conditions were satisfied."
            );
            character.StopMovement();
            State = HomeTravelState.Completed;
            SimulationPerformanceCounters.AddBarnShelterArrival();
            return;
        }

        if (character.HasMovementPathFailed())
        {
            LogBarnShelterDiagnostic(
                "PathFailed",
                activeBarnShelter,
                barnShelterPosition,
                "The resolved NavMesh path is invalid or partial."
            );
            ResourceStorageRegistry.MarkStorageTemporarilyUnreachable(
                activeBarnShelter,
                transform.position,
                GetRequesterId(),
                "Overnight barn shelter path became invalid.",
                true
            );
            ScheduleBarnShelterRetry("the route to the selected barn became invalid");
            return;
        }

        if (character.HasMovementStalled(MovementStuckWorldHours))
        {
            barnApproachDiagnostics.LogConfirmedStall(
                logBarnApproachStallDiagnostics,
                character,
                identity,
                activeBarnShelter,
                barnShelterPosition,
                "BarnShelter",
                State.ToString(),
                "Character.HasMovementStalled returned true");
            LogBarnShelterDiagnostic(
                "MovementStalled",
                activeBarnShelter,
                barnShelterPosition,
                "Character.HasMovementStalled returned true."
            );
            SimulationPerformanceCounters.AddHomeMovementStall();
            ResourceStorageRegistry.MarkStorageTemporarilyUnreachable(
                activeBarnShelter,
                transform.position,
                GetRequesterId(),
                "Overnight barn shelter movement stalled.",
                true
            );
            ScheduleBarnShelterRetry("movement to the selected barn stalled");
        }
    }

    private bool TryStartBarnShelterTravelOrWait()
    {
        isUsingBarnShelter = true;
        ResourceStorageLookupResult lookupResult = ResourceStorageRegistry.GetClosestStorage(
            transform.position,
            GetRequesterId(),
            out ResourceStorage shelter
        );

        if (lookupResult != ResourceStorageLookupResult.Found || shelter == null)
        {
            SimulationPerformanceCounters.AddBarnShelterLookupFailure();
            ScheduleBarnShelterRetry("no active barn is available");
            return true;
        }

        if (shelter.IsInInteractionRange(transform.position))
        {
            activeBarnShelter = shelter;
            barnShelterPosition = transform.position;
            missingBarnWarningLogged = false;
            barnPathResolutionLogged = true;
            barnShelterRetryReason = string.Empty;

            if (character != null)
                character.StopMovement();

            State = HomeTravelState.Completed;
            SimulationPerformanceCounters.AddBarnShelterArrival();
            LogBarnShelterDiagnostic(
                "Arrived",
                activeBarnShelter,
                barnShelterPosition,
                "NPC was already inside the barn interaction range; no movement route was requested."
            );
            return true;
        }

        if (!shelter.TryGetInteractionPosition(
                transform.position,
                GetRequesterId(),
                out Vector3 interactionPosition))
        {
            SimulationPerformanceCounters.AddBarnShelterLookupFailure();
            ResourceStorageRegistry.MarkStorageTemporarilyUnreachable(
                shelter,
                transform.position,
                GetRequesterId(),
                "Overnight barn shelter has no reachable interaction position.",
                true
            );
            ScheduleBarnShelterRetry("the closest barn has no reachable interaction position");
            return true;
        }

        if (character == null || !character.TryMoveTo(interactionPosition))
        {
            SimulationPerformanceCounters.AddBarnShelterLookupFailure();
            ResourceStorageRegistry.MarkStorageTemporarilyUnreachable(
                shelter,
                transform.position,
                GetRequesterId(),
                "Overnight barn shelter movement could not start.",
                true
            );
            ScheduleBarnShelterRetry("movement to the closest barn could not start");
            return true;
        }

        activeBarnShelter = shelter;
        barnShelterPosition = interactionPosition;
        missingBarnWarningLogged = false;
        barnPathResolutionLogged = false;
        barnShelterRetryReason = string.Empty;
        State = HomeTravelState.Moving;
        SimulationPerformanceCounters.AddBarnShelterTravelStart();
        LogBarnShelterDiagnostic(
            "RouteRequested",
            activeBarnShelter,
            barnShelterPosition,
            "Character.TryMoveTo accepted the selected interaction position."
        );
        return true;
    }

    private void ScheduleBarnShelterRetry(string reason)
    {
        LogBarnShelterDiagnostic(
            "RetryScheduled",
            activeBarnShelter,
            barnShelterPosition,
            reason
        );

        if (character != null)
            character.StopMovement();

        activeBarnShelter = null;
        barnShelterPosition = transform.position;
        isUsingBarnShelter = true;
        barnPathResolutionLogged = false;
        barnShelterRetryReason = reason;
        ResolveWorldTimeManager();

        if (worldTimeManager != null)
        {
            nextShelterRetryTotalWorldHours =
                worldTimeManager.TotalWorldHours +
                ResourceStorageRegistry.TemporaryUnreachableDurationWorldHours;
            nextShelterRetryRealTime = 0f;
        }
        else
        {
            nextShelterRetryTotalWorldHours = 0f;
            nextShelterRetryRealTime = Time.time + ShelterRetryRealSeconds;
        }

        State = HomeTravelState.WaitingForShelter;
        SimulationPerformanceCounters.AddBarnShelterCooldown();

        if (missingBarnWarningLogged)
            return;

        Debug.LogWarning(
            $"Homeless NPC is waiting for an overnight barn shelter because {reason}. " +
            $"GameObject: {gameObject.name}."
        );
        missingBarnWarningLogged = true;
    }

    private void TryLogResolvedBarnPath()
    {
        if (!logBarnShelterDiagnostics ||
            barnPathResolutionLogged ||
            navigationAgent == null)
        {
            return;
        }

        if (navigationAgent.enabled &&
            navigationAgent.isOnNavMesh &&
            navigationAgent.pathPending)
        {
            return;
        }

        barnPathResolutionLogged = true;
        LogBarnShelterDiagnostic(
            "PathResolved",
            activeBarnShelter,
            barnShelterPosition,
            "The NavMeshAgent is no longer waiting for path resolution."
        );
    }

    private void TryLogResolvedHomePath()
    {
        if (!logHomeTravelDiagnostics ||
            homePathResolutionLogged ||
            navigationAgent == null)
        {
            return;
        }

        if (navigationAgent.enabled &&
            navigationAgent.isOnNavMesh &&
            navigationAgent.pathPending)
        {
            return;
        }

        homePathResolutionLogged = true;
        homeAssignment.TryGetHomePosition(out Vector3 homePosition);
        LogHomeTravelDiagnostic(
            "PathResolved",
            homeAssignment.Home,
            homePosition,
            null,
            "The NavMeshAgent is no longer waiting for path resolution."
        );
    }

    private void LogBarnShelterDiagnostic(
        string eventName,
        ResourceStorage shelter,
        Vector3 targetPosition,
        string reason)
    {
        if (!logBarnShelterDiagnostics)
            return;

        ResolveWorldTimeManager();

        if (navigationAgent == null)
            navigationAgent = GetComponent<NavMeshAgent>();

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
        int agentTypeId = agentExists ? navigationAgent.agentTypeID : -1;
        int areaMask = agentExists ? navigationAgent.areaMask : 0;

        Vector3 currentPosition = transform.position;
        Vector3 targetOffset = targetPosition - currentPosition;
        targetOffset.y = 0f;
        float targetDistance = targetOffset.magnitude;
        Vector3 barnPosition = shelter != null
            ? shelter.GetDepositPosition()
            : targetPosition;
        bool insideInteractionRange =
            shelter != null &&
            shelter.IsInInteractionRange(currentPosition);
        string barnName = shelter != null ? shelter.gameObject.name : "None";

        int worldDay = worldTimeManager != null ? worldTimeManager.CurrentDay : -1;
        float worldHour = worldTimeManager != null ? worldTimeManager.CurrentHour : -1f;
        float totalWorldHours = worldTimeManager != null
            ? worldTimeManager.TotalWorldHours
            : -1f;
        float deltaWorldHours = worldTimeManager != null
            ? worldTimeManager.DeltaWorldHours
            : 0f;
        float dayDuration = worldTimeManager != null
            ? worldTimeManager.DayDurationRealSeconds
            : 0f;
        float referenceDuration = worldTimeManager != null
            ? worldTimeManager.ReferenceDayDurationRealSeconds
            : 0f;

        Debug.Log(
            $"BarnShelterDiagnostic | Event={eventName} | NPC={gameObject.name} | " +
            $"CharacterId={GetRequesterId()} | Frame={Time.frameCount} | " +
            $"WorldDay={worldDay} | WorldHour={worldHour:0.###} | " +
            $"TotalWorldHours={totalWorldHours:0.###} | DeltaWorldHours={deltaWorldHours:0.#####} | " +
            $"DayDurationRealSeconds={dayDuration:0.###} | ReferenceDayDurationRealSeconds={referenceDuration:0.###} | " +
            $"Position={currentPosition.ToString("F3")} | " +
            $"LastProgressPosition={progress.LastProgressPosition.ToString("F3")} | " +
            $"HasProgressSample={progress.HasProgressSample} | " +
            $"PlanarProgressDistance={progress.PlanarDistanceSinceLastSample:0.###} | " +
            $"NoProgressWorldHours={progress.NoProgressWorldHours:0.###} | " +
            $"RequiredProgressDistance={progress.RequiredProgressDistance:0.###} | " +
            $"StallThresholdWorldHours={MovementStuckWorldHours:0.###} | " +
            $"Barn={barnName} | BarnPosition={barnPosition.ToString("F3")} | " +
            $"Target={targetPosition.ToString("F3")} | TargetPlanarDistance={targetDistance:0.###} | " +
            $"InsideBarnInteractionRange={insideInteractionRange} | " +
            $"AgentExists={agentExists} | AgentEnabled={agentEnabled} | " +
            $"IsOnNavMesh={isOnNavMesh} | IsStopped={isStopped} | " +
            $"PathPending={pathPending} | HasPath={hasPath} | PathStatus={pathStatus} | " +
            $"RemainingDistance={remainingDistance} | StoppingDistance={stoppingDistance:0.###} | " +
            $"Speed={speed:0.###} | Velocity={velocity:0.###} | DesiredVelocity={desiredVelocity:0.###} | " +
            $"AgentTypeId={agentTypeId} | AreaMask={areaMask} | " +
            $"ControlMode={CharacterControlRegistry.GetControlMode(character)} | Reason={reason}"
        );
    }

    private void LogHomeTravelDiagnostic(
        string eventName,
        House diagnosticHome,
        Vector3 targetPosition,
        House comparedHome,
        string reason)
    {
        if (!logHomeTravelDiagnostics)
            return;

        ResolveWorldTimeManager();

        if (navigationAgent == null)
            navigationAgent = GetComponent<NavMeshAgent>();

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
        float atHomeDistance =
            homeAssignment != null
                ? homeAssignment.AtHomeDistance
                : 0f;
        bool withinHomeDistance = targetDistance <= atHomeDistance;
        bool isAtHome = homeAssignment != null && homeAssignment.IsAtHome;
        bool hasArrivalValidation =
            homeAssignment != null &&
            homeAssignment.HasCachedArrivalValidation;
        bool arrivalValidationResult =
            homeAssignment != null &&
            homeAssignment.CachedArrivalValidationResult;

        string homeName = diagnosticHome != null
            ? diagnosticHome.gameObject.name
            : "None";
        int homeInstanceId = diagnosticHome != null
            ? diagnosticHome.GetInstanceID()
            : 0;
        Vector3 rawEntryPosition = diagnosticHome != null
            ? diagnosticHome.HomeEntryPosition
            : targetPosition;
        int occupiedSlots = diagnosticHome != null
            ? diagnosticHome.OccupiedSlots
            : 0;
        int freeSlots = diagnosticHome != null
            ? diagnosticHome.FreeSlots
            : 0;
        int sleepingCapacity = diagnosticHome != null
            ? diagnosticHome.SleepingCapacity
            : 0;
        int comparedHomeInstanceId = comparedHome != null
            ? comparedHome.GetInstanceID()
            : 0;
        bool selectedSameAsComparedHome =
            diagnosticHome != null &&
            diagnosticHome == comparedHome;

        int worldDay = worldTimeManager != null ? worldTimeManager.CurrentDay : -1;
        float worldHour = worldTimeManager != null ? worldTimeManager.CurrentHour : -1f;
        float totalWorldHours = worldTimeManager != null
            ? worldTimeManager.TotalWorldHours
            : -1f;
        float deltaWorldHours = worldTimeManager != null
            ? worldTimeManager.DeltaWorldHours
            : 0f;

        Debug.Log(
            $"HomeTravelDiagnostic | Event={eventName} | NPC={gameObject.name} | " +
            $"CharacterId={GetRequesterId()} | Frame={Time.frameCount} | " +
            $"WorldDay={worldDay} | WorldHour={worldHour:0.###} | " +
            $"TotalWorldHours={totalWorldHours:0.###} | DeltaWorldHours={deltaWorldHours:0.#####} | " +
            $"Position={currentPosition.ToString("F3")} | Home={homeName} | " +
            $"HomeInstanceId={homeInstanceId} | ComparedHomeInstanceId={comparedHomeInstanceId} | " +
            $"SelectedSameAsComparedHome={selectedSameAsComparedHome} | " +
            $"RawEntryPosition={rawEntryPosition.ToString("F3")} | " +
            $"ResolvedHomePosition={targetPosition.ToString("F3")} | " +
            $"TargetPlanarDistance={targetDistance:0.###} | AtHomeDistance={atHomeDistance:0.###} | " +
            $"WithinHomeDistance={withinHomeDistance} | " +
            $"HasHome={homeAssignment != null && homeAssignment.HasHome} | IsAtHome={isAtHome} | " +
            $"HasArrivalValidation={hasArrivalValidation} | " +
            $"ArrivalValidationResult={arrivalValidationResult} | " +
            $"OccupiedSlots={occupiedSlots} | FreeSlots={freeSlots} | " +
            $"SleepingCapacity={sleepingCapacity} | " +
            $"LastProgressPosition={progress.LastProgressPosition.ToString("F3")} | " +
            $"HasProgressSample={progress.HasProgressSample} | " +
            $"PlanarProgressDistance={progress.PlanarDistanceSinceLastSample:0.###} | " +
            $"NoProgressWorldHours={progress.NoProgressWorldHours:0.###} | " +
            $"RequiredProgressDistance={progress.RequiredProgressDistance:0.###} | " +
            $"StallThresholdWorldHours={MovementStuckWorldHours:0.###} | " +
            $"AgentExists={agentExists} | AgentEnabled={agentEnabled} | " +
            $"IsOnNavMesh={isOnNavMesh} | IsStopped={isStopped} | " +
            $"PathPending={pathPending} | HasPath={hasPath} | PathStatus={pathStatus} | " +
            $"RemainingDistance={remainingDistance} | StoppingDistance={stoppingDistance:0.###} | " +
            $"Speed={speed:0.###} | Velocity={velocity:0.###} | DesiredVelocity={desiredVelocity:0.###} | " +
            $"ControlMode={CharacterControlRegistry.GetControlMode(character)} | Reason={reason}"
        );
    }

    private bool CanRetryBarnShelterNow()
    {
        ResolveWorldTimeManager();
        if (worldTimeManager != null)
            return worldTimeManager.TotalWorldHours >= nextShelterRetryTotalWorldHours;

        return Time.time >= nextShelterRetryRealTime;
    }

    private bool IsShelterSleepWindowActive()
    {
        ResolveWorldTimeManager();
        return worldTimeManager == null ||
            workProfile == null ||
            workProfile.IsSleepTime(worldTimeManager.CurrentHour);
    }

    private int GetRequesterId()
    {
        return identity != null ? identity.CharacterId : gameObject.GetInstanceID();
    }

    private void ResolveWorldTimeManager()
    {
        if (worldTimeManager == null)
            worldTimeManager = FindFirstObjectByType<WorldTimeManager>();
    }

    private bool TryGetValidHomePosition(out Vector3 homePosition)
    {
        homePosition = transform.position;

        return homeAssignment != null &&
            homeAssignment.HasHome &&
            homeAssignment.TryGetHomePosition(out homePosition);
    }

    private bool TryGetReachableHomePosition(out Vector3 homePosition)
    {
        homePosition = transform.position;

        if (!TryGetValidHomePosition(out Vector3 requestedHomePosition) ||
            !NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit requesterHit,
                RequesterNavMeshSampleDistance,
                NavMesh.AllAreas))
        {
            return false;
        }

        if (homePath == null)
            homePath = new NavMeshPath();

        SimulationPerformanceCounters.AddHomePathCalculation();
        if (!NavMesh.CalculatePath(
                requesterHit.position,
                requestedHomePosition,
                NavMesh.AllAreas,
                homePath) ||
            homePath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        homePosition = requestedHomePosition;
        return true;
    }

    private bool TryReassignAndGetReachableHomePosition(out Vector3 homePosition)
    {
        homePosition = transform.position;
        SimulationPerformanceCounters.AddHomeReassignmentAttempt();

        House previousHome =
            homeAssignment != null
                ? homeAssignment.Home
                : null;
        Vector3 previousHomePosition =
            homeAssignment != null
                ? homeAssignment.HomePosition
                : transform.position;
        LogHomeTravelDiagnostic(
            "ReassignmentStarted",
            previousHome,
            previousHomePosition,
            previousHome,
            "Attempting to replace the current or missing home assignment."
        );

        if (homeAssignment == null || !homeAssignment.TryReassignHome())
        {
            LogHomeTravelDiagnostic(
                "ReassignmentFailed",
                null,
                transform.position,
                previousHome,
                "No alternative reachable home slot was assigned."
            );
            return false;
        }

        if (TryGetReachableHomePosition(out homePosition))
        {
            LogHomeTravelDiagnostic(
                "ReassignmentSucceeded",
                homeAssignment.Home,
                homePosition,
                previousHome,
                "A reachable home slot was assigned."
            );
            return true;
        }

        LogHomeTravelDiagnostic(
            "ReassignmentFailed",
            homeAssignment.Home,
            homeAssignment.HomePosition,
            previousHome,
            "The assigned home did not pass the shared reachability validation."
        );
        homeAssignment.ReleaseHome();
        return false;
    }

    private bool TryRestartTravelWithAlternativeHome()
    {
        if (character != null)
            character.StopMovement();

        if (!TryReassignAndGetReachableHomePosition(out Vector3 homePosition) ||
            character == null ||
            !character.TryMoveTo(homePosition))
        {
            LogHomeTravelDiagnostic(
                "TravelFailed",
                homeAssignment != null ? homeAssignment.Home : null,
                homePosition,
                null,
                "Alternative home travel could not start."
            );
            if (homeAssignment != null && homeAssignment.HasHome)
                homeAssignment.ReleaseHome();

            return false;
        }

        homePathResolutionLogged = false;
        State = HomeTravelState.Moving;
        LogHomeTravelDiagnostic(
            "RouteRequested",
            homeAssignment.Home,
            homePosition,
            null,
            "Character.TryMoveTo accepted the reassigned home position."
        );
        return true;
    }

    private void Fail(string message)
    {
        if (!isUsingBarnShelter)
        {
            Vector3 homePosition =
                homeAssignment != null
                    ? homeAssignment.HomePosition
                    : transform.position;
            LogHomeTravelDiagnostic(
                "TravelFailed",
                homeAssignment != null ? homeAssignment.AssignedHome : null,
                homePosition,
                null,
                message
            );
        }

        Debug.LogWarning($"{message} GameObject: {gameObject.name}.");

        if (character != null)
            character.StopMovement();

        if (isUsingBarnShelter)
            ClearBarnShelter();

        homePathResolutionLogged = true;
        State = HomeTravelState.Failed;
    }
}
