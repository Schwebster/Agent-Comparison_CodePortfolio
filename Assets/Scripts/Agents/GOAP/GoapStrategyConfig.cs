/*
===============================================================================
Script:
GoapStrategyConfig

Purpose:
Stores the small set of inspectable GOAP goal priorities and reserve thresholds.

Folder:
Assets/Scripts/Agents/GOAP/

Attach To:
Serialized inside GoapAgentController
===============================================================================
*/

using System;
using UnityEngine;

[Serializable]
public sealed class GoapStrategyConfig
{
    private const float MinimumPositiveValue = 0.01f;

    [SerializeField, Min(0f)] private float safeReserveDays = 2f;
    [SerializeField] private int foodReservePriority = 400;
    [SerializeField] private int woodReservePriority = 350;
    [SerializeField] private int constructionPriority = 200;
    [SerializeField] private int explorationPriority = 100;
    [SerializeField, Min(MinimumPositiveValue)] private float retryIntervalWorldHours = 0.05f;
    [SerializeField, Min(MinimumPositiveValue)] private float failedActionCooldownWorldHours = 0.5f;
    [SerializeField, Min(0f)] private float populationFoodReserveDays = 1f;

    public float SafeReserveDays => Mathf.Max(0f, safeReserveDays);
    public int FoodReservePriority => foodReservePriority;
    public int WoodReservePriority => woodReservePriority;
    public int ConstructionPriority => constructionPriority;
    public int ExplorationPriority => explorationPriority;
    public float RetryIntervalWorldHours => Mathf.Max(MinimumPositiveValue, retryIntervalWorldHours);
    public float FailedActionCooldownWorldHours => Mathf.Max(RetryIntervalWorldHours, failedActionCooldownWorldHours);
    public float PopulationFoodReserveDays => Mathf.Max(0f, populationFoodReserveDays);

    public void Validate()
    {
        safeReserveDays = Mathf.Max(0f, safeReserveDays);
        retryIntervalWorldHours = Mathf.Max(MinimumPositiveValue, retryIntervalWorldHours);
        failedActionCooldownWorldHours = Mathf.Max(retryIntervalWorldHours, failedActionCooldownWorldHours);
        populationFoodReserveDays = Mathf.Max(0f, populationFoodReserveDays);
    }
}
