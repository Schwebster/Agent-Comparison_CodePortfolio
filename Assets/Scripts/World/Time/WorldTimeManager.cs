/*
===============================================================================
Script:
WorldTimeManager

Purpose:
Provides the single runtime source of truth for world simulation time.

Folder:
Assets/Scripts/World/Time/

Scene Hierarchy:
_Systems
- WorldTimeManager

Attach To:
WorldTimeManager

Required Children:
None

Dependencies:
- WorldTimeConfig
- ResourceRegistry

Automatically Resolved:
None

Inspector References:
- config
===============================================================================
*/

using UnityEngine;

public class WorldTimeManager : MonoBehaviour
{
    private const float DefaultDayDurationRealSeconds = 600f;
    private const float MinimumDayDurationRealSeconds = 1f;
    private const float DefaultStartHour = 6f;

    [SerializeField] private WorldTimeConfig config;

    private float totalWorldHours;
    private float dayDurationRealSeconds = DefaultDayDurationRealSeconds;
    private float referenceDayDurationRealSeconds = DefaultDayDurationRealSeconds;
    private float deltaWorldHours;
    private float deltaWorldSeconds;
    private bool isPaused = true;

    public float CurrentHour => WorldTimeConfig.NormalizeHour(totalWorldHours);
    public float TotalWorldHours => totalWorldHours;
    public int CurrentDay => Mathf.FloorToInt(totalWorldHours / WorldTimeConfig.HoursPerDay) + 1;
    public float NormalizedDayTime => CurrentHour / WorldTimeConfig.HoursPerDay;
    public float DeltaWorldHours => deltaWorldHours;
    public float DeltaWorldSeconds => deltaWorldSeconds;
    public float WorldHoursPerRealSecond => BaseWorldHoursPerRealSecond;
    public float BaseWorldHoursPerRealSecond => WorldTimeConfig.HoursPerDay / DayDurationRealSeconds;
    public float DayDurationRealSeconds => dayDurationRealSeconds;
    public float ReferenceDayDurationRealSeconds => referenceDayDurationRealSeconds;
    public bool IsPaused => isPaused;

    private void Awake()
    {
        WarnIfMultipleManagersExist();
        InitializeFromConfig();
    }

    private void Update()
    {
        AdvanceFromRealDelta(Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        SimulationEventLogger.Flush();
    }

    public void SetTime(float hour)
    {
        int currentDayIndex = Mathf.Max(0, CurrentDay - 1);
        totalWorldHours = currentDayIndex * WorldTimeConfig.HoursPerDay + WorldTimeConfig.NormalizeHour(hour);
        ResetDelta();
    }

    public void AddWorldHours(float hours)
    {
        if (isPaused)
        {
            ResetDelta();
            return;
        }

        float previousWorldHours = totalWorldHours;
        AddWorldHoursInternal(hours);
        SetDelta(totalWorldHours - previousWorldHours);
    }

    public void SetDayDurationRealSeconds(float seconds)
    {
        dayDurationRealSeconds = Mathf.Max(MinimumDayDurationRealSeconds, seconds);
    }

    public void SetReferenceDayDurationRealSeconds(float seconds)
    {
        referenceDayDurationRealSeconds = Mathf.Max(MinimumDayDurationRealSeconds, seconds);
    }

    public void Pause()
    {
        isPaused = true;
        ResetDelta();
    }

    public void Resume()
    {
        isPaused = false;
        ResetDelta();
    }

    public void TogglePause()
    {
        if (isPaused)
            Resume();
        else
            Pause();
    }

    private void InitializeFromConfig()
    {
        totalWorldHours = WorldTimeConfig.NormalizeHour(GetInitialStartHour());
        float initialDayDurationRealSeconds = GetInitialDayDurationRealSeconds();
        SetReferenceDayDurationRealSeconds(initialDayDurationRealSeconds);
        SetDayDurationRealSeconds(initialDayDurationRealSeconds);
        Pause();

        if (config == null)
            Debug.LogWarning($"WorldTimeManager is using default time settings because no WorldTimeConfig is assigned. GameObject: {gameObject.name}.");
    }

    private float GetInitialStartHour()
    {
        return config != null ? config.StartHour : DefaultStartHour;
    }

    private float GetInitialDayDurationRealSeconds()
    {
        return config != null ? config.DayDurationRealSeconds : DefaultDayDurationRealSeconds;
    }

    private void AdvanceFromRealDelta(float realDeltaSeconds)
    {
        if (isPaused)
        {
            ResetDelta();
            return;
        }

        float worldHours = Mathf.Max(0f, realDeltaSeconds) * WorldHoursPerRealSecond;
        AddWorldHoursInternal(worldHours);
        SetDelta(worldHours);
    }

    private void AddWorldHoursInternal(float hours)
    {
        totalWorldHours = Mathf.Max(0f, totalWorldHours + hours);
    }

    private void SetDelta(float worldHours)
    {
        deltaWorldHours = worldHours;
        deltaWorldSeconds = worldHours * WorldTimeConfig.SecondsPerHour;
    }

    private void ResetDelta()
    {
        deltaWorldHours = 0f;
        deltaWorldSeconds = 0f;
    }

    private void WarnIfMultipleManagersExist()
    {
        WorldTimeManager[] managers = FindObjectsByType<WorldTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (managers.Length > 1)
            Debug.LogWarning($"Found {managers.Length} WorldTimeManager instances. World time should have one source of truth.");
    }
}
