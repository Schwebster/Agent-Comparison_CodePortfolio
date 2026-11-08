/*
===============================================================================
Script:
ResourceStorage

Purpose:
Stores resources deposited by characters.

Folder:
Assets/Scripts/Buildings/

Scene Hierarchy:
_World
- Buildings
  - Barn

Attach To:
Barn

Required Children:
None

Dependencies:
- ResourceStorageRegistry

Automatically Resolved:
None

Inspector References:
- depositPoint
- interactionRadius
- spawnPoint
- npcPrefab
- villagerSpawnFoodCost
- woodStored fallback for scenes that do not provide SimulationBalanceConfig launch settings
- berriesStored fallback for scenes that do not provide SimulationBalanceConfig launch settings
===============================================================================
*/
using UnityEngine;
using UnityEngine.AI;

public class ResourceStorage : MonoBehaviour
{
    private const float RequesterNavMeshSampleDistance = 1f;
    private const float NpcSpawnNavMeshSampleDistance = 1f;
    private const float InteractionPositionRadiusFactor = 0.8f;
    private const int InteractionCandidateCount = 16;

    public struct InteractionPositionDiagnostics
    {
        public bool RequesterSampleFailed { get; private set; }
        public int CandidateAttempts { get; private set; }
        public int CandidateSampleFailures { get; private set; }
        public int CandidateOutsideRangeFailures { get; private set; }
        public int PathCalculationFailures { get; private set; }
        public int PartialPathFailures { get; private set; }
        public int InvalidPathFailures { get; private set; }
        public bool HasPartialPathSample { get; private set; }
        public Vector3 PartialPathEnd { get; private set; }
        public Vector3 PartialPathTarget { get; private set; }

        public string ToSummary()
        {
            if (RequesterSampleFailed)
                return "RequesterSampleFailed";

            if (CandidateAttempts <= 0)
                return "None";

            string summary = $"attempts={CandidateAttempts}/sample={CandidateSampleFailures}/outside={CandidateOutsideRangeFailures}/calculate={PathCalculationFailures}/partial={PartialPathFailures}/invalid={InvalidPathFailures}";
            if (HasPartialPathSample)
            {
                summary += $"/partialEnd={PartialPathEnd.ToString("F2")}/target={PartialPathTarget.ToString("F2")}";
            }

            return summary;
        }

        internal void RecordRequesterSampleFailure()
        {
            RequesterSampleFailed = true;
        }

        internal void RecordCandidateAttempt()
        {
            CandidateAttempts++;
        }

        internal void RecordCandidateSampleFailure()
        {
            CandidateSampleFailures++;
        }

        internal void RecordCandidateOutsideRangeFailure()
        {
            CandidateOutsideRangeFailures++;
        }

        internal void RecordPathCalculationFailure()
        {
            PathCalculationFailures++;
        }

        internal void RecordPathStatusFailure(
            NavMeshPathStatus status,
            Vector3 partialPathEnd,
            Vector3 partialPathTarget)
        {
            if (status == NavMeshPathStatus.PathPartial)
            {
                PartialPathFailures++;
                if (!HasPartialPathSample)
                {
                    HasPartialPathSample = true;
                    PartialPathEnd = partialPathEnd;
                    PartialPathTarget = partialPathTarget;
                }
            }
            else
                InvalidPathFailures++;
        }
    }

    [SerializeField] private Transform depositPoint;
    [SerializeField] private float interactionRadius = 2.5f;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private GameObject npcPrefab;
    [SerializeField] private int villagerSpawnFoodCost = 100;
    [Tooltip("Fallback initial wood only when no generated-world launch settings configure shared barn storage.")]
    [SerializeField] private int woodStored;
    [Tooltip("Fallback initial food only when no generated-world launch settings configure shared barn storage.")]
    [SerializeField] private int berriesStored;
    [SerializeField] private bool logInventoryChanges;
    [SerializeField] private bool logNavMeshDiagnostics;

    private int contributedToSharedInventoryGeneration = -1;
    private NavMeshPath interactionPath;
    private float nextNavMeshDiagnosticRealtime;

    public int WoodStored => ResourceStorageRegistry.SharedWoodStored;
    public int BerriesStored => ResourceStorageRegistry.SharedBerriesStored;
    public float InteractionRadius => Mathf.Max(0.1f, interactionRadius);
    public int VillagerSpawnFoodCost => Mathf.Max(0, villagerSpawnFoodCost);

    private void Awake()
    {
        interactionPath = new NavMeshPath();
        ResolveInteractionPoints();
    }

