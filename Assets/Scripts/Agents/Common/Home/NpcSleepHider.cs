/*
===============================================================================
Script:
NpcSleepHider

Purpose:
Hides an NPC's visible and physical scene presence while the shared sleep executor is active.

Folder:
Assets/Scripts/Agents/Common/Home/

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
- NavMeshAgent

Automatically Resolved:
- Renderers in this GameObject's children
- Colliders in this GameObject's children
- NavMeshAgent on the same GameObject

Inspector References:
- hideRenderers
- disableColliders
- disableNavMeshAgent
===============================================================================
*/

using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(NavMeshAgent))]
public class NpcSleepHider : MonoBehaviour
{
    private const float NavMeshRecoverySampleDistance = 1.5f;

    [SerializeField] private bool hideRenderers = true;
    [SerializeField] private bool disableColliders = true;
    [SerializeField] private bool disableNavMeshAgent = true;

    private Renderer[] renderers = System.Array.Empty<Renderer>();
    private Collider[] colliders = System.Array.Empty<Collider>();
    private bool[] rendererEnabledStates = System.Array.Empty<bool>();
    private bool[] colliderEnabledStates = System.Array.Empty<bool>();
    private NavMeshAgent navMeshAgent;
    private bool navMeshAgentEnabled;
    private bool isHidden;
    private bool hasSafeNavMeshPosition;
    private Vector3 safeNavMeshPosition;
    private int safeNavMeshAreaMask = NavMesh.AllAreas;
    private int safeNavMeshAgentTypeId;

    public bool IsHidden => isHidden;

