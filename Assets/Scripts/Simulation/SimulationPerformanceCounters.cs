/*
===============================================================================
Script:
SimulationPerformanceCounters

Purpose:
Tracks lightweight runtime counters for expensive simulation operations.

Folder:
Assets/Scripts/Simulation/

Scene Hierarchy:
None

Attach To:
None

Required Children:
None

Dependencies:
None

Automatically Resolved:
None

Inspector References:
None
===============================================================================
*/

using UnityEngine;

public static class SimulationPerformanceCounters
{
    private const float SummaryIntervalSeconds = 1f;

    private static bool enabled;
    private static bool logSummaries;
    private static float nextSummaryTime;

    private static int fogQueries;
    private static int fogCandidatesChecked;
    private static int fogSamplePositionCalls;
    private static int fogCalculatePathCalls;
    private static int fogKnownCellPathChecks;
    private static int fogUnknownCellPathRejections;
    private static int resourceQueries;
    private static int resourceCandidatesChecked;
    private static int resourceSamplePositionCalls;
    private static int resourceCalculatePathCalls;
    private static int manualTaskStartAttempts;
    private static int manualTaskRestartAttempts;
    private static int roleCouldNotBeginRestarts;
    private static int exploreExecutionFailedRestarts;
    private static int exploreExecutionFailures;
    private static int gatherResourceReservationFailures;
    private static int diagnosticLogAttempts;
    private static int fogUnreachableCacheSkips;
    private static int resourceUnreachableCacheSkips;
    private static int exploreAvailabilityCheapChecks;
    private static int resourceAvailabilityCheapChecks;
    private static int resourceMovementFailuresMarkedUnreachable;
    private static int fogMovementFailuresMarkedUnreachable;
    private static int barnMovementFailureCooldowns;
    private static int storageMovementFailureCooldowns;
    private static int sameTargetRetryPrevented;
    private static int exploreDirectionAssignments;
    private static int exploreDirectionalSearchFailures;
    private static int exploreFallbackSearchSuccesses;
    private static int exploreNoTargetWaits;
    private static int exploreCooperativeRevealContributions;
    private static int exploreTargetsAlreadyRevealed;
    private static int agentContextBuilds;
    private static int roleSelectionAttempts;
    private static int activeVillagerCountScans;
    private static int activeVillagerCountCacheHits;
    private static int roleAvailabilityCacheHits;
    private static int roleAvailabilityCacheMisses;
    private static int foodAvailabilityChecks;
    private static int woodAvailabilityChecks;
    private static int exploreAvailabilityChecks;
    private static int buildAvailabilityChecks;
    private static int constructionSiteScansAvoided;
    private static int resourceAvailabilityScansAvoided;
    private static int fogAvailabilityScansAvoided;
    private static int agentContextComponentCacheHits;
    private static int agentContextComponentCacheMisses;
    private static int agentContextFallbackLookups;
    private static int agentContextReuses;
    private static int agentContextRebuildsAfterMutation;
    private static int resourceCacheHits;
    private static int resourceCacheMisses;
    private static int resourceCandidatesFromCache;
    private static int resourceFullScansAvoided;
    private static int resourceNearestCandidatesChecked;
    private static int resourcePathChecksSkippedByCheapFilters;
    private static int homeTravelStarts;
    private static int homeArrivals;
    private static int homeMovementStalls;
    private static int homeReassignmentAttempts;
    private static int homePathCalculations;
    private static int sleepStarts;
    private static int wakeRestorations;
    private static int offNavMeshWakeDetections;
    private static int navMeshRecoverySuccesses;
    private static int navMeshRecoveryFailures;
    private static int nightAgentContextBuilds;
    private static int nightAgentContextBuildsAvoided;
    private static int homelessShelterRequests;
    private static int barnShelterTravelStarts;
    private static int barnShelterArrivals;
    private static int barnShelterLookupFailures;
    private static int barnShelterCooldowns;
    private static int barnShelterSleepStarts;
    private static int visibleHomelessNpcsDuringSleep;

    public static bool Enabled => enabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayModeReload()
    {
        enabled = false;
        logSummaries = false;
        nextSummaryTime = 0f;
        ResetCounts();
    }

    public static void Configure(bool countersEnabled, bool summariesEnabled)
    {
        enabled = countersEnabled;
        logSummaries = countersEnabled && summariesEnabled;

        if (!enabled)
            ResetCounts();
    }

