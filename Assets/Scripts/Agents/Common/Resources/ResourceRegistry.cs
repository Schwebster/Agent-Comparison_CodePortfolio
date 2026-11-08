/*
===============================================================================
Script:
ResourceRegistry

Purpose:
Keeps a runtime registry of resource nodes for shared NPC resource lookup and reservation.

Folder:
Assets/Scripts/Agents/Common/Resources/

Scene Hierarchy:
None

Attach To:
None

Required Children:
None

Dependencies:
- ResourceNode
- CharacterIdentity

Automatically Resolved:
Resource nodes register themselves from ResourceNode.OnEnable and ResourceNode.OnDisable.

Inspector References:
None
===============================================================================
*/

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public static class ResourceRegistry
{
    private const float DistanceTieEpsilon = 0.0001f;
    private const float RequesterNavMeshSampleDistance = 2f;
    private const float TemporaryUnreachableDurationWorldHours = 0.12f;
    private const float UnreachableStartCellSize = 6f;
    private const int MaxReachabilityCandidates = 25;
    private const int MaxSharedCandidateOrderCacheEntries = 64;

    private static readonly List<ResourceNode> resources = new List<ResourceNode>();
    private static readonly HashSet<ResourceNode> registeredResources = new HashSet<ResourceNode>();
    private static readonly HashSet<ResourceNode> knownTrees = new HashSet<ResourceNode>();
    private static readonly HashSet<ResourceNode> knownBerryBushes = new HashSet<ResourceNode>();
    private static readonly HashSet<ResourceNode> knownEmptyBerryBushes = new HashSet<ResourceNode>();
    private static readonly HashSet<ResourceNode> availableKnownTrees = new HashSet<ResourceNode>();
    private static readonly HashSet<ResourceNode> availableKnownBerryBushes = new HashSet<ResourceNode>();
    private static readonly HashSet<ResourceNode> availableKnownEmptyBerryBushes = new HashSet<ResourceNode>();
    private static readonly List<ResourceCandidate> nearestCandidates = new List<ResourceCandidate>(MaxReachabilityCandidates);
    private static readonly Dictionary<ResourceSearchKey, List<ResourceCandidate>> sharedCandidateOrderCache =
        new Dictionary<ResourceSearchKey, List<ResourceCandidate>>();
    private static readonly Dictionary<ResourceUnreachableKey, float> temporarilyUnreachableUntil = new Dictionary<ResourceUnreachableKey, float>();
    private static readonly List<ResourceUnreachableKey> unreachableKeysToRemove = new List<ResourceUnreachableKey>();
    private static readonly NavMeshPath sharedPath = new NavMeshPath();
    private static WorldTimeManager worldTimeManager;

    private struct ResourceCandidate
    {
        public ResourceNode Resource;
        public float Distance;
    }

    private struct ResourceSearchKey : IEquatable<ResourceSearchKey>
    {
        public ResourceNode.ResourceType ResourceType;
        public Vector3 RequesterPosition;
        public bool IncludeEmptyBerryBushes;

        public bool Equals(ResourceSearchKey other)
        {
            return ResourceType == other.ResourceType &&
                RequesterPosition.Equals(other.RequesterPosition) &&
                IncludeEmptyBerryBushes == other.IncludeEmptyBerryBushes;
        }

        public override bool Equals(object obj)
        {
            return obj is ResourceSearchKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hash = (int)ResourceType;
            hash = (hash * 397) ^ RequesterPosition.GetHashCode();
            hash = (hash * 397) ^ IncludeEmptyBerryBushes.GetHashCode();
            return hash;
        }
    }

    private struct ResourceUnreachableKey : IEquatable<ResourceUnreachableKey>
    {
        public ResourceNode Resource;
        public Vector2Int StartCell;

        public bool Equals(ResourceUnreachableKey other)
        {
            return Resource == other.Resource && StartCell == other.StartCell;
        }

        public override bool Equals(object obj)
        {
            return obj is ResourceUnreachableKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            int resourceHash = Resource != null ? Resource.GetInstanceID() : 0;
            return (resourceHash * 397) ^ StartCell.GetHashCode();
        }
    }

    public static int RegisteredResourceCount => resources.Count;

    public static bool HasKnownFoodResource()
    {
        return knownBerryBushes.Count > 0;
    }

    public static bool HasKnownWoodResource(bool includeEmptyBerryBushes)
    {
        return knownTrees.Count > 0 ||
            (includeEmptyBerryBushes && knownEmptyBerryBushes.Count > 0);
    }

    public static bool HasAvailableResource(ResourceNode.ResourceType resourceType)
    {
        return GetCachedCandidateCount(resourceType) > 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        resources.Clear();
        registeredResources.Clear();
        knownTrees.Clear();
        knownBerryBushes.Clear();
        knownEmptyBerryBushes.Clear();
        availableKnownTrees.Clear();
        availableKnownBerryBushes.Clear();
        availableKnownEmptyBerryBushes.Clear();
        nearestCandidates.Clear();
        sharedCandidateOrderCache.Clear();
        temporarilyUnreachableUntil.Clear();
        unreachableKeysToRemove.Clear();
        worldTimeManager = null;
    }

    public static void Register(ResourceNode resource)
    {
        if (resource == null || !registeredResources.Add(resource))
            return;

        resources.Add(resource);
        sharedCandidateOrderCache.Clear();
        UpdateKnownCandidate(resource);
    }

    public static void Unregister(ResourceNode resource)
    {
        if (resource == null)
            return;

        registeredResources.Remove(resource);
        resources.Remove(resource);
        sharedCandidateOrderCache.Clear();
        RemoveTemporaryUnreachableEntries(resource);
        RemoveKnownCandidate(resource);
    }

    public static void ResetKnownResourceCandidates()
    {
        knownTrees.Clear();
        knownBerryBushes.Clear();
        knownEmptyBerryBushes.Clear();
        availableKnownTrees.Clear();
        availableKnownBerryBushes.Clear();
        availableKnownEmptyBerryBushes.Clear();
        temporarilyUnreachableUntil.Clear();
        unreachableKeysToRemove.Clear();
        worldTimeManager = null;

        PruneMissingResources();

        for (int i = 0; i < resources.Count; i++)
            UpdateKnownCandidate(resources[i]);
    }

    public static void NotifyResourceStateChanged(ResourceNode resource)
    {
        if (resource == null)
            return;

        if (!registeredResources.Contains(resource))
            return;

        RemoveTemporaryUnreachableEntries(resource);
        UpdateKnownCandidate(resource);
    }

    public static void NotifyResourceReservationChanged(ResourceNode resource)
    {
        if (resource == null || !registeredResources.Contains(resource))
            return;

        UpdateKnownCandidate(resource);
    }

    public static bool TryGetClosestAvailableResource(
        ResourceNode.ResourceType resourceType,
        Vector3 requesterPosition,
        out ResourceNode resource)
    {
        SimulationPerformanceCounters.AddResourceQuery();
        RecordResourceCacheQuery(resourceType);
        resource = FindClosestAvailableResource(resourceType, requesterPosition);
        return resource != null;
    }

    public static bool TryGetClosestReachableAvailableResource(
        ResourceNode.ResourceType resourceType,
        Vector3 requesterPosition,
        out ResourceNode resource)
    {
        SimulationPerformanceCounters.AddResourceQuery();
        RecordResourceCacheQuery(resourceType);
        resource = FindClosestReachableResource(resourceType, requesterPosition, out _);
        return resource != null;
    }

    public static void GetKnownResourceCounts(
        out int trees,
        out int berryBushes,
        out int emptyBerryBushes)
    {
        PruneMissingKnownResources();

        trees = knownTrees.Count;
        berryBushes = knownBerryBushes.Count;
        emptyBerryBushes = knownEmptyBerryBushes.Count;
    }

    public static void GetResourcesWithinBounds(Bounds bounds, List<ResourceNode> results)
    {
        if (results == null)
            return;

        results.Clear();
        PruneMissingResources();

        for (int i = 0; i < resources.Count; i++)
        {
            ResourceNode resource = resources[i];
            if (resource == null)
                continue;

            if (bounds.Contains(resource.transform.position))
                results.Add(resource);
        }
    }

    public static bool TryReserveClosestResource(
        ResourceNode.ResourceType resourceType,
        Vector3 requesterPosition,
        CharacterIdentity reserver,
        out ResourceNode reservedResource)
    {
        return TryReserveClosestReachableResource(
            resourceType,
            requesterPosition,
            reserver,
            out reservedResource,
            out _
        );
    }

    public static bool TryReserveClosestReachableResource(
        ResourceNode.ResourceType resourceType,
        Vector3 requesterPosition,
        CharacterIdentity reserver,
        out ResourceNode reservedResource,
        out Vector3 interactionPosition)
    {
        reservedResource = null;
        interactionPosition = requesterPosition;

        if (reserver == null)
            return false;

        SimulationPerformanceCounters.AddResourceQuery();
        RecordResourceCacheQuery(resourceType);

        ResourceNode resource = FindClosestReachableResource(resourceType, requesterPosition, out interactionPosition);
        if (resource == null || !resource.TryReserve(reserver))
        {
            SimulationPerformanceCounters.AddGatherResourceReservationFailure();
            return false;
        }

        reservedResource = resource;
        return true;
    }

    public static void MarkResourceTemporarilyUnreachable(
        ResourceNode resource,
        Vector3 requesterPosition,
        string reason)
    {
        if (!CanMarkTemporarilyUnreachable(resource))
            return;

        MarkTemporarilyUnreachable(resource, requesterPosition);
        SimulationPerformanceCounters.AddResourceMovementFailureMarkedUnreachable();
    }

    private static ResourceNode FindClosestAvailableResource(ResourceNode.ResourceType resourceType, Vector3 requesterPosition)
    {
        if (GetCachedCandidateCount(resourceType) <= 0)
            return null;

        ResourceNode closestResource = null;
        float closestDistance = float.PositiveInfinity;

        HashSet<ResourceNode> source = GetPrimaryAvailableCandidateSet(resourceType);
        closestResource = FindClosestAvailableResourceInSet(source, resourceType, requesterPosition, closestResource, ref closestDistance);

        if (resourceType == ResourceNode.ResourceType.Wood && ManualDayPlanState.AllowCutEmptyBushes)
        {
            closestResource = FindClosestAvailableResourceInSet(
                availableKnownEmptyBerryBushes,
                resourceType,
                requesterPosition,
                closestResource,
                ref closestDistance
            );
        }

        return closestResource;
    }

    private static ResourceNode FindClosestAvailableResourceInSet(
        HashSet<ResourceNode> source,
        ResourceNode.ResourceType resourceType,
        Vector3 requesterPosition,
        ResourceNode currentClosest,
        ref float closestDistance)
    {
        ResourceNode closestResource = currentClosest;

        SimulationPerformanceCounters.AddResourceCandidatesChecked(source.Count);
        foreach (ResourceNode resource in source)
        {
            if (!CanUseResource(resource, resourceType))
            {
                SimulationPerformanceCounters.AddResourcePathCheckSkippedByCheapFilter();
                continue;
            }

            if (IsTemporarilyUnreachable(resource, requesterPosition))
            {
                SimulationPerformanceCounters.AddResourceUnreachableCacheSkip();
                SimulationPerformanceCounters.AddSameTargetRetryPrevented();
                SimulationPerformanceCounters.AddResourcePathCheckSkippedByCheapFilter();
                continue;
            }

            float distance = (resource.transform.position - requesterPosition).sqrMagnitude;
            if (!IsBetterResource(resource, distance, closestResource, closestDistance))
                continue;

            closestResource = resource;
            closestDistance = distance;
        }

        return closestResource;
    }

    private static ResourceNode FindClosestReachableResource(
        ResourceNode.ResourceType resourceType,
        Vector3 requesterPosition,
        out Vector3 closestInteractionPosition)
    {
        closestInteractionPosition = requesterPosition;

        if (GetCachedCandidateCount(resourceType) <= 0)
            return null;

        SimulationPerformanceCounters.AddResourceSamplePositionCall();
        if (!NavMesh.SamplePosition(
            requesterPosition,
            out NavMeshHit requesterHit,
            RequesterNavMeshSampleDistance,
            NavMesh.AllAreas))
        {
            return null;
        }

        SelectNearestResourceCandidates(resourceType, requesterHit.position);

        for (int i = 0; i < nearestCandidates.Count; i++)
        {
            SimulationPerformanceCounters.AddResourceNearestCandidateChecked();
            ResourceNode resource = nearestCandidates[i].Resource;

            if (IsTemporarilyUnreachable(resource, requesterHit.position))
            {
                SimulationPerformanceCounters.AddResourceUnreachableCacheSkip();
                SimulationPerformanceCounters.AddSameTargetRetryPrevented();
                continue;
            }

            if (!resource.TryGetInteractionPosition(requesterHit.position, out Vector3 interactionPosition))
            {
                MarkTemporarilyUnreachable(resource, requesterHit.position);
                continue;
            }

            if (!HasCompletePath(requesterHit.position, interactionPosition))
            {
                MarkTemporarilyUnreachable(resource, requesterHit.position);
                continue;
            }

            closestInteractionPosition = interactionPosition;
            return resource;
        }

        return null;
    }

    private static void SelectNearestResourceCandidates(ResourceNode.ResourceType resourceType, Vector3 requesterPosition)
    {
        nearestCandidates.Clear();

        List<ResourceCandidate> orderedCandidates = GetSharedCandidateOrder(resourceType, requesterPosition);
        SimulationPerformanceCounters.AddResourceCandidatesChecked(orderedCandidates.Count);

        for (int i = 0; i < orderedCandidates.Count; i++)
        {
            ResourceCandidate candidate = orderedCandidates[i];
            ResourceNode resource = candidate.Resource;

            if (!CanUseResource(resource, resourceType))
            {
                SimulationPerformanceCounters.AddResourcePathCheckSkippedByCheapFilter();
                continue;
            }

            if (IsTemporarilyUnreachable(resource, requesterPosition))
            {
                SimulationPerformanceCounters.AddResourceUnreachableCacheSkip();
                SimulationPerformanceCounters.AddSameTargetRetryPrevented();
                SimulationPerformanceCounters.AddResourcePathCheckSkippedByCheapFilter();
                continue;
            }

            nearestCandidates.Add(candidate);
            if (nearestCandidates.Count >= MaxReachabilityCandidates)
                break;
        }
    }

    private static List<ResourceCandidate> GetSharedCandidateOrder(
        ResourceNode.ResourceType resourceType,
        Vector3 requesterPosition)
    {
        ResourceSearchKey key = new ResourceSearchKey
        {
            ResourceType = resourceType,
            RequesterPosition = requesterPosition,
            IncludeEmptyBerryBushes = ManualDayPlanState.AllowCutEmptyBushes
        };

        if (sharedCandidateOrderCache.TryGetValue(key, out List<ResourceCandidate> orderedCandidates))
            return orderedCandidates;

        if (sharedCandidateOrderCache.Count >= MaxSharedCandidateOrderCacheEntries)
            sharedCandidateOrderCache.Clear();

        orderedCandidates = new List<ResourceCandidate>(resources.Count);
        for (int i = 0; i < resources.Count; i++)
        {
            ResourceNode resource = resources[i];
            if (resource == null)
                continue;

            float distance = (resource.transform.position - requesterPosition).sqrMagnitude;
            orderedCandidates.Add(new ResourceCandidate
            {
                Resource = resource,
                Distance = distance
            });
        }

        orderedCandidates.Sort(CompareResourceCandidates);
        sharedCandidateOrderCache[key] = orderedCandidates;
        return orderedCandidates;
    }

    private static int CompareResourceCandidates(ResourceCandidate left, ResourceCandidate right)
    {
        if (left.Distance < right.Distance - DistanceTieEpsilon)
            return -1;

        if (left.Distance > right.Distance + DistanceTieEpsilon)
            return 1;

        return CompareResourceOrder(left.Resource, right.Resource);
    }

    private static HashSet<ResourceNode> GetPrimaryAvailableCandidateSet(ResourceNode.ResourceType resourceType)
    {
        return resourceType == ResourceNode.ResourceType.Berries ? availableKnownBerryBushes : availableKnownTrees;
    }

    private static void RecordResourceCacheQuery(ResourceNode.ResourceType resourceType)
    {
        int candidateCount = GetCachedCandidateCount(resourceType);
        SimulationPerformanceCounters.AddResourceCandidatesFromCache(candidateCount);
        SimulationPerformanceCounters.AddResourceFullScanAvoided();

        if (candidateCount > 0)
            SimulationPerformanceCounters.AddResourceCacheHit();
        else
            SimulationPerformanceCounters.AddResourceCacheMiss();
    }

    private static int GetCachedCandidateCount(ResourceNode.ResourceType resourceType)
    {
        int count = GetPrimaryAvailableCandidateSet(resourceType).Count;

        if (resourceType == ResourceNode.ResourceType.Wood && ManualDayPlanState.AllowCutEmptyBushes)
            count += availableKnownEmptyBerryBushes.Count;

        return count;
    }

    private static void UpdateKnownCandidate(ResourceNode resource)
    {
        RemoveKnownCandidate(resource);

        if (resource == null || !resource.isActiveAndEnabled || !resource.IsDiscovered)
            return;

        if (resource.BaseResourceType == ResourceNode.ResourceType.Wood)
        {
            if (resource.HasResources)
            {
                knownTrees.Add(resource);
                if (resource.IsAvailableForReservation)
                    availableKnownTrees.Add(resource);
            }

            return;
        }

        if (resource.HasResources)
        {
            knownBerryBushes.Add(resource);
            if (resource.IsAvailableForReservation)
                availableKnownBerryBushes.Add(resource);

            return;
        }

        if (resource.IsAvailable)
        {
            knownEmptyBerryBushes.Add(resource);
            if (resource.IsAvailableForReservation)
                availableKnownEmptyBerryBushes.Add(resource);
        }
    }

    private static void RemoveKnownCandidate(ResourceNode resource)
    {
        knownTrees.Remove(resource);
        knownBerryBushes.Remove(resource);
        knownEmptyBerryBushes.Remove(resource);
        availableKnownTrees.Remove(resource);
        availableKnownBerryBushes.Remove(resource);
        availableKnownEmptyBerryBushes.Remove(resource);
    }

    private static bool IsTemporarilyUnreachable(ResourceNode resource, Vector3 requesterPosition)
    {
        if (resource == null)
            return false;

        ResourceUnreachableKey key = CreateUnreachableKey(resource, requesterPosition);
        if (!temporarilyUnreachableUntil.TryGetValue(key, out float expiresAt))
            return false;

        if (!TryGetCurrentTotalWorldHours(out float currentTotalWorldHours))
        {
            temporarilyUnreachableUntil.Remove(key);
            return false;
        }

        if (currentTotalWorldHours < expiresAt)
            return true;

        temporarilyUnreachableUntil.Remove(key);
        return false;
    }

    private static void MarkTemporarilyUnreachable(ResourceNode resource, Vector3 requesterPosition)
    {
        if (resource == null)
            return;

        if (!TryGetCurrentTotalWorldHours(out float currentTotalWorldHours))
            return;

        ResourceUnreachableKey key = CreateUnreachableKey(resource, requesterPosition);
        temporarilyUnreachableUntil[key] = currentTotalWorldHours + TemporaryUnreachableDurationWorldHours;
    }

    private static bool TryGetCurrentTotalWorldHours(out float totalWorldHours)
    {
        if (worldTimeManager == null)
        {
            worldTimeManager = UnityEngine.Object.FindFirstObjectByType<WorldTimeManager>(
                FindObjectsInactive.Include
            );
        }

        if (worldTimeManager == null)
        {
            totalWorldHours = 0f;
            return false;
        }

        totalWorldHours = worldTimeManager.TotalWorldHours;
        return true;
    }

    private static bool CanMarkTemporarilyUnreachable(ResourceNode resource)
    {
        return resource != null &&
            resource.isActiveAndEnabled &&
            resource.IsAvailable;
    }

    private static ResourceUnreachableKey CreateUnreachableKey(ResourceNode resource, Vector3 requesterPosition)
    {
        return new ResourceUnreachableKey
        {
            Resource = resource,
            StartCell = GetCoarseStartCell(requesterPosition)
        };
    }

    private static Vector2Int GetCoarseStartCell(Vector3 position)
    {
        float cellSize = Mathf.Max(0.1f, UnreachableStartCellSize);
        return new Vector2Int(
            Mathf.FloorToInt(position.x / cellSize),
            Mathf.FloorToInt(position.z / cellSize)
        );
    }

    private static void RemoveTemporaryUnreachableEntries(ResourceNode resource)
    {
        if (resource == null || temporarilyUnreachableUntil.Count <= 0)
            return;

        unreachableKeysToRemove.Clear();
        foreach (ResourceUnreachableKey key in temporarilyUnreachableUntil.Keys)
        {
            if (key.Resource == resource)
                unreachableKeysToRemove.Add(key);
        }

        for (int i = 0; i < unreachableKeysToRemove.Count; i++)
            temporarilyUnreachableUntil.Remove(unreachableKeysToRemove[i]);

        unreachableKeysToRemove.Clear();
    }

    private static void InsertNearestCandidate(ResourceNode resource, float distance)
    {
        ResourceCandidate candidate = new ResourceCandidate
        {
            Resource = resource,
            Distance = distance
        };

        int insertIndex = nearestCandidates.Count;
        for (int i = 0; i < nearestCandidates.Count; i++)
        {
            ResourceCandidate current = nearestCandidates[i];
            if (distance < current.Distance - DistanceTieEpsilon ||
                (Mathf.Abs(distance - current.Distance) <= DistanceTieEpsilon &&
                    CompareResourceOrder(resource, current.Resource) < 0))
            {
                insertIndex = i;
                break;
            }
        }

        if (insertIndex >= MaxReachabilityCandidates)
            return;

        nearestCandidates.Insert(insertIndex, candidate);

        if (nearestCandidates.Count > MaxReachabilityCandidates)
            nearestCandidates.RemoveAt(nearestCandidates.Count - 1);
    }

    private static bool CanUseResource(ResourceNode resource, ResourceNode.ResourceType resourceType)
    {
        if (resource == null ||
            !resource.isActiveAndEnabled ||
            !resource.IsAvailableForReservation ||
            resource.CurrentHarvestType != resourceType)
        {
            return false;
        }

        if (resourceType == ResourceNode.ResourceType.Wood &&
            resource.BaseResourceType == ResourceNode.ResourceType.Berries &&
            !ManualDayPlanState.AllowCutEmptyBushes)
        {
            return false;
        }

        return true;
    }

    private static bool IsBetterResource(ResourceNode candidate, float candidateDistance, ResourceNode current, float currentDistance)
    {
        if (current == null)
            return true;

        if (candidateDistance < currentDistance - DistanceTieEpsilon)
            return true;

        if (candidateDistance > currentDistance + DistanceTieEpsilon)
            return false;

        return CompareResourceOrder(candidate, current) < 0;
    }

    private static int CompareResourceOrder(ResourceNode left, ResourceNode right)
    {
        Vector3 leftPosition = left.transform.position;
        Vector3 rightPosition = right.transform.position;

        int xComparison = leftPosition.x.CompareTo(rightPosition.x);
        if (xComparison != 0)
            return xComparison;

        int zComparison = leftPosition.z.CompareTo(rightPosition.z);
        if (zComparison != 0)
            return zComparison;

        int yComparison = leftPosition.y.CompareTo(rightPosition.y);
        if (yComparison != 0)
            return yComparison;

        return string.CompareOrdinal(left.gameObject.name, right.gameObject.name);
    }

    private static bool HasCompletePath(Vector3 startPosition, Vector3 targetPosition)
    {
        SimulationPerformanceCounters.AddResourceCalculatePathCall();
        if (!NavMesh.CalculatePath(startPosition, targetPosition, NavMesh.AllAreas, sharedPath))
            return false;

        return sharedPath.status == NavMeshPathStatus.PathComplete;
    }

    private static void PruneMissingResources()
    {
        for (int i = resources.Count - 1; i >= 0; i--)
        {
            if (resources[i] == null)
            {
                RemoveTemporaryUnreachableEntries(resources[i]);
                registeredResources.Remove(resources[i]);
                resources.RemoveAt(i);
            }
        }
    }

    private static void PruneMissingKnownResources()
    {
        PruneMissingResourceSet(knownTrees);
        PruneMissingResourceSet(knownBerryBushes);
        PruneMissingResourceSet(knownEmptyBerryBushes);
        PruneMissingResourceSet(availableKnownTrees);
        PruneMissingResourceSet(availableKnownBerryBushes);
        PruneMissingResourceSet(availableKnownEmptyBerryBushes);
    }

    private static void PruneMissingResourceSet(HashSet<ResourceNode> source)
    {
        source.RemoveWhere(resource => resource == null || !resource.isActiveAndEnabled || !resource.IsDiscovered);
    }
}
