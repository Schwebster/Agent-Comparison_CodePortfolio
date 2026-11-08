/*
===============================================================================
Script:
AgentDesiredTask

Purpose:
Describes the high-level task intent selected by shared schedule policy.

Folder:
Assets/Scripts/Agents/Common/Schedule/

Scene Hierarchy:
Created in code by AgentDailyRoutine

Attach To:
None

Required Children:
None

Dependencies:
- AgentTaskType
- ResourceNode

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/

public struct AgentDesiredTask
{
    public AgentTaskType TaskType { get; }
    public ResourceNode.ResourceType ResourceType { get; }
    public bool Repeat { get; }
    public bool ShouldCancelCurrentTask { get; }
    public string Reason { get; }
    public bool HasTask => TaskType != AgentTaskType.None;
    public bool UsesResource => TaskType == AgentTaskType.GatherResource;

    private AgentDesiredTask(
        AgentTaskType taskType,
        ResourceNode.ResourceType resourceType,
        bool repeat,
        bool shouldCancelCurrentTask,
        string reason)
    {
        TaskType = taskType;
        ResourceType = resourceType;
        Repeat = repeat;
        ShouldCancelCurrentTask = shouldCancelCurrentTask;
        Reason = reason;
    }

    public static AgentDesiredTask None(string reason)
    {
        return new AgentDesiredTask(AgentTaskType.None, ResourceNode.ResourceType.Wood, false, false, reason);
    }

    public static AgentDesiredTask Idle(string reason)
    {
        return new AgentDesiredTask(AgentTaskType.Idle, ResourceNode.ResourceType.Wood, false, false, reason);
    }

    public static AgentDesiredTask GatherResource(
        ResourceNode.ResourceType resourceType,
        bool repeat,
        bool shouldCancelCurrentTask,
        string reason)
    {
        return new AgentDesiredTask(AgentTaskType.GatherResource, resourceType, repeat, shouldCancelCurrentTask, reason);
    }

    public static AgentDesiredTask GoHome(bool shouldCancelCurrentTask, string reason)
    {
        return new AgentDesiredTask(AgentTaskType.GoHome, ResourceNode.ResourceType.Wood, false, shouldCancelCurrentTask, reason);
    }

    public static AgentDesiredTask Sleep(bool shouldCancelCurrentTask, string reason)
    {
        return new AgentDesiredTask(AgentTaskType.Sleep, ResourceNode.ResourceType.Wood, false, shouldCancelCurrentTask, reason);
    }
}
