/*
===============================================================================
Script:
Backpack

Purpose:
Stores carried resources and updates backpack resource visuals.

Folder:
Assets/Scripts/Inventory/

Scene Hierarchy:
_World
- Characters
  - Character
    - Backpack

Attach To:
Backpack

Required Children:
- Woodpack
- Berrypack
- Berry visual children under Berrypack
- Optional Hammer visual child
- Optional Lantern or Laterne visual child

Dependencies:
None

Automatically Resolved:
- Woodpack child GameObject
- Berrypack child GameObject
- Berry visual child GameObjects
- Hammer child GameObject
- Lantern or Laterne child GameObject

Inspector References:
- maxWood
- maxBerries
- woodGatherCarryCapacity
- berryGatherCarryCapacity
- Wood Logs array
===============================================================================
*/
using UnityEngine;
using System.Collections.Generic;

public class Backpack : MonoBehaviour
{
    private const int WoodGatherCapacity = 5;
    private const int BerryGatherCapacity = 60;

    [SerializeField] private int maxWood = 5;
    [SerializeField] private int currentWood = 0;
    [SerializeField] private int currentBerries = 0;
    [SerializeField] private int maxBerries = BerryGatherCapacity;
    [Tooltip("Legacy shared gather-work trip capacity. Use woodGatherCarryCapacity and berryGatherCarryCapacity for current behavior.")]
    [SerializeField, HideInInspector] private int gatherCarryCapacity = WoodGatherCapacity;
    [Tooltip("Wood basket capacity for shared gather-work trips.")]
    [SerializeField] private int woodGatherCarryCapacity = WoodGatherCapacity;
    [Tooltip("Berry basket capacity for shared gather-work trips.")]
    [SerializeField] private int berryGatherCarryCapacity = BerryGatherCapacity;
    [System.Obsolete("Use GetGatherCarryCapacity(resourceType) instead.")]
    public int GatherCarryCapacity => WoodGatherCarryCapacity;

    [Header("Wood Visuals")]
    [SerializeField] private GameObject[] woodLogs;

    [Header("Tool Visuals")]
    [SerializeField] private GameObject hammer;
    [SerializeField] private GameObject lantern;
    [SerializeField] private bool logInventoryChanges;

    private GameObject woodpack;
    private GameObject berrypack;
    private GameObject[] berryVisuals;
    private bool hasGatherEquipment;
    private ResourceNode.ResourceType equippedResourceType;
    private bool showHammer;
    private bool showLantern;
    private bool missingHammerWarningLogged;
    private bool missingLanternWarningLogged;
    private bool referencesCached;
    private bool woodpackWasResolved;
    private bool berrypackWasResolved;
    private bool hammerWasResolved;
    private bool lanternWasResolved;

    public int CurrentWood => currentWood;
    public int CurrentBerries => currentBerries;
    public int WoodGatherCarryCapacity => Mathf.Max(WoodGatherCapacity, woodGatherCarryCapacity);
    public int BerryGatherCarryCapacity => Mathf.Max(BerryGatherCapacity, berryGatherCarryCapacity);
    public bool HasResources => currentWood > 0 || currentBerries > 0;
    public bool HasGatherEquipment => hasGatherEquipment;
    public ResourceNode.ResourceType EquippedResourceType => equippedResourceType;

    private void Awake()
    {
        ValidateSettings();
        AutoWireReferences();

        Debug.Assert(
            woodpack != null,
            $"Missing child GameObject named Woodpack. Expected location: child of Backpack. Script: {nameof(Backpack)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            berrypack != null,
            $"Missing child GameObject named Berrypack. Expected location: child of Backpack. Script: {nameof(Backpack)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            berryVisuals.Length >= maxBerries,
            $"Missing berry visual children. Expected at least {maxBerries} child GameObjects named Berry under Berrypack. Script: {nameof(Backpack)}. GameObject: {gameObject.name}. Found: {berryVisuals.Length}."
        );
    }

