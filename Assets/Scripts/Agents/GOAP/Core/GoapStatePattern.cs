using System;

namespace AgentComparison.Goap
{
    public readonly struct GoapStatePattern
    {
        internal ulong RequiredTrueFacts { get; }
        internal ulong RequiredFalseFacts { get; }

        public static GoapStatePattern Empty => default;
        public bool IsEmpty => RequiredTrueFacts == 0UL && RequiredFalseFacts == 0UL;

        private GoapStatePattern(ulong requiredTrueFacts, ulong requiredFalseFacts)
        {
            RequiredTrueFacts = requiredTrueFacts;
            RequiredFalseFacts = requiredFalseFacts;
        }

        public GoapStatePattern Require(GoapFact fact, bool value)
        {
            ulong bit = GoapWorldState.GetBit(fact);

            if (value && (RequiredFalseFacts & bit) != 0UL ||
                !value && (RequiredTrueFacts & bit) != 0UL)
            {
                throw new InvalidOperationException($"GOAP fact {fact} cannot be required as both true and false.");
            }

            return value
                ? new GoapStatePattern(RequiredTrueFacts | bit, RequiredFalseFacts)
                : new GoapStatePattern(RequiredTrueFacts, RequiredFalseFacts | bit);
        }
    }
}
