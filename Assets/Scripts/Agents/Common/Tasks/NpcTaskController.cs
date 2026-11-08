/*
===============================================================================
Script:
NpcTaskController

Purpose:
Stores and runs the single current shared task for an NPC.

Folder:
Assets/Scripts/Agents/Common/Tasks/

Scene Hierarchy:
_World
- Characters
  - Character

Attach To:
NPC character root

Required Children:
None

Dependencies:
- Character
- HomeTravelExecutor
- SleepExecutor
- GatherResourceExecutor
- NpcResourceReservation
- ExploreFogExecutor
- ResourceCollectionExecutor
- ResourceDepositExecutor
- NpcHealth

Automatically Resolved:
- Required components on the same GameObject

Inspector References:
None
===============================================================================
*/

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(HomeTravelExecutor))]
[RequireComponent(typeof(SleepExecutor))]
[RequireComponent(typeof(GatherResourceExecutor))]
[RequireComponent(typeof(NpcResourceReservation))]
[RequireComponent(typeof(ExploreFogExecutor))]
[RequireComponent(typeof(ResourceCollectionExecutor))]
[RequireComponent(typeof(ResourceDepositExecutor))]
public class NpcTaskController : MonoBehaviour
{
    private const float StorageRetryDelayWorldHours =
        ResourceStorageRegistry.TemporaryUnreachableDurationWorldHours;
    private const float BuildMovementStuckWorldHours = 0.5f;

    private Character character;
    private CharacterIdentity identity;
    private HomeTravelExecutor homeTravelExecutor;
    private SleepExecutor sleepExecutor;
    private GatherResourceExecutor gatherResourceExecutor;
    private NpcResourceReservation resourceReservation;
    private ExploreFogExecutor exploreFogExecutor;
    private ResourceCollectionExecutor resourceCollectionExecutor;
    private ResourceDepositExecutor resourceDepositExecutor;
    private NpcHealth npcHealth;
    private AgentWorkProfile workProfile;
    private WorldTimeManager worldTimeManager;
    private ResourceNode.ResourceType requestedGatherResourceType = ResourceNode.ResourceType.Wood;
    private ConstructionSite activeConstructionSite;
    private bool repeatGatherTask;
    private bool repeatExploreTask;
    private bool goHomeTravelStarted;
    private bool goHomeNeedsDailyUpkeep;
    private bool currentTaskUsesManualDayPlan;
    private bool hasAssignedWorkRole;
    private bool hasAssignedExploreDirection;
    private bool repeatManualDayPlanTask = true;
    private float depositRetryAllowedAtTotalWorldHours;
    private float goHomeDepositRetryAllowedAtTotalWorldHours;
    private int currentTaskRunId;
    private NpcWorkRole assignedWorkRole = NpcWorkRole.Food;
    private ExploreDirection assignedExploreDirection = ExploreDirection.North;
    private readonly HashSet<NpcWorkRole> manualFallbackRejectedRoles = new HashSet<NpcWorkRole>();

    public AgentTaskType CurrentTask { get; private set; } = AgentTaskType.Idle;
    public AgentTaskState TaskState { get; private set; } = AgentTaskState.Running;
    public bool IsBusy => IsTaskActive;
    public int CompletedConstructionCount { get; private set; }
    public bool HasAssignedWorkRole => hasAssignedWorkRole;
    public NpcWorkRole AssignedWorkRole => assignedWorkRole;
    public string LastManualDayPlanFailureReason { get; private set; } = string.Empty;
    public bool IsTaskActive =>
        CurrentTask != AgentTaskType.Idle &&
        CurrentTask != AgentTaskType.None &&
        (TaskState == AgentTaskState.Start || TaskState == AgentTaskState.Running);

    private void Awake()
    {
        character = GetComponent<Character>();
        identity = GetComponent<CharacterIdentity>();
        homeTravelExecutor = GetComponent<HomeTravelExecutor>();
        sleepExecutor = GetComponent<SleepExecutor>();
        gatherResourceExecutor = GetComponent<GatherResourceExecutor>();
        resourceReservation = GetComponent<NpcResourceReservation>();
        exploreFogExecutor = GetComponent<ExploreFogExecutor>();
        resourceCollectionExecutor = GetComponent<ResourceCollectionExecutor>();
        resourceDepositExecutor = GetComponent<ResourceDepositExecutor>();
        npcHealth = GetComponent<NpcHealth>();
        if (npcHealth == null)
            npcHealth = gameObject.AddComponent<NpcHealth>();

        workProfile = GetComponent<AgentWorkProfile>();
        ResolveWorldTimeManager();
    }

