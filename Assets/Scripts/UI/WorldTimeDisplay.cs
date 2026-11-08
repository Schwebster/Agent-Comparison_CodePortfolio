/*
===============================================================================
Script:
WorldTimeDisplay

Purpose:
Displays the current world day and time from WorldTimeManager.

Folder:
Assets/Scripts/UI/

Scene Hierarchy:
_UI
- ScreenSpaceCanvas
  - WorldTimePanel
    - WorldTimeText

Attach To:
WorldTimeText

Required Children:
None

Dependencies:
- WorldTimeManager
- TextMeshProUGUI

Automatically Resolved:
- TextMeshProUGUI on this GameObject
- WorldTimeManager in scene

Inspector References:
- worldTimeText
- worldTimeManager
===============================================================================
*/

using TMPro;
using UnityEngine;

public class WorldTimeDisplay : MonoBehaviour
{
    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;

    [SerializeField] private TextMeshProUGUI worldTimeText;
    [SerializeField] private WorldTimeManager worldTimeManager;

    private int displayedDay = -1;
    private int displayedMinuteOfDay = -1;

    private void Reset()
    {
        AutoWireReferences();
    }

    private void Awake()
    {
        AutoWireReferences();

        Debug.Assert(
            worldTimeText != null,
            $"Missing component: TextMeshProUGUI. Expected location: same GameObject as WorldTimeDisplay. Script: {nameof(WorldTimeDisplay)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            worldTimeManager != null,
            $"Missing component: WorldTimeManager. Expected location: active or inactive scene object. Script: {nameof(WorldTimeDisplay)}. GameObject: {gameObject.name}."
        );
    }

    private void LateUpdate()
    {
        UpdateDisplay();
    }

    [ContextMenu("Auto Wire References")]
    private void AutoWireReferences()
    {
        if (worldTimeText == null)
            worldTimeText = GetComponent<TextMeshProUGUI>();

        if (worldTimeManager == null)
            worldTimeManager = FindFirstObjectByType<WorldTimeManager>(FindObjectsInactive.Include);
    }

    private void UpdateDisplay()
    {
        if (worldTimeText == null || worldTimeManager == null)
            return;

        int currentDay = worldTimeManager.CurrentDay;
        int minuteOfDay = GetMinuteOfDay(worldTimeManager.CurrentHour);

        if (currentDay == displayedDay && minuteOfDay == displayedMinuteOfDay)
            return;

        displayedDay = currentDay;
        displayedMinuteOfDay = minuteOfDay;

        int hour = minuteOfDay / MinutesPerHour;
        int minute = minuteOfDay % MinutesPerHour;

        worldTimeText.SetText("Day {0} | {1:00}:{2:00}", currentDay, hour, minute);
    }

    private int GetMinuteOfDay(float currentHour)
    {
        int minuteOfDay = Mathf.FloorToInt(currentHour * MinutesPerHour);
        return Mathf.Clamp(minuteOfDay, 0, HoursPerDay * MinutesPerHour - 1);
    }
}
