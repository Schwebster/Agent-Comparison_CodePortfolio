/*
===============================================================================
Script:
SelectionHighlightController

Purpose:
Coordinates the currently active selection highlight.

Folder:
Assets/Scripts/Interaction/

Scene Hierarchy:
_Systems
- GameManager

Attach To:
GameManager

Required Children:
None

Dependencies:
- SelectionHighlight

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/
using UnityEngine;

public class SelectionHighlightController : MonoBehaviour
{
    private SelectionHighlight currentHighlight;

    public void Select(GameObject target)
    {
        Clear();

        if (target == null)
            return;

        currentHighlight = target.GetComponent<SelectionHighlight>();

        if (currentHighlight == null)
            currentHighlight = target.AddComponent<SelectionHighlight>();

        currentHighlight.Show();
    }

    public void Clear()
    {
        if (currentHighlight == null)
            return;

        currentHighlight.Hide();
        currentHighlight = null;
    }
}
