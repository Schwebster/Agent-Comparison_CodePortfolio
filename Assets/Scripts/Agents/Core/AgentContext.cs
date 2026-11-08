/*
===============================================================================
Script:
AgentContext

Purpose:
Provides simple read-only character, home, resource, task, and execution-state information for agent decisions.

Folder:
Assets/Scripts/Agents/Core/

Scene Hierarchy:
Created in code for future agent decision evaluation

Attach To:
None

Required Children:
None

Dependencies:
- Character
- NpcHomeAssignment
- HomeTravelExecutor
- SleepExecutor
- NpcResourceReservation
- GatherEquipmentExecutor
- GatherResourceExecutor
- ResourceCollectionExecutor
- ResourceDepositExecutor
- NpcTaskController
- AgentWorkProfile
- CharacterControlRegistry
- CharacterControlMode

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/

using System.Collections.Generic;
using UnityEngine;

public sealed class AgentContext
{
    private static readonly HashSet<int> missingRuntimeWarnings = new HashSet<int>();

    public string CharacterName { get; }
    public Vector3 CharacterPosition { get; }
    public int CarriedWood { get; }
    public int CarriedBerries { get; }
    public bool HasCarriedResources { get; }
    public bool HasGatherEquipment { get; }
    public bool HasDepositLoad { get; }
    public CharacterControlMode ControlMode { get; }
    public bool IsPlayerControlled => ControlMode == CharacterControlMode.Player;
    public bool IsAutonomousControlled => ControlMode == CharacterControlMode.Autonomous;
    public bool HasHome { get; }
    public bool IsAtHome { get; }
    public bool HasHomePosition { get; }
    public Vector3 HomePosition { get; }
    public HomeTravelState HomeTravelState { get; }
    public bool HasBarnShelter { get; }
    public bool IsAtBarnShelter { get; }
    public bool HasOvernightDestination => HasHome || HasBarnShelter;
    public bool IsAtOvernightDestination => IsAtHome || IsAtBarnShelter;
    public bool IsSleeping { get; }
    public SleepExecutionState SleepState { get; }
    public bool HasCompletedRequiredSleep { get; }
    public float SleepElapsedWorldHours { get; }
    public float SleepRemainingWorldHours { get; }
    public bool HasReservedResource { get; }
    public bool HasReservedResourcePosition { get; }
    public Vector3 ReservedResourcePosition { get; }
    public GatherEquipmentState GatherEquipmentState { get; }
    public GatherResourceState GatherResourceState { get; }
    public ResourceCollectionState ResourceCollectionState { get; }
    public ResourceDepositState ResourceDepositState { get; }
    public AgentTaskType CurrentTask { get; }
    public AgentTaskState TaskState { get; }
    public bool IsBusy { get; }
    public ResourceNode.ResourceType WorkResourceType { get; }
    public bool RepeatGatherTask { get; }
    public float WorkStartHour { get; }
    public float WorkEndHour { get; }
    public float SleepStartHour { get; }
    public float SleepEndHour { get; }
    public bool IsFullForWorkResource { get; }
    public bool CanCarryWorkResource { get; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        missingRuntimeWarnings.Clear();
    }

    private struct ComponentReferences
    {
        public Character Character;
        public AgentWorkProfile WorkProfile;
        public NpcHomeAssignment HomeAssignment;
        public HomeTravelExecutor HomeTravel;
        public SleepExecutor Sleep;
        public NpcResourceReservation ResourceReservation;
        public GatherEquipmentExecutor GatherEquipment;
        public GatherResourceExecutor GatherResource;
        public ResourceCollectionExecutor ResourceCollection;
        public ResourceDepositExecutor ResourceDeposit;
        public NpcTaskController TaskController;
    }

    public AgentContext(Character character)
        : this(ResolveComponents(character))
    {
    }

    public AgentContext(NpcAgentRuntime runtime)
        : this(ResolveComponents(runtime))
    {
    }

    private AgentContext(ComponentReferences components)
    {
        SimulationPerformanceCounters.AddAgentContextBuild();

        Character character = components.Character;
        AgentWorkProfile workProfile = components.WorkProfile;
        NpcHomeAssignment homeAssignment = components.HomeAssignment;
        HomeTravelExecutor homeTravel = components.HomeTravel;
        SleepExecutor sleep = components.Sleep;
        NpcResourceReservation resourceReservation = components.ResourceReservation;
        GatherEquipmentExecutor gatherEquipment = components.GatherEquipment;
        GatherResourceExecutor gatherResource = components.GatherResource;
        ResourceCollectionExecutor resourceCollection = components.ResourceCollection;
        ResourceDepositExecutor resourceDeposit = components.ResourceDeposit;
        NpcTaskController taskController = components.TaskController;

        CharacterName = character != null ? character.gameObject.name : "None";
        CharacterPosition = character != null ? character.transform.position : Vector3.zero;
        CarriedWood = character != null ? character.CarriedWood : 0;
        CarriedBerries = character != null ? character.CarriedBerries : 0;
        HasCarriedResources = character != null && character.HasCarriedResources;
        HasGatherEquipment = character != null && character.HasGatherEquipment;
        HasDepositLoad = character != null && character.HasDepositLoad;
        ControlMode = CharacterControlRegistry.GetControlMode(character);

        WorkResourceType = workProfile != null ? workProfile.GatherResourceType : ResourceNode.ResourceType.Wood;
        RepeatGatherTask = workProfile == null || workProfile.RepeatGatherTask;
        WorkStartHour = workProfile != null ? workProfile.WorkStartHour : AgentScheduleConfig.DefaultWorkStartHour;
        WorkEndHour = workProfile != null ? workProfile.WorkEndHour : AgentScheduleConfig.DefaultWorkEndHour;
        SleepStartHour = workProfile != null ? workProfile.SleepStartHour : AgentScheduleConfig.DefaultSleepStartHour;
        SleepEndHour = workProfile != null ? workProfile.SleepEndHour : AgentScheduleConfig.DefaultSleepEndHour;
        IsFullForWorkResource = character != null && character.IsFullForResource(WorkResourceType);
        CanCarryWorkResource = character != null && character.CanCarryResourceType(WorkResourceType);

        HasHome = homeAssignment != null && homeAssignment.HasHome;
        IsAtHome = homeAssignment != null && homeAssignment.IsAtHome;
        Vector3 resolvedHomePosition = CharacterPosition;
        HasHomePosition = homeAssignment != null && homeAssignment.TryGetHomePosition(out resolvedHomePosition);
        HomePosition = resolvedHomePosition;
        HomeTravelState = homeTravel != null ? homeTravel.State : HomeTravelState.Idle;
        HasBarnShelter = homeTravel != null && homeTravel.HasActiveBarnShelter;
        IsAtBarnShelter = homeTravel != null && homeTravel.IsAtBarnShelter;
        IsSleeping = sleep != null && sleep.IsSleeping;
        SleepState = sleep != null ? sleep.State : SleepExecutionState.Idle;
        HasCompletedRequiredSleep = sleep != null && sleep.HasCompletedRequiredSleep;
        SleepElapsedWorldHours = sleep != null ? sleep.SleepElapsedWorldHours : 0f;
        SleepRemainingWorldHours = sleep != null ? sleep.SleepRemainingWorldHours : 0f;

        HasReservedResource = resourceReservation != null && resourceReservation.HasReservedResource;
        Vector3 resolvedResourcePosition = CharacterPosition;
        HasReservedResourcePosition = resourceReservation != null && resourceReservation.TryGetReservedResourcePosition(out resolvedResourcePosition);
        ReservedResourcePosition = resolvedResourcePosition;

        GatherEquipmentState = gatherEquipment != null ? gatherEquipment.State : GatherEquipmentState.Idle;
        GatherResourceState = gatherResource != null ? gatherResource.State : GatherResourceState.Idle;
        ResourceCollectionState = resourceCollection != null ? resourceCollection.State : ResourceCollectionState.Idle;
        ResourceDepositState = resourceDeposit != null ? resourceDeposit.State : ResourceDepositState.Idle;

        CurrentTask = taskController != null ? taskController.CurrentTask : AgentTaskType.None;
        TaskState = taskController != null ? taskController.TaskState : AgentTaskState.Completed;
        IsBusy = taskController != null && taskController.IsBusy;
    }

    private static ComponentReferences ResolveComponents(NpcAgentRuntime runtime)
    {
        if (runtime == null)
        {
            SimulationPerformanceCounters.AddAgentContextComponentCacheMiss();
            return default(ComponentReferences);
        }

        runtime.EnsureInitialized();
        SimulationPerformanceCounters.AddAgentContextComponentCacheHit();
        return new ComponentReferences
        {
            Character = runtime.Character,
            WorkProfile = runtime.WorkProfile,
            HomeAssignment = runtime.HomeAssignment,
            HomeTravel = runtime.HomeTravelExecutor,
            Sleep = runtime.SleepExecutor,
            ResourceReservation = runtime.ResourceReservation,
            GatherEquipment = runtime.GatherEquipmentExecutor,
            GatherResource = runtime.GatherResourceExecutor,
            ResourceCollection = runtime.ResourceCollectionExecutor,
            ResourceDeposit = runtime.ResourceDepositExecutor,
            TaskController = runtime.TaskController
        };
    }

    private static ComponentReferences ResolveComponents(Character character)
    {
        if (character == null)
        {
            SimulationPerformanceCounters.AddAgentContextComponentCacheMiss();
            return default(ComponentReferences);
        }

        SimulationPerformanceCounters.AddAgentContextFallbackLookup();
        NpcAgentRuntime runtime = character.GetComponent<NpcAgentRuntime>();
        if (runtime != null)
            return ResolveComponents(runtime);

        WarnMissingRuntimeOnce(character);
        SimulationPerformanceCounters.AddAgentContextComponentCacheMiss();
        return ResolveComponentsWithoutRuntime(character);
    }

    private static ComponentReferences ResolveComponentsWithoutRuntime(Character character)
    {
        SimulationPerformanceCounters.AddAgentContextFallbackLookup();
        AgentWorkProfile workProfile = character.GetComponent<AgentWorkProfile>();
        SimulationPerformanceCounters.AddAgentContextFallbackLookup();
        NpcHomeAssignment homeAssignment = character.GetComponent<NpcHomeAssignment>();
        SimulationPerformanceCounters.AddAgentContextFallbackLookup();
        HomeTravelExecutor homeTravel = character.GetComponent<HomeTravelExecutor>();
        SimulationPerformanceCounters.AddAgentContextFallbackLookup();
        SleepExecutor sleep = character.GetComponent<SleepExecutor>();
        SimulationPerformanceCounters.AddAgentContextFallbackLookup();
        NpcResourceReservation resourceReservation = character.GetComponent<NpcResourceReservation>();
        SimulationPerformanceCounters.AddAgentContextFallbackLookup();
        GatherEquipmentExecutor gatherEquipment = character.GetComponent<GatherEquipmentExecutor>();
        SimulationPerformanceCounters.AddAgentContextFallbackLookup();
        GatherResourceExecutor gatherResource = character.GetComponent<GatherResourceExecutor>();
        SimulationPerformanceCounters.AddAgentContextFallbackLookup();
        ResourceCollectionExecutor resourceCollection = character.GetComponent<ResourceCollectionExecutor>();
        SimulationPerformanceCounters.AddAgentContextFallbackLookup();
        ResourceDepositExecutor resourceDeposit = character.GetComponent<ResourceDepositExecutor>();
        SimulationPerformanceCounters.AddAgentContextFallbackLookup();
        NpcTaskController taskController = character.GetComponent<NpcTaskController>();

        return new ComponentReferences
        {
            Character = character,
            WorkProfile = workProfile,
            HomeAssignment = homeAssignment,
            HomeTravel = homeTravel,
            Sleep = sleep,
            ResourceReservation = resourceReservation,
            GatherEquipment = gatherEquipment,
            GatherResource = gatherResource,
            ResourceCollection = resourceCollection,
            ResourceDeposit = resourceDeposit,
            TaskController = taskController
        };
    }

    private static void WarnMissingRuntimeOnce(Character character)
    {
        int instanceId = character.GetInstanceID();
        if (!missingRuntimeWarnings.Add(instanceId))
            return;

        Debug.LogWarning(
            $"AgentContext is using fallback component lookups because NpcAgentRuntime is missing. GameObject: {character.gameObject.name}."
        );
    }
}
