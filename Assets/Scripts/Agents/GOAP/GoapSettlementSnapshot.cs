/*
===============================================================================
Script:
GoapSettlementSnapshot

Purpose:
Captures the shared settlement facts that the GOAP decision layer may observe.

Folder:
Assets/Scripts/Agents/GOAP/

Attach To:
None
===============================================================================
*/

using System.Collections.Generic;
using System.Globalization;
using AgentComparison.Goap;
using UnityEngine;

public readonly struct GoapSettlementSnapshot
{
    public int LivingNpcCount { get; }
    public int StoredFood { get; }
    public int StoredWood { get; }
    public int DailyFoodNeed { get; }
    public int DailyWoodNeed { get; }
    public int UnrevealedFogCount { get; }
    public bool HasFoodSourceAvailable { get; }
    public bool HasWoodSourceAvailable { get; }
    public bool HasExplorationTargetAvailable { get; }
    public bool HasActiveConstructionSite { get; }
    public bool HasConstructionSiteAvailable { get; }

    public float FoodReserveDays => DailyFoodNeed > 0
        ? (float)StoredFood / DailyFoodNeed
        : float.PositiveInfinity;

    public float WoodReserveDays => DailyWoodNeed > 0
        ? (float)StoredWood / DailyWoodNeed
        : float.PositiveInfinity;

    private GoapSettlementSnapshot(
        int livingNpcCount,
        int storedFood,
        int storedWood,
        int dailyFoodNeed,
        int dailyWoodNeed,
        int unrevealedFogCount,
        bool hasFoodSourceAvailable,
        bool hasWoodSourceAvailable,
        bool hasExplorationTargetAvailable,
        bool hasActiveConstructionSite,
        bool hasConstructionSiteAvailable)
    {
        LivingNpcCount = livingNpcCount;
        StoredFood = storedFood;
        StoredWood = storedWood;
        DailyFoodNeed = dailyFoodNeed;
        DailyWoodNeed = dailyWoodNeed;
        UnrevealedFogCount = unrevealedFogCount;
        HasFoodSourceAvailable = hasFoodSourceAvailable;
        HasWoodSourceAvailable = hasWoodSourceAvailable;
        HasExplorationTargetAvailable = hasExplorationTargetAvailable;
        HasActiveConstructionSite = hasActiveConstructionSite;
        HasConstructionSiteAvailable = hasConstructionSiteAvailable;
    }

    public static GoapSettlementSnapshot Capture(IReadOnlyList<GoapAgentController> agents)
    {
        int livingNpcCount = 0;
        int dailyFoodNeed = 0;
        int dailyWoodNeed = 0;

        if (agents != null)
        {
            for (int i = 0; i < agents.Count; i++)
            {
                GoapAgentController agent = agents[i];
                if (agent == null || !agent.isActiveAndEnabled || agent.IsDead)
                    continue;

                livingNpcCount++;
                dailyFoodNeed += agent.DailyFoodRequirement;
                dailyWoodNeed += agent.DailyWoodRequirement;
            }
        }

        return new GoapSettlementSnapshot(
            livingNpcCount,
            ResourceStorageRegistry.SharedBerriesStored,
            ResourceStorageRegistry.SharedWoodStored,
            dailyFoodNeed,
            dailyWoodNeed,
            FogRegistry.CountUnrevealedFog(),
            ResourceRegistry.HasAvailableResource(ResourceNode.ResourceType.Berries),
            ResourceRegistry.HasAvailableResource(ResourceNode.ResourceType.Wood),
            FogRegistry.HasAvailableFrontierFog(),
            ConstructionSiteRegistry.HasActiveConstructionSite(),
            ConstructionSiteRegistry.HasAvailableConstructionSite()
        );
    }

    public GoapWorldState CreateWorldState(float safeReserveDays, bool hasDepositLoad, bool storageAvailable)
    {
        float requiredReserveDays = Mathf.Max(0f, safeReserveDays);

        return GoapWorldState.Empty
            .With(GoapFact.FoodReserveSafe, FoodReserveDays >= requiredReserveDays)
            .With(GoapFact.WoodReserveSafe, WoodReserveDays >= requiredReserveDays)
            .With(GoapFact.FoodSourceAvailable, HasFoodSourceAvailable)
            .With(GoapFact.WoodSourceAvailable, HasWoodSourceAvailable)
            .With(GoapFact.UnrevealedAreaAvailable, HasExplorationTargetAvailable)
            .With(GoapFact.ConstructionSiteAvailable, HasConstructionSiteAvailable)
            .With(GoapFact.HasDepositLoad, hasDepositLoad)
            .With(GoapFact.StorageAvailable, storageAvailable);
    }

    public string GetSummary()
    {
        return
            $"Food={StoredFood}/{DailyFoodNeed} ({FormatReserveDays(FoodReserveDays)}d), " +
            $"Wood={StoredWood}/{DailyWoodNeed} ({FormatReserveDays(WoodReserveDays)}d), " +
            $"Fog={UnrevealedFogCount}, Construction={HasActiveConstructionSite}";
    }

    private static string FormatReserveDays(float reserveDays)
    {
        return float.IsPositiveInfinity(reserveDays)
            ? "inf"
            : reserveDays.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
