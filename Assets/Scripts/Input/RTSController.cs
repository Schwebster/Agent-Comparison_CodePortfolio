/*
===============================================================================
Script:
RTSController

Purpose:
Routes player mouse input to world selection and character command systems.

Folder:
Assets/Scripts/Input/

Scene Hierarchy:
_Systems
- GameManager

Attach To:
GameManager

Required Children:
None

Dependencies:
- WorldSelectionController
- CharacterCommandController
- EventSystem
- Unity Input System

Automatically Resolved:
- WorldSelectionController on the same GameObject
- CharacterCommandController on the same GameObject

Inspector References:
None
===============================================================================
*/
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class RTSController : MonoBehaviour
{
    private WorldSelectionController worldSelection;
    private CharacterCommandController characterCommands;

    private void Awake()
    {
        worldSelection = GetComponent<WorldSelectionController>();
        characterCommands = GetComponent<CharacterCommandController>();

        Debug.Assert(
            worldSelection != null,
            $"Missing component: WorldSelectionController. Expected location: same GameObject as RTSController. Script: {nameof(RTSController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            characterCommands != null,
            $"Missing component: CharacterCommandController. Expected location: same GameObject as RTSController. Script: {nameof(RTSController)}. GameObject: {gameObject.name}."
        );
    }

    private void Update()
    {
        if (Mouse.current == null)
            return;

        if (IsPointerOverUI())
            return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
            HandleLeftClick(Mouse.current.position.ReadValue());

        if (Mouse.current.rightButton.wasPressedThisFrame)
            HandleRightClick(Mouse.current.position.ReadValue());
    }

    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void HandleLeftClick(Vector2 screenPosition)
    {
        if (worldSelection != null)
            worldSelection.HandleLeftClick(screenPosition);
    }

    private void HandleRightClick(Vector2 screenPosition)
    {
        if (characterCommands != null)
            characterCommands.HandleRightClick(screenPosition);
    }
}
