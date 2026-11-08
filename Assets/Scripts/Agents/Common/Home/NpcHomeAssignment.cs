/*
===============================================================================
Script:
NpcHomeAssignment

Purpose:
Provides shared NPC home assignment, reservation, and home facts for agent logic.

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
- CharacterIdentity
- House
- HomeRegistry
- WorldTimeManager

Automatically Resolved:
- CharacterIdentity on the same GameObject
- WorldTimeManager in the scene

Inspector References:
- retryCooldownWorldHours
- atHomeDistance
- logHomeTravelDiagnostics
===============================================================================
*/

using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterIdentity))]
public class NpcHomeAssignment : MonoBehaviour
{
    private const float MinimumRetryCooldownWorldHours = 0.01f;
    private const float ArrivalValidationPositionTolerance = 0.1f;

    [SerializeField] private float retryCooldownWorldHours = 0.2f;
    [SerializeField] private float atHomeDistance = 2f;
    [SerializeField] private bool logHomeTravelDiagnostics = true;

    private CharacterIdentity identity;
    private NavMeshAgent navigationAgent;
    private WorldTimeManager worldTimeManager;
    private House home;
    private Vector3 resolvedHomePosition;
    private bool hasResolvedHomePosition;
    private float nextRetryTotalWorldHours;
    private bool hasScheduledRetry;
    private bool noHomeLogged;
    private House arrivalValidationHome;
    private Vector3 arrivalValidationPosition;
    private Vector3 arrivalValidationHomePosition;
    private bool hasArrivalValidation;
    private bool cachedArrivalValidationResult;
    private int lastReleasedHomeInstanceId;

    public bool HasHome => home != null && home.isActiveAndEnabled && hasResolvedHomePosition;
    public House Home => HasHome ? home : null;
    public House AssignedHome => home;
    public Vector3 HomePosition => HasHome ? resolvedHomePosition : transform.position;
    public bool IsAtHome => IsAtValidHomeEntry();
    public float AtHomeDistance => Mathf.Max(0f, atHomeDistance);
    public bool HasCachedArrivalValidation => hasArrivalValidation;
    public bool CachedArrivalValidationResult =>
        hasArrivalValidation &&
        cachedArrivalValidationResult;

    private void Awake()
    {
        identity = GetComponent<CharacterIdentity>();
        navigationAgent = GetComponent<NavMeshAgent>();
        ResolveWorldTimeManager();
    }

    private void Start()
    {
        TryAssignHome();
    }

    private void OnDisable()
    {
        ReleaseHome();
    }

    private void Update()
    {
        if (HasHome)
            return;

        ReleaseInvalidHomeReference();

        if (!CanRetryNow())
            return;

        TryAssignHome();
    }

    public bool TryAssignHome()
    {
        return TryAssignHome(null);
    }

    public bool TryReassignHome()
    {
        House excludedHome = home;
        ReleaseHome();
        return TryAssignHome(excludedHome);
    }

    private bool TryAssignHome(House excludedHome)
    {
        if (HasHome)
            return true;

        ResolveIdentity();

        if (identity == null)
            return false;

        identity.EnsureAssigned();
        ReleaseInvalidHomeReference();

        bool assigned = HomeRegistry.TryReserveHome(
            identity,
            transform.position,
            excludedHome,
            out House reservedHome,
            out Vector3 homePosition
        );
        if (!assigned)
        {
            ScheduleRetry();
            LogNoHomeOnce();
            return false;
        }

        home = reservedHome;
        resolvedHomePosition = homePosition;
        hasResolvedHomePosition = true;
        InvalidateArrivalValidation();
        ClearRetrySchedule();
        noHomeLogged = false;
        LogHomeAssignmentDiagnostic(
            "HomeAssigned",
            home,
            resolvedHomePosition,
            home.GetInstanceID() == lastReleasedHomeInstanceId
                ? "SelectedSameAsLastReleased=True"
                : "SelectedSameAsLastReleased=False"
        );
        Debug.Log($"NPC home assigned: {identity.FullName} (ID {identity.CharacterId}) -> {home.gameObject.name}.");
        return true;
    }

