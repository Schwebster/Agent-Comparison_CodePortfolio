/*
===============================================================================
Script:
Character

Purpose:
Moves a character and executes simple resource, deposit, and construction actions.

Folder:
Assets/Scripts/Characters/

Scene Hierarchy:
_World
- Characters
  - Character

Attach To:
Character prefab root

Required Children:
- Backpack child

Dependencies:
- NavMeshAgent
- CharacterIdentity
- NpcHealth
- NpcAgentRuntime
- Backpack
- ResourceNode
- ResourceStorage
- ConstructionSite
- CharacterControlRegistry
- WorldTimeManager
- SimulationBalanceConfig
- SimulationSpeedCalculator

Automatically Resolved:
- NavMeshAgent on this GameObject
- CharacterIdentity on this GameObject
- NpcHealth on this GameObject
- Backpack in children
- Animator in children
- WorldTimeManager in scene

Inspector References:
- interactionDistance
- animator
- worldTimeManager
- simulationBalanceConfig
===============================================================================
*/

using UnityEngine;
using UnityEngine.AI;

public readonly struct CharacterMovementProgressDiagnostics
{
    public bool HasProgressSample { get; }
    public Vector3 LastProgressPosition { get; }
    public float PlanarDistanceSinceLastSample { get; }
    public float NoProgressWorldHours { get; }
    public float RequiredProgressDistance { get; }

    public CharacterMovementProgressDiagnostics(
        bool hasProgressSample,
        Vector3 lastProgressPosition,
        float planarDistanceSinceLastSample,
        float noProgressWorldHours,
        float requiredProgressDistance)
    {
        HasProgressSample = hasProgressSample;
        LastProgressPosition = lastProgressPosition;
        PlanarDistanceSinceLastSample = planarDistanceSinceLastSample;
        NoProgressWorldHours = noProgressWorldHours;
        RequiredProgressDistance = requiredProgressDistance;
    }
}

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CharacterIdentity))]
public class Character : MonoBehaviour
{
    private const float InstantMovementAcceleration = 100000f;
    private const float DirectMovementAngularSpeed = 100000f;
    private const float MovementProgressDistance = 0.1f;

    [SerializeField] private float interactionDistance = 1.5f;
    [SerializeField] private Animator animator;
    [SerializeField] private WorldTimeManager worldTimeManager;
    [SerializeField] private SimulationBalanceConfig simulationBalanceConfig;
    [SerializeField] private bool logActionStateChanges;

    private NavMeshAgent agent;
    private CharacterIdentity identity;
    private NpcHealth npcHealth;
    private Backpack backpack;

    private ResourceNode targetResource;
    private ResourceNode.ResourceType targetResourceType;
    private Vector3 targetResourcePosition;
    private ResourceStorage targetStorage;
    private Vector3 targetStoragePosition;
    private ConstructionSite targetConstructionSite;
    private Vector3 targetConstructionPosition;

    private bool isHarvesting;
    private bool isDepositing;
    private bool isConstructing;
    private float requiredResourceActionWorldHours;
    private bool missingWorldTimeManagerWarningLogged;
    private bool missingBalanceConfigWarningLogged;
    private bool hasAppliedSpeedState;
    private WorldTimeManager lastSpeedWorldTimeManager;
    private SimulationBalanceConfig lastSpeedBalanceConfig;
    private Animator lastSpeedAnimator;
    private bool lastSpeedPaused;
    private float lastSpeedDayDurationRealSeconds;
    private float lastSpeedReferenceDayDurationRealSeconds;
    private float lastSpeedBalanceReferenceDayDurationRealSeconds;
    private float lastSpeedBaseMovementSpeed;
    private float lastSpeedBaseAnimatorSpeed;
    private float lastHealthSpeedMultiplier;
    private Vector3 lastMovementProgressPosition;
    private float movementNoProgressWorldHours;
    private bool hasMovementProgressSample;