    public static void AddFogQuery() => Add(ref fogQueries);
    public static void AddFogCandidatesChecked(int count) => Add(ref fogCandidatesChecked, count);
    public static void AddFogSamplePositionCall() => Add(ref fogSamplePositionCalls);
    public static void AddFogCalculatePathCall() => Add(ref fogCalculatePathCalls);
    public static void AddFogKnownCellPathCheck() => Add(ref fogKnownCellPathChecks);
    public static void AddFogUnknownCellPathRejection() => Add(ref fogUnknownCellPathRejections);
    public static void AddResourceQuery() => Add(ref resourceQueries);
    public static void AddResourceCandidatesChecked(int count) => Add(ref resourceCandidatesChecked, count);
    public static void AddResourceSamplePositionCall() => Add(ref resourceSamplePositionCalls);
    public static void AddResourceCalculatePathCall() => Add(ref resourceCalculatePathCalls);
    public static void AddManualTaskStartAttempt() => Add(ref manualTaskStartAttempts);
    public static void AddManualTaskRestartAttempt() => Add(ref manualTaskRestartAttempts);
    public static void AddRoleCouldNotBeginRestart() => Add(ref roleCouldNotBeginRestarts);
    public static void AddExploreExecutionFailedRestart() => Add(ref exploreExecutionFailedRestarts);
    public static void AddExploreExecutionFailure() => Add(ref exploreExecutionFailures);
    public static void AddGatherResourceReservationFailure() => Add(ref gatherResourceReservationFailures);
    public static void AddDiagnosticLogAttempt() => Add(ref diagnosticLogAttempts);
    public static void AddFogUnreachableCacheSkip() => Add(ref fogUnreachableCacheSkips);
    public static void AddResourceUnreachableCacheSkip() => Add(ref resourceUnreachableCacheSkips);
    public static void AddExploreAvailabilityCheapCheck() => Add(ref exploreAvailabilityCheapChecks);
    public static void AddResourceAvailabilityCheapCheck() => Add(ref resourceAvailabilityCheapChecks);
    public static void AddResourceMovementFailureMarkedUnreachable() => Add(ref resourceMovementFailuresMarkedUnreachable);
    public static void AddFogMovementFailureMarkedUnreachable() => Add(ref fogMovementFailuresMarkedUnreachable);
    public static void AddBarnMovementFailureCooldown() => Add(ref barnMovementFailureCooldowns);
    public static void AddStorageMovementFailureCooldown() => Add(ref storageMovementFailureCooldowns);
    public static void AddSameTargetRetryPrevented() => Add(ref sameTargetRetryPrevented);
    public static void AddExploreDirectionAssignment() => Add(ref exploreDirectionAssignments);
    public static void AddExploreDirectionalSearchFailure() => Add(ref exploreDirectionalSearchFailures);
    public static void AddExploreFallbackSearchSuccess() => Add(ref exploreFallbackSearchSuccesses);
    public static void AddExploreNoTargetWait() => Add(ref exploreNoTargetWaits);
    public static void AddExploreCooperativeRevealContribution() => Add(ref exploreCooperativeRevealContributions);
    public static void AddExploreTargetAlreadyRevealed() => Add(ref exploreTargetsAlreadyRevealed);
    public static void AddAgentContextBuild() => Add(ref agentContextBuilds);
    public static void AddRoleSelectionAttempt() => Add(ref roleSelectionAttempts);
    public static void AddActiveVillagerCountScan() => Add(ref activeVillagerCountScans);
    public static void AddActiveVillagerCountCacheHit() => Add(ref activeVillagerCountCacheHits);
    public static void AddRoleAvailabilityCacheHit() => Add(ref roleAvailabilityCacheHits);
    public static void AddRoleAvailabilityCacheMiss() => Add(ref roleAvailabilityCacheMisses);
    public static void AddFoodAvailabilityCheck() => Add(ref foodAvailabilityChecks);
    public static void AddWoodAvailabilityCheck() => Add(ref woodAvailabilityChecks);
    public static void AddExploreAvailabilityCheck() => Add(ref exploreAvailabilityChecks);
    public static void AddBuildAvailabilityCheck() => Add(ref buildAvailabilityChecks);
    public static void AddConstructionSiteScansAvoided() => Add(ref constructionSiteScansAvoided);
    public static void AddResourceAvailabilityScansAvoided() => Add(ref resourceAvailabilityScansAvoided);
    public static void AddFogAvailabilityScansAvoided() => Add(ref fogAvailabilityScansAvoided);
    public static void AddAgentContextComponentCacheHit() => Add(ref agentContextComponentCacheHits);
    public static void AddAgentContextComponentCacheMiss() => Add(ref agentContextComponentCacheMisses);
    public static void AddAgentContextFallbackLookup() => Add(ref agentContextFallbackLookups);
    public static void AddAgentContextReuse() => Add(ref agentContextReuses);
    public static void AddAgentContextRebuildAfterMutation() => Add(ref agentContextRebuildsAfterMutation);
    public static void AddResourceCacheHit() => Add(ref resourceCacheHits);
    public static void AddResourceCacheMiss() => Add(ref resourceCacheMisses);
    public static void AddResourceCandidatesFromCache(int count) => Add(ref resourceCandidatesFromCache, count);
    public static void AddResourceFullScanAvoided() => Add(ref resourceFullScansAvoided);
    public static void AddResourceNearestCandidateChecked() => Add(ref resourceNearestCandidatesChecked);
    public static void AddResourcePathCheckSkippedByCheapFilter() => Add(ref resourcePathChecksSkippedByCheapFilters);
    public static void AddHomeTravelStart() => Add(ref homeTravelStarts);
    public static void AddHomeArrival() => Add(ref homeArrivals);
    public static void AddHomeMovementStall() => Add(ref homeMovementStalls);
    public static void AddHomeReassignmentAttempt() => Add(ref homeReassignmentAttempts);
    public static void AddHomePathCalculation() => Add(ref homePathCalculations);
    public static void AddSleepStart() => Add(ref sleepStarts);
    public static void AddWakeRestoration() => Add(ref wakeRestorations);
    public static void AddOffNavMeshWakeDetection() => Add(ref offNavMeshWakeDetections);
    public static void AddNavMeshRecoverySuccess() => Add(ref navMeshRecoverySuccesses);
    public static void AddNavMeshRecoveryFailure() => Add(ref navMeshRecoveryFailures);
    public static void AddNightAgentContextBuild() => Add(ref nightAgentContextBuilds);
    public static void AddNightAgentContextBuildAvoided() => Add(ref nightAgentContextBuildsAvoided);
    public static void AddHomelessShelterRequest() => Add(ref homelessShelterRequests);
    public static void AddBarnShelterTravelStart() => Add(ref barnShelterTravelStarts);
    public static void AddBarnShelterArrival() => Add(ref barnShelterArrivals);
    public static void AddBarnShelterLookupFailure() => Add(ref barnShelterLookupFailures);
    public static void AddBarnShelterCooldown() => Add(ref barnShelterCooldowns);
    public static void AddBarnShelterSleepStart() => Add(ref barnShelterSleepStarts);
    public static void AddVisibleHomelessNpcDuringSleep() => Add(ref visibleHomelessNpcsDuringSleep);

