/*
===============================================================================
Script:
ResourceNode

Purpose:
Represents a harvestable wood or berry resource and updates its visuals.

Folder:
Assets/Scripts/Resources/

Scene Hierarchy:
_World
- Nature
  - Resource prefab instance

Attach To:
Tree or BerryBush prefab root

Required Children:
- Tree visual children for wood resources
- Berry visual children for berry resources

Dependencies:
- NavMeshModifier
- NavMeshObstacle
- Collider
- Rigidbody
- CharacterController
- CharacterIdentity
- ResourceRegistry
- FogObject

Automatically Resolved:
- NavMeshModifier on this GameObject
- Berry visual child GameObjects for berry resources

Inspector References:
- Resource type and amount
- interactionRadius
- navMeshSampleDistance
- Tree visual GameObjects
- Sapling visual GameObject
- Stump visual GameObject
===============================================================================
*/
using UnityEngine;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine.AI;

public class ResourceNode : MonoBehaviour
{
    private const int TreeWoodYield = 5;
    private const int BerryBushFoodYield = 15;

    public enum ResourceType
    {
        Wood,
        Berries
    }

    [Header("Resource")]
    [SerializeField] private ResourceType resourceType = ResourceType.Wood;
    [SerializeField] private int amount = 1;
    [SerializeField] private int treeHarvestYield = TreeWoodYield;
    [SerializeField] private int berryHarvestYield = BerryBushFoodYield;
    [SerializeField] private float interactionRadius = 1.5f;
    [SerializeField] private float navMeshSampleDistance = 2f;
    [SerializeField] private float harvestProgressWorldHours;
    [SerializeField] private bool isDiscovered = true;

    [Header("Visual States")]
    [SerializeField] private GameObject treeVisual;
    [SerializeField] private GameObject saplingVisual;
    [SerializeField] private GameObject stumpVisual;
    [SerializeField] private CharacterIdentity reservedBy;
    [SerializeField] private bool logReservationChanges;
    [SerializeField] private bool logResourceVisualChanges;

    public bool HasResources => amount > 0;
    public bool CanHarvest => amount > 0;
    public bool IsDiscovered => isDiscovered;
    public bool IsAvailable => isActiveAndEnabled && isDiscovered && CanHarvest;
    public bool IsReserved
    {
        get
        {
            RemoveMissingWorkers();
            return activeWorkers.Count > 0;
        }
    }
    public bool IsAvailableForReservation => IsAvailable && (!IsReserved || AllowsCooperativeWorkers);
    public CharacterIdentity ReservedBy => reservedBy;
    public int ActiveWorkerCount
    {
        get
        {
            RemoveMissingWorkers();
            return activeWorkers.Count;
        }
    }
    public int HarvestAmount => GetCurrentHarvestYield();
    public float HarvestProgressWorldHours => Mathf.Max(0f, harvestProgressWorldHours);
    public ResourceType BaseResourceType => resourceType;
    public ResourceType CurrentHarvestType => resourceType;
    public string DisplayName => resourceType == ResourceType.Berries ? "Berry Bush" : "Tree";
    public string EmptyDisplayName => resourceType == ResourceType.Berries ? "Empty Berry Bush" : "Tree Stump";

    private readonly HashSet<CharacterIdentity> activeWorkers = new HashSet<CharacterIdentity>();
    private readonly HashSet<CharacterIdentity> harvestingWorkers = new HashSet<CharacterIdentity>();
    private GameObject[] berryVisuals;
    private CharacterIdentity rewardOwner;
    private float cachedInteractionRadius;
    private bool interactionRadiusCached;
    private int lastHarvestProgressFrame = -1;
    private bool AllowsCooperativeWorkers => resourceType == ResourceType.Wood;

    private void Awake()
    {
        InitializeResourceAmount();
        ConfigureDynamicResourceNavigation();
        CacheBerryVisuals();
        CacheInteractionRadius();
        AssertBerryVisuals();
    }

    private void OnEnable()
    {
        ResourceRegistry.Register(this);
    }

    private void OnDisable()
    {
        ForceReleaseReservation();
        ResourceRegistry.Unregister(this);
    }

    private void Start()
    {
        UpdateVisuals();
    }

