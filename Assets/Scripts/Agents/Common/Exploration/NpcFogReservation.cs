/*
===============================================================================
Script:
NpcFogReservation

Purpose:
Stores the FogObject currently reserved by one NPC.

Folder:
Assets/Scripts/Agents/Common/Exploration/

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
- FogObject
- FogRegistry

Automatically Resolved:
- CharacterIdentity on the same GameObject

Inspector References:
None
===============================================================================
*/

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterIdentity))]
public class NpcFogReservation : MonoBehaviour
{
    private CharacterIdentity identity;
    private FogObject reservedFog;
    private Vector3 reservedFogInteractionPosition;

    public bool HasReservedFog => reservedFog != null && reservedFog.IsReservedBy(identity);
    public FogObject ReservedFog => HasReservedFog ? reservedFog : null;
    public Vector3 ReservedFogInteractionPosition => HasReservedFog ? reservedFogInteractionPosition : transform.position;

    private void Awake()
    {
        identity = GetComponent<CharacterIdentity>();
    }

    private void OnDisable()
    {
        ReleaseFog();
    }

    private void Update()
    {
        if (reservedFog == null)
            return;

        ResolveIdentity();

        if (identity == null)
        {
            ClearReservationFacts();
            return;
        }

        if (!reservedFog.IsAvailable)
        {
            reservedFog.ReleaseReservation(identity);
            ClearReservationFacts();
            return;
        }

        if (!reservedFog.IsReservedBy(identity))
            ClearReservationFacts();
    }

    public bool TryReserveFog()
    {
        return TryReserveFog(transform.position);
    }

    public bool TryReserveFog(Vector3 requesterPosition)
    {
        if (HasReservedFog && reservedFog.IsAvailable)
            return true;

        ReleaseFog();
        ResolveIdentity();

        if (identity == null)
            return false;

        identity.EnsureAssigned();

        return FogRegistry.TryReserveClosestReachableFog(
            requesterPosition,
            identity,
            out reservedFog,
            out reservedFogInteractionPosition
        );
    }

    public bool TryReserveFogInDirection(
        Vector3 origin,
        ExploreDirection direction,
        Vector3 requesterPosition)
    {
        if (HasReservedFog && reservedFog.IsAvailable)
            return true;

        ReleaseFog();
        ResolveIdentity();

        if (identity == null)
            return false;

        identity.EnsureAssigned();

        return FogRegistry.TryReserveClosestReachableFogInDirection(
            origin,
            direction,
            requesterPosition,
            identity,
            out reservedFog,
            out reservedFogInteractionPosition
        );
    }

    public void ReleaseFog()
    {
        if (reservedFog == null)
            return;

        ResolveIdentity();

        if (identity != null)
            reservedFog.ReleaseReservation(identity);

        ClearReservationFacts();
    }

    public bool TryGetReservedFogPosition(out Vector3 position)
    {
        if (!HasReservedFog)
        {
            position = transform.position;
            return false;
        }

        position = reservedFogInteractionPosition;
        return true;
    }

    private void ResolveIdentity()
    {
        if (identity == null)
            identity = GetComponent<CharacterIdentity>();
    }

    private void ClearReservationFacts()
    {
        reservedFog = null;
        reservedFogInteractionPosition = transform.position;
    }
}
