/*
===============================================================================
Script:
SimulationBalanceConfig

Purpose:
Stores gameplay balancing values expressed relative to world time.

Folder:
Assets/Scripts/Simulation/

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
- referenceDayDurationRealSeconds
- defaultWorldSeed
- defaultWorldWidth
- defaultWorldHeight
- defaultWoodPercent
- defaultBerryPercent
- defaultEmptyPercent
- defaultNpcCount
- defaultNormalClockSpeedSecondsPerDay
- defaultCurrentClockSpeedSecondsPerDay
- barnStartingFood
- barnStartingWood
- houseBuildDurationWorldHours
- barnBuildDurationWorldHours
- treeChopDurationWorldHours
- bushHarvestDurationWorldHours
- berryGatherDurationWorldHours
- baseMovementSpeed
- baseAnimatorSpeed
- consumptionNeedFood
- consumptionNeedWood
- missingFoodDamage
- missingWoodDamage
- fullUpkeepHealthRegen
- maxNpcHealth
- deathHealthThreshold
===============================================================================
*/

using UnityEngine;

[CreateAssetMenu(fileName = "SimulationBalanceConfig", menuName = "Agent Comparison/Simulation Balance Config")]
public class SimulationBalanceConfig : ScriptableObject
{
    private const float MinimumPositiveValue = 0.01f;

    [Header("Time Reference")]
    [Tooltip("Real seconds for one complete world day at normal simulation speed.")]
    [SerializeField] private float referenceDayDurationRealSeconds = 600f;

    [Header("Start Screen Defaults")]
    [Tooltip("Default StartScene world generation seed.")]
    [SerializeField] private int defaultWorldSeed = 2026;
    [Tooltip("Default generated world width in grid cells.")]
    [SerializeField] private int defaultWorldWidth = 40;
    [Tooltip("Default generated world height in grid cells.")]
    [SerializeField] private int defaultWorldHeight = 40;
    [Tooltip("Default tree/wood percentage used by the StartScene settings UI.")]
    [SerializeField] private int defaultWoodPercent = 35;
    [Tooltip("Default berry bush percentage used by the StartScene settings UI.")]
    [SerializeField] private int defaultBerryPercent = 35;
    [Tooltip("Default empty tile percentage used by the StartScene settings UI.")]
    [SerializeField] private int defaultEmptyPercent = 40;
    [Tooltip("Default number of NPCs generated from the StartScene settings UI.")]
    [SerializeField] private int defaultNpcCount = 3;
    [Tooltip("Default normal clock speed shown by the StartScene settings UI, in real seconds per world day.")]
    [SerializeField] private float defaultNormalClockSpeedSecondsPerDay = 120f;
    [Tooltip("Default current clock speed applied to generated scenes, in real seconds per world day.")]
    [SerializeField] private float defaultCurrentClockSpeedSecondsPerDay = 120f;
    [Tooltip("Initial food stored in the shared barn inventory when a generated world starts.")]
    [SerializeField] private int barnStartingFood = 300;
    [Tooltip("Initial wood stored in the shared barn inventory when a generated world starts.")]
    [SerializeField] private int barnStartingWood = 10;

    [Header("Construction")]
    [Tooltip("World hours required to build a house.")]
    [SerializeField] private float houseBuildDurationWorldHours = 8f;
    [Tooltip("World hours required to build a barn.")]
    [SerializeField] private float barnBuildDurationWorldHours = 12f;

    [Header("Resources")]
    [Tooltip("World hours required to chop one tree.")]
    [SerializeField] private float treeChopDurationWorldHours = 1f;
    [Tooltip("World hours required to harvest one bush.")]
    [SerializeField] private float bushHarvestDurationWorldHours = 1f;
    [Tooltip("World hours required to gather berries.")]
    [SerializeField] private float berryGatherDurationWorldHours = 1f;

    [Header("NPC")]
    [Tooltip("Movement speed used when the current day duration equals the reference day duration.")]
    [SerializeField] private float baseMovementSpeed = 3.5f;
    [Tooltip("Animator speed used when the current day duration equals the reference day duration.")]
    [SerializeField] private float baseAnimatorSpeed = 1f;

    [Header("NPC Upkeep")]
    [Tooltip("Food consumed by each NPC once per day.")]
    [SerializeField] private int consumptionNeedFood = 5;
    [Tooltip("Wood consumed by each NPC once per day.")]
    [SerializeField] private int consumptionNeedWood = 2;
    [Tooltip("HP damage applied once when an NPC cannot receive full daily food.")]
    [SerializeField] private float missingFoodDamage = 30f;
    [Tooltip("HP damage applied once when an NPC cannot receive full daily wood.")]
    [SerializeField] private float missingWoodDamage = 10f;
    [Tooltip("HP restored once when an NPC receives full daily food and wood.")]
    [SerializeField] private float fullUpkeepHealthRegen = 10f;
    [Tooltip("Maximum NPC health.")]
    [SerializeField] private float maxNpcHealth = 100f;
    [Tooltip("NPC dies when health drops below this value.")]
    [SerializeField] private float deathHealthThreshold = 10f;