    private void OnValidate()
    {
        treeHarvestYield = Mathf.Max(TreeWoodYield, treeHarvestYield);
        berryHarvestYield = Mathf.Max(BerryBushFoodYield, berryHarvestYield);
        harvestProgressWorldHours = Mathf.Max(0f, harvestProgressWorldHours);
        interactionRadiusCached = false;
    }

    public bool BeginHarvestWork(CharacterIdentity worker)
    {
        if (!CanHarvest)
            return false;

        if (worker == null)
            return true;

        if (IsReserved && !IsReservedBy(worker))
            return false;

        if (!IsReserved)
        {
            activeWorkers.Add(worker);
            reservedBy = worker;
        }

        harvestingWorkers.Add(worker);

        if (rewardOwner == null)
            rewardOwner = worker;

        return true;
    }

    public void EndHarvestWork(CharacterIdentity worker)
    {
        if (worker == null)
            return;

        harvestingWorkers.Remove(worker);
    }

    public bool AdvanceHarvestProgress(CharacterIdentity worker, float deltaWorldHours, float requiredWorldHours)
    {
        if (!CanHarvest)
            return false;

        float required = Mathf.Max(0f, requiredWorldHours);
        if (required <= 0f)
            return CanClaimHarvestReward(worker);

        if (worker != null && !harvestingWorkers.Contains(worker) && !BeginHarvestWork(worker))
            return false;

        if (lastHarvestProgressFrame != Time.frameCount)
        {
            int workerCount = Mathf.Max(1, harvestingWorkers.Count);
            harvestProgressWorldHours = Mathf.Max(0f, harvestProgressWorldHours) +
                Mathf.Max(0f, deltaWorldHours) * workerCount;
            lastHarvestProgressFrame = Time.frameCount;
        }

        return harvestProgressWorldHours >= required && CanClaimHarvestReward(worker);
    }

    public void ResetHarvestProgress()
    {
        harvestProgressWorldHours = 0f;
        lastHarvestProgressFrame = -1;
    }

    public void SetDiscovered(bool discovered)
    {
        if (isDiscovered == discovered)
        {
            ApplyDiscoveryVisualState();
            return;
        }

        isDiscovered = discovered;

        if (!isDiscovered)
            ForceReleaseReservation();

        ApplyDiscoveryVisualState();
        ResourceRegistry.NotifyResourceStateChanged(this);
    }

    public int HarvestOne(CharacterIdentity harvester)
    {
        if (!CanHarvest || !CanClaimHarvestReward(harvester))
            return 0;

        ResourceType harvestedType = CurrentHarvestType;
        int harvestedAmount = harvestedType == ResourceType.Berries
            ? HarvestBerries()
            : HarvestWood();

        ResetHarvestProgress();
        UpdateVisuals();
        ReleaseReservationIfUnavailable();
        ResourceRegistry.NotifyResourceStateChanged(this);

        SimulationEventLogger.LogWorldChange(
            "ResourceHarvested",
            harvester,
            $"Citizen harvested {harvestedAmount} {harvestedType} from {gameObject.name}.",
            gameObject.name,
            harvestedType.ToString(),
            harvestedAmount
        );

        return harvestedAmount;
    }

    public int HarvestOne()
    {
        return HarvestOne(rewardOwner);
    }

    public bool TryGetInteractionPosition(Vector3 requesterPosition, out Vector3 interactionPosition)
    {
        Vector3 center = transform.position;
        Vector3 direction = requesterPosition - center;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
            direction = -transform.forward;

        Vector3 candidate = center + direction.normalized * GetInteractionRadius();

        SimulationPerformanceCounters.AddResourceSamplePositionCall();
        if (NavMesh.SamplePosition(candidate, out NavMeshHit candidateHit, GetNavMeshSampleDistance(), NavMesh.AllAreas))
        {
            interactionPosition = candidateHit.position;
            return true;
        }

        SimulationPerformanceCounters.AddResourceSamplePositionCall();
        if (NavMesh.SamplePosition(center, out NavMeshHit centerHit, GetNavMeshSampleDistance(), NavMesh.AllAreas))
        {
            interactionPosition = centerHit.position;
            return true;
        }

        interactionPosition = center;
        return false;
    }

