using UnityEngine;

public static class SettlementDevelopmentService
{
    public const int HouseWoodCost = 2;
    public const int BarnWoodCost = 3;

    public static int GetPlannedHousingCapacity(int completedHousingCapacity)
    {
        int plannedCapacity = Mathf.Max(0, completedHousingCapacity);
        var constructionSites = ConstructionSiteRegistry.ActiveConstructionSites;

        for (int i = 0; i < constructionSites.Count; i++)
        {
            ConstructionSite constructionSite = constructionSites[i];
            if (constructionSite == null || !constructionSite.isActiveAndEnabled || constructionSite.IsComplete)
                continue;

            plannedCapacity += constructionSite.PlannedSleepingCapacity;
        }

        return plannedCapacity;
    }

    public static bool TryPlaceHouse(
        GridWorldGenerator worldGenerator,
        string architectureName,
        WorldTimeManager worldTimeManager,
        SimulationBalanceConfig balanceConfig,
        out ConstructionSite constructionSite)
    {
        return TryPlace(
            worldGenerator,
            SettlementBuildingType.House,
            HouseWoodCost,
            architectureName,
            worldTimeManager,
            balanceConfig,
            out constructionSite
        );
    }

    public static bool TryPlaceBarn(
        GridWorldGenerator worldGenerator,
        string architectureName,
        WorldTimeManager worldTimeManager,
        SimulationBalanceConfig balanceConfig,
        out ConstructionSite constructionSite)
    {
        return TryPlace(
            worldGenerator,
            SettlementBuildingType.Barn,
            BarnWoodCost,
            architectureName,
            worldTimeManager,
            balanceConfig,
            out constructionSite
        );
    }

    public static bool TryGetCitizenSpawnSource(
        GridWorldGenerator worldGenerator,
        out ResourceStorage spawnSource)
    {
        spawnSource = null;
        return worldGenerator != null &&
            ResourceStorageRegistry.TryGetClosestStorage(worldGenerator.SettlementCenter, out spawnSource) &&
            spawnSource != null;
    }

    public static bool TrySpawnCitizen(ResourceStorage spawnSource)
    {
        return spawnSource != null && spawnSource.TrySpawnVillager();
    }

    private static bool TryPlace(
        GridWorldGenerator worldGenerator,
        SettlementBuildingType buildingType,
        int woodCost,
        string architectureName,
        WorldTimeManager worldTimeManager,
        SimulationBalanceConfig balanceConfig,
        out ConstructionSite constructionSite)
    {
        constructionSite = null;
        return worldGenerator != null && worldGenerator.TryPlaceNextConstructionSite(
            buildingType,
            woodCost,
            0,
            architectureName,
            worldTimeManager,
            balanceConfig,
            out constructionSite
        );
    }
}
