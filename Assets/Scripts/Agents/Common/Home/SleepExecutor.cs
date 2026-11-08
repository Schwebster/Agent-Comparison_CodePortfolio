/*
===============================================================================
Script:
SleepExecutor

Purpose:
Executes shared sleep state for an NPC at its home or temporary barn shelter.

Folder:
Assets/Scripts/Agents/Common/Home/

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
- NpcHomeAssignment
- HomeTravelExecutor
- NpcSleepHider
- WorldTimeManager
- Animator

Automatically Resolved:
- Character on the same GameObject
- NpcHomeAssignment on the same GameObject
- NpcSleepHider on the same GameObject
- WorldTimeManager in the scene, if not assigned
- Animator in children, if present

Inspector References:
- sleepDurationWorldHours
- worldTimeManager
- useAnimatorSleepBool
- sleepAnimatorBoolName
===============================================================================
*/

using UnityEngine;

public enum SleepExecutionState
{
    Idle,
    Sleeping,
    Completed,
    Failed
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(NpcHomeAssignment))]
[RequireComponent(typeof(NpcSleepHider))]
public class SleepExecutor : MonoBehaviour
{
    [SerializeField] private float sleepDurationWorldHours = 8f;
    [SerializeField] private WorldTimeManager worldTimeManager;
    [SerializeField] private bool useAnimatorSleepBool;
    [SerializeField] private string sleepAnimatorBoolName = "IsSleeping";
    [SerializeField] private Animator animator;

    private Character character;
    private NpcHomeAssignment homeAssignment;
    private HomeTravelExecutor homeTravelExecutor;
    private NpcSleepHider sleepHider;
    private float sleepStartTotalWorldHours;
    private bool hasSleepStartTime;
    private bool missingWorldTimeManagerWarningLogged;
    private bool sleepingAtBarnShelter;

    public SleepExecutionState State { get; private set; } = SleepExecutionState.Idle;
    public bool IsSleeping => State == SleepExecutionState.Sleeping;
    public bool IsCompleted => State == SleepExecutionState.Completed;
    public bool HasFailed => State == SleepExecutionState.Failed;
    public float RequiredSleepWorldHours => Mathf.Max(0f, sleepDurationWorldHours);
    public float SleepElapsedWorldHours => IsCompleted
        ? RequiredSleepWorldHours
        : IsSleeping && hasSleepStartTime && worldTimeManager != null
        ? Mathf.Max(0f, worldTimeManager.TotalWorldHours - sleepStartTotalWorldHours)
        : 0f;
    public float SleepRemainingWorldHours => Mathf.Max(0f, RequiredSleepWorldHours - SleepElapsedWorldHours);
    public bool HasCompletedRequiredSleep => IsCompleted || ((IsSleeping || IsCompleted) && SleepElapsedWorldHours >= RequiredSleepWorldHours);

    private void Awake()
    {
        character = GetComponent<Character>();
        homeAssignment = GetComponent<NpcHomeAssignment>();
        homeTravelExecutor = GetComponent<HomeTravelExecutor>();
        sleepHider = GetComponent<NpcSleepHider>();
        animator = animator != null ? animator : GetComponentInChildren<Animator>(true);
        ResolveWorldTimeManager();

        Debug.Assert(
            character != null,
            $"Missing component: Character. Expected location: same GameObject as SleepExecutor. Script: {nameof(SleepExecutor)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            homeAssignment != null,
            $"Missing component: NpcHomeAssignment. Expected location: same GameObject as SleepExecutor. Script: {nameof(SleepExecutor)}. GameObject: {gameObject.name}."
        );

        Debug.Assert(
            sleepHider != null,
            $"Missing component: NpcSleepHider. Expected location: same GameObject as SleepExecutor. Script: {nameof(SleepExecutor)}. GameObject: {gameObject.name}."
        );
    }

    private void OnDisable()
    {
        if (IsSleeping)
            StopSleeping();
    }

    private void Update()
    {
        if (!IsSleeping)
            return;

        ResolveWorldTimeManager();

        if (sleepingAtBarnShelter)
        {
            if (homeTravelExecutor == null || !homeTravelExecutor.HasActiveBarnShelter)
            {
                Fail("Sleep execution failed: overnight barn shelter became invalid.");
                return;
            }
        }
        else if (homeAssignment == null || !homeAssignment.HasHome)
        {
            Fail("Sleep execution failed: assigned home became invalid.");
            return;
        }

        if (HasCompletedRequiredSleep)
            CompleteSleeping();
    }

