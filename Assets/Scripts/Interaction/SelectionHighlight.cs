/*
===============================================================================
Script:
SelectionHighlight

Purpose:
Applies and restores visual highlighting for a selected world object.

Folder:
Assets/Scripts/Interaction/

Scene Hierarchy:
Selectable world objects

Attach To:
Selectable world object root

Required Children:
- One or more Renderer components in this GameObject or its children

Dependencies:
- Renderer
- MaterialPropertyBlock

Automatically Resolved:
- Renderer components in child objects, including inactive children

Inspector References:
None
===============================================================================
*/
using UnityEngine;

public class SelectionHighlight : MonoBehaviour
{
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.35f, 1f);
    [SerializeField, Range(0f, 1f)] private float colorBlend = 0.25f;

    private RendererState[] rendererStates;
    private bool isHighlighted;

    private void Awake()
    {
        CacheRenderers();
    }

    private void OnDisable()
    {
        Hide();
    }

    public void Show()
    {
        if (isHighlighted)
            return;

        if (rendererStates == null)
            CacheRenderers();

        foreach (RendererState rendererState in rendererStates)
            rendererState.ApplyHighlight(highlightColor, colorBlend);

        isHighlighted = true;
    }

    public void Hide()
    {
        if (!isHighlighted || rendererStates == null)
            return;

        foreach (RendererState rendererState in rendererStates)
            rendererState.Restore();

        isHighlighted = false;
    }

    private void CacheRenderers()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        rendererStates = new RendererState[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
            rendererStates[i] = new RendererState(renderers[i]);
    }

    private class RendererState
    {
        private readonly Renderer renderer;
        private readonly MaterialPropertyBlock[] originalBlocks;

        public RendererState(Renderer renderer)
        {
            this.renderer = renderer;

            int materialCount = renderer.sharedMaterials.Length;
            originalBlocks = new MaterialPropertyBlock[materialCount];

            for (int i = 0; i < materialCount; i++)
            {
                originalBlocks[i] = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(originalBlocks[i], i);
            }
        }

        public void ApplyHighlight(Color highlightColor, float colorBlend)
        {
            Material[] materials = renderer.sharedMaterials;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;

                string colorProperty = GetColorProperty(material);
                if (string.IsNullOrEmpty(colorProperty))
                    continue;

                Color baseColor = material.GetColor(colorProperty);
                Color selectedColor = Color.Lerp(baseColor, highlightColor, colorBlend);

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                block.SetColor(colorProperty, selectedColor);
                renderer.SetPropertyBlock(block, i);
            }
        }

        public void Restore()
        {
            for (int i = 0; i < originalBlocks.Length; i++)
                renderer.SetPropertyBlock(originalBlocks[i], i);
        }

        private string GetColorProperty(Material material)
        {
            if (material.HasProperty("_BaseColor"))
                return "_BaseColor";

            if (material.HasProperty("_Color"))
                return "_Color";

            return string.Empty;
        }
    }
}
