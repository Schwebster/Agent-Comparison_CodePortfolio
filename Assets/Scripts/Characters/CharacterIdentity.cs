/*
===============================================================================
Script:
CharacterIdentity

Purpose:
Stores a unique play-session identity and generated name for a character.

Folder:
Assets/Scripts/Characters/

Scene Hierarchy:
_World
- Characters

Attach To:
Character

Required Children:
None

Dependencies:
- CharacterNameGenerator

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CharacterIdentity : MonoBehaviour
{
    private static int nextCharacterId = 1;
    private static readonly HashSet<CharacterIdentity> activeIdentities = new HashSet<CharacterIdentity>();

    [SerializeField] private string firstName;
    [SerializeField] private string lastName;
    [SerializeField] private int characterId;

    public string FullName => $"{firstName} {lastName}";
    public int CharacterId => characterId;
    public static int ActiveIdentityCount => activeIdentities.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        activeIdentities.Clear();
    }

    private void Awake()
    {
        EnsureAssigned();
    }

    private void OnEnable()
    {
        EnsureAssigned();
        activeIdentities.Add(this);
    }

    private void OnDisable()
    {
        activeIdentities.Remove(this);
    }

    public void EnsureAssigned()
    {
        if (characterId <= 0)
        {
            characterId = nextCharacterId++;
        }
        else if (characterId >= nextCharacterId)
        {
            nextCharacterId = characterId + 1;
        }

        if (string.IsNullOrWhiteSpace(firstName))
            firstName = CharacterNameGenerator.GetRandomFirstName();

        if (string.IsNullOrWhiteSpace(lastName))
            lastName = CharacterNameGenerator.GetRandomLastName();

        ApplyGameObjectName(gameObject);
    }

    public void ApplyGameObjectName(GameObject target)
    {
        if (target == null)
            return;

        target.name = FullName;
    }
}
