using System;
using System.Collections.Generic;

namespace AgentComparison.Goap
{
    public sealed class GoapPlanner
    {
        private const int DefaultMaximumExpandedStates = 4096;

        private readonly int maximumExpandedStates;

        private sealed class SearchNode
        {
            public GoapWorldState State { get; }
            public List<GoapAction> Actions { get; }
            public int TotalCost { get; }
            public string PathKey { get; }

            public SearchNode(
                GoapWorldState state,
                List<GoapAction> actions,
                int totalCost,
                string pathKey)
            {
                State = state;
                Actions = actions;
                TotalCost = totalCost;
                PathKey = pathKey;
            }
        }

        private readonly struct VisitRecord
        {
            public int TotalCost { get; }
            public int ActionCount { get; }
            public string PathKey { get; }

            public VisitRecord(int totalCost, int actionCount, string pathKey)
            {
                TotalCost = totalCost;
                ActionCount = actionCount;
                PathKey = pathKey;
            }
        }

        public GoapPlanner(int maximumExpandedStates = DefaultMaximumExpandedStates)
        {
            if (maximumExpandedStates <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumExpandedStates),
                    maximumExpandedStates,
                    "Maximum expanded GOAP states must be positive."
                );
            }

            this.maximumExpandedStates = maximumExpandedStates;
        }

        public GoapPlan CreatePlan(
            GoapWorldState initialState,
            IEnumerable<GoapGoal> goals,
            IEnumerable<GoapAction> actions)
        {
            if (goals == null)
                throw new ArgumentNullException(nameof(goals));

            if (actions == null)
                throw new ArgumentNullException(nameof(actions));

            List<GoapGoal> orderedGoals = CopyAndValidateGoals(goals);
            List<GoapAction> orderedActions = CopyAndValidateActions(actions);
            if (orderedGoals.Count == 0)
                return GoapPlan.Failure("No GOAP goals were provided.", GoapPlanFailure.NoGoals);

            orderedGoals.Sort(CompareGoals);

            bool hasUnsatisfiedGoal = false;
            for (int i = 0; i < orderedGoals.Count; i++)
            {
                if (!initialState.Satisfies(orderedGoals[i].DesiredState))
                {
                    hasUnsatisfiedGoal = true;
                    break;
                }
            }

            if (!hasUnsatisfiedGoal)
                return GoapPlan.Success(orderedGoals[0], new List<GoapAction>(), 0);

            int goalIndex = 0;
            while (goalIndex < orderedGoals.Count)
            {
                int priority = orderedGoals[goalIndex].Priority;
                GoapPlan bestPlanAtPriority = null;

                while (goalIndex < orderedGoals.Count && orderedGoals[goalIndex].Priority == priority)
                {
                    GoapGoal goal = orderedGoals[goalIndex];
                    if (!initialState.Satisfies(goal.DesiredState))
                    {
                        GoapPlan candidate = CreatePlanForGoal(initialState, goal, orderedActions);
                        // A budget limit does not prove this priority is unreachable.
                        if (candidate.FailureKind == GoapPlanFailure.SearchBudgetExceeded)
                            return candidate;
                        if (candidate.IsSuccess && IsBetterGoalPlan(candidate, bestPlanAtPriority))
                            bestPlanAtPriority = candidate;
                    }

                    goalIndex++;
                }

                if (bestPlanAtPriority != null)
                    return bestPlanAtPriority;
            }

            return GoapPlan.Failure("No provided GOAP goal is reachable from the current world state.");
        }

        private GoapPlan CreatePlanForGoal(
            GoapWorldState initialState,
            GoapGoal goal,
            IReadOnlyList<GoapAction> orderedActions)
        {
            if (initialState.Satisfies(goal.DesiredState))
                return GoapPlan.Success(goal, new List<GoapAction>(), 0);

            List<SearchNode> openNodes = new List<SearchNode>
            {
                new SearchNode(initialState, new List<GoapAction>(), 0, string.Empty)
            };
            Dictionary<GoapWorldState, VisitRecord> bestVisits = new Dictionary<GoapWorldState, VisitRecord>
            {
                { initialState, new VisitRecord(0, 0, string.Empty) }
            };

            int expandedStates = 0;
            while (openNodes.Count > 0)
            {
                int bestNodeIndex = FindBestNodeIndex(openNodes);
                SearchNode current = openNodes[bestNodeIndex];
                openNodes.RemoveAt(bestNodeIndex);

                VisitRecord bestVisit = bestVisits[current.State];
                if (IsBetterVisit(bestVisit.TotalCost, bestVisit.ActionCount, bestVisit.PathKey,
                    new VisitRecord(current.TotalCost, current.Actions.Count, current.PathKey)))
                    continue;

                if (current.State.Satisfies(goal.DesiredState))
                    return GoapPlan.Success(goal, current.Actions, current.TotalCost);

                if (expandedStates >= maximumExpandedStates)
                    return GoapPlan.Failure(
                        $"GOAP search reached the limit of {maximumExpandedStates} expanded states.",
                        GoapPlanFailure.SearchBudgetExceeded);

                expandedStates++;

                for (int actionIndex = 0; actionIndex < orderedActions.Count; actionIndex++)
                {
                    GoapAction action = orderedActions[actionIndex];
                    if (!action.CanApply(current.State))
                        continue;

                    GoapWorldState nextState = action.Apply(current.State);
                    if (nextState == current.State)
                        continue;

                    int nextCost = checked(current.TotalCost + action.Cost);
                    int nextActionCount = current.Actions.Count + 1;
                    string nextPathKey = AppendPathKey(current.PathKey, action.Name);

                    if (bestVisits.TryGetValue(nextState, out VisitRecord previousVisit) &&
                        !IsBetterVisit(nextCost, nextActionCount, nextPathKey, previousVisit))
                    {
                        continue;
                    }

                    List<GoapAction> nextActions = new List<GoapAction>(current.Actions)
                    {
                        action
                    };

                    bestVisits[nextState] = new VisitRecord(nextCost, nextActionCount, nextPathKey);
                    openNodes.Add(new SearchNode(nextState, nextActions, nextCost, nextPathKey));
                }
            }

            return GoapPlan.Failure($"GOAP goal {goal.Name} is unreachable.");
        }

        private static List<GoapGoal> CopyAndValidateGoals(IEnumerable<GoapGoal> goals)
        {
            List<GoapGoal> result = new List<GoapGoal>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

            foreach (GoapGoal goal in goals)
            {
                if (goal == null)
                    throw new ArgumentException("GOAP goal collection must not contain null.", nameof(goals));

                if (!names.Add(goal.Name))
                    throw new ArgumentException($"GOAP goal names must be unique. Duplicate: {goal.Name}.", nameof(goals));

                result.Add(goal);
            }

            return result;
        }

        private static List<GoapAction> CopyAndValidateActions(IEnumerable<GoapAction> actions)
        {
            List<GoapAction> result = new List<GoapAction>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

            foreach (GoapAction action in actions)
            {
                if (action == null)
                    throw new ArgumentException("GOAP action collection must not contain null.", nameof(actions));

                if (!names.Add(action.Name))
                    throw new ArgumentException($"GOAP action names must be unique. Duplicate: {action.Name}.", nameof(actions));

                result.Add(action);
            }

            result.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            return result;
        }

        private static int CompareGoals(GoapGoal left, GoapGoal right)
        {
            int priorityComparison = right.Priority.CompareTo(left.Priority);
            return priorityComparison != 0
                ? priorityComparison
                : string.CompareOrdinal(left.Name, right.Name);
        }

        private static bool IsBetterGoalPlan(GoapPlan candidate, GoapPlan currentBest)
        {
            if (currentBest == null)
                return true;

            if (candidate.TotalCost != currentBest.TotalCost)
                return candidate.TotalCost < currentBest.TotalCost;

            if (candidate.Actions.Count != currentBest.Actions.Count)
                return candidate.Actions.Count < currentBest.Actions.Count;

            return string.CompareOrdinal(candidate.Goal.Name, currentBest.Goal.Name) < 0;
        }

        private static int FindBestNodeIndex(IReadOnlyList<SearchNode> nodes)
        {
            int bestIndex = 0;

            for (int i = 1; i < nodes.Count; i++)
            {
                if (CompareNodes(nodes[i], nodes[bestIndex]) < 0)
                    bestIndex = i;
            }

            return bestIndex;
        }

        private static int CompareNodes(SearchNode left, SearchNode right)
        {
            if (left.TotalCost != right.TotalCost)
                return left.TotalCost.CompareTo(right.TotalCost);

            if (left.Actions.Count != right.Actions.Count)
                return left.Actions.Count.CompareTo(right.Actions.Count);

            return string.CompareOrdinal(left.PathKey, right.PathKey);
        }

        private static bool IsBetterVisit(
            int totalCost,
            int actionCount,
            string pathKey,
            VisitRecord previous)
        {
            if (totalCost != previous.TotalCost)
                return totalCost < previous.TotalCost;

            if (actionCount != previous.ActionCount)
                return actionCount < previous.ActionCount;

            return string.CompareOrdinal(pathKey, previous.PathKey) < 0;
        }

        private static string AppendPathKey(string currentPathKey, string actionName)
        {
            return currentPathKey.Length == 0
                ? actionName
                : currentPathKey + "\u001f" + actionName;
        }
    }
}
