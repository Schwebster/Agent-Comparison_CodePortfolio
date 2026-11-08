/*
===============================================================================
Script:
DummyWaypointAgent

Purpose:
Moves a character between two waypoints for autonomous-control handoff testing.

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
- ICharacterActions
- CharacterActions

Automatically Resolved:
- Character on the same GameObject

Inspector References:
- Waypoint A
- Waypoint B
===============================================================================
*/

using UnityEngine;

public class DummyWaypointAgent : MonoBehaviour
{
    [SerializeField] private Transform waypointA;
    [SerializeField] private Transform waypointB;
    [SerializeField] private float commandInterval = 3f;

    private Character character;
    private ICharacterActions characterActions;
    private bool movingToA = true;
    private float commandTimer;

    private void Awake()
    {
        character = GetComponent<Character>();
        characterActions = new CharacterActions(character);

        Debug.Assert(
            character != null,
            $"Missing component: Character. Expected location: same GameObject as DummyWaypointAgent. Script: {nameof(DummyWaypointAgent)}. GameObject: {gameObject.name}."
        );
    }

    private void OnDisable()
    {
        CharacterControlRegistry.ReleaseAutonomousControl(character);
    }

    private void Update()
    {
        if (character == null || waypointA == null || waypointB == null)
            return;

        if (CharacterControlRegistry.IsPlayerControlled(character))
            return;

        if (!CharacterControlRegistry.TryTakeAutonomousControl(character))
            return;

        commandTimer -= Time.deltaTime;

        if (commandTimer > 0f)
            return;

        Transform targetWaypoint = movingToA ? waypointA : waypointB;
        characterActions.MoveTo(targetWaypoint.position);

        movingToA = !movingToA;
        commandTimer = commandInterval;
    }
}
