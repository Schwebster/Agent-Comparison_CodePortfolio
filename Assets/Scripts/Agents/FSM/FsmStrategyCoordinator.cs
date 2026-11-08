using System.Collections.Generic;
using UnityEngine;

public static class FsmStrategyCoordinator
{
    private static readonly NpcWorkRole[] explorationPriorities =
    {
        NpcWorkRole.Explore,
        NpcWorkRole.Food,
        NpcWorkRole.Wood,
        NpcWorkRole.Build
    };

    private static readonly NpcWorkRole[] foodPriorities =
    {
        NpcWorkRole.Food,
        NpcWorkRole.Wood,
        NpcWorkRole.Explore,
        NpcWorkRole.Build
    };

    private static readonly NpcWorkRole[] woodPriorities =
    {
        NpcWorkRole.Wood,
        NpcWorkRole.Food,
        NpcWorkRole.Explore,
        NpcWorkRole.Build
    };

    private static readonly NpcWorkRole[] constructionPriorities =
    {
        NpcWorkRole.Build,
        NpcWorkRole.Wood,
        NpcWorkRole.Food,
        NpcWorkRole.Explore
    };

    private static readonly List<FsmAgentController> livingAgents = new List<FsmAgentController>();
    private static readonly List<FsmAgentController> recallCandidates = new List<FsmAgentController>();

    private static FsmAgentState currentState = FsmAgentState.ExplorationFocus;
    private static FsmSettlementPlan currentPlan;
    private static float nextEvaluationTotalWorldHours;
    private static float stateEnteredAtTotalWorldHours;
    private static int lastEvaluationFrame = -1;
    private static bool initialized;

