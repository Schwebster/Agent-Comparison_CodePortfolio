/*
===============================================================================
Script:
WorldTimeSettingsPanelController

Purpose:
Controls the world time settings panel and forwards runtime duration changes.

Folder:
Assets/Scripts/UI/

Scene Hierarchy:
_UI
- ScreenSpaceCanvas
  - WorldTimePanel
    - WorldTimeButton
    - WorldTimeConfigPanel
      - DayDurationLabel
      - DayDurationInput
      - ChangeButton
      - PauseButton
      - CloseButton

Attach To:
WorldTimePanel

Required Children:
- WorldTimeButton
- WorldTimeConfigPanel
- DayDurationLabel
- DayDurationInput
- ChangeButton
- PauseButton
- CloseButton

Dependencies:
- WorldTimeManager
- TMP_InputField
- Button

Automatically Resolved:
- WorldTimeManager in scene

Inspector References:
- worldTimeManager
- settingsPanel
- worldTimeButton
- dayDurationLabel
- dayDurationInput
- changeButton
- pauseButton
- pauseButtonText
- closeButton
===============================================================================
*/

using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WorldTimeSettingsPanelController : MonoBehaviour
{
    private const string PauseButtonTextWhenRunning = "Pause";
    private const string PauseButtonTextWhenPaused = "Play";

    [SerializeField] private WorldTimeManager worldTimeManager;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Button worldTimeButton;
    [SerializeField] private TMP_Text dayDurationLabel;
    [SerializeField] private TMP_InputField dayDurationInput;
    [SerializeField] private Button changeButton;
    [SerializeField] private Button pauseButton;
    [SerializeField] private TMP_Text pauseButtonText;
    [SerializeField] private Button closeButton;

    private void Reset()
    {
        AutoWireReferences();
    }

    private void Awake()
    {
        AutoWireReferences();

        Debug.Assert(
            worldTimeManager != null,
            $"Missing component: WorldTimeManager. Expected location: active or inactive scene object. Script: {nameof(WorldTimeSettingsPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            settingsPanel != null,
            $"Missing child GameObject named WorldTimeConfigPanel. Expected location: child of WorldTimePanel. Script: {nameof(WorldTimeSettingsPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            worldTimeButton != null,
            $"Missing component: Button named WorldTimeButton. Expected location: child of WorldTimePanel. Script: {nameof(WorldTimeSettingsPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            dayDurationLabel != null,
            $"Missing component: TMP_Text named DayDurationLabel. Expected location: child of WorldTimeConfigPanel. Script: {nameof(WorldTimeSettingsPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            dayDurationInput != null,
            $"Missing component: TMP_InputField named DayDurationInput. Expected location: child of WorldTimeConfigPanel. Script: {nameof(WorldTimeSettingsPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            changeButton != null,
            $"Missing component: Button named ChangeButton. Expected location: child of WorldTimeConfigPanel. Script: {nameof(WorldTimeSettingsPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            pauseButton != null,
            $"Missing component: Button named PauseButton. Expected location: child of WorldTimeConfigPanel. Script: {nameof(WorldTimeSettingsPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            pauseButtonText != null,
            $"Missing component: TMP_Text under PauseButton. Expected location: child of PauseButton. Script: {nameof(WorldTimeSettingsPanelController)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            closeButton != null,
            $"Missing component: Button named CloseButton. Expected location: child of WorldTimeConfigPanel. Script: {nameof(WorldTimeSettingsPanelController)}. GameObject: {gameObject.name}."
        );

        RegisterEvents();
    }

    private void Start()
    {
        HideSettingsPanel();
        RefreshDayDurationControls();
        RefreshPauseButtonText();
    }

    private void OnDestroy()
    {
        UnregisterEvents();
    }

    public void ToggleSettingsPanel()
    {
        if (settingsPanel == null)
            return;

        bool showPanel = !settingsPanel.activeSelf;
        settingsPanel.SetActive(showPanel);

        if (showPanel)
        {
            RefreshDayDurationControls();
            RefreshPauseButtonText();
        }
    }

    public void HideSettingsPanel()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    private void RegisterEvents()
    {
        if (worldTimeButton != null)
        {
            worldTimeButton.onClick.RemoveListener(ToggleSettingsPanel);
            worldTimeButton.onClick.AddListener(ToggleSettingsPanel);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(HideSettingsPanel);
            closeButton.onClick.AddListener(HideSettingsPanel);
        }

        if (changeButton != null)
        {
            changeButton.onClick.RemoveListener(ApplyDayDurationInput);
            changeButton.onClick.AddListener(ApplyDayDurationInput);
        }

        if (pauseButton != null)
        {
            pauseButton.onClick.RemoveListener(TogglePause);
            pauseButton.onClick.AddListener(TogglePause);
        }
    }

    private void UnregisterEvents()
    {
        if (worldTimeButton != null)
            worldTimeButton.onClick.RemoveListener(ToggleSettingsPanel);

        if (closeButton != null)
            closeButton.onClick.RemoveListener(HideSettingsPanel);

        if (changeButton != null)
            changeButton.onClick.RemoveListener(ApplyDayDurationInput);

        if (pauseButton != null)
            pauseButton.onClick.RemoveListener(TogglePause);
    }

    private void ApplyDayDurationInput()
    {
        if (worldTimeManager == null)
        {
            RefreshDayDurationControls();
            return;
        }

        string inputValue = dayDurationInput != null ? dayDurationInput.text : string.Empty;
        if (!TryParsePositiveSeconds(inputValue, out float seconds))
        {
            RefreshDayDurationControls();
            return;
        }

        worldTimeManager.SetDayDurationRealSeconds(seconds);
        RefreshDayDurationControls();
    }

    private void TogglePause()
    {
        if (worldTimeManager == null)
            return;

        worldTimeManager.TogglePause();
        RefreshPauseButtonText();
    }

    private bool TryParsePositiveSeconds(string inputValue, out float seconds)
    {
        return float.TryParse(inputValue, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
            && seconds > 0f;
    }

    private void RefreshDayDurationControls()
    {
        if (worldTimeManager == null)
            return;

        string value = GetFormattedDayDuration();
        RefreshDayDurationInput(value);
        RefreshDayDurationLabel(value);
    }

    private void RefreshDayDurationInput(string value)
    {
        if (dayDurationInput != null)
            dayDurationInput.SetTextWithoutNotify(value);
    }

    private void RefreshDayDurationLabel(string value)
    {
        if (dayDurationLabel != null)
            dayDurationLabel.SetText($"Day Duration: {value} s");
    }

    private string GetFormattedDayDuration()
    {
        return worldTimeManager.DayDurationRealSeconds.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void RefreshPauseButtonText()
    {
        if (worldTimeManager == null || pauseButtonText == null)
            return;

        pauseButtonText.text = worldTimeManager.IsPaused
            ? PauseButtonTextWhenPaused
            : PauseButtonTextWhenRunning;
    }

    [ContextMenu("Auto Wire References")]
    private void AutoWireReferences()
    {
        if (worldTimeManager == null)
            worldTimeManager = FindFirstObjectByType<WorldTimeManager>(FindObjectsInactive.Include);

        if (settingsPanel == null)
            settingsPanel = FindChild("WorldTimeConfigPanel")?.gameObject;

        if (worldTimeButton == null)
            worldTimeButton = FindChildComponent<Button>("WorldTimeButton");

        if (dayDurationLabel == null)
            dayDurationLabel = FindChildComponent<TMP_Text>("DayDurationLabel");

        if (dayDurationInput == null)
            dayDurationInput = FindChildComponent<TMP_InputField>("DayDurationInput");

        if (changeButton == null)
            changeButton = FindChildComponent<Button>("ChangeButton");

        if (pauseButton == null)
            pauseButton = FindChildComponent<Button>("PauseButton");

        if (pauseButtonText == null && pauseButton != null)
            pauseButtonText = pauseButton.GetComponentInChildren<TMP_Text>(true);

        if (closeButton == null)
            closeButton = FindChildComponent<Button>("CloseButton");
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
