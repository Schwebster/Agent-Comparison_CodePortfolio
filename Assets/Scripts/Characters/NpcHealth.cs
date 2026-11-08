/*
===============================================================================
Script:
NpcHealth

Purpose:
Tracks daily upkeep, health, movement penalty, regeneration, and death for an NPC.

Folder:
Assets/Scripts/Characters/

Scene Hierarchy:
_World
- Characters
  - Character

Attach To:
NPC character root

Required Children:
None

Dependencies:
- Character
- CharacterIdentity
- NpcTaskController
- ResourceStorageRegistry
- SimulationBalanceConfig

Automatically Resolved:
- Character on the same GameObject
- CharacterIdentity on the same GameObject

Inspector References:
- simulationBalanceConfig
===============================================================================
*/

using UnityEngine;

[DisallowMultipleComponent]
public class NpcHealth : MonoBehaviour
{
    private const int FallbackConsumptionNeedFood = 5;
    private const int FallbackConsumptionNeedWood = 2;
    private const float DefaultMissingFoodDamage = 30f;
    private const float DefaultMissingWoodDamage = 10f;
    private const float DefaultFullUpkeepHealthRegen = 10f;
    private const float DefaultMaxHealth = 100f;
    private const float DefaultDeathHealthThreshold = 10f;
    private const float MinimumMovementMultiplier = 0.05f;

    [SerializeField] private SimulationBalanceConfig simulationBalanceConfig;
    [SerializeField] private float currentHealth = -1f;
    [SerializeField] private int lastUpkeepDay = -1;

    private Character character;
    private CharacterIdentity identity;
    private bool isDead;

    public float CurrentHealth => Mathf.Clamp(currentHealth, 0f, MaxHealth);
    public float MaxHealth => simulationBalanceConfig != null ? simulationBalanceConfig.MaxNpcHealth : DefaultMaxHealth;
    public float MovementSpeedMultiplier => Mathf.Clamp(CurrentHealth / Mathf.Max(0.01f, MaxHealth), MinimumMovementMultiplier, 1f);
    public bool IsDead => isDead;
    public int DailyFoodRequirement => DailyFoodRequired;
    public int DailyWoodRequirement => DailyWoodRequired;
    public SimulationBalanceConfig BalanceConfig => simulationBalanceConfig;

    private void Awake()
    {
        character = GetComponent<Character>();
        identity = GetComponent<CharacterIdentity>();
        InitializeHealth();
    }

    public void Configure(SimulationBalanceConfig balanceConfig)
    {
        if (balanceConfig != null)
            simulationBalanceConfig = balanceConfig;

        InitializeHealth();
    }

    public bool NeedsDailyUpkeep(WorldTimeManager manager)
    {
        return NeedsDailyUpkeep(manager, AgentScheduleConfig.DefaultSleepEndHour);
    }

    public bool NeedsDailyUpkeep(WorldTimeManager manager, float upkeepDayStartHour)
    {
        if (isDead)
            return false;

        if (manager == null)
            return false;

        return lastUpkeepDay != GetUpkeepDay(manager, upkeepDayStartHour);
    }

    public bool ApplyDailyUpkeep(WorldTimeManager manager)
    {
        return ApplyDailyUpkeep(manager, AgentScheduleConfig.DefaultSleepEndHour);
    }