    public int CarriedWood => backpack != null ? backpack.CurrentWood : 0;
    public int CarriedBerries => backpack != null ? backpack.CurrentBerries : 0;
    public bool HasCarriedResources => backpack != null && backpack.HasResources;
    public bool HasGatherEquipment => backpack != null && backpack.HasGatherEquipment;
    public bool HasDepositLoad => HasCarriedResources || HasGatherEquipment;
    public long TotalFoodDeposited { get; private set; }
    public long TotalWoodDeposited { get; private set; }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        identity = GetComponent<CharacterIdentity>();

        if (identity == null)
            identity = gameObject.AddComponent<CharacterIdentity>();

        identity.EnsureAssigned();
        EnsureAgentRuntimeComponent();
        EnsureHealthComponent();
        backpack = GetComponentInChildren<Backpack>(true);
        animator = animator != null ? animator : GetComponentInChildren<Animator>(true);
        ConfigureSimulationFriendlyMovement();
        NpcCollisionSettingsApplier.ApplyToAgent(agent);
        AutoResolveWorldTimeManager();

        Debug.Assert(
            agent != null,
            $"Missing component: NavMeshAgent. Expected location: same GameObject as Character. Script: {nameof(Character)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            backpack != null,
            $"Missing component: Backpack. Expected location: child of Character, including inactive children. Script: {nameof(Character)}. GameObject: {gameObject.name}."
        );
    }

    private void OnDestroy()
    {
        CharacterControlRegistry.Unregister(this);
    }

    private void EnsureAgentRuntimeComponent()
    {
        if (GetComponent<NpcAgentRuntime>() == null)
            gameObject.AddComponent<NpcAgentRuntime>();
    }

    private void EnsureHealthComponent()
    {
        npcHealth = GetComponent<NpcHealth>();

        if (npcHealth == null)
            npcHealth = gameObject.AddComponent<NpcHealth>();

        npcHealth.Configure(simulationBalanceConfig);
    }

    private void Update()
    {
        UpdateWorldTimeScaledSpeed();

        if (isHarvesting)
        {
            UpdateHarvesting();
            return;
        }

        if (isDepositing)
        {
            UpdateDepositing();
            return;
        }

        if (isConstructing)
        {
            UpdateConstructing();
            return;
        }

        if (targetResource != null)
        {
            UpdateMoveToResource();
            return;
        }

        if (targetStorage != null)
        {
            UpdateMoveToStorage();
            return;
        }

        if (targetConstructionSite != null)
        {
            UpdateMoveToConstructionSite();
            return;
        }
    }

    private void UpdateWorldTimeScaledSpeed()
    {
        if (agent == null)
            return;

        if (!TryGetWorldTimeDependencies(out WorldTimeManager manager, out SimulationBalanceConfig balanceConfig))
        {
            hasAppliedSpeedState = false;
            return;
        }

        float healthSpeedMultiplier = npcHealth != null ? npcHealth.MovementSpeedMultiplier : 1f;
        bool isPaused = manager.IsPaused;
        float dayDurationRealSeconds = manager.DayDurationRealSeconds;
        float referenceDayDurationRealSeconds = manager.ReferenceDayDurationRealSeconds;
        float balanceReferenceDayDurationRealSeconds = balanceConfig.ReferenceDayDurationRealSeconds;
        float baseMovementSpeed = balanceConfig.BaseMovementSpeed;
        float baseAnimatorSpeed = balanceConfig.BaseAnimatorSpeed;

        if (hasAppliedSpeedState &&
            lastSpeedWorldTimeManager == manager &&
            lastSpeedBalanceConfig == balanceConfig &&
            lastSpeedAnimator == animator &&
            lastSpeedPaused == isPaused &&
            lastSpeedDayDurationRealSeconds == dayDurationRealSeconds &&
            lastSpeedReferenceDayDurationRealSeconds == referenceDayDurationRealSeconds &&
            lastSpeedBalanceReferenceDayDurationRealSeconds == balanceReferenceDayDurationRealSeconds &&
            lastSpeedBaseMovementSpeed == baseMovementSpeed &&
            lastSpeedBaseAnimatorSpeed == baseAnimatorSpeed &&
            lastHealthSpeedMultiplier == healthSpeedMultiplier)
        {
            return;
        }

        agent.speed = SimulationSpeedCalculator.GetMovementSpeed(manager, balanceConfig) * healthSpeedMultiplier;

        if (animator != null)
            animator.speed = SimulationSpeedCalculator.GetAnimatorSpeed(manager, balanceConfig) * healthSpeedMultiplier;

        hasAppliedSpeedState = true;
        lastSpeedWorldTimeManager = manager;
        lastSpeedBalanceConfig = balanceConfig;
        lastSpeedAnimator = animator;
        lastSpeedPaused = isPaused;
        lastSpeedDayDurationRealSeconds = dayDurationRealSeconds;
        lastSpeedReferenceDayDurationRealSeconds = referenceDayDurationRealSeconds;
        lastSpeedBalanceReferenceDayDurationRealSeconds = balanceReferenceDayDurationRealSeconds;
        lastSpeedBaseMovementSpeed = baseMovementSpeed;
        lastSpeedBaseAnimatorSpeed = baseAnimatorSpeed;
        lastHealthSpeedMultiplier = healthSpeedMultiplier;
    }

    private void ConfigureSimulationFriendlyMovement()
    {
        if (agent == null)
            return;

        agent.acceleration = InstantMovementAcceleration;
        agent.angularSpeed = DirectMovementAngularSpeed;
        agent.autoBraking = true;
    }

    private bool TryGetWorldTimeDependencies(out WorldTimeManager manager, out SimulationBalanceConfig balanceConfig)
    {
        if (worldTimeManager == null)
            AutoResolveWorldTimeManager();

        manager = worldTimeManager;
        balanceConfig = simulationBalanceConfig;

        if (manager == null)
        {
            if (!missingWorldTimeManagerWarningLogged)
            {
                Debug.LogWarning($"Character cannot update world-time actions: WorldTimeManager is missing. GameObject: {gameObject.name}.");
                missingWorldTimeManagerWarningLogged = true;
            }

            return false;
        }

        if (balanceConfig == null)
        {
            if (!missingBalanceConfigWarningLogged)
            {
                Debug.LogWarning($"Character cannot update world-time actions: SimulationBalanceConfig is not assigned. GameObject: {gameObject.name}.");
                missingBalanceConfigWarningLogged = true;
            }

            return false;
        }

        return true;
    }

    private void AutoResolveWorldTimeManager()
    {
        if (worldTimeManager != null)
            return;

        worldTimeManager = FindFirstObjectByType<WorldTimeManager>();
    }

    public void MoveTo(Vector3 position)
    {
        TryMoveTo(position);
    }

    public bool TryMoveTo(Vector3 position)
    {
        if (agent == null || !agent.isOnNavMesh)
            return false;

        ClearTargets();
        if (!agent.SetDestination(position))
            return false;

        ResetMovementProgressTracking();
        return true;
    }

    public void StopMovement()
    {
        if (agent != null && agent.isOnNavMesh)
            agent.ResetPath();

        ClearTargets();
    }

    public void Harvest(ResourceNode resource)
    {
        TryHarvest(resource);
    }

    public bool TryHarvest(ResourceNode resource)
    {
        if (resource == null)
            return false;

        Vector3 harvestPosition = resource.transform.position;
        if (resource.TryGetInteractionPosition(transform.position, out Vector3 interactionPosition))
            harvestPosition = interactionPosition;

        return TryHarvest(resource, harvestPosition);
    }

    public bool TryHarvest(ResourceNode resource, Vector3 interactionPosition)
    {
        if (resource == null || !resource.CanHarvest)
            return false;

        if (!TryGetWorldTimeDependencies(out _, out _))
            return false;

        if (backpack != null && !CanCarryResource(resource))
        {
            if (logActionStateChanges)
                Debug.Log("Backpack is full.");

            return false;
        }

        if (agent == null || !agent.isOnNavMesh)
            return false;

        ClearTargets();
        if (!agent.SetDestination(interactionPosition))
            return false;

        targetResource = resource;
        targetResourceType = resource.CurrentHarvestType;
        targetResourcePosition = interactionPosition;
        ResetMovementProgressTracking();
        return true;
    }

    public bool HasReachedInteractionTarget(Vector3 targetPosition)
    {
        return HasReachedTarget(targetPosition);
    }

    public bool HasMovementPathFailed()
    {
        if (agent == null || !agent.isOnNavMesh)
            return true;

        if (agent.pathPending)
            return false;

        return agent.pathStatus == NavMeshPathStatus.PathInvalid ||
            agent.pathStatus == NavMeshPathStatus.PathPartial;
    }

    public bool HasMovementPathInvalid()
    {
        if (agent == null || !agent.isOnNavMesh)
            return true;

        if (agent.pathPending)
            return false;

        return agent.pathStatus == NavMeshPathStatus.PathInvalid;
    }

    public bool IsMovementPathPending()
    {
        return agent != null && agent.isOnNavMesh && agent.pathPending;
    }

    public bool HasMovementStalled(float maximumNoProgressWorldHours)
    {
        if (maximumNoProgressWorldHours <= 0f ||
            agent == null ||
            !agent.isOnNavMesh ||
            agent.pathPending)
        {
            return false;
        }

        if (!hasMovementProgressSample)
        {
            ResetMovementProgressTracking();
            return false;
        }

        Vector3 movementOffset = transform.position - lastMovementProgressPosition;
        movementOffset.y = 0f;
        if (movementOffset.sqrMagnitude > MovementProgressDistance * MovementProgressDistance)
        {
            ResetMovementProgressTracking();
            return false;
        }

        if (worldTimeManager == null)
            AutoResolveWorldTimeManager();

        if (worldTimeManager == null)
            return false;

        movementNoProgressWorldHours += Mathf.Max(0f, worldTimeManager.DeltaWorldHours);
        return movementNoProgressWorldHours >= maximumNoProgressWorldHours;
    }

    public CharacterMovementProgressDiagnostics GetMovementProgressDiagnostics()
    {
        Vector3 movementOffset = transform.position - lastMovementProgressPosition;
        movementOffset.y = 0f;

        return new CharacterMovementProgressDiagnostics(
            hasMovementProgressSample,
            lastMovementProgressPosition,
            movementOffset.magnitude,
            movementNoProgressWorldHours,
            MovementProgressDistance
        );
    }

    public bool IsWorkingOnResource(ResourceNode resource)
    {
        return resource != null && targetResource == resource;
    }

    public void Deposit(ResourceStorage storage)
    {
        TryDeposit(storage);
    }

    public bool TryDeposit(ResourceStorage storage)
    {
        if (storage == null)
            return false;

        if (backpack == null || !HasDepositLoad)
        {
            if (logActionStateChanges)
                Debug.Log("No resources or gather equipment to deposit.");

            return false;
        }

        if (agent == null || !agent.isOnNavMesh)
            return false;

        int requesterId = identity != null ? identity.CharacterId : 0;
        if (!storage.TryGetInteractionPosition(
            transform.position,
            requesterId,
            out Vector3 interactionPosition))
        {
            return false;
        }

        ClearTargets();
        targetStorage = storage;
        targetStoragePosition = interactionPosition;
        return agent.SetDestination(targetStoragePosition);
    }

    public bool TryCompleteDeposit(ResourceStorage storage)
    {
        if (storage == null || backpack == null)
            return false;

        if (!HasDepositLoad)
            return true;

        int wood = backpack.RemoveAllWood();
        int berries = backpack.RemoveAllBerries();

        storage.AddWood(wood);
        storage.AddBerries(berries);
        TotalFoodDeposited += berries;
        TotalWoodDeposited += wood;
        backpack.ClearGatherEquipment();

        SimulationEventLogger.LogWorldChange(
            "ResourcesDeposited",
            identity,
            $"Citizen deposited {wood} wood and {berries} food at {storage.gameObject.name}.",
            storage.gameObject.name,
            "WoodAndFood",
            wood,
            berries
        );

        return true;
    }

    public bool IsWorkingOnStorage(ResourceStorage storage)
    {
        return storage != null && targetStorage == storage;
    }

    public bool IsFullForResource(ResourceNode.ResourceType resourceType)
    {
        return backpack != null && backpack.IsFullForGathering(resourceType);
    }

    public bool CanCarryResourceType(ResourceNode.ResourceType resourceType, int amount = 1)
    {
        if (backpack == null)
            return false;

        switch (resourceType)
        {
            case ResourceNode.ResourceType.Berries:
                return backpack.CanAddForGathering(resourceType, amount);
            default:
                return backpack.CanAddForGathering(resourceType, amount);
        }
    }

    public bool HasCorrectGatherEquipment(ResourceNode.ResourceType resourceType)
    {
        return backpack != null && backpack.HasCorrectGatherEquipment(resourceType);
    }

    public bool TryEquipForGathering(ResourceNode.ResourceType resourceType)
    {
        if (backpack == null)
            return false;

        backpack.EquipForGathering(resourceType);
        return backpack.HasCorrectGatherEquipment(resourceType);
    }

    public void EquipForGathering(ResourceNode.ResourceType resourceType)
    {
        TryEquipForGathering(resourceType);
    }

    public void ShowBuildTool()
    {
        if (backpack != null)
            backpack.ShowBuildTool();
    }

    public void ShowExploreTool()
    {
        if (backpack != null)
            backpack.ShowExploreTool();
    }

    public void ClearJobTools()
    {
        if (backpack != null)
            backpack.ClearJobTools();
    }

    public void Construct(ConstructionSite constructionSite)
    {
        TryConstruct(constructionSite);
    }

    public bool TryConstruct(ConstructionSite constructionSite)
    {
        if (constructionSite == null)
            return false;

        if (constructionSite.IsComplete || !constructionSite.CanAcceptWork())
            return false;

        if (agent == null || !agent.isOnNavMesh)
            return false;

        Vector3 constructionPosition = constructionSite.GetInteractionPosition(transform.position);

        ClearTargets();
        if (!agent.SetDestination(constructionPosition))
            return false;

        targetConstructionSite = constructionSite;
        targetConstructionPosition = constructionPosition;
        ResetMovementProgressTracking();
        return true;
    }

    public bool IsWorkingOnConstructionSite(ConstructionSite constructionSite)
    {
        return constructionSite != null && targetConstructionSite == constructionSite;
    }

    public bool IsActivelyConstructing(ConstructionSite constructionSite)
    {
        return constructionSite != null &&
            targetConstructionSite == constructionSite &&
            isConstructing;
    }

    private void UpdateMoveToResource()
    {
        if (targetResource == null || !targetResource.CanHarvest)
        {
            ClearTargets();
            return;
        }

        if (HasReachedTarget(targetResourcePosition))
        {
            agent.ResetPath();

            if (!targetResource.BeginHarvestWork(identity))
            {
                ClearTargets();
                return;
            }

            targetResourceType = targetResource.CurrentHarvestType;
            isHarvesting = true;
            requiredResourceActionWorldHours = GetResourceActionDurationWorldHours(targetResource);

            if (logActionStateChanges)
                Debug.Log("Started harvesting " + GetResourceName(targetResourceType) + ".");
        }
    }

    private void UpdateHarvesting()
    {
        if (!TryGetWorldTimeDependencies(out WorldTimeManager manager, out _))
            return;

        if (targetResource == null || !targetResource.CanHarvest)
        {
            ClearTargets();
            return;
        }

        if (!targetResource.AdvanceHarvestProgress(identity, manager.DeltaWorldHours, requiredResourceActionWorldHours))
            return;

        isHarvesting = false;

        if (targetResource != null && targetResource.CanHarvest)
        {
            targetResourceType = targetResource.CurrentHarvestType;
            int harvested = targetResource.HarvestOne(identity);

            if (backpack != null && harvested > 0)
                AddHarvestedResource(targetResourceType, harvested);
        }

        ClearTargets();
    }

    private void UpdateMoveToStorage()
    {
        if (targetStorage == null)
        {
            ClearTargets();
            return;
        }

        if (targetStorage.IsInInteractionRange(transform.position) ||
            HasReachedTarget(targetStoragePosition))
        {
            ResourceStorage storage = targetStorage;

            if (agent != null && agent.isOnNavMesh)
                agent.ResetPath();

            TryCompleteDeposit(storage);
            ClearTargets();
        }
    }

    private void UpdateDepositing()
    {
        isDepositing = false;

        if (targetStorage != null)
            TryCompleteDeposit(targetStorage);

        ClearTargets();
    }

    private void UpdateMoveToConstructionSite()
    {
        if (targetConstructionSite == null)
        {
            ClearTargets();
            return;
        }

        if (HasReachedTarget(targetConstructionPosition))
        {
            agent.ResetPath();
            isConstructing = true;

            if (logActionStateChanges)
                Debug.Log($"Construction started: {targetConstructionSite.gameObject.name}.");
        }
    }

    private void UpdateConstructing()
    {
        if (targetConstructionSite == null)
        {
            ClearTargets();
            return;
        }

        if (!targetConstructionSite.CanAcceptWork())
        {
            ClearTargets();
            return;
        }

        targetConstructionSite.Work(identity);

        if (targetConstructionSite == null || targetConstructionSite.IsComplete)
            ClearTargets();
    }

    private bool CanCarryResource(ResourceNode resource)
    {
        if (backpack == null || resource == null)
            return false;

        switch (resource.CurrentHarvestType)
        {
            case ResourceNode.ResourceType.Berries:
                return backpack.CanAddBerries(resource.HarvestAmount);
            default:
                return backpack.CanAddWood(resource.HarvestAmount);
        }
    }

    private float GetResourceActionDurationWorldHours(ResourceNode resource)
    {
        if (resource == null || simulationBalanceConfig == null)
            return 0f;

        if (resource.CurrentHarvestType == ResourceNode.ResourceType.Berries)
            return simulationBalanceConfig.BerryGatherDurationWorldHours;

        if (resource.BaseResourceType == ResourceNode.ResourceType.Berries)
            return simulationBalanceConfig.BushHarvestDurationWorldHours;

        return simulationBalanceConfig.TreeChopDurationWorldHours;
    }

    private void AddHarvestedResource(ResourceNode.ResourceType resourceType, int amount)
    {
        if (backpack == null)
            return;

        switch (resourceType)
        {
            case ResourceNode.ResourceType.Berries:
                backpack.AddBerries(amount);
                break;
            default:
                backpack.AddWood(amount);
                break;
        }
    }

    private string GetResourceName(ResourceNode.ResourceType resourceType)
    {
        switch (resourceType)
        {
            case ResourceNode.ResourceType.Berries:
                return "berries";
            default:
                return "wood";
        }
    }

    private bool HasReachedTarget(Vector3 targetPosition)
    {
        if (agent == null || !agent.isOnNavMesh)
            return false;

        if (agent.pathPending)
            return false;

        Vector3 targetOffset = targetPosition - transform.position;
        targetOffset.y = 0f;
        bool isWithinTargetDistance =
            targetOffset.sqrMagnitude <= interactionDistance * interactionDistance;

        if (!agent.hasPath)
            return isWithinTargetDistance;

        if (agent.pathStatus != NavMeshPathStatus.PathComplete)
            return false;

        if (agent.remainingDistance == Mathf.Infinity)
            return false;

        return agent.remainingDistance <= interactionDistance && isWithinTargetDistance;
    }

    private void ResetMovementProgressTracking()
    {
        lastMovementProgressPosition = transform.position;
        movementNoProgressWorldHours = 0f;
        hasMovementProgressSample = true;
    }

    private void ClearTargets()
    {
        if (targetResource != null && identity != null)
            targetResource.EndHarvestWork(identity);

        if (targetConstructionSite != null && identity != null)
            targetConstructionSite.UnregisterWorker(identity);

        targetResource = null;
        targetResourceType = ResourceNode.ResourceType.Wood;
        targetResourcePosition = Vector3.zero;
        targetStorage = null;
        targetStoragePosition = Vector3.zero;
        targetConstructionSite = null;
        targetConstructionPosition = Vector3.zero;
        isHarvesting = false;
        isDepositing = false;
        isConstructing = false;
        requiredResourceActionWorldHours = 0f;
        movementNoProgressWorldHours = 0f;
        hasMovementProgressSample = false;
    }
}
