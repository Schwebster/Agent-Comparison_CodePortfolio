/*
===============================================================================
Script:
NpcCollisionSettingsApplier

Purpose:
Applies the shared NPC collision strategy setting to NPC NavMeshAgents.

Folder:
Assets/Scripts/Agents/Common/

Scene Hierarchy:
None

Attach To:
None

Dependencies:
- ManualDayPlanState
- CharacterIdentity
- NavMeshAgent

Automatically Resolved:
- Active NPC NavMeshAgents through CharacterIdentity

Inspector References:
None
===============================================================================
*/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public static class NpcCollisionSettingsApplier
{
    private static readonly Dictionary<int, ObstacleAvoidanceType> originalAvoidanceTypes = new Dictionary<int, ObstacleAvoidanceType>();
    private static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        ManualDayPlanState.StrategyFlagsChanged -= ApplyCurrentSettingToAllNpcs;
        initialized = false;
        originalAvoidanceTypes.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyAfterSceneLoad()
    {
        EnsureInitialized();
        ApplyCurrentSettingToAllNpcs();
    }

    public static void ApplyToAgent(NavMeshAgent agent)
    {
        EnsureInitialized();

        if (agent == null)
            return;

        int instanceId = agent.GetInstanceID();
        if (!originalAvoidanceTypes.ContainsKey(instanceId))
            originalAvoidanceTypes.Add(instanceId, agent.obstacleAvoidanceType);

        agent.obstacleAvoidanceType = ManualDayPlanState.NpcCollisionActive
            ? originalAvoidanceTypes[instanceId]
            : ObstacleAvoidanceType.NoObstacleAvoidance;
    }

    public static void ApplyCurrentSettingToAllNpcs()
    {
        EnsureInitialized();

        CharacterIdentity[] identities = Object.FindObjectsByType<CharacterIdentity>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        for (int i = 0; i < identities.Length; i++)
        {
            CharacterIdentity identity = identities[i];
            if (identity == null)
                continue;

            ApplyToAgent(identity.GetComponent<NavMeshAgent>());
        }
    }

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        ManualDayPlanState.StrategyFlagsChanged -= ApplyCurrentSettingToAllNpcs;
        ManualDayPlanState.StrategyFlagsChanged += ApplyCurrentSettingToAllNpcs;
        initialized = true;
    }
}
