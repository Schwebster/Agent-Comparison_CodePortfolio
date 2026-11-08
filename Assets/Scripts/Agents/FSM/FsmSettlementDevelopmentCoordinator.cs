using System.Collections.Generic;

// Retains the existing FSM entry point; the morning policy is shared with GOAP.
public static class FsmSettlementDevelopmentCoordinator
{
    public static void TryRun(
        FsmAgentController requester,
        IReadOnlyList<FsmAgentController> activeControllers,
        WorldTimeManager worldTimeManager,
        FsmStrategyConfig strategy)
    {
        if (requester == null || activeControllers == null || strategy == null ||
            !IsCoordinator(requester, activeControllers))
            return;

        SettlementMorningDevelopment.TryRun(requester.ArchitectureName, worldTimeManager,
            requester.SimulationBalanceConfig, requester.DailyFoodRequirement,
            requester.DailyWoodRequirement, strategy.PopulationFoodReserveDays);
    }

    private static bool IsCoordinator(
        FsmAgentController requester,
        IReadOnlyList<FsmAgentController> activeControllers)
    {
        FsmAgentController coordinator = null;

        for (int i = 0; i < activeControllers.Count; i++)
        {
            FsmAgentController candidate = activeControllers[i];
            if (candidate == null || !candidate.isActiveAndEnabled || candidate.IsDead)
                continue;

            if (coordinator == null || candidate.CharacterId < coordinator.CharacterId)
                coordinator = candidate;
        }

        return coordinator == requester;
    }
}
