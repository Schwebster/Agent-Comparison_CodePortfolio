/*
===============================================================================
Script:
SimulationSpeedCalculator

Purpose:
Calculates runtime simulation speed values from world time and balance settings.

Folder:
Assets/Scripts/Simulation/

Scene Hierarchy:
Used by runtime simulation systems

Attach To:
None

Required Children:
None

Dependencies:
- WorldTimeManager
- SimulationBalanceConfig

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/

using UnityEngine;

public static class SimulationSpeedCalculator
{
    private const float DefaultSpeedFactor = 1f;

    public static float GetSimulationSpeedFactor(WorldTimeManager worldTimeManager, SimulationBalanceConfig balanceConfig)
    {
        if (worldTimeManager == null || balanceConfig == null)
            return DefaultSpeedFactor;

        if (worldTimeManager.IsPaused)
            return 0f;

        float referenceDayDurationRealSeconds = worldTimeManager.ReferenceDayDurationRealSeconds > 0f
            ? worldTimeManager.ReferenceDayDurationRealSeconds
            : balanceConfig.ReferenceDayDurationRealSeconds;

        if (referenceDayDurationRealSeconds <= 0f || worldTimeManager.DayDurationRealSeconds <= 0f)
            return DefaultSpeedFactor;

        return Mathf.Max(0f, referenceDayDurationRealSeconds / worldTimeManager.DayDurationRealSeconds);
    }

    public static float GetMovementSpeed(WorldTimeManager worldTimeManager, SimulationBalanceConfig balanceConfig)
    {
        if (balanceConfig == null)
            return 0f;

        return balanceConfig.BaseMovementSpeed * GetSimulationSpeedFactor(worldTimeManager, balanceConfig);
    }

    public static float GetAnimatorSpeed(WorldTimeManager worldTimeManager, SimulationBalanceConfig balanceConfig)
    {
        if (balanceConfig == null)
            return 0f;

        return balanceConfig.BaseAnimatorSpeed * GetSimulationSpeedFactor(worldTimeManager, balanceConfig);
    }
}
