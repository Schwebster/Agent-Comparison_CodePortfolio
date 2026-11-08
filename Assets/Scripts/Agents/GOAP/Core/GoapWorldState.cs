using System;

namespace AgentComparison.Goap
{
    public readonly struct GoapWorldState : IEquatable<GoapWorldState>
    {
        private readonly ulong trueFacts;

        public static GoapWorldState Empty => default;

        private GoapWorldState(ulong trueFacts)
        {
            this.trueFacts = trueFacts;
        }

        public bool IsTrue(GoapFact fact)
        {
            return (trueFacts & GetBit(fact)) != 0UL;
        }

        public GoapWorldState With(GoapFact fact, bool value)
        {
            ulong bit = GetBit(fact);
            ulong nextFacts = value
                ? trueFacts | bit
                : trueFacts & ~bit;

            return new GoapWorldState(nextFacts);
        }

        public bool Satisfies(GoapStatePattern pattern)
        {
            return (trueFacts & pattern.RequiredTrueFacts) == pattern.RequiredTrueFacts &&
                (trueFacts & pattern.RequiredFalseFacts) == 0UL;
        }

        public GoapWorldState Apply(GoapStateChange change)
        {
            ulong nextFacts = (trueFacts | change.SetTrueFacts) & ~change.SetFalseFacts;
            return new GoapWorldState(nextFacts);
        }

        public bool Equals(GoapWorldState other)
        {
            return trueFacts == other.trueFacts;
        }

        public override bool Equals(object obj)
        {
            return obj is GoapWorldState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return trueFacts.GetHashCode();
        }

        public static bool operator ==(GoapWorldState left, GoapWorldState right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GoapWorldState left, GoapWorldState right)
        {
            return !left.Equals(right);
        }

        internal static ulong GetBit(GoapFact fact)
        {
            int index = (int)fact;
            if (index < 0 || index >= (int)GoapFact.Count || index >= 64)
                throw new ArgumentOutOfRangeException(nameof(fact), fact, "GOAP fact is outside the supported range.");

            return 1UL << index;
        }
    }
}
