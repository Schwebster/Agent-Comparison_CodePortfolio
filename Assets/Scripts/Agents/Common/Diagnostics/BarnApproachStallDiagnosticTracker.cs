using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public sealed class BarnApproachStallDiagnosticTracker
{
    private const float ObservationSeconds = 1.5f;
    private const float MeaningfulProgressDistance = 0.1f;
    private const int MaximumLoggedPathCorners = 6;

    private Vector3 observationPosition;
    private float observationStartedAt;
    private bool hasObservation;
    private bool stallLogged;
    private NavMeshAgent navigationAgent;

    public void Reset()
    {
        if (stallLogged)
            return;

        Clear();
    }

    public void Clear()
    {
        hasObservation = false;
        stallLogged = false;
        observationPosition = Vector3.zero;
        observationStartedAt = 0f;
    }

    public void Update(
        bool diagnosticsEnabled,
        Character character,
        CharacterIdentity identity,
        ResourceStorage barn,
        Vector3 requestedDestination,
        string purpose,
        string executorState)
    {
        if (!diagnosticsEnabled || character == null || barn == null)
        {
            Clear();
            return;
        }

        float now = Time.realtimeSinceStartup;
        Vector3 currentPosition = character.transform.position;

        if (!hasObservation)
        {
            BeginObservation(currentPosition, now);
            return;
        }

        float planarProgress = PlanarDistance(observationPosition, currentPosition);
        if (planarProgress >= MeaningfulProgressDistance)
        {
            if (stallLogged)
            {
                LogRecovery(
                    character,
                    identity,
                    barn,
                    requestedDestination,
                    purpose,
                    executorState,
                    now - observationStartedAt,
                    planarProgress);
            }

            BeginObservation(currentPosition, now);
            return;
        }

        if (stallLogged || barn.IsInInteractionRange(currentPosition))
            return;

        if (navigationAgent == null)
            navigationAgent = character.GetComponent<NavMeshAgent>();

        if (navigationAgent == null ||
            !navigationAgent.enabled ||
            !navigationAgent.isOnNavMesh ||
            navigationAgent.pathPending)
        {
            BeginObservation(currentPosition, now);
            return;
        }

        float noProgressSeconds = now - observationStartedAt;
        if (noProgressSeconds < ObservationSeconds)
            return;

        LogStall(
            character,
            identity,
            barn,
            requestedDestination,
            purpose,
            executorState,
            noProgressSeconds,
            "real-time no-progress threshold reached");
    }

    public void LogConfirmedStall(
        bool diagnosticsEnabled,
        Character character,
        CharacterIdentity identity,
        ResourceStorage barn,
        Vector3 requestedDestination,
        string purpose,
        string executorState,
        string reason)
    {
        if (!diagnosticsEnabled || stallLogged || character == null || barn == null)
            return;

        float noProgressSeconds = hasObservation
            ? Mathf.Max(0f, Time.realtimeSinceStartup - observationStartedAt)
            : 0f;

        LogStall(
            character,
            identity,
            barn,
            requestedDestination,
            purpose,
            executorState,
            noProgressSeconds,
            reason);
    }

    private void BeginObservation(Vector3 position, float now)
    {
        observationPosition = position;
        observationStartedAt = now;
        hasObservation = true;
        stallLogged = false;
    }

    private void LogStall(
        Character character,
        CharacterIdentity identity,
        ResourceStorage barn,
        Vector3 requestedDestination,
        string purpose,
        string executorState,
        float noProgressSeconds,
        string reason)
    {
        stallLogged = true;
        Debug.LogWarning(BuildStallMessage(
            character,
            identity,
            barn,
            requestedDestination,
            purpose,
            executorState,
            noProgressSeconds,
            reason));
    }

    private static string BuildStallMessage(
        Character character,
        CharacterIdentity identity,
        ResourceStorage barn,
        Vector3 requestedDestination,
        string purpose,
        string executorState,
        float noProgressSeconds,
        string reason)
    {
        Transform characterTransform = character.transform;
        NavMeshAgent agent = character.GetComponent<NavMeshAgent>();
        Vector3 currentPosition = characterTransform.position;
        Vector3 depositWorldPosition = barn.GetDepositPosition();
        Vector3 depositLocalPosition = barn.transform.InverseTransformPoint(depositWorldPosition);
        bool agentEnabled = agent != null && agent.enabled;
        bool isOnNavMesh = agentEnabled && agent.isOnNavMesh;
        bool canReadPath = isOnNavMesh;
        Vector3[] pathCorners = canReadPath && agent.hasPath
            ? agent.path.corners
            : System.Array.Empty<Vector3>();
        Vector3 nextPathPoint = FindNextPathPoint(currentPosition, requestedDestination, pathCorners);

        NavMeshHit navMeshRaycastHit = default;
        NavMeshHit closestEdgeHit = default;
        bool navMeshRaycastAvailable = isOnNavMesh;
        bool navMeshRaycastBlocked = navMeshRaycastAvailable &&
            NavMesh.Raycast(currentPosition, nextPathPoint, out navMeshRaycastHit, agent.areaMask);
        bool closestEdgeFound = isOnNavMesh &&
            NavMesh.FindClosestEdge(currentPosition, out closestEdgeHit, agent.areaMask);

        Collider physicsHitCollider = null;
        float physicsHitDistance = 0f;
        Vector3 physicsHitPoint = Vector3.zero;
        TryFindBlockingCollider(
            characterTransform,
            agent,
            currentPosition,
            nextPathPoint,
            out physicsHitCollider,
            out physicsHitDistance,
            out physicsHitPoint);

        bool physicsHitBarn = physicsHitCollider != null &&
            physicsHitCollider.transform.IsChildOf(barn.transform);
        string characterName = identity != null ? identity.FullName : character.gameObject.name;
        int characterId = identity != null ? identity.CharacterId : character.gameObject.GetInstanceID();
        WorldTimeManager time = Object.FindFirstObjectByType<WorldTimeManager>();

        StringBuilder message = new StringBuilder(2048);
        message.Append("BarnApproachStallDiagnostic | Event=Stall")
            .Append(" | NPC=").Append(characterName)
            .Append(" | CharacterId=").Append(characterId)
            .Append(" | Purpose=").Append(purpose)
            .Append(" | ExecutorState=").Append(executorState)
            .Append(" | Frame=").Append(Time.frameCount)
            .Append(" | WorldDay=").Append(time != null ? time.CurrentDay : -1)
            .Append(" | WorldHour=").Append(time != null ? time.CurrentHour.ToString("0.###") : "Unavailable")
            .Append(" | NoProgressRealSeconds=").Append(noProgressSeconds.ToString("0.###"))
            .Append(" | ObservationThresholdRealSeconds=").Append(ObservationSeconds.ToString("0.###"))
            .Append(" | RequiredProgressDistance=").Append(MeaningfulProgressDistance.ToString("0.###"))
            .Append(" | Position=").Append(currentPosition.ToString("F3"))
            .Append(" | Barn=").Append(barn.gameObject.name)
            .Append(" | BarnTransformPosition=").Append(barn.transform.position.ToString("F3"))
            .Append(" | DepositPointLocal=").Append(depositLocalPosition.ToString("F3"))
            .Append(" | DepositPointWorld=").Append(depositWorldPosition.ToString("F3"))
            .Append(" | RequestedDestination=").Append(requestedDestination.ToString("F3"))
            .Append(" | DestinationPlanarDistance=").Append(PlanarDistance(currentPosition, requestedDestination).ToString("0.###"))
            .Append(" | InsideInteractionRange=").Append(barn.IsInInteractionRange(currentPosition))
            .Append(" | InteractionRadius=").Append(barn.InteractionRadius.ToString("0.###"))
            .Append(" | AgentExists=").Append(agent != null)
            .Append(" | AgentEnabled=").Append(agentEnabled)
            .Append(" | IsOnNavMesh=").Append(isOnNavMesh);

        if (agent != null)
        {
            message.Append(" | IsStopped=").Append(agent.isStopped)
                .Append(" | PathPending=").Append(canReadPath && agent.pathPending)
                .Append(" | HasPath=").Append(canReadPath && agent.hasPath)
                .Append(" | PathStatus=").Append(canReadPath ? agent.pathStatus.ToString() : "Unavailable")
                .Append(" | RemainingDistance=").Append(canReadPath ? agent.remainingDistance.ToString("0.###") : "Unavailable")
                .Append(" | Velocity=").Append(agent.velocity.magnitude.ToString("0.###"))
                .Append(" | DesiredVelocity=").Append(agent.desiredVelocity.magnitude.ToString("0.###"))
                .Append(" | NextPosition=").Append(agent.nextPosition.ToString("F3"))
                .Append(" | SteeringTarget=").Append(canReadPath ? agent.steeringTarget.ToString("F3") : "Unavailable")
                .Append(" | AgentRadius=").Append(agent.radius.ToString("0.###"))
                .Append(" | AgentHeight=").Append(agent.height.ToString("0.###"))
                .Append(" | StoppingDistance=").Append(agent.stoppingDistance.ToString("0.###"))
                .Append(" | AvoidancePriority=").Append(agent.avoidancePriority)
                .Append(" | ObstacleAvoidanceType=").Append(agent.obstacleAvoidanceType);
        }

        message.Append(" | PathCorners=").Append(FormatPathCorners(pathCorners))
            .Append(" | NextPathPoint=").Append(nextPathPoint.ToString("F3"))
            .Append(" | NextPathPointPlanarDistance=").Append(PlanarDistance(currentPosition, nextPathPoint).ToString("0.###"))
            .Append(" | NavMeshRaycastAvailable=").Append(navMeshRaycastAvailable)
            .Append(" | NavMeshRaycastBlocked=").Append(navMeshRaycastBlocked)
            .Append(" | NavMeshRaycastHit=").Append(navMeshRaycastBlocked ? navMeshRaycastHit.position.ToString("F3") : "None")
            .Append(" | ClosestNavMeshEdgeFound=").Append(closestEdgeFound)
            .Append(" | ClosestNavMeshEdge=").Append(closestEdgeFound ? closestEdgeHit.position.ToString("F3") : "Unavailable")
            .Append(" | ClosestNavMeshEdgeDistance=").Append(closestEdgeFound ? Vector3.Distance(currentPosition, closestEdgeHit.position).ToString("0.###") : "Unavailable")
            .Append(" | PhysicsCastHit=").Append(physicsHitCollider != null)
            .Append(" | PhysicsHitObject=").Append(physicsHitCollider != null ? physicsHitCollider.gameObject.name : "None")
            .Append(" | PhysicsHitCollider=").Append(physicsHitCollider != null ? physicsHitCollider.GetType().Name : "None")
            .Append(" | PhysicsHitDistance=").Append(physicsHitCollider != null ? physicsHitDistance.ToString("0.###") : "Unavailable")
            .Append(" | PhysicsHitPoint=").Append(physicsHitCollider != null ? physicsHitPoint.ToString("F3") : "Unavailable")
            .Append(" | PhysicsHitBelongsToBarn=").Append(physicsHitBarn)
            .Append(" | BarnColliderSummary=").Append(BuildBarnColliderSummary(
                barn,
                currentPosition,
                depositWorldPosition,
                requestedDestination))
            .Append(" | BarnObstacleSummary=").Append(BuildBarnObstacleSummary(barn))
            .Append(" | BarnModifierSummary=").Append(BuildBarnModifierSummary(barn))
            .Append(" | ControlMode=").Append(CharacterControlRegistry.GetControlMode(character))
            .Append(" | Reason=").Append(reason);

        return message.ToString();
    }

    private static void LogRecovery(
        Character character,
        CharacterIdentity identity,
        ResourceStorage barn,
        Vector3 requestedDestination,
        string purpose,
        string executorState,
        float episodeSeconds,
        float progressDistance)
    {
        string characterName = identity != null ? identity.FullName : character.gameObject.name;
        int characterId = identity != null ? identity.CharacterId : character.gameObject.GetInstanceID();
        Debug.Log(
            $"BarnApproachStallDiagnostic | Event=Recovered | NPC={characterName} | " +
            $"CharacterId={characterId} | Purpose={purpose} | ExecutorState={executorState} | " +
            $"Frame={Time.frameCount} | Position={character.transform.position.ToString("F3")} | " +
            $"Barn={barn.gameObject.name} | RequestedDestination={requestedDestination.ToString("F3")} | " +
            $"EpisodeRealSeconds={episodeSeconds:0.###} | PlanarProgressDistance={progressDistance:0.###} | " +
            $"ControlMode={CharacterControlRegistry.GetControlMode(character)}");
    }

    private static Vector3 FindNextPathPoint(
        Vector3 currentPosition,
        Vector3 fallback,
        Vector3[] corners)
    {
        for (int i = 0; i < corners.Length; i++)
        {
            if (PlanarDistance(currentPosition, corners[i]) > 0.05f)
                return corners[i];
        }

        return fallback;
    }

    private static string FormatPathCorners(Vector3[] corners)
    {
        if (corners == null || corners.Length == 0)
            return "None";

        StringBuilder result = new StringBuilder();
        int count = Mathf.Min(corners.Length, MaximumLoggedPathCorners);
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                result.Append(" -> ");

            result.Append(corners[i].ToString("F3"));
        }

        if (corners.Length > count)
            result.Append(" -> ...(").Append(corners.Length).Append(" total)");

        return result.ToString();
    }

    private static bool TryFindBlockingCollider(
        Transform characterTransform,
        NavMeshAgent agent,
        Vector3 origin,
        Vector3 target,
        out Collider blockingCollider,
        out float hitDistance,
        out Vector3 hitPoint)
    {
        blockingCollider = null;
        hitDistance = 0f;
        hitPoint = Vector3.zero;

        Vector3 displacement = target - origin;
        float distance = displacement.magnitude;
        if (distance <= 0.001f)
            return false;

        float radius = agent != null ? Mathf.Max(0.05f, agent.radius) : 0.5f;
        float height = agent != null ? Mathf.Max(radius * 2f, agent.height) : 2f;
        float baseOffset = agent != null ? agent.baseOffset : 0f;
        Vector3 baseCenter = origin + Vector3.up * baseOffset;
        Vector3 bottom = baseCenter + Vector3.up * radius;
        Vector3 top = baseCenter + Vector3.up * Mathf.Max(radius, height - radius);
        RaycastHit[] hits = Physics.CapsuleCastAll(
            bottom,
            top,
            radius,
            displacement / distance,
            distance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (candidate == null || candidate.transform.IsChildOf(characterTransform))
                continue;

            if (hits[i].distance >= closestDistance)
                continue;

            closestDistance = hits[i].distance;
            blockingCollider = candidate;
            hitDistance = hits[i].distance;
            hitPoint = hits[i].point;
        }

        return blockingCollider != null;
    }

    private static string BuildBarnColliderSummary(
        ResourceStorage barn,
        Vector3 npcPosition,
        Vector3 depositPosition,
        Vector3 requestedDestination)
    {
        Collider[] colliders = barn.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0)
            return "None";

        StringBuilder result = new StringBuilder();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (i > 0)
                result.Append(';');

            result.Append(collider.gameObject.name)
                .Append('(').Append(collider.GetType().Name)
                .Append(",enabled=").Append(collider.enabled)
                .Append(",trigger=").Append(collider.isTrigger);

            if (collider.enabled)
            {
                result.Append(",npcSurfaceDistance=")
                    .Append(Vector3.Distance(npcPosition, collider.ClosestPoint(npcPosition)).ToString("0.###"))
                    .Append(",depositSurfaceDistance=")
                    .Append(Vector3.Distance(depositPosition, collider.ClosestPoint(depositPosition)).ToString("0.###"))
                    .Append(",destinationSurfaceDistance=")
                    .Append(Vector3.Distance(requestedDestination, collider.ClosestPoint(requestedDestination)).ToString("0.###"));
            }

            result.Append(')');
        }

        return result.ToString();
    }

    private static string BuildBarnObstacleSummary(ResourceStorage barn)
    {
        NavMeshObstacle[] obstacles = barn.GetComponentsInChildren<NavMeshObstacle>(true);
        if (obstacles.Length == 0)
            return "None";

        StringBuilder result = new StringBuilder();
        for (int i = 0; i < obstacles.Length; i++)
        {
            if (i > 0)
                result.Append(';');

            NavMeshObstacle obstacle = obstacles[i];
            result.Append(obstacle.gameObject.name)
                .Append("(enabled=").Append(obstacle.enabled)
                .Append(",carving=").Append(obstacle.carving)
                .Append(",shape=").Append(obstacle.shape)
                .Append(')');
        }

        return result.ToString();
    }

    private static string BuildBarnModifierSummary(ResourceStorage barn)
    {
        NavMeshModifier[] modifiers = barn.GetComponentsInChildren<NavMeshModifier>(true);
        if (modifiers.Length == 0)
            return "None";

        StringBuilder result = new StringBuilder();
        for (int i = 0; i < modifiers.Length; i++)
        {
            if (i > 0)
                result.Append(';');

            NavMeshModifier modifier = modifiers[i];
            result.Append(modifier.gameObject.name)
                .Append("(enabled=").Append(modifier.enabled)
                .Append(",overrideArea=").Append(modifier.overrideArea)
                .Append(",area=").Append(modifier.area)
                .Append(')');
        }

        return result.ToString();
    }

    private static float PlanarDistance(Vector3 first, Vector3 second)
    {
        Vector3 offset = second - first;
        offset.y = 0f;
        return offset.magnitude;
    }
}
