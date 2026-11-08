/*
===============================================================================
Script:
FogRegistry

Purpose:
Keeps a runtime registry of FogObject tiles for shared exploration lookup and reservation.

Folder:
Assets/Scripts/World/Fog/

Scene Hierarchy:
None

Attach To:
None

Required Children:
None

Dependencies:
- FogObject
- CharacterIdentity

Automatically Resolved:
FogObject instances register themselves from OnEnable and OnDisable.

Inspector References:
None
===============================================================================
*/

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public static class FogRegistry
{
    private const float DistanceTieEpsilon = 0.0001f;
    private const float RequesterNavMeshSampleDistance = 2f;
    private const int MaxReachabilityCandidates = 128;
    private const int MaxSharedCandidateOrderCacheEntries = 32;
    private const int MaxSharedPathCorners = 256;

    private static readonly List<FogObject> fogObjects = new List<FogObject>();
    private static readonly HashSet<FogObject> fogObjectSet = new HashSet<FogObject>();
    private static readonly List<FogObject> frontierFogObjects = new List<FogObject>();
    private static readonly HashSet<FogObject> frontierFogSet = new HashSet<FogObject>();
    private static readonly List<FogObject> availableFogObjects = new List<FogObject>();
    private static readonly HashSet<FogObject> availableFogSet = new HashSet<FogObject>();
    private static readonly List<FogCandidate> nearestCandidates = new List<FogCandidate>(MaxReachabilityCandidates);
    private static readonly Dictionary<FogSearchKey, List<FogCandidate>> sharedCandidateOrderCache =
        new Dictionary<FogSearchKey, List<FogCandidate>>();
    private static readonly List<Vector3> interactionCandidates = new List<Vector3>(9);
    private static readonly Dictionary<Vector2Int, FogObject> fogByCell = new Dictionary<Vector2Int, FogObject>();
    private static readonly HashSet<Vector2Int> knownCells = new HashSet<Vector2Int>();
    private static readonly HashSet<FogUnreachableKey> unreachableFogForPlanningCycle = new HashSet<FogUnreachableKey>();
    private static readonly NavMeshPath sharedPath = new NavMeshPath();
    private static readonly Vector3[] sharedPathCorners = new Vector3[MaxSharedPathCorners];
    private static int lastMissingFogPruneFrame = -1;

    private struct FogCandidate
    {
        public FogObject FogObject;
        public float Distance;
    }

    private struct FogSearchKey : IEquatable<FogSearchKey>
    {
        public Vector3 RequesterPosition;
        public Vector3 DirectionOrigin;
        public ExploreDirection Direction;
        public bool UsesDirection;

        public bool Equals(FogSearchKey other)
        {
            return RequesterPosition.Equals(other.RequesterPosition) &&
                DirectionOrigin.Equals(other.DirectionOrigin) &&
                Direction == other.Direction &&
                UsesDirection == other.UsesDirection;
        }

        public override bool Equals(object obj)
        {
            return obj is FogSearchKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hash = RequesterPosition.GetHashCode();
            hash = (hash * 397) ^ DirectionOrigin.GetHashCode();
            hash = (hash * 397) ^ (int)Direction;
            hash = (hash * 397) ^ UsesDirection.GetHashCode();
            return hash;
        }
    }

    private struct FogUnreachableKey : IEquatable<FogUnreachableKey>
    {
        public FogObject FogObject;
        public int RequesterId;

        public bool Equals(FogUnreachableKey other)
        {
            return FogObject == other.FogObject && RequesterId == other.RequesterId;
        }

        public override bool Equals(object obj)
        {
            return obj is FogUnreachableKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            int fogHash = FogObject != null ? FogObject.GetInstanceID() : 0;
            return (fogHash * 397) ^ RequesterId;
        }
    }

    public static int RegisteredFogCount => fogObjects.Count;
    public static event Action FrontierFogChanged;

    public static bool HasFrontierFog()
    {
        return availableFogObjects.Count > 0;
    }

    public static int GetFrontierFogCount()
    {
        return availableFogObjects.Count;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        fogObjects.Clear();
        fogObjectSet.Clear();
        frontierFogObjects.Clear();
        frontierFogSet.Clear();
        availableFogObjects.Clear();
        availableFogSet.Clear();
        nearestCandidates.Clear();
        sharedCandidateOrderCache.Clear();
        interactionCandidates.Clear();
        fogByCell.Clear();
        knownCells.Clear();
        unreachableFogForPlanningCycle.Clear();
        lastMissingFogPruneFrame = -1;
    }

    public static void Register(FogObject fogObject)
    {
        if (fogObject == null || !fogObjectSet.Add(fogObject))
            return;

        fogObjects.Add(fogObject);
        UpdateAvailableCandidate(fogObject);
    }

    public static void Unregister(FogObject fogObject)
    {
        if (fogObject == null)
            return;

        fogObjectSet.Remove(fogObject);
        fogObjects.Remove(fogObject);
        frontierFogObjects.Remove(fogObject);
        frontierFogSet.Remove(fogObject);
        sharedCandidateOrderCache.Clear();
        RemoveUnreachableEntries(fogObject);
        bool removedAvailable = availableFogSet.Remove(fogObject);
        availableFogObjects.Remove(fogObject);

        if (fogObject.HasGridCoordinate)
            fogByCell.Remove(fogObject.GridCoordinate);

        if (removedAvailable)
            FrontierFogChanged?.Invoke();
    }

    public static void NotifyFogAvailabilityChanged(FogObject fogObject)
    {
        if (fogObject == null)
            return;

        if (!fogObjectSet.Contains(fogObject))
            return;

        UpdateAvailableCandidate(fogObject);
    }

    public static void ResetGridWorldState()
    {
        bool hadAvailableFog = availableFogObjects.Count > 0;

        frontierFogObjects.Clear();
        frontierFogSet.Clear();
        availableFogObjects.Clear();
        availableFogSet.Clear();
        nearestCandidates.Clear();
        sharedCandidateOrderCache.Clear();
        interactionCandidates.Clear();
        fogByCell.Clear();
        knownCells.Clear();
        unreachableFogForPlanningCycle.Clear();
        lastMissingFogPruneFrame = -1;

        for (int i = 0; i < fogObjects.Count; i++)
            UpdateAvailableCandidate(fogObjects[i]);

        if (hadAvailableFog && availableFogObjects.Count <= 0)
            FrontierFogChanged?.Invoke();
    }

    public static void RegisterKnownCell(int x, int z)
    {
        knownCells.Add(new Vector2Int(x, z));
    }

    public static void ConfigureGeneratedFog(FogObject fogObject, int x, int z, bool isInitialFrontier)
    {
        if (fogObject == null)
            return;

        Vector2Int coordinate = new Vector2Int(x, z);
        fogObject.SetGridCoordinate(x, z);
        fogByCell[coordinate] = fogObject;

        if (isInitialFrontier)
            AddFrontierFog(fogObject);
        else
            RemoveFrontierFog(fogObject);

        UpdateAvailableCandidate(fogObject);
    }

    public static void NotifyFogRevealed(FogObject fogObject)
    {
        if (fogObject == null)
            return;

        RemoveFrontierFog(fogObject);
        RemoveUnreachableEntries(fogObject);
        UpdateAvailableCandidate(fogObject);

        if (!fogObject.HasGridCoordinate)
            return;

        Vector2Int coordinate = fogObject.GridCoordinate;
        knownCells.Add(coordinate);
        AddNeighboringFrontierFog(coordinate);
    }

    public static bool TryGetClosestAvailableFog(Vector3 requesterPosition, out FogObject fogObject)
    {
        return TryGetClosestAvailableFog(requesterPosition, out fogObject, out _);
    }

    public static bool TryGetClosestAvailableFog(
        Vector3 requesterPosition,
        out FogObject fogObject,
        out Vector3 interactionPosition)
    {
        return TryGetClosestAvailableFog(requesterPosition, 0, out fogObject, out interactionPosition);
    }

    public static bool TryGetClosestAvailableFog(
        Vector3 requesterPosition,
        int requesterId,
        out FogObject fogObject,
        out Vector3 interactionPosition)
    {
        SimulationPerformanceCounters.AddFogQuery();
        PruneMissingFogObjects();
        fogObject = FindClosestReachableFog(requesterPosition, requesterId, false, out interactionPosition);
        return fogObject != null;
    }

    public static bool TryGetClosestReachableFogIncludingReserved(Vector3 requesterPosition, out FogObject fogObject)
    {
        SimulationPerformanceCounters.AddFogQuery();
        PruneMissingFogObjects();
        fogObject = FindClosestReachableFog(requesterPosition, 0, true, out _);
        return fogObject != null;
    }

    public static bool TryReserveClosestReachableFog(
        Vector3 requesterPosition,
        CharacterIdentity reserver,
        out FogObject reservedFog,
        out Vector3 interactionPosition)
    {
        reservedFog = null;
        interactionPosition = requesterPosition;

        if (reserver == null)
            return false;

        SimulationPerformanceCounters.AddFogQuery();
        PruneMissingFogObjects();

        int requesterId = reserver.CharacterId;
        FogObject fogObject = FindClosestReachableFog(requesterPosition, requesterId, false, out interactionPosition);
        if (fogObject == null || !fogObject.TryReserve(reserver))
            return false;

        reservedFog = fogObject;
        return true;
    }

    public static bool TryReserveClosestReachableFogInDirection(
        Vector3 origin,
        ExploreDirection direction,
        Vector3 requesterPosition,
        CharacterIdentity reserver,
        out FogObject reservedFog,
        out Vector3 interactionPosition)
    {
        reservedFog = null;
        interactionPosition = requesterPosition;

        if (reserver == null)
            return false;

        SimulationPerformanceCounters.AddFogQuery();
        PruneMissingFogObjects();

        FogObject fogObject = FindClosestReachableFogInDirection(
            origin,
            direction,
            requesterPosition,
            reserver.CharacterId,
            out interactionPosition
        );

        if (fogObject == null)
            fogObject = FindClosestReachableFog(requesterPosition, reserver.CharacterId, false, out interactionPosition);

        if (fogObject == null || !fogObject.TryReserve(reserver))
            return false;

        reservedFog = fogObject;
        return true;
    }

    public static bool TryGetClosestReachableFogInDirection(
        Vector3 origin,
        ExploreDirection direction,
        Vector3 requesterPosition,
        out FogObject fogObject,
        out Vector3 interactionPosition)
    {
        return TryGetClosestReachableFogInDirection(
            origin,
            direction,
            requesterPosition,
            0,
            out fogObject,
            out interactionPosition
        );
    }

    public static bool TryGetClosestReachableFogInDirection(
        Vector3 origin,
        ExploreDirection direction,
        Vector3 requesterPosition,
        int requesterId,
        out FogObject fogObject,
        out Vector3 interactionPosition)
    {
        SimulationPerformanceCounters.AddFogQuery();
        PruneMissingFogObjects();

        fogObject = FindClosestReachableFogInDirection(
            origin,
            direction,
            requesterPosition,
            requesterId,
            out interactionPosition
        );

        if (fogObject != null)
            return true;

        SimulationPerformanceCounters.AddExploreDirectionalSearchFailure();
        fogObject = FindClosestReachableFog(requesterPosition, requesterId, false, out interactionPosition);

        if (fogObject == null)
            return false;

        SimulationPerformanceCounters.AddExploreFallbackSearchSuccess();
        return true;
    }

    public static void MarkFogUnreachableForPlanningCycle(
        FogObject fogObject,
        int requesterId)
    {
        if (!CanMarkUnreachable(fogObject))
            return;

        MarkUnreachable(fogObject, requesterId);
        SimulationPerformanceCounters.AddFogMovementFailureMarkedUnreachable();
    }

    public static void ClearUnreachableFogForPlanningCycle()
    {
        unreachableFogForPlanningCycle.Clear();
    }

    public static void ClearUnreachableFogForRequester(int requesterId)
    {
        if (unreachableFogForPlanningCycle.Count <= 0)
            return;

        FogUnreachableKey[] keys = new FogUnreachableKey[unreachableFogForPlanningCycle.Count];
        unreachableFogForPlanningCycle.CopyTo(keys);

        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i].RequesterId == requesterId)
                unreachableFogForPlanningCycle.Remove(keys[i]);
        }
    }

    public static int CountUnrevealedFog()
    {
        PruneMissingFogObjects();

        int count = 0;
        for (int i = 0; i < fogObjects.Count; i++)
        {
            FogObject fogObject = fogObjects[i];
            if (fogObject != null && fogObject.isActiveAndEnabled && !fogObject.IsRevealed)
                count++;
        }

        return count;
    }

    public static bool HasUnrevealedFogInArea(Vector3 center, Vector2 areaSize)
    {
        PruneMissingFogObjects();
        float halfWidth = Mathf.Max(0f, areaSize.x * 0.5f);
        float halfDepth = Mathf.Max(0f, areaSize.y * 0.5f);

        for (int i = 0; i < fogObjects.Count; i++)
        {
            FogObject fogObject = fogObjects[i];
            if (fogObject == null || !fogObject.isActiveAndEnabled || fogObject.IsRevealed)
                continue;

            float fogHalfSize = fogObject.TileSize * 0.5f;
            Vector3 fogPosition = fogObject.transform.position;
            if (Mathf.Abs(fogPosition.x - center.x) < halfWidth + fogHalfSize &&
                Mathf.Abs(fogPosition.z - center.z) < halfDepth + fogHalfSize)
            {
                return true;
            }
        }

        return false;
    }

    public static int CountReachableFrontierFog()
    {
        PruneMissingAvailableFogObjects();
        return availableFogObjects.Count;
    }

    public static int CountFrontierFog(bool includeReserved)
    {
        if (includeReserved)
        {
            PruneMissingFogObjects();
            return frontierFogObjects.Count;
        }

        PruneMissingAvailableFogObjects();
        return availableFogObjects.Count;
    }

    public static bool HasAvailableFrontierFog()
    {
        PruneMissingAvailableFogObjects();
        return availableFogObjects.Count > 0;
    }

    public static int CountPathReachableFrontierFog(Vector3 requesterPosition, bool includeReserved)
    {
        SimulationPerformanceCounters.AddFogQuery();
        SimulationPerformanceCounters.AddFogSamplePositionCall();
        if (!NavMesh.SamplePosition(
            requesterPosition,
            out NavMeshHit requesterHit,
            RequesterNavMeshSampleDistance,
            NavMesh.AllAreas))
        {
            return 0;
        }

        SelectNearestFogCandidates(requesterHit.position, 0, includeReserved);

        int count = 0;
        for (int i = 0; i < nearestCandidates.Count; i++)
        {
            FogObject fogObject = nearestCandidates[i].FogObject;
            if (fogObject == null)
                continue;

            if (IsUnreachable(fogObject, 0))
            {
                SimulationPerformanceCounters.AddFogUnreachableCacheSkip();
                SimulationPerformanceCounters.AddSameTargetRetryPrevented();
                continue;
            }

            if (!TryGetClosestInteractionCandidate(
                fogObject,
                requesterHit.position,
                out _,
                out _))
            {
                MarkUnreachable(fogObject, 0);
                continue;
            }

            count++;
        }

        return count;
    }

    private static FogObject FindClosestReachableFog(
        Vector3 requesterPosition,
        int requesterId,
        bool includeReserved,
        out Vector3 closestInteractionPosition)
    {
        closestInteractionPosition = requesterPosition;

        SimulationPerformanceCounters.AddFogSamplePositionCall();
        if (!NavMesh.SamplePosition(
            requesterPosition,
            out NavMeshHit requesterHit,
            RequesterNavMeshSampleDistance,
            NavMesh.AllAreas))
        {
            return null;
        }

        SelectNearestFogCandidates(requesterHit.position, requesterId, includeReserved);

        for (int i = 0; i < nearestCandidates.Count; i++)
        {
            FogObject fogObject = nearestCandidates[i].FogObject;

            if (IsUnreachable(fogObject, requesterId))
            {
                SimulationPerformanceCounters.AddFogUnreachableCacheSkip();
                SimulationPerformanceCounters.AddSameTargetRetryPrevented();
                continue;
            }

            if (!TryGetClosestInteractionCandidate(
                fogObject,
                requesterHit.position,
                out Vector3 interactionPosition,
                out _))
            {
                MarkUnreachable(fogObject, requesterId);
                continue;
            }

            closestInteractionPosition = interactionPosition;
            return fogObject;
        }

        return null;
    }

    private static FogObject FindClosestReachableFogInDirection(
        Vector3 origin,
        ExploreDirection direction,
        Vector3 requesterPosition,
        int requesterId,
        out Vector3 closestInteractionPosition)
    {
        closestInteractionPosition = requesterPosition;

        SimulationPerformanceCounters.AddFogSamplePositionCall();
        if (!NavMesh.SamplePosition(
            requesterPosition,
            out NavMeshHit requesterHit,
            RequesterNavMeshSampleDistance,
            NavMesh.AllAreas))
        {
            return null;
        }

        SelectNearestFogCandidatesInDirection(origin, direction, requesterHit.position, requesterId);

        for (int i = 0; i < nearestCandidates.Count; i++)
        {
            FogObject fogObject = nearestCandidates[i].FogObject;

            if (IsUnreachable(fogObject, requesterId))
            {
                SimulationPerformanceCounters.AddFogUnreachableCacheSkip();
                SimulationPerformanceCounters.AddSameTargetRetryPrevented();
                continue;
            }

            if (!TryGetClosestInteractionCandidate(
                fogObject,
                requesterHit.position,
                out Vector3 interactionPosition,
                out _))
            {
                MarkUnreachable(fogObject, requesterId);
                continue;
            }

            closestInteractionPosition = interactionPosition;
            return fogObject;
        }

        return null;
    }

    private static void SelectNearestFogCandidates(Vector3 requesterPosition, int requesterId, bool includeReserved)
    {
        nearestCandidates.Clear();
        PruneMissingFogObjects();

        List<FogCandidate> orderedCandidates = GetSharedFogCandidateOrder(
            requesterPosition,
            Vector3.zero,
            ExploreDirection.North,
            false);

        SimulationPerformanceCounters.AddFogCandidatesChecked(orderedCandidates.Count);
        for (int i = 0; i < orderedCandidates.Count; i++)
        {
            FogCandidate candidate = orderedCandidates[i];
            FogObject fogObject = candidate.FogObject;

            if (!CanUseFog(fogObject, includeReserved))
                continue;

            if (IsUnreachable(fogObject, requesterId))
            {
                SimulationPerformanceCounters.AddFogUnreachableCacheSkip();
                SimulationPerformanceCounters.AddSameTargetRetryPrevented();
                continue;
            }

            nearestCandidates.Add(candidate);
            if (nearestCandidates.Count >= MaxReachabilityCandidates)
                break;
        }
    }

    private static void SelectNearestFogCandidatesInDirection(
        Vector3 origin,
        ExploreDirection direction,
        Vector3 requesterPosition,
        int requesterId)
    {
        nearestCandidates.Clear();
        PruneMissingFogObjects();

        List<FogCandidate> orderedCandidates = GetSharedFogCandidateOrder(
            requesterPosition,
            origin,
            direction,
            true);

        SimulationPerformanceCounters.AddFogCandidatesChecked(orderedCandidates.Count);
        for (int i = 0; i < orderedCandidates.Count; i++)
        {
            FogCandidate candidate = orderedCandidates[i];
            FogObject fogObject = candidate.FogObject;

            if (!CanUseFog(fogObject))
                continue;

            if (IsUnreachable(fogObject, requesterId))
            {
                SimulationPerformanceCounters.AddFogUnreachableCacheSkip();
                SimulationPerformanceCounters.AddSameTargetRetryPrevented();
                continue;
            }

            nearestCandidates.Add(candidate);
            if (nearestCandidates.Count >= MaxReachabilityCandidates)
                break;
        }
    }

    private static List<FogCandidate> GetSharedFogCandidateOrder(
        Vector3 requesterPosition,
        Vector3 directionOrigin,
        ExploreDirection direction,
        bool usesDirection)
    {
        FogSearchKey key = new FogSearchKey
        {
            RequesterPosition = requesterPosition,
            DirectionOrigin = directionOrigin,
            Direction = direction,
            UsesDirection = usesDirection
        };

        if (sharedCandidateOrderCache.TryGetValue(key, out List<FogCandidate> orderedCandidates))
            return orderedCandidates;

        if (sharedCandidateOrderCache.Count >= MaxSharedCandidateOrderCacheEntries)
            sharedCandidateOrderCache.Clear();

        orderedCandidates = new List<FogCandidate>(frontierFogObjects.Count);
        for (int i = 0; i < frontierFogObjects.Count; i++)
        {
            FogObject fogObject = frontierFogObjects[i];
            if (fogObject == null)
                continue;

            if (usesDirection &&
                !IsInExploreDirection(fogObject.transform.position, directionOrigin, direction))
            {
                continue;
            }

            orderedCandidates.Add(new FogCandidate
            {
                FogObject = fogObject,
                Distance = (fogObject.transform.position - requesterPosition).sqrMagnitude
            });
        }

        orderedCandidates.Sort(CompareFogCandidates);
        sharedCandidateOrderCache[key] = orderedCandidates;
        return orderedCandidates;
    }

    private static int CompareFogCandidates(FogCandidate left, FogCandidate right)
    {
        if (left.Distance < right.Distance - DistanceTieEpsilon)
            return -1;

        if (left.Distance > right.Distance + DistanceTieEpsilon)
            return 1;

        return CompareFogOrder(left.FogObject, right.FogObject);
    }

    private static bool IsInExploreDirection(Vector3 candidatePosition, Vector3 origin, ExploreDirection direction)
    {
        const float DirectionEpsilon = 0.01f;
        Vector3 offset = candidatePosition - origin;

        switch (direction)
        {
            case ExploreDirection.East:
                return offset.x > DirectionEpsilon;
            case ExploreDirection.South:
                return offset.z < -DirectionEpsilon;
            case ExploreDirection.West:
                return offset.x < -DirectionEpsilon;
            default:
                return offset.z > DirectionEpsilon;
        }
    }

    private static void InsertNearestCandidate(FogObject fogObject, float distance)
    {
        FogCandidate candidate = new FogCandidate
        {
            FogObject = fogObject,
            Distance = distance
        };

        int insertIndex = nearestCandidates.Count;
        for (int i = 0; i < nearestCandidates.Count; i++)
        {
            FogCandidate current = nearestCandidates[i];
            if (distance < current.Distance - DistanceTieEpsilon ||
                (Mathf.Abs(distance - current.Distance) <= DistanceTieEpsilon &&
                    CompareFogOrder(fogObject, current.FogObject) < 0))
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

    private static void UpdateAvailableCandidate(FogObject fogObject)
    {
        bool isTracked = availableFogSet.Contains(fogObject);
        bool shouldTrack = CanUseFog(fogObject);

        if (shouldTrack && !isTracked)
        {
            availableFogSet.Add(fogObject);
            availableFogObjects.Add(fogObject);
            FrontierFogChanged?.Invoke();
            return;
        }

        if (!shouldTrack && isTracked)
        {
            availableFogSet.Remove(fogObject);
            availableFogObjects.Remove(fogObject);
            FrontierFogChanged?.Invoke();
        }
    }

    private static bool CanUseFog(FogObject fogObject)
    {
        return CanUseFog(fogObject, false);
    }

    private static bool CanUseFog(FogObject fogObject, bool includeReserved)
    {
        if (fogObject == null ||
            !fogObject.isActiveAndEnabled ||
            !fogObject.IsAvailable ||
            (!includeReserved && fogObject.IsReserved))
        {
            return false;
        }

        if (!fogObject.HasGridCoordinate)
            return true;

        return frontierFogSet.Contains(fogObject);
    }

    private static void AddNeighboringFrontierFog(Vector2Int coordinate)
    {
        for (int z = coordinate.y - 1; z <= coordinate.y + 1; z++)
        {
            for (int x = coordinate.x - 1; x <= coordinate.x + 1; x++)
            {
                if (x == coordinate.x && z == coordinate.y)
                    continue;

                Vector2Int neighbor = new Vector2Int(x, z);
                if (knownCells.Contains(neighbor))
                    continue;

                if (!fogByCell.TryGetValue(neighbor, out FogObject fogObject))
                    continue;

                AddFrontierFog(fogObject);
            }
        }
    }

    private static void AddFrontierFog(FogObject fogObject)
    {
        if (fogObject == null ||
            !fogObject.isActiveAndEnabled ||
            fogObject.IsRevealed)
        {
            return;
        }

        if (frontierFogSet.Add(fogObject))
        {
            frontierFogObjects.Add(fogObject);
            sharedCandidateOrderCache.Clear();
        }

        UpdateAvailableCandidate(fogObject);
    }

    private static void RemoveFrontierFog(FogObject fogObject)
    {
        bool removedFrontier = frontierFogSet.Remove(fogObject);
        frontierFogObjects.Remove(fogObject);
        if (removedFrontier)
            sharedCandidateOrderCache.Clear();

        bool removedAvailable = availableFogSet.Remove(fogObject);
        availableFogObjects.Remove(fogObject);
        if (removedAvailable)
            FrontierFogChanged?.Invoke();
    }

    private static bool IsUnreachable(FogObject fogObject, int requesterId)
    {
        if (fogObject == null)
            return false;

        return unreachableFogForPlanningCycle.Contains(CreateUnreachableKey(fogObject, requesterId));
    }

    private static void MarkUnreachable(FogObject fogObject, int requesterId)
    {
        if (fogObject == null)
            return;

        unreachableFogForPlanningCycle.Add(CreateUnreachableKey(fogObject, requesterId));
    }

    private static bool CanMarkUnreachable(FogObject fogObject)
    {
        return fogObject != null &&
            fogObject.isActiveAndEnabled &&
            fogObject.IsAvailable &&
            !fogObject.IsRevealed;
    }

    private static FogUnreachableKey CreateUnreachableKey(FogObject fogObject, int requesterId)
    {
        return new FogUnreachableKey
        {
            FogObject = fogObject,
            RequesterId = requesterId
        };
    }

    private static void RemoveUnreachableEntries(FogObject fogObject)
    {
        if (fogObject == null || unreachableFogForPlanningCycle.Count <= 0)
            return;

        FogUnreachableKey[] keys = new FogUnreachableKey[unreachableFogForPlanningCycle.Count];
        unreachableFogForPlanningCycle.CopyTo(keys);

        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i].FogObject == fogObject)
                unreachableFogForPlanningCycle.Remove(keys[i]);
        }
    }

    private static bool IsBetterFog(FogObject candidate, float candidateDistance, FogObject current, float currentDistance)
    {
        if (current == null)
            return true;

        if (candidateDistance < currentDistance - DistanceTieEpsilon)
            return true;

        if (candidateDistance > currentDistance + DistanceTieEpsilon)
            return false;

        return CompareFogOrder(candidate, current) < 0;
    }

    private static int CompareFogOrder(FogObject left, FogObject right)
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

    private static bool TryGetClosestInteractionCandidate(
        FogObject fogObject,
        Vector3 requesterPosition,
        out Vector3 closestInteractionPosition,
        out float closestDistance)
    {
        closestInteractionPosition = requesterPosition;
        closestDistance = float.PositiveInfinity;

        if (fogObject == null ||
            !fogObject.TryGetInteractionPositions(requesterPosition, interactionCandidates))
        {
            return false;
        }

        for (int i = 0; i < interactionCandidates.Count; i++)
        {
            Vector3 candidate = interactionCandidates[i];
            if (!IsKnownWorldPosition(fogObject, candidate))
            {
                SimulationPerformanceCounters.AddFogUnknownCellPathRejection();
                continue;
            }

            if (!HasCompleteKnownCellPath(fogObject, requesterPosition, candidate))
                continue;

            closestInteractionPosition = candidate;
            closestDistance = (candidate - requesterPosition).sqrMagnitude;
            return true;
        }

        return false;
    }

    private static bool HasCompleteKnownCellPath(
        FogObject targetFog,
        Vector3 startPosition,
        Vector3 targetPosition)
    {
        SimulationPerformanceCounters.AddFogCalculatePathCall();

        if (!NavMesh.CalculatePath(startPosition, targetPosition, NavMesh.AllAreas, sharedPath) ||
            sharedPath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        if (targetFog == null || !targetFog.HasGridCoordinate)
            return true;

        SimulationPerformanceCounters.AddFogKnownCellPathCheck();
        int cornerCount = sharedPath.GetCornersNonAlloc(sharedPathCorners);
        if (cornerCount <= 0 || cornerCount >= sharedPathCorners.Length)
        {
            SimulationPerformanceCounters.AddFogUnknownCellPathRejection();
            return false;
        }

        float sampleInterval = targetFog.TileSize * 0.5f;
        for (int cornerIndex = 1; cornerIndex < cornerCount; cornerIndex++)
        {
            if (!IsKnownPathSegment(
                    targetFog,
                    sharedPathCorners[cornerIndex - 1],
                    sharedPathCorners[cornerIndex],
                    sampleInterval))
            {
                SimulationPerformanceCounters.AddFogUnknownCellPathRejection();
                return false;
            }
        }

        return true;
    }

    private static bool IsKnownPathSegment(
        FogObject referenceFog,
        Vector3 segmentStart,
        Vector3 segmentEnd,
        float sampleInterval)
    {
        Vector3 segment = segmentEnd - segmentStart;
        segment.y = 0f;
        int sampleSteps = Mathf.Max(1, Mathf.CeilToInt(segment.magnitude / sampleInterval));

        for (int step = 0; step <= sampleSteps; step++)
        {
            float interpolation = step / (float)sampleSteps;
            Vector3 samplePosition = Vector3.Lerp(segmentStart, segmentEnd, interpolation);
            if (!IsKnownWorldPosition(referenceFog, samplePosition))
                return false;
        }

        return true;
    }

    private static bool IsKnownWorldPosition(FogObject referenceFog, Vector3 worldPosition)
    {
        if (referenceFog == null || !referenceFog.HasGridCoordinate)
            return true;

        float tileSize = referenceFog.TileSize;
        Vector3 referencePosition = referenceFog.transform.position;
        Vector2Int referenceCoordinate = referenceFog.GridCoordinate;
        Vector2Int coordinate = new Vector2Int(
            referenceCoordinate.x + Mathf.RoundToInt((worldPosition.x - referencePosition.x) / tileSize),
            referenceCoordinate.y + Mathf.RoundToInt((worldPosition.z - referencePosition.z) / tileSize)
        );

        return knownCells.Contains(coordinate);
    }

    private static void PruneMissingFogObjects()
    {
        PruneMissingFogCollectionsOncePerFrame();
    }

    private static void PruneMissingAvailableFogObjects()
    {
        PruneMissingFogCollectionsOncePerFrame();
    }

    private static void PruneMissingFogCollectionsOncePerFrame()
    {
        int currentFrame = Time.frameCount;
        if (lastMissingFogPruneFrame == currentFrame)
            return;

        lastMissingFogPruneFrame = currentFrame;

        for (int i = fogObjects.Count - 1; i >= 0; i--)
        {
            FogObject fogObject = fogObjects[i];
            if (fogObject != null)
                continue;

            fogObjectSet.Remove(fogObject);
            frontierFogSet.Remove(fogObject);
            availableFogSet.Remove(fogObject);
            frontierFogObjects.Remove(fogObject);
            availableFogObjects.Remove(fogObject);
            fogObjects.RemoveAt(i);
        }

        for (int i = availableFogObjects.Count - 1; i >= 0; i--)
        {
            FogObject fogObject = availableFogObjects[i];
            if (fogObject != null)
                continue;

            availableFogSet.Remove(fogObject);
            availableFogObjects.RemoveAt(i);
        }

        for (int i = frontierFogObjects.Count - 1; i >= 0; i--)
        {
            FogObject fogObject = frontierFogObjects[i];
            if (fogObject != null)
                continue;

            frontierFogSet.Remove(fogObject);
            frontierFogObjects.RemoveAt(i);
        }
    }
}