    private void Start()
    {
        WarnIfToolVisualsMissing();
        UpdateVisuals();
    }

    private void OnValidate()
    {
        ValidateSettings();
    }

    public bool CanAddWood()
    {
        return CanAddWood(1);
    }

    public bool CanAddWood(int amount)
    {
        return currentBerries <= 0 && currentWood + amount <= maxWood;
    }

    public bool CanAddBerries()
    {
        return CanAddBerries(1);
    }

    public bool CanAddBerries(int amount)
    {
        return currentWood <= 0 && currentBerries + amount <= maxBerries;
    }

    public bool CanAddForGathering(ResourceNode.ResourceType resourceType, int amount)
    {
        switch (resourceType)
        {
            case ResourceNode.ResourceType.Berries:
                return currentWood <= 0 && currentBerries + amount <= Mathf.Min(maxBerries, BerryGatherCarryCapacity);
            default:
                return currentBerries <= 0 && currentWood + amount <= Mathf.Min(maxWood, WoodGatherCarryCapacity);
        }
    }

    public void AddWood(int amount)
    {
        currentWood = Mathf.Clamp(currentWood + amount, 0, maxWood);
        UpdateVisuals();

        if (logInventoryChanges)
            Debug.Log("Wood in backpack: " + currentWood + "/" + maxWood);
    }

    public void AddBerries(int amount)
    {
        currentBerries = Mathf.Clamp(currentBerries + amount, 0, maxBerries);
        UpdateVisuals();

        if (logInventoryChanges)
            Debug.Log("Berries in backpack: " + currentBerries + "/" + maxBerries);
    }

    public bool IsFullFor(ResourceNode.ResourceType resourceType)
    {
        return IsFullForGathering(resourceType);
    }

    public bool IsFullForGathering(ResourceNode.ResourceType resourceType)
    {
        switch (resourceType)
        {
            case ResourceNode.ResourceType.Berries:
                return currentBerries >= Mathf.Min(maxBerries, BerryGatherCarryCapacity);
            default:
                return currentWood >= Mathf.Min(maxWood, WoodGatherCarryCapacity);
        }
    }

    public int GetGatherCarryCapacity(ResourceNode.ResourceType resourceType)
    {
        switch (resourceType)
        {
            case ResourceNode.ResourceType.Berries:
                return Mathf.Min(maxBerries, BerryGatherCarryCapacity);
            default:
                return Mathf.Min(maxWood, WoodGatherCarryCapacity);
        }
    }

    public bool HasCorrectGatherEquipment(ResourceNode.ResourceType resourceType)
    {
        return hasGatherEquipment && equippedResourceType == resourceType;
    }

    public void EquipForGathering(ResourceNode.ResourceType resourceType)
    {
        equippedResourceType = resourceType;
        hasGatherEquipment = true;
        showHammer = false;
        showLantern = false;
        UpdateVisuals();
    }

    public void ClearGatherEquipment()
    {
        hasGatherEquipment = false;
        UpdateVisuals();
    }

    public void ShowBuildTool()
    {
        showHammer = true;
        showLantern = false;
        UpdateVisuals();
    }

    public void ShowExploreTool()
    {
        showHammer = false;
        showLantern = true;
        UpdateVisuals();
    }

    public void ClearJobTools()
    {
        showHammer = false;
        showLantern = false;
        UpdateVisuals();
    }

    public int RemoveAllWood()
    {
        int removed = currentWood;
        currentWood = 0;
        UpdateVisuals();

        return removed;
    }

    public int RemoveAllBerries()
    {
        int removed = currentBerries;
        currentBerries = 0;
        UpdateVisuals();

        return removed;
    }

