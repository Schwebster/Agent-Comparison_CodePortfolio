/*
===============================================================================
Script:
CharacterDetailsPanelController

Purpose:
Controls the screen-space character details and carried inventory panel.

Folder:
Assets/Scripts/UI/

Scene Hierarchy:
_UI
- ScreenSpaceCanvas
  - CharacterDetailsPanel

Attach To:
CharacterDetailsPanel

Required Children:
- CharacterNameText
- InventoryTitleText
- InventoryContentText
- HpBar
- HpBar/HpCurrentFill
- CharacterDetailsCloseButton or InventoryCloseButton

Dependencies:
- Character
- NpcHealth

Automatically Resolved:
- CharacterNameText
- InventoryTitleText
- InventoryContentText
- HpBar
- HpCurrentFill
- CharacterDetailsCloseButton or InventoryCloseButton

Inspector References:
- hpBar
- hpFillImage
===============================================================================
*/
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterDetailsPanelController : MonoBehaviour
{
    [SerializeField] private GameObject hpBar;
    [SerializeField] private Image hpFillImage;

    private TMP_Text characterNameText;
    private TMP_Text inventoryTitleText;
    private TMP_Text inventoryContentText;
    private Button closeButton;
    private Character shownCharacter;
    private bool missingHpFillWarningLogged;
    private bool hpFillTypeWarningLogged;

    private void Reset()
    {
        AutoWireReferences();
    }

    private void Awake()
    {
        AutoWireReferences();

        Debug.Assert(
            characterNameText != null,
            $"Missing component: TMP_Text named CharacterNameText. Expected location: child of CharacterDetailsPanel. Script: {nameof(CharacterDetailsPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            inventoryTitleText != null,
            $"Missing component: TMP_Text named InventoryTitleText. Expected location: child of CharacterDetailsPanel. Script: {nameof(CharacterDetailsPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            inventoryContentText != null,
            $"Missing component: TMP_Text named InventoryContentText. Expected location: child of CharacterDetailsPanel. Script: {nameof(CharacterDetailsPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            closeButton != null,
            $"Missing component: Button named CharacterDetailsCloseButton or InventoryCloseButton. Expected location: child of CharacterDetailsPanel. Script: {nameof(CharacterDetailsPanelController)}. GameObject: {gameObject.name}."
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

    public void SetCharacter(Character character)
    {
        AutoWireReferences();

        shownCharacter = character;
        UpdatePanel();
    }

    public void Show()
    {
        AutoWireReferences();

        if (shownCharacter == null)
            return;

        gameObject.SetActive(true);
        UpdatePanel();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void UpdatePanel()
    {
        if (shownCharacter == null || !gameObject.activeSelf)
            return;

        if (characterNameText != null)
            characterNameText.text = shownCharacter.gameObject.name;

        if (inventoryTitleText != null)
            inventoryTitleText.text = "Inventory";

        if (inventoryContentText != null)
            inventoryContentText.text = $"Wood: {shownCharacter.CarriedWood}\nBerries: {shownCharacter.CarriedBerries}";

        UpdateHealthBar();
    }

    private void UpdateHealthBar()
    {
        if (shownCharacter == null)
        {
            SetHealthBarVisible(false);
            SetHealthFill(0f);
            return;
        }

        NpcHealth health = shownCharacter.GetComponent<NpcHealth>();
        if (health == null)
        {
            SetHealthBarVisible(false);
            SetHealthFill(0f);
            return;
        }

        SetHealthBarVisible(true);

        float ratio = Mathf.Clamp01(health.CurrentHealth / Mathf.Max(0.01f, health.MaxHealth));
        SetHealthFill(ratio);
    }

    private void SetHealthBarVisible(bool visible)
    {
        if (hpBar != null)
            hpBar.SetActive(visible);
    }

    private void SetHealthFill(float ratio)
    {
        if (hpFillImage == null)
        {
            if (!missingHpFillWarningLogged)
            {
                Debug.LogWarning($"Character details HP fill is not assigned. Assign CharacterDetailsPanel/HpBar/HpCurrentFill to hpFillImage. GameObject: {gameObject.name}.");
                missingHpFillWarningLogged = true;
            }

            return;
        }

        if (hpFillImage.type != Image.Type.Filled)
        {
            if (!hpFillTypeWarningLogged)
            {
                Debug.LogWarning($"Character details HP fill should use Image Type Filled, Horizontal, Left. GameObject: {hpFillImage.gameObject.name}.");
                hpFillTypeWarningLogged = true;
            }

            hpFillImage.rectTransform.localScale = new Vector3(Mathf.Clamp01(ratio), 1f, 1f);
            return;
        }

        hpFillImage.fillAmount = Mathf.Clamp01(ratio);
    }

    private void RegisterButtonEvents()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);
    }

    private void UnregisterButtonEvents()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Hide);
    }

    [ContextMenu("Auto Wire References")]
    private void AutoWireReferences()
    {
        if (characterNameText == null)
            characterNameText = FindChildComponent<TMP_Text>("CharacterNameText");

        if (inventoryTitleText == null)
            inventoryTitleText = FindChildComponent<TMP_Text>("InventoryTitleText");

        if (inventoryContentText == null)
            inventoryContentText = FindChildComponent<TMP_Text>("InventoryContentText");

        if (closeButton == null)
            closeButton = FindChildComponent<Button>("CharacterDetailsCloseButton");

        if (closeButton == null)
            closeButton = FindChildComponent<Button>("InventoryCloseButton");

        if (hpBar == null)
        {
            Transform hpBarTransform = FindChild("HpBar");
            if (hpBarTransform != null)
                hpBar = hpBarTransform.gameObject;
        }

        if (hpFillImage == null)
            hpFillImage = FindChildComponent<Image>("HpCurrentFill");
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
