/*
===============================================================================
Script:
ResourceStorageRegistry

Purpose:
Keeps a runtime registry of barns/resource storages for shared deposit and equipment execution.

Folder:
Assets/Scripts/Agents/Common/Resources/

Scene Hierarchy:
None

Attach To:
None

Required Children:
None

Dependencies:
- ResourceStorage

Automatically Resolved:
Resource storages register themselves from ResourceStorage.OnEnable and ResourceStorage.OnDisable.

Inspector References:
None
===============================================================================
*/

using System.Collections.Generic;
using UnityEngine;

public enum ResourceStorageLookupResult
{
    Found,
    NoActiveStorage,
    TemporarilyUnreachable
}

public static class ResourceStorageRegistry
{
    public const float TemporaryUnreachableDurationWorldHours = 0.12f;

    private static readonly List<ResourceStorage> storages = new List<ResourceStorage>();
    private static int sharedWoodStored;
    private static int sharedBerriesStored;
    private static int registryGeneration;
    private static bool hasConfiguredInitialResources;
    private static bool initialResourcesContributed;
    private static int configuredStartingWood;
    private static int configuredStartingFood;

    public static int RegisteredStorageCount => storages.Count;
    public static int SharedWoodStored => sharedWoodStored;
    public static int SharedBerriesStored => sharedBerriesStored;
    internal static int RegistryGeneration => registryGeneration;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        storages.Clear();
        sharedWoodStored = 0;
        sharedBerriesStored = 0;
        hasConfiguredInitialResources = false;
        initialResourcesContributed = false;
        configuredStartingWood = 0;
        configuredStartingFood = 0;
        registryGeneration++;
    }

    public static void Register(ResourceStorage storage)
    {
        if (storage == null || storages.Contains(storage))
            return;

        storages.Add(storage);
        storage.ContributeInitialResourcesToSharedInventory(registryGeneration);
    }

    public static void ConfigureInitialSharedResources(int startingWood, int startingFood)
    {
        configuredStartingWood = Mathf.Max(0, startingWood);
        configuredStartingFood = Mathf.Max(0, startingFood);
        hasConfiguredInitialResources = true;
        initialResourcesContributed = false;
        sharedWoodStored = 0;
        sharedBerriesStored = 0;
        registryGeneration++;
    }

    public static void Unregister(ResourceStorage storage)
    {
        if (storage == null)
            return;

        storages.Remove(storage);
    }

    public static bool TryGetClosestStorage(Vector3 requesterPosition, out ResourceStorage storage)
    {
        return GetClosestStorage(requesterPosition, out storage) == ResourceStorageLookupResult.Found;
    }

    public static bool TryGetClosestStorage(
        Vector3 requesterPosition,
        int requesterId,
        out ResourceStorage storage)
    {
        return GetClosestStorage(requesterPosition, requesterId, out storage) == ResourceStorageLookupResult.Found;
    }

    public static ResourceStorageLookupResult GetClosestStorage(
        Vector3 requesterPosition,
        out ResourceStorage storage)
    {
        return GetClosestStorage(requesterPosition, 0, out storage);
    }

    public static ResourceStorageLookupResult GetClosestStorage(
        Vector3 requesterPosition,
        int requesterId,
        out ResourceStorage storage)
    {
        return GetClosestStorage(
            requesterPosition,
            requesterId,
            false,
            out storage
        );
    }

    public static bool TryGetClosestStorageIgnoringTemporaryUnreachable(
        Vector3 requesterPosition,
        int requesterId,
        out ResourceStorage storage)
    {
        return GetClosestStorage(
            requesterPosition,
            requesterId,
            true,
            out storage
        ) == ResourceStorageLookupResult.Found;
    }

    private static ResourceStorageLookupResult GetClosestStorage(
        Vector3 requesterPosition,
        int requesterId,
        bool ignoreTemporaryUnreachable,
        out ResourceStorage storage)
    {
        PruneMissingStorages();

        storage = null;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < storages.Count; i++)
        {
            ResourceStorage candidate = storages[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
                continue;

            float distance = (candidate.GetDepositPosition() - requesterPosition).sqrMagnitude;
            if (distance >= closestDistance)
                continue;

            storage = candidate;
            closestDistance = distance;
        }

        if (storage != null)
            return ResourceStorageLookupResult.Found;

        return ResourceStorageLookupResult.NoActiveStorage;
    }

    public static void MarkStorageTemporarilyUnreachable(
        ResourceStorage storage,
        Vector3 requesterPosition,
        string reason,
        bool barnReturnFailure = false)
    {
        MarkStorageTemporarilyUnreachable(
            storage,
            requesterPosition,
            0,
            reason,
            barnReturnFailure
        );
    }

    public static void MarkStorageTemporarilyUnreachable(
        ResourceStorage storage,
        Vector3 requesterPosition,
        int requesterId,
        string reason,
        bool barnReturnFailure = false)
    {
        // Barns/resource storages are settlement anchors. Unlike resources, fog, or
        // construction targets, they must never be hidden behind an unreachable cache.
    }

    public static void AddWood(int amount)
    {
        int addedAmount = Mathf.Max(0, amount);
        if (addedAmount <= 0)
            return;

        sharedWoodStored += addedAmount;
        SimulationEventLogger.LogWorldChange(
            "StoredWoodChanged",
            null,
            $"Shared storage gained {addedAmount} wood.",
            "SharedStorage",
            "Wood",
            addedAmount
        );
    }

    public static void AddBerries(int amount)
    {
        int addedAmount = Mathf.Max(0, amount);
        if (addedAmount <= 0)
            return;

        sharedBerriesStored += addedAmount;
        SimulationEventLogger.LogWorldChange(
            "StoredFoodChanged",
            null,
            $"Shared storage gained {addedAmount} food.",
            "SharedStorage",
            "Food",
            addedAmount
        );
    }

    public static bool HasResources(int woodAmount, int foodAmount)
    {
        int requiredWood = Mathf.Max(0, woodAmount);
        int requiredFood = Mathf.Max(0, foodAmount);

        return sharedWoodStored >= requiredWood && sharedBerriesStored >= requiredFood;
    }

    public static bool TrySpendResources(int woodAmount, int foodAmount)
    {
        int requiredWood = Mathf.Max(0, woodAmount);
        int requiredFood = Mathf.Max(0, foodAmount);

        if (!HasResources(requiredWood, requiredFood))
            return false;

        sharedWoodStored -= requiredWood;
        sharedBerriesStored -= requiredFood;
        SimulationEventLogger.LogWorldChange(
            "StoredResourcesSpent",
            null,
            $"Shared storage spent {requiredWood} wood and {requiredFood} food.",
            "SharedStorage",
            "WoodAndFood",
            requiredWood,
            requiredFood
        );
        return true;
    }

    public static void WithdrawDailyUpkeep(
        int requestedWood,
        int requestedFood,
        out int receivedWood,
        out int receivedFood)
    {
        int requiredWood = Mathf.Max(0, requestedWood);
        int requiredFood = Mathf.Max(0, requestedFood);

        receivedWood = Mathf.Min(sharedWoodStored, requiredWood);
        receivedFood = Mathf.Min(sharedBerriesStored, requiredFood);

        sharedWoodStored -= receivedWood;
        sharedBerriesStored -= receivedFood;

        if (receivedWood > 0 || receivedFood > 0)
        {
            SimulationEventLogger.LogWorldChange(
                "DailyResourcesWithdrawn",
                null,
                $"Daily upkeep withdrew {receivedWood} wood and {receivedFood} food from shared storage.",
                "SharedStorage",
                "WoodAndFood",
                receivedWood,
                receivedFood
            );
        }
    }

    internal static void AddInitialSharedResources(int fallbackWoodAmount, int fallbackFoodAmount)
    {
        if (initialResourcesContributed)
            return;

        int woodAmount = hasConfiguredInitialResources
            ? configuredStartingWood
            : Mathf.Max(0, fallbackWoodAmount);

        int foodAmount = hasConfiguredInitialResources
            ? configuredStartingFood
            : Mathf.Max(0, fallbackFoodAmount);

        AddWood(woodAmount);
        AddBerries(foodAmount);
        initialResourcesContributed = true;
    }

    private static void PruneMissingStorages()
    {
        for (int i = storages.Count - 1; i >= 0; i--)
        {
            if (storages[i] != null)
                continue;

            storages.RemoveAt(i);
        }
    }
}
