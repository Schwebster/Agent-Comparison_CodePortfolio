/*
===============================================================================
Script:
GridWorldGenerator

Purpose:
Generates a deterministic 2D grid world in the 3D scene.

Folder:
Assets/Scripts/World/

Scene Hierarchy:
_World
- WorldGenerator

Attach To:
WorldGenerator

Required Children:
None

Dependencies:
None

Automatically Resolved:
None

Inspector References:
- groundPrefab
- barnPrefab
- housePrefab
- npcPrefab
- treePrefab
- berryBushPrefab
- fogTilePrefab
- generatedParent
- navMeshSurface
===============================================================================
*/

using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public enum SettlementBuildingType
{
    House,
    Barn
}

[DefaultExecutionOrder(-1000)]
public class GridWorldGenerator : MonoBehaviour
{
    private const string GeneratedObjectPrefix = "Generated_";
    private const float UnityPlaneSize = 10f;
    private const float HouseWorldWidth = 4f;
    private const float HouseWorldDepth = 4f;
    private const float BarnWorldWidth = 4f;
    private const float BarnWorldDepth = 8f;
    private const int CentralReservedSize = 10;
    private const int CentralSectorSize = 5;
    private const float ConstructionFootprintCheckHeight = 2f;
    private const float NpcPlacementEpsilon = 0.0001f;
    private const float NpcNavMeshHorizontalTolerance = 0.01f;

    private enum TileContentType
    {
        Grass,
        Tree,
        BerryBush
    }

    private struct StructurePlacement
    {
        public int StartX;
        public int StartZ;
        public int FootprintWidth;
        public int FootprintDepth;
    }

    private struct TileArea
    {
        public int StartX;
        public int StartZ;
        public int Width;
        public int Height;
    }

    [Header("Grid")]
    [SerializeField] private int width = 16;
    [SerializeField] private int height = 16;
    [SerializeField] private float tileSize = 2f;
    [SerializeField] private int randomSeed = 12345;

    [Header("Weights")]
    [SerializeField] private float grassWeight = 70f;
    [SerializeField] private float treeWeight = 20f;
    [SerializeField] private float berryBushWeight = 10f;

    [Header("Prefabs")]
    [SerializeField] private GameObject groundPrefab;
    [SerializeField] private GameObject barnPrefab;
    [SerializeField] private GameObject housePrefab;
    [SerializeField] private GameObject npcPrefab;
    [SerializeField] private GameObject treePrefab;
    [SerializeField] private GameObject berryBushPrefab;

    [Header("Automated Construction")]
    [SerializeField] private GameObject houseConstructionSitePrefab;
    [SerializeField] private GameObject barnConstructionSitePrefab;

    [Header("Fog")]
    [SerializeField] private GameObject fogTilePrefab;

    [Header("NPCs")]
    [SerializeField] private int npcCount = 3;

    [Header("Output")]
    [SerializeField] private Transform generatedParent;
    [SerializeField] private NavMeshSurface navMeshSurface;

    private bool hasPendingLaunchSettingsToFinalize;
    private WorldGenerationLaunchSettings.Settings pendingLaunchSettingsToFinalize;
    private bool runtimeWorldPrepared;
    private bool runtimeWorldGenerated;
    private int generationVersion;

    public bool IsWorldGenerated => runtimeWorldGenerated;
    public int GenerationVersion => generationVersion;
    public Vector3 SettlementCenter
    {
        get
        {
            return TryGetGeneratedGroundCollider(out Collider groundCollider)
                ? groundCollider.bounds.center
                : transform.position;
        }
    }

    private void Awake()
    {
        PrepareRuntimeWorld();
    }

    private void Start()
    {
        if (!runtimeWorldPrepared || !GenerateInternal(false))
            return;

        FinalizePendingLaunchSettings();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        GenerateInternal();
    }

