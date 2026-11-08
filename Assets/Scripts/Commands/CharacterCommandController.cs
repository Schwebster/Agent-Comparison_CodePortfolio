/*
===============================================================================
Script:
CharacterCommandController

Purpose:
Handles right-click commands for the currently player-controlled character.

Folder:
Assets/Scripts/Commands/

Scene Hierarchy:
_Systems
- GameManager

Attach To:
GameManager

Required Children:
None

Dependencies:
- Character
- CharacterControlRegistry
- ICharacterActions
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

public class CharacterCommandController : MonoBehaviour
{
    private const float RaycastDistance = 1000f;

    public void HandleRightClick(Vector2 screenPosition)
    {
        Character controlledCharacter = CharacterControlRegistry.GetPlayerControlledCharacter();

        if (controlledCharacter == null)
        {
            Debug.Log("No character controlled.");
            return;
        }

        ICharacterActions characterActions = new CharacterActions(controlledCharacter);

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("No main camera found for command handling.");
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(screenPosition);

        if (!TryGetCommandHit(ray, out RaycastHit hit))
            return;

        ResourceNode resource = hit.collider.GetComponent<ResourceNode>();
        if (resource == null)
            resource = hit.collider.GetComponentInParent<ResourceNode>();

        if (resource != null && resource.CanHarvest)
        {
            characterActions.Harvest(resource);
            return;
        }

        ResourceStorage storage = hit.collider.GetComponent<ResourceStorage>();
        if (storage == null)
            storage = hit.collider.GetComponentInParent<ResourceStorage>();

        if (storage != null)
        {
            characterActions.Deposit(storage);
            return;
        }

        ConstructionSite constructionSite = hit.collider.GetComponent<ConstructionSite>();
        if (constructionSite == null)
            constructionSite = hit.collider.GetComponentInParent<ConstructionSite>();

        if (constructionSite != null)
        {
            characterActions.Construct(constructionSite);
            return;
        }

        characterActions.MoveTo(hit.point);
    }

    private bool TryGetCommandHit(Ray ray, out RaycastHit commandHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            RaycastDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide
        );

        System.Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));

        foreach (RaycastHit hit in hits)
        {
            ResourceNode resource = hit.collider.GetComponent<ResourceNode>();
            if (resource == null)
                resource = hit.collider.GetComponentInParent<ResourceNode>();

            if (resource != null && !resource.CanHarvest)
                continue;

            commandHit = hit;
            return true;
        }

        commandHit = default;
        return false;
    }
}
