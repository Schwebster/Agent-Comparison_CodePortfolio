/*
===============================================================================
Script:
AgentScheduleConfig

Purpose:
Stores deterministic work and sleep time windows for shared agent policy queries.

Folder:
Assets/Scripts/Agents/Common/Schedule/

Scene Hierarchy:
Serialized through AgentWorkProfile

Attach To:
None

Required Children:
None

Dependencies:
- WorldTimeConfig

Automatically Resolved:
None

Inspector References:
- workStartHour
- workEndHour
- sleepStartHour
- sleepEndHour
===============================================================================
*/

using System;
using UnityEngine;

[Serializable]
public class AgentScheduleConfig
{
    public const float DefaultWorkStartHour = 6f;
    public const float DefaultWorkEndHour = 22f;
    public const float DefaultSleepStartHour = 22f;
    public const float DefaultSleepEndHour = 6f;

    [SerializeField] private float workStartHour = DefaultWorkStartHour;
    [SerializeField] private float workEndHour = DefaultWorkEndHour;
    [SerializeField] private float sleepStartHour = DefaultSleepStartHour;
    [SerializeField] private float sleepEndHour = DefaultSleepEndHour;

    public float WorkStartHour => WorldTimeConfig.NormalizeHour(workStartHour);
    public float WorkEndHour => WorldTimeConfig.NormalizeHour(workEndHour);
    public float SleepStartHour => WorldTimeConfig.NormalizeHour(sleepStartHour);
    public float SleepEndHour => WorldTimeConfig.NormalizeHour(sleepEndHour);

    public bool IsWorkTime(float worldHour)
    {
        return IsHourInRange(worldHour, WorkStartHour, WorkEndHour);
    }

    public bool IsSleepTime(float worldHour)
    {
        return IsHourInRange(worldHour, SleepStartHour, SleepEndHour);
    }

    public void Validate()
    {
        workStartHour = WorldTimeConfig.NormalizeHour(workStartHour);
        workEndHour = WorldTimeConfig.NormalizeHour(workEndHour);
        sleepStartHour = WorldTimeConfig.NormalizeHour(sleepStartHour);
        sleepEndHour = WorldTimeConfig.NormalizeHour(sleepEndHour);
    }

    public static bool IsHourInRange(float worldHour, float startHour, float endHour)
    {
        float hour = WorldTimeConfig.NormalizeHour(worldHour);
        float start = WorldTimeConfig.NormalizeHour(startHour);
        float end = WorldTimeConfig.NormalizeHour(endHour);

        if (Mathf.Approximately(start, end))
            return false;

        if (start < end)
            return hour >= start && hour < end;

        return hour >= start || hour < end;
    }
}
