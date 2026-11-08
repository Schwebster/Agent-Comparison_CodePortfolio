/*
===============================================================================
Script:
NpcResourceReservation

Purpose:
Provides shared NPC resource lookup and reservation facts for future agent logic.

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
- CharacterIdentity
- ResourceNode
- ResourceRegistry

Automatically Resolved:
- CharacterIdentity on the same GameObject

Inspector References:
None
===============================================================================
*/

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterIdentity))]
public class NpcResourceReservation : MonoBehaviour
{
    private CharacterIdentity identity;
    private ResourceNode reservedResource;
    private Vector3 reservedResourceInteractionPosition;

    public bool HasReservedResource => reservedResource != null && reservedResource.IsReservedBy(identity);
    public ResourceNode ReservedResource => HasReservedResource ? reservedResource : null;
    public Vector3 ReservedResourceInteractionPosition => HasReservedResource ? reservedResourceInteractionPosition : transform.position;

    private void Awake()
    {
        identity = GetComponent<CharacterIdentity>();
    }

    private void OnDisable()
    {
        ReleaseResource();
    }

    private void Update()
    {
        if (reservedResource == null)
            return;

        ResolveIdentity();

        if (identity == null)
        {
            reservedResource = null;
            reservedResourceInteractionPosition = transform.position;
            return;
        }

        if (!reservedResource.IsAvailable)
        {
            reservedResource.ReleaseReservation(identity);
            reservedResource = null;
            reservedResourceInteractionPosition = transform.position;
            return;
        }

        if (!reservedResource.IsReservedBy(identity))
        {
            reservedResource = null;
            reservedResourceInteractionPosition = transform.position;
        }
    }

    public bool TryReserveResource(ResourceNode.ResourceType resourceType)
    {
        return TryReserveResource(resourceType, transform.position);
    }

    public bool TryReserveResource(ResourceNode.ResourceType resourceType, Vector3 requesterPosition)
    {
        if (HasReservedResource &&
            reservedResource.IsAvailable &&
            reservedResource.CurrentHarvestType == resourceType)
        {
            return true;
        }

        ReleaseResource();
        ResolveIdentity();

        if (identity == null)
            return false;

        identity.EnsureAssigned();

        return ResourceRegistry.TryReserveClosestReachableResource(
            resourceType,
            requesterPosition,
            identity,
            out reservedResource,
            out reservedResourceInteractionPosition
        );
    }

    public void ReleaseResource()
    {
        if (reservedResource == null)
            return;

        ResolveIdentity();

        if (identity != null)
            reservedResource.ReleaseReservation(identity);

        reservedResource = null;
        reservedResourceInteractionPosition = transform.position;
    }

    public bool TryGetReservedResourcePosition(out Vector3 position)
    {
        if (!HasReservedResource)
        {
            position = transform.position;
            return false;
        }

        position = reservedResourceInteractionPosition;
        return true;
    }

    private void ResolveIdentity()
    {
        if (identity == null)
            identity = GetComponent<CharacterIdentity>();
    }
}
