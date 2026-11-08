/*
===============================================================================
Script:
ConstructionSite

Purpose:
Tracks world-time construction progress and replaces itself with a finished building.

Folder:
Assets/Scripts/Buildings/

Scene Hierarchy:
_World
- Buildings
  - ConstructionSite

Attach To:
ConstructionSite

Required Children:
None

Dependencies:
- WorldTimeManager
- SimulationBalanceConfig
- House
- ResourceStorage

Automatically Resolved:
- WorldTimeManager

Inspector References:
- finishedBuildingPrefab
- worldTimeManager
- simulationBalanceConfig
===============================================================================
*/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class ConstructionSite : MonoBehaviour
{
    private const float FallbackInteractionOffset = 0.5f;
    private const float NavMeshSampleDistance = 1.5f;

    [SerializeField] private GameObject finishedBuildingPrefab;
    [SerializeField] private WorldTimeManager worldTimeManager;
    [SerializeField] private SimulationBalanceConfig simulationBalanceConfig;

    private float currentWorkWorldHours;
    private readonly HashSet<CharacterIdentity> activeWorkers = new HashSet<CharacterIdentity>();
    private bool missingFinishedPrefabWarningLogged;
    private bool missingWorldTimeManagerWarningLogged;
    private bool missingBalanceConfigWarningLogged;
    private bool unknownFinishedBuildingWarningLogged;

    public bool IsComplete { get; private set; }
    public int PlannedSleepingCapacity
    {
        get
        {
            if (finishedBuildingPrefab == null)
                return 0;

            House house = finishedBuildingPrefab.GetComponentInChildren<House>(true);
            return house != null ? house.SleepingCapacity : 0;
        }
    }
    public int ActiveWorkerCount
    {
        get
        {
            activeWorkers.RemoveWhere(worker => worker == null);
            return activeWorkers.Count;
        }
    }

    private void Awake()
    {
        AutoResolveWorldTimeManager();
    }

    private void OnEnable()
    {
        ConstructionSiteRegistry.Register(this);
    }

    private void OnDisable()
    {
        ConstructionSiteRegistry.Unregister(this);
    }

    public void Configure(WorldTimeManager manager, SimulationBalanceConfig balanceConfig)
    {
        if (manager != null)
            worldTimeManager = manager;

        if (balanceConfig != null)
            simulationBalanceConfig = balanceConfig;
    }

    public void Configure(
        WorldTimeManager manager,
        SimulationBalanceConfig balanceConfig,
        GameObject configuredFinishedBuildingPrefab)
    {
        Configure(manager, balanceConfig);

        if (configuredFinishedBuildingPrefab != null)
            finishedBuildingPrefab = configuredFinishedBuildingPrefab;
    }

    public Vector3 GetInteractionPosition(Vector3 builderPosition)
    {
        Bounds bounds = GetConstructionBounds();
        Vector3[] candidates = CreateInteractionCandidates(bounds);
        Vector3 bestPosition = Vector3.zero;
        float bestDistance = float.MaxValue;
        bool foundPosition = false;

        foreach (Vector3 candidate in candidates)
        {
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavMeshSampleDistance, NavMesh.AllAreas))
                continue;

            if (IsInsideHorizontalBounds(hit.position, bounds))
                continue;

            float distance = (hit.position - builderPosition).sqrMagnitude;
            if (distance >= bestDistance)
                continue;

            bestPosition = hit.position;
            bestDistance = distance;
            foundPosition = true;
        }

        if (!foundPosition)
            bestPosition = GetFallbackInteractionPosition(bounds, builderPosition);

        return bestPosition;
    }

    public bool RegisterWorker(CharacterIdentity worker)
    {
        if (worker == null || IsComplete)
            return false;

        activeWorkers.Add(worker);
        return true;
    }

    public void UnregisterWorker(CharacterIdentity worker)
    {
        if (worker == null)
            return;

        activeWorkers.Remove(worker);
    }

    public void Work(CharacterIdentity worker)
    {
        if (IsComplete)
            return;

        if (worker != null && !RegisterWorker(worker))
            return;

        if (!CanAdvanceConstruction())
            return;

        float requiredWorkWorldHours = GetRequiredWorkWorldHours();
        currentWorkWorldHours += Mathf.Max(0f, worldTimeManager.DeltaWorldHours);

        if (currentWorkWorldHours >= requiredWorkWorldHours)
            FinishConstruction(worker);
    }

    public void Work()
    {
        Work(null);
    }

    public bool CanAcceptWork()
    {
        return !IsComplete && CanAdvanceConstruction();
    }

    private bool CanAdvanceConstruction()
    {
        if (finishedBuildingPrefab == null)
        {
            if (!missingFinishedPrefabWarningLogged)
            {
                Debug.LogWarning($"ConstructionSite cannot finish: finishedBuildingPrefab is not assigned. GameObject: {gameObject.name}.");
                missingFinishedPrefabWarningLogged = true;
            }

            return false;
        }

        if (worldTimeManager == null)
            AutoResolveWorldTimeManager();

        if (worldTimeManager == null)
        {
            if (!missingWorldTimeManagerWarningLogged)
            {
                Debug.LogWarning($"ConstructionSite cannot advance: WorldTimeManager is not assigned or available. GameObject: {gameObject.name}.");
                missingWorldTimeManagerWarningLogged = true;
            }

            return false;
        }

        if (simulationBalanceConfig == null)
        {
            if (!missingBalanceConfigWarningLogged)
            {
                Debug.LogWarning($"ConstructionSite cannot advance: SimulationBalanceConfig is not assigned. GameObject: {gameObject.name}.");
                missingBalanceConfigWarningLogged = true;
            }

            return false;
        }

        return true;
    }

    private float GetRequiredWorkWorldHours()
    {
        if (finishedBuildingPrefab.GetComponentInChildren<House>(true) != null)
            return simulationBalanceConfig.HouseBuildDurationWorldHours;

        if (finishedBuildingPrefab.GetComponentInChildren<ResourceStorage>(true) != null)
            return simulationBalanceConfig.BarnBuildDurationWorldHours;

        if (!unknownFinishedBuildingWarningLogged)
        {
            Debug.LogWarning($"ConstructionSite could not identify finished building type for {finishedBuildingPrefab.name}. Falling back to house construction duration.");
            unknownFinishedBuildingWarningLogged = true;
        }

        return simulationBalanceConfig.HouseBuildDurationWorldHours;
    }

    private void AutoResolveWorldTimeManager()
    {
        if (worldTimeManager != null)
            return;

        worldTimeManager = FindFirstObjectByType<WorldTimeManager>();
    }

    private void FinishConstruction(CharacterIdentity completingWorker)
    {
        if (IsComplete)
            return;

        IsComplete = true;
        Debug.Log($"Construction completed: {gameObject.name}.");
        activeWorkers.Clear();
        GameObject finishedBuilding = Instantiate(
            finishedBuildingPrefab,
            transform.position,
            transform.rotation,
            transform.parent
        );
        DisableOwnNavigationBlockers();

        SimulationEventLogger.LogWorldChange(
            "ConstructionCompleted",
            completingWorker,
            $"Construction site {gameObject.name} completed as {finishedBuilding.name}.",
            finishedBuilding.name
        );

        Destroy(gameObject);
    }

    private void DisableOwnNavigationBlockers()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider siteCollider in colliders)
        {
            siteCollider.enabled = false;
        }

        NavMeshObstacle[] obstacles = GetComponentsInChildren<NavMeshObstacle>();
        foreach (NavMeshObstacle obstacle in obstacles)
        {
            obstacle.enabled = false;
        }
    }

    private Bounds GetConstructionBounds()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
            return new Bounds(transform.position, Vector3.zero);

        Bounds bounds = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++)
        {
            bounds.Encapsulate(colliders[i].bounds);
        }

        return bounds;
    }

    private Vector3[] CreateInteractionCandidates(Bounds bounds)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        Vector3[] directions =
        {
            Vector3.forward,
            Vector3.back,
            Vector3.left,
            Vector3.right,
            (Vector3.forward + Vector3.left).normalized,
            (Vector3.forward + Vector3.right).normalized,
            (Vector3.back + Vector3.left).normalized,
            (Vector3.back + Vector3.right).normalized
        };

        Vector3[] candidates = new Vector3[directions.Length];
        for (int i = 0; i < directions.Length; i++)
        {
            Vector3 direction = directions[i];
            Vector3 edgeOffset = Vector3.Scale(direction, extents);
            candidates[i] = new Vector3(
                center.x + edgeOffset.x + direction.x * FallbackInteractionOffset,
                transform.position.y,
                center.z + edgeOffset.z + direction.z * FallbackInteractionOffset
            );
        }

        return candidates;
    }

    private Vector3 GetFallbackInteractionPosition(Bounds bounds, Vector3 builderPosition)
    {
        Vector3 direction = new Vector3(
            builderPosition.x - bounds.center.x,
            0f,
            builderPosition.z - bounds.center.z
        ).normalized;

        if (direction.sqrMagnitude < 0.001f)
            direction = Vector3.back;

        Vector3 edgePosition = bounds.center + Vector3.Scale(direction, bounds.extents);
        edgePosition += direction * FallbackInteractionOffset;
        return new Vector3(edgePosition.x, transform.position.y, edgePosition.z);
    }

    private bool IsInsideHorizontalBounds(Vector3 position, Bounds bounds)
    {
        return position.x > bounds.min.x
            && position.x < bounds.max.x
            && position.z > bounds.min.z
            && position.z < bounds.max.z;
    }
}
