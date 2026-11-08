/*
===============================================================================
Script:
GatherEquipmentExecutor

Purpose:
Executes pickup/switching of visible gathering equipment at the barn.

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
- ResourceStorageRegistry

Automatically Resolved:
- Character on the same GameObject

Inspector References:
None
===============================================================================
*/

using UnityEngine;
using UnityEngine.AI;

public enum GatherEquipmentState
{
    Idle,
    MovingToBarn,
    Equipping,
    Completed,
    Failed
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
public class GatherEquipmentExecutor : MonoBehaviour
{
    private const float MovementStuckWorldHours = 0.5f;

    private Character character;
    private CharacterIdentity identity;
    private ResourceNode.ResourceType requestedResourceType;
    private ResourceStorage equipmentStorage;
    private Vector3 equipmentInteractionPosition;

    public GatherEquipmentState State { get; private set; } = GatherEquipmentState.Idle;
    public bool IsRunning => State == GatherEquipmentState.MovingToBarn || State == GatherEquipmentState.Equipping;
    public bool IsCompleted => State == GatherEquipmentState.Completed;
    public bool HasFailed => State == GatherEquipmentState.Failed;
    public ResourceStorageLookupResult LastStorageLookupResult { get; private set; } =
        ResourceStorageLookupResult.Found;

    private void Awake()
    {
        character = GetComponent<Character>();
        identity = GetComponent<CharacterIdentity>();

        Debug.Assert(
            character != null,
            $"Missing component: Character. Expected location: same GameObject as GatherEquipmentExecutor. Script: {nameof(GatherEquipmentExecutor)}. GameObject: {gameObject.name}."
        );
    }

    private void OnDisable()
    {
        if (IsRunning)
            CancelEquipmentPickup();
    }

    private void Update()
    {
        if (State == GatherEquipmentState.MovingToBarn)
            UpdateMovingToBarn();
    }

    public bool StartEquipmentPickup(ResourceNode.ResourceType resourceType)
    {
        if (IsRunning)
            return false;

        LastStorageLookupResult = ResourceStorageLookupResult.Found;
        requestedResourceType = resourceType;

        if (character.HasCorrectGatherEquipment(resourceType))
        {
            State = GatherEquipmentState.Completed;
            return true;
        }

        if (character.HasCarriedResources)
        {
            Fail("Gather equipment pickup failed: NPC is carrying resources.");
            return false;
        }

        LastStorageLookupResult = ResourceStorageRegistry.GetClosestStorage(
            transform.position,
            GetRequesterId(),
            out equipmentStorage);

        if (LastStorageLookupResult == ResourceStorageLookupResult.TemporarilyUnreachable)
        {
            FailWithoutWarning();
            return false;
        }

        if (LastStorageLookupResult == ResourceStorageLookupResult.NoActiveStorage)
        {
            Fail("Gather equipment pickup failed: no active barn/equipment target exists.");
            return false;
        }

        equipmentInteractionPosition = GetBarnTravelTarget(equipmentStorage);

        if (!character.TryMoveTo(equipmentInteractionPosition))
        {
            Fail("Gather equipment pickup failed: movement to barn could not start.");
            return false;
        }

        State = GatherEquipmentState.MovingToBarn;
        return true;
    }

    public void CancelEquipmentPickup()
    {
        if (character != null)
            character.StopMovement();

        equipmentStorage = null;
        equipmentInteractionPosition = Vector3.zero;
        State = GatherEquipmentState.Idle;
        LastStorageLookupResult = ResourceStorageLookupResult.Found;
    }

    private void UpdateMovingToBarn()
    {
        if (equipmentStorage == null || !equipmentStorage.isActiveAndEnabled)
        {
            Fail("Gather equipment pickup failed: barn/equipment target became invalid.");
            return;
        }

        if (equipmentStorage.IsInInteractionRange(transform.position))
        {
            CompleteEquipmentPickup();
            return;
        }

        if (character.HasMovementPathInvalid())
        {
            Fail("Gather equipment pickup failed: movement path is invalid.");
            return;
        }

        if (!character.HasMovementStalled(MovementStuckWorldHours))
            return;

        Fail("Gather equipment pickup failed: movement stalled before reaching the barn.");
    }

    private Vector3 GetBarnTravelTarget(ResourceStorage storage)
    {
        if (storage == null)
            return transform.position;

        Vector3 depositPosition = storage.GetDepositPosition();
        if (NavMesh.SamplePosition(
            depositPosition,
            out NavMeshHit hit,
            0.5f,
            NavMesh.AllAreas))
        {
            return hit.position;
        }

        return depositPosition;
    }

    private void CompleteEquipmentPickup()
    {
        State = GatherEquipmentState.Equipping;

        if (!character.TryEquipForGathering(requestedResourceType))
        {
            Fail("Gather equipment pickup failed: correct gather container could not be equipped.");
            return;
        }

        character.StopMovement();
        equipmentStorage = null;
        equipmentInteractionPosition = Vector3.zero;
        State = GatherEquipmentState.Completed;
    }

    private void Fail(string message)
    {
        Debug.LogWarning($"{message} GameObject: {gameObject.name}.");

        SetFailedState();
    }

    private void FailWithoutWarning()
    {
        SetFailedState();
    }

    private void SetFailedState()
    {
        if (character != null)
            character.StopMovement();

        equipmentStorage = null;
        equipmentInteractionPosition = Vector3.zero;
        State = GatherEquipmentState.Failed;
    }

    private int GetRequesterId()
    {
        return identity != null ? identity.CharacterId : 0;
    }
}