    private void OnDisable()
    {
        if (IsTaskActive)
            CancelCurrentTask();

        ReleaseAssignedWorkRole();
    }

    private void Update()
    {
        if (!IsTaskActive)
            return;

        if (TaskState == AgentTaskState.Start)
        {
            StartCurrentTask();

            if (TaskState == AgentTaskState.Running)
            {
                SimulationEventLogger.LogTaskEvent(
                    identity,
                    "TaskStarted",
                    CurrentTask,
                    TaskState,
                    hasAssignedWorkRole,
                    assignedWorkRole,
                    currentTaskRunId
                );
            }

            return;
        }

        UpdateRunningTask();
    }

    public bool TryStartTask(AgentTaskType taskType)
    {
        if (IsTaskActive)
            return false;

        ResetCompletedSleepForDayTask(taskType);
        CurrentTask = taskType;
        currentTaskRunId++;

        if (taskType == AgentTaskType.Idle)
        {
            ClearJobTools();
            TaskState = AgentTaskState.Running;
            LogTaskSelected();
            return true;
        }

        if (taskType == AgentTaskType.None)
        {
            ClearJobTools();
            TaskState = AgentTaskState.Completed;
            LogTaskSelected();
            return true;
        }

        TaskState = AgentTaskState.Start;
        LogTaskSelected();
        return true;
    }

    public bool TryStartGatherResourceTask(ResourceNode.ResourceType resourceType, bool repeat = false)
    {
        currentTaskUsesManualDayPlan = false;
        return TryStartGatherResourceTaskInternal(resourceType, repeat);
    }

    public bool TryStartWorkRole(NpcWorkRole role, bool repeat = false)
    {
        if (IsTaskActive || !IsSupportedWorkRole(role))
            return false;

        RestoreAssignedWorkRoleFromRegistry();
        if (hasAssignedWorkRole && assignedWorkRole != role)
            return false;

        if (!hasAssignedWorkRole &&
            !NpcWorkRoleAssignmentRegistry.TryAssignRequestedRole(identity, role))
        {
            return false;
        }

        assignedWorkRole = role;
        hasAssignedWorkRole = true;
        currentTaskUsesManualDayPlan = false;
        ConfigureExploreDirectionForRequestedRole(role);

        if (TryStartWorkRoleTask(role, repeat, false))
            return true;

        ReleaseAssignedWorkRole();
        return false;
    }

    public bool TryStartManualDayPlanWorkTask(bool repeat = true, bool logFailure = true)
    {
        if (IsTaskActive)
        {
            LastManualDayPlanFailureReason = "task controller is already running a task.";
            return false;
        }

        SimulationPerformanceCounters.AddManualTaskStartAttempt();

        manualFallbackRejectedRoles.Clear();
        repeatManualDayPlanTask = repeat;
        LastManualDayPlanFailureReason = string.Empty;

        RestoreAssignedWorkRoleFromRegistry();
        if (hasAssignedWorkRole)
        {
            if (!ManualDayPlanState.HasLoopFlag(assignedWorkRole))
            {
                currentTaskUsesManualDayPlan = true;
                if (TryStartWorkRoleTask(assignedWorkRole, repeatManualDayPlanTask, true))
                    return true;

                TryRestartManualPlanAfterRoleCouldNotBegin(
                    $"Manual day plan role {assignedWorkRole} could not resume."
                );
                return IsTaskActive;
            }

            manualFallbackRejectedRoles.Add(assignedWorkRole);
            ReleaseAssignedWorkRole();
            return TryStartManualFallbackTask(
                "the assigned daily role already has a global LoopFlag."
            );
        }

        if (!ManualDayPlanRoleSelector.TryAssignNextRole(
            identity,
            out NpcWorkRole role,
            out string reason))
        {
            LastManualDayPlanFailureReason = reason;

            if (logFailure)
                Debug.Log($"Manual day plan did not start work for {gameObject.name}: {reason}");

            return false;
        }

        assignedWorkRole = role;
        hasAssignedWorkRole = true;
        currentTaskUsesManualDayPlan = true;

        if (TryStartWorkRoleTask(role, repeatManualDayPlanTask, true))
            return true;

        TryRestartManualPlanAfterRoleCouldNotBegin($"Manual day plan role {role} could not start.");
        return IsTaskActive;
    }

    private bool TryStartGatherResourceTaskInternal(ResourceNode.ResourceType resourceType, bool repeat)
    {
        requestedGatherResourceType = resourceType;
        repeatGatherTask = repeat;
        return TryStartTask(AgentTaskType.GatherResource);
    }