    public static FsmAgentState CurrentState => currentState;
    public static FsmSettlementPlan CurrentPlan => currentPlan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        livingAgents.Clear();
        recallCandidates.Clear();
        currentState = FsmAgentState.ExplorationFocus;
        currentPlan = null;
        nextEvaluationTotalWorldHours = 0f;
        stateEnteredAtTotalWorldHours = 0f;
        lastEvaluationFrame = -1;
        initialized = false;
    }

    public static void TryEvaluate(
        FsmAgentController requester,
        IReadOnlyList<FsmAgentController> activeAgents,
        WorldTimeManager worldTimeManager,
        FsmStrategyConfig config)
    {
        if (requester == null || activeAgents == null || config == null)
            return;

        if (lastEvaluationFrame == Time.frameCount)
            return;

        CollectLivingAgents(activeAgents);
        if (livingAgents.Count <= 0 || livingAgents[0] != requester)
            return;

        lastEvaluationFrame = Time.frameCount;
        float totalWorldHours = worldTimeManager != null
            ? worldTimeManager.TotalWorldHours
            : Time.time;

        if (initialized && totalWorldHours < nextEvaluationTotalWorldHours)
            return;

        FsmSettlementSnapshot snapshot = CreateSnapshot(livingAgents);
        string previousState = initialized ? currentState.ToString() : "Uninitialized";
        FsmAgentState nextState = SelectState(snapshot, config, totalWorldHours);
        bool stateChanged = !initialized || nextState != currentState;

        if (stateChanged)
        {
            currentState = nextState;
            stateEnteredAtTotalWorldHours = totalWorldHours;
        }

        FsmSettlementPlan nextPlan = CreatePlan(currentState, snapshot.LivingNpcCount, config);
        bool planChanged = !HasSamePlan(currentPlan, nextPlan);
        currentPlan = nextPlan;
        initialized = true;
        nextEvaluationTotalWorldHours = totalWorldHours + config.ReevaluationIntervalWorldHours;

        if (planChanged)
        {
            ManualDayPlanState.SetPriorities(nextPlan.Priorities);
            ManualDayPlanState.SetQuotas(nextPlan.Quotas);
            ManualDayPlanState.ClearLoopFlags();
            RequestRequiredReassignments(nextPlan, livingAgents);
        }

        string prioritiesText = FormatPriorities(nextPlan.Priorities);
        string quotasText =
            $"Food={nextPlan.GetQuota(NpcWorkRole.Food)}," +
            $"Wood={nextPlan.GetQuota(NpcWorkRole.Wood)}," +
            $"Explore={nextPlan.GetQuota(NpcWorkRole.Explore)}," +
            $"Build={nextPlan.GetQuota(NpcWorkRole.Build)}";

        SimulationEventLogger.LogStrategyDecision(
            stateChanged
                ? "StateTransition"
                : planChanged
                    ? "PlanAdjusted"
                    : "StateMaintained",
            previousState,
            currentState.ToString(),
            prioritiesText,
            quotasText,
            $"Food reserve={snapshot.FoodReserveDays:0.##} days; wood reserve={snapshot.WoodReserveDays:0.##} days."
        );

        if (!planChanged)
            return;

        Debug.Log(
            $"FSM strategy changed to {currentState}. " +
            $"Priorities: {prioritiesText}. " +
            $"Quotas: Food={nextPlan.GetQuota(NpcWorkRole.Food)}, " +
            $"Wood={nextPlan.GetQuota(NpcWorkRole.Wood)}, " +
            $"Explore={nextPlan.GetQuota(NpcWorkRole.Explore)}, " +
            $"Build={nextPlan.GetQuota(NpcWorkRole.Build)}. " +
            $"Food reserve={snapshot.FoodReserveDays:0.##} days, " +
            $"Wood reserve={snapshot.WoodReserveDays:0.##} days."
        );
    }

    private static void CollectLivingAgents(IReadOnlyList<FsmAgentController> activeAgents)
    {
        livingAgents.Clear();

        for (int i = 0; i < activeAgents.Count; i++)
        {
            FsmAgentController agent = activeAgents[i];
            if (agent == null || !agent.isActiveAndEnabled || agent.IsDead)
                continue;

            livingAgents.Add(agent);
        }

        livingAgents.Sort(CompareAgentsByCharacterId);
    }

    private static FsmSettlementSnapshot CreateSnapshot(IReadOnlyList<FsmAgentController> agents)
    {
        int population = agents.Count;
        int foodPerNpc = population > 0 ? agents[0].DailyFoodRequirement : 0;
        int woodPerNpc = population > 0 ? agents[0].DailyWoodRequirement : 0;

        return new FsmSettlementSnapshot(
            population,
            ResourceStorageRegistry.SharedBerriesStored,
            ResourceStorageRegistry.SharedWoodStored,
            population * foodPerNpc,
            population * woodPerNpc,
            FogRegistry.CountUnrevealedFog(),
            ConstructionSiteRegistry.HasActiveConstructionSite()
        );
    }

    private static FsmAgentState SelectState(
        FsmSettlementSnapshot snapshot,
        FsmStrategyConfig config,
        float totalWorldHours)
    {
        bool minimumDurationComplete = !initialized ||
            totalWorldHours - stateEnteredAtTotalWorldHours >= config.MinimumStateDurationWorldHours;

        if (!minimumDurationComplete)
            return currentState;

        bool foodEmergency = snapshot.FoodReserveDays < config.CriticalReserveDays;
        bool woodEmergency = snapshot.WoodReserveDays < config.CriticalReserveDays;

        if (currentState == FsmAgentState.FoodEmergency &&
            snapshot.FoodReserveDays < config.RecoveredReserveDays &&
            (!woodEmergency || snapshot.FoodReserveDays <= snapshot.WoodReserveDays))
        {
            return FsmAgentState.FoodEmergency;
        }

        if (currentState == FsmAgentState.WoodEmergency &&
            snapshot.WoodReserveDays < config.RecoveredReserveDays &&
            (!foodEmergency || snapshot.WoodReserveDays <= snapshot.FoodReserveDays))
        {
            return FsmAgentState.WoodEmergency;
        }

        if (foodEmergency || woodEmergency)
        {
            return snapshot.FoodReserveDays <= snapshot.WoodReserveDays
                ? FsmAgentState.FoodEmergency
                : FsmAgentState.WoodEmergency;
        }

        if (snapshot.HasActiveConstructionSite && config.MaximumBuilders > 0)
            return FsmAgentState.ConstructionFocus;

        if (snapshot.UnrevealedFogCount > 0)
            return FsmAgentState.ExplorationFocus;

        return FsmAgentState.Balanced;
    }

    private static FsmSettlementPlan CreatePlan(
        FsmAgentState state,
        int population,
        FsmStrategyConfig config)
    {
        int food = 0;
        int wood = 0;
        int explore = 0;
        int build = 0;
        int remaining = Mathf.Max(0, population);

        switch (state)
        {
            case FsmAgentState.FoodEmergency:
                food = TakeWorkers(ref remaining, Mathf.CeilToInt(population * config.EmergencyWorkerShare));
                wood = TakeWorkers(ref remaining, 1);
                explore = remaining;
                return new FsmSettlementPlan(state, foodPriorities, food, wood, explore, build);

            case FsmAgentState.WoodEmergency:
                wood = TakeWorkers(ref remaining, Mathf.CeilToInt(population * config.EmergencyWorkerShare));
                food = TakeWorkers(ref remaining, 1);
                explore = remaining;
                return new FsmSettlementPlan(state, woodPriorities, food, wood, explore, build);

            case FsmAgentState.ConstructionFocus:
                build = TakeWorkers(ref remaining, config.MaximumBuilders);
                wood = TakeWorkers(ref remaining, 1);
                food = TakeWorkers(ref remaining, 1);
                explore = remaining;
                return new FsmSettlementPlan(state, constructionPriorities, food, wood, explore, build);

            case FsmAgentState.ExplorationFocus:
                explore = TakeWorkers(ref remaining, 1);
                food = TakeWorkers(ref remaining, 1);
                wood = TakeWorkers(ref remaining, 1);
                explore += remaining;
                return new FsmSettlementPlan(state, explorationPriorities, food, wood, explore, build);

            default:
                food = TakeWorkers(ref remaining, Mathf.CeilToInt(population * 0.5f));
                wood = remaining;
                return new FsmSettlementPlan(state, foodPriorities, food, wood, explore, build);
        }
    }

    private static int TakeWorkers(ref int remaining, int requested)
    {
        int taken = Mathf.Min(remaining, Mathf.Max(0, requested));
        remaining -= taken;
        return taken;
    }

    private static void RequestRequiredReassignments(
        FsmSettlementPlan plan,
        IReadOnlyList<FsmAgentController> agents)
    {
        int replacementSlots = CountAvailableReplacementSlots(plan);
        if (replacementSlots <= 0)
            return;

        for (int i = plan.Priorities.Count - 1; i >= 0 && replacementSlots > 0; i--)
        {
            NpcWorkRole role = plan.Priorities[i];
            RequestRoleReassignments(
                role,
                plan.GetQuota(role),
                agents,
                ref replacementSlots
            );
        }
    }

    private static int CountAvailableReplacementSlots(FsmSettlementPlan plan)
    {
        int replacementSlots = 0;

        for (int i = 0; i < plan.Priorities.Count; i++)
        {
            NpcWorkRole role = plan.Priorities[i];
            int deficit = plan.GetQuota(role) - NpcWorkRoleAssignmentRegistry.GetAssignedCount(role);
            if (deficit <= 0 || !ManualDayPlanRoleSelector.CanBeginRoleNow(role))
                continue;

            replacementSlots += deficit;
        }

        return replacementSlots;
    }

    private static void RequestRoleReassignments(
        NpcWorkRole role,
        int targetQuota,
        IReadOnlyList<FsmAgentController> agents,
        ref int replacementSlots)
    {
        int excess = NpcWorkRoleAssignmentRegistry.GetAssignedCount(role) - targetQuota;
        if (excess <= 0 || replacementSlots <= 0)
            return;

        recallCandidates.Clear();
        for (int i = 0; i < agents.Count; i++)
        {
            FsmAgentController agent = agents[i];
            if (!agent.IsAssignedToRole(role))
                continue;

            if (agent.IsReassignmentRequested)
            {
                excess--;
                replacementSlots = Mathf.Max(0, replacementSlots - 1);
                continue;
            }

            recallCandidates.Add(agent);
        }

        recallCandidates.Sort(CompareAgentsByCharacterIdDescending);
        for (int i = 0;
            i < recallCandidates.Count && excess > 0 && replacementSlots > 0;
            i++)
        {
            recallCandidates[i].RequestWorkReassignment();
            excess--;
            replacementSlots--;
        }
    }

    private static bool HasSamePlan(FsmSettlementPlan left, FsmSettlementPlan right)
    {
        if (left == null || right == null || left.State != right.State)
            return false;

        for (int i = 0; i < explorationPriorities.Length; i++)
        {
            if (left.Priorities[i] != right.Priorities[i])
                return false;
        }

        return left.GetQuota(NpcWorkRole.Food) == right.GetQuota(NpcWorkRole.Food) &&
            left.GetQuota(NpcWorkRole.Wood) == right.GetQuota(NpcWorkRole.Wood) &&
            left.GetQuota(NpcWorkRole.Explore) == right.GetQuota(NpcWorkRole.Explore) &&
            left.GetQuota(NpcWorkRole.Build) == right.GetQuota(NpcWorkRole.Build);
    }

    private static int CompareAgentsByCharacterId(FsmAgentController left, FsmAgentController right)
    {
        return left.CharacterId.CompareTo(right.CharacterId);
    }

    private static int CompareAgentsByCharacterIdDescending(FsmAgentController left, FsmAgentController right)
    {
        return right.CharacterId.CompareTo(left.CharacterId);
    }

    private static string FormatPriorities(IReadOnlyList<NpcWorkRole> priorities)
    {
        return $"{priorities[0]} > {priorities[1]} > {priorities[2]} > {priorities[3]}";
    }
}