    private void OnEnable()
    {
        ResourceStorageRegistry.Register(this);
    }

    private void OnDisable()
    {
        ResourceStorageRegistry.Unregister(this);
    }

    public Vector3 GetDepositPosition()
    {
        if (depositPoint != null)
            return depositPoint.position;

        return transform.position;
    }

    public Vector3 GetEquipmentPosition()
    {
        return GetDepositPosition();
    }

    public bool TryGetInteractionPosition(Vector3 requesterPosition, out Vector3 interactionPosition)
    {
        return TryGetInteractionPosition(
            requesterPosition,
            CreateStableRequesterSeed(requesterPosition),
            out interactionPosition
        );
    }

    public bool TryGetInteractionPosition(
        Vector3 requesterPosition,
        int requesterStableId,
        out Vector3 interactionPosition)
    {
        return TryGetInteractionPosition(
            requesterPosition,
            requesterStableId,
            out interactionPosition,
            out _
        );
    }

    public bool TryGetInteractionPosition(
        Vector3 requesterPosition,
        int requesterStableId,
        out Vector3 interactionPosition,
        out InteractionPositionDiagnostics diagnostics)
    {
        return TryGetInteractionPositionInternal(
            requesterPosition,
            requesterStableId,
            out interactionPosition,
            out diagnostics
        );
    }

    private bool TryGetInteractionPositionInternal(
        Vector3 requesterPosition,
        int requesterStableId,
        out Vector3 interactionPosition,
        out InteractionPositionDiagnostics diagnostics)
    {
        Vector3 center = GetDepositPosition();
        interactionPosition = center;
        diagnostics = default;

        if (!NavMesh.SamplePosition(
            requesterPosition,
            out NavMeshHit requesterHit,
            RequesterNavMeshSampleDistance,
            NavMesh.AllAreas))
        {
            diagnostics.RecordRequesterSampleFailure();
            return false;
        }

        Vector3 requesterDirection = requesterHit.position - center;
        requesterDirection.y = 0f;

        if (requesterDirection.sqrMagnitude <= 0.001f)
            requesterDirection = -transform.forward;

        requesterDirection.Normalize();
        Vector3 rightDirection = new Vector3(requesterDirection.z, 0f, -requesterDirection.x);
        float candidateRadius = InteractionRadius * InteractionPositionRadiusFactor;
        int firstCandidateIndex = PositiveModulo(requesterStableId, InteractionCandidateCount);

        for (int offset = 0; offset < InteractionCandidateCount; offset++)
        {
            int candidateIndex = (firstCandidateIndex + offset) % InteractionCandidateCount;
            float angle = candidateIndex * Mathf.PI * 2f / InteractionCandidateCount;
            Vector3 candidateDirection =
                requesterDirection * Mathf.Cos(angle) +
                rightDirection * Mathf.Sin(angle);

            if (TryUseInteractionCandidate(
                center + candidateDirection * candidateRadius,
                center,
                requesterHit.position,
                out interactionPosition,
                ref diagnostics))
            {
                return true;
            }
        }

        if (TryUseInteractionCandidate(
            center,
            center,
            requesterHit.position,
            out interactionPosition,
            ref diagnostics))
        {
            return true;
        }

        interactionPosition = center;
        MaybeLogNavMeshDiagnostic(
            requesterPosition,
            requesterHit.position,
            requesterStableId,
            center,
            diagnostics);
        return false;
    }

    public bool IsInInteractionRange(Vector3 position)
    {
        Vector3 interactionPosition = GetDepositPosition();
        Vector2 current = new Vector2(position.x, position.z);
        Vector2 target = new Vector2(interactionPosition.x, interactionPosition.z);

        return (current - target).sqrMagnitude <= InteractionRadius * InteractionRadius;
    }

    public void AddWood(int amount)
    {
        if (amount <= 0)
            return;

        ResourceStorageRegistry.AddWood(amount);

        if (logInventoryChanges)
            Debug.Log("Wood in storage: " + WoodStored);
    }

    public void AddBerries(int amount)
    {
        if (amount <= 0)
            return;

        ResourceStorageRegistry.AddBerries(amount);

        if (logInventoryChanges)
            Debug.Log("Berries in storage: " + BerriesStored);
    }

    public bool TrySpendResources(int woodAmount, int foodAmount)
    {
        int requiredWood = Mathf.Max(0, woodAmount);
        int requiredFood = Mathf.Max(0, foodAmount);

        return ResourceStorageRegistry.TrySpendResources(requiredWood, requiredFood);
    }