    private bool TryStartBuildTaskInternal()
    {
        return TryStartTask(AgentTaskType.Build);
    }

    private bool TryStartExploreFogTaskInternal(bool repeat)
    {
        repeatExploreTask = repeat;
        return TryStartTask(AgentTaskType.ExploreFog);
    }

    private bool TryStartWorkRoleTask(NpcWorkRole role, bool repeat, bool claimExploreDirection)
    {
        if (role == NpcWorkRole.Build)
            return TryStartBuildTaskInternal();

        if (role == NpcWorkRole.Explore)
        {
            if (claimExploreDirection)
                assignedExploreDirection = ManualDayPlanState.ClaimNextExploreDirection();

            hasAssignedExploreDirection = true;
            return TryStartExploreFogTaskInternal(repeat);
        }

        if (role == NpcWorkRole.Food)
        {
            return TryStartGatherResourceTaskInternal(ResourceNode.ResourceType.Berries, repeat);
        }

        if (role == NpcWorkRole.Wood)
            return TryStartGatherResourceTaskInternal(ResourceNode.ResourceType.Wood, repeat);

        return false;
    }

    private void ConfigureExploreDirectionForRequestedRole(NpcWorkRole role)
    {
        hasAssignedExploreDirection = role == NpcWorkRole.Explore;
        if (!hasAssignedExploreDirection)
        {
            assignedExploreDirection = ExploreDirection.North;
            return;
        }

        int characterId = identity != null ? identity.CharacterId : 0;
        switch (Mathf.Abs(characterId) % 4)
        {
            case 1:
                assignedExploreDirection = ExploreDirection.East;
                break;
            case 2:
                assignedExploreDirection = ExploreDirection.South;
                break;
            case 3:
                assignedExploreDirection = ExploreDirection.West;
                break;
            default:
                assignedExploreDirection = ExploreDirection.North;
                break;
        }
    }

    private static bool IsSupportedWorkRole(NpcWorkRole role)
    {
        return role == NpcWorkRole.Food ||
            role == NpcWorkRole.Wood ||
            role == NpcWorkRole.Explore ||
            role == NpcWorkRole.Build;
    }

    public bool TryStartCollectResourceTask()
    {
        return TryStartTask(AgentTaskType.CollectResource);
    }

    public bool TryStartDepositResourcesTask()
    {
        return TryStartTask(AgentTaskType.DepositResources);
    }

    public bool TryStartBuildTask()
    {
        currentTaskUsesManualDayPlan = false;
        return TryStartBuildTaskInternal();
    }

    public bool TryStartGoHomeTask()
    {
        return TryStartTask(AgentTaskType.GoHome);
    }

    public bool TryStartSleepTask()
    {
        return TryStartTask(AgentTaskType.Sleep);
    }

    public bool TryStartIdleTask()
    {
        return TryStartTask(AgentTaskType.Idle);
    }

    public void CancelCurrentTask()
    {
        bool wasActive = IsTaskActive;
        AgentTaskType cancelledTask = CurrentTask;
        bool cancelledTaskHadRole = hasAssignedWorkRole;
        NpcWorkRole cancelledRole = assignedWorkRole;
        CleanupCurrentTask();
        TaskState = AgentTaskState.Cancelled;

        if (wasActive)
        {
            SimulationEventLogger.LogTaskEvent(
                identity,
                "TaskCancelled",
                cancelledTask,
                TaskState,
                cancelledTaskHadRole,
                cancelledRole,
                currentTaskRunId,
                "The active task was cancelled before completion."
            );
        }
    }

    public void SetIdle()
    {
        bool alreadyIdle = CurrentTask == AgentTaskType.Idle &&
            TaskState == AgentTaskState.Running;

        if (IsTaskActive)
            CancelCurrentTask();

        if (alreadyIdle)
            return;

        CurrentTask = AgentTaskType.Idle;
        TaskState = AgentTaskState.Running;
        currentTaskRunId++;
        LogTaskSelected();
    }

    private void StartCurrentTask()
    {
        switch (CurrentTask)
        {
            case AgentTaskType.None:
                CompleteCurrentTask();
                return;
            case AgentTaskType.Idle:
                TaskState = AgentTaskState.Running;
                return;
            case AgentTaskType.GatherResource:
                StartGatherResourceTask();
                return;
            case AgentTaskType.ExploreFog:
                StartExploreFogTask();
                return;
            case AgentTaskType.CollectResource:
                StartCollectResourceTask();
                return;
            case AgentTaskType.DepositResources:
                StartDepositResourcesTask();
                return;
            case AgentTaskType.Build:
                StartBuildTask();
                return;
            case AgentTaskType.GoHome:
                StartGoHomeTask();
                return;
            case AgentTaskType.Sleep:
                StartSleepTask();
                return;
            default:
                FailCurrentTask($"Task has no shared executor yet: {CurrentTask}.");
                return;
        }
    }