    public bool TryReserve(CharacterIdentity reserver)
    {
        if (!CanReserve(reserver))
            return false;

        if (activeWorkers.Contains(reserver))
            return true;

        activeWorkers.Add(reserver);

        if (reservedBy == null)
            reservedBy = reserver;

        ResourceRegistry.NotifyResourceReservationChanged(this);

        if (logReservationChanges)
            Debug.Log($"Reserved resource {gameObject.name} for {reserver.FullName} (ID {reserver.CharacterId}).");

        return true;
    }

    public bool ReleaseReservation(CharacterIdentity reserver)
    {
        RemoveMissingWorkers();

        if (reserver == null || !activeWorkers.Remove(reserver))
            return false;

        harvestingWorkers.Remove(reserver);

        if (reservedBy == reserver)
            reservedBy = GetDeterministicWorker(activeWorkers);

        if (rewardOwner == reserver)
            rewardOwner = GetDeterministicWorker(harvestingWorkers) ?? GetDeterministicWorker(activeWorkers);

        if (logReservationChanges)
            Debug.Log($"Released resource {gameObject.name} for {reserver.FullName} (ID {reserver.CharacterId}).");

        ResourceRegistry.NotifyResourceReservationChanged(this);
        return true;
    }

    public void ForceReleaseReservation()
    {
        RemoveMissingWorkers();

        if (activeWorkers.Count == 0 && reservedBy == null && rewardOwner == null)
            return;

        if (logReservationChanges)
            Debug.Log($"Released resource {gameObject.name} because it became unavailable.");

        reservedBy = null;
        rewardOwner = null;
        activeWorkers.Clear();
        harvestingWorkers.Clear();
        ResourceRegistry.NotifyResourceReservationChanged(this);
    }

    public bool IsReservedBy(CharacterIdentity reserver)
    {
        RemoveMissingWorkers();
        return reserver != null && activeWorkers.Contains(reserver);
    }

    private bool CanReserve(CharacterIdentity reserver)
    {
        RemoveMissingWorkers();

        if (reserver == null || !IsAvailable)
            return false;

        return activeWorkers.Count == 0 || activeWorkers.Contains(reserver) || AllowsCooperativeWorkers;
    }

    private bool CanClaimHarvestReward(CharacterIdentity worker)
    {
        RemoveMissingWorkers();

        if (worker == null)
            return rewardOwner == null;

        if (rewardOwner == null)
            rewardOwner = worker;

        return rewardOwner == worker;
    }

    private float GetInteractionRadius()
    {
        if (!interactionRadiusCached)
            CacheInteractionRadius();

        return cachedInteractionRadius;
    }

    private void CacheInteractionRadius()
    {
        float radius = Mathf.Max(0.1f, interactionRadius);
        Collider[] colliders = GetComponentsInChildren<Collider>(true);

        foreach (Collider resourceCollider in colliders)
        {
            if (resourceCollider == null || !resourceCollider.enabled || resourceCollider.isTrigger)
                continue;

            Vector3 extents = resourceCollider.bounds.extents;
            radius = Mathf.Max(radius, Mathf.Max(extents.x, extents.z) + 0.5f);
        }

        cachedInteractionRadius = radius;
        interactionRadiusCached = true;
    }

    private float GetNavMeshSampleDistance()
    {
        return Mathf.Max(0.1f, navMeshSampleDistance);
    }

    private void InitializeResourceAmount()
    {
        if (resourceType == ResourceType.Berries && amount > 0)
            amount = 1;
    }

    private void ReleaseReservationIfUnavailable()
    {
        if (!IsAvailable)
            ForceReleaseReservation();
    }

    private void RemoveMissingWorkers()
    {
        if (reservedBy == null)
            reservedBy = null;

        if (rewardOwner == null)
            rewardOwner = null;

        activeWorkers.RemoveWhere(worker => worker == null);
        harvestingWorkers.RemoveWhere(worker => worker == null || !activeWorkers.Contains(worker));
    }

    private CharacterIdentity GetDeterministicWorker(HashSet<CharacterIdentity> workers)
    {
        CharacterIdentity selected = null;

        foreach (CharacterIdentity worker in workers)
        {
            if (worker == null)
                continue;

            if (selected == null || worker.CharacterId < selected.CharacterId)
                selected = worker;
        }

        return selected;
    }

    private void AssertBerryVisuals()
    {
        if (resourceType == ResourceType.Berries)
        {
            Debug.Assert(
                berryVisuals.Length > 0,
                $"Missing child GameObjects named Berry. Expected location: recursive children of berry ResourceNode. Script: {nameof(ResourceNode)}. GameObject: {gameObject.name}."
            );
        }
    }

