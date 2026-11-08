/*
===============================================================================
Script:
DecisionLogger

Purpose:
Logs agent decisions in a readable format for later evaluation.

Folder:
Assets/Scripts/Agents/Shared/

Scene Hierarchy:
Used by future agent evaluation systems

Attach To:
None

Required Children:
None

Dependencies:
- AgentContext
- AgentDecision

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/

using UnityEngine;

public static class DecisionLogger
{
    public static void LogDecision(string agentName, AgentContext context, AgentDecision decision)
    {
        string resolvedAgentName = string.IsNullOrEmpty(agentName) ? "Unnamed Agent" : agentName;
        string characterName = context != null ? context.CharacterName : "None";
        AgentDecision resolvedDecision = decision ?? AgentDecision.None("Decision was null.");

        Debug.Log(
            $"AgentDecision | Agent: {resolvedAgentName} | Character: {characterName} | Control: {GetControlMode(context)} | Decision: {resolvedDecision.DecisionType} | Target: {resolvedDecision.GetTargetDescription()} | Reason: {resolvedDecision.Reason}"
        );
    }

    public static void LogDecision(IAgentController agentController, AgentContext context, AgentDecision decision)
    {
        string agentName = agentController != null ? agentController.AgentName : "Unnamed Agent";
        LogDecision(agentName, context, decision);
    }

    private static CharacterControlMode GetControlMode(AgentContext context)
    {
        return context != null ? context.ControlMode : CharacterControlMode.None;
    }
}
