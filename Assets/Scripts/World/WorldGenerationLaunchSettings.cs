using UnityEngine;
using UnityEngine.SceneManagement;

public static class WorldGenerationLaunchSettings
{
    public struct Settings
    {
        public int Seed;
        public int WorldWidth;
        public int WorldHeight;
        public int WoodDensityPercent;
        public int FoodDensityPercent;
        public int EmptyDensityPercent;
        public int NpcCount;
        public int BarnStartingFood;
        public int BarnStartingWood;
        public float NormalClockSpeedSecondsPerDay;
        public float CurrentClockSpeedSecondsPerDay;

        public Settings(
            int seed,
            int worldWidth,
            int worldHeight,
            int woodDensityPercent,
            int foodDensityPercent,
            int emptyDensityPercent,
            int npcCount,
            int barnStartingFood,
            int barnStartingWood,
            float normalClockSpeedSecondsPerDay,
            float currentClockSpeedSecondsPerDay)
        {
            Seed = seed;
            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
            WoodDensityPercent = woodDensityPercent;
            FoodDensityPercent = foodDensityPercent;
            EmptyDensityPercent = emptyDensityPercent;
            NpcCount = npcCount;
            BarnStartingFood = barnStartingFood;
            BarnStartingWood = barnStartingWood;
            NormalClockSpeedSecondsPerDay = normalClockSpeedSecondsPerDay;
            CurrentClockSpeedSecondsPerDay = currentClockSpeedSecondsPerDay;
        }
    }

    private static bool hasPendingStartSceneGeneration;
    private static Settings pendingStartSceneGeneration;

    public static bool HasPendingStartSceneGeneration => hasPendingStartSceneGeneration;

    public static void QueueStartSceneGeneration(Settings settings)
    {
        pendingStartSceneGeneration = settings;
        hasPendingStartSceneGeneration = true;
        SceneManager.sceneLoaded -= ValidateLoadedSceneHasGenerator;
        SceneManager.sceneLoaded += ValidateLoadedSceneHasGenerator;
    }

    public static bool TryGetPendingStartSceneGeneration(out Settings settings)
    {
        settings = pendingStartSceneGeneration;
        return hasPendingStartSceneGeneration;
    }

    public static void ClearPendingStartSceneGeneration()
    {
        pendingStartSceneGeneration = default;
        hasPendingStartSceneGeneration = false;
        SceneManager.sceneLoaded -= ValidateLoadedSceneHasGenerator;
    }

    private static void ValidateLoadedSceneHasGenerator(Scene scene, LoadSceneMode mode)
    {
        if (!hasPendingStartSceneGeneration)
        {
            SceneManager.sceneLoaded -= ValidateLoadedSceneHasGenerator;
            return;
        }

        GridWorldGenerator[] generators = Object.FindObjectsByType<GridWorldGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < generators.Length; i++)
        {
            GridWorldGenerator generator = generators[i];
            if (generator != null && generator.isActiveAndEnabled)
                return;
        }

        Debug.LogError($"StartScene launch settings could not be applied because scene '{scene.name}' has no active GridWorldGenerator. Add or enable the scene-local _World/GridWorld generator object and assign its Inspector references.");
    }
}
