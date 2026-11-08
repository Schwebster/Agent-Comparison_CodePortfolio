using System;

namespace AgentComparison.Goap
{
    public readonly struct GoapStateChange
    {
        internal ulong SetTrueFacts { get; }
        internal ulong SetFalseFacts { get; }

        public static GoapStateChange Empty => default;
        public bool IsEmpty => SetTrueFacts == 0UL && SetFalseFacts == 0UL;

        private GoapStateChange(ulong setTrueFacts, ulong setFalseFacts)
        {
            SetTrueFacts = setTrueFacts;
            SetFalseFacts = setFalseFacts;
        }

        public GoapStateChange Set(GoapFact fact, bool value)
        {
            ulong bit = GoapWorldState.GetBit(fact);

            if (value && (SetFalseFacts & bit) != 0UL ||
                !value && (SetTrueFacts & bit) != 0UL)
            {
                throw new InvalidOperationException($"GOAP fact {fact} cannot be set to both true and false.");
            }

            return value
                ? new GoapStateChange(SetTrueFacts | bit, SetFalseFacts)
                : new GoapStateChange(SetTrueFacts, SetFalseFacts | bit);
        }
    }
}
