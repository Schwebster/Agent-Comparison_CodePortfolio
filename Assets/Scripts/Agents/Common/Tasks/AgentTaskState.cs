/*
===============================================================================
Script:
AgentTaskState

Purpose:
Tracks the lifecycle state of the currently assigned shared NPC task.

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

public enum AgentTaskState
{
    Start,
    Running,
    Completed,
    Failed,
    Cancelled
}
