using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AgentComparison.Goap.Tests
{
    public sealed class GoapPlannerTests
    {
        private readonly GoapPlanner planner = new GoapPlanner();

        [Test]
        public void CreatePlan_SelectsHighestPriorityReachableGoal()
        {
            GoapWorldState state = GoapWorldState.Empty
                .With(GoapFact.StorageAvailable, true)
                .With(GoapFact.FoodSourceAvailable, true)
                .With(GoapFact.UnrevealedAreaAvailable, true);
            GoapGoal[] goals =
            {
                GoapSurvivalDomain.RevealExplorationTarget(10),
                GoapSurvivalDomain.DeliverFood(100)
            };

            GoapPlan plan = planner.CreatePlan(state, goals, GoapSurvivalDomain.CreateActions());

            Assert.That(plan.IsSuccess, Is.True);
            Assert.That(plan.Goal.Name, Is.EqualTo("DeliverFood"));
            Assert.That(plan.Actions.Select(action => action.Name), Is.EqualTo(new[] { "GatherFood" }));
        }

        [Test]
        public void CreatePlan_ReturnsLoadBeforeStartingGatherCycle()
        {
            GoapWorldState state = GoapWorldState.Empty
                .With(GoapFact.HasDepositLoad, true)
                .With(GoapFact.StorageAvailable, true)
                .With(GoapFact.FoodSourceAvailable, true);
            GoapPlan plan = planner.CreatePlan(state,
                new[] { GoapSurvivalDomain.DeliverFood(100) }, GoapSurvivalDomain.CreateActions());
            Assert.That(plan.IsSuccess, Is.True);
            Assert.That(plan.TotalCost, Is.EqualTo(2));
            Assert.That(plan.Actions.Select(action => action.Name), Is.EqualTo(new[] { "Deposit", "GatherFood" }));
            GoapWorldState simulated = state;
            foreach (GoapAction action in plan.Actions)
            {
                Assert.That(action.CanApply(simulated), Is.True);
                simulated = action.Apply(simulated);
            }
            Assert.That(simulated.IsTrue(GoapFact.FoodDelivered), Is.True);
            Assert.That(simulated.IsTrue(GoapFact.FoodReserveSafe), Is.False);
            Assert.That(state.IsTrue(GoapFact.HasDepositLoad), Is.True, "Search must not mutate observations.");
        }

        [Test]
        public void CreatePlan_FallsBackWhenHigherPriorityGoalIsUnreachable()
        {
            GoapWorldState state = GoapWorldState.Empty
                .With(GoapFact.UnrevealedAreaAvailable, true);
            GoapGoal[] goals =
            {
                GoapSurvivalDomain.DeliverFood(100),
                GoapSurvivalDomain.RevealExplorationTarget(10)
            };

            GoapPlan plan = planner.CreatePlan(state, goals, GoapSurvivalDomain.CreateActions());

            Assert.That(plan.IsSuccess, Is.True);
            Assert.That(plan.Goal.Name, Is.EqualTo("RevealExplorationTarget"));
            Assert.That(plan.Actions.Select(action => action.Name), Is.EqualTo(new[] { "Explore" }));
        }

        [Test]
        public void CreatePlan_UsesOrdinalActionNameForEqualCostTie()
        {
            GoapAction firstByName = CreateFoodAction("A_GatherFood");
            GoapAction secondByName = CreateFoodAction("B_GatherFood");
            GoapGoal[] goals =
            {
                GoapSurvivalDomain.DeliverFood(1)
            };

            for (int attempt = 0; attempt < 20; attempt++)
            {
                IEnumerable<GoapAction> actions = attempt % 2 == 0
                    ? new[] { secondByName, firstByName }
                    : new[] { firstByName, secondByName };

                GoapPlan plan = planner.CreatePlan(GoapWorldState.Empty, goals, actions);

                Assert.That(plan.IsSuccess, Is.True);
                Assert.That(plan.Actions[0].Name, Is.EqualTo("A_GatherFood"));
            }
        }

        [Test]
        public void CreatePlan_ReturnsEmptyPlanWhenGoalIsAlreadySatisfied()
        {
            GoapWorldState state = GoapWorldState.Empty.With(GoapFact.FoodDelivered, true);
            GoapGoal[] goals =
            {
                GoapSurvivalDomain.DeliverFood(100)
            };

            GoapPlan plan = planner.CreatePlan(state, goals, GoapSurvivalDomain.CreateActions());

            Assert.That(plan.IsSuccess, Is.True);
            Assert.That(plan.TotalCost, Is.Zero);
            Assert.That(plan.Actions, Is.Empty);
        }

        [Test]
        public void CreatePlan_SkipsSatisfiedGoalWhenAnotherGoalNeedsWork()
        {
            GoapWorldState state = GoapWorldState.Empty
                .With(GoapFact.FoodDelivered, true)
                .With(GoapFact.UnrevealedAreaAvailable, true);
            GoapGoal[] goals =
            {
                GoapSurvivalDomain.DeliverFood(100),
                GoapSurvivalDomain.RevealExplorationTarget(10)
            };

            GoapPlan plan = planner.CreatePlan(state, goals, GoapSurvivalDomain.CreateActions());

            Assert.That(plan.IsSuccess, Is.True);
            Assert.That(plan.Goal.Name, Is.EqualTo("RevealExplorationTarget"));
            Assert.That(plan.Actions.Select(action => action.Name), Is.EqualTo(new[] { "Explore" }));
        }

        [Test]
        public void CreatePlan_ReturnsFailureWhenNoGoalCanBeReached()
        {
            GoapGoal[] goals =
            {
                GoapSurvivalDomain.CompleteConstructionSite(100)
            };

            GoapPlan plan = planner.CreatePlan(
                GoapWorldState.Empty,
                goals,
                GoapSurvivalDomain.CreateActions()
            );

            Assert.That(plan.IsSuccess, Is.False);
            Assert.That(plan.Actions, Is.Empty);
            Assert.That(plan.FailureReason, Does.Contain("reachable"));
        }

        private static GoapAction CreateFoodAction(string name)
        {
            return new GoapAction(
                name,
                1,
                GoapStatePattern.Empty,
                GoapStateChange.Empty.Set(GoapFact.FoodDelivered, true)
            );
        }
    }
}
