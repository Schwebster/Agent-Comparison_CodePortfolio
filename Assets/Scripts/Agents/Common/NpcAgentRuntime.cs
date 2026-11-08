/*
===============================================================================
Script:
NpcAgentRuntime

Purpose:
Owns shared agent runtime component wiring for an NPC.

Folder:
Assets/Scripts/Agents/Common/

Scene Hierarchy:
_World
- Characters
  - Character

Attach To:
NPC character root

Required Children:
None

Dependencies:
- Character
- CharacterIdentity
- NpcHomeAssignment
- HomeTravelExecutor
- SleepExecutor
- NpcSleepHider
- NpcResourceReservation
- GatherEquipmentExecutor
- GatherResourceExecutor
- ExploreFogExecutor
- ResourceCollectionExecutor
- ResourceDepositExecutor
- NpcTaskController
- AgentWorkProfile

Automatically Resolved:
- Required shared runtime components on this GameObject

Inspector References:
None
===============================================================================
*/

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(CharacterIdentity))]
[RequireComponent(typeof(NpcHomeAssignment))]
[RequireComponent(typeof(HomeTravelExecutor))]
[RequireComponent(typeof(SleepExecutor))]
[RequireComponent(typeof(NpcSleepHider))]
[RequireComponent(typeof(NpcResourceReservation))]
[RequireComponent(typeof(GatherEquipmentExecutor))]
[RequireComponent(typeof(GatherResourceExecutor))]
[RequireComponent(typeof(ExploreFogExecutor))]
[RequireComponent(typeof(ResourceCollectionExecutor))]
[RequireComponent(typeof(ResourceDepositExecutor))]
[RequireComponent(typeof(NpcTaskController))]
[RequireComponent(typeof(AgentWorkProfile))]
public class NpcAgentRuntime : MonoBehaviour
{
    private bool initialized;

    public Character Character { get; private set; }
    public CharacterIdentity Identity { get; private set; }
    public NpcHomeAssignment HomeAssignment { get; private set; }
    public HomeTravelExecutor HomeTravelExecutor { get; private set; }
    public SleepExecutor SleepExecutor { get; private set; }
    public NpcSleepHider SleepHider { get; private set; }
    public NpcResourceReservation ResourceReservation { get; private set; }
    public GatherEquipmentExecutor GatherEquipmentExecutor { get; private set; }
    public GatherResourceExecutor GatherResourceExecutor { get; private set; }
    public ExploreFogExecutor ExploreFogExecutor { get; private set; }
    public ResourceCollectionExecutor ResourceCollectionExecutor { get; private set; }
    public ResourceDepositExecutor ResourceDepositExecutor { get; private set; }
    public NpcTaskController TaskController { get; private set; }
    public AgentWorkProfile WorkProfile { get; private set; }

    private void Awake()
    {
        EnsureInitialized();
    }

    public void EnsureInitialized()
    {
        if (initialized)
            return;

        Character = EnsureComponent<Character>();
        Identity = EnsureComponent<CharacterIdentity>();
        Identity.EnsureAssigned();
        HomeAssignment = EnsureComponent<NpcHomeAssignment>();
        HomeTravelExecutor = EnsureComponent<HomeTravelExecutor>();
        SleepHider = EnsureComponent<NpcSleepHider>();
        SleepExecutor = EnsureComponent<SleepExecutor>();
        ResourceReservation = EnsureComponent<NpcResourceReservation>();
        GatherEquipmentExecutor = EnsureComponent<GatherEquipmentExecutor>();
        GatherResourceExecutor = EnsureComponent<GatherResourceExecutor>();
        ExploreFogExecutor = EnsureComponent<ExploreFogExecutor>();
        ResourceCollectionExecutor = EnsureComponent<ResourceCollectionExecutor>();
        ResourceDepositExecutor = EnsureComponent<ResourceDepositExecutor>();
        TaskController = EnsureComponent<NpcTaskController>();
        WorkProfile = EnsureComponent<AgentWorkProfile>();
        initialized = true;
    }

    private T EnsureComponent<T>() where T : Component
    {
        T component = GetComponent<T>();

        if (component != null)
            return component;

        return gameObject.AddComponent<T>();
    }
}
