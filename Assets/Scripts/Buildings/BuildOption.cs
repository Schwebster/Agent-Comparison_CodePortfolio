/*
===============================================================================
Script:
BuildOption

Purpose:
Describes one buildable construction option.

Folder:
Assets/Scripts/Buildings/

Scene Hierarchy:
Configured on build menu components

Attach To:
None

Required Children:
None

Dependencies:
None

Automatically Resolved:
None

Inspector References:
- displayName
- constructionSitePrefab
- ghostPrefab
- footprintSize
- requiredWood
- requiredFood
===============================================================================
*/

using System;
using UnityEngine;

[Serializable]
public class BuildOption
{
    [SerializeField] private string displayName;
    [SerializeField] private GameObject constructionSitePrefab;
    [SerializeField] private GameObject ghostPrefab;
    [SerializeField] private Vector2 footprintSize = new Vector2(4f, 4f);
    [SerializeField] private int requiredWood;
    [SerializeField] private int requiredFood;

    public string DisplayName => displayName;
    public GameObject ConstructionSitePrefab => constructionSitePrefab;
    public GameObject GhostPrefab => ghostPrefab;
    public Vector2 FootprintSize => footprintSize;
    public int RequiredWood => requiredWood;
    public int RequiredFood => requiredFood;
}
