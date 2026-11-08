/*
===============================================================================
Script:
BuildingPlacementController

Purpose:
Handles click-to-place construction sites for selected build options and provides construction runtime dependencies.

Folder:
Assets/Scripts/Buildings/

Scene Hierarchy:
_Systems
- GameManager

Attach To:
GameManager

Required Children:
None

Dependencies:
- BuildOption
- ResourceStorage
- ConstructionSite
- WorldTimeManager
- SimulationBalanceConfig

Automatically Resolved:
- WorldTimeManager

Inspector References:
- buildingGridCellSizeInTiles
- tileSize
- worldTimeManager
- simulationBalanceConfig
===============================================================================
*/

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class BuildingPlacementController : MonoBehaviour
{
    private const float RaycastDistance = 1000f;
    private const float FootprintCheckHeight = 2f;

    [SerializeField] private int buildingGridCellSizeInTiles = 5;
    [SerializeField] private float tileSize = 2f;
    [SerializeField] private WorldTimeManager worldTimeManager;
    [SerializeField] private SimulationBalanceConfig simulationBalanceConfig;

    private BuildOption selectedBuildOption;
    private ResourceStorage sourceStorage;
    private GameObject placementGhost;
    private GameObject validGhostVisual;
    private GameObject invalidGhostVisual;
    private Vector2Int currentBuildingGridCell;
    private bool isCurrentPlacementValid;
    private readonly HashSet<Vector2Int> occupiedBuildingGridCells = new HashSet<Vector2Int>();

    private bool IsPlacing => selectedBuildOption != null && sourceStorage != null;
    private float BuildingGridCellWorldSize => Mathf.Max(1, buildingGridCellSizeInTiles) * Mathf.Max(0.1f, tileSize);

    private void Awake()
    {
        AutoResolveWorldTimeManager();
    }

    private void Update()
    {
        if (!IsPlacing || Mouse.current == null)
            return;

        UpdatePlacementPreview(Mouse.current.position.ReadValue());

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelPlacement();
            return;
        }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelPlacement();
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
            TryPlaceAtPointer(Mouse.current.position.ReadValue());
    }

    public void BeginPlacement(BuildOption buildOption, ResourceStorage storage)
    {
        if (buildOption == null)
        {
            Debug.LogWarning("BuildingPlacementController cannot begin placement: build option is missing.");
            return;
        }

        if (buildOption.ConstructionSitePrefab == null)
        {
            Debug.LogWarning($"BuildingPlacementController cannot place {buildOption.DisplayName}: constructionSitePrefab is missing.");
            return;
        }

        if (buildOption.GhostPrefab == null)
        {
            Debug.LogWarning($"BuildingPlacementController cannot preview {buildOption.DisplayName}: ghostPrefab is missing.");
            return;
        }

        if (storage == null)
        {
            Debug.LogWarning($"BuildingPlacementController cannot place {buildOption.DisplayName}: source storage is missing.");
            return;
        }

        selectedBuildOption = buildOption;
        sourceStorage = storage;
        CreatePlacementGhost();
    }

    public void CancelPlacement()
    {
        DestroyPlacementGhost();
        selectedBuildOption = null;
        sourceStorage = null;
        isCurrentPlacementValid = false;
    }

    private void TryPlaceAtPointer(Vector2 screenPosition)
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (!TryGetPlacementPoint(screenPosition, out _, out _))
            return;

        if (!isCurrentPlacementValid)
        {
            Debug.Log("Cannot place construction site: selected placement is not valid.");
            return;
        }

        GameObject constructionSite = Instantiate(
            selectedBuildOption.ConstructionSitePrefab,
            placementGhost.transform.position,
            placementGhost.transform.rotation
        );

        constructionSite.name = $"{selectedBuildOption.DisplayName} Construction Site";
        ConfigureConstructionSite(constructionSite);

        if (!sourceStorage.TrySpendResources(selectedBuildOption.RequiredWood, selectedBuildOption.RequiredFood))
        {
            Destroy(constructionSite);
            Debug.Log("Cannot place construction site: resource spending failed.");
            return;
        }

        occupiedBuildingGridCells.Add(currentBuildingGridCell);
        CancelPlacement();
    }

    private void ConfigureConstructionSite(GameObject constructionSiteObject)
    {
        if (constructionSiteObject == null)
            return;

        ConstructionSite constructionSite = constructionSiteObject.GetComponent<ConstructionSite>();
        if (constructionSite == null)
        {
            Debug.LogWarning($"BuildingPlacementController placed {constructionSiteObject.name}, but it has no ConstructionSite component.");
            return;
        }

        AutoResolveWorldTimeManager();
        constructionSite.Configure(worldTimeManager, simulationBalanceConfig);
    }

    private void CreatePlacementGhost()
    {
        DestroyPlacementGhost();

        placementGhost = Instantiate(selectedBuildOption.GhostPrefab);
        placementGhost.name = $"{selectedBuildOption.DisplayName} Placement Ghost";
        validGhostVisual = FindGhostVisual("Green");
        invalidGhostVisual = FindGhostVisual("Red");

        if (validGhostVisual == null || invalidGhostVisual == null)
            Debug.LogWarning($"BuildingPlacementController preview for {selectedBuildOption.DisplayName} should include Green and Red child visuals.");

        Collider[] colliders = placementGhost.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            collider.enabled = false;
        }

        SetGhostVisual(false);
    }

    private void DestroyPlacementGhost()
    {
        if (placementGhost == null)
            return;

        Destroy(placementGhost);
        placementGhost = null;
        validGhostVisual = null;
        invalidGhostVisual = null;
    }

    private void UpdatePlacementPreview(Vector2 screenPosition)
    {
        if (placementGhost == null)
            return;

        if (!TryGetPlacementPoint(screenPosition, out Vector3 placementPosition, out Collider groundCollider))
        {
            isCurrentPlacementValid = false;
            SetGhostVisual(false);
            return;
        }

        Vector3 snappedPosition = SnapToBuildingGrid(placementPosition, groundCollider);
        currentBuildingGridCell = GetBuildingGridCell(snappedPosition, groundCollider);

        placementGhost.transform.position = snappedPosition;
        placementGhost.transform.rotation = Quaternion.identity;

        isCurrentPlacementValid = IsBuildingGridCellValid(snappedPosition, currentBuildingGridCell, groundCollider);
        SetGhostVisual(isCurrentPlacementValid);
    }

    private bool TryGetPlacementPoint(Vector2 screenPosition, out Vector3 placementPosition, out Collider groundCollider)
    {
        placementPosition = Vector3.zero;
        groundCollider = null;

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("BuildingPlacementController cannot place: no main camera found.");
            return false;
        }

        Ray ray = mainCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, RaycastDistance);

        if (hits.Length == 0)
            return false;

        Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));

        foreach (RaycastHit hit in hits)
        {
            if (!IsMapGroundCollider(hit.collider))
                continue;

            placementPosition = new Vector3(hit.point.x, 0f, hit.point.z);
            groundCollider = hit.collider;
            return true;
        }

        return false;
    }

    private GameObject FindGhostVisual(string visualNamePart)
    {
        Transform[] children = placementGhost.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child == placementGhost.transform)
                continue;

            if (child.name.Contains(visualNamePart))
                return child.gameObject;
        }

        return null;
    }

    private void SetGhostVisual(bool showValid)
    {
        if (validGhostVisual != null)
            validGhostVisual.SetActive(showValid);

        if (invalidGhostVisual != null)
            invalidGhostVisual.SetActive(!showValid);
    }

    private bool HasEnoughResources()
    {
        return sourceStorage.HasResources(selectedBuildOption.RequiredWood, selectedBuildOption.RequiredFood);
    }

    private Vector3 SnapToBuildingGrid(Vector3 position, Collider groundCollider)
    {
        Vector2Int cell = GetBuildingGridCell(position, groundCollider);
        float cellWorldSize = BuildingGridCellWorldSize;
        Vector3 origin = GetBuildingGridOrigin(groundCollider);

        return new Vector3(
            origin.x + (cell.x + 0.5f) * cellWorldSize,
            0f,
            origin.z + (cell.y + 0.5f) * cellWorldSize
        );
    }

    private Vector2Int GetBuildingGridCell(Vector3 position, Collider groundCollider)
    {
        float cellWorldSize = BuildingGridCellWorldSize;
        Vector3 origin = GetBuildingGridOrigin(groundCollider);

        int cellX = Mathf.FloorToInt((position.x - origin.x) / cellWorldSize);
        int cellZ = Mathf.FloorToInt((position.z - origin.z) / cellWorldSize);

        return new Vector2Int(cellX, cellZ);
    }

    private Vector3 GetBuildingGridOrigin(Collider groundCollider)
    {
        if (IsMapGroundCollider(groundCollider))
            return groundCollider.bounds.min;

        return Vector3.zero;
    }

    private bool IsBuildingGridCellValid(Vector3 center, Vector2Int cell, Collider groundCollider)
    {
        if (!IsBuildingGridCellInsideMap(center, groundCollider))
            return false;

        if (occupiedBuildingGridCells.Contains(cell))
            return false;

        if (!HasEnoughResources())
            return false;

        return IsSelectedFootprintFree(center, selectedBuildOption.FootprintSize, groundCollider);
    }

    private bool IsBuildingGridCellInsideMap(Vector3 center, Collider groundCollider)
    {
        if (!IsMapGroundCollider(groundCollider))
            return false;

        float halfSize = BuildingGridCellWorldSize * 0.5f;
        Bounds bounds = groundCollider.bounds;

        return center.x - halfSize >= bounds.min.x
            && center.x + halfSize <= bounds.max.x
            && center.z - halfSize >= bounds.min.z
            && center.z + halfSize <= bounds.max.z;
    }

    private bool IsSelectedFootprintFree(Vector3 center, Vector2 footprintSize, Collider groundCollider)
    {
        Vector3 overlapCenter = center + Vector3.up * (FootprintCheckHeight * 0.5f);
        Vector3 halfExtents = new Vector3(
            Mathf.Max(0.1f, footprintSize.x * 0.5f),
            FootprintCheckHeight * 0.5f,
            Mathf.Max(0.1f, footprintSize.y * 0.5f)
        );

        Collider[] colliders = Physics.OverlapBox(
            overlapCenter,
            halfExtents,
            Quaternion.identity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide
        );

        foreach (Collider collider in colliders)
        {
            if (placementGhost != null && collider.transform.IsChildOf(placementGhost.transform))
                continue;

            if (IsAllowedGroundCollider(collider, groundCollider))
                continue;

            if (IsDepletedResourceCollider(collider))
                continue;

            return false;
        }

        return true;
    }

    private bool IsAllowedGroundCollider(Collider collider, Collider groundCollider)
    {
        if (collider == groundCollider && IsMapGroundCollider(collider))
            return true;

        return IsMapGroundCollider(collider);
    }

    private bool IsMapGroundCollider(Collider collider)
    {
        return collider != null && collider.gameObject.name == "Generated_Ground";
    }

    private bool IsDepletedResourceCollider(Collider collider)
    {
        ResourceNode resource = collider.GetComponentInParent<ResourceNode>();
        return resource != null && !resource.CanHarvest;
    }

    private void AutoResolveWorldTimeManager()
    {
        if (worldTimeManager != null)
            return;

        worldTimeManager = FindFirstObjectByType<WorldTimeManager>();
    }
}