    public void ReleaseHome()
    {
        ResolveIdentity();

        if (home != null && identity != null)
        {
            LogHomeAssignmentDiagnostic(
                "HomeReleased",
                home,
                resolvedHomePosition,
                "Releasing the currently assigned home slot."
            );
            lastReleasedHomeInstanceId = home.GetInstanceID();
            home.ReleaseSlot(identity);
        }

        home = null;
        resolvedHomePosition = transform.position;
        hasResolvedHomePosition = false;
        InvalidateArrivalValidation();
        noHomeLogged = false;
        ScheduleRetry();
    }

    public bool TryGetHomePosition(out Vector3 position)
    {
        if (!HasHome)
        {
            position = transform.position;
            return false;
        }

        position = resolvedHomePosition;
        return true;
    }

    private bool IsAtValidHomeEntry()
    {
        if (!HasHome)
        {
            InvalidateArrivalValidation();
            return false;
        }

        Vector3 currentPosition = transform.position;
        Vector3 flatOffset = resolvedHomePosition - currentPosition;
        flatOffset.y = 0f;
        float maximumDistance = Mathf.Max(0f, atHomeDistance);
        if (flatOffset.sqrMagnitude > maximumDistance * maximumDistance)
        {
            InvalidateArrivalValidation();
            return false;
        }

        ResolveNavigationAgent();
        if (navigationAgent == null ||
            !navigationAgent.enabled ||
            !navigationAgent.isOnNavMesh)
        {
            InvalidateArrivalValidation();
            return false;
        }

        if (CanReuseArrivalValidation(currentPosition))
            return cachedArrivalValidationResult;

        NavMeshQueryFilter queryFilter = new NavMeshQueryFilter
        {
            agentTypeID = navigationAgent.agentTypeID,
            areaMask = navigationAgent.areaMask
        };
        bool isBlockedByNavMeshBoundary = NavMesh.Raycast(
            navigationAgent.nextPosition,
            resolvedHomePosition,
            out _,
            queryFilter
        );

        arrivalValidationHome = home;
        arrivalValidationPosition = currentPosition;
        arrivalValidationHomePosition = resolvedHomePosition;
        hasArrivalValidation = true;
        cachedArrivalValidationResult = !isBlockedByNavMeshBoundary;
        return cachedArrivalValidationResult;
    }

    private bool CanReuseArrivalValidation(Vector3 currentPosition)
    {
        if (!hasArrivalValidation ||
            arrivalValidationHome != home ||
            arrivalValidationHomePosition != resolvedHomePosition)
        {
            return false;
        }

        Vector3 movement = currentPosition - arrivalValidationPosition;
        movement.y = 0f;
        return movement.sqrMagnitude <=
            ArrivalValidationPositionTolerance * ArrivalValidationPositionTolerance;
    }

    private void InvalidateArrivalValidation()
    {
        arrivalValidationHome = null;
        arrivalValidationPosition = Vector3.zero;
        arrivalValidationHomePosition = Vector3.zero;
        hasArrivalValidation = false;
        cachedArrivalValidationResult = false;
    }

    private void ResolveIdentity()
    {
        if (identity == null)
            identity = GetComponent<CharacterIdentity>();
    }

    private void ResolveNavigationAgent()
    {
        if (navigationAgent == null)
            navigationAgent = GetComponent<NavMeshAgent>();
    }

    private void ReleaseInvalidHomeReference()
    {
        if (home == null && !hasResolvedHomePosition)
            return;

        if (home != null && home.isActiveAndEnabled && hasResolvedHomePosition)
            return;

        ReleaseHome();
    }

