/*
===============================================================================
Script:
BarnInventoryPanelController

Purpose:
Controls the screen-space barn inventory panel.

Folder:
Assets/Scripts/UI/

Scene Hierarchy:
_UI
- ScreenSpaceCanvas
  - BarnInventoryPanel

Attach To:
BarnInventoryPanel

Required Children:
- BarnWoodText
- BarnFoodText
- BuildButton
- SpawnVillagerButton
- PlannerButton
- BarnPlannerPanel

Dependencies:
- ResourceStorage
- BarnBuildMenu
- BarnPlannerPanelController

Automatically Resolved:
- BarnWoodText
- BarnFoodText
- BuildButton
- SpawnVillagerButton
- PlannerButton
- BarnPlannerPanel
- BarnBuildMenu in scene

Inspector References:
None
===============================================================================
*/
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BarnInventoryPanelController : MonoBehaviour
{
    private TMP_Text barnWoodText;
    private TMP_Text barnFoodText;
    private Button buildButton;
    private Button spawnVillagerButton;
    private Button plannerButton;
    private BarnBuildMenu buildMenu;
    private BarnPlannerPanelController plannerPanel;
    private GameObject barnPlannerPanelObject;
    private ResourceStorage currentResourceStorage;
    private int displayedWood = int.MinValue;
    private int displayedBerries = int.MinValue;

    private void Reset()
    {
        AutoWireReferences();
    }

    private void Awake()
    {
        AutoWireReferences();

        Debug.Assert(
            barnWoodText != null,
            $"Missing component: TMP_Text named BarnWoodText. Expected location: child of BarnInventoryPanel. Script: {nameof(BarnInventoryPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            barnFoodText != null,
            $"Missing component: TMP_Text named BarnFoodText. Expected location: child of BarnInventoryPanel. Script: {nameof(BarnInventoryPanelController)}. GameObject: {gameObject.name}."
        );

        RegisterButtonEvents();
    }

    private void OnDestroy()
    {
        UnregisterButtonEvents();
    }

    private void LateUpdate()
    {
        UpdatePanel();
    }

    public void Show(ResourceStorage barn)
    {
        AutoWireReferences();
        RegisterButtonEvents();

        currentResourceStorage = null;
        ResetDisplayedValues();

        if (barn == null)
            return;

        if (barnWoodText == null)
        {
            Debug.LogWarning("Barn inventory UI is not assigned.");
            return;
        }

        currentResourceStorage = barn;

        if (buildMenu != null)
            buildMenu.SetBarn(currentResourceStorage);

        if (plannerPanel != null)
            plannerPanel.SetBarn(currentResourceStorage);

        gameObject.SetActive(true);
        UpdatePanel();
    }

    public void Hide()
    {
        if (plannerPanel != null)
            plannerPanel.SetBarn(null);

        currentResourceStorage = null;
        ResetDisplayedValues();
        gameObject.SetActive(false);
    }

    private void UpdatePanel()
    {
        if (currentResourceStorage == null || barnWoodText == null || barnFoodText == null)
            return;

        int currentWood = currentResourceStorage.WoodStored;
        int currentBerries = currentResourceStorage.BerriesStored;

        if (currentWood != displayedWood)
        {
            displayedWood = currentWood;
            barnWoodText.text = $"Wood: {currentWood}";
        }

        if (currentBerries != displayedBerries)
        {
            displayedBerries = currentBerries;
            barnFoodText.text = $"Berries: {currentBerries}";
        }
    }

    private void ResetDisplayedValues()
    {
        displayedWood = int.MinValue;
        displayedBerries = int.MinValue;
    }

    private void OpenBuildMenu()
    {
        if (buildMenu == null)
            return;

        buildMenu.SetBarn(currentResourceStorage);
        buildMenu.OpenBuildMenu();
    }

    private void TogglePlannerPanel()
    {
        if (plannerPanel != null)
        {
            plannerPanel.SetBarn(currentResourceStorage);
            plannerPanel.TogglePanel();
            return;
        }

        if (barnPlannerPanelObject != null)
        {
            barnPlannerPanelObject.SetActive(!barnPlannerPanelObject.activeSelf);
            return;
        }

        Debug.LogWarning($"PlannerButton cannot open BarnPlannerPanel: no BarnPlannerPanel or BarnPlannerPanelController found under {gameObject.name}.");
    }

    private void RegisterButtonEvents()
    {
        if (buildButton != null)
        {
            buildButton.onClick.RemoveListener(OpenBuildMenu);
            buildButton.onClick.AddListener(OpenBuildMenu);
        }

        if (spawnVillagerButton != null)
        {
            spawnVillagerButton.onClick.RemoveListener(OnSpawnVillagerButtonClicked);
            spawnVillagerButton.onClick.AddListener(OnSpawnVillagerButtonClicked);
            Debug.Log($"SpawnVillagerButton wired on {gameObject.name}.");
        }
        else
        {
            Debug.LogWarning($"SpawnVillagerButton was not found or has no Button component under {gameObject.name}.");
        }

        if (plannerButton != null)
        {
            plannerButton.onClick.RemoveListener(TogglePlannerPanel);
            plannerButton.onClick.AddListener(TogglePlannerPanel);
        }
        else
        {
            Debug.LogWarning($"PlannerButton was not found or has no Button component under {gameObject.name}.");
        }
    }

    private void UnregisterButtonEvents()
    {
        if (buildButton != null)
            buildButton.onClick.RemoveListener(OpenBuildMenu);

        if (spawnVillagerButton != null)
            spawnVillagerButton.onClick.RemoveListener(OnSpawnVillagerButtonClicked);

        if (plannerButton != null)
            plannerButton.onClick.RemoveListener(TogglePlannerPanel);
    }

    public void OnSpawnVillagerButtonClicked()
    {
        Debug.Log("OnSpawnVillagerButtonClicked called.");

        if (currentResourceStorage == null)
        {
            Debug.LogWarning("Cannot spawn villager: no ResourceStorage is assigned to the open BarnInventoryPanel.");
            return;
        }

        Debug.Log($"SpawnVillagerButton is using ResourceStorage on {currentResourceStorage.gameObject.name}.");
        bool spawned = currentResourceStorage.TrySpawnVillager();
        UpdatePanel();

        if (spawned && plannerPanel != null)
            plannerPanel.RefreshAfterVillagerCountChanged();
    }

    [ContextMenu("Auto Wire References")]
    private void AutoWireReferences()
    {
        if (barnWoodText == null)
            barnWoodText = FindChildComponent<TMP_Text>("BarnWoodText");

        if (barnFoodText == null)
            barnFoodText = FindChildComponent<TMP_Text>("BarnFoodText");

        if (buildButton == null)
            buildButton = FindChildComponent<Button>("BuildButton");

        if (spawnVillagerButton == null)
        {
            spawnVillagerButton = FindChildComponent<Button>("SpawnVillagerButton");

            if (spawnVillagerButton != null)
                Debug.Log($"SpawnVillagerButton found under {gameObject.name}.");
        }

        if (plannerButton == null)
            plannerButton = FindChildComponent<Button>("PlannerButton");

        if (barnPlannerPanelObject == null)
        {
            Transform plannerPanelTransform = FindChild("BarnPlannerPanel");
            barnPlannerPanelObject = plannerPanelTransform != null ? plannerPanelTransform.gameObject : null;
        }

        if (plannerPanel == null && barnPlannerPanelObject != null)
            plannerPanel = barnPlannerPanelObject.GetComponent<BarnPlannerPanelController>();

        if (plannerPanel == null)
            plannerPanel = GetComponentInChildren<BarnPlannerPanelController>(true);

        if (buildMenu == null)
            buildMenu = FindFirstObjectByType<BarnBuildMenu>(FindObjectsInactive.Include);
    }

    private T FindChildComponent<T>(string childName) where T : Component
    {
        Transform child = FindChild(childName);
        return child != null ? child.GetComponent<T>() : null;
    }

    private Transform FindChild(string childName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child.name == childName)
                return child;
        }

        return null;
    }
}
