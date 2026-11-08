/*
===============================================================================
Script:
CharacterActions

Purpose:
Adapts the existing Character component to the shared character action interface.

Folder:
Assets/Scripts/Actions/

Scene Hierarchy:
Created in code for selected Character instances

Attach To:
None

Required Children:
None

Dependencies:
- Character
- ResourceNode
- ResourceStorage
- ConstructionSite

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/

using UnityEngine;

public sealed class CharacterActions : ICharacterActions
{
    private readonly Character character;

    public CharacterActions(Character character)
    {
        Debug.Assert(
            character != null,
            $"Missing dependency: Character. Expected location: constructor argument. Script: {nameof(CharacterActions)}."
        );

        this.character = character;
    }

    public void MoveTo(Vector3 position)
    {
        if (character == null)
            return;

        character.MoveTo(position);
    }

    public void Harvest(ResourceNode resource)
    {
        if (character == null)
            return;

        character.Harvest(resource);
    }

    public void Deposit(ResourceStorage storage)
    {
        if (character == null)
            return;

        character.Deposit(storage);
    }

    public void Construct(ConstructionSite constructionSite)
    {
        if (character == null)
            return;

        character.Construct(constructionSite);
    }
}