    private void UpdateRunningTask()
    {
        switch (CurrentTask)
        {
            case AgentTaskType.Idle:
                return;
            case AgentTaskType.GatherResource:
                UpdateGatherResourceTask();
                return;
            case AgentTaskType.ExploreFog:
                UpdateExploreFogTask();
                return;
            case AgentTaskType.CollectResource:
                UpdateCollectResourceTask();
                return;
            case AgentTaskType.DepositResources:
                UpdateDepositResourcesTask();
                return;
            case AgentTaskType.Build:
                UpdateBuildTask();
                return;
            case AgentTaskType.GoHome:
                UpdateGoHomeTask();
                return;
            case AgentTaskType.Sleep:
                UpdateSleepTask();
                return;
        }
    }

    private void StartGatherResourceTask()
    {
        if (gatherResourceExecutor == null || !gatherResourceExecutor.StartGathering(requestedGatherResourceType, repeatGatherTask))
        {
            if (TryRestartManualPlanAfterRoleCouldNotBegin("GatherResource task failed to start."))
                return;

            FailCurrentTask("GatherResource task failed to start.");
            return;
        }

        TaskState = AgentTaskState.Running;
    }

    private void UpdateGatherResourceTask()
    {
        if (gatherResourceExecutor == null)
        {
            FailCurrentTask("GatherResource task lost its executor.");
            return;
        }

        if (gatherResourceExecutor.IsRunning)
            return;

        if (gatherResourceExecutor.IsCompleted)
        {
            if (gatherResourceExecutor.CompletedBecauseResourceUnavailable &&
                TryRestartManualPlanAfterRoleCouldNotBegin(
                    "GatherResource task found no currently reservable resource."
                ))
            {
                return;
            }

            CompleteCurrentTask();
            return;
        }

        if (gatherResourceExecutor.HasFailed)
        {
            if (TryRestartManualPlanAfterRoleCouldNotBegin("GatherResource task failed during execution."))
                return;

            FailCurrentTask("GatherResource task failed during execution.");
        }
    }

    private void StartCollectResourceTask()
    {
        if (resourceCollectionExecutor == null || !resourceCollectionExecutor.StartResourceCollection())
        {
            FailCurrentTask("CollectResource task failed to start.");
            return;
        }

        TaskState = AgentTaskState.Running;
    }

    private void StartExploreFogTask()
    {
        if (exploreFogExecutor == null ||
            !exploreFogExecutor.StartExploration(
                repeatExploreTask,
                hasAssignedExploreDirection,
                assignedExploreDirection))
        {
            if (TryRestartManualPlanAfterRoleCouldNotBegin("ExploreFog task failed to start."))
                return;

            FailCurrentTask("ExploreFog task failed to start.");
            return;
        }

        if (character != null)
            character.ShowExploreTool();

        TaskState = AgentTaskState.Running;
    }

    private void UpdateExploreFogTask()
    {
        if (exploreFogExecutor == null)
        {
            FailCurrentTask("ExploreFog task lost its executor.");
            return;
        }

        if (exploreFogExecutor.IsRunning)
            return;

        if (exploreFogExecutor.IsCompleted)
        {
            CompleteCurrentTask();
            return;
        }

        if (exploreFogExecutor.HasFailed)
        {
            if (TryRestartManualPlanAfterExploreExecutionFailed())
                return;

            FailCurrentTask("ExploreFog task failed during execution.");
        }
    }

    private void UpdateCollectResourceTask()
    {
        if (resourceCollectionExecutor == null)
        {
            FailCurrentTask("CollectResource task lost its executor.");
            return;
        }

        if (resourceCollectionExecutor.IsRunning)
            return;

        if (resourceCollectionExecutor.IsCompleted)
        {
            CompleteCurrentTask();
            return;
        }

        if (resourceCollectionExecutor.HasFailed)
            FailCurrentTask("CollectResource task failed during execution.");
    }

    private void StartDepositResourcesTask()
    {
        if (resourceDepositExecutor == null || !resourceDepositExecutor.StartDeposit())
        {
            if (resourceDepositExecutor != null &&
                resourceDepositExecutor.FailedBecauseStorageTemporarilyUnreachable)
            {
                depositRetryAllowedAtTotalWorldHours = GetStorageRetryAllowedAtTotalWorldHours();
                TaskState = AgentTaskState.Running;
                return;
            }

            FailCurrentTask("DepositResources task failed to start.");
            return;
        }

        TaskState = AgentTaskState.Running;
    }

