/*
===============================================================================
Script:
AgentDailyRoutine

Purpose:
Provides deterministic shared day-cycle task intent queries for future agent controllers.

Folder:
Assets/Scripts/Agents/Common/Schedule/

Scene Hierarchy:
Used by FSM, GOAP, or other future controllers

Attach To:
None

Required Children:
None

Dependencies:
- AgentContext
- AgentWorkProfile
- AgentDesiredTask

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/

public static class AgentDailyRoutine
{
    public static bool IsWorkTime(float worldHour, AgentWorkProfile workProfile)
    {
        return GetSchedule(workProfile).IsWorkTime(worldHour);
    }

    public static bool IsSleepTime(float worldHour, AgentWorkProfile workProfile)
    {
        return GetSchedule(workProfile).IsSleepTime(worldHour);
    }

    public static AgentDesiredTask GetDesiredTask(
        AgentContext context,
        float worldHour,
        AgentWorkProfile workProfile)
    {
        AgentScheduleConfig schedule = GetSchedule(workProfile);
        bool isSleepTime = schedule.IsSleepTime(worldHour);
        bool isWorkTime = schedule.IsWorkTime(worldHour);

        if (context != null && context.IsSleeping && !context.HasCompletedRequiredSleep)
            return AgentDesiredTask.Sleep(false, "NPC is sleeping and has not completed the required sleep duration.");

        if (isWorkTime)
            return GetWorkIntent(context, workProfile);

        if (isSleepTime && context != null && context.SleepState == SleepExecutionState.Completed)
            return AgentDesiredTask.Idle("Required sleep duration is complete; waiting for the work window.");

        if (isSleepTime)
        {
            if (context == null)
                return AgentDesiredTask.GoHome(false, "Sleep time; resolve a home or overnight barn shelter.");

            if (!context.HasHome && !context.IsSleeping)
                SimulationPerformanceCounters.AddVisibleHomelessNpcDuringSleep();

            return GetSleepIntent(context);
        }

        if (context != null && context.IsBusy)
            return AgentDesiredTask.None("No schedule change; keep current task.");

        return AgentDesiredTask.Idle("No active work or sleep window.");
    }

    private static AgentDesiredTask GetSleepIntent(AgentContext context)
    {
        if (context.IsSleeping)
            return AgentDesiredTask.Sleep(false, "Sleep time and NPC is already sleeping.");

        if (context.IsAtOvernightDestination)
        {
            string location = context.IsAtHome ? "home" : "overnight barn shelter";
            return AgentDesiredTask.Sleep(
                ShouldCancelCurrentTask(context, AgentTaskType.Sleep),
                $"Sleep time and NPC is at its {location}."
            );
        }

        string destination = context.HasHome ? "assigned home" : "closest active barn shelter";
        return AgentDesiredTask.GoHome(
            ShouldCancelCurrentTask(context, AgentTaskType.GoHome),
            $"Sleep time; travel to {destination}."
        );
    }

    private static AgentDesiredTask GetWorkIntent(AgentContext context, AgentWorkProfile workProfile)
    {
        ResourceNode.ResourceType resourceType = GetGatherResourceType(workProfile);
        bool repeat = workProfile == null || workProfile.RepeatGatherTask;
        bool shouldCancel = ShouldCancelForWork(context);

        return AgentDesiredTask.GatherResource(resourceType, repeat, shouldCancel, "Work time.");
    }

    private static bool ShouldCancelCurrentTask(AgentContext context, AgentTaskType desiredTask)
    {
        return context != null &&
            context.IsBusy &&
            context.CurrentTask != desiredTask;
    }

    private static bool ShouldCancelForWork(AgentContext context)
    {
        if (context == null || !context.IsBusy)
            return false;

        if (context.CurrentTask == AgentTaskType.GatherResource ||
            context.CurrentTask == AgentTaskType.ExploreFog ||
            context.CurrentTask == AgentTaskType.CollectResource ||
            context.CurrentTask == AgentTaskType.DepositResources ||
            context.CurrentTask == AgentTaskType.Build)
        {
            return false;
        }

        return true;
    }

    private static ResourceNode.ResourceType GetGatherResourceType(AgentWorkProfile workProfile)
    {
        return workProfile != null ? workProfile.GatherResourceType : ResourceNode.ResourceType.Wood;
    }

    private static AgentScheduleConfig GetSchedule(AgentWorkProfile workProfile)
    {
        return workProfile != null && workProfile.Schedule != null
            ? workProfile.Schedule
            : new AgentScheduleConfig();
    }
}
