/*
===============================================================================
Script:
House

Purpose:
Tracks sleeping capacity and assigned residents for a house.

Folder:
Assets/Scripts/Buildings/

Scene Hierarchy:
_World
- Buildings
  - House

Attach To:
House

Required Children:
None

Dependencies:
- CharacterIdentity
- HomeRegistry

Automatically Resolved:
- Direct child named "Entry", if entryPoint is not assigned

Inspector References:
- sleepingCapacity
- entryPoint
===============================================================================
*/
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class House : MonoBehaviour
{
    [SerializeField] private int sleepingCapacity = 3;
    [SerializeField] private Transform entryPoint;
    [SerializeField] private List<CharacterIdentity> residents = new List<CharacterIdentity>();

    public int SleepingCapacity => Mathf.Max(0, sleepingCapacity);
    public int OccupiedSlots
    {
        get
        {
            RemoveMissingResidents();
            return residents.Count;
        }
    }

    public int FreeSlots => Mathf.Max(0, SleepingCapacity - OccupiedSlots);
    public IReadOnlyList<CharacterIdentity> Residents => residents;
    public bool HasFreeSlot => FreeSlots > 0;
    public Vector3 HomePosition => transform.position;
    public Vector3 HomeEntryPosition => entryPoint != null ? entryPoint.position : transform.position;
    public Vector3 EntryPosition => HomeEntryPosition;

    private void Awake()
    {
        ResolveEntryPoint();
    }

    private void OnEnable()
    {
        HomeRegistry.Register(this);
    }

    private void OnDisable()
    {
        HomeRegistry.Unregister(this);
    }

    public bool TryAssignResident(CharacterIdentity resident)
    {
        return TryReserveSlot(resident);
    }

    public bool TryReserveSlot(CharacterIdentity resident)
    {
        if (resident == null)
            return false;

        RemoveMissingResidents();

        if (residents.Contains(resident))
            return true;

        if (!HasFreeSlot)
            return false;

        residents.Add(resident);
        Debug.Log($"Reserved home slot for {resident.FullName} (ID {resident.CharacterId}) at {gameObject.name}.");
        return true;
    }

    public bool RemoveResident(CharacterIdentity resident)
    {
        return ReleaseSlot(resident);
    }

    public bool ReleaseSlot(CharacterIdentity resident)
    {
        if (resident == null)
            return false;

        bool removed = residents.Remove(resident);

        if (removed)
            Debug.Log($"Released home slot for {resident.FullName} (ID {resident.CharacterId}) at {gameObject.name}.");

        return removed;
    }

    private void OnValidate()
    {
        sleepingCapacity = Mathf.Max(0, sleepingCapacity);
        ResolveEntryPoint();
        RemoveMissingResidents();

        while (residents.Count > sleepingCapacity)
            residents.RemoveAt(residents.Count - 1);
    }

    private void ResolveEntryPoint()
    {
        if (entryPoint != null)
            return;

        Transform directEntryChild = transform.Find("Entry");

        if (directEntryChild != null)
            entryPoint = directEntryChild;
    }

    private void RemoveMissingResidents()
    {
        residents.RemoveAll(resident => resident == null);
    }
}