    private void UpdateDepositResourcesTask()
    {
        if (resourceDepositExecutor == null)
        {
            FailCurrentTask("DepositResources task lost its executor.");
            return;
        }

        if (resourceDepositExecutor.IsRunning)
            return;

        if (resourceDepositExecutor.IsCompleted)
        {
            CompleteCurrentTask();
            return;
        }

        if (resourceDepositExecutor.HasFailed)
        {
            if (resourceDepositExecutor.FailedBecauseStorageTemporarilyUnreachable)
            {
                if (IsWaitingForStorageRetry(depositRetryAllowedAtTotalWorldHours))
                    return;

                StartDepositResourcesTask();
                return;
            }

            FailCurrentTask("DepositResources task failed during execution.");
        }
    }

    private void StartBuildTask()
    {
        if (!ManualDayPlanRoleSelector.TryGetClosestActiveConstructionSite(transform.position, out activeConstructionSite))
        {
            if (TryRestartManualPlanAfterRoleCouldNotBegin("Build task failed to start: no active construction site is available."))
                return;

            FailCurrentTask("Build task failed to start: no active construction site is available.");
            return;
        }

        if (character == null || !character.TryConstruct(activeConstructionSite))
        {
            ManualDayPlanRoleSelector.MarkConstructionSiteTemporarilyUnreachable(
                activeConstructionSite,
                transform.position,
                "Build task failed to start: construction movement could not begin."
            );

            if (TryRestartManualPlanAfterRoleCouldNotBegin("Build task failed to start: construction movement could not begin."))
                return;

            FailCurrentTask("Build task failed to start: construction movement could not begin.");
            return;
        }

        character.ShowBuildTool();
        TaskState = AgentTaskState.Running;
    }

    private void UpdateBuildTask()
    {
        if (activeConstructionSite == null)
        {
            // Finished sites destroy their Unity object at frame end. The retained
            // managed completion flag distinguishes that from an externally removed site.
            if (!ReferenceEquals(activeConstructionSite, null) && activeConstructionSite.IsComplete)
                CompletedConstructionCount++;
            activeConstructionSite = null;
            CompleteCurrentTask();
            return;
        }

        if (activeConstructionSite.IsComplete)
        {
            CompletedConstructionCount++;
            activeConstructionSite = null;
            CompleteCurrentTask();
            return;
        }

        if (character == null)
        {
            FailCurrentTask("Build task lost its Character component.");
            return;
        }

        if (character.IsActivelyConstructing(activeConstructionSite))
            return;

        if (character.HasMovementPathFailed())
        {
            ManualDayPlanRoleSelector.MarkConstructionSiteTemporarilyUnreachable(
                activeConstructionSite,
                transform.position,
                "Build task failed: movement path is invalid."
            );
            if (TryRestartManualPlanAfterRoleCouldNotBegin("Build task failed: movement path is invalid."))
                return;

            FailCurrentTask("Build task failed: movement path is invalid.");
            return;
        }

        if (character.HasMovementStalled(BuildMovementStuckWorldHours))
        {
            ManualDayPlanRoleSelector.MarkConstructionSiteTemporarilyUnreachable(
                activeConstructionSite,
                transform.position,
                "Build task failed: movement stalled before reaching the construction site."
            );

            if (TryRestartManualPlanAfterRoleCouldNotBegin(
                "Build task failed: movement stalled before reaching the construction site."
            ))
            {
                return;
            }

            FailCurrentTask("Build task failed: movement stalled before reaching the construction site.");
            return;
        }

        if (character.IsWorkingOnConstructionSite(activeConstructionSite))
            return;

        ManualDayPlanRoleSelector.MarkConstructionSiteTemporarilyUnreachable(
            activeConstructionSite,
            transform.position,
            "Build task stopped before construction completed."
        );
        if (TryRestartManualPlanAfterRoleCouldNotBegin("Build task stopped before construction completed."))
            return;

        FailCurrentTask("Build task stopped before construction completed.");
    }

    private void StartGoHomeTask()
    {
        goHomeTravelStarted = false;
        goHomeNeedsDailyUpkeep = NeedsDailyUpkeepBeforeSleep();
        goHomeDepositRetryAllowedAtTotalWorldHours = 0f;

        if (NeedsDepositBeforeGoHome() || goHomeNeedsDailyUpkeep)
        {
            if (StartGoHomeDepositPreparation())
                return;
        }

        StartGoHomeTravelTask();
    }

