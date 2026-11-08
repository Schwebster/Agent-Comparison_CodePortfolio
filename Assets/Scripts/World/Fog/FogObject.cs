/*
===============================================================================
Script:
FogObject

Purpose:
Represents one tile-sized fog overlay that can be reserved and revealed by NPCs.

Folder:
Assets/Scripts/World/Fog/

Scene Hierarchy:
_World
- Fog
  - FogObject

Attach To:
Square cloud overlay object centered on one grid tile

Required Children:
- CloudVisual child assigned to cloudVisualRoot

Dependencies:
- FogRegistry
- CharacterIdentity
- ResourceNode

Automatically Resolved:
- ResourceNode objects inside the tile bounds

Inspector References:
- tileSize
- interactionRadius
- navMeshSampleDistance
- cloudVisualRoot
===============================================================================
*/

using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class FogObject : MonoBehaviour
{
    private const float AdjacentTileInteractionTolerance = 0.1f;

    [Header("Tile")]
    [Tooltip("World-space width and depth of this fog tile. Match the grid tile size used for one tree or berry bush.")]
    [SerializeField] private float tileSize = 2f;

    [Tooltip("Distance from the tile center where an NPC should stand to explore this fog tile.")]
    [SerializeField] private float interactionRadius = 1.5f;

    [Tooltip("Maximum NavMesh sampling distance used to find the NPC interaction position.")]
    [SerializeField] private float navMeshSampleDistance = 2f;

    [Header("Visual")]
    [Tooltip("Existing child object that contains the fog/cloud Renderer. Optional colliders on this child are disabled on reveal; no collider is required.")]
    [SerializeField] private GameObject cloudVisualRoot;

    [Header("Runtime State")]
    [SerializeField] private bool isRevealed;
    [SerializeField] private CharacterIdentity reservedBy;
    [SerializeField] private float revealProgressWorldHours;
    [SerializeField] private bool logReservationChanges;

    private readonly HashSet<CharacterIdentity> revealWorkers = new HashSet<CharacterIdentity>();
    private readonly HashSet<CharacterIdentity> revealContributorsThisFrame = new HashSet<CharacterIdentity>();
    private readonly List<ResourceNode> coveredResources = new List<ResourceNode>(1);
    private Renderer[] cloudRenderers = new Renderer[0];
    private Collider[] cloudColliders = new Collider[0];
    private GameObject cachedCloudVisualRoot;
    private bool coveredResourcesInitialized;
    private bool usesExplicitCoveredResources;
    [SerializeField] private bool hasGridCoordinate;
    [SerializeField] private Vector2Int gridCoordinate;
    private readonly List<Vector3> reusableInteractionPositions = new List<Vector3>(8);
    private bool missingCloudVisualWarningLogged;
    private int revealContributionFrame = -1;

    public bool IsRevealed => isRevealed;
    public bool IsReserved => reservedBy != null;
    public bool IsAvailable => isActiveAndEnabled && !isRevealed;
    public bool IsAvailableForReservation => IsAvailable && !IsReserved;
    public CharacterIdentity ReservedBy => reservedBy;
    public float TileSize => Mathf.Max(0.1f, tileSize);
    public float RevealProgressWorldHours => Mathf.Max(0f, revealProgressWorldHours);
    public bool HasGridCoordinate => hasGridCoordinate;
    public Vector2Int GridCoordinate => gridCoordinate;

    public void SetTileSize(float worldTileSize)
    {
        tileSize = Mathf.Max(0.1f, worldTileSize);
        coveredResourcesInitialized = usesExplicitCoveredResources;
    }

    public void SetCoveredResource(ResourceNode resource)
    {
        coveredResources.Clear();

        if (resource != null)
            coveredResources.Add(resource);

        usesExplicitCoveredResources = true;
        coveredResourcesInitialized = true;
        SetCoveredResourcesDiscovered(isRevealed);
    }

    public void SetGridCoordinate(int x, int z)
    {
        gridCoordinate = new Vector2Int(x, z);
        hasGridCoordinate = true;
        FogRegistry.NotifyFogAvailabilityChanged(this);
    }

    private void Awake()
    {
        CacheCloudVisualComponents();
        ApplyRevealState();
    }

    private void OnEnable()
    {
        FogRegistry.Register(this);
        ApplyRevealState();
    }

    private void Start()
    {
        SetCoveredResourcesDiscovered(isRevealed);
    }

    private void OnDisable()
    {
        ForceReleaseReservation();
        FogRegistry.Unregister(this);
    }

    private void OnValidate()
    {
        tileSize = Mathf.Max(0.1f, tileSize);
        interactionRadius = Mathf.Max(0.1f, interactionRadius);
        navMeshSampleDistance = Mathf.Max(0.1f, navMeshSampleDistance);
    }

    public bool TryGetInteractionPosition(Vector3 requesterPosition, out Vector3 interactionPosition)
    {
        if (!TryGetInteractionPositions(requesterPosition, reusableInteractionPositions) ||
            reusableInteractionPositions.Count <= 0)
        {
            interactionPosition = transform.position;
            return false;
        }

        interactionPosition = reusableInteractionPositions[0];
        return true;
    }

    public bool IsWithinRevealRange(Vector3 workerPosition)
    {
        Vector3 offset = workerPosition - transform.position;
        offset.y = 0f;
        float maximumDistance = GetMaximumAdjacentRevealDistance();
        return offset.sqrMagnitude <= maximumDistance * maximumDistance;
    }

    public bool TryGetInteractionPositions(Vector3 requesterPosition, List<Vector3> interactionPositions)
    {
        Vector3 center = transform.position;
        Vector3 direction = requesterPosition - center;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
            direction = -transform.forward;

        float radius = Mathf.Max(interactionRadius, tileSize * 0.5f + 0.25f);
        float maximumAdjacentDistance = GetMaximumAdjacentRevealDistance();
        Vector3 requesterDirection = direction.normalized;

        if (interactionPositions == null)
            return false;

        interactionPositions.Clear();

        AddSampledInteractionPosition(center + requesterDirection * radius, center, maximumAdjacentDistance, requesterPosition, interactionPositions);

        AddSampledInteractionPosition(center + Vector3.forward * radius, center, maximumAdjacentDistance, requesterPosition, interactionPositions);
        AddSampledInteractionPosition(center + Vector3.back * radius, center, maximumAdjacentDistance, requesterPosition, interactionPositions);
        AddSampledInteractionPosition(center + Vector3.right * radius, center, maximumAdjacentDistance, requesterPosition, interactionPositions);
        AddSampledInteractionPosition(center + Vector3.left * radius, center, maximumAdjacentDistance, requesterPosition, interactionPositions);

        AddSampledInteractionPosition(center + (Vector3.forward + Vector3.right).normalized * radius, center, maximumAdjacentDistance, requesterPosition, interactionPositions);
        AddSampledInteractionPosition(center + (Vector3.forward + Vector3.left).normalized * radius, center, maximumAdjacentDistance, requesterPosition, interactionPositions);
        AddSampledInteractionPosition(center + (Vector3.back + Vector3.right).normalized * radius, center, maximumAdjacentDistance, requesterPosition, interactionPositions);
        AddSampledInteractionPosition(center + (Vector3.back + Vector3.left).normalized * radius, center, maximumAdjacentDistance, requesterPosition, interactionPositions);

        return interactionPositions.Count > 0;
    }

    private float GetMaximumAdjacentRevealDistance()
    {
        float normalizedTileSize = Mathf.Max(0.1f, tileSize);
        float diagonalNeighborDistance = normalizedTileSize * Mathf.Sqrt(2f);
        float skippedOrthogonalTileDistance = normalizedTileSize * 2f;
        float maximumDistanceBeforeSkipping =
            skippedOrthogonalTileDistance - Mathf.Min(0.01f, normalizedTileSize * 0.01f);

        return Mathf.Min(
            diagonalNeighborDistance + AdjacentTileInteractionTolerance,
            maximumDistanceBeforeSkipping
        );
    }

    private void AddSampledInteractionPosition(
        Vector3 candidate,
        Vector3 tileCenter,
        float maximumAdjacentDistance,
        Vector3 requesterPosition,
        List<Vector3> interactionPositions)
    {
        SimulationPerformanceCounters.AddFogSamplePositionCall();
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
            return;

        Vector3 sampledPosition = hit.position;
        Vector3 centerOffset = sampledPosition - tileCenter;
        centerOffset.y = 0f;
        if (centerOffset.sqrMagnitude > maximumAdjacentDistance * maximumAdjacentDistance)
            return;

        for (int i = 0; i < interactionPositions.Count; i++)
        {
            if ((interactionPositions[i] - sampledPosition).sqrMagnitude <= 0.01f)
                return;
        }

        float sampledDistance = (sampledPosition - requesterPosition).sqrMagnitude;
        int insertIndex = interactionPositions.Count;

        for (int i = 0; i < interactionPositions.Count; i++)
        {
            float existingDistance = (interactionPositions[i] - requesterPosition).sqrMagnitude;
            if (sampledDistance < existingDistance)
            {
                insertIndex = i;
                break;
            }
        }

        interactionPositions.Insert(insertIndex, sampledPosition);
    }

    public bool TryReserve(CharacterIdentity reserver)
    {
        if (!CanReserve(reserver))
            return false;

        if (reservedBy == reserver)
            return true;

        reservedBy = reserver;
        FogRegistry.NotifyFogAvailabilityChanged(this);

        if (logReservationChanges)
            Debug.Log($"Reserved fog {gameObject.name} for {reservedBy.FullName} (ID {reservedBy.CharacterId}).");

        return true;
    }

    public bool ReleaseReservation(CharacterIdentity reserver)
    {
        RemoveMissingReservation();

        if (reserver == null || reservedBy != reserver)
            return false;

        if (logReservationChanges)
            Debug.Log($"Released fog {gameObject.name} for {reservedBy.FullName} (ID {reservedBy.CharacterId}).");

        reservedBy = null;
        FogRegistry.NotifyFogAvailabilityChanged(this);
        return true;
    }

    public void ForceReleaseReservation()
    {
        RemoveMissingReservation();

        if (reservedBy == null)
            return;

        if (logReservationChanges)
            Debug.Log($"Released fog {gameObject.name} because it became unavailable.");

        reservedBy = null;
        FogRegistry.NotifyFogAvailabilityChanged(this);
    }

    public bool IsReservedBy(CharacterIdentity reserver)
    {
        RemoveMissingReservation();
        return reserver != null && reservedBy == reserver;
    }

    public void Reveal(CharacterIdentity revealer = null)
    {
        if (isRevealed)
            return;

        isRevealed = true;
        revealProgressWorldHours = 0f;
        revealWorkers.Clear();
        revealContributorsThisFrame.Clear();
        revealContributionFrame = -1;
        ForceReleaseReservation();
        SetCoveredResourcesDiscovered(true);
        FogRegistry.NotifyFogRevealed(this);
        ApplyRevealState();

        SimulationEventLogger.LogWorldChange(
            "FogRevealed",
            revealer,
            $"Fog cell {gameObject.name} was revealed.",
            gameObject.name
        );
    }

    public bool BeginRevealWork(CharacterIdentity worker)
    {
        if (!IsAvailable)
            return false;

        if (worker != null)
            revealWorkers.Add(worker);

        return true;
    }

    public void EndRevealWork(CharacterIdentity worker)
    {
        if (worker == null)
            return;

        revealWorkers.Remove(worker);
        revealContributorsThisFrame.Remove(worker);
    }

    public bool AdvanceRevealProgress(
        CharacterIdentity worker,
        float deltaWorldHours,
        float requiredWorldHours)
    {
        if (!IsAvailable)
            return isRevealed;

        BeginRevealWork(worker);

        float required = Mathf.Max(0f, requiredWorldHours);
        if (required <= 0f)
        {
            Reveal(worker);
            return true;
        }

        if (!TryRegisterRevealContribution(worker))
            return false;

        revealProgressWorldHours = Mathf.Max(0f, revealProgressWorldHours) +
            Mathf.Max(0f, deltaWorldHours);
        SimulationPerformanceCounters.AddExploreCooperativeRevealContribution();

        if (revealProgressWorldHours < required)
            return false;

        Reveal(worker);
        return true;
    }

    private bool CanReserve(CharacterIdentity reserver)
    {
        RemoveMissingReservation();

        if (reserver == null || !IsAvailable)
            return false;

        return reservedBy == null || reservedBy == reserver;
    }

    private bool TryRegisterRevealContribution(CharacterIdentity worker)
    {
        if (revealContributionFrame != Time.frameCount)
        {
            revealContributorsThisFrame.Clear();
            revealContributionFrame = Time.frameCount;
        }

        if (worker == null)
            return true;

        return revealContributorsThisFrame.Add(worker);
    }

    private void ApplyRevealState()
    {
        if (cloudVisualRoot == null)
        {
            LogMissingCloudVisualWarning();
            return;
        }

        CacheCloudVisualComponents();
        bool fogVisible = !isRevealed;

        for (int i = 0; i < cloudRenderers.Length; i++)
            cloudRenderers[i].enabled = fogVisible;

        for (int i = 0; i < cloudColliders.Length; i++)
            cloudColliders[i].enabled = fogVisible;
    }

    private void CacheCloudVisualComponents()
    {
        if (cloudVisualRoot == cachedCloudVisualRoot)
            return;

        cachedCloudVisualRoot = cloudVisualRoot;

        if (cloudVisualRoot == null)
        {
            cloudRenderers = new Renderer[0];
            cloudColliders = new Collider[0];
            return;
        }

        cloudRenderers = cloudVisualRoot.GetComponentsInChildren<Renderer>(true);
        cloudColliders = cloudVisualRoot.GetComponentsInChildren<Collider>(true);
    }

    private void LogMissingCloudVisualWarning()
    {
        if (missingCloudVisualWarningLogged)
            return;

        Debug.LogWarning($"FogObject is missing cloudVisualRoot. Create and assign a child visual object in the FogTile prefab. GameObject: {gameObject.name}.");
        missingCloudVisualWarningLogged = true;
    }

    private void SetCoveredResourcesDiscovered(bool discovered)
    {
        EnsureCoveredResourcesInitialized();

        for (int i = coveredResources.Count - 1; i >= 0; i--)
        {
            ResourceNode resource = coveredResources[i];
            if (resource == null)
            {
                coveredResources.RemoveAt(i);
                continue;
            }

            resource.SetDiscovered(discovered);
        }
    }

    private void EnsureCoveredResourcesInitialized()
    {
        if (coveredResourcesInitialized)
            return;

        ResourceRegistry.GetResourcesWithinBounds(GetTileBounds(), coveredResources);
        coveredResourcesInitialized = true;
    }

    private Bounds GetTileBounds()
    {
        Vector3 size = new Vector3(tileSize, Mathf.Max(100f, tileSize), tileSize);
        return new Bounds(transform.position, size);
    }

    private void RemoveMissingReservation()
    {
        if (reservedBy == null)
            reservedBy = null;

        revealWorkers.RemoveWhere(worker => worker == null);
        revealContributorsThisFrame.RemoveWhere(worker => worker == null);
    }
}
