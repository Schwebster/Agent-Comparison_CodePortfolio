/*
===============================================================================
Script:
StartMenuController

Purpose:
Controls the manually created StartScene menu settings and launch buttons.

Folder:
Assets/Scripts/UI/

Scene Hierarchy:
ScreenSpaceCanvas
- StartMenuPanel
  - SettingsPanel
    - WorldGeneration
    - GameSettings

Attach To:
StartMenuPanel

Required Children:
- SeedLabelText, SeedInputField
- WorldSizeLabelText, WorldSizeInputField
- ResourceDensityWoodLabelText, ResourceDensityWoodInputField
- ResourceDensityFoodLabelText, ResourceDensityFoodInputField
- ResourceDensityEmptyLabelText, ResourceDensityEmptyInputField
- NpcCountLabelText, NpcCountInputField
- BarnStartingFoodLabelText, BarnStartingFoodInputField
- BarnStartingWoodLabelText, BarnStartingWoodInputField
- DayDurationLabelText, DayDurationInputField
- SimulationSpeedLabelText, SimulationSpeedInputField
- GenerateNoAgentWorldButton
- GenerateFsmAgentWorldButton
- GenerateGoapAgentWorldButton
- EndGameButton
- ErrorText

Dependencies:
- TextMeshPro
- Unity UI Button
- SceneManager

Automatically Resolved:
- Existing child/scene objects by the names listed above
- SimulationBalanceConfig asset in the Unity Editor when the Inspector reference is not assigned
===============================================================================
*/

