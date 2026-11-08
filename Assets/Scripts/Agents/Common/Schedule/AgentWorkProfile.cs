/*
===============================================================================
Script:
AgentWorkProfile

Purpose:
Stores per-NPC work settings that shared schedule queries can read.

Folder:
Assets/Scripts/Agents/Common/Schedule/

Scene Hierarchy:
_World
- Characters
  - Character

Attach To:
NPC character root

Required Children:
None

Dependencies:
- ResourceNode
- AgentScheduleConfig

Automatically Resolved:
None

Inspector References:
- gatherResourceType
- repeatGatherTask
- schedule
===============================================================================
*/

using UnityEngine;

[DisallowMultipleComponent]
public class AgentWorkProfile : MonoBehaviour
{
    [SerializeField] private ResourceNode.ResourceType gatherResourceType = ResourceNode.ResourceType.Wood;
    [SerializeField] private bool repeatGatherTask = true;
    [SerializeField] private AgentScheduleConfig schedule = new AgentScheduleConfig();

    public ResourceNode.ResourceType GatherResourceType => gatherResourceType;
    public bool RepeatGatherTask => repeatGatherTask;
    public AgentScheduleConfig Schedule => schedule;
    public float WorkStartHour => SafeSchedule.WorkStartHour;
    public float WorkEndHour => SafeSchedule.WorkEndHour;
    public float SleepStartHour => SafeSchedule.SleepStartHour;
    public float SleepEndHour => SafeSchedule.SleepEndHour;

    private AgentScheduleConfig SafeSchedule
    {
        get
        {
            if (schedule == null)
                schedule = new AgentScheduleConfig();

            return schedule;
        }
    }

    private void Awake()
    {
        ValidateSettings();
    }

    private void OnValidate()
    {
        ValidateSettings();
    }

    public bool IsWorkTime(float worldHour)
    {
        return SafeSchedule.IsWorkTime(worldHour);
    }

    public bool IsSleepTime(float worldHour)
    {
        return SafeSchedule.IsSleepTime(worldHour);
    }

    private void ValidateSettings()
    {
        SafeSchedule.Validate();
    }
}
