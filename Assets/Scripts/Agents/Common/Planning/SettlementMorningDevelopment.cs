// Shared settlement policy for FSM and GOAP. Called by each architecture's
// lowest-ID living agent and reevaluated at deterministic world-time intervals.
// No component attachment or new Inspector references are required.
using UnityEngine;

public static class SettlementMorningDevelopment
{
    private const float MorningPlanningHour = 6f;
    private const float ReevaluationIntervalWorldHours = 0.25f;

    private static int lastPlanningFrame = -1;
    private static int lastReportedDay = -1;
    private static int lastProcessedGeneratorId;
    private static int lastProcessedGenerationVersion = -1;
    private static float nextEvaluationTotalWorldHours;
    private static bool missingBalanceWarningLogged;
    private static GridWorldGenerator cachedWorldGenerator;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        lastPlanningFrame = -1;
        lastReportedDay = -1;
        lastProcessedGeneratorId = 0;
        lastProcessedGenerationVersion = -1;
        nextEvaluationTotalWorldHours = 0f;
        missingBalanceWarningLogged = false;
        cachedWorldGenerator = null;
    }

    public static void TryRun(
        string architectureName,
        WorldTimeManager worldTimeManager,
        SimulationBalanceConfig balanceConfig,
        int foodPerNpc,
        int woodPerNpc,
        float populationFoodReserveDays)
    {
        if (worldTimeManager == null || lastPlanningFrame == Time.frameCount)
            return;

        lastPlanningFrame = Time.frameCount;
        if (worldTimeManager.CurrentHour < MorningPlanningHour)
            return;

        if (cachedWorldGenerator == null)
            cachedWorldGenerator = Object.FindFirstObjectByType<GridWorldGenerator>();

        GridWorldGenerator worldGenerator = cachedWorldGenerator;
        if (worldGenerator == null || !worldGenerator.IsWorldGenerated)
            return;

        int generatorId = worldGenerator.GetInstanceID();
        int generationVersion = worldGenerator.GenerationVersion;
        int day = worldTimeManager.CurrentDay;
        bool isInitialGenerationPlan = lastProcessedGeneratorId != generatorId ||
            lastProcessedGenerationVersion != worldGenerator.GenerationVersion;

        if (!isInitialGenerationPlan &&
            worldTimeManager.TotalWorldHours < nextEvaluationTotalWorldHours)
            return;

        if (balanceConfig == null)
        {
            if (!missingBalanceWarningLogged)
            {
                Debug.LogWarning("Settlement development requires SimulationBalanceConfig on its NPC prefab.");
                missingBalanceWarningLogged = true;
            }

            return;
        }

        RunDevelopmentEvaluation(
            architectureName,
            worldGenerator,
            worldTimeManager,
            foodPerNpc,
            woodPerNpc,
            populationFoodReserveDays,
            balanceConfig,
            !isInitialGenerationPlan,
            lastReportedDay != day
        );
        lastReportedDay = day;
        lastProcessedGeneratorId = generatorId;
        lastProcessedGenerationVersion = generationVersion;
        nextEvaluationTotalWorldHours = worldTimeManager.TotalWorldHours + ReevaluationIntervalWorldHours;
    }

    private static void RunDevelopmentEvaluation(
        string architectureName,
        GridWorldGenerator worldGenerator,
        WorldTimeManager worldTimeManager,
        int foodPerNpc,
        int woodPerNpc,
        float populationFoodReserveDays,
        SimulationBalanceConfig balanceConfig,
        bool allowCitizenSpawning,
        bool logNoChanges)
    {
        HomeRegistry.GetLivingPopulationStatus(out int livingNpcCount, out int completedHousingCapacity);
        int dailyFoodNeed = livingNpcCount * foodPerNpc;
        int dailyWoodNeed = livingNpcCount * woodPerNpc;
        int plannedHousingCapacity = SettlementDevelopmentService.GetPlannedHousingCapacity(completedHousingCapacity);
        int housesPlaced = 0;
        int citizensSpawned = 0;

        while (plannedHousingCapacity < livingNpcCount &&
            ResourceStorageRegistry.SharedBerriesStored >= dailyFoodNeed &&
            ResourceStorageRegistry.SharedWoodStored >= SettlementDevelopmentService.HouseWoodCost + dailyWoodNeed)
        {
            if (!SettlementDevelopmentService.TryPlaceHouse(
                worldGenerator,
                architectureName,
                worldTimeManager,
                balanceConfig,
                out ConstructionSite houseSite))
            {
                break;
            }

            housesPlaced++;
            plannedHousingCapacity += houseSite.PlannedSleepingCapacity;
        }

        bool housingDeficitRemains = plannedHousingCapacity < livingNpcCount;
        bool hasSpawnSource = SettlementDevelopmentService.TryGetCitizenSpawnSource(
            worldGenerator,
            out ResourceStorage spawnSource
        );

        if (!housingDeficitRemains &&
            plannedHousingCapacity <= livingNpcCount &&
            hasSpawnSource &&
            HasFoodReserveForNextCitizen(
                livingNpcCount,
                foodPerNpc,
                spawnSource.VillagerSpawnFoodCost,
                populationFoodReserveDays) &&
            ResourceStorageRegistry.SharedWoodStored >= SettlementDevelopmentService.HouseWoodCost + dailyWoodNeed &&
            SettlementDevelopmentService.TryPlaceHouse(
                worldGenerator,
                architectureName,
                worldTimeManager,
                balanceConfig,
                out ConstructionSite futureHouseSite))
        {
            housesPlaced++;
            plannedHousingCapacity += futureHouseSite.PlannedSleepingCapacity;
        }

        if (allowCitizenSpawning && hasSpawnSource)
        {
            while (HomeRegistry.HasFreeBedForNewNpc(out livingNpcCount, out _) &&
                HasFoodReserveForNextCitizen(
                    livingNpcCount,
                    foodPerNpc,
                    spawnSource.VillagerSpawnFoodCost,
                    populationFoodReserveDays))
            {
                if (!SettlementDevelopmentService.TrySpawnCitizen(spawnSource))
                    break;

                citizensSpawned++;
            }
        }

        if (logNoChanges || housesPlaced > 0 || citizensSpawned > 0)
        {
            SimulationEventLogger.LogPlanChange(
                architectureName + "SettlementDevelopment",
                $"{architectureName} settlement development placed {housesPlaced} house site(s) " +
                $"and spawned {citizensSpawned} citizen(s). Planned housing capacity is {plannedHousingCapacity}; " +
                $"population food reserve is {populationFoodReserveDays:0.##} day(s). " +
                (allowCitizenSpawning
                    ? "Citizen spawning was enabled."
                    : "Citizen spawning was skipped for the initial world generation plan.")
            );
        }
    }

    private static bool HasFoodReserveForNextCitizen(
        int livingNpcCount,
        int foodPerNpcPerDay,
        int spawnFoodCost,
        float reserveDays)
    {
        float requiredFood = Mathf.Max(0, spawnFoodCost) +
            (livingNpcCount + 1) * Mathf.Max(0, foodPerNpcPerDay) * Mathf.Max(0f, reserveDays);

        return ResourceStorageRegistry.SharedBerriesStored >= requiredFood;
    }

}