    private bool StartGoHomeDepositPreparation()
    {
        if (resourceDepositExecutor != null && resourceDepositExecutor.StartDeposit(goHomeNeedsDailyUpkeep))
        {
            if (!resourceDepositExecutor.IsCompleted)
            {
                TaskState = AgentTaskState.Running;
                return true;
            }

            ApplyDailyUpkeepAfterBarnVisit();
            return npcHealth != null && npcHealth.IsDead;
        }

        if (resourceDepositExecutor != null &&
            resourceDepositExecutor.FailedBecauseStorageTemporarilyUnreachable)
        {
            goHomeDepositRetryAllowedAtTotalWorldHours = GetStorageRetryAllowedAtTotalWorldHours();
            TaskState = AgentTaskState.Running;
            return true;
        }

        Debug.LogWarning($"GoHome task will continue without depositing because DepositResources could not start. GameObject: {gameObject.name}.");
        goHomeNeedsDailyUpkeep = false;
        return false;
    }

    private bool NeedsDepositBeforeGoHome()
    {
        return character != null && character.HasDepositLoad;
    }

    private void StartGoHomeTravelTask()
    {
        if (homeTravelExecutor == null || !homeTravelExecutor.StartGoHome())
        {
            FailCurrentTask("GoHome task failed to start.");
            return;
        }

        goHomeTravelStarted = true;
        TaskState = AgentTaskState.Running;
    }

    private void UpdateGoHomeTask()
    {
        if (!goHomeTravelStarted)
        {
            UpdateGoHomeDepositPreparation();
            return;
        }

        if (homeTravelExecutor == null)
        {
            FailCurrentTask("GoHome task lost its executor.");
            return;
        }

        if (homeTravelExecutor.IsRunning)
            return;

        if (homeTravelExecutor.IsCompleted)
        {
            CompleteCurrentTask();
            return;
        }

        if (homeTravelExecutor.HasFailed)
            FailCurrentTask("GoHome task failed during execution.");
    }

    private void UpdateGoHomeDepositPreparation()
    {
        if (resourceDepositExecutor != null && resourceDepositExecutor.IsRunning)
            return;

        if (resourceDepositExecutor != null &&
            resourceDepositExecutor.FailedBecauseStorageTemporarilyUnreachable)
        {
            if (IsWaitingForStorageRetry(goHomeDepositRetryAllowedAtTotalWorldHours))
                return;

            if (StartGoHomeDepositPreparation())
                return;

            StartGoHomeTravelTask();
            return;
        }

        if (resourceDepositExecutor != null && resourceDepositExecutor.HasFailed)
        {
            Debug.LogWarning($"GoHome task is continuing after DepositResources failed. GameObject: {gameObject.name}.");
            goHomeNeedsDailyUpkeep = false;
        }
        else
        {
            ApplyDailyUpkeepAfterBarnVisit();
            if (npcHealth != null && npcHealth.IsDead)
                return;
        }

        StartGoHomeTravelTask();
    }

    private void StartSleepTask()
    {
        if (NeedsDailyUpkeepBeforeSleep())
        {
            CurrentTask = AgentTaskType.GoHome;
            StartGoHomeTask();
            return;
        }

        if (sleepExecutor == null || !sleepExecutor.StartSleeping())
        {
            FailCurrentTask("Sleep task failed to start.");
            return;
        }

        TaskState = AgentTaskState.Running;
    }

    private bool NeedsDailyUpkeepBeforeSleep()
    {
        ResolveWorldTimeManager();

        return npcHealth != null &&
            worldTimeManager != null &&
            IsSleepWindowActive() &&
            npcHealth.NeedsDailyUpkeep(worldTimeManager, GetUpkeepDayStartHour());
    }

    private void ApplyDailyUpkeepAfterBarnVisit()
    {
        if (!goHomeNeedsDailyUpkeep)
            return;

        goHomeNeedsDailyUpkeep = false;

        ResolveWorldTimeManager();

        if (npcHealth != null && worldTimeManager != null)
            npcHealth.ApplyDailyUpkeep(worldTimeManager, GetUpkeepDayStartHour());
    }

    private float GetUpkeepDayStartHour()
    {
        AgentScheduleConfig schedule = workProfile != null && workProfile.Schedule != null
            ? workProfile.Schedule
            : new AgentScheduleConfig();

        return schedule.SleepEndHour;
    }

