/*
===============================================================================
Script:
HomeRegistry

Purpose:
Keeps a runtime registry of available homes for shared NPC home assignment.

Folder:
Assets/Scripts/Agents/Common/Home/

Scene Hierarchy:
None

Attach To:
None

Required Children:
None

Dependencies:
- House
- CharacterIdentity

Automatically Resolved:
Homes register themselves from House.OnEnable and House.OnDisable.

Inspector References:
None
===============================================================================
*/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public static class HomeRegistry
{
    private const float RequesterNavMeshSampleDistance = 2f;
    private const float HomeEntryNavMeshSampleDistance = 2f;

    private static readonly List<House> homes = new List<House>();
    private static readonly NavMeshPath sharedHomePath = new NavMeshPath();

    public static int RegisteredHomeCount => homes.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        homes.Clear();
    }

    public static void Register(House home)
    {
        if (home == null || homes.Contains(home))
            return;

        homes.Add(home);
    }

    public static void Unregister(House home)
    {
        if (home == null)
            return;

        homes.Remove(home);
    }

    public static void GetHousingStatus(out int occupiedSlots, out int sleepingCapacity)
    {
        PruneMissingHomes();

        occupiedSlots = 0;
        sleepingCapacity = 0;

        for (int i = 0; i < homes.Count; i++)
        {
            House home = homes[i];

            if (home == null || !home.isActiveAndEnabled)
                continue;

            occupiedSlots += home.OccupiedSlots;
            sleepingCapacity += home.SleepingCapacity;
        }
    }

    public static void GetLivingPopulationStatus(out int livingNpcCount, out int sleepingCapacity)
    {
        GetHousingStatus(out _, out sleepingCapacity);

        Character[] characters = Object.FindObjectsByType<Character>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        livingNpcCount = 0;

        if (characters == null)
            return;

        for (int i = 0; i < characters.Length; i++)
        {
            Character character = characters[i];
            if (character == null)
                continue;

            NpcHealth health = character.GetComponent<NpcHealth>();
            if (health != null && health.IsDead)
                continue;

            livingNpcCount++;
        }
    }

    public static bool HasFreeBedForNewNpc(out int livingNpcCount, out int sleepingCapacity)
    {
        GetLivingPopulationStatus(out livingNpcCount, out sleepingCapacity);
        return livingNpcCount < sleepingCapacity;
    }

    public static bool TryReserveHome(CharacterIdentity resident, Vector3 requesterPosition, out House reservedHome)
    {
        return TryReserveHome(
            resident,
            requesterPosition,
            null,
            out reservedHome,
            out _
        );
    }

    public static bool TryReserveHome(
        CharacterIdentity resident,
        Vector3 requesterPosition,
        out House reservedHome,
        out Vector3 resolvedHomePosition)
    {
        return TryReserveHome(
            resident,
            requesterPosition,
            null,
            out reservedHome,
            out resolvedHomePosition
        );
    }

    public static bool TryReserveHome(
        CharacterIdentity resident,
        Vector3 requesterPosition,
        House excludedHome,
        out House reservedHome,
        out Vector3 resolvedHomePosition)
    {
        reservedHome = null;
        resolvedHomePosition = requesterPosition;

        if (resident == null)
            return false;

        PruneMissingHomes();

        if (!NavMesh.SamplePosition(
                requesterPosition,
                out NavMeshHit requesterHit,
                RequesterNavMeshSampleDistance,
                NavMesh.AllAreas))
        {
            return false;
        }

        House bestHome = null;
        Vector3 bestHomePosition = requesterHit.position;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < homes.Count; i++)
        {
            House home = homes[i];

            if (home == null ||
                home == excludedHome ||
                !home.isActiveAndEnabled ||
                !home.HasFreeSlot ||
                !TryGetReachableHomePosition(home, requesterHit.position, out Vector3 candidateHomePosition))
            {
                continue;
            }

            float distance = (candidateHomePosition - requesterHit.position).sqrMagnitude;
            if (distance >= bestDistance)
                continue;

            bestHome = home;
            bestHomePosition = candidateHomePosition;
            bestDistance = distance;
        }

        if (bestHome == null)
            return false;

        if (!bestHome.TryReserveSlot(resident))
            return false;

        reservedHome = bestHome;
        resolvedHomePosition = bestHomePosition;
        return true;
    }

    private static bool TryGetReachableHomePosition(
        House home,
        Vector3 requesterNavMeshPosition,
        out Vector3 resolvedHomePosition)
    {
        resolvedHomePosition = requesterNavMeshPosition;

        if (home == null ||
            !NavMesh.SamplePosition(
                home.HomeEntryPosition,
                out NavMeshHit homeHit,
                HomeEntryNavMeshSampleDistance,
                NavMesh.AllAreas))
        {
            return false;
        }

        SimulationPerformanceCounters.AddHomePathCalculation();
        if (!NavMesh.CalculatePath(
                requesterNavMeshPosition,
                homeHit.position,
                NavMesh.AllAreas,
                sharedHomePath) ||
            sharedHomePath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        resolvedHomePosition = homeHit.position;
        return true;
    }

    private static void PruneMissingHomes()
    {
        homes.RemoveAll(home => home == null);
    }
}