    private void UpdateVisuals()
    {
        AutoWireReferences();

        bool hasResources = currentWood > 0 || currentBerries > 0;
        bool showWoodEquipment = hasGatherEquipment && equippedResourceType == ResourceNode.ResourceType.Wood;
        bool showBerryEquipment = hasGatherEquipment && equippedResourceType == ResourceNode.ResourceType.Berries;
        bool hasVisibleTool = showHammer || showLantern;

        if (gameObject.activeSelf != (hasResources || hasGatherEquipment || hasVisibleTool))
            gameObject.SetActive(hasResources || hasGatherEquipment || hasVisibleTool);

        if (woodpack != null)
            woodpack.SetActive(currentWood > 0 || showWoodEquipment);

        if (berrypack != null)
            berrypack.SetActive(currentBerries > 0 || showBerryEquipment);

        if (hammer != null)
            hammer.SetActive(showHammer);

        if (lantern != null)
            lantern.SetActive(showLantern);

        if (woodLogs != null)
        {
            for (int i = 0; i < woodLogs.Length; i++)
            {
                if (woodLogs[i] != null)
                    woodLogs[i].SetActive(i < currentWood);
            }
        }

        for (int i = 0; i < berryVisuals.Length; i++)
        {
            if (berryVisuals[i] != null)
                berryVisuals[i].SetActive(i < currentBerries);
        }
    }

    private void AutoWireReferences()
    {
        if (HasValidReferenceCache())
            return;

        RebuildReferenceCache();
    }

    [ContextMenu("Auto Wire References")]
    private void ForceAutoWireReferences()
    {
        RebuildReferenceCache();
    }

    private bool HasValidReferenceCache()
    {
        if (!referencesCached || berryVisuals == null)
            return false;

        if (woodpackWasResolved && woodpack == null)
            return false;

        if (berrypackWasResolved && berrypack == null)
            return false;

        if (hammerWasResolved && hammer == null)
            return false;

        return !lanternWasResolved || lantern != null;
    }

    private void RebuildReferenceCache()
    {
        if (woodpack == null)
            woodpack = FindChild("Woodpack")?.gameObject;

        if (berrypack == null)
            berrypack = FindChild("Berrypack")?.gameObject;

        if (hammer == null)
            hammer = FindChild("Hammer")?.gameObject;

        if (lantern == null)
            lantern = FindChild("Lantern")?.gameObject ?? FindChild("Laterne")?.gameObject;

        CacheBerryVisuals();
        woodpackWasResolved = woodpack != null;
        berrypackWasResolved = berrypack != null;
        hammerWasResolved = hammer != null;
        lanternWasResolved = lantern != null;
        referencesCached = true;
    }

    private void WarnIfToolVisualsMissing()
    {
        if (hammer == null && !missingHammerWarningLogged)
        {
            Debug.LogWarning($"Backpack could not find optional tool visual child named Hammer. Build jobs will run without a hammer visual. GameObject: {gameObject.name}.");
            missingHammerWarningLogged = true;
        }

        if (lantern == null && !missingLanternWarningLogged)
        {
            Debug.LogWarning($"Backpack could not find optional tool visual child named Lantern or Laterne. Explore jobs will run without a lantern visual. GameObject: {gameObject.name}.");
            missingLanternWarningLogged = true;
        }
    }

    private void CacheBerryVisuals()
    {
        List<GameObject> foundVisuals = new List<GameObject>();

        if (berrypack == null)
        {
            berryVisuals = foundVisuals.ToArray();
            return;
        }

        Transform[] children = berrypack.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (IsBerryVisualName(child.name))
                foundVisuals.Add(child.gameObject);
        }

        berryVisuals = foundVisuals.ToArray();
    }

    private bool IsBerryVisualName(string objectName)
    {
        return objectName == "Berry" || objectName.StartsWith("Berry (");
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

    private void ValidateSettings()
    {
        gatherCarryCapacity = Mathf.Max(1, gatherCarryCapacity);
        woodGatherCarryCapacity = WoodGatherCapacity;
        berryGatherCarryCapacity = BerryGatherCapacity;
        maxWood = WoodGatherCapacity;
        maxBerries = BerryGatherCapacity;
        currentWood = Mathf.Clamp(currentWood, 0, maxWood);
        currentBerries = Mathf.Clamp(currentBerries, 0, maxBerries);
    }
}
