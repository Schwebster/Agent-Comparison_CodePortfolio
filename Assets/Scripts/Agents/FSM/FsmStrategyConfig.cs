using System;
using UnityEngine;

[Serializable]
public sealed class FsmStrategyConfig
{
    private const float MinimumPositiveValue = 0.01f;

    [SerializeField, Min(0f)] private float criticalReserveDays = 1f;
    [SerializeField, Min(0f)] private float recoveredReserveDays = 2f;
    [SerializeField, Range(0.5f, 1f)] private float emergencyWorkerShare = 0.6f;
    [SerializeField, Min(0)] private int maximumBuilders = 1;
    [SerializeField, Min(0f)] private float populationFoodReserveDays = 1f;
    [SerializeField, Min(MinimumPositiveValue)] private float reevaluationIntervalWorldHours = 0.25f;
    [SerializeField, Min(0f)] private float minimumStateDurationWorldHours = 0.5f;

    public float CriticalReserveDays => Mathf.Max(0f, criticalReserveDays);
    public float RecoveredReserveDays => Mathf.Max(CriticalReserveDays, recoveredReserveDays);
    public float EmergencyWorkerShare => Mathf.Clamp(emergencyWorkerShare, 0.5f, 1f);
    public int MaximumBuilders => Mathf.Max(0, maximumBuilders);
    public float PopulationFoodReserveDays => Mathf.Max(0f, populationFoodReserveDays);
    public float ReevaluationIntervalWorldHours => Mathf.Max(MinimumPositiveValue, reevaluationIntervalWorldHours);
    public float MinimumStateDurationWorldHours => Mathf.Max(0f, minimumStateDurationWorldHours);

    public void Validate()
    {
        criticalReserveDays = Mathf.Max(0f, criticalReserveDays);
        recoveredReserveDays = Mathf.Max(criticalReserveDays, recoveredReserveDays);
        emergencyWorkerShare = Mathf.Clamp(emergencyWorkerShare, 0.5f, 1f);
        maximumBuilders = Mathf.Max(0, maximumBuilders);
        populationFoodReserveDays = Mathf.Max(0f, populationFoodReserveDays);
        reevaluationIntervalWorldHours = Mathf.Max(MinimumPositiveValue, reevaluationIntervalWorldHours);
        minimumStateDurationWorldHours = Mathf.Max(0f, minimumStateDurationWorldHours);
    }
}