    private void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        CacheComponents();
    }

    private void OnDisable()
    {
        Show();
    }

    public bool Hide()
    {
        if (isHidden)
            return true;

        CaptureCurrentState();

        if (disableNavMeshAgent &&
            navMeshAgentEnabled &&
            !TryCacheSafeNavMeshPosition())
        {
            LogNavMeshRecoveryWarning("Sleep hide was rejected because no valid pre-sleep NavMesh position was available.");
            return false;
        }

        if (hideRenderers)
            SetRenderersEnabled(false);

        if (disableColliders)
            SetCollidersEnabled(false);

        if (disableNavMeshAgent && navMeshAgent != null)
            navMeshAgent.enabled = false;

        isHidden = true;
        return true;
    }

    public bool Show()
    {
        if (!isHidden)
            return true;

        if (!gameObject.activeInHierarchy)
        {
            if (disableNavMeshAgent && navMeshAgent != null)
            {
                if (hasSafeNavMeshPosition)
                    transform.position = safeNavMeshPosition;

                navMeshAgent.enabled = navMeshAgentEnabled;
            }

            RestoreColliders();
            RestoreRenderers();
            isHidden = false;
            return true;
        }

        SimulationPerformanceCounters.AddWakeRestoration();
        bool navMeshRestored = RestoreNavMeshAgent();

        RestoreColliders();
        RestoreRenderers();

        isHidden = false;
        return navMeshRestored;
    }

    private void CacheComponents()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        rendererEnabledStates = new bool[renderers.Length];
        colliderEnabledStates = new bool[colliders.Length];
    }

    private void CaptureCurrentState()
    {
        navMeshAgentEnabled = navMeshAgent != null && navMeshAgent.enabled;

        for (int i = 0; i < renderers.Length; i++)
            rendererEnabledStates[i] = renderers[i] != null && renderers[i].enabled;

        for (int i = 0; i < colliders.Length; i++)
            colliderEnabledStates[i] = colliders[i] != null && colliders[i].enabled;
    }

    private bool TryCacheSafeNavMeshPosition()
    {
        hasSafeNavMeshPosition = false;

        if (navMeshAgent == null ||
            !navMeshAgent.enabled ||
            !navMeshAgent.isOnNavMesh)
        {
            return false;
        }

        safeNavMeshAreaMask = navMeshAgent.areaMask;
        safeNavMeshAgentTypeId = navMeshAgent.agentTypeID;
        NavMeshQueryFilter queryFilter = new NavMeshQueryFilter
        {
            agentTypeID = safeNavMeshAgentTypeId,
            areaMask = safeNavMeshAreaMask
        };
        Vector3 candidate = navMeshAgent.nextPosition;
        if (!NavMesh.SamplePosition(
                candidate,
                out NavMeshHit hit,
                NavMeshRecoverySampleDistance,
                queryFilter))
        {
            return false;
        }

        safeNavMeshPosition = hit.position;
        hasSafeNavMeshPosition = true;
        return true;
    }

    private bool RestoreNavMeshAgent()
    {
        if (!disableNavMeshAgent || navMeshAgent == null)
            return true;

        if (!navMeshAgentEnabled)
        {
            navMeshAgent.enabled = false;
            return true;
        }

        int areaMask = hasSafeNavMeshPosition ? safeNavMeshAreaMask : navMeshAgent.areaMask;
        int agentTypeId = hasSafeNavMeshPosition ? safeNavMeshAgentTypeId : navMeshAgent.agentTypeID;
        NavMeshQueryFilter queryFilter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeId,
            areaMask = areaMask
        };
        bool currentPositionIsValid = NavMesh.SamplePosition(
            transform.position,
            out NavMeshHit currentHit,
            NavMeshRecoverySampleDistance,
            queryFilter
        );

        if (!currentPositionIsValid)
            SimulationPerformanceCounters.AddOffNavMeshWakeDetection();

        Vector3 recoveryPosition = hasSafeNavMeshPosition
            ? safeNavMeshPosition
            : currentHit.position;

        if (!hasSafeNavMeshPosition && !currentPositionIsValid)
        {
            navMeshAgent.enabled = true;
            SimulationPerformanceCounters.AddNavMeshRecoveryFailure();
            LogNavMeshRecoveryWarning("Wake-up NavMesh recovery failed because neither the cached nor current position was valid.");
            return false;
        }

        navMeshAgent.enabled = false;
        transform.position = recoveryPosition;
        navMeshAgent.enabled = true;

        if (!navMeshAgent.isOnNavMesh ||
            !navMeshAgent.Warp(recoveryPosition) ||
            !navMeshAgent.isOnNavMesh)
        {
            SimulationPerformanceCounters.AddOffNavMeshWakeDetection();
            SimulationPerformanceCounters.AddNavMeshRecoveryFailure();
            LogNavMeshRecoveryWarning("Wake-up NavMesh recovery could not bind the agent to the cached NavMesh position.");
            return false;
        }

        navMeshAgent.ResetPath();
        SimulationPerformanceCounters.AddNavMeshRecoverySuccess();
        return true;
    }

    private void LogNavMeshRecoveryWarning(string reason)
    {
        CharacterIdentity identity = GetComponent<CharacterIdentity>();
        string identityText = identity != null
            ? $"{identity.FullName} (ID {identity.CharacterId})"
            : gameObject.name;
        string agentText = navMeshAgent != null
            ? $"enabled={navMeshAgent.enabled}, isOnNavMesh={navMeshAgent.isOnNavMesh}, areaMask={navMeshAgent.areaMask}"
            : "agent=missing";

        Debug.LogWarning(
            $"{reason} NPC={identityText}, currentPosition={transform.position}, " +
            $"hasCachedPosition={hasSafeNavMeshPosition}, cachedPosition={safeNavMeshPosition}, {agentText}."
        );
    }

    private void SetRenderersEnabled(bool enabled)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = enabled;
        }
    }

    private void SetCollidersEnabled(bool enabled)
    {
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = enabled;
        }
    }

    private void RestoreRenderers()
    {
        int count = Mathf.Min(renderers.Length, rendererEnabledStates.Length);

        for (int i = 0; i < count; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = rendererEnabledStates[i];
        }
    }

    private void RestoreColliders()
    {
        int count = Mathf.Min(colliders.Length, colliderEnabledStates.Length);

        for (int i = 0; i < count; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = colliderEnabledStates[i];
        }
    }
}