    private int HarvestBerries()
    {
        amount = 0;
        return Mathf.Max(BerryBushFoodYield, berryHarvestYield);
    }

    private int HarvestWood()
    {
        amount--;
        return Mathf.Max(TreeWoodYield, treeHarvestYield);
    }

    private int GetCurrentHarvestYield()
    {
        if (!CanHarvest)
            return 0;

        return resourceType == ResourceType.Berries
            ? Mathf.Max(BerryBushFoodYield, berryHarvestYield)
            : Mathf.Max(TreeWoodYield, treeHarvestYield);
    }

    private void UpdateVisuals()
    {
        if (resourceType == ResourceType.Berries)
        {
            UpdateBerryResourceVisuals();
            ApplyDiscoveryVisualState();
            return;
        }

        UpdateWoodResourceVisuals();
        ApplyDiscoveryVisualState();
    }

    private void UpdateBerryResourceVisuals()
    {
        UpdateBerryVisuals();

        if (amount <= 0)
        {
            // Release the building footprint and unregister immediately; destruction is deferred.
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        SetStumpVisual(false);
        EnableActiveResourceBlockers();
    }

    private void UpdateWoodResourceVisuals()
    {
        if (treeVisual != null)
            treeVisual.SetActive(amount > 0);

        if (saplingVisual != null)
            saplingVisual.SetActive(false);

        if (amount <= 0)
        {
            ConvertToStump();
            return;
        }

        SetStumpVisual(false);
        EnableActiveResourceBlockers();
    }

    private void UpdateBerryVisuals()
    {
        if (berryVisuals == null)
            CacheBerryVisuals();

        foreach (GameObject berryVisual in berryVisuals)
        {
            if (berryVisual != null)
                berryVisual.SetActive(amount > 0);
        }
    }

    private void CacheBerryVisuals()
    {
        List<GameObject> foundVisuals = new List<GameObject>();
        Transform[] children = GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child == transform)
                continue;

            if (child.name == "Berry")
                foundVisuals.Add(child.gameObject);
        }