    public float ReferenceDayDurationRealSeconds => Mathf.Max(MinimumPositiveValue, referenceDayDurationRealSeconds);
    public int DefaultWorldSeed => Mathf.Max(0, defaultWorldSeed);
    public int DefaultWorldWidth => Mathf.Max(1, defaultWorldWidth);
    public int DefaultWorldHeight => Mathf.Max(1, defaultWorldHeight);
    public int DefaultWoodPercent => Mathf.Clamp(defaultWoodPercent, 0, 100);
    public int DefaultBerryPercent => Mathf.Clamp(defaultBerryPercent, 0, 100);
    public int DefaultEmptyPercent => Mathf.Clamp(defaultEmptyPercent, 0, 100);
    public int DefaultNpcCount => Mathf.Max(0, defaultNpcCount);
    public float DefaultNormalClockSpeedSecondsPerDay => Mathf.Max(MinimumPositiveValue, defaultNormalClockSpeedSecondsPerDay);
    public float DefaultCurrentClockSpeedSecondsPerDay => Mathf.Max(MinimumPositiveValue, defaultCurrentClockSpeedSecondsPerDay);
    public int BarnStartingFood => Mathf.Max(0, barnStartingFood);
    public int BarnStartingWood => Mathf.Max(0, barnStartingWood);
    public float HouseBuildDurationWorldHours => Mathf.Max(MinimumPositiveValue, houseBuildDurationWorldHours);
    public float BarnBuildDurationWorldHours => Mathf.Max(MinimumPositiveValue, barnBuildDurationWorldHours);
    public float TreeChopDurationWorldHours => Mathf.Max(MinimumPositiveValue, treeChopDurationWorldHours);
    public float BushHarvestDurationWorldHours => Mathf.Max(MinimumPositiveValue, bushHarvestDurationWorldHours);
    public float BerryGatherDurationWorldHours => Mathf.Max(MinimumPositiveValue, berryGatherDurationWorldHours);
    public float BaseMovementSpeed => Mathf.Max(MinimumPositiveValue, baseMovementSpeed);
    public float BaseAnimatorSpeed => Mathf.Max(MinimumPositiveValue, baseAnimatorSpeed);
    public int ConsumptionNeedFood => Mathf.Max(0, consumptionNeedFood);
    public int ConsumptionNeedWood => Mathf.Max(0, consumptionNeedWood);
    public float MissingFoodDamage => Mathf.Max(0f, missingFoodDamage);
    public float MissingWoodDamage => Mathf.Max(0f, missingWoodDamage);
    public float FullUpkeepHealthRegen => Mathf.Max(0f, fullUpkeepHealthRegen);
    public float MaxNpcHealth => Mathf.Max(MinimumPositiveValue, maxNpcHealth);
    public float DeathHealthThreshold => Mathf.Clamp(deathHealthThreshold, 0f, MaxNpcHealth);

    [System.Obsolete("Use ConsumptionNeedFood instead.")]
    public int DailyFoodRequiredPerNpc => ConsumptionNeedFood;

    [System.Obsolete("Use ConsumptionNeedWood instead.")]
    public int DailyWoodRequiredPerNpc => ConsumptionNeedWood;

    private void OnValidate()
    {
        referenceDayDurationRealSeconds = Mathf.Max(MinimumPositiveValue, referenceDayDurationRealSeconds);
        defaultWorldSeed = Mathf.Max(0, defaultWorldSeed);
        defaultWorldWidth = Mathf.Max(1, defaultWorldWidth);
        defaultWorldHeight = Mathf.Max(1, defaultWorldHeight);
        defaultWoodPercent = Mathf.Clamp(defaultWoodPercent, 0, 100);
        defaultBerryPercent = Mathf.Clamp(defaultBerryPercent, 0, 100);
        defaultEmptyPercent = Mathf.Clamp(defaultEmptyPercent, 0, 100);
        defaultNpcCount = Mathf.Max(0, defaultNpcCount);
        defaultNormalClockSpeedSecondsPerDay = Mathf.Max(MinimumPositiveValue, defaultNormalClockSpeedSecondsPerDay);
        defaultCurrentClockSpeedSecondsPerDay = Mathf.Max(MinimumPositiveValue, defaultCurrentClockSpeedSecondsPerDay);
        barnStartingFood = Mathf.Max(0, barnStartingFood);
        barnStartingWood = Mathf.Max(0, barnStartingWood);
        houseBuildDurationWorldHours = Mathf.Max(MinimumPositiveValue, houseBuildDurationWorldHours);
        barnBuildDurationWorldHours = Mathf.Max(MinimumPositiveValue, barnBuildDurationWorldHours);
        treeChopDurationWorldHours = Mathf.Max(MinimumPositiveValue, treeChopDurationWorldHours);
        bushHarvestDurationWorldHours = Mathf.Max(MinimumPositiveValue, bushHarvestDurationWorldHours);
        berryGatherDurationWorldHours = Mathf.Max(MinimumPositiveValue, berryGatherDurationWorldHours);
        baseMovementSpeed = Mathf.Max(MinimumPositiveValue, baseMovementSpeed);
        baseAnimatorSpeed = Mathf.Max(MinimumPositiveValue, baseAnimatorSpeed);
        consumptionNeedFood = Mathf.Max(0, consumptionNeedFood);
        consumptionNeedWood = Mathf.Max(0, consumptionNeedWood);
        missingFoodDamage = Mathf.Max(0f, missingFoodDamage);
        missingWoodDamage = Mathf.Max(0f, missingWoodDamage);
        fullUpkeepHealthRegen = Mathf.Max(0f, fullUpkeepHealthRegen);
        maxNpcHealth = Mathf.Max(MinimumPositiveValue, maxNpcHealth);
        deathHealthThreshold = Mathf.Clamp(deathHealthThreshold, 0f, maxNpcHealth);
    }
}
