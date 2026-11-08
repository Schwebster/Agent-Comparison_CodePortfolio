public readonly struct FsmSettlementSnapshot
{
    public int LivingNpcCount { get; }
    public int StoredFood { get; }
    public int StoredWood { get; }
    public int DailyFoodNeed { get; }
    public int DailyWoodNeed { get; }
    public int UnrevealedFogCount { get; }
    public bool HasActiveConstructionSite { get; }

    public float FoodReserveDays => DailyFoodNeed > 0
        ? (float)StoredFood / DailyFoodNeed
        : float.PositiveInfinity;

    public float WoodReserveDays => DailyWoodNeed > 0
        ? (float)StoredWood / DailyWoodNeed
        : float.PositiveInfinity;

    public FsmSettlementSnapshot(
        int livingNpcCount,
        int storedFood,
        int storedWood,
        int dailyFoodNeed,
        int dailyWoodNeed,
        int unrevealedFogCount,
        bool hasActiveConstructionSite)
    {
        LivingNpcCount = livingNpcCount;
        StoredFood = storedFood;
        StoredWood = storedWood;
        DailyFoodNeed = dailyFoodNeed;
        DailyWoodNeed = dailyWoodNeed;
        UnrevealedFogCount = unrevealedFogCount;
        HasActiveConstructionSite = hasActiveConstructionSite;
    }
}