    private bool GenerateInternal(bool clearExistingWorld = true)
    {
        if (!CanGenerate())
            return false;

        if (clearExistingWorld)
            Clear();

        FogRegistry.ResetGridWorldState();
        ResourceRegistry.ResetKnownResourceCandidates();

        TileArea centralArea = GetCentralReservedArea();
        RegisterKnownArea(centralArea);
        TileArea sector1 = GetCentralSector(centralArea, 0, 1);
        TileArea sector2 = GetCentralSector(centralArea, 1, 1);
        TileArea sector3 = GetCentralSector(centralArea, 0, 0);
        TileArea sector4 = GetCentralSector(centralArea, 1, 0);

        bool[,] resourceBlockedTiles = new bool[width, height];
        bool[,] occupiedTiles = new bool[width, height];
        ResourceNode[,] resourceNodesByTile = new ResourceNode[width, height];

        ReserveArea(centralArea, resourceBlockedTiles);

        if (!TryCreateCenteredStructurePlacement("Barn", BarnWorldWidth, BarnWorldDepth, sector1, out StructurePlacement barnPlacement))
            return false;

        ReserveTiles(barnPlacement, occupiedTiles);

        if (!TryCreateCenteredStructurePlacement("House", HouseWorldWidth, HouseWorldDepth, sector2, out StructurePlacement housePlacement))
            return false;

        ReserveTiles(housePlacement, occupiedTiles);

        int clampedNpcCount = Mathf.Max(0, npcCount);
        List<Vector3> npcFormationPositions = new List<Vector3>();
        NavMeshAgent npcPrefabAgent = null;

        if (clampedNpcCount > 0)
        {
            if (!TryGetNpcPlacementWidth(out float npcWidth, out npcPrefabAgent))
                return false;

            if (!TryCreateNpcFormation(sector3, sector4, clampedNpcCount, npcWidth, out npcFormationPositions))
                return false;
        }

        CreateGround();
        CreateStructure(barnPrefab, "Barn", barnPlacement);
        CreateStructure(housePrefab, "House", housePlacement);

        System.Random random = new System.Random(randomSeed);
        int grassCount = 0;
        int treeCount = 0;
        int berryBushCount = 0;
        int fogTileCount = 0;

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (resourceBlockedTiles[x, z])
                {
                    if (!occupiedTiles[x, z])
                        grassCount++;

                    continue;
                }

                TileContentType contentType = SelectContentType(random);

                if (contentType == TileContentType.Grass)
                {
                    grassCount++;
                    continue;
                }

                GameObject prefab = GetPrefab(contentType);

                if (prefab == null)
                {
                    Debug.LogWarning($"GridWorldGenerator skipped {contentType}: prefab is not assigned. GameObject: {gameObject.name}.");
                    continue;
                }

                Vector3 position = new Vector3(x * tileSize, 0f, z * tileSize);
                GameObject instance = Instantiate(prefab, position, Quaternion.identity, generatedParent);
                instance.name = $"{GeneratedObjectPrefix}{contentType}_{x}_{z}";

                ResourceNode resourceNode = instance.GetComponent<ResourceNode>();
                if (resourceNode != null)
                    resourceNodesByTile[x, z] = resourceNode;

                if (contentType == TileContentType.Tree)
                    treeCount++;
                else if (contentType == TileContentType.BerryBush)
                    berryBushCount++;
            }
        }

        fogTileCount = CreateFogTiles(centralArea, resourceNodesByTile);
        RebuildNavMesh();

        if (!TryValidateNpcFormationOnNavMesh(npcFormationPositions, npcPrefabAgent, out List<Vector3> validatedNpcPositions))
            return false;

        CreateNpcs(validatedNpcPositions);
        runtimeWorldGenerated = true;
        generationVersion++;
        LogGenerationSummary(grassCount, treeCount, berryBushCount, fogTileCount);
        return true;
    }

    public void ApplyLaunchSettings(WorldGenerationLaunchSettings.Settings settings)
    {
        randomSeed = settings.Seed;
        width = settings.WorldWidth;
        height = settings.WorldHeight;
        treeWeight = Mathf.Max(0f, settings.WoodDensityPercent);
        berryBushWeight = Mathf.Max(0f, settings.FoodDensityPercent);
        grassWeight = Mathf.Max(0f, settings.EmptyDensityPercent);
        npcCount = settings.NpcCount;
        ResourceStorageRegistry.ConfigureInitialSharedResources(settings.BarnStartingWood, settings.BarnStartingFood);
    }

    public bool TryPlaceNextConstructionSite(
        SettlementBuildingType buildingType,
        int requiredWood,
        int requiredFood,
        string decisionArchitecture,
        WorldTimeManager worldTimeManager,
        SimulationBalanceConfig simulationBalanceConfig,
        out ConstructionSite constructionSite)
    {
        constructionSite = null;

        if (!runtimeWorldGenerated || generatedParent == null)
            return false;

        GameObject constructionPrefab = buildingType == SettlementBuildingType.House
            ? houseConstructionSitePrefab
            : barnConstructionSitePrefab;

        GameObject finishedBuildingPrefab = buildingType == SettlementBuildingType.House
            ? housePrefab
            : barnPrefab;

        if (constructionPrefab == null || finishedBuildingPrefab == null || simulationBalanceConfig == null)
        {
            Debug.LogWarning(
                $"GridWorldGenerator cannot place an automated {buildingType} construction site: " +
                "a construction prefab, finished building prefab, or SimulationBalanceConfig is missing."
            );
            return false;
        }

        if (constructionPrefab.GetComponent<ConstructionSite>() == null)
        {
            Debug.LogWarning(
                $"GridWorldGenerator cannot place {constructionPrefab.name}: the prefab has no ConstructionSite component."
            );
            return false;
        }

        if (!TryFindAvailableConstructionSlot(buildingType, out Vector2Int gridCell, out Vector3 position))
            return false;

        int woodCost = Mathf.Max(0, requiredWood);
        int foodCost = Mathf.Max(0, requiredFood);
        if (!ResourceStorageRegistry.TrySpendResources(woodCost, foodCost))
            return false;

        GameObject instance = Instantiate(constructionPrefab, position, Quaternion.identity, generatedParent);
        instance.name = $"{GeneratedObjectPrefix}{buildingType}_ConstructionSite_{gridCell.x}_{gridCell.y}";
        constructionSite = instance.GetComponent<ConstructionSite>();
        constructionSite.Configure(worldTimeManager, simulationBalanceConfig, finishedBuildingPrefab);

        SimulationEventLogger.LogWorldChange(
            "ConstructionSitePlaced",
            null,
            $"{decisionArchitecture} placed a {buildingType} construction site in settlement grid cell ({gridCell.x}, {gridCell.y}).",
            instance.name,
            buildingType.ToString(),
            woodCost,
            foodCost
        );
        return true;
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        runtimeWorldGenerated = false;

        if (generatedParent == null)
        {
            Debug.LogWarning($"GridWorldGenerator cannot clear: generatedParent is not assigned. GameObject: {gameObject.name}.");
            return;
        }

        for (int i = generatedParent.childCount - 1; i >= 0; i--)
        {
            GameObject child = generatedParent.GetChild(i).gameObject;

            if (IsGeneratedChild(child))
                DestroyGeneratedChild(child);
        }
    }

    private bool IsGeneratedChild(GameObject child)
    {
        if (child.name.StartsWith(GeneratedObjectPrefix, StringComparison.Ordinal))
            return true;

        return child.GetComponent<CharacterIdentity>() != null;
    }

    private bool TryFindAvailableConstructionSlot(
        SettlementBuildingType buildingType,
        out Vector2Int selectedCell,
        out Vector3 selectedPosition)
    {
        selectedCell = default;
        selectedPosition = Vector3.zero;

        if (!TryGetGeneratedGroundCollider(out Collider groundCollider))
            return false;

        float cellWorldSize = CentralSectorSize * tileSize;
        Bounds bounds = groundCollider.bounds;
        int columns = Mathf.FloorToInt(bounds.size.x / cellWorldSize + NpcPlacementEpsilon);
        int rows = Mathf.FloorToInt(bounds.size.z / cellWorldSize + NpcPlacementEpsilon);
        Vector2 footprint = GetConstructionFootprint(buildingType);
        List<Vector2Int> candidates = new List<Vector2Int>();

        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (IsBarnConstructionCell(cell) != (buildingType == SettlementBuildingType.Barn))
                    continue;

                candidates.Add(cell);
            }
        }

        Vector3 center = bounds.center;
        candidates.Sort((left, right) => CompareConstructionCells(left, right, bounds.min, cellWorldSize, center));

        for (int i = 0; i < candidates.Count; i++)
        {
            Vector2Int cell = candidates[i];
            Vector3 position = GetConstructionCellCenter(cell, bounds.min, cellWorldSize);
            if (!IsAutomatedConstructionFootprintFree(position, footprint, groundCollider))
                continue;

            selectedCell = cell;
            selectedPosition = position;
            return true;
        }

        return false;
    }

    private bool TryGetGeneratedGroundCollider(out Collider groundCollider)
    {
        groundCollider = null;
        if (generatedParent == null)
            return false;

        Transform ground = generatedParent.Find($"{GeneratedObjectPrefix}Ground");
        if (ground == null)
            return false;

        groundCollider = ground.GetComponentInChildren<Collider>();
        return groundCollider != null;
    }

    private static bool IsBarnConstructionCell(Vector2Int cell)
    {
        return cell.x % 2 == 1 && cell.y % 2 == 0;
    }

    private static Vector2 GetConstructionFootprint(SettlementBuildingType buildingType)
    {
        return buildingType == SettlementBuildingType.House
            ? new Vector2(HouseWorldWidth, HouseWorldDepth)
            : new Vector2(8f, 4f);
    }

    private static Vector3 GetConstructionCellCenter(Vector2Int cell, Vector3 gridOrigin, float cellWorldSize)
    {
        return new Vector3(
            gridOrigin.x + (cell.x + 0.5f) * cellWorldSize,
            0f,
            gridOrigin.z + (cell.y + 0.5f) * cellWorldSize
        );
    }

    private static int CompareConstructionCells(
        Vector2Int left,
        Vector2Int right,
        Vector3 gridOrigin,
        float cellWorldSize,
        Vector3 settlementCenter)
    {
        Vector3 leftPosition = GetConstructionCellCenter(left, gridOrigin, cellWorldSize);
        Vector3 rightPosition = GetConstructionCellCenter(right, gridOrigin, cellWorldSize);
        int distanceComparison = ((leftPosition - settlementCenter).sqrMagnitude)
            .CompareTo((rightPosition - settlementCenter).sqrMagnitude);

        if (distanceComparison != 0)
            return distanceComparison;

        int rowComparison = left.y.CompareTo(right.y);
        return rowComparison != 0 ? rowComparison : left.x.CompareTo(right.x);
    }

    private bool IsAutomatedConstructionFootprintFree(
        Vector3 center,
        Vector2 footprintSize,
        Collider groundCollider)
    {
        if (FogRegistry.HasUnrevealedFogInArea(center, footprintSize))
            return false;

        Vector3 overlapCenter = center + Vector3.up * (ConstructionFootprintCheckHeight * 0.5f);
        Vector3 halfExtents = new Vector3(
            Mathf.Max(0.1f, footprintSize.x * 0.5f),
            ConstructionFootprintCheckHeight * 0.5f,
            Mathf.Max(0.1f, footprintSize.y * 0.5f)
        );

        Collider[] colliders = Physics.OverlapBox(
            overlapCenter,
            halfExtents,
            Quaternion.identity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == groundCollider || collider.transform.IsChildOf(groundCollider.transform))
                continue;

            if (collider.GetComponentInParent<Character>() != null)
                continue;

            ResourceNode resource = collider.GetComponentInParent<ResourceNode>();
            if (resource != null && !resource.CanHarvest)
                continue;

            return false;
        }

        return true;
    }

    private bool CanGenerate()
    {
        if (generatedParent == null)
        {
            Debug.LogWarning($"GridWorldGenerator cannot generate: generatedParent is not assigned. GameObject: {gameObject.name}.");
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            Debug.LogWarning($"GridWorldGenerator cannot generate: width and height must be greater than zero. GameObject: {gameObject.name}.");
            return false;
        }

        if (tileSize <= 0f)
        {
            Debug.LogWarning($"GridWorldGenerator cannot generate: tileSize must be greater than zero. GameObject: {gameObject.name}.");
            return false;
        }

        if (npcCount < 0)
        {
            Debug.LogWarning($"GridWorldGenerator cannot generate: npcCount must not be negative. GameObject: {gameObject.name}.");
            return false;
        }

        if (groundPrefab == null)
        {
            Debug.LogWarning($"GridWorldGenerator cannot generate: groundPrefab is not assigned. GameObject: {gameObject.name}.");
            return false;
        }

        if (housePrefab == null)
        {
            Debug.LogWarning($"GridWorldGenerator cannot generate: housePrefab is not assigned. GameObject: {gameObject.name}.");
            return false;
        }

        if (barnPrefab == null)
        {
            Debug.LogWarning($"GridWorldGenerator cannot generate: barnPrefab is not assigned. GameObject: {gameObject.name}.");
            return false;
        }

        if (npcCount > 0 && npcPrefab == null)
        {
            Debug.LogWarning($"GridWorldGenerator cannot generate: npcPrefab is not assigned. GameObject: {gameObject.name}.");
            return false;
        }

        if (npcCount > 0 && navMeshSurface == null)
        {
            Debug.LogWarning($"GridWorldGenerator cannot generate NPCs: navMeshSurface is not assigned. GameObject: {gameObject.name}.");
            return false;
        }

        if (width < CentralReservedSize || height < CentralReservedSize)
        {
            Debug.LogWarning($"GridWorldGenerator cannot generate: grid must be at least {CentralReservedSize} x {CentralReservedSize}. GameObject: {gameObject.name}.");
            return false;
        }

        return true;
    }

    private void PrepareRuntimeWorld()
    {
        if (WorldGenerationLaunchSettings.TryGetPendingStartSceneGeneration(out WorldGenerationLaunchSettings.Settings settings))
        {
            ApplyLaunchSettings(settings);
            pendingLaunchSettingsToFinalize = settings;
            hasPendingLaunchSettingsToFinalize = true;
        }

        Clear();

        if (hasPendingLaunchSettingsToFinalize)
        {
            SimulationEventLogger.BeginRun(
                pendingLaunchSettingsToFinalize,
                gameObject.scene.name
            );
        }

        runtimeWorldPrepared = true;
    }

    private void FinalizePendingLaunchSettings()
    {
        if (!hasPendingLaunchSettingsToFinalize ||
            !TryApplyLaunchClockSettings(pendingLaunchSettingsToFinalize))
        {
            return;
        }

        WorldGenerationLaunchSettings.ClearPendingStartSceneGeneration();
        pendingLaunchSettingsToFinalize = default;
        hasPendingLaunchSettingsToFinalize = false;
    }

    private bool TryApplyLaunchClockSettings(WorldGenerationLaunchSettings.Settings settings)
    {
        float secondsPerDay = settings.CurrentClockSpeedSecondsPerDay > 0f
            ? settings.CurrentClockSpeedSecondsPerDay
            : settings.NormalClockSpeedSecondsPerDay;

        if (secondsPerDay <= 0f)
            return true;

        WorldTimeManager worldTimeManager = FindFirstObjectByType<WorldTimeManager>(FindObjectsInactive.Include);

        if (worldTimeManager == null)
        {
            Debug.LogWarning($"GridWorldGenerator could not apply StartScene clock speed because no WorldTimeManager exists in this scene. GameObject: {gameObject.name}.");
            return false;
        }

        if (settings.NormalClockSpeedSecondsPerDay > 0f)
            worldTimeManager.SetReferenceDayDurationRealSeconds(settings.NormalClockSpeedSecondsPerDay);

        worldTimeManager.SetDayDurationRealSeconds(secondsPerDay);
        return true;
    }

    private void CreateGround()
    {
        Vector3 position = new Vector3(
            (width - 1) * tileSize * 0.5f,
            0f,
            (height - 1) * tileSize * 0.5f
        );

        GameObject ground = Instantiate(groundPrefab, position, Quaternion.identity, generatedParent);
        ground.name = $"{GeneratedObjectPrefix}Ground";
        ground.transform.localScale = new Vector3(
            width * tileSize / UnityPlaneSize,
            1f,
            height * tileSize / UnityPlaneSize
        );
    }

    private TileArea GetCentralReservedArea()
    {
        return new TileArea
        {
            StartX = Mathf.FloorToInt((width - CentralReservedSize) * 0.5f),
            StartZ = Mathf.FloorToInt((height - CentralReservedSize) * 0.5f),
            Width = CentralReservedSize,
            Height = CentralReservedSize
        };
    }

    private TileArea GetCentralSector(TileArea centralArea, int sectorX, int sectorZ)
    {
        return new TileArea
        {
            StartX = centralArea.StartX + sectorX * CentralSectorSize,
            StartZ = centralArea.StartZ + sectorZ * CentralSectorSize,
            Width = CentralSectorSize,
            Height = CentralSectorSize
        };
    }

    private bool TryCreateCenteredStructurePlacement(string structureName, float worldWidth, float worldDepth, TileArea sector, out StructurePlacement placement)
    {
        placement = new StructurePlacement();

        int footprintWidth = Mathf.Max(1, Mathf.CeilToInt(worldWidth / tileSize));
        int footprintDepth = Mathf.Max(1, Mathf.CeilToInt(worldDepth / tileSize));

        if (footprintWidth > sector.Width || footprintDepth > sector.Height)
        {
            Debug.LogWarning($"GridWorldGenerator cannot place {structureName}: footprint does not fit its sector. GameObject: {gameObject.name}.");
            return false;
        }

        placement.StartX = sector.StartX + Mathf.FloorToInt((sector.Width - footprintWidth) * 0.5f);
        placement.StartZ = sector.StartZ + Mathf.FloorToInt((sector.Height - footprintDepth) * 0.5f);
        placement.FootprintWidth = footprintWidth;
        placement.FootprintDepth = footprintDepth;

        return true;
    }

    private void CreateStructure(GameObject prefab, string structureName, StructurePlacement placement)
    {
        Vector3 position = GetStructurePosition(placement);
        GameObject instance = Instantiate(prefab, position, Quaternion.identity, generatedParent);
        instance.name = $"{GeneratedObjectPrefix}{structureName}";
    }

    private bool TryGetNpcPlacementWidth(out float npcWidth, out NavMeshAgent navMeshAgent)
    {
        npcWidth = 0f;
        navMeshAgent = npcPrefab.GetComponent<NavMeshAgent>();

        if (navMeshAgent == null || !navMeshAgent.enabled)
        {
            Debug.LogWarning($"GridWorldGenerator cannot place NPCs: the assigned NPC prefab needs an enabled NavMeshAgent on its root. GameObject: {gameObject.name}.");
            return false;
        }

        Vector3 scale = npcPrefab.transform.lossyScale;
        float scaleX = Mathf.Abs(scale.x);
        float scaleZ = Mathf.Abs(scale.z);
        float agentWidth = navMeshAgent.radius * 2f * Mathf.Max(scaleX, scaleZ);
        CapsuleCollider capsuleCollider = npcPrefab.GetComponent<CapsuleCollider>();
        float colliderWidth = 0f;

        if (capsuleCollider != null && capsuleCollider.enabled)
        {
            float diameter = capsuleCollider.radius * 2f;
            float widthX = (capsuleCollider.direction == 0 ? capsuleCollider.height : diameter) * scaleX;
            float widthZ = (capsuleCollider.direction == 2 ? capsuleCollider.height : diameter) * scaleZ;
            colliderWidth = Mathf.Max(widthX, widthZ);
        }

        npcWidth = Mathf.Max(agentWidth, colliderWidth);

        if (npcWidth <= 0f)
        {
            Debug.LogWarning($"GridWorldGenerator cannot place NPCs: the assigned NPC prefab has no positive horizontal width. GameObject: {gameObject.name}.");
            return false;
        }

        return true;
    }

    private bool TryCreateNpcFormation(TileArea lowerLeftSector, TileArea lowerRightSector, int requestedCount, float npcWidth, out List<Vector3> positions)
    {
        positions = new List<Vector3>(requestedCount);
        float minX = Mathf.Min(GetAreaMinX(lowerLeftSector), GetAreaMinX(lowerRightSector));
        float maxX = Mathf.Max(GetAreaMaxX(lowerLeftSector), GetAreaMaxX(lowerRightSector));
        float minZ = Mathf.Min(GetAreaMinZ(lowerLeftSector), GetAreaMinZ(lowerRightSector));
        float maxZ = Mathf.Max(GetAreaMaxZ(lowerLeftSector), GetAreaMaxZ(lowerRightSector));
        int columns = GetFormationSlotCount(maxX - minX, npcWidth);
        int rows = GetFormationSlotCount(maxZ - minZ, npcWidth);
        int capacity = columns * rows;

        if (requestedCount > capacity)
        {
            Debug.LogWarning(
                $"GridWorldGenerator cannot place {requestedCount} NPCs in the lower central formation: capacity is {capacity} " +
                $"({columns} columns x {rows} rows) for an NPC width of {npcWidth:F3}. No NPCs were created. GameObject: {gameObject.name}."
            );
            return false;
        }

        float startX = minX + npcWidth;
        float startZ = maxZ - npcWidth;

        for (int i = 0; i < requestedCount; i++)
        {
            int row = i / columns;
            int column = i % columns;
            positions.Add(new Vector3(startX + column * npcWidth, 0f, startZ - row * npcWidth));
        }

        return true;
    }

    private int GetFormationSlotCount(float areaLength, float npcWidth)
    {
        float distanceBetweenFirstAndLastCenter = areaLength - 2f * npcWidth;

        if (distanceBetweenFirstAndLastCenter < -NpcPlacementEpsilon)
            return 0;

        return Mathf.FloorToInt((distanceBetweenFirstAndLastCenter + NpcPlacementEpsilon) / npcWidth) + 1;
    }

    private float GetAreaMinX(TileArea area)
    {
        return (area.StartX - 0.5f) * tileSize;
    }

    private float GetAreaMaxX(TileArea area)
    {
        return (area.StartX + area.Width - 0.5f) * tileSize;
    }

    private float GetAreaMinZ(TileArea area)
    {
        return (area.StartZ - 0.5f) * tileSize;
    }

    private float GetAreaMaxZ(TileArea area)
    {
        return (area.StartZ + area.Height - 0.5f) * tileSize;
    }

    private bool TryValidateNpcFormationOnNavMesh(List<Vector3> formationPositions, NavMeshAgent prefabAgent, out List<Vector3> validatedPositions)
    {
        validatedPositions = new List<Vector3>(formationPositions.Count);

        if (formationPositions.Count == 0)
            return true;

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = prefabAgent.agentTypeID,
            areaMask = prefabAgent.areaMask
        };
        // SamplePosition measures in 3D. Allow the baked NavMesh to differ vertically
        // from the ground while the horizontal check below still protects the formation.
        float sampleDistance = Mathf.Max(0.1f, tileSize * 0.1f);
        float horizontalToleranceSquared = NpcNavMeshHorizontalTolerance * NpcNavMeshHorizontalTolerance;

        for (int i = 0; i < formationPositions.Count; i++)
        {
            Vector3 position = formationPositions[i];

            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, sampleDistance, filter))
            {
                Debug.LogWarning($"GridWorldGenerator cannot place NPC {i + 1}: its exact formation position {position} is not on the finished NavMesh. No NPCs were created. GameObject: {gameObject.name}.");
                return false;
            }

            float deltaX = hit.position.x - position.x;
            float deltaZ = hit.position.z - position.z;

            if (deltaX * deltaX + deltaZ * deltaZ > horizontalToleranceSquared)
            {
                Debug.LogWarning(
                    $"GridWorldGenerator cannot place NPC {i + 1}: NavMesh validation would move its X/Z formation position. " +
                    $"Requested: {position.ToString("F3")}. Sampled: {hit.position.ToString("F3")}. " +
                    $"Delta X/Z: ({deltaX:F3}, {deltaZ:F3}). No NPCs were created. GameObject: {gameObject.name}."
                );
                return false;
            }

            validatedPositions.Add(new Vector3(position.x, hit.position.y, position.z));
        }

        return true;
    }

    private void CreateNpcs(List<Vector3> npcPositions)
    {
        for (int i = 0; i < npcPositions.Count; i++)
        {
            GameObject npc = Instantiate(npcPrefab, npcPositions[i], Quaternion.identity, generatedParent);
            CharacterIdentity identity = npc.GetComponent<CharacterIdentity>();

            if (identity == null)
                identity = npc.AddComponent<CharacterIdentity>();

            identity.EnsureAssigned();
            NpcCollisionSettingsApplier.ApplyToAgent(npc.GetComponent<NavMeshAgent>());
        }
    }

    private void ReserveTiles(StructurePlacement placement, bool[,] reservedTiles)
    {
        for (int z = placement.StartZ; z < placement.StartZ + placement.FootprintDepth; z++)
        {
            for (int x = placement.StartX; x < placement.StartX + placement.FootprintWidth; x++)
                reservedTiles[x, z] = true;
        }
    }

    private void ReserveArea(TileArea area, bool[,] reservedTiles)
    {
        for (int z = area.StartZ; z < area.StartZ + area.Height; z++)
        {
            for (int x = area.StartX; x < area.StartX + area.Width; x++)
                reservedTiles[x, z] = true;
        }
    }

    private Vector3 GetStructurePosition(StructurePlacement placement)
    {
        float footprintCenterX =
            (placement.StartX + (placement.FootprintWidth - 1) * 0.5f) * tileSize;
        float footprintCenterZ =
            (placement.StartZ + (placement.FootprintDepth - 1) * 0.5f) * tileSize;

        return new Vector3(
            SnapToBuildingGridCenter(footprintCenterX),
            0f,
            SnapToBuildingGridCenter(footprintCenterZ)
        );
    }

    private float SnapToBuildingGridCenter(float worldCoordinate)
    {
        float cellWorldSize = CentralSectorSize * tileSize;
        float gridOrigin = tileSize * -0.5f;
        int cell = Mathf.FloorToInt((worldCoordinate - gridOrigin) / cellWorldSize);
        return gridOrigin + (cell + 0.5f) * cellWorldSize;
    }

    private TileContentType SelectContentType(System.Random random)
    {
        float grass = Mathf.Max(0f, grassWeight);
        float tree = Mathf.Max(0f, treeWeight);
        float berryBush = Mathf.Max(0f, berryBushWeight);
        float totalWeight = grass + tree + berryBush;

        if (totalWeight <= 0f)
            return TileContentType.Grass;

        double roll = random.NextDouble() * totalWeight;

        if (roll < grass)
            return TileContentType.Grass;

        if (roll < grass + tree)
            return TileContentType.Tree;

        return TileContentType.BerryBush;
    }

    private GameObject GetPrefab(TileContentType contentType)
    {
        switch (contentType)
        {
            case TileContentType.Tree:
                return treePrefab;
            case TileContentType.BerryBush:
                return berryBushPrefab;
            default:
                return null;
        }
    }

    private int CreateFogTiles(TileArea clearArea, ResourceNode[,] resourceNodesByTile)
    {
        if (fogTilePrefab == null)
        {
            Debug.LogWarning($"GridWorldGenerator skipped fog generation because fogTilePrefab is not assigned. Assign the manually created FogTile prefab in the Inspector. GameObject: {gameObject.name}.");
            return 0;
        }

        if (fogTilePrefab.GetComponent<FogObject>() == null)
        {
            Debug.LogError($"GridWorldGenerator skipped fog generation because fogTilePrefab has no FogObject component. Prefab: {fogTilePrefab.name}. GameObject: {gameObject.name}.");
            return 0;
        }

        int fogTileCount = 0;
        Transform fogParent = CreateGeneratedContainer("Fog");

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (IsInsideArea(x, z, clearArea))
                    continue;

                Vector3 position = new Vector3(x * tileSize, 0f, z * tileSize);
                GameObject fogTile = Instantiate(fogTilePrefab, position, Quaternion.identity, fogParent);
                fogTile.name = $"{GeneratedObjectPrefix}FogTile_{x}_{z}";

                FogObject fogObject = fogTile.GetComponent<FogObject>();
                if (fogObject != null)
                {
                    fogObject.SetTileSize(tileSize);
                    fogObject.SetCoveredResource(resourceNodesByTile != null ? resourceNodesByTile[x, z] : null);
                    FogRegistry.ConfigureGeneratedFog(fogObject, x, z, IsAdjacentToArea(x, z, clearArea));
                }

                fogTileCount++;
            }
        }

        return fogTileCount;
    }

    private void RegisterKnownArea(TileArea area)
    {
        for (int z = area.StartZ; z < area.StartZ + area.Height; z++)
        {
            for (int x = area.StartX; x < area.StartX + area.Width; x++)
                FogRegistry.RegisterKnownCell(x, z);
        }
    }

    private Transform CreateGeneratedContainer(string containerName)
    {
        GameObject container = new GameObject($"{GeneratedObjectPrefix}{containerName}");
        container.transform.SetParent(generatedParent, false);
        return container.transform;
    }

    private bool IsInsideArea(int tileX, int tileZ, TileArea area)
    {
        return tileX >= area.StartX &&
            tileX < area.StartX + area.Width &&
            tileZ >= area.StartZ &&
            tileZ < area.StartZ + area.Height;
    }

    private bool IsAdjacentToArea(int tileX, int tileZ, TileArea area)
    {
        if (IsInsideArea(tileX, tileZ, area))
            return false;

        return tileX >= area.StartX - 1 &&
            tileX <= area.StartX + area.Width &&
            tileZ >= area.StartZ - 1 &&
            tileZ <= area.StartZ + area.Height;
    }

    private void LogGenerationSummary(int grassCount, int treeCount, int berryBushCount, int fogTileCount)
    {
        Debug.Log(
            $"Grid generated.\nSeed: {randomSeed}\nSize: {width} x {height}\nGrass: {grassCount}\nTrees: {treeCount}\nBerryBushes: {berryBushCount}\nFogTiles: {fogTileCount}"
        );
    }

    private void RebuildNavMesh()
    {
        if (navMeshSurface != null)
            navMeshSurface.BuildNavMesh();
    }

    private void DestroyGeneratedChild(GameObject child)
    {
        if (Application.isPlaying)
        {
            // Destroy is deferred until the end of the frame. Disable old generated
            // content immediately so it cannot affect the replacement NavMesh build.
            child.SetActive(false);
            Destroy(child);
        }
        else
            DestroyImmediate(child);
    }
}
