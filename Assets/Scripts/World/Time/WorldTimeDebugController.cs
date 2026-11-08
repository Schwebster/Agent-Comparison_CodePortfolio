/*
===============================================================================
Script:
WorldTimeDebugController

Purpose:
Provides manual test controls for world time without changing time calculation.

Folder:
Assets/Scripts/World/Time/

Scene Hierarchy:
_Systems
- WorldTimeDebugController

Attach To:
WorldTimeDebugController

Required Children:
None

Dependencies:
- WorldTimeManager

Automatically Resolved:
- WorldTimeManager in scene

Inspector References:
- worldTimeManager
- dayDurationChangeFactor
- minimumDayDurationRealSeconds
- maximumDayDurationRealSeconds
===============================================================================
*/

using UnityEngine;

public class WorldTimeDebugController : MonoBehaviour
{
    private const float MorningHour = 6f;
    private const float NoonHour = 12f;
    private const float EveningHour = 18f;
    private const float MidnightHour = 0f;

    [SerializeField] private WorldTimeManager worldTimeManager;
    [SerializeField] private float dayDurationChangeFactor = 2f;
    [SerializeField] private float minimumDayDurationRealSeconds = 1f;
    [SerializeField] private float maximumDayDurationRealSeconds = 3600f;

    private void Reset()
    {
        AutoWireReferences();
    }

    private void Awake()
    {
        AutoWireReferences();
    }

    [ContextMenu("Jump To Morning")]
    public void JumpToMorning()
    {
        SetTime(MorningHour);
    }

    [ContextMenu("Jump To Noon")]
    public void JumpToNoon()
    {
        SetTime(NoonHour);
    }

    [ContextMenu("Jump To Evening")]
    public void JumpToEvening()
    {
        SetTime(EveningHour);
    }

    [ContextMenu("Jump To Midnight")]
    public void JumpToMidnight()
    {
        SetTime(MidnightHour);
    }

    [ContextMenu("Pause World Time")]
    public void PauseWorldTime()
    {
        if (!TryGetWorldTimeManager(out WorldTimeManager manager))
            return;

        manager.Pause();
    }

    [ContextMenu("Resume World Time")]
    public void ResumeWorldTime()
    {
        if (!TryGetWorldTimeManager(out WorldTimeManager manager))
            return;

        manager.Resume();
    }

    [ContextMenu("Increase Simulation Speed")]
    public void IncreaseSimulationSpeed()
    {
        if (!TryGetWorldTimeManager(out WorldTimeManager manager))
            return;

        SetDayDurationRealSeconds(manager.DayDurationRealSeconds / GetSafeDayDurationChangeFactor());
    }

    [ContextMenu("Decrease Simulation Speed")]
    public void DecreaseSimulationSpeed()
    {
        if (!TryGetWorldTimeManager(out WorldTimeManager manager))
            return;

        SetDayDurationRealSeconds(manager.DayDurationRealSeconds * GetSafeDayDurationChangeFactor());
    }

    public void TogglePause()
    {
        if (TryGetWorldTimeManager(out WorldTimeManager manager))
            manager.TogglePause();
    }

    private void SetTime(float hour)
    {
        if (TryGetWorldTimeManager(out WorldTimeManager manager))
            manager.SetTime(hour);
    }

    private void SetDayDurationRealSeconds(float seconds)
    {
        if (!TryGetWorldTimeManager(out WorldTimeManager manager))
            return;

        float minimumDuration = Mathf.Max(1f, minimumDayDurationRealSeconds);
        float maximumDuration = Mathf.Max(minimumDuration, maximumDayDurationRealSeconds);

        manager.SetDayDurationRealSeconds(Mathf.Clamp(seconds, minimumDuration, maximumDuration));
    }

    private float GetSafeDayDurationChangeFactor()
    {
        return Mathf.Max(1.01f, dayDurationChangeFactor);
    }

    [ContextMenu("Auto Wire References")]
    private void AutoWireReferences()
    {
        if (worldTimeManager == null)
            worldTimeManager = FindFirstObjectByType<WorldTimeManager>(FindObjectsInactive.Include);
    }

    private bool TryGetWorldTimeManager(out WorldTimeManager manager)
    {
        AutoWireReferences();
        manager = worldTimeManager;

        if (manager != null)
            return true;

        Debug.LogWarning($"WorldTimeDebugController cannot control time: WorldTimeManager is missing. GameObject: {gameObject.name}.");
        return false;
    }
}