    public bool HasResources(int woodAmount, int foodAmount)
    {
        int requiredWood = Mathf.Max(0, woodAmount);
        int requiredFood = Mathf.Max(0, foodAmount);

        return ResourceStorageRegistry.HasResources(requiredWood, requiredFood);
    }

    public bool TrySpawnVillager()
    {
        Debug.Log($"Villager spawn request received at {gameObject.name}.");
        Debug.Log($"Current food: {BerriesStored}. Required food: {villagerSpawnFoodCost}.");
        Debug.Log(npcPrefab != null
            ? $"Villager npcPrefab assigned: {npcPrefab.name}."
            : "Villager npcPrefab missing.");
        Debug.Log(spawnPoint != null
            ? $"Villager spawnPoint assigned: {spawnPoint.name}."
            : "Villager spawnPoint missing. Falling back to position in front of the barn.");

        if (!HomeRegistry.HasFreeBedForNewNpc(out int livingNpcCount, out int sleepingCapacity))
        {
            Debug.Log($"Villager spawn skipped: no free beds. Living NPCs: {livingNpcCount}. Beds: {sleepingCapacity}.");
            return false;
        }

        if (BerriesStored < villagerSpawnFoodCost)
        {
            Debug.Log($"Villager spawn failed: insufficient food. Required: {villagerSpawnFoodCost}. Available: {BerriesStored}.");
            return false;
        }

        if (npcPrefab == null)
        {
            Debug.LogWarning($"Villager spawn failed: npcPrefab is not assigned. GameObject: {gameObject.name}.");
            return false;
        }

        Vector3 requestedPosition = GetSpawnPosition();
        NavMeshAgent prefabAgent = npcPrefab.GetComponent<NavMeshAgent>();
        if (prefabAgent == null)
        {
            Debug.LogWarning($"Villager spawn failed: npcPrefab has no NavMeshAgent. Prefab: {npcPrefab.name}.");
            return false;
        }

        NavMeshQueryFilter spawnFilter = new NavMeshQueryFilter
        {
            agentTypeID = prefabAgent.agentTypeID,
            areaMask = prefabAgent.areaMask
        };

        if (!NavMesh.SamplePosition(
                requestedPosition,
                out NavMeshHit spawnHit,
                NpcSpawnNavMeshSampleDistance,
                spawnFilter))
        {
            Debug.LogWarning(
                $"Villager spawn failed: spawn position {requestedPosition} is not on the NavMesh. " +
                $"GameObject: {gameObject.name}."
            );
            return false;
        }

        Vector3 position = spawnHit.position;
        Quaternion rotation = transform.rotation;

        if (!ResourceStorageRegistry.TrySpendResources(0, villagerSpawnFoodCost))
        {
            Debug.Log($"Villager spawn failed: insufficient food. Required: {villagerSpawnFoodCost}. Available: {BerriesStored}.");
            return false;
        }

        GameObject villager = Instantiate(npcPrefab, position, rotation);
        CharacterIdentity identity = villager.GetComponent<CharacterIdentity>();

        if (identity == null)
            identity = villager.AddComponent<CharacterIdentity>();

        identity.EnsureAssigned();
        NpcCollisionSettingsApplier.ApplyToAgent(villager.GetComponent<NavMeshAgent>());

        SimulationEventLogger.LogWorldChange(
            "CitizenSpawned",
            identity,
            $"New citizen {identity.FullName} (ID {identity.CharacterId}) spawned at {gameObject.name}.",
            gameObject.name
        );

        Debug.Log($"Villager spawned: {identity.FullName} (ID {identity.CharacterId}) at {position}. Food remaining: {BerriesStored}.");
        return true;
    }

    internal void ContributeInitialResourcesToSharedInventory(int registryGeneration)
    {
        if (contributedToSharedInventoryGeneration == registryGeneration)
            return;

        ResourceStorageRegistry.AddInitialSharedResources(woodStored, berriesStored);
        woodStored = 0;
        berriesStored = 0;
        contributedToSharedInventoryGeneration = registryGeneration;
    }

    private Vector3 GetSpawnPosition()
    {
        if (spawnPoint != null)
            return spawnPoint.position;

        return transform.position + transform.forward * 2f;
    }

    private void ResolveInteractionPoints()
    {
        if (depositPoint == null)
            depositPoint = FindDirectChild("DepositPoint");

        if (spawnPoint == null)
            spawnPoint = FindDirectChild("SpawnPoint");
    }

