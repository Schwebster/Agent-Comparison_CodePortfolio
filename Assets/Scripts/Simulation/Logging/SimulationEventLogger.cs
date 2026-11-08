using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class SimulationEventLogger
{
    private const int SchemaVersion = 2;
    private const int FlushIntervalEvents = 32;
    private const int MaximumLoggedPathCorners = 8;

    private static StreamWriter writer;
    private static string runId = string.Empty;
    private static string architecture = "Unknown";
    private static long nextSequence = 1;
    private static int eventsSinceFlush;
    private static int lastFlushedDay = -1;
    private static WorldTimeManager worldTimeManager;
    private static bool applicationQuittingRegistered;
    private static bool isWriting;
    private static bool isShuttingDown;

    public static bool IsRunActive => writer != null;
    public static string LastLogPath { get; private set; } = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        CloseWriter();
        runId = string.Empty;
        architecture = "Unknown";
        nextSequence = 1;
        eventsSinceFlush = 0;
        lastFlushedDay = -1;
        worldTimeManager = null;
        LastLogPath = string.Empty;
        isWriting = false;
        isShuttingDown = false;

        if (applicationQuittingRegistered)
            Application.quitting -= HandleApplicationQuitting;

        Application.quitting += HandleApplicationQuitting;
        applicationQuittingRegistered = true;
    }

    public static void BeginRun(
        WorldGenerationLaunchSettings.Settings settings,
        string architectureName)
    {
        CloseWriter();

        architecture = string.IsNullOrWhiteSpace(architectureName)
            ? "Unknown"
            : architectureName;

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        runId = $"{timestamp}_{SanitizeFileName(architecture)}_seed{settings.Seed}";
        nextSequence = 1;
        eventsSinceFlush = 0;
        lastFlushedDay = -1;
        worldTimeManager = null;

        if (!TryOpenWriter())
            return;

        SimulationRunMetadata metadata = new SimulationRunMetadata
        {
            seed = settings.Seed,
            world_width = settings.WorldWidth,
            world_height = settings.WorldHeight,
            wood_density_percent = settings.WoodDensityPercent,
            food_density_percent = settings.FoodDensityPercent,
            empty_density_percent = settings.EmptyDensityPercent,
            starting_npc_count = settings.NpcCount,
            starting_food = settings.BarnStartingFood,
            starting_wood = settings.BarnStartingWood,
            normal_seconds_per_day = settings.NormalClockSpeedSecondsPerDay,
            current_seconds_per_day = settings.CurrentClockSpeedSecondsPerDay
        };

        WriteEntry(
            "Run",
            "RunStarted",
            null,
            new SimulationEventDetails
            {
                message = "Benchmark run initialized from StartScene launch settings."
            },
            metadata
        );

        Flush();
        Debug.Log($"Simulation event log started: {LastLogPath}");
    }

    public static void LogTaskEvent(
        CharacterIdentity actor,
        string eventType,
        AgentTaskType taskType,
        AgentTaskState taskState,
        bool hasRole,
        NpcWorkRole role,
        int taskRunId,
        string message = "")
    {
        EnsureRunStarted();
        WriteEntry(
            "NpcTask",
            eventType,
            actor,
            new SimulationEventDetails
            {
                message = message,
                task = taskType.ToString(),
                task_state = taskState.ToString(),
                role = hasRole ? role.ToString() : "None",
                task_run_id = taskRunId
            },
            null
        );
    }

    public static void LogBarnApproachFailure(
        CharacterIdentity actor,
        Character character,
        ResourceStorage barn,
        Vector3 requestedDestination,
        string executorState,
        string message)
    {
        if (character == null || barn == null)
            return;

        EnsureRunStarted();

        NavMeshAgent agent = character.GetComponent<NavMeshAgent>();
        Vector3 characterPosition = character.transform.position;
        Vector3 barnPosition = barn.transform.position;
        Vector3 depositPosition = barn.GetDepositPosition();
        bool agentEnabled = agent != null && agent.enabled;
        bool isOnNavMesh = agentEnabled && agent.isOnNavMesh;
        bool canReadPath = isOnNavMesh && !agent.pathPending;

        WriteEntry(
            "NpcDiagnostic",
            "BarnApproachFailed",
            actor,
            new SimulationEventDetails
            {
                message = message,
                target = barn.gameObject.name,
                barn_approach = new SimulationBarnApproachDiagnostics
                {
                    executor_state = executorState,
                    barn_position_x = barnPosition.x,
                    barn_position_z = barnPosition.z,
                    deposit_position_x = depositPosition.x,
                    deposit_position_z = depositPosition.z,
                    requested_destination_x = requestedDestination.x,
                    requested_destination_z = requestedDestination.z,
                    distance_to_deposit = PlanarDistance(characterPosition, depositPosition),
                    distance_to_requested_destination = PlanarDistance(characterPosition, requestedDestination),
                    interaction_radius = barn.InteractionRadius,
                    inside_interaction_range = barn.IsInInteractionRange(characterPosition),
                    npc_collision_active = ManualDayPlanState.NpcCollisionActive,
                    agent_exists = agent != null,
                    agent_enabled = agentEnabled,
                    agent_on_navmesh = isOnNavMesh,
                    agent_is_stopped = agentEnabled && agent.isStopped,
                    path_pending = isOnNavMesh && agent.pathPending,
                    has_path = isOnNavMesh && agent.hasPath,
                    path_status = canReadPath ? agent.pathStatus.ToString() : "Unavailable",
                    remaining_distance = canReadPath && !float.IsInfinity(agent.remainingDistance)
                        ? agent.remainingDistance
                        : -1f,
                    velocity = agentEnabled ? agent.velocity.magnitude : 0f,
                    desired_velocity = agentEnabled ? agent.desiredVelocity.magnitude : 0f,
                    agent_radius = agent != null ? agent.radius : 0f,
                    stopping_distance = agent != null ? agent.stoppingDistance : 0f,
                    avoidance_priority = agent != null ? agent.avoidancePriority : -1,
                    obstacle_avoidance_type = agent != null
                        ? agent.obstacleAvoidanceType.ToString()
                        : "Unavailable",
                    path_corners = canReadPath && agent.hasPath
                        ? FormatPathCorners(agent.path.corners)
                        : "None"
                }
            },
            null
        );
    }

    public static void LogRoleEvent(
        CharacterIdentity actor,
        string eventType,
        NpcWorkRole? role,
        string message)
    {
        EnsureRunStarted();
        WriteEntry(
            "NpcDecision",
            eventType,
            actor,
            new SimulationEventDetails
            {
                message = message,
                role = role.HasValue ? role.Value.ToString() : "None"
            },
            null
        );
    }

    public static void LogWorldChange(
        string eventType,
        CharacterIdentity actor,
        string message,
        string target = "",
        string resourceType = "",
        int amount = 0,
        int secondaryAmount = 0)
    {
        EnsureRunStarted();
        WriteEntry(
            "WorldChange",
            eventType,
            actor,
            new SimulationEventDetails
            {
                message = message,
                target = target,
                resource_type = resourceType,
                amount = amount,
                secondary_amount = secondaryAmount
            },
            null
        );
    }

    public static void LogStrategyDecision(
        string eventType,
        string previousState,
        string nextState,
        string priorities,
        string quotas,
        string message)
    {
        EnsureRunStarted();
        WriteEntry(
            "ArchitectureDecision",
            eventType,
            null,
            new SimulationEventDetails
            {
                message = message,
                previous_value = previousState,
                new_value = nextState,
                priorities = priorities,
                quotas = quotas
            },
            null
        );
    }

    public static void LogPlanChange(string eventType, string message)
    {
        if (!IsRunActive)
            return;

        WriteEntry(
            "SettlementPlan",
            eventType,
            null,
            new SimulationEventDetails { message = message },
            null
        );
    }

    public static void Flush()
    {
        if (writer == null)
            return;

        try
        {
            writer.Flush();
            eventsSinceFlush = 0;
        }
        catch (Exception exception)
        {
            DisableAfterWriteFailure(exception);
        }
    }

    private static void EnsureRunStarted()
    {
        if (writer != null || isShuttingDown)
            return;

        architecture = SceneManager.GetActiveScene().name;
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        runId = $"{timestamp}_{SanitizeFileName(architecture)}_direct";
        nextSequence = 1;
        eventsSinceFlush = 0;
        lastFlushedDay = -1;
        worldTimeManager = null;

        if (!TryOpenWriter())
            return;

        WriteEntry(
            "Run",
            "RunStarted",
            null,
            new SimulationEventDetails
            {
                message = "Run started directly without StartScene launch metadata."
            },
            null
        );

        Flush();
        Debug.Log($"Simulation event log started: {LastLogPath}");
    }

    private static float PlanarDistance(Vector3 first, Vector3 second)
    {
        float x = first.x - second.x;
        float z = first.z - second.z;
        return Mathf.Sqrt(x * x + z * z);
    }

    private static string FormatPathCorners(Vector3[] corners)
    {
        if (corners == null || corners.Length == 0)
            return "None";

        StringBuilder result = new StringBuilder();
        int cornerCount = Mathf.Min(corners.Length, MaximumLoggedPathCorners);
        for (int i = 0; i < cornerCount; i++)
        {
            if (i > 0)
                result.Append(" -> ");

            result.Append('(')
                .Append(corners[i].x.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(corners[i].z.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(')');
        }

        if (corners.Length > cornerCount)
            result.Append(" -> ...");

        return result.ToString();
    }

    private static bool TryOpenWriter()
    {
        try
        {
            string directory = GetLogDirectory();
            Directory.CreateDirectory(directory);
            LastLogPath = Path.Combine(directory, runId + ".events.jsonl");
            writer = new StreamWriter(LastLogPath, false, new UTF8Encoding(false), 65536);
            return true;
        }
        catch (Exception exception)
        {
            writer = null;
            Debug.LogError($"Simulation event log could not be opened: {exception.Message}");
            return false;
        }
    }

    private static void WriteEntry(
        string category,
        string eventType,
        CharacterIdentity actor,
        SimulationEventDetails details,
        SimulationRunMetadata metadata)
    {
        if (writer == null || isWriting)
            return;

        isWriting = true;
        try
        {
            ResolveWorldTimeManager();
            SimulationTimeSnapshot time = CaptureTime();
            SimulationLogEntry entry = new SimulationLogEntry
            {
                schema_version = SchemaVersion,
                run_id = runId,
                sequence = nextSequence++,
                recorded_utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unity_frame = Time.frameCount,
                architecture = architecture,
                category = category,
                event_type = eventType,
                simulation_time = time,
                actor = CaptureActor(actor),
                details = details ?? new SimulationEventDetails(),
                world = CaptureWorldState(),
                run_metadata = metadata
            };

            writer.WriteLine(JsonUtility.ToJson(entry));
            eventsSinceFlush++;

            if (time.day != lastFlushedDay || eventsSinceFlush >= FlushIntervalEvents)
            {
                writer.Flush();
                eventsSinceFlush = 0;
                lastFlushedDay = time.day;
            }
        }
        catch (Exception exception)
        {
            DisableAfterWriteFailure(exception);
        }
        finally
        {
            isWriting = false;
        }
    }

    private static SimulationActorSnapshot CaptureActor(CharacterIdentity actor)
    {
        if (actor == null)
            return null;

        Character character = actor.GetComponent<Character>();
        NpcHealth health = actor.GetComponent<NpcHealth>();
        Vector3 position = actor.transform.position;

        return new SimulationActorSnapshot
        {
            citizen_id = actor.CharacterId,
            citizen_name = actor.FullName,
            position_x = position.x,
            position_z = position.z,
            carried_food = character != null ? character.CarriedBerries : 0,
            carried_wood = character != null ? character.CarriedWood : 0,
            health = health != null ? health.CurrentHealth : 0f
        };
    }

    private static SimulationTimeSnapshot CaptureTime()
    {
        if (worldTimeManager == null)
            return new SimulationTimeSnapshot { day = 0, hour = 0f, total_world_hours = 0f };

        return new SimulationTimeSnapshot
        {
            day = worldTimeManager.CurrentDay,
            hour = worldTimeManager.CurrentHour,
            total_world_hours = worldTimeManager.TotalWorldHours
        };
    }

    private static SimulationWorldSnapshot CaptureWorldState()
    {
        HomeRegistry.GetHousingStatus(out int occupiedHousing, out int housingCapacity);

        int totalFog = FogRegistry.RegisteredFogCount;
        int unrevealedFog = FogRegistry.CountUnrevealedFog();
        float exploredPercent = totalFog > 0
            ? Mathf.Clamp01((float)(totalFog - unrevealedFog) / totalFog) * 100f
            : 100f;

        int activeConstructionSites = 0;
        IReadOnlyList<ConstructionSite> sites = ConstructionSiteRegistry.ActiveConstructionSites;
        for (int i = 0; i < sites.Count; i++)
        {
            ConstructionSite site = sites[i];
            if (site != null && site.isActiveAndEnabled && !site.IsComplete)
                activeConstructionSites++;
        }

        return new SimulationWorldSnapshot
        {
            stored_food = ResourceStorageRegistry.SharedBerriesStored,
            stored_wood = ResourceStorageRegistry.SharedWoodStored,
            living_citizens = CharacterIdentity.ActiveIdentityCount,
            occupied_housing = occupiedHousing,
            housing_capacity = housingCapacity,
            registered_houses = HomeRegistry.RegisteredHomeCount,
            total_fog_cells = totalFog,
            unrevealed_fog_cells = unrevealedFog,
            explored_percent = exploredPercent,
            active_construction_sites = activeConstructionSites,
            assigned_food = NpcWorkRoleAssignmentRegistry.GetAssignedCount(NpcWorkRole.Food),
            assigned_wood = NpcWorkRoleAssignmentRegistry.GetAssignedCount(NpcWorkRole.Wood),
            assigned_explore = NpcWorkRoleAssignmentRegistry.GetAssignedCount(NpcWorkRole.Explore),
            assigned_build = NpcWorkRoleAssignmentRegistry.GetAssignedCount(NpcWorkRole.Build),
            quota_food = ManualDayPlanState.GetQuota(NpcWorkRole.Food),
            quota_wood = ManualDayPlanState.GetQuota(NpcWorkRole.Wood),
            quota_explore = ManualDayPlanState.GetQuota(NpcWorkRole.Explore),
            quota_build = ManualDayPlanState.GetQuota(NpcWorkRole.Build),
            priorities = FormatPriorities(ManualDayPlanState.GetPriorities())
        };
    }

    private static string FormatPriorities(IReadOnlyList<NpcWorkRole> priorities)
    {
        if (priorities == null || priorities.Count <= 0)
            return string.Empty;

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < priorities.Count; i++)
        {
            if (i > 0)
                builder.Append(" > ");

            builder.Append(priorities[i]);
        }

        return builder.ToString();
    }

    private static void ResolveWorldTimeManager()
    {
        if (worldTimeManager == null)
            worldTimeManager = UnityEngine.Object.FindFirstObjectByType<WorldTimeManager>();
    }

    private static string GetLogDirectory()
    {
#if UNITY_EDITOR
        DirectoryInfo projectDirectory = Directory.GetParent(Application.dataPath);
        if (projectDirectory != null)
            return Path.Combine(projectDirectory.FullName, "Logs", "Benchmark");
#endif
        return Path.Combine(Application.persistentDataPath, "BenchmarkLogs");
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unknown";

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            builder.Append(Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }

    private static void HandleApplicationQuitting()
    {
        isShuttingDown = true;

        if (writer != null)
        {
            WriteEntry(
                "Run",
                "RunEnded",
                null,
                new SimulationEventDetails { message = "Application or Play Mode is ending." },
                null
            );
        }

        CloseWriter();
    }

    private static void CloseWriter()
    {
        if (writer == null)
            return;

        try
        {
            writer.Flush();
            writer.Dispose();
        }
        catch
        {
            // Logging must never stop or invalidate the simulation shutdown path.
        }

        writer = null;
    }

    private static void DisableAfterWriteFailure(Exception exception)
    {
        Debug.LogError($"Simulation event logging stopped after a write failure: {exception.Message}");
        CloseWriter();
    }
}

[Serializable]
internal sealed class SimulationLogEntry
{
    public int schema_version;
    public string run_id;
    public long sequence;
    public string recorded_utc;
    public int unity_frame;
    public string architecture;
    public string category;
    public string event_type;
    public SimulationTimeSnapshot simulation_time;
    public SimulationActorSnapshot actor;
    public SimulationEventDetails details;
    public SimulationWorldSnapshot world;
    public SimulationRunMetadata run_metadata;
}

[Serializable]
internal sealed class SimulationTimeSnapshot
{
    public int day;
    public float hour;
    public float total_world_hours;
}

[Serializable]
internal sealed class SimulationActorSnapshot
{
    public int citizen_id;
    public string citizen_name;
    public float position_x;
    public float position_z;
    public int carried_food;
    public int carried_wood;
    public float health;
}

[Serializable]
internal sealed class SimulationEventDetails
{
    public string message;
    public string task;
    public string task_state;
    public string role;
    public int task_run_id;
    public string target;
    public string resource_type;
    public int amount;
    public int secondary_amount;
    public string previous_value;
    public string new_value;
    public string priorities;
    public string quotas;
    public SimulationBarnApproachDiagnostics barn_approach;
}

[Serializable]
internal sealed class SimulationBarnApproachDiagnostics
{
    public string executor_state;
    public float barn_position_x;
    public float barn_position_z;
    public float deposit_position_x;
    public float deposit_position_z;
    public float requested_destination_x;
    public float requested_destination_z;
    public float distance_to_deposit;
    public float distance_to_requested_destination;
    public float interaction_radius;
    public bool inside_interaction_range;
    public bool npc_collision_active;
    public bool agent_exists;
    public bool agent_enabled;
    public bool agent_on_navmesh;
    public bool agent_is_stopped;
    public bool path_pending;
    public bool has_path;
    public string path_status;
    public float remaining_distance;
    public float velocity;
    public float desired_velocity;
    public float agent_radius;
    public float stopping_distance;
    public int avoidance_priority;
    public string obstacle_avoidance_type;
    public string path_corners;
}

[Serializable]
internal sealed class SimulationWorldSnapshot
{
    public int stored_food;
    public int stored_wood;
    public int living_citizens;
    public int occupied_housing;
    public int housing_capacity;
    public int registered_houses;
    public int total_fog_cells;
    public int unrevealed_fog_cells;
    public float explored_percent;
    public int active_construction_sites;
    public int assigned_food;
    public int assigned_wood;
    public int assigned_explore;
    public int assigned_build;
    public int quota_food;
    public int quota_wood;
    public int quota_explore;
    public int quota_build;
    public string priorities;
}

[Serializable]
internal sealed class SimulationRunMetadata
{
    public int seed;
    public int world_width;
    public int world_height;
    public int wood_density_percent;
    public int food_density_percent;
    public int empty_density_percent;
    public int starting_npc_count;
    public int starting_food;
    public int starting_wood;
    public float normal_seconds_per_day;
    public float current_seconds_per_day;
}