    private bool CanRetryNow()
    {
        ResolveWorldTimeManager();
        if (worldTimeManager == null)
            return true;

        return !hasScheduledRetry ||
            worldTimeManager.TotalWorldHours >= nextRetryTotalWorldHours;
    }

    private void ScheduleRetry()
    {
        ResolveWorldTimeManager();
        if (worldTimeManager == null)
        {
            ClearRetrySchedule();
            return;
        }

        nextRetryTotalWorldHours = worldTimeManager.TotalWorldHours +
            Mathf.Max(MinimumRetryCooldownWorldHours, retryCooldownWorldHours);
        hasScheduledRetry = true;
    }

    private void ClearRetrySchedule()
    {
        nextRetryTotalWorldHours = 0f;
        hasScheduledRetry = false;
    }

    private void ResolveWorldTimeManager()
    {
        if (worldTimeManager == null)
            worldTimeManager = FindFirstObjectByType<WorldTimeManager>(FindObjectsInactive.Include);
    }

    private void OnValidate()
    {
        retryCooldownWorldHours = Mathf.Max(MinimumRetryCooldownWorldHours, retryCooldownWorldHours);
        atHomeDistance = Mathf.Max(0f, atHomeDistance);
    }

    private void LogNoHomeOnce()
    {
        if (noHomeLogged)
            return;

        string nameText = identity != null ? $"{identity.FullName} (ID {identity.CharacterId})" : gameObject.name;
        Debug.Log($"NPC has no free home slot yet: {nameText}. Will retry.");
        noHomeLogged = true;
    }

    private void LogHomeAssignmentDiagnostic(
        string eventName,
        House diagnosticHome,
        Vector3 diagnosticHomePosition,
        string reason)
    {
        if (!logHomeTravelDiagnostics)
            return;

        ResolveWorldTimeManager();

        string npcName = identity != null ? identity.FullName : gameObject.name;
        int characterId = identity != null ? identity.CharacterId : gameObject.GetInstanceID();
        string homeName = diagnosticHome != null
            ? diagnosticHome.gameObject.name
            : "None";
        int homeInstanceId = diagnosticHome != null
            ? diagnosticHome.GetInstanceID()
            : 0;
        Vector3 rawEntryPosition = diagnosticHome != null
            ? diagnosticHome.HomeEntryPosition
            : diagnosticHomePosition;
        int occupiedSlots = diagnosticHome != null
            ? diagnosticHome.OccupiedSlots
            : 0;
        int freeSlots = diagnosticHome != null
            ? diagnosticHome.FreeSlots
            : 0;
        int sleepingCapacity = diagnosticHome != null
            ? diagnosticHome.SleepingCapacity
            : 0;
        int worldDay = worldTimeManager != null ? worldTimeManager.CurrentDay : -1;
        float worldHour = worldTimeManager != null ? worldTimeManager.CurrentHour : -1f;
        float totalWorldHours = worldTimeManager != null
            ? worldTimeManager.TotalWorldHours
            : -1f;

        Debug.Log(
            $"HomeTravelDiagnostic | Event={eventName} | NPC={npcName} | " +
            $"CharacterId={characterId} | Frame={Time.frameCount} | " +
            $"WorldDay={worldDay} | WorldHour={worldHour:0.###} | " +
            $"TotalWorldHours={totalWorldHours:0.###} | " +
            $"Position={transform.position.ToString("F3")} | " +
            $"Home={homeName} | HomeInstanceId={homeInstanceId} | " +
            $"LastReleasedHomeInstanceId={lastReleasedHomeInstanceId} | " +
            $"RawEntryPosition={rawEntryPosition.ToString("F3")} | " +
            $"ResolvedHomePosition={diagnosticHomePosition.ToString("F3")} | " +
            $"OccupiedSlots={occupiedSlots} | FreeSlots={freeSlots} | " +
            $"SleepingCapacity={sleepingCapacity} | Reason={reason}"
        );
    }
}
