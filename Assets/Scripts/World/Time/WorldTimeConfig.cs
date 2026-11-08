/*
===============================================================================
Script:
WorldTimeConfig

Purpose:
Stores configurable world time settings for deterministic simulation timing.

Folder:
Assets/Scripts/World/Time/

Scene Hierarchy:
Configured as a project asset

Attach To:
None

Required Children:
None

Dependencies:
None

Automatically Resolved:
None

Inspector References:
- dayDurationRealSeconds
- startHour
===============================================================================
*/

using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "WorldTimeConfig", menuName = "Agent Comparison/World Time Config")]
public class WorldTimeConfig : ScriptableObject, ISerializationCallbackReceiver
{
    private const float MinimumDayDurationRealSeconds = 0.01f;
    private const float SecondsPerMinute = 60f;
    private const float LegacyDurationNotSet = -1f;

    public const float HoursPerDay = 24f;
    public const float SecondsPerHour = 3600f;

    [Header("World Time")]
    [Tooltip("Real seconds required for one complete 24-hour world day.")]
    [SerializeField] private float dayDurationRealSeconds = 600f;
    [Tooltip("World hour used when the simulation starts.")]
    [SerializeField] private float startHour = 6f;

    [SerializeField]
    [HideInInspector]
    [FormerlySerializedAs("dayDurationRealMinutes")]
    private float legacyDayDurationRealMinutes = LegacyDurationNotSet;

    public float DayDurationRealSeconds => Mathf.Max(MinimumDayDurationRealSeconds, dayDurationRealSeconds);
    public float StartHour => NormalizeHour(startHour);

    public static float NormalizeHour(float hour)
    {
        float normalizedHour = hour % HoursPerDay;

        if (normalizedHour < 0f)
            normalizedHour += HoursPerDay;

        return normalizedHour;
    }

    public void OnBeforeSerialize()
    {
    }

    public void OnAfterDeserialize()
    {
        MigrateLegacyDuration();
    }

    private void OnValidate()
    {
        MigrateLegacyDuration();
        dayDurationRealSeconds = Mathf.Max(MinimumDayDurationRealSeconds, dayDurationRealSeconds);
        startHour = NormalizeHour(startHour);
    }

    private void MigrateLegacyDuration()
    {
        if (legacyDayDurationRealMinutes <= 0f)
            return;

        dayDurationRealSeconds = Mathf.Max(MinimumDayDurationRealSeconds, legacyDayDurationRealMinutes * SecondsPerMinute);
        legacyDayDurationRealMinutes = LegacyDurationNotSet;
    }
}
