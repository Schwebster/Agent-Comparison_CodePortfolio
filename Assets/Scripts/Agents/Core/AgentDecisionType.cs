/*
===============================================================================
Script:
AgentDecisionType

Purpose:
Defines the simple action categories an agent decision can request.

Folder:
Assets/Scripts/Agents/Core/

Scene Hierarchy:
Used by future agent decision systems

Attach To:
None

Required Children:
None

Dependencies:
None

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/

public enum AgentDecisionType
{
    None,
    Wait,
    MoveTo,
    Harvest,
    Deposit
}
