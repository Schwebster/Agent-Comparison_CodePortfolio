using System;

namespace AgentComparison.Goap
{
    public sealed class GoapGoal
    {
        public string Name { get; }
        public int Priority { get; }
        public GoapStatePattern DesiredState { get; }

        public GoapGoal(string name, int priority, GoapStatePattern desiredState)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("GOAP goal name must not be empty.", nameof(name));

            if (desiredState.IsEmpty)
                throw new ArgumentException("GOAP goal must require at least one fact.", nameof(desiredState));

            Name = name;
            Priority = priority;
            DesiredState = desiredState;
        }
    }
}