        berryVisuals = foundVisuals.ToArray();
    }

    private void DisableMovementBlockers()
    {
        DisableResourceColliders();
        DisableResourceNavMeshObstacles();
        DisableResourceCharacterControllers();
        DisableResourceRigidbodies();
        EnableStumpTriggerColliders();
    }

    private void DisableResourceColliders()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        foreach (Collider resourceCollider in colliders)
        {
            bool wasEnabled = resourceCollider.enabled;
            resourceCollider.enabled = false;

            if (logResourceVisualChanges)
            {
                Debug.Log(
                    $"ResourceNode stump collider disabled. Resource: {gameObject.name}. Object: {resourceCollider.gameObject.name}. Type: {resourceCollider.GetType().Name}. Before: {wasEnabled}. After: {resourceCollider.enabled}."
                );
            }
        }
    }

    private void DisableResourceNavMeshObstacles()
    {
        NavMeshObstacle[] obstacles = GetComponentsInChildren<NavMeshObstacle>(true);
        foreach (NavMeshObstacle obstacle in obstacles)
        {
            bool wasEnabled = obstacle.enabled;
            obstacle.enabled = false;

            if (logResourceVisualChanges)
            {
                Debug.Log(
                    $"ResourceNode stump NavMeshObstacle disabled. Resource: {gameObject.name}. Object: {obstacle.gameObject.name}. Before: {wasEnabled}. After: {obstacle.enabled}."
                );
            }
        }
    }

    private void DisableResourceCharacterControllers()
    {
        CharacterController[] characterControllers = GetComponentsInChildren<CharacterController>(true);
        foreach (CharacterController characterController in characterControllers)
        {
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;

            if (logResourceVisualChanges)
            {
                Debug.Log(
                    $"ResourceNode stump CharacterController disabled. Resource: {gameObject.name}. Object: {characterController.gameObject.name}. Before: {wasEnabled}. After: {characterController.enabled}."
                );
            }
        }
    }

    private void DisableResourceRigidbodies()
    {
        Rigidbody[] rigidbodies = GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody rigidbody in rigidbodies)
        {
            if (logResourceVisualChanges)
            {
                Debug.Log(
                    $"ResourceNode stump Rigidbody found. Resource: {gameObject.name}. Object: {rigidbody.gameObject.name}. IsKinematic: {rigidbody.isKinematic}. DetectCollisions before: {rigidbody.detectCollisions}."
                );
            }

            rigidbody.detectCollisions = false;

            if (logResourceVisualChanges)
            {
                Debug.Log(
                    $"ResourceNode stump Rigidbody updated. Resource: {gameObject.name}. Object: {rigidbody.gameObject.name}. DetectCollisions after: {rigidbody.detectCollisions}."
                );
            }
        }
    }

    private void ConvertToStump()
    {
        if (treeVisual != null)
            treeVisual.SetActive(false);

        if (saplingVisual != null)
            saplingVisual.SetActive(false);

        SetStumpVisual(true);

        if (logResourceVisualChanges)
            Debug.Log($"ResourceNode converted to stump: {gameObject.name}.");

        DisableMovementBlockers();
    }

    private void SetStumpVisual(bool isVisible)
    {
        if (stumpVisual != null)
            stumpVisual.SetActive(isVisible);
    }

    private void EnableStumpTriggerColliders()
    {
        if (stumpVisual == null)
            return;

        Collider[] stumpColliders = stumpVisual.GetComponentsInChildren<Collider>(true);
        foreach (Collider stumpCollider in stumpColliders)
        {
            bool wasEnabled = stumpCollider.enabled;
            bool wasTrigger = stumpCollider.isTrigger;
            stumpCollider.enabled = true;
            stumpCollider.isTrigger = true;

            if (logResourceVisualChanges)
            {
                Debug.Log(
                    $"ResourceNode stump trigger collider enabled. Resource: {gameObject.name}. Object: {stumpCollider.gameObject.name}. Type: {stumpCollider.GetType().Name}. Enabled before: {wasEnabled}. Enabled after: {stumpCollider.enabled}. Trigger before: {wasTrigger}. Trigger after: {stumpCollider.isTrigger}."
                );
            }
        }

        NavMeshObstacle[] stumpObstacles = stumpVisual.GetComponentsInChildren<NavMeshObstacle>(true);
        foreach (NavMeshObstacle stumpObstacle in stumpObstacles)
        {
            bool wasEnabled = stumpObstacle.enabled;
            stumpObstacle.enabled = false;

            if (logResourceVisualChanges)
            {
                Debug.Log(
                    $"ResourceNode stump visual NavMeshObstacle disabled. Resource: {gameObject.name}. Object: {stumpObstacle.gameObject.name}. Before: {wasEnabled}. After: {stumpObstacle.enabled}."
                );
            }
        }
    }

    private void ConfigureDynamicResourceNavigation()
    {
        NavMeshModifier modifier = GetComponent<NavMeshModifier>();
        if (modifier == null)
            modifier = gameObject.AddComponent<NavMeshModifier>();

        modifier.ignoreFromBuild = true;
        modifier.applyToChildren = true;
    }

    private void EnableActiveResourceBlockers()
    {
        SetBlockersEnabled(treeVisual, true);
        SetBlockersEnabled(saplingVisual, saplingVisual != null && saplingVisual.activeSelf);

        NavMeshObstacle[] obstacles = GetComponentsInChildren<NavMeshObstacle>(true);
        foreach (NavMeshObstacle obstacle in obstacles)
        {
            obstacle.carving = true;
        }
    }

    private void SetBlockersEnabled(GameObject visualRoot, bool enabled)
    {
        if (visualRoot == null)
            return;

        Collider[] colliders = visualRoot.GetComponentsInChildren<Collider>(true);
        foreach (Collider resourceCollider in colliders)
        {
            resourceCollider.enabled = enabled;
            resourceCollider.isTrigger = false;
        }

        NavMeshObstacle[] obstacles = visualRoot.GetComponentsInChildren<NavMeshObstacle>(true);
        foreach (NavMeshObstacle obstacle in obstacles)
        {
            obstacle.enabled = enabled;
            obstacle.carving = true;
        }
    }

    private void ApplyDiscoveryVisualState()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer resourceRenderer in renderers)
        {
            if (resourceRenderer != null)
                resourceRenderer.enabled = isDiscovered;
        }
    }
}
