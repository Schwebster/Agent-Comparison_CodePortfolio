/*
===============================================================================
Script:
GatherResourceExecutor

Purpose:
Executes a generic gather work chain for a requested resource type.

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
- GatherEquipmentExecutor
- NpcResourceReservation
- ResourceCollectionExecutor
- ResourceDepositExecutor

Automatically Resolved:
- Required components on the same GameObject

Inspector References:
None
===============================================================================
*/

using UnityEngine;

public enum GatherResourceState
{
    Idle,
    GettingEquipment,
    ReservingResource,
    Collecting,
    Depositing,
    Completed,
    Failed
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(GatherEquipmentExecutor))]
[RequireComponent(typeof(NpcResourceReservation))]
[RequireComponent(typeof(ResourceCollectionExecutor))]
[RequireComponent(typeof(ResourceDepositExecutor))]
public class GatherResourceExecutor : MonoBehaviour
{
    private Character character;
    private GatherEquipmentExecutor equipmentExecutor;
    private NpcResourceReservation resourceReservation;
    private ResourceCollectionExecutor collectionExecutor;
    private ResourceDepositExecutor depositExecutor;
    private ResourceNode.ResourceType requestedResourceType;
    private bool repeatUntilCancelled;

    public GatherResourceState State { get; private set; } = GatherResourceState.Idle;
    public bool IsRunning =>
        State == GatherResourceState.GettingEquipment ||
        State == GatherResourceState.ReservingResource ||
        State == GatherResourceState.Collecting ||
        State == GatherResourceState.Depositing;
    public bool IsCompleted => State == GatherResourceState.Completed;
    public bool HasFailed => State == GatherResourceState.Failed;
    public bool CompletedBecauseResourceUnavailable { get; private set; }
    public ResourceNode.ResourceType RequestedResourceType => requestedResourceType;

    private void Awake()
    {
        character = GetComponent<Character>();
        equipmentExecutor = GetComponent<GatherEquipmentExecutor>();
        resourceReservation = GetComponent<NpcResourceReservation>();
        collectionExecutor = GetComponent<ResourceCollectionExecutor>();
        depositExecutor = GetComponent<ResourceDepositExecutor>();
    }

    private void OnDisable()
    {
        if (IsRunning)
            CancelGathering();
    }

    private void Update()
    {
        switch (State)
        {
            case GatherResourceState.GettingEquipment:
                UpdateGettingEquipment();
                break;
            case GatherResourceState.Collecting:
                UpdateCollecting();
                break;
            case GatherResourceState.Depositing:
                UpdateDepositing();
                break;
        }
    }

    public bool StartGathering(ResourceNode.ResourceType resourceType, bool repeat)
    {
        if (IsRunning)
            return false;

        requestedResourceType = resourceType;
        repeatUntilCancelled = repeat;
        CompletedBecauseResourceUnavailable = false;
        return StartNextGatherStep();
    }

    public void CancelGathering()
    {
        if (equipmentExecutor != null)
            equipmentExecutor.CancelEquipmentPickup();

        if (collectionExecutor != null)
            collectionExecutor.CancelCollection();

        if (depositExecutor != null)
            depositExecutor.CancelDeposit();

        if (resourceReservation != null)
            resourceReservation.ReleaseResource();

        State = GatherResourceState.Idle;
    }

    private bool StartNextGatherStep()
    {
        if (NeedsDepositBeforeGathering())
            return StartDepositStep();

        if (!HasKnownResource())
        {
            if (character != null && character.HasCarriedResources)
                return StartDepositStep();

            return CompleteBecauseNoResourceIsAvailable();
        }

        if (!TryReserveRequestedResource())
            return HandleUnavailableResource();

        if (!character.HasCorrectGatherEquipment(requestedResourceType))
            return StartEquipmentStep();

        return StartReservationAndCollectionStep();
    }

    private bool HasKnownResource()
    {
        SimulationPerformanceCounters.AddResourceAvailabilityCheapCheck();
        SimulationPerformanceCounters.AddResourceAvailabilityScansAvoided();

        // Known resources include nodes currently reserved by another NPC.
        // The reservation attempt below decides whether this NPC can use one now.
        if (requestedResourceType == ResourceNode.ResourceType.Berries)
            return ResourceRegistry.HasKnownFoodResource();

        return ResourceRegistry.HasKnownWoodResource(ManualDayPlanState.AllowCutEmptyBushes);
    }

    private bool NeedsDepositBeforeGathering()
    {
        if (!character.HasCarriedResources)
            return false;

        if (character.IsFullForResource(requestedResourceType))
            return true;

        return !character.CanCarryResourceType(requestedResourceType);
    }

