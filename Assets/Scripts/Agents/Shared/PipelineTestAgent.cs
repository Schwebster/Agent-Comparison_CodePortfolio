/*
===============================================================================
Script:
PipelineTestAgent

Purpose:
Tests the shared agent pipeline by moving a character between two waypoints.

Folder:
Assets/Scripts/Agents/Shared/

Scene Hierarchy:
_World
- Characters
  - Character

Attach To:
Character

Required Children:
None

Dependencies:
- Character
- CharacterControlRegistry
- AgentContext
- IAgentController
- AgentDecision
- DecisionLogger
- AgentDecisionExecutor

Automatically Resolved:
- Character on the same GameObject
- AgentDecisionExecutor on the same GameObject

Inspector References:
- waypointA
- waypointB
- decisionInterval
===============================================================================
*/

using UnityEngine;

[RequireComponent(typeof(AgentDecisionExecutor))]
public class PipelineTestAgent : MonoBehaviour, IAgentController
{
    [SerializeField] private Transform waypointA;
    [SerializeField] private Transform waypointB;
    [SerializeField] private float decisionInterval = 3f;

    private Character character;
    private AgentDecisionExecutor decisionExecutor;
    private bool movingToA = true;
    private float decisionTimer;

    public string AgentName => nameof(PipelineTestAgent);

    private void Awake()
    {
        character = GetComponent<Character>();
        decisionExecutor = GetComponent<AgentDecisionExecutor>();

        Debug.Assert(
            character != null,
            $"Missing component: Character. Expected location: same GameObject as PipelineTestAgent. Script: {nameof(PipelineTestAgent)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            decisionExecutor != null,
            $"Missing component: AgentDecisionExecutor. Expected location: same GameObject as PipelineTestAgent. Script: {nameof(PipelineTestAgent)}. GameObject: {gameObject.name}."
        );
    }

    private void OnDisable()
    {
        CharacterControlRegistry.ReleaseAutonomousControl(character);
    }

    private void Update()
    {
        if (character == null || decisionExecutor == null)
            return;

        if (waypointA == null || waypointB == null)
        {
            Debug.LogWarning($"PipelineTestAgent requires waypointA and waypointB. GameObject: {gameObject.name}.");
            return;
        }

        if (CharacterControlRegistry.IsPlayerControlled(character))
            return;

        if (!CharacterControlRegistry.TryTakeAutonomousControl(character))
            return;

        decisionTimer -= Time.deltaTime;

        if (decisionTimer > 0f)
            return;

        AgentContext context = new AgentContext(character);
        AgentDecision decision = Decide(context);

        DecisionLogger.LogDecision(this, context, decision);
        decisionExecutor.Execute(decision);

        decisionTimer = Mathf.Max(0.1f, decisionInterval);
    }

    public AgentDecision Decide(AgentContext context)
    {
        Transform targetWaypoint = movingToA ? waypointA : waypointB;
        movingToA = !movingToA;

        return AgentDecision.MoveTo(targetWaypoint.position, "Pipeline test waypoint patrol.");
    }
}