    private void UpdateSleepTask()
    {
        if (sleepExecutor == null)
        {
            FailCurrentTask("Sleep task lost its executor.");
            return;
        }

        if (sleepExecutor.IsCompleted)
        {
            CompleteCurrentTask();
            return;
        }

        if (sleepExecutor.HasFailed)
            FailCurrentTask("Sleep task failed during execution.");
    }

    private void ResetCompletedSleepForDayTask(AgentTaskType taskType)
    {
        if (sleepExecutor == null)
            return;

        if (IsSleepWindowActive())
            return;

        switch (taskType)
        {
            case AgentTaskType.GatherResource:
            case AgentTaskType.CollectResource:
            case AgentTaskType.DepositResources:
            case AgentTaskType.ExploreFog:
            case AgentTaskType.Build:
                sleepExecutor.ResetCompletedSleep();
                break;
        }
    }

    private bool IsSleepWindowActive()
    {
        ResolveWorldTimeManager();

        if (worldTimeManager == null)
            return false;

        AgentScheduleConfig schedule = workProfile != null && workProfile.Schedule != null
            ? workProfile.Schedule
            : new AgentScheduleConfig();

        return schedule.IsSleepTime(worldTimeManager.CurrentHour);
    }

    private void ResolveWorldTimeManager()
    {
        if (worldTimeManager != null)
            return;

        worldTimeManager = FindFirstObjectByType<WorldTimeManager>();
    }

    private void CompleteCurrentTask()
    {
        AgentTaskType completedTask = CurrentTask;
        bool completedTaskHadRole = hasAssignedWorkRole;
        NpcWorkRole completedRole = assignedWorkRole;
        ClearJobTools();

        if (!currentTaskUsesManualDayPlan)
            ReleaseAssignedWorkRole();

        currentTaskUsesManualDayPlan = false;
        CurrentTask = CurrentTask == AgentTaskType.None ? AgentTaskType.None : CurrentTask;
        TaskState = AgentTaskState.Completed;

        SimulationEventLogger.LogTaskEvent(
            identity,
            "TaskCompleted",
            completedTask,
            TaskState,
            completedTaskHadRole,
            completedRole,
            currentTaskRunId
        );
    }

    private void FailCurrentTask(string reason)
    {
        AgentTaskType failedTask = CurrentTask;
        bool failedTaskHadRole = hasAssignedWorkRole;
        NpcWorkRole failedRole = assignedWorkRole;
        Debug.LogWarning($"{reason} GameObject: {gameObject.name}.");
        CleanupCurrentTask();
        TaskState = AgentTaskState.Failed;

        SimulationEventLogger.LogTaskEvent(
            identity,
            "TaskFailed",
            failedTask,
            TaskState,
            failedTaskHadRole,
            failedRole,
            currentTaskRunId,
            reason
        );
    }

    private void LogTaskSelected()
    {
        SimulationEventLogger.LogTaskEvent(
            identity,
            "TaskSelected",
            CurrentTask,
            TaskState,
            hasAssignedWorkRole,
            assignedWorkRole,
            currentTaskRunId
        );
    }

    private void CleanupCurrentTask()
    {
        ClearJobTools();

        switch (CurrentTask)
        {
            case AgentTaskType.GatherResource:
                if (gatherResourceExecutor != null)
                    gatherResourceExecutor.CancelGathering();

                if (resourceReservation != null)
                    resourceReservation.ReleaseResource();

                break;
            case AgentTaskType.ExploreFog:
                if (exploreFogExecutor != null)
                    exploreFogExecutor.CancelExploration();

                break;
            case AgentTaskType.CollectResource:
                if (resourceCollectionExecutor != null)
                    resourceCollectionExecutor.CancelCollection();

                if (resourceReservation != null)
                    resourceReservation.ReleaseResource();

                break;
            case AgentTaskType.DepositResources:
                if (resourceDepositExecutor != null)
                    resourceDepositExecutor.CancelDeposit();

                depositRetryAllowedAtTotalWorldHours = 0f;
                break;
            case AgentTaskType.Build:
                activeConstructionSite = null;
                break;
            case AgentTaskType.GoHome:
                if (resourceDepositExecutor != null && resourceDepositExecutor.IsRunning)
                    resourceDepositExecutor.CancelDeposit();

                if (homeTravelExecutor != null)
                    homeTravelExecutor.CancelGoHome();

                goHomeTravelStarted = false;
                goHomeDepositRetryAllowedAtTotalWorldHours = 0f;
                break;
            case AgentTaskType.Sleep:
                if (sleepExecutor != null)
                    sleepExecutor.StopSleeping();

                break;
        }

        if (character != null && CurrentTask != AgentTaskType.Idle)
            character.StopMovement();

        ReleaseAssignedWorkRole();
        currentTaskUsesManualDayPlan = false;
        hasAssignedExploreDirection = false;
    }