    public bool StartSleeping()
    {
        if (IsSleeping)
            return true;

        bool isAtValidHome =
            homeAssignment != null &&
            homeAssignment.HasHome &&
            homeAssignment.IsAtHome;
        bool isAtValidBarnShelter =
            !isAtValidHome &&
            homeTravelExecutor != null &&
            homeTravelExecutor.IsAtBarnShelter;

        if (!isAtValidHome && !isAtValidBarnShelter)
        {
            Fail("Sleep execution failed: NPC is not at a valid home or overnight barn shelter.");
            return false;
        }

        sleepingAtBarnShelter = isAtValidBarnShelter;

        if (!TryRecordSleepStartTime())
            return false;

        if (character != null)
            character.StopMovement();

        SetSleepAnimation(true);
        if (!HideSleepingNpc())
        {
            Fail("Sleep execution failed: NPC could not be hidden at a valid NavMesh position.");
            return false;
        }

        State = SleepExecutionState.Sleeping;
        SimulationPerformanceCounters.AddSleepStart();

        if (sleepingAtBarnShelter)
            SimulationPerformanceCounters.AddBarnShelterSleepStart();

        return true;
    }

    public void StopSleeping()
    {
        ShowSleepingNpc();
        SetSleepAnimation(false);
        ClearTemporarySleepLocation();

        if (IsCompleted)
            return;

        hasSleepStartTime = false;
        State = SleepExecutionState.Idle;
    }

    public void ResetCompletedSleep()
    {
        if (!IsCompleted)
            return;

        hasSleepStartTime = false;
        ClearTemporarySleepLocation();
        State = SleepExecutionState.Idle;
    }

    private void Fail(string message)
    {
        Debug.LogWarning($"{message} GameObject: {gameObject.name}.");
        ShowSleepingNpc();
        SetSleepAnimation(false);
        hasSleepStartTime = false;
        ClearTemporarySleepLocation();
        State = SleepExecutionState.Failed;
    }

    private void CompleteSleeping()
    {
        bool navMeshRestored = ShowSleepingNpc();
        SetSleepAnimation(false);
        ClearTemporarySleepLocation();

        if (!navMeshRestored)
        {
            hasSleepStartTime = false;
            State = SleepExecutionState.Failed;
            Debug.LogWarning($"Sleep completion failed to restore the NPC to the NavMesh. GameObject: {gameObject.name}.");
            return;
        }

        State = SleepExecutionState.Completed;
    }

    private void ClearTemporarySleepLocation()
    {
        if (sleepingAtBarnShelter && homeTravelExecutor != null)
            homeTravelExecutor.ClearBarnShelter();

        sleepingAtBarnShelter = false;
    }

    private bool HideSleepingNpc()
    {
        return sleepHider == null || sleepHider.Hide();
    }

    private bool ShowSleepingNpc()
    {
        return sleepHider == null || sleepHider.Show();
    }

    private void SetSleepAnimation(bool isSleeping)
    {
        if (!useAnimatorSleepBool || animator == null || string.IsNullOrWhiteSpace(sleepAnimatorBoolName))
            return;

        animator.SetBool(sleepAnimatorBoolName, isSleeping);
    }

    private bool TryRecordSleepStartTime()
    {
        ResolveWorldTimeManager();

        if (worldTimeManager == null)
        {
            if (!missingWorldTimeManagerWarningLogged)
            {
                Debug.LogWarning($"Sleep execution failed: WorldTimeManager is missing. GameObject: {gameObject.name}.");
                missingWorldTimeManagerWarningLogged = true;
            }

            Fail("Sleep execution failed: no WorldTimeManager is available.");
            return false;
        }

        sleepStartTotalWorldHours = worldTimeManager.TotalWorldHours;
        hasSleepStartTime = true;
        return true;
    }

    private void ResolveWorldTimeManager()
    {
        if (worldTimeManager != null)
            return;

        worldTimeManager = FindFirstObjectByType<WorldTimeManager>();
    }

    private void OnValidate()
    {
        sleepDurationWorldHours = Mathf.Max(0f, sleepDurationWorldHours);
    }
}
