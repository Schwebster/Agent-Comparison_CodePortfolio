/*
===============================================================================
Script:
AgentDecision

Purpose:
Describes what an agent wants to do without executing the action.

Folder:
Assets/Scripts/Agents/Core/

Scene Hierarchy:
Created in code by future agent controllers

Attach To:
None

Required Children:
None

Dependencies:
- AgentDecisionType
- ResourceNode
- ResourceStorage

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/

using UnityEngine;

public sealed class AgentDecision
{
    public AgentDecisionType DecisionType { get; }
    public Vector3 TargetPosition { get; }
    public ResourceNode TargetResource { get; }
    public ResourceStorage TargetStorage { get; }
    public string Reason { get; }

    private AgentDecision(
        AgentDecisionType decisionType,
        Vector3 targetPosition,
        ResourceNode targetResource,
        ResourceStorage targetStorage,
        string reason)
    {
        DecisionType = decisionType;
        TargetPosition = targetPosition;
        TargetResource = targetResource;
        TargetStorage = targetStorage;
        Reason = string.IsNullOrEmpty(reason) ? "No reason provided." : reason;
    }

    public static AgentDecision None(string reason = "")
    {
        return new AgentDecision(AgentDecisionType.None, Vector3.zero, null, null, reason);
    }

    public static AgentDecision Wait(string reason = "")
    {
        return new AgentDecision(AgentDecisionType.Wait, Vector3.zero, null, null, reason);
    }

    public static AgentDecision MoveTo(Vector3 position, string reason = "")
    {
        return new AgentDecision(AgentDecisionType.MoveTo, position, null, null, reason);
    }

    public static AgentDecision Harvest(ResourceNode resource, string reason = "")
    {
        Vector3 position = resource != null ? resource.transform.position : Vector3.zero;
        return new AgentDecision(AgentDecisionType.Harvest, position, resource, null, reason);
    }

    public static AgentDecision Deposit(ResourceStorage storage, string reason = "")
    {
        Vector3 position = storage != null ? storage.transform.position : Vector3.zero;
        return new AgentDecision(AgentDecisionType.Deposit, position, null, storage, reason);
    }

    public string GetTargetDescription()
    {
        if (TargetResource != null)
            return TargetResource.gameObject.name;

        if (TargetStorage != null)
            return TargetStorage.gameObject.name;

        if (DecisionType == AgentDecisionType.MoveTo)
            return TargetPosition.ToString("F2");

        return "None";
    }
}
