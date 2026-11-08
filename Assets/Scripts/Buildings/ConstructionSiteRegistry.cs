/*
===============================================================================
Script:
ConstructionSiteRegistry

Purpose:
Keeps a lightweight runtime registry of active construction sites for NPC build availability checks.

Folder:
Assets/Scripts/Buildings/

Scene Hierarchy:
None

Attach To:
None

Required Children:
None

Dependencies:
- ConstructionSite

Automatically Resolved:
ConstructionSite instances register themselves from OnEnable and OnDisable.

Inspector References:
None
===============================================================================
*/

using System.Collections.Generic;
using UnityEngine;

public static class ConstructionSiteRegistry
{
    private static readonly List<ConstructionSite> constructionSites = new List<ConstructionSite>();
    private static int lastAvailabilityCheckFrame = -1;
    private static bool hasAvailableConstructionSite;

    public static IReadOnlyList<ConstructionSite> ActiveConstructionSites => constructionSites;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        constructionSites.Clear();
        InvalidateAvailabilityCache();
    }

    public static void Register(ConstructionSite constructionSite)
    {
        if (constructionSite == null || constructionSites.Contains(constructionSite))
            return;

        constructionSites.Add(constructionSite);
        InvalidateAvailabilityCache();
    }

    public static void Unregister(ConstructionSite constructionSite)
    {
        if (constructionSite == null)
            return;

        constructionSites.Remove(constructionSite);
        InvalidateAvailabilityCache();
    }

    public static bool HasActiveConstructionSite()
    {
        for (int i = constructionSites.Count - 1; i >= 0; i--)
        {
            ConstructionSite constructionSite = constructionSites[i];
            if (constructionSite == null)
            {
                constructionSites.RemoveAt(i);
                continue;
            }

            if (constructionSite.isActiveAndEnabled && !constructionSite.IsComplete)
                return true;
        }

        return false;
    }

    public static bool HasAvailableConstructionSite()
    {
        if (lastAvailabilityCheckFrame == Time.frameCount)
            return hasAvailableConstructionSite;

        lastAvailabilityCheckFrame = Time.frameCount;
        hasAvailableConstructionSite = false;

        for (int i = constructionSites.Count - 1; i >= 0; i--)
        {
            ConstructionSite constructionSite = constructionSites[i];
            if (constructionSite == null)
            {
                constructionSites.RemoveAt(i);
                continue;
            }

            if (!constructionSite.isActiveAndEnabled ||
                constructionSite.IsComplete ||
                !constructionSite.CanAcceptWork())
            {
                continue;
            }

            hasAvailableConstructionSite = true;
            break;
        }

        return hasAvailableConstructionSite;
    }

    private static void InvalidateAvailabilityCache()
    {
        lastAvailabilityCheckFrame = -1;
        hasAvailableConstructionSite = false;
    }
}
