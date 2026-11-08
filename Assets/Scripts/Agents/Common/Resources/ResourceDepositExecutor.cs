/*
===============================================================================
Script:
ResourceDepositExecutor

Purpose:
Executes depositing carried resources and returning gather equipment at the nearest barn/resource storage.

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
- ResourceStorage
- ResourceStorageRegistry

Automatically Resolved:
- Character on the same GameObject

Inspector References:
None
===============================================================================
*/

using UnityEngine;
using UnityEngine.AI;

public enum ResourceDepositState
{
    Idle,
    MovingToBarn,
    Depositing,
    Completed,
    Failed
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
public class ResourceDepositExecutor : MonoBehaviour
{
    private const float MovementStuckWorldHours = 0.5f;

    [SerializeField] private bool logBarnApproachStallDiagnostics;

    private Character character;
    private CharacterIdentity identity;
    private ResourceStorage activeStorage;
    private Vector3 activeInteractionPosition;
    private BarnApproachStallDiagnosticTracker barnApproachDiagnostics;

    public ResourceDepositState State { get; private set; } = ResourceDepositState.Idle;
    public bool IsRunning => State == ResourceDepositState.MovingToBarn || State == ResourceDepositState.Depositing;
    public bool IsCompleted => State == ResourceDepositState.Completed;
    public bool HasFailed => State == ResourceDepositState.Failed;
    public ResourceStorageLookupResult LastStorageLookupResult { get; private set; } =
        ResourceStorageLookupResult.Found;
    public bool FailedBecauseStorageTemporarilyUnreachable =>
        HasFailed && LastStorageLookupResult == ResourceStorageLookupResult.TemporarilyUnreachable;

    private void Awake()
    {
        character = GetComponent<Character>();
        identity = GetComponent<CharacterIdentity>();
        barnApproachDiagnostics = new BarnApproachStallDiagnosticTracker();

        Debug.Assert(
            character != null,
            $"Missing component: Character. Expected location: same GameObject as ResourceDepositExecutor. Script: {nameof(ResourceDepositExecutor)}. GameObject: {gameObject.name}."
        );
    }

    private void OnDisable()
    {
        if (IsRunning)
            CancelDeposit();
    }

    private void Update()
    {
        if (!IsRunning)
            return;

        UpdateDeposit();
    }

    public bool StartDeposit(bool forceBarnVisit = false)
    {
        if (IsRunning)
            return false;

        barnApproachDiagnostics.Reset();

        LastStorageLookupResult = ResourceStorageLookupResult.Found;

        if (!forceBarnVisit && !character.HasDepositLoad)
        {
            State = ResourceDepositState.Completed;
            return true;
        }

        LastStorageLookupResult = ResourceStorageRegistry.GetClosestStorage(
            transform.position,
            GetRequesterId(),
            out activeStorage);

        if (LastStorageLookupResult == ResourceStorageLookupResult.TemporarilyUnreachable)
        {
            FailWithoutWarning();
            return false;
        }

        if (LastStorageLookupResult == ResourceStorageLookupResult.NoActiveStorage)
        {
            Fail("Resource deposit failed: no active barn/deposit target exists.");
            return false;
        }

        activeInteractionPosition = GetBarnTravelTarget(activeStorage);

        if (!character.TryMoveTo(activeInteractionPosition))
        {
            Fail("Resource deposit failed: movement to barn could not start.");
            return false;
        }

        State = ResourceDepositState.MovingToBarn;
        return true;
    }

    public void CancelDeposit()
    {
        if (character != null)
            character.StopMovement();

        activeStorage = null;
        activeInteractionPosition = Vector3.zero;
        barnApproachDiagnostics.Reset();
        State = ResourceDepositState.Idle;
        LastStorageLookupResult = ResourceStorageLookupResult.Found;
    }

    private void UpdateDeposit()
    {
        if (activeStorage == null || !activeStorage.isActiveAndEnabled)
        {
            Fail("Resource deposit failed: barn/deposit target became invalid.");
            return;
        }

        barnApproachDiagnostics.Update(
            logBarnApproachStallDiagnostics,
            character,
            identity,
            activeStorage,
            activeInteractionPosition,
            "ResourceDeposit",
            State.ToString());

        if (activeStorage.IsInInteractionRange(transform.position))
        {
            CompleteDeposit();
            return;
        }

        if (character.HasMovementPathInvalid())
        {
            Fail("Resource deposit failed: movement path is invalid.");
            return;
        }

        if (!character.HasMovementStalled(MovementStuckWorldHours))
            return;

        barnApproachDiagnostics.LogConfirmedStall(
            logBarnApproachStallDiagnostics,
            character,
            identity,
            activeStorage,
            activeInteractionPosition,
            "ResourceDeposit",
            State.ToString(),
            "Character.HasMovementStalled returned true");

        Fail("Resource deposit failed: movement stalled before reaching the barn.");
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

    private void CompleteDeposit()
    {
        ResourceStorage storage = activeStorage;
        State = ResourceDepositState.Depositing;
        character.StopMovement();

        if (!character.TryCompleteDeposit(storage))
        {
            Fail("Resource deposit failed: deposit could not be completed.");
            return;
        }

        activeStorage = null;
        activeInteractionPosition = Vector3.zero;
        barnApproachDiagnostics.Clear();
        State = ResourceDepositState.Completed;
    }

    private void Fail(string message)
    {
        SimulationEventLogger.LogBarnApproachFailure(
            identity,
            character,
            activeStorage,
            activeInteractionPosition,
            State.ToString(),
            message);

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

        activeStorage = null;
        activeInteractionPosition = Vector3.zero;
        barnApproachDiagnostics.Reset();
        State = ResourceDepositState.Failed;
    }

    private int GetRequesterId()
    {
        return identity != null ? identity.CharacterId : 0;
    }
}