    private void ClearJobTools()
    {
        if (character != null)
            character.ClearJobTools();
    }

    private bool TryRestartManualPlanAfterRoleCouldNotBegin(string reason)
    {
        if (!currentTaskUsesManualDayPlan || !hasAssignedWorkRole)
            return false;

        SimulationPerformanceCounters.AddManualTaskRestartAttempt();
        SimulationPerformanceCounters.AddRoleCouldNotBeginRestart();
        RestartManualPlanAfterRoleFailure(reason);
        return true;
    }

    private bool TryRestartManualPlanAfterExploreExecutionFailed()
    {
        if (!currentTaskUsesManualDayPlan ||
            !hasAssignedWorkRole ||
            assignedWorkRole != NpcWorkRole.Explore)
        {
            return false;
        }

        SimulationPerformanceCounters.AddManualTaskRestartAttempt();
        SimulationPerformanceCounters.AddExploreExecutionFailedRestart();
        RestartManualPlanAfterRoleFailure("ExploreFog task failed during execution.");
        return true;
    }

    private void RestartManualPlanAfterRoleFailure(string reason)
    {
        NpcWorkRole failedRole = assignedWorkRole;
        manualFallbackRejectedRoles.Add(failedRole);
        SetGlobalLoopFlagIfRoleIsExhausted(failedRole, reason);

        CleanupCurrentTask();
        CurrentTask = AgentTaskType.Idle;
        TaskState = AgentTaskState.Running;

        TryStartManualFallbackTask(reason);
    }

    private bool TryStartManualFallbackTask(string failureContext)
    {
        while (ManualDayPlanRoleSelector.TryAssignFallbackRole(
            identity,
            manualFallbackRejectedRoles,
            out NpcWorkRole fallbackRole,
            out string fallbackReason))
        {
            assignedWorkRole = fallbackRole;
            hasAssignedWorkRole = true;
            currentTaskUsesManualDayPlan = true;

            if (TryStartWorkRoleTask(fallbackRole, repeatManualDayPlanTask, true))
            {
                LastManualDayPlanFailureReason = string.Empty;
                return true;
            }

            manualFallbackRejectedRoles.Add(fallbackRole);
            SetGlobalLoopFlagIfRoleIsExhausted(fallbackRole, fallbackReason);
            ReleaseAssignedWorkRole();
            currentTaskUsesManualDayPlan = false;
        }

        LastManualDayPlanFailureReason =
            $"{failureContext} No local fallback role could start.";
        return false;
    }

    private void SetGlobalLoopFlagIfRoleIsExhausted(NpcWorkRole role, string failureContext)
    {
        if (!ManualDayPlanRoleSelector.IsRoleGloballyExhausted(role, out string unavailableReason))
            return;

        ManualDayPlanState.SetLoopFlag(role);
        Debug.LogWarning(
            $"{failureContext} Manual day plan set LoopFlag for globally exhausted role {role}: " +
            $"{unavailableReason} GameObject: {gameObject.name}."
        );
    }

    private void RestoreAssignedWorkRoleFromRegistry()
    {
        if (NpcWorkRoleAssignmentRegistry.TryGetAssignedRole(identity, out NpcWorkRole registryRole))
        {
            assignedWorkRole = registryRole;
            hasAssignedWorkRole = true;
            return;
        }

        hasAssignedWorkRole = false;
        assignedWorkRole = NpcWorkRole.Food;
        hasAssignedExploreDirection = false;
        assignedExploreDirection = ExploreDirection.North;
    }

    private float GetStorageRetryAllowedAtTotalWorldHours()
    {
        ResolveWorldTimeManager();
        return worldTimeManager != null
            ? worldTimeManager.TotalWorldHours + StorageRetryDelayWorldHours
            : 0f;
    }

    private bool IsWaitingForStorageRetry(float retryAllowedAtTotalWorldHours)
    {
        ResolveWorldTimeManager();
        return worldTimeManager != null &&
            worldTimeManager.TotalWorldHours < retryAllowedAtTotalWorldHours;
    }

    private void ReleaseAssignedWorkRole()
    {
        if (!hasAssignedWorkRole)
            return;

        NpcWorkRoleAssignmentRegistry.Release(identity);
        hasAssignedWorkRole = false;
        assignedWorkRole = NpcWorkRole.Food;
        hasAssignedExploreDirection = false;
        assignedExploreDirection = ExploreDirection.North;
    }
}
