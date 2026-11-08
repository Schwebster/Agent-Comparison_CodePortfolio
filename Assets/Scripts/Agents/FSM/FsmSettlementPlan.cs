using System.Collections.Generic;

public sealed class FsmSettlementPlan
{
    private readonly Dictionary<NpcWorkRole, int> quotas;

    public FsmAgentState State { get; }
    public IReadOnlyList<NpcWorkRole> Priorities { get; }
    public IDictionary<NpcWorkRole, int> Quotas => quotas;

    public FsmSettlementPlan(
        FsmAgentState state,
        IReadOnlyList<NpcWorkRole> priorities,
        int foodQuota,
        int woodQuota,
        int exploreQuota,
        int buildQuota)
    {
        State = state;
        Priorities = priorities;
        quotas = new Dictionary<NpcWorkRole, int>
        {
            { NpcWorkRole.Food, foodQuota },
            { NpcWorkRole.Wood, woodQuota },
            { NpcWorkRole.Explore, exploreQuota },
            { NpcWorkRole.Build, buildQuota }
        };
    }

    public int GetQuota(NpcWorkRole role)
    {
        return quotas.TryGetValue(role, out int quota) ? quota : 0;
    }
}
