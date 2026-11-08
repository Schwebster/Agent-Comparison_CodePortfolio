using System;
using System.Linq;
using NUnit.Framework;

namespace AgentComparison.Goap.Tests
{
    public sealed class GoapDomainTests
    {
        private readonly GoapPlanner planner = new GoapPlanner();

        [Test]
        public void LowestCostSequenceWinsOverShorterExpensiveAction()
        {
            var state = GoapWorldState.Empty.With(GoapFact.HasDepositLoad, true)
                .With(GoapFact.StorageAvailable, true).With(GoapFact.FoodSourceAvailable, true);
            var expensive = new GoapAction("DirectDelivery", 5, GoapStatePattern.Empty,
                GoapStateChange.Empty.Set(GoapFact.FoodDelivered, true));
            var plan = planner.CreatePlan(state, new[] { GoapSurvivalDomain.DeliverFood(1) },
                GoapSurvivalDomain.CreateActions().Concat(new[] { expensive }));
            Assert.That(plan.Actions.Select(a => a.Name), Is.EqualTo(new[] { "Deposit", "GatherFood" }));
            Assert.That(plan.TotalCost, Is.EqualTo(2));
        }

        [Test]
        public void MissingStorageMakesDeliveryUnreachable()
        {
            var state = GoapWorldState.Empty.With(GoapFact.FoodSourceAvailable, true);
            var plan = planner.CreatePlan(state, new[] { GoapSurvivalDomain.DeliverFood(1) },
                GoapSurvivalDomain.CreateActions());
            Assert.That(plan.FailureKind, Is.EqualTo(GoapPlanFailure.Unreachable));
        }

        [Test]
        public void PaidConstructionSiteDoesNotRequireAnotherWoodReserve()
        {
            var state = GoapWorldState.Empty.With(GoapFact.ConstructionSiteAvailable, true);
            var plan = planner.CreatePlan(state, new[] { GoapSurvivalDomain.CompleteConstructionSite(1) },
                GoapSurvivalDomain.CreateActions());
            Assert.That(plan.Actions.Select(a => a.Name), Is.EqualTo(new[] { "Build" }));
        }

        [Test]
        public void ExplorePromisesOneTargetRatherThanAResourceReserve()
        {
            var state = GoapWorldState.Empty.With(GoapFact.UnrevealedAreaAvailable, true);
            var action = GoapSurvivalDomain.CreateActions().Single(a => a.Name == "Explore");
            var result = action.Apply(state);
            Assert.That(result.IsTrue(GoapFact.ExplorationTargetRevealed), Is.True);
            Assert.That(result.IsTrue(GoapFact.FoodReserveSafe), Is.False);
            Assert.That(result.IsTrue(GoapFact.UnrevealedAreaAvailable), Is.True);
        }

        [Test]
        public void BudgetExhaustionIsNotReportedAsUnreachableOrLowerPrioritySuccess()
        {
            var state = GoapWorldState.Empty.With(GoapFact.HasDepositLoad, true)
                .With(GoapFact.StorageAvailable, true).With(GoapFact.FoodSourceAvailable, true);
            var plan = new GoapPlanner(1).CreatePlan(state,
                new[] { GoapSurvivalDomain.DeliverFood(100), GoapSurvivalDomain.ReturnLoad(1) },
                GoapSurvivalDomain.CreateActions());
            Assert.That(plan.IsSuccess, Is.False);
            Assert.That(plan.FailureKind, Is.EqualTo(GoapPlanFailure.SearchBudgetExceeded));
        }

        [Test]
        public void GoalOnBudgetBoundaryIsAcceptedWithoutAnotherExpansion()
        {
            var state = GoapWorldState.Empty.With(GoapFact.HasDepositLoad, true)
                .With(GoapFact.StorageAvailable, true);
            var plan = new GoapPlanner(1).CreatePlan(state,
                new[] { GoapSurvivalDomain.ReturnLoad(1) }, GoapSurvivalDomain.CreateActions());
            Assert.That(plan.IsSuccess, Is.True);
        }

        [Test]
        public void ExhaustedFrontierAtBudgetBoundaryIsUnreachable()
        {
            var plan = new GoapPlanner(1).CreatePlan(GoapWorldState.Empty,
                new[] { GoapSurvivalDomain.DeliverFood(1) }, Array.Empty<GoapAction>());
            Assert.That(plan.FailureKind, Is.EqualTo(GoapPlanFailure.Unreachable));
        }

        [Test]
        public void RemovedActionAllowsReachableAlternativeGoal()
        {
            var state = GoapWorldState.Empty.With(GoapFact.FoodSourceAvailable, true)
                .With(GoapFact.StorageAvailable, true).With(GoapFact.UnrevealedAreaAvailable, true);
            var plan = planner.CreatePlan(state,
                new[] { GoapSurvivalDomain.DeliverFood(100), GoapSurvivalDomain.RevealExplorationTarget(1) },
                GoapSurvivalDomain.CreateActions().Where(a => a.Name != "GatherFood"));
            Assert.That(plan.Goal.Name, Is.EqualTo("RevealExplorationTarget"));
        }

        [Test]
        public void ChangedObservationInvalidatesPreviouslyApplicableAction()
        {
            var action = GoapSurvivalDomain.CreateActions().Single(a => a.Name == "GatherFood");
            var state = GoapWorldState.Empty.With(GoapFact.FoodSourceAvailable, true)
                .With(GoapFact.StorageAvailable, true);
            Assert.That(action.CanApply(state), Is.True);
            Assert.That(action.CanApply(state.With(GoapFact.FoodSourceAvailable, false)), Is.False);
            Assert.Throws<InvalidOperationException>(() => action.Apply(GoapWorldState.Empty));
        }

        [Test]
        public void PositiveCostsAndConflictingFactsAreValidated()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GoapAction("Free", 0,
                GoapStatePattern.Empty, GoapStateChange.Empty.Set(GoapFact.FoodDelivered, true)));
            Assert.Throws<InvalidOperationException>(() => GoapStatePattern.Empty
                .Require(GoapFact.HasDepositLoad, true).Require(GoapFact.HasDepositLoad, false));
        }

        [Test]
        public void NoGoalsHasDistinctFailureKind()
        {
            Assert.That(planner.CreatePlan(GoapWorldState.Empty, Array.Empty<GoapGoal>(),
                GoapSurvivalDomain.CreateActions()).FailureKind, Is.EqualTo(GoapPlanFailure.NoGoals));
        }
    }
}
