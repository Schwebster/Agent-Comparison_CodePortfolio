using System;

namespace AgentComparison.Goap
{
    public sealed class GoapAction
    {
        public string Name { get; }
        public int Cost { get; }
        public GoapStatePattern Preconditions { get; }
        public GoapStateChange Effects { get; }

        public GoapAction(
            string name,
            int cost,
            GoapStatePattern preconditions,
            GoapStateChange effects)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("GOAP action name must not be empty.", nameof(name));

            if (cost <= 0)
                throw new ArgumentOutOfRangeException(nameof(cost), cost, "GOAP action cost must be positive.");

            if (effects.IsEmpty)
                throw new ArgumentException("GOAP action must change at least one fact.", nameof(effects));

            Name = name;
            Cost = cost;
            Preconditions = preconditions;
            Effects = effects;
        }

        public bool CanApply(GoapWorldState state)
        {
            return state.Satisfies(Preconditions);
        }

        public GoapWorldState Apply(GoapWorldState state)
        {
            if (!CanApply(state))
                throw new InvalidOperationException($"GOAP action {Name} cannot be applied because its preconditions are not satisfied.");

            return state.Apply(Effects);
        }
    }
}
