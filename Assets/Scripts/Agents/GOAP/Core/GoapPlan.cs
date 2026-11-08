using System;
using System.Collections.Generic;

namespace AgentComparison.Goap
{
    public enum GoapPlanFailure { None, NoGoals, Unreachable, SearchBudgetExceeded }

    public sealed class GoapPlan
    {
        private static readonly IReadOnlyList<GoapAction> noActions = Array.Empty<GoapAction>();

        public bool IsSuccess { get; }
        public GoapGoal Goal { get; }
        public IReadOnlyList<GoapAction> Actions { get; }
        public int TotalCost { get; }
        public string FailureReason { get; }
        public GoapPlanFailure FailureKind { get; }

        private GoapPlan(
            bool isSuccess,
            GoapGoal goal,
            IReadOnlyList<GoapAction> actions,
            int totalCost,
            string failureReason,
            GoapPlanFailure failureKind = GoapPlanFailure.None)
        {
            IsSuccess = isSuccess;
            Goal = goal;
            Actions = actions;
            TotalCost = totalCost;
            FailureReason = failureReason;
            FailureKind = failureKind;
        }

        internal static GoapPlan Success(GoapGoal goal, List<GoapAction> actions, int totalCost)
        {
            return new GoapPlan(
                true,
                goal,
                actions.AsReadOnly(),
                totalCost,
                string.Empty
            );
        }

        internal static GoapPlan Failure(string reason, GoapPlanFailure kind = GoapPlanFailure.Unreachable)
        {
            return new GoapPlan(false, null, noActions, 0, reason, kind);
        }
    }
}