    private Transform FindDirectChild(string childName)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);

            if (child.name == childName)
                return child;
        }

        return null;
    }

    private bool TryUseInteractionCandidate(
        Vector3 candidate,
        Vector3 center,
        Vector3 requesterNavMeshPosition,
        out Vector3 interactionPosition,
        ref InteractionPositionDiagnostics diagnostics)
    {
        diagnostics.RecordCandidateAttempt();
        float sampleDistance = Mathf.Clamp(InteractionRadius * 0.5f, 0.25f, 1f);
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
        {
            diagnostics.RecordCandidateSampleFailure();
            interactionPosition = center;
            return false;
        }

        if (!IsInInteractionRange(hit.position))
        {
            diagnostics.RecordCandidateOutsideRangeFailure();
            interactionPosition = center;
            return false;
        }

        if (interactionPath == null)
            interactionPath = new NavMeshPath();

        if (!NavMesh.CalculatePath(requesterNavMeshPosition, hit.position, NavMesh.AllAreas, interactionPath))
        {
            diagnostics.RecordPathCalculationFailure();
            interactionPosition = center;
            return false;
        }

        if (interactionPath.status != NavMeshPathStatus.PathComplete)
        {
            Vector3 partialPathEnd = requesterNavMeshPosition;
            if (interactionPath.status == NavMeshPathStatus.PathPartial &&
                !diagnostics.HasPartialPathSample)
            {
                Vector3[] corners = interactionPath.corners;
                if (corners.Length > 0)
                    partialPathEnd = corners[corners.Length - 1];
            }

            diagnostics.RecordPathStatusFailure(
                interactionPath.status,
                partialPathEnd,
                hit.position);
            interactionPosition = center;
            return false;
        }

        interactionPosition = hit.position;
        return true;
    }

    private static int CreateStableRequesterSeed(Vector3 requesterPosition)
    {
        int x = Mathf.RoundToInt(requesterPosition.x * 100f);
        int z = Mathf.RoundToInt(requesterPosition.z * 100f);

        unchecked
        {
            return (x * 397) ^ z;
        }
    }

    private static int PositiveModulo(int value, int divisor)
    {
        int remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }

    private void MaybeLogNavMeshDiagnostic(
        Vector3 requesterPosition,
        Vector3 requesterNavMeshPosition,
        int requesterStableId,
        Vector3 depositPosition,
        InteractionPositionDiagnostics diagnostics)
    {
        if (!logNavMeshDiagnostics)
            return;

        if (Time.realtimeSinceStartup < nextNavMeshDiagnosticRealtime)
            return;

        nextNavMeshDiagnosticRealtime = Time.realtimeSinceStartup + 1f;
        string depositSampleText;
        if (NavMesh.SamplePosition(
            depositPosition,
            out NavMeshHit depositHit,
            Mathf.Max(0.5f, InteractionRadius),
            NavMesh.AllAreas))
        {
            NavMeshPath directPath = new NavMeshPath();
            bool calculated = NavMesh.CalculatePath(
                requesterNavMeshPosition,
                depositHit.position,
                NavMesh.AllAreas,
                directPath);

            Vector3 pathEnd = requesterNavMeshPosition;
            if (directPath.corners != null && directPath.corners.Length > 0)
                pathEnd = directPath.corners[directPath.corners.Length - 1];

            depositSampleText =
                $"depositHit={depositHit.position.ToString("F2")}, " +
                $"depositSampleOffset={Vector3.Distance(depositPosition, depositHit.position):0.###}, " +
                $"directCalc={calculated}, directStatus={directPath.status}, " +
                $"directCorners={directPath.corners.Length}, directEnd={pathEnd.ToString("F2")}, " +
                $"directEndToDeposit={Vector3.Distance(pathEnd, depositHit.position):0.###}";
        }
        else
        {
            depositSampleText = "depositSampleFailed";
        }

        Debug.LogWarning(
            "ResourceStorageNavMeshDiagnostic | " +
            $"storage={gameObject.name}, requesterId={requesterStableId}, " +
            $"requester={requesterPosition.ToString("F2")}, requesterHit={requesterNavMeshPosition.ToString("F2")}, " +
            $"deposit={depositPosition.ToString("F2")}, radius={InteractionRadius:0.###}, " +
            $"{depositSampleText}, interaction={diagnostics.ToSummary()}"
        );
    }

    private void OnValidate()
    {
        interactionRadius = Mathf.Max(0.1f, interactionRadius);
        ResolveInteractionPoints();
    }
}
