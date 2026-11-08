#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Batch-only integration check using the existing scenes and procedural generator.
// No scene or prefab assets are created or saved. Run in an isolated project copy.
[InitializeOnLoad]
public static class GoapSmokeValidation
{
    private const string SessionKey = "AgentComparison.SmokeScene";
    private static bool started;
    private static bool finished;
    private static double deadline;
    private static float startHour;
    private static int initialFog;
    private static int exceptions;
    private static long foodDelivered;
    private static long woodDelivered;
    private static int sitesCompleted;
    private static string sceneName;
    private static bool development;
    private static float targetDurationWorldHours;
    private static int maximumPopulation;

    static GoapSmokeValidation()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void RunGoap() => Begin("GoapAgent");
    public static void RunFsm() => Begin("FsmAgent");
    public static void RunGoapDevelopment() => Begin("GoapAgent", true);
    public static void RunFsmDevelopment() => Begin("FsmAgent", true);
    public static void RunGoapDaySevenRegression() => Begin("GoapAgent", true, 192f);

    private static void Begin(string scene, bool testDevelopment = false, float durationWorldHours = 96f)
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Run this validation in a separate Unity batch process.");
        SessionState.SetString(SessionKey, scene);
        SessionState.SetBool(SessionKey + ".Development", testDevelopment);
        SessionState.SetFloat(SessionKey + ".Duration", durationWorldHours);
        EditorSceneManager.OpenScene("Assets/Scenes/StartScene.unity");
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        sceneName = SessionState.GetString(SessionKey, "");
        development = SessionState.GetBool(SessionKey + ".Development", false);
        targetDurationWorldHours = SessionState.GetFloat(SessionKey + ".Duration", 96f);
        if (string.IsNullOrEmpty(sceneName))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            deadline = EditorApplication.timeSinceStartup + 240;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            // Clock acceleration uses the project's existing movement/time scaling.
            WorldGenerationLaunchSettings.QueueStartSceneGeneration(
                new WorldGenerationLaunchSettings.Settings(13, 20, 20, 20, 10, 70,
                    development ? 4 : 2, development ? 300 : 25, development ? 30 : 15, 600f, 24f));
            SceneManager.LoadScene(sceneName);
        }
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(SessionKey, "")) &&
            (type == LogType.Exception || type == LogType.Error || type == LogType.Assert))
            exceptions++;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || finished || string.IsNullOrEmpty(sceneName))
            return;
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Finish(false, "Integration run timed out.");
            return;
        }
        var generator = UnityEngine.Object.FindFirstObjectByType<GridWorldGenerator>();
        var clock = UnityEngine.Object.FindFirstObjectByType<WorldTimeManager>();
        if (generator == null || !generator.IsWorldGenerated || clock == null)
            return;
        if (!started)
        {
            started = true;
            initialFog = FogRegistry.CountUnrevealedFog();
            startHour = clock.TotalWorldHours;
            clock.Resume();
        }
        // Keep maxima because a dead NPC can subsequently disappear from the scene.
        var citizens = UnityEngine.Object.FindObjectsByType<Character>(FindObjectsSortMode.None);
        foodDelivered = Math.Max(foodDelivered, citizens.Sum(c => c.TotalFoodDeposited));
        woodDelivered = Math.Max(woodDelivered, citizens.Sum(c => c.TotalWoodDeposited));
        sitesCompleted = Math.Max(sitesCompleted, UnityEngine.Object
            .FindObjectsByType<NpcTaskController>(FindObjectsSortMode.None).Sum(t => t.CompletedConstructionCount));
        HomeRegistry.GetLivingPopulationStatus(out int population, out int housing);
        maximumPopulation = Math.Max(maximumPopulation, population);
        if (clock.TotalWorldHours - startHour < targetDurationWorldHours)
            return;

        int revealed = initialFog - FogRegistry.CountUnrevealedFog();
        bool scenarioPassed = development
            ? sitesCompleted > 0 && maximumPopulation > 4
            : foodDelivered > 0 && woodDelivered > 0;
        bool success = exceptions == 0 && revealed > 0 && population > 0 && scenarioPassed;
        Finish(success, $"Scene={sceneName}; Hours={clock.TotalWorldHours - startHour:0.##}; " +
            $"FoodDelivered={foodDelivered}; WoodDelivered={woodDelivered}; SitesCompleted={sitesCompleted}; " +
            $"Revealed={revealed}; Population={population}; MaximumPopulation={maximumPopulation}; Housing={housing}; Errors={exceptions}");
    }

    private static void Finish(bool success, string detail)
    {
        finished = true;
        SimulationEventLogger.Flush();
        string result = (success ? "PASS: " : "FAIL: ") + detail;
        Directory.CreateDirectory("Logs/Validation");
        File.WriteAllText($"Logs/Validation/{sceneName}-{(development ? "development" : "survival")}.txt", result);
        Debug.Log("ARCHITECTURE_SMOKE " + result);
        SessionState.EraseString(SessionKey);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
#endif
