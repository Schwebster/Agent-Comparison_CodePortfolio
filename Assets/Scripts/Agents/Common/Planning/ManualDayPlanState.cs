/*
===============================================================================
Script:
ManualDayPlanState

Purpose:
Stores the shared manual NoAgent day plan for priorities, quotas, strategy flags, and loop flags.

Folder:
Assets/Scripts/Agents/Common/Planning/

Scene Hierarchy:
None

Attach To:
None

Required Children:
None

Dependencies:
- NpcWorkRole

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.AI;

public static class ManualDayPlanState
{
    private const float MorningLoopFlagResetHour = 6f;
    private const float TimeEpsilon = 0.0001f;

    private static readonly List<NpcWorkRole> priorities = new List<NpcWorkRole>
    {
        NpcWorkRole.Food,
        NpcWorkRole.Wood,
        NpcWorkRole.Explore,
        NpcWorkRole.Build
    };

    private static readonly ReadOnlyCollection<NpcWorkRole> readOnlyPriorities = priorities.AsReadOnly();
    private static readonly HashSet<NpcWorkRole> loopFlags = new HashSet<NpcWorkRole>();
    private static readonly Dictionary<NpcWorkRole, int> quotas = new Dictionary<NpcWorkRole, int>
    {
        { NpcWorkRole.Food, 0 },
        { NpcWorkRole.Wood, 0 },
        { NpcWorkRole.Explore, 0 },
        { NpcWorkRole.Build, 0 }
    };
    private static readonly ReadOnlyDictionary<NpcWorkRole, int> readOnlyQuotas = new ReadOnlyDictionary<NpcWorkRole, int>(quotas);
    private static bool quotasInitialized;
    private static bool quotasSetExplicitly;
    private static int automaticQuotaVillagerCount = -1;
    private static int lastAutomaticLoopFlagResetDay = -1;
    private static int lastAutomaticLoopFlagUpdateFrame = -1;
    private static int nextExploreDirectionIndex;

    public static event Action LoopFlagsChanged;
    public static event Action RoleAssignmentsChanged;
    public static event Action StrategyFlagsChanged;

    public static bool AllowCutEmptyBushes { get; private set; }
    public static bool NpcCollisionActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        ResetToDefaults();
    }

    public static IReadOnlyList<NpcWorkRole> GetPriorities()
    {
        return readOnlyPriorities;
    }

    public static bool TryGetHighestPriorityRoleWithoutLoopFlag(out NpcWorkRole role)
    {
        for (int i = 0; i < priorities.Count; i++)
        {
            NpcWorkRole candidate = priorities[i];
            if (HasLoopFlag(candidate))
                continue;

            role = candidate;
            return true;
        }

        role = NpcWorkRole.Food;
        return false;
    }

    public static bool HasLoopFlag(NpcWorkRole role)
    {
        return loopFlags.Contains(role);
    }

    public static void SetLoopFlag(NpcWorkRole role)
    {
        if (!loopFlags.Add(role))
            return;

        // A complete pass found no runnable role. Start a fresh pass so work
        // discovered since the earlier flags were set is considered again.
        if (loopFlags.Count >= GetRoleCount())
            loopFlags.Clear();

        LoopFlagsChanged?.Invoke();
        SimulationEventLogger.LogPlanChange(
            "LoopFlagSet",
            $"Role {role} was marked unavailable for the current planning pass."
        );
    }

    public static void ClearLoopFlags()
    {
        FogRegistry.ClearUnreachableFogForPlanningCycle();

        if (loopFlags.Count <= 0)
            return;

        loopFlags.Clear();
        LoopFlagsChanged?.Invoke();
        SimulationEventLogger.LogPlanChange(
            "LoopFlagsCleared",
            "All role availability loop flags were cleared."
        );
    }

    public static void UpdateDailyLoopFlagReset(WorldTimeManager worldTimeManager)
    {
        if (worldTimeManager == null)
            return;

        if (lastAutomaticLoopFlagUpdateFrame == Time.frameCount)
            return;

        lastAutomaticLoopFlagUpdateFrame = Time.frameCount;

        int currentDay = worldTimeManager.CurrentDay;
        if (currentDay == lastAutomaticLoopFlagResetDay)
            return;

        float resetTotalWorldHours =
            (currentDay - 1) * WorldTimeConfig.HoursPerDay +
            WorldTimeConfig.NormalizeHour(MorningLoopFlagResetHour);

        if (worldTimeManager.TotalWorldHours + TimeEpsilon < resetTotalWorldHours)
            return;

        ClearLoopFlags();
        ResetExploreDirectionAssignments();
        EnsureDefaultQuotas(CharacterIdentity.ActiveIdentityCount);
        lastAutomaticLoopFlagResetDay = currentDay;
    }

    public static void SetPriorities(IReadOnlyList<NpcWorkRole> newPriorities)
    {
        priorities.Clear();

        bool[] usedRoles = new bool[GetRoleCount()];

        if (newPriorities != null)
        {
            for (int i = 0; i < newPriorities.Count; i++)
            {
                NpcWorkRole role = newPriorities[i];
                int roleIndex = GetRoleIndex(role);

                if (roleIndex < 0 || usedRoles[roleIndex])
                    continue;

                priorities.Add(role);
                usedRoles[roleIndex] = true;
            }
        }

        AddMissingPriorities(usedRoles);
        SimulationEventLogger.LogPlanChange(
            "PrioritiesChanged",
            "The settlement role priority order changed."
        );
    }

    public static int GetQuota(NpcWorkRole role)
    {
        return quotas.TryGetValue(role, out int quota) ? quota : 0;
    }

    public static void SetQuota(NpcWorkRole role, int count)
    {
        quotas[role] = Mathf.Max(0, count);
        quotasInitialized = true;
        quotasSetExplicitly = true;
        automaticQuotaVillagerCount = -1;
        SimulationEventLogger.LogPlanChange(
            "QuotaChanged",
            $"Quota for {role} changed to {quotas[role]}."
        );
    }

    public static IReadOnlyDictionary<NpcWorkRole, int> GetQuotas()
    {
        return readOnlyQuotas;
    }

    public static int GetTotalAssignedQuota()
    {
        return GetQuota(NpcWorkRole.Food) +
            GetQuota(NpcWorkRole.Wood) +
            GetQuota(NpcWorkRole.Explore) +
            GetQuota(NpcWorkRole.Build);
    }

    public static void SetQuotas(IDictionary<NpcWorkRole, int> newQuotas)
    {
        ResetQuotaValues();

        if (newQuotas != null)
        {
            SetQuotaValue(NpcWorkRole.Food, newQuotas);
            SetQuotaValue(NpcWorkRole.Wood, newQuotas);
            SetQuotaValue(NpcWorkRole.Explore, newQuotas);
            SetQuotaValue(NpcWorkRole.Build, newQuotas);
        }

        quotasInitialized = true;
        quotasSetExplicitly = true;
        automaticQuotaVillagerCount = -1;
        SimulationEventLogger.LogPlanChange(
            "QuotasChanged",
            "The settlement role quotas changed."
        );
    }

    public static void EnsureDefaultQuotas(int villagerCount)
    {
        int normalizedVillagerCount = Mathf.Max(0, villagerCount);
        if (quotasSetExplicitly ||
            (quotasInitialized && automaticQuotaVillagerCount == normalizedVillagerCount))
        {
            return;
        }

        ResetQuotasToDefault(normalizedVillagerCount);
    }

    public static void ResetQuotasToDefault(int villagerCount)
    {
        int remainingVillagers = Mathf.Max(0, villagerCount);
        ResetQuotaValues();
        quotasSetExplicitly = false;
        automaticQuotaVillagerCount = remainingVillagers;

        if (remainingVillagers <= 0)
        {
            quotasInitialized = true;
            return;
        }

        if (remainingVillagers < GetRoleCount())
        {
            AddDefaultQuotaByPriority(remainingVillagers);
            quotasInitialized = true;
            return;
        }

        quotas[NpcWorkRole.Food] = 1;
        quotas[NpcWorkRole.Wood] = 1;
        quotas[NpcWorkRole.Explore] = 1;
        quotas[NpcWorkRole.Build] = 1;
        remainingVillagers -= GetRoleCount();

        AddWeightedDefaultQuota(remainingVillagers);
        quotasInitialized = true;
    }

    public static void ClampTotalQuota(int maxTotal)
    {
        int allowedTotal = Mathf.Max(0, maxTotal);

        while (GetTotalAssignedQuota() > allowedTotal)
        {
            if (TryReduceQuota(NpcWorkRole.Build))
                continue;

            if (TryReduceQuota(NpcWorkRole.Explore))
                continue;

            if (TryReduceQuota(NpcWorkRole.Wood))
                continue;

            if (TryReduceQuota(NpcWorkRole.Food))
                continue;

            break;
        }

        quotasInitialized = true;
    }

    public static void SetStrategyFlags(
        bool allowCutEmptyBushes,
        bool npcCollisionActive)
    {
        bool changed =
            AllowCutEmptyBushes != allowCutEmptyBushes ||
            NpcCollisionActive != npcCollisionActive;

        AllowCutEmptyBushes = allowCutEmptyBushes;
        NpcCollisionActive = npcCollisionActive;

        if (changed)
            StrategyFlagsChanged?.Invoke();
    }

    public static void ResetToDefaults()
    {
        priorities.Clear();
        priorities.Add(NpcWorkRole.Food);
        priorities.Add(NpcWorkRole.Wood);
        priorities.Add(NpcWorkRole.Explore);
        priorities.Add(NpcWorkRole.Build);

        loopFlags.Clear();
        FogRegistry.ClearUnreachableFogForPlanningCycle();
        ManualDayPlanRoleSelector.ClearConstructionUnreachableCache();
        ResetExploreDirectionAssignments();
        ResetQuotaValues();
        quotasInitialized = false;
        quotasSetExplicitly = false;
        automaticQuotaVillagerCount = -1;
        lastAutomaticLoopFlagResetDay = -1;
        lastAutomaticLoopFlagUpdateFrame = -1;
        AllowCutEmptyBushes = false;
        NpcCollisionActive = false;
    }

    internal static void NotifyRoleAssignmentsChanged()
    {
        RoleAssignmentsChanged?.Invoke();
    }

    private static void SetQuotaValue(NpcWorkRole role, IDictionary<NpcWorkRole, int> newQuotas)
    {
        quotas[role] = newQuotas.TryGetValue(role, out int quota) ? Mathf.Max(0, quota) : 0;
    }

    private static void ResetQuotaValues()
    {
        quotas[NpcWorkRole.Food] = 0;
        quotas[NpcWorkRole.Wood] = 0;
        quotas[NpcWorkRole.Explore] = 0;
        quotas[NpcWorkRole.Build] = 0;
    }

    private static void AddDefaultQuotaByPriority(int villagerCount)
    {
        if (villagerCount >= 1)
            quotas[NpcWorkRole.Food] = 1;

        if (villagerCount >= 2)
            quotas[NpcWorkRole.Wood] = 1;

        if (villagerCount >= 3)
            quotas[NpcWorkRole.Explore] = 1;
    }

    private static void AddWeightedDefaultQuota(int remainingVillagers)
    {
        int food = Mathf.FloorToInt(remainingVillagers * 0.3f);
        int wood = Mathf.FloorToInt(remainingVillagers * 0.3f);
        int explore = Mathf.FloorToInt(remainingVillagers * 0.3f);
        int build = Mathf.FloorToInt(remainingVillagers * 0.1f);

        quotas[NpcWorkRole.Food] += food;
        quotas[NpcWorkRole.Wood] += wood;
        quotas[NpcWorkRole.Explore] += explore;
        quotas[NpcWorkRole.Build] += build;

        int assigned = food + wood + explore + build;
        int remainder = remainingVillagers - assigned;

        AddRemainderQuota(NpcWorkRole.Food, ref remainder);
        AddRemainderQuota(NpcWorkRole.Wood, ref remainder);
        AddRemainderQuota(NpcWorkRole.Explore, ref remainder);
        AddRemainderQuota(NpcWorkRole.Build, ref remainder);
    }

    private static void AddRemainderQuota(NpcWorkRole role, ref int remainder)
    {
        if (remainder <= 0)
            return;

        quotas[role]++;
        remainder--;
    }

    private static bool TryReduceQuota(NpcWorkRole role)
    {
        if (GetQuota(role) <= 0)
            return false;

        quotas[role]--;
        return true;
    }

    private static void AddMissingPriorities(bool[] usedRoles)
    {
        AddPriorityIfMissing(NpcWorkRole.Food, usedRoles);
        AddPriorityIfMissing(NpcWorkRole.Wood, usedRoles);
        AddPriorityIfMissing(NpcWorkRole.Explore, usedRoles);
        AddPriorityIfMissing(NpcWorkRole.Build, usedRoles);
    }

    private static void AddPriorityIfMissing(NpcWorkRole role, bool[] usedRoles)
    {
        int roleIndex = GetRoleIndex(role);
        if (roleIndex < 0 || usedRoles[roleIndex])
            return;

        priorities.Add(role);
        usedRoles[roleIndex] = true;
    }

    private static int GetRoleIndex(NpcWorkRole role)
    {
        switch (role)
        {
            case NpcWorkRole.Food:
                return 0;
            case NpcWorkRole.Wood:
                return 1;
            case NpcWorkRole.Explore:
                return 2;
            case NpcWorkRole.Build:
                return 3;
            default:
                return -1;
        }
    }

    private static int GetRoleCount()
    {
        return 4;
    }

    public static ExploreDirection ClaimNextExploreDirection()
    {
        int directionIndex = nextExploreDirectionIndex % 4;
        nextExploreDirectionIndex = (nextExploreDirectionIndex + 1) % 4;
        SimulationPerformanceCounters.AddExploreDirectionAssignment();

        switch (directionIndex)
        {
            case 1:
                return ExploreDirection.East;
            case 2:
                return ExploreDirection.South;
            case 3:
                return ExploreDirection.West;
            default:
                return ExploreDirection.North;
        }
    }

    public static void ResetExploreDirectionAssignments()
    {
        nextExploreDirectionIndex = 0;
    }
}

public static class ManualDayPlanRoleSelector
{
    private const float TemporaryConstructionUnreachableDurationWorldHours = 0.12f;
    private const float ConstructionUnreachableStartCellSize = 6f;
    private const float ConstructionRequesterNavMeshSampleDistance = 2f;

    private static readonly Dictionary<ConstructionUnreachableKey, float> temporarilyUnreachableConstructionUntil =
        new Dictionary<ConstructionUnreachableKey, float>();
    private static readonly NavMeshPath sharedConstructionPath = new NavMeshPath();
    private static WorldTimeManager worldTimeManager;

    private struct ConstructionUnreachableKey : IEquatable<ConstructionUnreachableKey>
    {
        public ConstructionSite ConstructionSite;
        public Vector2Int StartCell;

        public bool Equals(ConstructionUnreachableKey other)
        {
            return ConstructionSite == other.ConstructionSite && StartCell == other.StartCell;
        }

        public override bool Equals(object obj)
        {
            return obj is ConstructionUnreachableKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            int siteHash = ConstructionSite != null ? ConstructionSite.GetInstanceID() : 0;
            return (siteHash * 397) ^ StartCell.GetHashCode();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        ClearConstructionUnreachableCache();
        worldTimeManager = null;
    }

    public static void ClearConstructionUnreachableCache()
    {
        temporarilyUnreachableConstructionUntil.Clear();
    }

    public static bool TryAssignNextRole(
        CharacterIdentity identity,
        out NpcWorkRole role,
        out string reason)
    {
        role = NpcWorkRole.Food;
        reason = string.Empty;
        SimulationPerformanceCounters.AddRoleSelectionAttempt();

        if (identity == null)
        {
            reason = "Manual day plan cannot assign a role because the NPC identity is missing.";
            return false;
        }

        int activeVillagerCount = GetActiveVillagerCount();
        ManualDayPlanState.EnsureDefaultQuotas(activeVillagerCount);
        IReadOnlyList<NpcWorkRole> priorities = ManualDayPlanState.GetPriorities();

        for (int i = 0; i < priorities.Count; i++)
        {
            NpcWorkRole candidate = priorities[i];

            if (ManualDayPlanState.HasLoopFlag(candidate))
                continue;

            int quota = ManualDayPlanState.GetQuota(candidate);
            if (quota <= 0)
                continue;

            if (NpcWorkRoleAssignmentRegistry.GetAssignedCount(candidate) >= quota)
                continue;

            if (!CanRoleBegin(candidate, out string unavailableReason, out bool shouldSetLoopFlag))
            {
                SetLoopFlagIfGloballyExhausted(candidate, unavailableReason, shouldSetLoopFlag);

                continue;
            }

            if (!NpcWorkRoleAssignmentRegistry.TryAssignUnderTarget(identity, candidate, quota))
                continue;

            role = candidate;
            reason = $"Manual day plan assigned {candidate}.";
            return true;
        }

        for (int i = 0; i < priorities.Count; i++)
        {
            NpcWorkRole candidate = priorities[i];

            if (ManualDayPlanState.HasLoopFlag(candidate))
                continue;

            if (!CanRoleBegin(candidate, out string unavailableReason, out bool shouldSetLoopFlag))
            {
                SetLoopFlagIfGloballyExhausted(candidate, unavailableReason, shouldSetLoopFlag);

                continue;
            }

            if (!NpcWorkRoleAssignmentRegistry.TryAssignAnyValidRole(identity, candidate))
                continue;

            role = candidate;
            reason = $"Manual day plan assigned extra {candidate} above quota target.";
            return true;
        }

        reason = "Manual day plan found no unflagged role with available work.";
        return false;
    }

    public static bool TryAssignFallbackRole(
        CharacterIdentity identity,
        ISet<NpcWorkRole> excludedRoles,
        out NpcWorkRole role,
        out string reason)
    {
        role = NpcWorkRole.Food;
        reason = string.Empty;
        SimulationPerformanceCounters.AddRoleSelectionAttempt();

        if (identity == null)
        {
            reason = "Manual day plan cannot assign a fallback role because the NPC identity is missing.";
            return false;
        }

        if (TryAssignFallbackCandidate(
            identity,
            NpcWorkRole.Explore,
            excludedRoles))
        {
            role = NpcWorkRole.Explore;
            reason = "Manual day plan assigned Explore as the preferred fallback.";
            return true;
        }

        IReadOnlyList<NpcWorkRole> priorities = ManualDayPlanState.GetPriorities();
        for (int i = 0; i < priorities.Count; i++)
        {
            NpcWorkRole candidate = priorities[i];
            if (candidate == NpcWorkRole.Explore)
                continue;

            if (!TryAssignFallbackCandidate(identity, candidate, excludedRoles))
                continue;

            role = candidate;
            reason = $"Manual day plan assigned {candidate} as the highest-priority fallback.";
            return true;
        }

        reason = "Manual day plan found no unflagged fallback role with globally available work.";
        return false;
    }

    public static bool IsRoleGloballyExhausted(NpcWorkRole role, out string reason)
    {
        switch (role)
        {
            case NpcWorkRole.Food:
                reason = "no known Berries resource exists.";
                return !ResourceRegistry.HasKnownFoodResource();
            case NpcWorkRole.Wood:
                reason = "no known Wood resource exists.";
                return !ResourceRegistry.HasKnownWoodResource(ManualDayPlanState.AllowCutEmptyBushes);
            case NpcWorkRole.Explore:
                reason = "NoFogAvailable";
                return FogRegistry.CountFrontierFog(true) <= 0;
            case NpcWorkRole.Build:
                reason = "no build order exists.";
                return !ConstructionSiteRegistry.HasActiveConstructionSite();
            default:
                reason = "unknown work role.";
                return false;
        }
    }

    public static bool CanBeginRoleNow(NpcWorkRole role)
    {
        if (ManualDayPlanState.HasLoopFlag(role))
            return false;

        return CanRoleBegin(role, out _, out _);
    }

    public static ResourceNode.ResourceType GetGatherResourceType(NpcWorkRole role)
    {
        return role == NpcWorkRole.Food
            ? ResourceNode.ResourceType.Berries
            : ResourceNode.ResourceType.Wood;
    }

    public static bool IsGatherRole(NpcWorkRole role)
    {
        return role == NpcWorkRole.Food || role == NpcWorkRole.Wood;
    }

    private static bool CanRoleBegin(
        NpcWorkRole role,
        out string reason,
        out bool shouldSetLoopFlag)
    {
        shouldSetLoopFlag = false;

        switch (role)
        {
            case NpcWorkRole.Food:
            case NpcWorkRole.Wood:
                SimulationPerformanceCounters.AddResourceAvailabilityCheapCheck();
                SimulationPerformanceCounters.AddResourceAvailabilityScansAvoided();
                if (role == NpcWorkRole.Food)
                    SimulationPerformanceCounters.AddFoodAvailabilityCheck();
                else
                    SimulationPerformanceCounters.AddWoodAvailabilityCheck();

                bool hasKnownResource = role == NpcWorkRole.Food
                    ? ResourceRegistry.HasKnownFoodResource()
                    : ResourceRegistry.HasKnownWoodResource(ManualDayPlanState.AllowCutEmptyBushes);

                if (!hasKnownResource)
                {
                    SimulationPerformanceCounters.AddRoleAvailabilityCacheMiss();
                    ResourceNode.ResourceType exhaustedResourceType = GetGatherResourceType(role);
                    reason = $"no known {exhaustedResourceType} resource exists.";
                    shouldSetLoopFlag = true;
                    return false;
                }

                ResourceNode.ResourceType resourceType = GetGatherResourceType(role);
                if (ResourceRegistry.HasAvailableResource(resourceType))
                {
                    SimulationPerformanceCounters.AddRoleAvailabilityCacheHit();
                    reason = string.Empty;
                    return true;
                }

                SimulationPerformanceCounters.AddRoleAvailabilityCacheMiss();
                reason = $"known {resourceType} resources are reserved or unreachable for this NPC.";
                shouldSetLoopFlag = role == NpcWorkRole.Food;
                return false;

            case NpcWorkRole.Explore:
                SimulationPerformanceCounters.AddExploreAvailabilityCheapCheck();
                SimulationPerformanceCounters.AddExploreAvailabilityCheck();
                SimulationPerformanceCounters.AddFogAvailabilityScansAvoided();
                if (FogRegistry.CountFrontierFog(true) <= 0)
                {
                    SimulationPerformanceCounters.AddRoleAvailabilityCacheMiss();
                    reason = "NoFogAvailable";
                    shouldSetLoopFlag = true;
                    return false;
                }

                if (FogRegistry.HasAvailableFrontierFog())
                {
                    SimulationPerformanceCounters.AddRoleAvailabilityCacheHit();
                    reason = string.Empty;
                    return true;
                }

                SimulationPerformanceCounters.AddRoleAvailabilityCacheMiss();
                reason = "frontier fog exists but is reserved or unreachable for this NPC.";
                return false;

            case NpcWorkRole.Build:
                SimulationPerformanceCounters.AddBuildAvailabilityCheck();
                SimulationPerformanceCounters.AddConstructionSiteScansAvoided();
                if (!ConstructionSiteRegistry.HasActiveConstructionSite())
                {
                    SimulationPerformanceCounters.AddRoleAvailabilityCacheMiss();
                    reason = "no build order exists.";
                    shouldSetLoopFlag = true;
                    return false;
                }

                if (ConstructionSiteRegistry.HasAvailableConstructionSite())
                {
                    SimulationPerformanceCounters.AddRoleAvailabilityCacheHit();
                    reason = string.Empty;
                    return true;
                }

                SimulationPerformanceCounters.AddRoleAvailabilityCacheMiss();
                reason = "build orders exist but are temporarily unreachable for this NPC.";
                return false;

            default:
                reason = "unknown work role.";
                return false;
        }
    }

    private static bool TryAssignFallbackCandidate(
        CharacterIdentity identity,
        NpcWorkRole candidate,
        ISet<NpcWorkRole> excludedRoles)
    {
        if ((excludedRoles != null && excludedRoles.Contains(candidate)) ||
            ManualDayPlanState.HasLoopFlag(candidate))
        {
            return false;
        }

        if (!CanRoleBegin(candidate, out string unavailableReason, out bool shouldSetLoopFlag))
        {
            SetLoopFlagIfGloballyExhausted(candidate, unavailableReason, shouldSetLoopFlag);
            return false;
        }

        return NpcWorkRoleAssignmentRegistry.TryAssignAnyValidRole(identity, candidate);
    }

    private static void SetLoopFlagIfGloballyExhausted(
        NpcWorkRole role,
        string unavailableReason,
        bool shouldSetLoopFlag)
    {
        if (!shouldSetLoopFlag)
            return;

        ManualDayPlanState.SetLoopFlag(role);
        Debug.Log($"Manual day plan set LoopFlag for {role}: {unavailableReason}");
    }

    private static int GetActiveVillagerCount()
    {
        SimulationPerformanceCounters.AddActiveVillagerCountCacheHit();
        return CharacterIdentity.ActiveIdentityCount;
    }

    public static bool TryGetClosestActiveConstructionSite(Vector3 requesterPosition, out ConstructionSite constructionSite)
    {
        constructionSite = null;
        float closestDistance = float.PositiveInfinity;

        if (!NavMesh.SamplePosition(
                requesterPosition,
                out NavMeshHit requesterHit,
                ConstructionRequesterNavMeshSampleDistance,
                NavMesh.AllAreas))
        {
            return false;
        }

        IReadOnlyList<ConstructionSite> constructionSites = ConstructionSiteRegistry.ActiveConstructionSites;
        for (int i = 0; i < constructionSites.Count; i++)
        {
            ConstructionSite candidate = constructionSites[i];
            if (candidate == null ||
                !candidate.isActiveAndEnabled ||
                candidate.IsComplete ||
                !candidate.CanAcceptWork())
            {
                continue;
            }

            if (IsConstructionSiteTemporarilyUnreachable(candidate, requesterPosition))
            {
                SimulationPerformanceCounters.AddSameTargetRetryPrevented();
                continue;
            }

            Vector3 interactionPosition = candidate.GetInteractionPosition(requesterHit.position);
            if (!NavMesh.CalculatePath(
                    requesterHit.position,
                    interactionPosition,
                    NavMesh.AllAreas,
                    sharedConstructionPath) ||
                sharedConstructionPath.status != NavMeshPathStatus.PathComplete)
            {
                MarkConstructionSiteTemporarilyUnreachable(
                    candidate,
                    requesterPosition,
                    "Build role skipped a construction site without a complete path."
                );
                continue;
            }

            float distance = (interactionPosition - requesterHit.position).sqrMagnitude;
            if (distance > closestDistance)
                continue;

            if (Mathf.Approximately(distance, closestDistance) &&
                constructionSite != null &&
                candidate.GetInstanceID() > constructionSite.GetInstanceID())
            {
                continue;
            }

            constructionSite = candidate;
            closestDistance = distance;
        }

        return constructionSite != null;
    }

    public static void MarkConstructionSiteTemporarilyUnreachable(
        ConstructionSite constructionSite,
        Vector3 requesterPosition,
        string reason)
    {
        if (constructionSite == null || !constructionSite.isActiveAndEnabled || constructionSite.IsComplete)
            return;

        if (!TryGetCurrentTotalWorldHours(out float currentTotalWorldHours))
            return;

        ConstructionUnreachableKey key = CreateConstructionUnreachableKey(constructionSite, requesterPosition);
        temporarilyUnreachableConstructionUntil[key] =
            currentTotalWorldHours + TemporaryConstructionUnreachableDurationWorldHours;
    }

    private static bool IsConstructionSiteTemporarilyUnreachable(
        ConstructionSite constructionSite,
        Vector3 requesterPosition)
    {
        if (constructionSite == null)
            return false;

        ConstructionUnreachableKey key = CreateConstructionUnreachableKey(constructionSite, requesterPosition);
        if (!temporarilyUnreachableConstructionUntil.TryGetValue(key, out float expiresAt))
            return false;

        if (!TryGetCurrentTotalWorldHours(out float currentTotalWorldHours))
        {
            temporarilyUnreachableConstructionUntil.Remove(key);
            return false;
        }

        if (currentTotalWorldHours < expiresAt)
            return true;

        temporarilyUnreachableConstructionUntil.Remove(key);
        return false;
    }

    private static bool TryGetCurrentTotalWorldHours(out float totalWorldHours)
    {
        if (worldTimeManager == null)
        {
            worldTimeManager = UnityEngine.Object.FindFirstObjectByType<WorldTimeManager>(
                FindObjectsInactive.Include
            );
        }

        if (worldTimeManager == null)
        {
            totalWorldHours = 0f;
            return false;
        }

        totalWorldHours = worldTimeManager.TotalWorldHours;
        return true;
    }

    private static ConstructionUnreachableKey CreateConstructionUnreachableKey(
        ConstructionSite constructionSite,
        Vector3 requesterPosition)
    {
        return new ConstructionUnreachableKey
        {
            ConstructionSite = constructionSite,
            StartCell = GetConstructionUnreachableStartCell(requesterPosition)
        };
    }

    private static Vector2Int GetConstructionUnreachableStartCell(Vector3 position)
    {
        float cellSize = Mathf.Max(0.1f, ConstructionUnreachableStartCellSize);
        return new Vector2Int(
            Mathf.FloorToInt(position.x / cellSize),
            Mathf.FloorToInt(position.z / cellSize)
        );
    }
}

public static class NpcWorkRoleAssignmentRegistry
{
    private static readonly Dictionary<int, NpcWorkRole> assignments = new Dictionary<int, NpcWorkRole>();
    private static int assignedFoodCount;
    private static int assignedWoodCount;
    private static int assignedExploreCount;
    private static int assignedBuildCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        assignments.Clear();
        assignedFoodCount = 0;
        assignedWoodCount = 0;
        assignedExploreCount = 0;
        assignedBuildCount = 0;
    }

    public static bool TryAssignUnderTarget(CharacterIdentity identity, NpcWorkRole role, int quotaTarget)
    {
        if (identity == null || quotaTarget <= 0)
            return false;

        int characterId = identity.CharacterId;
        if (assignments.TryGetValue(characterId, out NpcWorkRole assignedRole))
            return assignedRole == role;

        if (GetAssignedCount(role) >= quotaTarget)
            return false;

        assignments[characterId] = role;
        IncrementAssignedCount(role);
        ManualDayPlanState.NotifyRoleAssignmentsChanged();
        SimulationEventLogger.LogRoleEvent(
            identity,
            "RoleAssigned",
            role,
            $"Citizen was assigned to {role} under quota target {quotaTarget}."
        );
        return true;
    }

    public static void Release(CharacterIdentity identity)
    {
        if (identity == null)
            return;

        int characterId = identity.CharacterId;
        if (!assignments.TryGetValue(characterId, out NpcWorkRole assignedRole) ||
            !assignments.Remove(characterId))
            return;

        DecrementAssignedCount(assignedRole);
        ManualDayPlanState.NotifyRoleAssignmentsChanged();
        SimulationEventLogger.LogRoleEvent(
            identity,
            "RoleReleased",
            assignedRole,
            $"Citizen released role {assignedRole}."
        );
    }

    public static int GetAssignedCount(NpcWorkRole role)
    {
        switch (role)
        {
            case NpcWorkRole.Food:
                return assignedFoodCount;
            case NpcWorkRole.Wood:
                return assignedWoodCount;
            case NpcWorkRole.Explore:
                return assignedExploreCount;
            case NpcWorkRole.Build:
                return assignedBuildCount;
            default:
                return 0;
        }
    }

    public static bool TryGetAssignedRole(CharacterIdentity identity, out NpcWorkRole role)
    {
        role = NpcWorkRole.Food;

        if (identity == null)
            return false;

        return assignments.TryGetValue(identity.CharacterId, out role);
    }

    public static int GetTotalAssignedCount()
    {
        return assignments.Count;
    }

    public static bool TryAssignAnyValidRole(CharacterIdentity identity, NpcWorkRole role)
    {
        if (identity == null)
            return false;

        int characterId = identity.CharacterId;
        if (assignments.TryGetValue(characterId, out NpcWorkRole assignedRole))
            return assignedRole == role;

        assignments[characterId] = role;
        IncrementAssignedCount(role);
        ManualDayPlanState.NotifyRoleAssignmentsChanged();
        SimulationEventLogger.LogRoleEvent(
            identity,
            "FallbackRoleAssigned",
            role,
            $"Citizen was assigned to fallback role {role} above the current quota if necessary."
        );
        return true;
    }

    public static bool TryAssignRequestedRole(CharacterIdentity identity, NpcWorkRole role)
    {
        if (identity == null)
            return false;

        int characterId = identity.CharacterId;
        if (assignments.TryGetValue(characterId, out NpcWorkRole assignedRole))
            return assignedRole == role;

        assignments[characterId] = role;
        IncrementAssignedCount(role);
        ManualDayPlanState.NotifyRoleAssignmentsChanged();
        SimulationEventLogger.LogRoleEvent(
            identity,
            "RequestedRoleAssigned",
            role,
            $"Citizen was assigned to the explicitly requested {role} role."
        );
        return true;
    }

    private static void IncrementAssignedCount(NpcWorkRole role)
    {
        switch (role)
        {
            case NpcWorkRole.Food:
                assignedFoodCount++;
                break;
            case NpcWorkRole.Wood:
                assignedWoodCount++;
                break;
            case NpcWorkRole.Explore:
                assignedExploreCount++;
                break;
            case NpcWorkRole.Build:
                assignedBuildCount++;
                break;
        }
    }

    private static void DecrementAssignedCount(NpcWorkRole role)
    {
        switch (role)
        {
            case NpcWorkRole.Food:
                assignedFoodCount--;
                break;
            case NpcWorkRole.Wood:
                assignedWoodCount--;
                break;
            case NpcWorkRole.Explore:
                assignedExploreCount--;
                break;
            case NpcWorkRole.Build:
                assignedBuildCount--;
                break;
        }
    }
}
