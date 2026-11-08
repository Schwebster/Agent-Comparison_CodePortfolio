/*
===============================================================================
Script:
ICharacterActions

Purpose:
Defines character actions that can be issued by player control or autonomous agents.

Folder:
Assets/Scripts/Actions/

Scene Hierarchy:
Implemented by character action adapters

Attach To:
None

Required Children:
None

Dependencies:
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

public interface ICharacterActions
{
    void MoveTo(Vector3 position);
    void Harvest(ResourceNode resource);
    void Deposit(ResourceStorage storage);
    void Construct(ConstructionSite constructionSite);
}
