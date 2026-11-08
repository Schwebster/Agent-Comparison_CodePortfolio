/*
===============================================================================
Script:
WorldSelectionController

Purpose:
Handles left-click world selection and routes selected objects to highlight and UI systems.

Folder:
Assets/Scripts/Interaction/

Scene Hierarchy:
_Systems
- GameManager

Attach To:
GameManager

Required Children:
None

Dependencies:
- SelectionUIController
- SelectionHighlightController
- Character
- ResourceNode
- ResourceStorage
- House

Automatically Resolved:
- SelectionUIController on the same GameObject
- SelectionHighlightController on the same GameObject

Inspector References:
None
===============================================================================
*/

using UnityEngine;

public class WorldSelectionController : MonoBehaviour
{
    private const float RaycastDistance = 1000f;

    private SelectionUIController selectionUI;
    private SelectionHighlightController selectionHighlight;

    public Character SelectedCharacter { get; private set; }

    private void Awake()
    {
        selectionUI = GetComponent<SelectionUIController>();
        selectionHighlight = GetComponent<SelectionHighlightController>();

        Debug.Assert(
            selectionUI != null,
            $"Missing component: SelectionUIController. Expected location: same GameObject as WorldSelectionController. Script: {nameof(WorldSelectionController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            selectionHighlight != null,
            $"Missing component: SelectionHighlightController. Expected location: same GameObject as WorldSelectionController. Script: {nameof(WorldSelectionController)}. GameObject: {gameObject.name}."
        );
    }

    public void HandleLeftClick(Vector2 screenPosition)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("No main camera found for selection.");
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, RaycastDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            ClearHighlight();
            HideCurrentUI();
            return;
        }

        Character character = hit.collider.GetComponent<Character>();

        if (character == null)
            character = hit.collider.GetComponentInParent<Character>();

        ResourceNode resource = hit.collider.GetComponent<ResourceNode>();
        if (resource == null)
            resource = hit.collider.GetComponentInParent<ResourceNode>();

        ResourceStorage storage = hit.collider.GetComponent<ResourceStorage>();
        if (storage == null)
            storage = hit.collider.GetComponentInParent<ResourceStorage>();

        House house = hit.collider.GetComponent<House>();
        if (house == null)
            house = hit.collider.GetComponentInParent<House>();

        if (character != null)
        {
            SelectedCharacter = character;
            SelectHighlight(character.gameObject);

            if (selectionUI != null)
                selectionUI.ShowCharacter(character);

            Debug.Log("Selected character: " + SelectedCharacter.gameObject.name);
            return;
        }

        if (resource != null)
        {
            SelectHighlight(resource.gameObject);

            if (selectionUI != null)
                selectionUI.ShowTree(resource);

            return;
        }

        if (house != null)
        {
            SelectHighlight(house.gameObject);

            if (selectionUI != null)
                selectionUI.ShowHouse(house);

            return;
        }

        if (storage != null)
        {
            SelectHighlight(storage.gameObject);

            if (selectionUI != null)
                selectionUI.ShowBarn(storage);

            return;
        }

        ClearHighlight();
        HideCurrentUI();
    }

    private void HideCurrentUI()
    {
        if (selectionUI != null)
            selectionUI.HideCurrent();
    }

    private void SelectHighlight(GameObject target)
    {
        if (selectionHighlight != null)
            selectionHighlight.Select(target);
    }

    private void ClearHighlight()
    {
        if (selectionHighlight != null)
            selectionHighlight.Clear();
    }
}
