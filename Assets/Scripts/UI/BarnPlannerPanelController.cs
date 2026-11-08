/*
===============================================================================
Script:
BarnPlannerPanelController

Purpose:
Controls the manually built NoAgent barn planner UI and writes shared manual day-plan state.

Folder:
Assets/Scripts/UI/

Scene Hierarchy:
_UI
- ScreenSpaceCanvas
  - BarnInventoryPanel
    - BarnPlannerPanel

Attach To:
BarnPlannerPanel

Required Children:
Existing manually built planner buttons, panels, texts, dropdowns, sliders, and toggles.

Dependencies:
- ManualDayPlanState
- NpcWorkRole
- ResourceStorage
- ResourceRegistry
- HomeRegistry
- ConstructionSite
- TextMeshPro
- Unity UI

Automatically Resolved:
Known child objects under BarnPlannerPanel, when serialized references are not assigned.

Inspector References:
All serialized UI fields may be assigned manually.
===============================================================================
*/

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BarnPlannerPanelController : MonoBehaviour
{
    private const int PriorityCount = 4;

    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Planner Buttons")]
    [SerializeField] private Button worldStateButton;
    [SerializeField] private Button priorityButton;
    [SerializeField] private Button loopFlagButton;
    [SerializeField] private Button strategyButton;
    [SerializeField] private Button quotaButton;
    [SerializeField] private Button closeButton;

    [Header("Subpanels")]
    [SerializeField] private GameObject worldStatePanel;
    [SerializeField] private GameObject priorityPanel;
    [SerializeField] private GameObject loopFlagPanel;
    [SerializeField] private GameObject strategyPanel;
    [SerializeField] private GameObject quotaPanel;

    [Header("Priority")]
    [SerializeField] private TMP_Dropdown priority1Dropdown;
    [SerializeField] private TMP_Dropdown priority2Dropdown;
    [SerializeField] private TMP_Dropdown priority3Dropdown;
    [SerializeField] private TMP_Dropdown priority4Dropdown;

    [Header("Strategy")]
    [SerializeField] private Toggle allowCutEmptyBushToggle;
    [SerializeField] private Toggle npcCollisionActiveToggle;
    [SerializeField] private Button allowCutEmptyBushButton;
    [SerializeField] private Button npcCollisionActiveButton;

    [Header("Loop Flags")]
    [SerializeField] private TMP_Text foodLoopFlagText;
    [SerializeField] private TMP_Text woodLoopFlagText;
    [SerializeField] private TMP_Text exploreLoopFlagText;
    [SerializeField] private TMP_Text buildLoopFlagText;
    [SerializeField] private Button resetLoopFlagsButton;

    [Header("World State")]
    [SerializeField] private TMP_Text foodStoredText;
    [SerializeField] private TMP_Text woodStoredText;
    [SerializeField] private TMP_Text knownTreesText;
    [SerializeField] private TMP_Text knownBerryBushesText;
    [SerializeField] private TMP_Text knownEmptyBushesText;
    [SerializeField] private TMP_Text knownExploreTargetsText;
    [SerializeField] private TMP_Text activeBuildingSitesText;
    [SerializeField] private TMP_Text housingText;

    [Header("Quotas")]
    [SerializeField] private Slider foodQuotaSlider;
    [SerializeField] private TextMeshProUGUI foodQuotaText;
    [SerializeField] private Slider woodQuotaSlider;
    [SerializeField] private TextMeshProUGUI woodQuotaText;
    [SerializeField] private Slider exploreQuotaSlider;
    [SerializeField] private TextMeshProUGUI exploreQuotaText;
    [SerializeField] private Slider buildQuotaSlider;
    [SerializeField] private TextMeshProUGUI buildQuotaText;
    [SerializeField] private TextMeshProUGUI totalQuotaText;

    private readonly List<TMP_Dropdown> priorityDropdowns = new List<TMP_Dropdown>(PriorityCount);
    private bool suppressPriorityEvents;
    private bool suppressStrategyEvents;
    private bool suppressQuotaEvents;
    private bool hasStarted;
    private bool missingVillagerCountWarningLogged;
    private ResourceStorage currentResourceStorage;
    private int currentVillagerCount;

    private void Reset()
    {
        AutoWireReferences();
    }

    private void Awake()
    {
        AutoWireReferences();
        InitializePriorityDropdowns();
        InitializeStrategyToggles();
    }

    private void OnEnable()
    {
        AutoWireReferences();
        RegisterEvents();
        InitializePriorityDropdowns();
        InitializeStrategyToggles();

        if (hasStarted)
            RefreshAll();
    }

    private void Start()
    {
        hasStarted = true;
        RefreshAll();
    }

    private void OnDisable()
    {
        UnregisterEvents();
    }

    private void OnDestroy()
    {
        UnregisterEvents();
    }

    public void SetBarn(ResourceStorage barn)
    {
        currentResourceStorage = barn;
        RefreshWorldState();
    }

    public void OpenPanel()
    {
        AutoWireReferences();

        if (panelRoot == null)
        {
            LogMissingReference(nameof(panelRoot));
            return;
        }

        panelRoot.SetActive(true);
        RefreshAll();
    }

    public void ClosePanel()
    {
        if (panelRoot == null)
        {
            LogMissingReference(nameof(panelRoot));
            return;
        }

        panelRoot.SetActive(false);
    }

    public void TogglePanel()
    {
        AutoWireReferences();

        if (panelRoot == null)
        {
            LogMissingReference(nameof(panelRoot));
            return;
        }

        bool shouldOpen = !panelRoot.activeSelf;
        panelRoot.SetActive(shouldOpen);

        if (shouldOpen)
            RefreshAll();
    }

    public void RefreshAfterVillagerCountChanged()
    {
        AutoWireReferences();
        RefreshQuotas();
        RefreshWorldState();
    }

    private void RegisterEvents()
    {
        if (worldStateButton != null)
        {
            worldStateButton.onClick.RemoveListener(ToggleWorldStatePanel);
            worldStateButton.onClick.AddListener(ToggleWorldStatePanel);
        }
        else
        {
            LogMissingReference(nameof(worldStateButton));
        }

        if (priorityButton != null)
        {
            priorityButton.onClick.RemoveListener(TogglePriorityPanel);
            priorityButton.onClick.AddListener(TogglePriorityPanel);
        }
        else
        {
            LogMissingReference(nameof(priorityButton));
        }

        if (loopFlagButton != null)
        {
            loopFlagButton.onClick.RemoveListener(ToggleLoopFlagPanel);
            loopFlagButton.onClick.AddListener(ToggleLoopFlagPanel);
        }
        else
        {
            LogMissingReference(nameof(loopFlagButton));
        }

        if (strategyButton != null)
        {
            strategyButton.onClick.RemoveListener(ToggleStrategyPanel);
            strategyButton.onClick.AddListener(ToggleStrategyPanel);
        }
        else
        {
            LogMissingReference(nameof(strategyButton));
        }

        if (quotaButton != null)
        {
            quotaButton.onClick.RemoveListener(ToggleQuotaPanel);
            quotaButton.onClick.AddListener(ToggleQuotaPanel);
        }
        else
        {
            LogMissingReference(nameof(quotaButton));
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(ClosePanel);
            closeButton.onClick.AddListener(ClosePanel);
        }
        else
        {
            LogMissingReference(nameof(closeButton));
        }

        RegisterDropdownEvents();
        RegisterToggleEvents();
        RegisterQuotaEvents();
        RegisterManualDayPlanEvents();
        RegisterWorldStateEvents();

        if (resetLoopFlagsButton != null)
        {
            resetLoopFlagsButton.onClick.RemoveListener(ResetLoopFlags);
            resetLoopFlagsButton.onClick.AddListener(ResetLoopFlags);
        }
        else
        {
            LogMissingReference(nameof(resetLoopFlagsButton));
        }
    }

    private void UnregisterEvents()
    {
        if (worldStateButton != null)
            worldStateButton.onClick.RemoveListener(ToggleWorldStatePanel);

        if (priorityButton != null)
            priorityButton.onClick.RemoveListener(TogglePriorityPanel);

        if (loopFlagButton != null)
            loopFlagButton.onClick.RemoveListener(ToggleLoopFlagPanel);

        if (strategyButton != null)
            strategyButton.onClick.RemoveListener(ToggleStrategyPanel);

        if (quotaButton != null)
            quotaButton.onClick.RemoveListener(ToggleQuotaPanel);

        if (closeButton != null)
            closeButton.onClick.RemoveListener(ClosePanel);

        UnregisterDropdownEvents();
        UnregisterToggleEvents();
        UnregisterQuotaEvents();
        UnregisterManualDayPlanEvents();
        UnregisterWorldStateEvents();

        if (resetLoopFlagsButton != null)
            resetLoopFlagsButton.onClick.RemoveListener(ResetLoopFlags);
    }

    private void RegisterManualDayPlanEvents()
    {
        UnregisterManualDayPlanEvents();
        ManualDayPlanState.LoopFlagsChanged += OnManualDayPlanLoopFlagsChanged;
        ManualDayPlanState.RoleAssignmentsChanged += OnManualDayPlanRoleAssignmentsChanged;
        ManualDayPlanState.StrategyFlagsChanged += OnManualDayPlanStrategyFlagsChanged;
    }

    private void UnregisterManualDayPlanEvents()
    {
        ManualDayPlanState.LoopFlagsChanged -= OnManualDayPlanLoopFlagsChanged;
        ManualDayPlanState.RoleAssignmentsChanged -= OnManualDayPlanRoleAssignmentsChanged;
        ManualDayPlanState.StrategyFlagsChanged -= OnManualDayPlanStrategyFlagsChanged;
    }

    private void OnManualDayPlanLoopFlagsChanged()
    {
        RefreshLoopFlags();
    }

    private void OnManualDayPlanRoleAssignmentsChanged()
    {
        RefreshQuotaLabels();
    }

    private void OnManualDayPlanStrategyFlagsChanged()
    {
        InitializeStrategyToggles();
    }

    private void RegisterWorldStateEvents()
    {
        UnregisterWorldStateEvents();
        FogRegistry.FrontierFogChanged += OnFrontierFogChanged;
    }

    private void UnregisterWorldStateEvents()
    {
        FogRegistry.FrontierFogChanged -= OnFrontierFogChanged;
    }

    private void OnFrontierFogChanged()
    {
        RefreshWorldState();
    }

    private void RegisterDropdownEvents()
    {
        if (priority1Dropdown != null)
        {
            priority1Dropdown.onValueChanged.RemoveListener(OnPriority1Changed);
            priority1Dropdown.onValueChanged.AddListener(OnPriority1Changed);
        }

        if (priority2Dropdown != null)
        {
            priority2Dropdown.onValueChanged.RemoveListener(OnPriority2Changed);
            priority2Dropdown.onValueChanged.AddListener(OnPriority2Changed);
        }

        if (priority3Dropdown != null)
        {
            priority3Dropdown.onValueChanged.RemoveListener(OnPriority3Changed);
            priority3Dropdown.onValueChanged.AddListener(OnPriority3Changed);
        }

        if (priority4Dropdown != null)
        {
            priority4Dropdown.onValueChanged.RemoveListener(OnPriority4Changed);
            priority4Dropdown.onValueChanged.AddListener(OnPriority4Changed);
        }
    }

    private void UnregisterDropdownEvents()
    {
        if (priority1Dropdown != null)
            priority1Dropdown.onValueChanged.RemoveListener(OnPriority1Changed);

        if (priority2Dropdown != null)
            priority2Dropdown.onValueChanged.RemoveListener(OnPriority2Changed);

        if (priority3Dropdown != null)
            priority3Dropdown.onValueChanged.RemoveListener(OnPriority3Changed);

        if (priority4Dropdown != null)
            priority4Dropdown.onValueChanged.RemoveListener(OnPriority4Changed);
    }

    private void RegisterToggleEvents()
    {
        if (allowCutEmptyBushToggle != null)
        {
            allowCutEmptyBushToggle.onValueChanged.RemoveListener(OnAllowCutEmptyBushChanged);
            allowCutEmptyBushToggle.onValueChanged.AddListener(OnAllowCutEmptyBushChanged);
        }

        if (npcCollisionActiveToggle != null)
        {
            npcCollisionActiveToggle.onValueChanged.RemoveListener(OnNpcCollisionActiveChanged);
            npcCollisionActiveToggle.onValueChanged.AddListener(OnNpcCollisionActiveChanged);
        }

        if (allowCutEmptyBushButton != null)
        {
            allowCutEmptyBushButton.onClick.RemoveListener(ToggleAllowCutEmptyBush);
            allowCutEmptyBushButton.onClick.AddListener(ToggleAllowCutEmptyBush);
        }

        if (npcCollisionActiveButton != null)
        {
            npcCollisionActiveButton.onClick.RemoveListener(ToggleNpcCollisionActive);
            npcCollisionActiveButton.onClick.AddListener(ToggleNpcCollisionActive);
        }
    }

    private void UnregisterToggleEvents()
    {
        if (allowCutEmptyBushToggle != null)
            allowCutEmptyBushToggle.onValueChanged.RemoveListener(OnAllowCutEmptyBushChanged);

        if (npcCollisionActiveToggle != null)
            npcCollisionActiveToggle.onValueChanged.RemoveListener(OnNpcCollisionActiveChanged);

        if (allowCutEmptyBushButton != null)
            allowCutEmptyBushButton.onClick.RemoveListener(ToggleAllowCutEmptyBush);

        if (npcCollisionActiveButton != null)
            npcCollisionActiveButton.onClick.RemoveListener(ToggleNpcCollisionActive);
    }

    private void RegisterQuotaEvents()
    {
        if (foodQuotaSlider != null)
        {
            foodQuotaSlider.onValueChanged.RemoveListener(OnFoodQuotaChanged);
            foodQuotaSlider.onValueChanged.AddListener(OnFoodQuotaChanged);
        }

        if (woodQuotaSlider != null)
        {
            woodQuotaSlider.onValueChanged.RemoveListener(OnWoodQuotaChanged);
            woodQuotaSlider.onValueChanged.AddListener(OnWoodQuotaChanged);
        }

        if (exploreQuotaSlider != null)
        {
            exploreQuotaSlider.onValueChanged.RemoveListener(OnExploreQuotaChanged);
            exploreQuotaSlider.onValueChanged.AddListener(OnExploreQuotaChanged);
        }

        if (buildQuotaSlider != null)
        {
            buildQuotaSlider.onValueChanged.RemoveListener(OnBuildQuotaChanged);
            buildQuotaSlider.onValueChanged.AddListener(OnBuildQuotaChanged);
        }
    }

    private void UnregisterQuotaEvents()
    {
        if (foodQuotaSlider != null)
            foodQuotaSlider.onValueChanged.RemoveListener(OnFoodQuotaChanged);

        if (woodQuotaSlider != null)
            woodQuotaSlider.onValueChanged.RemoveListener(OnWoodQuotaChanged);

        if (exploreQuotaSlider != null)
            exploreQuotaSlider.onValueChanged.RemoveListener(OnExploreQuotaChanged);

        if (buildQuotaSlider != null)
            buildQuotaSlider.onValueChanged.RemoveListener(OnBuildQuotaChanged);
    }

    private void ToggleWorldStatePanel()
    {
        ToggleSubpanel(worldStatePanel);
        RefreshWorldState();
    }

    private void TogglePriorityPanel()
    {
        ToggleSubpanel(priorityPanel);
    }

    private void ToggleLoopFlagPanel()
    {
        ToggleSubpanel(loopFlagPanel);
        RefreshLoopFlags();
    }

    private void ToggleStrategyPanel()
    {
        ToggleSubpanel(strategyPanel);
        InitializeStrategyToggles();
    }

    private void ToggleQuotaPanel()
    {
        ToggleSubpanel(quotaPanel);
        RefreshQuotas();
    }

    private void ToggleSubpanel(GameObject subpanel)
    {
        if (subpanel == null)
        {
            LogMissingReference(nameof(subpanel));
            return;
        }

        subpanel.SetActive(!subpanel.activeSelf);
    }

    private void OnPriority1Changed(int value)
    {
        HandlePriorityChanged(0, value);
    }

    private void OnPriority2Changed(int value)
    {
        HandlePriorityChanged(1, value);
    }

    private void OnPriority3Changed(int value)
    {
        HandlePriorityChanged(2, value);
    }

    private void OnPriority4Changed(int value)
    {
        HandlePriorityChanged(3, value);
    }

    private void HandlePriorityChanged(int changedIndex, int value)
    {
        if (suppressPriorityEvents)
            return;

        RefreshPriorityDropdownList();

        if (priorityDropdowns.Count < PriorityCount)
            return;

        NpcWorkRole[] selectedRoles = ReadDropdownRoles();
        selectedRoles[changedIndex] = RoleFromDropdownValue(value);
        NormalizePriorityRoles(selectedRoles, changedIndex);
        ApplyPriorityRoles(selectedRoles);
        ManualDayPlanState.SetPriorities(selectedRoles);
    }

    private void OnAllowCutEmptyBushChanged(bool value)
    {
        WriteStrategyFlags();
    }

    private void OnNpcCollisionActiveChanged(bool value)
    {
        WriteStrategyFlags();
    }

    private void ToggleAllowCutEmptyBush()
    {
        ManualDayPlanState.SetStrategyFlags(
            !ManualDayPlanState.AllowCutEmptyBushes,
            ManualDayPlanState.NpcCollisionActive
        );
    }

    private void ToggleNpcCollisionActive()
    {
        ManualDayPlanState.SetStrategyFlags(
            ManualDayPlanState.AllowCutEmptyBushes,
            !ManualDayPlanState.NpcCollisionActive
        );
    }

    private void OnFoodQuotaChanged(float value)
    {
        HandleQuotaChanged(NpcWorkRole.Food, Mathf.RoundToInt(value));
    }

    private void OnWoodQuotaChanged(float value)
    {
        HandleQuotaChanged(NpcWorkRole.Wood, Mathf.RoundToInt(value));
    }

    private void OnExploreQuotaChanged(float value)
    {
        HandleQuotaChanged(NpcWorkRole.Explore, Mathf.RoundToInt(value));
    }

    private void OnBuildQuotaChanged(float value)
    {
        HandleQuotaChanged(NpcWorkRole.Build, Mathf.RoundToInt(value));
    }

    private void WriteStrategyFlags()
    {
        if (suppressStrategyEvents)
            return;

        ManualDayPlanState.SetStrategyFlags(
            allowCutEmptyBushToggle != null ? allowCutEmptyBushToggle.isOn : ManualDayPlanState.AllowCutEmptyBushes,
            npcCollisionActiveToggle != null ? npcCollisionActiveToggle.isOn : ManualDayPlanState.NpcCollisionActive
        );
    }

    private void ResetLoopFlags()
    {
        ManualDayPlanState.ClearLoopFlags();
        RefreshLoopFlags();
    }

    private void RefreshAll()
    {
        InitializePriorityDropdowns();
        InitializeStrategyToggles();
        RefreshLoopFlags();
        RefreshQuotas();
        RefreshWorldState();
    }

    private void InitializePriorityDropdowns()
    {
        RefreshPriorityDropdownList();
        EnsurePriorityOptions();

        IReadOnlyList<NpcWorkRole> priorities = ManualDayPlanState.GetPriorities();
        NpcWorkRole[] selectedRoles =
        {
            GetPriorityOrDefault(priorities, 0, NpcWorkRole.Food),
            GetPriorityOrDefault(priorities, 1, NpcWorkRole.Wood),
            GetPriorityOrDefault(priorities, 2, NpcWorkRole.Explore),
            GetPriorityOrDefault(priorities, 3, NpcWorkRole.Build)
        };

        ApplyPriorityRoles(selectedRoles);
        ManualDayPlanState.SetPriorities(selectedRoles);
    }

    private void RefreshPriorityDropdownList()
    {
        priorityDropdowns.Clear();
        AddPriorityDropdown(priority1Dropdown, nameof(priority1Dropdown));
        AddPriorityDropdown(priority2Dropdown, nameof(priority2Dropdown));
        AddPriorityDropdown(priority3Dropdown, nameof(priority3Dropdown));
        AddPriorityDropdown(priority4Dropdown, nameof(priority4Dropdown));
    }

    private void AddPriorityDropdown(TMP_Dropdown dropdown, string fieldName)
    {
        if (dropdown == null)
        {
            LogMissingReference(fieldName);
            return;
        }

        priorityDropdowns.Add(dropdown);
    }

    private void EnsurePriorityOptions()
    {
        for (int i = 0; i < priorityDropdowns.Count; i++)
        {
            TMP_Dropdown dropdown = priorityDropdowns[i];
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>
            {
                RoleToDisplayName(NpcWorkRole.Food),
                RoleToDisplayName(NpcWorkRole.Wood),
                RoleToDisplayName(NpcWorkRole.Explore),
                RoleToDisplayName(NpcWorkRole.Build)
            });
        }
    }

    private void ApplyPriorityRoles(IReadOnlyList<NpcWorkRole> selectedRoles)
    {
        suppressPriorityEvents = true;

        for (int i = 0; i < priorityDropdowns.Count && i < selectedRoles.Count; i++)
            priorityDropdowns[i].SetValueWithoutNotify(DropdownValueFromRole(selectedRoles[i]));

        suppressPriorityEvents = false;
    }

    private NpcWorkRole[] ReadDropdownRoles()
    {
        NpcWorkRole[] roles =
        {
            NpcWorkRole.Food,
            NpcWorkRole.Wood,
            NpcWorkRole.Explore,
            NpcWorkRole.Build
        };

        for (int i = 0; i < priorityDropdowns.Count && i < roles.Length; i++)
            roles[i] = RoleFromDropdownValue(priorityDropdowns[i].value);

        return roles;
    }

    private void NormalizePriorityRoles(NpcWorkRole[] roles, int changedIndex)
    {
        bool[] usedRoles = new bool[PriorityCount];
        NpcWorkRole changedRole = roles[Mathf.Clamp(changedIndex, 0, roles.Length - 1)];
        roles[changedIndex] = changedRole;
        usedRoles[DropdownValueFromRole(changedRole)] = true;

        for (int i = 0; i < roles.Length; i++)
        {
            if (i == changedIndex)
                continue;

            int roleIndex = DropdownValueFromRole(roles[i]);
            if (!usedRoles[roleIndex])
            {
                usedRoles[roleIndex] = true;
                continue;
            }

            roles[i] = GetFirstMissingRole(usedRoles);
            usedRoles[DropdownValueFromRole(roles[i])] = true;
        }
    }

    private NpcWorkRole GetFirstMissingRole(bool[] usedRoles)
    {
        if (!usedRoles[0])
            return NpcWorkRole.Food;

        if (!usedRoles[1])
            return NpcWorkRole.Wood;

        if (!usedRoles[2])
            return NpcWorkRole.Explore;

        return NpcWorkRole.Build;
    }

    private void InitializeStrategyToggles()
    {
        suppressStrategyEvents = true;

        if (allowCutEmptyBushToggle != null)
            allowCutEmptyBushToggle.SetIsOnWithoutNotify(ManualDayPlanState.AllowCutEmptyBushes);
        else if (allowCutEmptyBushButton == null)
            LogMissingReference(nameof(allowCutEmptyBushToggle));

        if (npcCollisionActiveToggle != null)
        {
            npcCollisionActiveToggle.SetIsOnWithoutNotify(ManualDayPlanState.NpcCollisionActive);
            SetToggleLabel(npcCollisionActiveToggle, "NPC Collision Active");
        }
        else if (npcCollisionActiveButton == null)
        {
            LogMissingReference(nameof(npcCollisionActiveToggle));
        }

        RefreshStrategyButtons();
        suppressStrategyEvents = false;
    }

    private void RefreshStrategyButtons()
    {
        SetStrategyButtonText(allowCutEmptyBushButton, "Allow Cut Empty Bush", ManualDayPlanState.AllowCutEmptyBushes);
        SetStrategyButtonText(npcCollisionActiveButton, "NPC Collision Active", ManualDayPlanState.NpcCollisionActive);
    }

    private void SetStrategyButtonText(Button button, string label, bool isEnabled)
    {
        if (button == null)
            return;

        TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>(true);
        if (buttonText != null)
            buttonText.text = $"{label}: {(isEnabled ? "On" : "Off")}";
    }

    private void SetToggleLabel(Toggle toggle, string label)
    {
        if (toggle == null)
            return;

        TMP_Text labelText = FindChildComponentIn<TMP_Text>(toggle.transform, "Label");
        if (labelText != null)
            labelText.text = label;
    }

    private void RefreshLoopFlags()
    {
        SetLoopFlagText(foodLoopFlagText, NpcWorkRole.Food);
        SetLoopFlagText(woodLoopFlagText, NpcWorkRole.Wood);
        SetLoopFlagText(exploreLoopFlagText, NpcWorkRole.Explore);
        SetLoopFlagText(buildLoopFlagText, NpcWorkRole.Build);
    }

    private void SetLoopFlagText(TMP_Text text, NpcWorkRole role)
    {
        if (text == null)
        {
            LogMissingReference($"{role}LoopFlagText");
            return;
        }

        text.text = $"{RoleToDisplayName(role)}: {(ManualDayPlanState.HasLoopFlag(role) ? "Flagged" : "Clear")}";
    }

    private void RefreshWorldState()
    {
        int foodStored = currentResourceStorage != null ? currentResourceStorage.BerriesStored : 0;
        int woodStored = currentResourceStorage != null ? currentResourceStorage.WoodStored : 0;

        ResourceRegistry.GetKnownResourceCounts(out int knownTrees, out int knownBerryBushes, out int knownEmptyBushes);
        HomeRegistry.GetHousingStatus(out int occupiedBeds, out int totalBeds);

        SetText(foodStoredText, $"Food: {foodStored}", nameof(foodStoredText));
        SetText(woodStoredText, $"Wood: {woodStored}", nameof(woodStoredText));
        SetText(knownTreesText, $"Trees: {knownTrees}", nameof(knownTreesText));
        SetText(knownBerryBushesText, $"Berry Bushes: {knownBerryBushes}", nameof(knownBerryBushesText));
        SetText(knownEmptyBushesText, $"Empty Bushes: {knownEmptyBushes}", nameof(knownEmptyBushesText));
        SetText(knownExploreTargetsText, $"Fog: {FogRegistry.CountReachableFrontierFog()}", nameof(knownExploreTargetsText));
        SetText(activeBuildingSitesText, $"Constructions: {CountActiveConstructionSites()}", nameof(activeBuildingSitesText));
        SetText(housingText, $"Beds: {occupiedBeds}/{totalBeds}", nameof(housingText));
    }

    private void RefreshQuotas()
    {
        currentVillagerCount = GetCurrentVillagerCount();
        ManualDayPlanState.EnsureDefaultQuotas(currentVillagerCount);
        ManualDayPlanState.ClampTotalQuota(currentVillagerCount);

        ConfigureQuotaSlider(foodQuotaSlider);
        ConfigureQuotaSlider(woodQuotaSlider);
        ConfigureQuotaSlider(exploreQuotaSlider);
        ConfigureQuotaSlider(buildQuotaSlider);
        ApplyQuotaSliderValues();
        RefreshQuotaLabels();
    }

    private void ConfigureQuotaSlider(Slider slider)
    {
        if (slider == null)
            return;

        slider.minValue = 0f;
        slider.maxValue = currentVillagerCount;
        slider.wholeNumbers = true;
    }

    private void ApplyQuotaSliderValues()
    {
        suppressQuotaEvents = true;

        try
        {
            SetQuotaSliderValue(foodQuotaSlider, ManualDayPlanState.GetQuota(NpcWorkRole.Food));
            SetQuotaSliderValue(woodQuotaSlider, ManualDayPlanState.GetQuota(NpcWorkRole.Wood));
            SetQuotaSliderValue(exploreQuotaSlider, ManualDayPlanState.GetQuota(NpcWorkRole.Explore));
            SetQuotaSliderValue(buildQuotaSlider, ManualDayPlanState.GetQuota(NpcWorkRole.Build));
        }
        finally
        {
            suppressQuotaEvents = false;
        }
    }

    private void SetQuotaSliderValue(Slider slider, int value)
    {
        if (slider == null)
            return;

        slider.SetValueWithoutNotify(Mathf.Clamp(value, 0, currentVillagerCount));
    }

    private void HandleQuotaChanged(NpcWorkRole changedRole, int requestedValue)
    {
        if (suppressQuotaEvents)
            return;

        currentVillagerCount = GetCurrentVillagerCount();
        int otherAssigned = ManualDayPlanState.GetTotalAssignedQuota() - ManualDayPlanState.GetQuota(changedRole);
        int maxAllowedForChangedRole = Mathf.Max(0, currentVillagerCount - otherAssigned);
        int clampedValue = Mathf.Clamp(requestedValue, 0, maxAllowedForChangedRole);

        ManualDayPlanState.SetQuota(changedRole, clampedValue);

        suppressQuotaEvents = true;
        try
        {
            SetQuotaSliderValue(GetQuotaSlider(changedRole), clampedValue);
        }
        finally
        {
            suppressQuotaEvents = false;
        }

        RefreshQuotaLabels();
    }

    private Slider GetQuotaSlider(NpcWorkRole role)
    {
        switch (role)
        {
            case NpcWorkRole.Food:
                return foodQuotaSlider;
            case NpcWorkRole.Wood:
                return woodQuotaSlider;
            case NpcWorkRole.Explore:
                return exploreQuotaSlider;
            case NpcWorkRole.Build:
                return buildQuotaSlider;
            default:
                return null;
        }
    }

    private void RefreshQuotaLabels()
    {
        currentVillagerCount = GetCurrentVillagerCount();
        SetQuotaText(foodQuotaText, NpcWorkRole.Food, nameof(foodQuotaText));
        SetQuotaText(woodQuotaText, NpcWorkRole.Wood, nameof(woodQuotaText));
        SetQuotaText(exploreQuotaText, NpcWorkRole.Explore, nameof(exploreQuotaText));
        SetQuotaText(buildQuotaText, NpcWorkRole.Build, nameof(buildQuotaText));
        SetText(totalQuotaText, $"Working: {NpcWorkRoleAssignmentRegistry.GetTotalAssignedCount()}/{currentVillagerCount}", nameof(totalQuotaText));
    }

    private void SetQuotaText(TMP_Text text, NpcWorkRole role, string fieldName)
    {
        int workingCount = NpcWorkRoleAssignmentRegistry.GetAssignedCount(role);
        int quota = ManualDayPlanState.GetQuota(role);
        SetText(text, $"{RoleToDisplayName(role)}: {workingCount}/{quota}", fieldName);
    }

    private int GetCurrentVillagerCount()
    {
        CharacterIdentity[] villagers = FindObjectsByType<CharacterIdentity>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        if (villagers == null)
        {
            if (!missingVillagerCountWarningLogged)
            {
                Debug.LogWarning("BarnPlannerPanelController could not read villager count from CharacterIdentity objects. Quotas will use 0.");
                missingVillagerCountWarningLogged = true;
            }

            return 0;
        }

        return villagers.Length;
    }

    private int CountActiveConstructionSites()
    {
        ConstructionSite[] constructionSites = FindObjectsByType<ConstructionSite>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        return constructionSites.Length;
    }

    private void SetText(TMP_Text text, string value, string fieldName)
    {
        if (text == null)
        {
            LogMissingReference(fieldName);
            return;
        }

        text.text = value;
    }

    [ContextMenu("Auto Wire References")]
    private void AutoWireReferences()
    {
        if (panelRoot == null)
            panelRoot = gameObject;

        if (worldStateButton == null)
            worldStateButton = FindChildComponent<Button>("WorldStateButton");

        if (priorityButton == null)
            priorityButton = FindChildComponent<Button>("PriorityButton");

        if (loopFlagButton == null)
            loopFlagButton = FindChildComponent<Button>("LoopFlagButton");

        if (strategyButton == null)
            strategyButton = FindChildComponent<Button>("StrategyButton");

        if (quotaButton == null)
            quotaButton = FindChildComponent<Button>("QuotaButton");

        if (closeButton == null)
            closeButton = FindChildComponent<Button>("CloseButton");

        if (worldStatePanel == null)
            worldStatePanel = FindChildGameObject("WorldStatePanel");

        if (priorityPanel == null)
            priorityPanel = FindChildGameObject("PriorityPanel");

        if (loopFlagPanel == null)
            loopFlagPanel = FindChildGameObject("LoopFlagPanel");

        if (strategyPanel == null)
            strategyPanel = FindChildGameObject("StrategyPanel");

        if (quotaPanel == null)
            quotaPanel = FindChildGameObject("QuotaPanel");

        AutoWirePriorityReferences();
        AutoWireStrategyReferences();
        AutoWireLoopFlagReferences();
        AutoWireWorldStateReferences();
        AutoWireQuotaReferences();
    }

    private void AutoWirePriorityReferences()
    {
        Transform root = priorityPanel != null ? priorityPanel.transform : transform;

        if (priority1Dropdown == null)
            priority1Dropdown = FindChildComponentIn<TMP_Dropdown>(root, "Priority1Dropdown");

        if (priority2Dropdown == null)
            priority2Dropdown = FindChildComponentIn<TMP_Dropdown>(root, "Priority2Dropdown");

        if (priority3Dropdown == null)
            priority3Dropdown = FindChildComponentIn<TMP_Dropdown>(root, "Priority3Dropdown");

        if (priority4Dropdown == null)
            priority4Dropdown = FindChildComponentIn<TMP_Dropdown>(root, "Priority4Dropdown");
    }

    private void AutoWireStrategyReferences()
    {
        Transform root = strategyPanel != null ? strategyPanel.transform : transform;

        if (allowCutEmptyBushToggle == null)
            allowCutEmptyBushToggle = FindChildComponentIn<Toggle>(root, "AllowCutEmptyBushToggle");

        if (npcCollisionActiveToggle == null)
            npcCollisionActiveToggle = FindChildComponentIn<Toggle>(root, "NpcCollisionActive");

        if (allowCutEmptyBushButton == null)
            allowCutEmptyBushButton = FindFirstStrategyButton(root, "AllowCutEmptyBushButton", "AllowCutEmptyBushesButton", "AllowCutEmptyBushToggle");

        if (npcCollisionActiveButton == null)
            npcCollisionActiveButton = FindFirstStrategyButton(root, "NpcCollisionActiveButton", "NpcCollisionActive");
    }

    private Button FindFirstStrategyButton(Transform root, params string[] childNames)
    {
        for (int i = 0; i < childNames.Length; i++)
        {
            Button button = FindChildComponentIn<Button>(root, childNames[i]);
            if (button != null)
                return button;
        }

        return null;
    }

    private void AutoWireLoopFlagReferences()
    {
        Transform root = loopFlagPanel != null ? loopFlagPanel.transform : transform;

        if (foodLoopFlagText == null)
            foodLoopFlagText = FindChildComponentIn<TMP_Text>(root, "FoodLoopFlagText");

        if (woodLoopFlagText == null)
            woodLoopFlagText = FindChildComponentIn<TMP_Text>(root, "WoodLoopFlagText");

        if (exploreLoopFlagText == null)
            exploreLoopFlagText = FindChildComponentIn<TMP_Text>(root, "ExploreLoopFlagText");

        if (buildLoopFlagText == null)
            buildLoopFlagText = FindChildComponentIn<TMP_Text>(root, "BuildLoopFlagText");

        if (resetLoopFlagsButton == null)
            resetLoopFlagsButton = FindChildComponentIn<Button>(root, "ResetLoopFlagsButton");
    }

    private void AutoWireWorldStateReferences()
    {
        Transform root = worldStatePanel != null ? worldStatePanel.transform : transform;

        if (foodStoredText == null)
            foodStoredText = FindChildComponentIn<TMP_Text>(root, "FoodStoredText");

        if (woodStoredText == null)
            woodStoredText = FindChildComponentIn<TMP_Text>(root, "WoodStoredText");

        if (knownTreesText == null)
            knownTreesText = FindChildComponentIn<TMP_Text>(root, "KnownTreesText");

        if (knownBerryBushesText == null)
            knownBerryBushesText = FindChildComponentIn<TMP_Text>(root, "KnownBerryBushesText");

        if (knownEmptyBushesText == null)
            knownEmptyBushesText = FindChildComponentIn<TMP_Text>(root, "KnownEmptyBushesText");

        if (knownEmptyBushesText == null)
            knownEmptyBushesText = FindChildComponentIn<TMP_Text>(root, "KnownEmptyBushesText (1)");

        if (knownExploreTargetsText == null)
            knownExploreTargetsText = FindChildComponentIn<TMP_Text>(root, "KnownExploreTargetsText");

        if (activeBuildingSitesText == null)
            activeBuildingSitesText = FindChildComponentIn<TMP_Text>(root, "ActiveBuildingSitesText");

        if (housingText == null)
            housingText = FindChildComponentIn<TMP_Text>(root, "HousingText");
    }

    private void AutoWireQuotaReferences()
    {
        Transform root = quotaPanel != null ? quotaPanel.transform : transform;

        if (foodQuotaSlider == null)
            foodQuotaSlider = FindChildComponentIn<Slider>(root, "FoodQuotaSlider");

        if (foodQuotaText == null)
            foodQuotaText = FindChildComponentIn<TextMeshProUGUI>(root, "FoodQuotaText");

        if (woodQuotaSlider == null)
            woodQuotaSlider = FindChildComponentIn<Slider>(root, "WoodQuotaSlider");

        if (woodQuotaText == null)
            woodQuotaText = FindChildComponentIn<TextMeshProUGUI>(root, "WoodQuotaText");

        if (exploreQuotaSlider == null)
            exploreQuotaSlider = FindChildComponentIn<Slider>(root, "ExploreQuotaSlider");

        if (exploreQuotaText == null)
            exploreQuotaText = FindChildComponentIn<TextMeshProUGUI>(root, "ExploreQuotaText");

        if (buildQuotaSlider == null)
            buildQuotaSlider = FindChildComponentIn<Slider>(root, "BuildQuotaSlider");

        if (buildQuotaText == null)
            buildQuotaText = FindChildComponentIn<TextMeshProUGUI>(root, "BuildQuotaText");

        if (totalQuotaText == null)
            totalQuotaText = FindChildComponentIn<TextMeshProUGUI>(root, "TotalQuotaText");
    }

    private GameObject FindChildGameObject(string childName)
    {
        Transform child = FindChild(transform, childName);
        return child != null ? child.gameObject : null;
    }

    private T FindChildComponent<T>(string childName) where T : Component
    {
        return FindChildComponentIn<T>(transform, childName);
    }

    private T FindChildComponentIn<T>(Transform root, string childName) where T : Component
    {
        Transform child = FindChild(root, childName);
        return child != null ? child.GetComponent<T>() : null;
    }

    private Transform FindChild(Transform root, string childName)
    {
        if (root == null)
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child.name == childName)
                return child;
        }

        return null;
    }

    private NpcWorkRole GetPriorityOrDefault(IReadOnlyList<NpcWorkRole> priorities, int index, NpcWorkRole fallback)
    {
        if (priorities == null || index < 0 || index >= priorities.Count)
            return fallback;

        return priorities[index];
    }

    private NpcWorkRole RoleFromDropdownValue(int value)
    {
        switch (value)
        {
            case 0:
                return NpcWorkRole.Food;
            case 1:
                return NpcWorkRole.Wood;
            case 2:
                return NpcWorkRole.Explore;
            case 3:
                return NpcWorkRole.Build;
            default:
                return NpcWorkRole.Food;
        }
    }

    private int DropdownValueFromRole(NpcWorkRole role)
    {
        switch (role)
        {
            case NpcWorkRole.Food:
                return 0;
            case NpcWorkRole.Wood:
                return 1;
            case NpcWorkRole.Explore:
                return 2;
            case NpcWorkRole.Build:
                return 3;
            default:
                return 0;
        }
    }

    private string RoleToDisplayName(NpcWorkRole role)
    {
        switch (role)
        {
            case NpcWorkRole.Food:
                return "Food";
            case NpcWorkRole.Wood:
                return "Wood";
            case NpcWorkRole.Explore:
                return "Explore";
            case NpcWorkRole.Build:
                return "Build";
            default:
                return "Food";
        }
    }

    private void LogMissingReference(string referenceName)
    {
        Debug.LogWarning($"BarnPlannerPanelController missing reference: {referenceName}. Assign it in the Inspector or keep the expected child name. GameObject: {gameObject.name}.");
    }
}
