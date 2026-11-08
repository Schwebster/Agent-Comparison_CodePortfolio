/*
===============================================================================
Script:
SelectionUIController

Purpose:
Coordinates selection UI panels through their public methods.

Folder:
Assets/Scripts/UI/

Scene Hierarchy:
_Systems
- GameManager

Attach To:
GameManager

Required Children:
None

Dependencies:
- TreeStatusPanelController
- BarnInventoryPanelController
- HouseStatusPanelController
- CharacterQuickPanelController
- CharacterDetailsPanelController

Automatically Resolved:
- TreeStatusPanelController in scene
- BarnInventoryPanelController in scene
- HouseStatusPanelController in scene
- CharacterQuickPanelController in scene
- CharacterDetailsPanelController in scene

Inspector References:
None
===============================================================================
*/
using UnityEngine;

public class SelectionUIController : MonoBehaviour
{
    private TreeStatusPanelController treeStatusPanel;
    private BarnInventoryPanelController barnInventoryPanel;
    private HouseStatusPanelController houseStatusPanel;
    private CharacterQuickPanelController characterQuickPanel;
    private CharacterDetailsPanelController characterDetailsPanel;

    private void Reset()
    {
        AutoWireReferences();
    }

    private void Awake()
    {
        AutoWireReferences();

        Debug.Assert(
            treeStatusPanel != null,
            $"Missing component: TreeStatusPanelController. Expected location: unique scene UI panel controller. Script: {nameof(SelectionUIController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            barnInventoryPanel != null,
            $"Missing component: BarnInventoryPanelController. Expected location: unique scene UI panel controller. Script: {nameof(SelectionUIController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            houseStatusPanel != null,
            $"Missing component: HouseStatusPanelController. Expected location: HouseStatusPanel under ScreenSpaceCanvas. Script: {nameof(SelectionUIController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            characterQuickPanel != null,
            $"Missing component: CharacterQuickPanelController. Expected location: unique scene UI panel controller. Script: {nameof(SelectionUIController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            characterDetailsPanel != null,
            $"Missing component: CharacterDetailsPanelController. Expected location: unique scene UI panel controller. Script: {nameof(SelectionUIController)}. GameObject: {gameObject.name}."
        );

        RegisterPanelEvents();

        HideCurrent();
    }

    private void OnDestroy()
    {
        UnregisterPanelEvents();
    }

    public void ShowTree(ResourceNode tree)
    {
        HideCurrent();

        if (treeStatusPanel != null)
            treeStatusPanel.Show(tree);
    }

    public void ShowBarn(ResourceStorage barn)
    {
        HideCurrent();

        if (barnInventoryPanel != null)
            barnInventoryPanel.Show(barn);
    }

    public void ShowHouse(House house)
    {
        HideCurrent();

        if (houseStatusPanel != null)
            houseStatusPanel.Show(house);
    }

    public void ShowCharacter(Character character)
    {
        if (treeStatusPanel != null)
            treeStatusPanel.Hide();

        if (barnInventoryPanel != null)
            barnInventoryPanel.Hide();

        if (houseStatusPanel != null)
            houseStatusPanel.Hide();

        if (characterDetailsPanel != null)
            characterDetailsPanel.SetCharacter(character);

        if (characterQuickPanel != null)
            characterQuickPanel.Show(character);
    }

    public void HideCurrent()
    {
        if (treeStatusPanel != null)
            treeStatusPanel.Hide();

        if (barnInventoryPanel != null)
            barnInventoryPanel.Hide();

        if (houseStatusPanel != null)
            houseStatusPanel.Hide();

        if (characterQuickPanel != null)
            characterQuickPanel.Hide();

        if (characterDetailsPanel != null)
            characterDetailsPanel.Hide();
    }

    public bool IsCharacterControlled(Character character)
    {
        return characterQuickPanel != null && characterQuickPanel.IsCharacterControlled(character);
    }

    [ContextMenu("Auto Wire References")]
    private void AutoWireReferences()
    {
        if (treeStatusPanel == null)
            treeStatusPanel = GetComponentInChildren<TreeStatusPanelController>(true);

        if (treeStatusPanel == null)
            treeStatusPanel = FindFirstObjectByType<TreeStatusPanelController>(FindObjectsInactive.Include);

        if (barnInventoryPanel == null)
            barnInventoryPanel = GetComponentInChildren<BarnInventoryPanelController>(true);

        if (barnInventoryPanel == null)
            barnInventoryPanel = FindFirstObjectByType<BarnInventoryPanelController>(FindObjectsInactive.Include);

        if (houseStatusPanel == null)
            houseStatusPanel = FindHouseStatusPanelController();

        if (characterQuickPanel == null)
            characterQuickPanel = GetComponentInChildren<CharacterQuickPanelController>(true);

        if (characterQuickPanel == null)
            characterQuickPanel = FindFirstObjectByType<CharacterQuickPanelController>(FindObjectsInactive.Include);

        if (characterDetailsPanel == null)
            characterDetailsPanel = GetComponentInChildren<CharacterDetailsPanelController>(true);

        if (characterDetailsPanel == null)
            characterDetailsPanel = FindFirstObjectByType<CharacterDetailsPanelController>(FindObjectsInactive.Include);
    }

    private HouseStatusPanelController FindHouseStatusPanelController()
    {
        HouseStatusPanelController[] housePanels = FindObjectsByType<HouseStatusPanelController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        if (housePanels.Length == 0)
            return null;

        if (housePanels.Length > 1)
            Debug.LogWarning($"Found {housePanels.Length} HouseStatusPanelController instances. SelectionUIController will use only {housePanels[0].gameObject.name}.");

        return housePanels[0];
    }

    private void RegisterPanelEvents()
    {
        if (characterQuickPanel != null)
            characterQuickPanel.DetailsRequested += ShowCharacterDetails;
    }

    private void UnregisterPanelEvents()
    {
        if (characterQuickPanel != null)
            characterQuickPanel.DetailsRequested -= ShowCharacterDetails;
    }

    private void ShowCharacterDetails()
    {
        if (characterDetailsPanel != null)
            characterDetailsPanel.Show();
    }

}