    public bool ApplyDailyUpkeep(WorldTimeManager manager, float upkeepDayStartHour)
    {
        if (isDead || manager == null)
            return false;

        int upkeepDay = GetUpkeepDay(manager, upkeepDayStartHour);
        if (lastUpkeepDay == upkeepDay)
            return false;

        lastUpkeepDay = upkeepDay;

        int requiredFood = DailyFoodRequired;
        int requiredWood = DailyWoodRequired;

        ResourceStorageRegistry.WithdrawDailyUpkeep(
            requiredWood,
            requiredFood,
            out int receivedWood,
            out int receivedFood
        );

        bool hasFullFood = receivedFood >= requiredFood;
        bool hasFullWood = receivedWood >= requiredWood;
        float healthBefore = CurrentHealth;

        if (!hasFullFood)
            currentHealth -= MissingFoodDamage;

        if (!hasFullWood)
            currentHealth -= MissingWoodDamage;

        if (hasFullFood && hasFullWood)
            currentHealth += FullUpkeepHealthRegen;

        currentHealth = Mathf.Clamp(currentHealth, 0f, MaxHealth);

        Debug.Log(
            $"Daily upkeep for {GetNpcName()}: Food {receivedFood}/{requiredFood}, Wood {receivedWood}/{requiredWood}, HP {healthBefore:0.#}->{CurrentHealth:0.#}."
        );

        SimulationEventLogger.LogWorldChange(
            "DailyUpkeepApplied",
            identity,
            $"Daily upkeep consumed food {receivedFood}/{requiredFood} and wood {receivedWood}/{requiredWood}; health changed from {healthBefore:0.##} to {CurrentHealth:0.##}.",
            gameObject.name,
            "FoodAndWood",
            receivedFood,
            receivedWood
        );

        if (CurrentHealth < DeathHealthThreshold)
            Die("daily upkeep shortage");

        return true;
    }

    private int GetUpkeepDay(WorldTimeManager manager, float upkeepDayStartHour)
    {
        if (manager == null)
            return -1;

        float dayStartHour = WorldTimeConfig.NormalizeHour(upkeepDayStartHour);
        int day = manager.CurrentDay;

        if (dayStartHour > 0f && manager.CurrentHour < dayStartHour)
            day--;

        return Mathf.Max(1, day);
    }

    private int DailyFoodRequired => simulationBalanceConfig != null
        ? simulationBalanceConfig.ConsumptionNeedFood
        : FallbackConsumptionNeedFood;

    private int DailyWoodRequired => simulationBalanceConfig != null
        ? simulationBalanceConfig.ConsumptionNeedWood
        : FallbackConsumptionNeedWood;

    private float MissingFoodDamage => simulationBalanceConfig != null
        ? simulationBalanceConfig.MissingFoodDamage
        : DefaultMissingFoodDamage;

    private float MissingWoodDamage => simulationBalanceConfig != null
        ? simulationBalanceConfig.MissingWoodDamage
        : DefaultMissingWoodDamage;

    private float FullUpkeepHealthRegen => simulationBalanceConfig != null
        ? simulationBalanceConfig.FullUpkeepHealthRegen
        : DefaultFullUpkeepHealthRegen;

    private float DeathHealthThreshold => simulationBalanceConfig != null
        ? simulationBalanceConfig.DeathHealthThreshold
        : DefaultDeathHealthThreshold;

    private void InitializeHealth()
    {
        float maxHealth = MaxHealth;

        if (currentHealth < 0f)
            currentHealth = maxHealth;

        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    private void Die(string reason)
    {
        if (isDead)
            return;

        isDead = true;

        Debug.Log($"NPC died from {reason}: {GetNpcName()}.");
        SimulationEventLogger.LogWorldChange(
            "CitizenDied",
            identity,
            $"Citizen died from {reason}.",
            gameObject.name
        );

        NpcTaskController taskController = GetComponent<NpcTaskController>();
        if (taskController != null && taskController.IsTaskActive)
            taskController.CancelCurrentTask();

        if (identity != null)
            NpcWorkRoleAssignmentRegistry.Release(identity);

        NpcHomeAssignment homeAssignment = GetComponent<NpcHomeAssignment>();
        if (homeAssignment != null)
            homeAssignment.ReleaseHome();

        if (character != null)
        {
            CharacterControlRegistry.ReleasePlayerControl(character);
            CharacterControlRegistry.ReleaseAutonomousControl(character);
            character.StopMovement();
        }

        Destroy(gameObject);
    }

    private string GetNpcName()
    {
        if (identity != null)
            return $"{identity.FullName} (ID {identity.CharacterId})";

        return gameObject.name;
    }
}
