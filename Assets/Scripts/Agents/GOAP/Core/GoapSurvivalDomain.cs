using System.Collections.Generic;

namespace AgentComparison.Goap
{
    // One action is one shared execution cycle, not a settlement-wide objective.
    // Contribution facts start false in each observation and are verified after execution.
    public static class GoapSurvivalDomain
    {
        public const string BuildActionName = "Build";
        public const string ExploreActionName = "Explore";
        public const string GatherFoodActionName = "GatherFood";
        public const string GatherWoodActionName = "GatherWood";
        public const string DepositActionName = "Deposit";

        public static IReadOnlyList<GoapAction> CreateActions()
        {
            GoapStatePattern emptyHands = GoapStatePattern.Empty.Require(GoapFact.HasDepositLoad, false);
            return new[]
            {
                new GoapAction(DepositActionName, 1,
                    GoapStatePattern.Empty.Require(GoapFact.HasDepositLoad, true)
                        .Require(GoapFact.StorageAvailable, true),
                    GoapStateChange.Empty.Set(GoapFact.HasDepositLoad, false)),
                // Materials are paid by the shared service when a construction site is placed.
                new GoapAction(BuildActionName, 1,
                    emptyHands.Require(GoapFact.ConstructionSiteAvailable, true),
                    GoapStateChange.Empty.Set(GoapFact.ConstructionSiteCompleted, true)),
                new GoapAction(ExploreActionName, 1,
                    emptyHands.Require(GoapFact.UnrevealedAreaAvailable, true),
                    GoapStateChange.Empty.Set(GoapFact.ExplorationTargetRevealed, true)),
                new GoapAction(GatherFoodActionName, 1,
                    emptyHands.Require(GoapFact.FoodSourceAvailable, true)
                        .Require(GoapFact.StorageAvailable, true),
                    GoapStateChange.Empty.Set(GoapFact.FoodDelivered, true)),
                new GoapAction(GatherWoodActionName, 1,
                    emptyHands.Require(GoapFact.WoodSourceAvailable, true)
                        .Require(GoapFact.StorageAvailable, true),
                    GoapStateChange.Empty.Set(GoapFact.WoodDelivered, true))
            };
        }

        public static GoapGoal DeliverFood(int priority) => new GoapGoal(
            "DeliverFood", priority, GoapStatePattern.Empty.Require(GoapFact.FoodDelivered, true));
        public static GoapGoal DeliverWood(int priority) => new GoapGoal(
            "DeliverWood", priority, GoapStatePattern.Empty.Require(GoapFact.WoodDelivered, true));
        public static GoapGoal RevealExplorationTarget(int priority) => new GoapGoal(
            "RevealExplorationTarget", priority,
            GoapStatePattern.Empty.Require(GoapFact.ExplorationTargetRevealed, true));
        public static GoapGoal CompleteConstructionSite(int priority) => new GoapGoal(
            "CompleteConstructionSite", priority,
            GoapStatePattern.Empty.Require(GoapFact.ConstructionSiteCompleted, true));
        public static GoapGoal ReturnLoad(int priority) => new GoapGoal(
            "ReturnLoad", priority, GoapStatePattern.Empty.Require(GoapFact.HasDepositLoad, false));
    }
}