    private static void Add(ref int counter, int amount = 1)
    {
        if (!enabled)
            return;

        counter += Mathf.Max(0, amount);
        MaybeLogSummary();
    }

    private static void MaybeLogSummary()
    {
        if (!logSummaries)
            return;

        float currentTime = Time.realtimeSinceStartup;
        if (currentTime < nextSummaryTime)
            return;

        nextSummaryTime = currentTime + SummaryIntervalSeconds;

        Debug.Log(
            "SimulationPerformanceCounters/sec | " +
            $"FogQueries={fogQueries} | FogCandidates={fogCandidatesChecked} | " +
            $"FogSamplePosition={fogSamplePositionCalls} | FogCalculatePath={fogCalculatePathCalls} | " +
            $"FogKnownCellPathChecks={fogKnownCellPathChecks} | " +
            $"FogUnknownCellPathRejections={fogUnknownCellPathRejections} | " +
            $"ResourceQueries={resourceQueries} | ResourceCandidates={resourceCandidatesChecked} | " +
            $"ResourceSamplePosition={resourceSamplePositionCalls} | ResourceCalculatePath={resourceCalculatePathCalls} | " +
            $"ManualTaskStarts={manualTaskStartAttempts} | ManualTaskRestarts={manualTaskRestartAttempts} | " +
            $"RoleCouldNotBeginRestarts={roleCouldNotBeginRestarts} | ExploreFailedRestarts={exploreExecutionFailedRestarts} | " +
            $"ExploreFailures={exploreExecutionFailures} | GatherReservationFailures={gatherResourceReservationFailures} | " +
            $"DiagnosticLogAttempts={diagnosticLogAttempts} | FogUnreachableCacheSkips={fogUnreachableCacheSkips} | " +
            $"ResourceUnreachableCacheSkips={resourceUnreachableCacheSkips} | " +
            $"ExploreAvailabilityCheapChecks={exploreAvailabilityCheapChecks} | ResourceAvailabilityCheapChecks={resourceAvailabilityCheapChecks} | " +
            $"ResourceMovementFailureMarkedUnreachable={resourceMovementFailuresMarkedUnreachable} | " +
            $"FogMovementFailureMarkedUnreachable={fogMovementFailuresMarkedUnreachable} | " +
            $"BarnMovementFailureCooldowns={barnMovementFailureCooldowns} | " +
            $"StorageMovementFailureCooldowns={storageMovementFailureCooldowns} | " +
            $"SameTargetRetryPrevented={sameTargetRetryPrevented} | " +
            $"ExploreDirectionAssignments={exploreDirectionAssignments} | " +
            $"ExploreDirectionalSearchFailures={exploreDirectionalSearchFailures} | " +
            $"ExploreFallbackSearchSuccesses={exploreFallbackSearchSuccesses} | " +
            $"ExploreNoTargetWaits={exploreNoTargetWaits} | " +
            $"ExploreCooperativeRevealContributions={exploreCooperativeRevealContributions} | " +
            $"ExploreTargetsAlreadyRevealed={exploreTargetsAlreadyRevealed} | " +
            $"AgentContextBuilds={agentContextBuilds} | " +
            $"RoleSelectionAttempts={roleSelectionAttempts} | " +
            $"ActiveVillagerCountScans={activeVillagerCountScans} | " +
            $"ActiveVillagerCountCacheHits={activeVillagerCountCacheHits} | " +
            $"RoleAvailabilityCacheHits={roleAvailabilityCacheHits} | " +
            $"RoleAvailabilityCacheMisses={roleAvailabilityCacheMisses} | " +
            $"FoodAvailabilityChecks={foodAvailabilityChecks} | " +
            $"WoodAvailabilityChecks={woodAvailabilityChecks} | " +
            $"ExploreAvailabilityChecks={exploreAvailabilityChecks} | " +
            $"BuildAvailabilityChecks={buildAvailabilityChecks} | " +
            $"ConstructionSiteScansAvoided={constructionSiteScansAvoided} | " +
            $"ResourceAvailabilityScansAvoided={resourceAvailabilityScansAvoided} | " +
            $"FogAvailabilityScansAvoided={fogAvailabilityScansAvoided} | " +
            $"AgentContextComponentCacheHits={agentContextComponentCacheHits} | " +
            $"AgentContextComponentCacheMisses={agentContextComponentCacheMisses} | " +
            $"AgentContextFallbackLookups={agentContextFallbackLookups} | " +
            $"AgentContextReuses={agentContextReuses} | " +
            $"AgentContextRebuildsAfterMutation={agentContextRebuildsAfterMutation} | " +
            $"ResourceCacheHits={resourceCacheHits} | " +
            $"ResourceCacheMisses={resourceCacheMisses} | " +
            $"ResourceCandidatesFromCache={resourceCandidatesFromCache} | " +
            $"ResourceFullScansAvoided={resourceFullScansAvoided} | " +
            $"ResourceNearestCandidatesChecked={resourceNearestCandidatesChecked} | " +
            $"ResourcePathChecksSkippedByCheapFilters={resourcePathChecksSkippedByCheapFilters} | " +
            $"HomeTravelStarts={homeTravelStarts} | HomeArrivals={homeArrivals} | " +
            $"HomeMovementStalls={homeMovementStalls} | HomeReassignments={homeReassignmentAttempts} | " +
            $"HomePathCalculations={homePathCalculations} | SleepStarts={sleepStarts} | " +
            $"WakeRestorations={wakeRestorations} | OffNavMeshWakes={offNavMeshWakeDetections} | " +
            $"NavMeshRecoverySuccesses={navMeshRecoverySuccesses} | NavMeshRecoveryFailures={navMeshRecoveryFailures} | " +
            $"NightAgentContextBuilds={nightAgentContextBuilds} | " +
            $"NightAgentContextBuildsAvoided={nightAgentContextBuildsAvoided} | " +
            $"HomelessShelterRequests={homelessShelterRequests} | " +
            $"BarnShelterTravelStarts={barnShelterTravelStarts} | BarnShelterArrivals={barnShelterArrivals} | " +
            $"BarnShelterLookupFailures={barnShelterLookupFailures} | BarnShelterCooldowns={barnShelterCooldowns} | " +
            $"BarnShelterSleepStarts={barnShelterSleepStarts} | " +
            $"VisibleHomelessNpcsDuringSleep={visibleHomelessNpcsDuringSleep}"
        );

        ResetCounts();
    }

