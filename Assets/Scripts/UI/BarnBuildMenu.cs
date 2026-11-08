/*
===============================================================================
Script:
BarnBuildMenu

Purpose:
Opens build options and forwards selected options to building placement.

Folder:
Assets/Scripts/UI/

Scene Hierarchy:
_UI
- ScreenSpaceCanvas
  - BarnBuildMenu

Attach To:
BarnBuildMenu

Required Children:
- HouseButton
- BarnButton
- BarnBuildOptionsPanelButtonClose

Dependencies:
- BuildOption
- BuildingPlacementController
- ResourceStorage

Automatically Resolved:
- BuildingPlacementController in scene
- HouseButton under buildOptionsPanel
- BarnButton under buildOptionsPanel
- BarnBuildOptionsPanelButtonClose under buildOptionsPanel

Inspector References:
- buildOptionsPanel
- buildOptions
===============================================================================
*/

using UnityEngine;
using UnityEngine.UI;

public class BarnBuildMenu : MonoBehaviour
{
    [SerializeField] private GameObject buildOptionsPanel;
    [SerializeField] private BuildOption[] buildOptions;

    private BuildingPlacementController placementController;
    private Button houseButton;
    private Button barnButton;
    private Button closeButton;
    private ResourceStorage currentStorage;

    private void Awake()
    {
        AutoWireReferences();

        Debug.Assert(
            placementController != null,
            $"Missing component: BuildingPlacementController. Expected location: unique scene placement controller. Script: {nameof(BarnBuildMenu)}. GameObject: {gameObject.name}."
        );

        RegisterButtonEvents();

        if (buildOptionsPanel != null)
            buildOptionsPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        UnregisterButtonEvents();
    }

    public void SetBarn(ResourceStorage storage)
    {
        currentStorage = storage;
    }

    public void OpenBuildMenu()
    {
        if (buildOptionsPanel != null)
            buildOptionsPanel.SetActive(true);

        Debug.Log("Build menu opened.");
    }

    public void CloseBuildMenu()
    {
        if (buildOptionsPanel != null)
            buildOptionsPanel.SetActive(false);

        Debug.Log("Build menu closed.");
    }

    public void SelectBuildOption(BuildOption buildOption)
    {
        if (placementController == null || buildOption == null || currentStorage == null)
            return;

        Debug.Log($"Build option selected: {buildOption.DisplayName}.");
        placementController.BeginPlacement(buildOption, currentStorage);
        Debug.Log($"Placement mode started for: {buildOption.DisplayName}.");
        CloseBuildMenu();
    }

    public void SelectBuildOptionByIndex(int buildOptionIndex)
    {
        if (buildOptions == null || buildOptionIndex < 0 || buildOptionIndex >= buildOptions.Length)
            return;

        BuildOption buildOption = buildOptions[buildOptionIndex];
        Debug.Log($"Selected build option index: {buildOptionIndex}. BuildOption: {buildOption.DisplayName}.");
        SelectBuildOption(buildOption);
    }

    private void SelectHouseBuildOption()
    {
        Debug.Log("House button clicked.");
        SelectBuildOptionByIndex(1);
    }

    private void SelectBarnBuildOption()
    {
        Debug.Log("Barn button clicked.");
        SelectBuildOptionByIndex(0);
    }

    private void RegisterButtonEvents()
    {
        if (houseButton != null)
            houseButton.onClick.AddListener(SelectHouseBuildOption);

        if (barnButton != null)
            barnButton.onClick.AddListener(SelectBarnBuildOption);

        if (closeButton != null)
            closeButton.onClick.AddListener(CloseBuildMenu);
    }

    private void UnregisterButtonEvents()
    {
        if (houseButton != null)
            houseButton.onClick.RemoveListener(SelectHouseBuildOption);

        if (barnButton != null)
            barnButton.onClick.RemoveListener(SelectBarnBuildOption);

        if (closeButton != null)
            closeButton.onClick.RemoveListener(CloseBuildMenu);
    }

    [ContextMenu("Open Build Menu")]
    private void OpenBuildMenuFromContext()
    {
        OpenBuildMenu();
    }

    [ContextMenu("Auto Wire References")]
    private void AutoWireReferences()
    {
        if (placementController == null)
            placementController = FindFirstObjectByType<BuildingPlacementController>(FindObjectsInactive.Include);

        if (buildOptionsPanel == null)
            return;

        if (houseButton == null)
            houseButton = FindPanelChildComponent<Button>("HouseButton");

        if (barnButton == null)
            barnButton = FindPanelChildComponent<Button>("BarnButton");

        if (closeButton == null)
            closeButton = FindPanelChildComponent<Button>("BarnBuildOptionsPanelButtonClose");
    }

    private T FindPanelChildComponent<T>(string childName) where T : Component
    {
        Transform[] children = buildOptionsPanel.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child.name == childName)
                return child.GetComponent<T>();
        }

        return null;
    }
}
