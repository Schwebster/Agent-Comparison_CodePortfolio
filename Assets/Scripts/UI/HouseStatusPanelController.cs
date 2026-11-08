/*
===============================================================================
Script:
HouseStatusPanelController

Purpose:
Shows and updates the manually built screen-space house status panel.

Folder:
Assets/Scripts/UI/

Scene Hierarchy:
_UI
- ScreenSpaceCanvas
  - HouseStatusPanel

Attach To:
HouseStatusPanel

Required Children:
- HouseTitleText
- ResidentsText

Dependencies:
- House

Automatically Resolved:
None

Inspector References:
- panelRoot
- houseTitleText
- residentsText
===============================================================================
*/
using System.Text;
using TMPro;
using UnityEngine;

public class HouseStatusPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text houseTitleText;
    [SerializeField] private TMP_Text residentsText;

    private House shownHouse;
    private readonly StringBuilder residentsBuilder = new StringBuilder();

    private void Awake()
    {
        Hide();
    }

    private void LateUpdate()
    {
        UpdatePanel();
    }

    public void Show(House house)
    {
        shownHouse = null;

        if (house == null)
            return;

        if (!HasRequiredReferences())
            return;

        shownHouse = house;
        ClearText();
        panelRoot.SetActive(true);
        UpdatePanel(true);
    }

    public void Hide()
    {
        shownHouse = null;

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void UpdatePanel()
    {
        UpdatePanel(false);
    }

    private void UpdatePanel(bool logWrite)
    {
        if (shownHouse == null || !HasRequiredReferences())
            return;

        string residentsTextValue = BuildResidentsText(shownHouse);

        houseTitleText.text = "House";
        residentsText.text = residentsTextValue;

        if (logWrite)
        {
            Debug.Log(
                $"HouseStatusPanelController.Show called. Selected House: {shownHouse.gameObject.name}. Lines written: {shownHouse.SleepingCapacity}. ResidentsText object: {residentsText.gameObject.name}."
            );
        }
    }

    private string BuildResidentsText(House house)
    {
        residentsBuilder.Clear();

        for (int i = 0; i < house.SleepingCapacity; i++)
        {
            if (i > 0)
                residentsBuilder.AppendLine();

            CharacterIdentity resident = GetResidentAtBedIndex(house, i);
            residentsBuilder.Append(resident != null ? resident.FullName : "Empty Bed");
        }

        return residentsBuilder.ToString();
    }

    private void ClearText()
    {
        houseTitleText.text = string.Empty;
        residentsText.text = string.Empty;
    }

    private CharacterIdentity GetResidentAtBedIndex(House house, int bedIndex)
    {
        int occupiedBeds = 0;

        for (int i = 0; i < house.Residents.Count; i++)
        {
            CharacterIdentity resident = house.Residents[i];
            if (resident == null)
                continue;

            if (occupiedBeds == bedIndex)
                return resident;

            occupiedBeds++;
        }

        return null;
    }

    private bool HasRequiredReferences()
    {
        if (panelRoot != null && houseTitleText != null && residentsText != null)
            return true;

        Debug.LogWarning(
            $"House status panel is missing required references. Assign Panel Root, House Title Text, and Residents Text in the Inspector. Script: {nameof(HouseStatusPanelController)}. GameObject: {gameObject.name}."
        );

        return false;
    }
}