    private static void ResetCounts()
    {
        fogQueries = 0;
        fogCandidatesChecked = 0;
        fogSamplePositionCalls = 0;
        fogCalculatePathCalls = 0;
        fogKnownCellPathChecks = 0;
        fogUnknownCellPathRejections = 0;
        resourceQueries = 0;
        resourceCandidatesChecked = 0;
        resourceSamplePositionCalls = 0;
        resourceCalculatePathCalls = 0;
        manualTaskStartAttempts = 0;
        manualTaskRestartAttempts = 0;
        roleCouldNotBeginRestarts = 0;
        exploreExecutionFailedRestarts = 0;
        exploreExecutionFailures = 0;
        gatherResourceReservationFailures = 0;
        diagnosticLogAttempts = 0;
        fogUnreachableCacheSkips = 0;
        resourceUnreachableCacheSkips = 0;
        exploreAvailabilityCheapChecks = 0;
        resourceAvailabilityCheapChecks = 0;
        resourceMovementFailuresMarkedUnreachable = 0;
        fogMovementFailuresMarkedUnreachable = 0;
        barnMovementFailureCooldowns = 0;
        storageMovementFailureCooldowns = 0;
        sameTargetRetryPrevented = 0;
        exploreDirectionAssignments = 0;
        exploreDirectionalSearchFailures = 0;
        exploreFallbackSearchSuccesses = 0;
        exploreNoTargetWaits = 0;
        exploreCooperativeRevealContributions = 0;
        exploreTargetsAlreadyRevealed = 0;
        agentContextBuilds = 0;
        roleSelectionAttempts = 0;
        activeVillagerCountScans = 0;
        activeVillagerCountCacheHits = 0;
        roleAvailabilityCacheHits = 0;
        roleAvailabilityCacheMisses = 0;
        foodAvailabilityChecks = 0;
        woodAvailabilityChecks = 0;
        exploreAvailabilityChecks = 0;
        buildAvailabilityChecks = 0;
        constructionSiteScansAvoided = 0;
        resourceAvailabilityScansAvoided = 0;
        fogAvailabilityScansAvoided = 0;
        agentContextComponentCacheHits = 0;
        agentContextComponentCacheMisses = 0;
        agentContextFallbackLookups = 0;
        agentContextReuses = 0;
        agentContextRebuildsAfterMutation = 0;
        resourceCacheHits = 0;
        resourceCacheMisses = 0;
        resourceCandidatesFromCache = 0;
        resourceFullScansAvoided = 0;
        resourceNearestCandidatesChecked = 0;
        resourcePathChecksSkippedByCheapFilters = 0;
        homeTravelStarts = 0;
        homeArrivals = 0;
        homeMovementStalls = 0;
        homeReassignmentAttempts = 0;
        homePathCalculations = 0;
        sleepStarts = 0;
        wakeRestorations = 0;
        offNavMeshWakeDetections = 0;
        navMeshRecoverySuccesses = 0;
        navMeshRecoveryFailures = 0;
        nightAgentContextBuilds = 0;
        nightAgentContextBuildsAvoided = 0;
        homelessShelterRequests = 0;
        barnShelterTravelStarts = 0;
        barnShelterArrivals = 0;
        barnShelterLookupFailures = 0;
        barnShelterCooldowns = 0;
        barnShelterSleepStarts = 0;
        visibleHomelessNpcsDuringSleep = 0;
    }
}
