/*
===============================================================================
Script:
TreeStatusPanelController

Purpose:
Controls the world-space status panel for selected resource nodes.

Folder:
Assets/Scripts/UI/

Scene Hierarchy:
_UI
- WorldSpaceCanvas
  - TreeStatusPanel

Attach To:
TreeStatusPanel

Required Children:
- TreeStatusText

Dependencies:
- ResourceNode
- Camera tagged MainCamera

Automatically Resolved:
- TreeStatusText
- Main Camera

Inspector References:
None
===============================================================================
*/
using UnityEngine;
using TMPro;

public class TreeStatusPanelController : MonoBehaviour
{
    [SerializeField] private Vector3 treeStatusOffset = new Vector3(0f, 1.4f, 0f);

    private TMP_Text treeStatusText;
    private ResourceNode shownTree;
    private Camera mainCamera;

    private void Reset()
    {
        AutoWireReferences();
    }

    private void Awake()
    {
        AutoWireReferences();

        Debug.Assert(
            treeStatusText != null,
            $"Missing component: TMP_Text named TreeStatusText. Expected location: child of TreeStatusPanel. Script: {nameof(TreeStatusPanelController)}. GameObject: {gameObject.name}."
        );

        mainCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        UpdatePanel();
    }

    public void Show(ResourceNode tree)
    {
        AutoWireReferences();

        shownTree = null;

        if (tree == null)
            return;

        if (treeStatusText == null)
        {
            Debug.LogWarning("Tree selection UI is not assigned.");
            return;
        }

        shownTree = tree;
        gameObject.SetActive(true);
        UpdatePanel();
    }

    public void Hide()
    {
        shownTree = null;
        gameObject.SetActive(false);
    }

    private void UpdatePanel()
    {
        if (shownTree == null || treeStatusText == null)
            return;

        transform.position = GetTreeStatusPosition(shownTree.transform);

        if (mainCamera != null)
            transform.rotation = mainCamera.transform.rotation;

        treeStatusText.text = shownTree.HasResources ? shownTree.DisplayName : shownTree.EmptyDisplayName;
    }

    private Vector3 GetTreeStatusPosition(Transform treeTransform)
    {
        Renderer renderer = treeTransform.GetComponentInChildren<Renderer>();
        if (renderer == null)
            return treeTransform.position + treeStatusOffset;

        return renderer.bounds.center + new Vector3(0f, renderer.bounds.extents.y, 0f) + treeStatusOffset;
    }

    [ContextMenu("Auto Wire References")]
    private void AutoWireReferences()
    {
        if (treeStatusText == null)
            treeStatusText = FindChildComponent<TMP_Text>("TreeStatusText");
    }

    private T FindChildComponent<T>(string childName) where T : Component
    {
        Transform child = FindChild(childName);
        return child != null ? child.GetComponent<T>() : null;
    }

    private Transform FindChild(string childName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child.name == childName)
                return child;
        }

        return null;
    }
}
