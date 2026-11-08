/*
===============================================================================
Script:
IAgentController

Purpose:
Defines the shared decision contract for future autonomous agent controllers.

Folder:
Assets/Scripts/Agents/Core/

Scene Hierarchy:
Implemented by future agent controller components

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

public interface IAgentController
{
    string AgentName { get; }
    AgentDecision Decide(AgentContext context);
}