using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class StartMenuController : MonoBehaviour
{
    private const int FallbackSeed = 2026;
    private const int FallbackWorldWidth = 40;
    private const int FallbackWorldHeight = 40;
    private const int FallbackWoodDensity = 35;
    private const int FallbackBerriesDensity = 35;
    private const int FallbackEmptyDensity = 40;
    private const int FallbackNpcCount = 3;
    private const int FallbackBarnStartingFood = 300;
    private const int FallbackBarnStartingWood = 10;
    private const float FallbackDayDurationSeconds = 120f;
    private const float FallbackSimulationSpeedSeconds = 120f;
    private const int RequiredResourceDensityTotal = 100;

    [Header("Balance Defaults")]
    [SerializeField] private SimulationBalanceConfig simulationBalanceConfig;

    [Header("Target Scenes")]
    [SerializeField] private string noAgentSceneName = "NoAgent";
    [SerializeField] private string fsmAgentSceneName = "FsmAgent";
    [SerializeField] private string goapAgentSceneName = "GoapAgent";

    [Header("World Generation")]
    [SerializeField] private TMP_Text seedLabelText;
    [SerializeField] private TMP_InputField seedInputField;
    [SerializeField] private TMP_Text worldSizeLabelText;
    [SerializeField] private TMP_InputField worldSizeInputField;
    [SerializeField] private TMP_Text resourceDensityWoodLabelText;
    [SerializeField] private TMP_InputField resourceDensityWoodInputField;
    [SerializeField] private TMP_Text resourceDensityFoodLabelText;
    [SerializeField] private TMP_InputField resourceDensityFoodInputField;
    [SerializeField] private TMP_Text resourceDensityEmptyLabelText;
    [SerializeField] private TMP_InputField resourceDensityEmptyInputField;
    [SerializeField] private TMP_Text npcCountLabelText;
    [SerializeField] private TMP_InputField npcCountInputField;
    [SerializeField] private TMP_Text barnStartingFoodLabelText;
    [SerializeField] private TMP_InputField barnStartingFoodInputField;
    [SerializeField] private TMP_Text barnStartingWoodLabelText;
    [SerializeField] private TMP_InputField barnStartingWoodInputField;

    [Header("Game Settings")]
    [SerializeField] private TMP_Text dayDurationLabelText;
    [SerializeField] private TMP_InputField dayDurationInputField;
    [SerializeField] private TMP_Text simulationSpeedLabelText;
    [SerializeField] private TMP_InputField simulationSpeedInputField;

    [Header("Buttons")]
    [SerializeField] private Button generateNoAgentWorldButton;
    [SerializeField] private Button generateFsmAgentWorldButton;
    [SerializeField] private Button generateGoapAgentWorldButton;
    [SerializeField] private Button endGameButton;

    [Header("Validation")]
    [SerializeField] private TMP_Text errorText;

    private int seed = FallbackSeed;
    private int worldWidth = FallbackWorldWidth;
    private int worldHeight = FallbackWorldHeight;
    private int woodDensity = FallbackWoodDensity;
    private int berriesDensity = FallbackBerriesDensity;
    private int emptyDensity = FallbackEmptyDensity;
    private int npcCount = FallbackNpcCount;
    private int barnStartingFood = FallbackBarnStartingFood;
    private int barnStartingWood = FallbackBarnStartingWood;
    private float dayDurationSeconds = FallbackDayDurationSeconds;
    private float simulationSpeedSeconds = FallbackSimulationSpeedSeconds;

    private void Reset()
    {
        AutoWireReferences();
    }

    private void Awake()
    {
        AutoWireReferences();
        ApplyDefaultSettings();
        RegisterEvents();
        RefreshAllLabels();
        ClearAllInputs();
        HideError();
    }

    private void OnDestroy()
    {
        UnregisterEvents();
    }

    private void RegisterEvents()
    {
        RegisterInput(seedInputField, ApplySeedInput);
        RegisterInput(worldSizeInputField, ApplyWorldSizeInput);
        RegisterInput(resourceDensityWoodInputField, ApplyWoodDensityInput);
        RegisterInput(resourceDensityFoodInputField, ApplyBerriesDensityInput);
        RegisterInput(resourceDensityEmptyInputField, ApplyEmptyDensityInput);
        RegisterInput(npcCountInputField, ApplyNpcCountInput);
        RegisterInput(barnStartingFoodInputField, ApplyBarnStartingFoodInput);
        RegisterInput(barnStartingWoodInputField, ApplyBarnStartingWoodInput);
        RegisterInput(dayDurationInputField, ApplyDayDurationInput);
        RegisterInput(simulationSpeedInputField, ApplySimulationSpeedInput);

        RegisterButton(generateNoAgentWorldButton, LoadNoAgentScene);
        RegisterButton(generateFsmAgentWorldButton, LoadFsmAgentScene);
        RegisterButton(generateGoapAgentWorldButton, LoadGoapAgentScene);
        RegisterButton(endGameButton, QuitGame);
    }

    private void UnregisterEvents()
    {
        UnregisterInput(seedInputField, ApplySeedInput);
        UnregisterInput(worldSizeInputField, ApplyWorldSizeInput);
        UnregisterInput(resourceDensityWoodInputField, ApplyWoodDensityInput);
        UnregisterInput(resourceDensityFoodInputField, ApplyBerriesDensityInput);
        UnregisterInput(resourceDensityEmptyInputField, ApplyEmptyDensityInput);
        UnregisterInput(npcCountInputField, ApplyNpcCountInput);
        UnregisterInput(barnStartingFoodInputField, ApplyBarnStartingFoodInput);
        UnregisterInput(barnStartingWoodInputField, ApplyBarnStartingWoodInput);
        UnregisterInput(dayDurationInputField, ApplyDayDurationInput);
        UnregisterInput(simulationSpeedInputField, ApplySimulationSpeedInput);

        UnregisterButton(generateNoAgentWorldButton, LoadNoAgentScene);
        UnregisterButton(generateFsmAgentWorldButton, LoadFsmAgentScene);
        UnregisterButton(generateGoapAgentWorldButton, LoadGoapAgentScene);
        UnregisterButton(endGameButton, QuitGame);
    }

    private void RegisterInput(TMP_InputField inputField, UnityEngine.Events.UnityAction<string> action)
    {
        if (inputField == null)
            return;

        inputField.onEndEdit.RemoveListener(action);
        inputField.onEndEdit.AddListener(action);
    }

    private void UnregisterInput(TMP_InputField inputField, UnityEngine.Events.UnityAction<string> action)
    {
        if (inputField != null)
            inputField.onEndEdit.RemoveListener(action);
    }

    private void RegisterButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private void UnregisterButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.RemoveListener(action);
    }

    private void ApplySeedInput(string inputValue)
    {
        if (TryParseNonNegativeInt(inputValue, out int parsed))
            seed = parsed;

        RefreshSeed();
        ClearInput(seedInputField);
    }

    private void ApplyWorldSizeInput(string inputValue)
    {
        if (TryParseWorldSize(inputValue, out int width, out int height))
        {
            worldWidth = width;
            worldHeight = height;
        }

        RefreshWorldSize();
        ClearInput(worldSizeInputField);
    }

    private void ApplyWoodDensityInput(string inputValue)
    {
        if (TryParsePercentage(inputValue, out int parsed))
            woodDensity = parsed;

        RefreshWoodDensity();
        ClearInput(resourceDensityWoodInputField);
    }

    private void ApplyBerriesDensityInput(string inputValue)
    {
        if (TryParsePercentage(inputValue, out int parsed))
            berriesDensity = parsed;

        RefreshBerriesDensity();
        ClearInput(resourceDensityFoodInputField);
    }

    private void ApplyEmptyDensityInput(string inputValue)
    {
        if (TryParsePercentage(inputValue, out int parsed))
            emptyDensity = parsed;

        RefreshEmptyDensity();
        ClearInput(resourceDensityEmptyInputField);
    }

    private void ApplyNpcCountInput(string inputValue)
    {
        if (TryParseNonNegativeInt(inputValue, out int parsed))
            npcCount = parsed;

        RefreshNpcCount();
        ClearInput(npcCountInputField);
    }

    private void ApplyBarnStartingFoodInput(string inputValue)
    {
        if (TryParseNonNegativeInt(inputValue, out int parsed))
            barnStartingFood = parsed;

        RefreshBarnStartingFood();
        ClearInput(barnStartingFoodInputField);
    }

    private void ApplyBarnStartingWoodInput(string inputValue)
    {
        if (TryParseNonNegativeInt(inputValue, out int parsed))
            barnStartingWood = parsed;

        RefreshBarnStartingWood();
        ClearInput(barnStartingWoodInputField);
    }

    private void ApplyDayDurationInput(string inputValue)
    {
        if (TryParsePositiveFloat(inputValue, out float parsed))
            dayDurationSeconds = parsed;

        RefreshDayDuration();
        ClearInput(dayDurationInputField);
    }

    private void ApplySimulationSpeedInput(string inputValue)
    {
        if (TryParsePositiveFloat(inputValue, out float parsed))
            simulationSpeedSeconds = parsed;

        RefreshSimulationSpeed();
        ClearInput(simulationSpeedInputField);
    }

    private bool TryParseNonNegativeInt(string inputValue, out int parsed)
    {
        return int.TryParse(inputValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) &&
            parsed >= 0;
    }

    private bool TryParsePositiveFloat(string inputValue, out float parsed)
    {
        return float.TryParse(inputValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
            parsed > 0f;
    }

    private bool TryParsePercentage(string inputValue, out int parsed)
    {
        return TryParseNonNegativeInt(inputValue, out parsed) && parsed <= 100;
    }

    private bool TryParseWorldSize(string inputValue, out int width, out int height)
    {
        width = worldWidth;
        height = worldHeight;

        if (string.IsNullOrWhiteSpace(inputValue))
            return false;

        string value = inputValue.Trim();
        string[] parts = value.Split('x', 'X');

        if (parts.Length == 1)
        {
            if (!TryParseNonNegativeInt(parts[0], out int squareSize) || squareSize <= 0)
                return false;

            width = squareSize;
            height = squareSize;
            return true;
        }

        if (parts.Length != 2)
            return false;

        if (!TryParseNonNegativeInt(parts[0], out int parsedWidth) || parsedWidth <= 0)
            return false;

        if (!TryParseNonNegativeInt(parts[1], out int parsedHeight) || parsedHeight <= 0)
            return false;

        width = parsedWidth;
        height = parsedHeight;
        return true;
    }

    private void RefreshAllLabels()
    {
        RefreshSeed();
        RefreshWorldSize();
        RefreshWoodDensity();
        RefreshBerriesDensity();
        RefreshEmptyDensity();
        RefreshNpcCount();
        RefreshBarnStartingFood();
        RefreshBarnStartingWood();
        RefreshDayDuration();
        RefreshSimulationSpeed();
    }

    private void RefreshSeed()
    {
        SetLabel(seedLabelText, $"Seed: {seed}");
    }

    private void RefreshWorldSize()
    {
        SetLabel(worldSizeLabelText, $"Size: {worldWidth}x{worldHeight}");
    }

    private void RefreshWoodDensity()
    {
        SetLabel(resourceDensityWoodLabelText, $"Wood: {woodDensity}%");
    }

    private void RefreshBerriesDensity()
    {
        SetLabel(resourceDensityFoodLabelText, $"Berries: {berriesDensity}%");
    }

    private void RefreshEmptyDensity()
    {
        SetLabel(resourceDensityEmptyLabelText, $"Empty: {emptyDensity}%");
    }

    private void RefreshNpcCount()
    {
        SetLabel(npcCountLabelText, $"Villiger: {npcCount}");
    }

    private void RefreshBarnStartingFood()
    {
        SetLabel(barnStartingFoodLabelText, $"Barn Food: {barnStartingFood}");
    }

    private void RefreshBarnStartingWood()
    {
        SetLabel(barnStartingWoodLabelText, $"Barn Wood: {barnStartingWood}");
    }

    private void RefreshDayDuration()
    {
        SetLabel(dayDurationLabelText, $"Normal clock speed sec/day: {FormatSeconds(dayDurationSeconds)}");
    }

    private void RefreshSimulationSpeed()
    {
        SetLabel(simulationSpeedLabelText, $"Current clock speed sec/day: {FormatSeconds(simulationSpeedSeconds)}");
    }

    private string FormatSeconds(float seconds)
    {
        return seconds.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void SetLabel(TMP_Text label, string text)
    {
        if (label != null)
            label.SetText(text);
    }

    private void ClearAllInputs()
    {
        ClearInput(seedInputField);
        ClearInput(worldSizeInputField);
        ClearInput(resourceDensityWoodInputField);
        ClearInput(resourceDensityFoodInputField);
        ClearInput(resourceDensityEmptyInputField);
        ClearInput(npcCountInputField);
        ClearInput(barnStartingFoodInputField);
        ClearInput(barnStartingWoodInputField);
        ClearInput(dayDurationInputField);
        ClearInput(simulationSpeedInputField);
    }

    private void ClearInput(TMP_InputField inputField)
    {
        if (inputField != null)
            inputField.SetTextWithoutNotify(string.Empty);
    }

    private void ApplyDefaultSettings()
    {
        SimulationBalanceConfig balanceConfig = ResolveSimulationBalanceConfig();
        if (balanceConfig == null)
        {
            Debug.LogWarning($"StartMenuController has no SimulationBalanceConfig assigned. Falling back to built-in safety defaults. GameObject: {gameObject.name}.");
            ApplyFallbackDefaultSettings();
            return;
        }

        seed = balanceConfig.DefaultWorldSeed;
        worldWidth = balanceConfig.DefaultWorldWidth;
        worldHeight = balanceConfig.DefaultWorldHeight;
        woodDensity = balanceConfig.DefaultWoodPercent;
        berriesDensity = balanceConfig.DefaultBerryPercent;
        emptyDensity = balanceConfig.DefaultEmptyPercent;
        npcCount = balanceConfig.DefaultNpcCount;
        barnStartingFood = balanceConfig.BarnStartingFood;
        barnStartingWood = balanceConfig.BarnStartingWood;
        dayDurationSeconds = balanceConfig.DefaultNormalClockSpeedSecondsPerDay;
        simulationSpeedSeconds = balanceConfig.DefaultCurrentClockSpeedSecondsPerDay;
    }

    private void ApplyFallbackDefaultSettings()
    {
        seed = FallbackSeed;
        worldWidth = FallbackWorldWidth;
        worldHeight = FallbackWorldHeight;
        woodDensity = FallbackWoodDensity;
        berriesDensity = FallbackBerriesDensity;
        emptyDensity = FallbackEmptyDensity;
        npcCount = FallbackNpcCount;
        barnStartingFood = FallbackBarnStartingFood;
        barnStartingWood = FallbackBarnStartingWood;
        dayDurationSeconds = FallbackDayDurationSeconds;
        simulationSpeedSeconds = FallbackSimulationSpeedSeconds;
    }

    private SimulationBalanceConfig ResolveSimulationBalanceConfig()
    {
        if (simulationBalanceConfig != null)
            return simulationBalanceConfig;

#if UNITY_EDITOR
        string[] guids = AssetDatabase.FindAssets("t:SimulationBalanceConfig");
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            SimulationBalanceConfig foundConfig = AssetDatabase.LoadAssetAtPath<SimulationBalanceConfig>(assetPath);
            if (foundConfig == null)
                continue;

            simulationBalanceConfig = foundConfig;
            return simulationBalanceConfig;
        }
#endif

        return null;
    }

    private void LoadNoAgentScene()
    {
        LoadScene(noAgentSceneName);
    }

    private void LoadFsmAgentScene()
    {
        LoadScene(fsmAgentSceneName);
    }

    private void LoadGoapAgentScene()
    {
        LoadScene(goapAgentSceneName);
    }

    private void LoadScene(string sceneName)
    {
        if (!ValidateResourceDensityTotal(out string errorMessage))
        {
            ShowError(errorMessage);
            return;
        }

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning($"StartMenuController cannot load an empty scene name. GameObject: {gameObject.name}.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning($"StartMenuController cannot load scene '{sceneName}' because it is not available in Build Settings. GameObject: {gameObject.name}.");
            return;
        }

        HideError();
        QueueWorldGenerationLaunchSettings();
        SceneManager.LoadScene(sceneName);
    }

    private bool ValidateResourceDensityTotal(out string errorMessage)
    {
        int total = woodDensity + berriesDensity + emptyDensity;
        if (total == RequiredResourceDensityTotal)
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = $"Wood, Berries and Empty percentages must add up to exactly 100%. Current total: {total}%.";
        return false;
    }

    private void ShowError(string message)
    {
        if (errorText == null)
            return;

        errorText.SetText(message);
        errorText.gameObject.SetActive(true);
    }

    private void HideError()
    {
        if (errorText == null)
            return;

        errorText.SetText(string.Empty);
        errorText.gameObject.SetActive(false);
    }

    private void QueueWorldGenerationLaunchSettings()
    {
        WorldGenerationLaunchSettings.QueueStartSceneGeneration(
            new WorldGenerationLaunchSettings.Settings(
                seed,
                worldWidth,
                worldHeight,
                woodDensity,
                berriesDensity,
                emptyDensity,
                npcCount,
                barnStartingFood,
                barnStartingWood,
                dayDurationSeconds,
                simulationSpeedSeconds
            )
        );
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    [ContextMenu("Auto Wire References")]
    private void AutoWireReferences()
    {
        ResolveSimulationBalanceConfig();

        seedLabelText = seedLabelText != null ? seedLabelText : FindNamedComponent<TMP_Text>("SeedLabelText");
        seedInputField = seedInputField != null ? seedInputField : FindNamedComponent<TMP_InputField>("SeedInputField");
        worldSizeLabelText = worldSizeLabelText != null ? worldSizeLabelText : FindNamedComponent<TMP_Text>("WorldSizeLabelText");
        worldSizeInputField = worldSizeInputField != null ? worldSizeInputField : FindNamedComponent<TMP_InputField>("WorldSizeInputField");
        resourceDensityWoodLabelText = resourceDensityWoodLabelText != null ? resourceDensityWoodLabelText : FindNamedComponent<TMP_Text>("ResourceDensityWoodLabelText");
        resourceDensityWoodInputField = resourceDensityWoodInputField != null ? resourceDensityWoodInputField : FindNamedComponent<TMP_InputField>("ResourceDensityWoodInputField");
        resourceDensityFoodLabelText = resourceDensityFoodLabelText != null ? resourceDensityFoodLabelText : FindNamedComponent<TMP_Text>("ResourceDensityFoodLabelText");
        resourceDensityFoodInputField = resourceDensityFoodInputField != null ? resourceDensityFoodInputField : FindNamedComponent<TMP_InputField>("ResourceDensityFoodInputField");
        resourceDensityEmptyLabelText = resourceDensityEmptyLabelText != null ? resourceDensityEmptyLabelText : FindNamedComponent<TMP_Text>("ResourceDensityEmptyLabelText");
        resourceDensityEmptyInputField = resourceDensityEmptyInputField != null ? resourceDensityEmptyInputField : FindNamedComponent<TMP_InputField>("ResourceDensityEmptyInputField");
        npcCountLabelText = npcCountLabelText != null ? npcCountLabelText : FindNamedComponent<TMP_Text>("NpcCountLabelText");
        npcCountInputField = npcCountInputField != null ? npcCountInputField : FindNamedComponent<TMP_InputField>("NpcCountInputField");
        barnStartingFoodLabelText = barnStartingFoodLabelText != null ? barnStartingFoodLabelText : FindNamedComponent<TMP_Text>("BarnStartingFoodLabelText");
        barnStartingFoodInputField = barnStartingFoodInputField != null ? barnStartingFoodInputField : FindNamedComponent<TMP_InputField>("BarnStartingFoodInputField");
        barnStartingWoodLabelText = barnStartingWoodLabelText != null ? barnStartingWoodLabelText : FindNamedComponent<TMP_Text>("BarnStartingWoodLabelText");
        barnStartingWoodInputField = barnStartingWoodInputField != null ? barnStartingWoodInputField : FindNamedComponent<TMP_InputField>("BarnStartingWoodInputField");
        dayDurationLabelText = dayDurationLabelText != null ? dayDurationLabelText : FindNamedComponent<TMP_Text>("DayDurationLabelText");
        dayDurationInputField = dayDurationInputField != null ? dayDurationInputField : FindNamedComponent<TMP_InputField>("DayDurationInputField");
        simulationSpeedLabelText = simulationSpeedLabelText != null ? simulationSpeedLabelText : FindNamedComponent<TMP_Text>("SimulationSpeedLabelText");
        simulationSpeedInputField = simulationSpeedInputField != null ? simulationSpeedInputField : FindNamedComponent<TMP_InputField>("SimulationSpeedInputField");

        generateNoAgentWorldButton = generateNoAgentWorldButton != null ? generateNoAgentWorldButton : FindNamedComponent<Button>("GenerateNoAgentWorldButton");
        generateFsmAgentWorldButton = generateFsmAgentWorldButton != null ? generateFsmAgentWorldButton : FindNamedComponent<Button>("GenerateFsmAgentWorldButton");
        generateGoapAgentWorldButton = generateGoapAgentWorldButton != null ? generateGoapAgentWorldButton : FindNamedComponent<Button>("GenerateGoapAgentWorldButton");
        endGameButton = endGameButton != null ? endGameButton : FindNamedComponent<Button>("EndGameButton");

        errorText = errorText != null ? errorText : FindNamedComponent<TMP_Text>("ErrorText");
    }

    private T FindNamedComponent<T>(string objectName) where T : Component
    {
        T childComponent = FindChildComponent<T>(objectName);
        if (childComponent != null)
            return childComponent;

        T[] components = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < components.Length; i++)
        {
            T component = components[i];
            if (component != null && component.gameObject.name == objectName)
                return component;
        }

        return null;
    }

    private T FindChildComponent<T>(string objectName) where T : Component
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && child.name == objectName)
                return child.GetComponent<T>();
        }

        return null;
    }
}
