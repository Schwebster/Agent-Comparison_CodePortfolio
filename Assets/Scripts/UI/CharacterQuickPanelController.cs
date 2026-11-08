/*
===============================================================================
Script:
CharacterQuickPanelController

Purpose:
Controls the world-space quick actions panel for the selected character.

Folder:
Assets/Scripts/UI/

Scene Hierarchy:
_UI
- WorldSpaceCanvas
  - CharacterQuickPanel

Attach To:
CharacterQuickPanel

Required Children:
- CharacterNameText
- DetailsButton
- PossessButton or PosessButton
- PossessButtonText or PosessButtonText

Dependencies:
- Character
- CharacterControlRegistry
- Camera tagged MainCamera

Automatically Resolved:
- CharacterNameText
- DetailsButton
- PossessButton or PosessButton
- PossessButtonText or PosessButtonText
- Main Camera

Inspector References:
None
===============================================================================
*/
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterQuickPanelController : MonoBehaviour
{
    [SerializeField] private Vector3 panelOffset = new Vector3(0f, 1.8f, 0f);

    private TMP_Text characterNameText;
    private Button detailsButton;
    private Button possessButton;
    private TMP_Text possessButtonText;
    private Character shownCharacter;
    private Camera mainCamera;

    public event Action DetailsRequested;

    private void Reset()
    {
        AutoWireReferences();
    }

    private void Awake()
    {
        AutoWireReferences();

        Debug.Assert(
            characterNameText != null,
            $"Missing component: TMP_Text named CharacterNameText. Expected location: child of CharacterQuickPanel. Script: {nameof(CharacterQuickPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            detailsButton != null,
            $"Missing component: Button named DetailsButton. Expected location: child of CharacterQuickPanel. Script: {nameof(CharacterQuickPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            possessButton != null,
            $"Missing component: Button named PossessButton or PosessButton. Expected location: child of CharacterQuickPanel. Script: {nameof(CharacterQuickPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            possessButtonText != null,
            $"Missing component: TMP_Text named PossessButtonText or PosessButtonText. Expected location: child of PossessButton. Script: {nameof(CharacterQuickPanelController)}. GameObject: {gameObject.name}."
        );

        mainCamera = Camera.main;
        RegisterButtonEvents();
    }

    private void OnDestroy()
    {
        UnregisterButtonEvents();
    }

    private void LateUpdate()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        UpdatePanel();
    }

    public void Show(Character character)
    {
        AutoWireReferences();

        shownCharacter = null;

        if (character == null)
            return;

        if (characterNameText == null)
        {
            Debug.LogWarning("Character quick UI is not assigned.");
            return;
        }

        shownCharacter = character;
        gameObject.SetActive(true);
        UpdatePanel();
    }

    public void Hide()
    {
        shownCharacter = null;
        gameObject.SetActive(false);
    }

    public bool IsCharacterControlled(Character character)
    {
        return CharacterControlRegistry.IsPlayerControlled(character);
    }

    private void UpdatePanel()
    {
        if (shownCharacter == null || characterNameText == null)
            return;

        transform.position = GetPanelPosition(shownCharacter.transform);

        if (mainCamera != null)
            transform.rotation = mainCamera.transform.rotation;

        characterNameText.text = shownCharacter.gameObject.name;

        if (possessButtonText != null)
            possessButtonText.text = CharacterControlRegistry.IsPlayerControlled(shownCharacter) ? "Release Control" : "Take Control";
    }

    private void RequestDetails()
    {
        if (shownCharacter == null)
            return;

        DetailsRequested?.Invoke();
    }

    private void ToggleControl()
    {
        if (shownCharacter == null)
            return;

        if (CharacterControlRegistry.IsPlayerControlled(shownCharacter))
            CharacterControlRegistry.ReleasePlayerControl(shownCharacter);
        else
            CharacterControlRegistry.TakePlayerControl(shownCharacter);

        UpdatePanel();
    }

    private Vector3 GetPanelPosition(Transform characterTransform)
    {
        Renderer renderer = characterTransform.GetComponentInChildren<Renderer>();
        if (renderer == null)
            return characterTransform.position + panelOffset;

        return renderer.bounds.center + new Vector3(0f, renderer.bounds.extents.y, 0f) + panelOffset;
    }

    private void RegisterButtonEvents()
    {
        if (detailsButton != null)
            detailsButton.onClick.AddListener(RequestDetails);

        if (possessButton != null)
            possessButton.onClick.AddListener(ToggleControl);
    }

    private void UnregisterButtonEvents()
    {
        if (detailsButton != null)
            detailsButton.onClick.RemoveListener(RequestDetails);

        if (possessButton != null)
            possessButton.onClick.RemoveListener(ToggleControl);
    }

    [ContextMenu("Auto Wire References")]
    private void AutoWireReferences()
    {
        if (characterNameText == null)
            characterNameText = FindChildComponent<TMP_Text>("CharacterNameText");

        if (detailsButton == null)
            detailsButton = FindChildComponent<Button>("DetailsButton");

        if (possessButton == null)
            possessButton = FindChildComponent<Button>("PossessButton");

        if (possessButton == null)
            possessButton = FindChildComponent<Button>("PosessButton");

        if (possessButtonText == null)
            possessButtonText = FindChildComponent<TMP_Text>("PossessButtonText");

        if (possessButtonText == null)
            possessButtonText = FindChildComponent<TMP_Text>("PosessButtonText");
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