    private bool StartEquipmentStep()
    {
        if (equipmentExecutor == null || !equipmentExecutor.StartEquipmentPickup(requestedResourceType))
        {
            Fail("GatherResource failed: equipment pickup could not start.");
            return false;
        }

        State = GatherResourceState.GettingEquipment;
        return true;
    }

    private bool StartReservationAndCollectionStep()
    {
        State = GatherResourceState.ReservingResource;

        if (!TryReserveRequestedResource())
            return HandleUnavailableResource();

        ResourceNode reservedResource = resourceReservation.ReservedResource;
        if (reservedResource == null)
        {
            if (!TryReserveRequestedResource())
                return HandleUnavailableResource();

            reservedResource = resourceReservation.ReservedResource;
            if (reservedResource == null)
                return HandleUnavailableResource();
        }

        if (!character.CanCarryResourceType(requestedResourceType, reservedResource.HarvestAmount))
        {
            resourceReservation.ReleaseResource();
            return StartDepositStep();
        }

        if (collectionExecutor == null || !collectionExecutor.StartResourceCollection())
        {
            Fail("GatherResource failed: resource collection could not start.");
            return false;
        }

        State = GatherResourceState.Collecting;
        return true;
    }

    private bool TryReserveRequestedResource()
    {
        if (resourceReservation != null)
            return resourceReservation.TryReserveResource(requestedResourceType);

        SimulationPerformanceCounters.AddGatherResourceReservationFailure();
        Fail("GatherResource failed: resource reservation component is missing.");
        return false;
    }

    private bool HandleUnavailableResource()
    {
        if (State == GatherResourceState.Failed)
            return false;

        if (character != null && character.HasCarriedResources)
            return StartDepositStep();

        return CompleteBecauseNoResourceIsAvailable();
    }

    private bool CompleteBecauseNoResourceIsAvailable()
    {
        if (resourceReservation != null)
            resourceReservation.ReleaseResource();

        CompletedBecauseResourceUnavailable = true;
        Complete();
        return true;
    }

    private bool StartDepositStep()
    {
        if (depositExecutor == null || !depositExecutor.StartDeposit())
        {
            Fail("GatherResource failed: deposit could not start.");
            return false;
        }

        State = GatherResourceState.Depositing;
        return true;
    }

    private void UpdateGettingEquipment()
    {
        if (equipmentExecutor == null)
        {
            Fail("GatherResource failed: equipment executor is missing.");
            return;
        }

        if (equipmentExecutor.IsRunning)
            return;

        if (equipmentExecutor.IsCompleted)
        {
            StartReservationAndCollectionStep();
            return;
        }

        if (equipmentExecutor.HasFailed)
            Fail("GatherResource failed during equipment pickup.");
    }

    private void UpdateCollecting()
    {
        if (collectionExecutor == null)
        {
            Fail("GatherResource failed: collection executor is missing.");
            return;
        }

        if (collectionExecutor.IsRunning)
            return;

        if (collectionExecutor.IsCompleted)
        {
            if (character.IsFullForResource(requestedResourceType))
            {
                StartDepositStep();
                return;
            }

            if (character.HasCarriedResources || repeatUntilCancelled)
            {
                StartNextGatherStep();
                return;
            }

            if (CanContinueEmptyHandedGathering())
            {
                StartNextGatherStep();
                return;
            }

            Complete();
            return;
        }

        if (collectionExecutor.HasFailed)
        {
            if (CanContinueAfterCollectionFailure())
            {
                StartNextGatherStep();
                return;
            }

            Fail("GatherResource failed during collection.");
        }
    }

    private void UpdateDepositing()
    {
        if (depositExecutor == null)
        {
            Fail("GatherResource failed: deposit executor is missing.");
            return;
        }

        if (depositExecutor.IsRunning)
            return;

        if (depositExecutor.IsCompleted)
        {
            if (repeatUntilCancelled)
            {
                StartNextGatherStep();
                return;
            }

            Complete();
            return;
        }

        if (depositExecutor.HasFailed)
            Fail("GatherResource failed during deposit.");
    }

    private void Complete()
    {
        State = GatherResourceState.Completed;
    }

    private bool CanContinueEmptyHandedGathering()
    {
        return character != null &&
            !character.HasCarriedResources &&
            HasKnownResource();
    }

    private bool CanContinueAfterCollectionFailure()
    {
        if (character == null)
            return false;

        if (NeedsDepositBeforeGathering())
            return true;

        if (!character.HasCorrectGatherEquipment(requestedResourceType))
            return false;

        return HasKnownResource();
    }

    private void Fail(string message)
    {
        Debug.LogWarning($"{message} GameObject: {gameObject.name}.");

        if (resourceReservation != null)
            resourceReservation.ReleaseResource();

        if (collectionExecutor != null)
            collectionExecutor.CancelCollection();

        if (depositExecutor != null)
            depositExecutor.CancelDeposit();

        State = GatherResourceState.Failed;
    }
}
