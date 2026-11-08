/*
===============================================================================
Script:
ResourceCollectionExecutor

Purpose:
Executes collection of an already reserved resource through shared character actions.

Folder:
Assets/Scripts/Agents/Common/Resources/

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
- NpcResourceReservation
- ResourceNode

Automatically Resolved:
- Character on the same GameObject
- NpcResourceReservation on the same GameObject

Inspector References:
None
===============================================================================
*/

using UnityEngine;

public enum ResourceCollectionState
{
    Idle,
    Moving,
    Collecting,
    Completed,
    Failed
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(NpcResourceReservation))]
public class ResourceCollectionExecutor : MonoBehaviour
{
    private const float MovementStuckWorldHours = 0.5f;

    private Character character;
    private NpcResourceReservation resourceReservation;
    private ResourceNode activeResource;
    private Vector3 activeInteractionPosition;

    public ResourceCollectionState State { get; private set; } = ResourceCollectionState.Idle;
    public ResourceNode ActiveResource => activeResource;
    public bool IsRunning => State == ResourceCollectionState.Moving || State == ResourceCollectionState.Collecting;
    public bool IsCompleted => State == ResourceCollectionState.Completed;
    public bool HasFailed => State == ResourceCollectionState.Failed;

    private void Awake()
    {
        character = GetComponent<Character>();
        resourceReservation = GetComponent<NpcResourceReservation>();

        Debug.Assert(
            character != null,
            $"Missing component: Character. Expected location: same GameObject as ResourceCollectionExecutor. Script: {nameof(ResourceCollectionExecutor)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            resourceReservation != null,
            $"Missing component: NpcResourceReservation. Expected location: same GameObject as ResourceCollectionExecutor. Script: {nameof(ResourceCollectionExecutor)}. GameObject: {gameObject.name}."
        );
    }

    private void OnDisable()
    {
        if (IsRunning)
            FailAndRelease("Resource collection aborted because executor was disabled.");
    }

    private void Update()
    {
        switch (State)
        {
            case ResourceCollectionState.Moving:
                UpdateMoving();
                break;
            case ResourceCollectionState.Collecting:
                UpdateCollecting();
                break;
        }
    }

    public bool ExecuteCollectResource()
    {
        return StartResourceCollection();
    }

    public void CancelCollection()
    {
        if (resourceReservation != null)
            resourceReservation.ReleaseResource();

        if (character != null)
            character.StopMovement();

        activeResource = null;
        activeInteractionPosition = transform.position;
        State = ResourceCollectionState.Idle;
    }

    public bool StartResourceCollection()
    {
        if (IsRunning)
            return false;

        if (resourceReservation == null || !resourceReservation.HasReservedResource)
        {
            FailWithoutReservation("Resource collection failed: no resource is reserved.");
            return false;
        }

        activeResource = resourceReservation.ReservedResource;
        if (!resourceReservation.TryGetReservedResourcePosition(out activeInteractionPosition))
        {
            FailAndRelease("Resource collection failed: reserved resource has no valid interaction position.");
            return false;
        }

        if (!IsActiveResourceValid())
        {
            FailAndRelease("Resource collection failed: reserved resource is no longer valid.");
            return false;
        }

        if (!character.TryMoveTo(activeInteractionPosition))
        {
            FailAndReleaseAndMarkResourceUnreachable("Resource collection failed: movement could not start.");
            return false;
        }

        State = ResourceCollectionState.Moving;
        return true;
    }

    public void ResetState()
    {
        if (IsRunning)
            return;

        activeResource = null;
        activeInteractionPosition = transform.position;
        State = ResourceCollectionState.Idle;
    }

    private void UpdateMoving()
    {
        if (!ValidateReservedResource())
            return;

        if (character.HasReachedInteractionTarget(activeInteractionPosition))
        {
            if (!character.TryHarvest(activeResource, activeInteractionPosition))
            {
                FailAndRelease("Resource collection failed: harvest action could not start.");
                return;
            }

            State = ResourceCollectionState.Collecting;
            return;
        }

        if (character.HasMovementPathFailed())
        {
            FailAndReleaseAndMarkResourceUnreachable("Resource collection failed: movement path is invalid.");
            return;
        }

        if (character.HasMovementStalled(MovementStuckWorldHours))
        {
            FailAndReleaseAndMarkResourceUnreachable(
                "Resource collection failed: movement stalled before reaching the resource."
            );
            return;
        }
    }

    private void UpdateCollecting()
    {
        if (activeResource == null || !activeResource.isActiveAndEnabled)
        {
            FailAndRelease("Resource collection failed: reserved resource disappeared.");
            return;
        }

        if (character.IsWorkingOnResource(activeResource))
            return;

        if (character.HasMovementPathFailed())
        {
            FailAndReleaseAndMarkResourceUnreachable("Resource collection failed: movement path became invalid during collection.");
            return;
        }

        CompleteAndRelease();
    }

    private bool ValidateReservedResource()
    {
        if (!IsActiveResourceValid())
        {
            FailAndRelease("Resource collection failed: reserved resource is no longer valid.");
            return false;
        }

        if (resourceReservation.ReservedResource != activeResource)
        {
            FailAndRelease("Resource collection failed: reserved resource changed before collection finished.");
            return false;
        }

        if (!resourceReservation.TryGetReservedResourcePosition(out activeInteractionPosition))
        {
            FailAndRelease("Resource collection failed: reserved resource interaction position became invalid.");
            return false;
        }

        return true;
    }

    private bool IsActiveResourceValid()
    {
        return activeResource != null &&
            activeResource.isActiveAndEnabled &&
            activeResource.IsAvailable &&
            resourceReservation != null &&
            resourceReservation.HasReservedResource;
    }

    private void CompleteAndRelease()
    {
        resourceReservation.ReleaseResource();
        activeResource = null;
        activeInteractionPosition = transform.position;
        State = ResourceCollectionState.Completed;
    }

    private void FailAndRelease(string message)
    {
        Debug.LogWarning($"{message} GameObject: {gameObject.name}.");

        if (resourceReservation != null)
            resourceReservation.ReleaseResource();

        if (character != null)
            character.StopMovement();

        activeResource = null;
        activeInteractionPosition = transform.position;
        State = ResourceCollectionState.Failed;
    }

    private void FailAndReleaseAndMarkResourceUnreachable(string message)
    {
        ResourceRegistry.MarkResourceTemporarilyUnreachable(activeResource, transform.position, message);
        FailAndRelease(message);
    }

    private void FailWithoutReservation(string message)
    {
        Debug.LogWarning($"{message} GameObject: {gameObject.name}.");

        if (resourceReservation != null)
            resourceReservation.ReleaseResource();

        activeResource = null;
        activeInteractionPosition = transform.position;
        State = ResourceCollectionState.Failed;
    }
}
