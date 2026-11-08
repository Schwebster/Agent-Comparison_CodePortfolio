/*
===============================================================================
Script:
AgentTaskType

Purpose:
Names the shared task types future agent controllers can request.

Folder:
Assets/Scripts/Agents/Common/Tasks/

Scene Hierarchy:
None

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

public enum AgentTaskType
{
    None,
    Idle,
    GatherResource,
    ExploreFog,
    CollectResource,
    DepositResources,
    Build,
    GoHome,
    Sleep
}
