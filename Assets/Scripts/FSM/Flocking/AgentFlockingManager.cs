using System;
using System.Collections.Generic;
using UnityEngine;

namespace IA.FSM.Flocking
{
    [Serializable]
    public struct AgentFlockingSettings
    {
        [Header("Distances")]
        [Min(0f)] public float alignmentDist;
        [Min(0f)] public float cohesionDist;
        [Min(0f)] public float separationDist;
        [Min(0f)] public float obstacleDist;

        [Header("Weights")]
        public float alignmentMod;
        public float cohesionMod;
        public float separationMod;
        public float obstacleMod;

        [Header("Safety / Performance")]
        [Min(0.01f)] public float minSpacing;
        [Min(0.0001f)] public float maxSteer;
        [Min(0.1f)] public float spatialHashCellSize;

        public AgentFlockingSettings ClampValues()
        {
            AgentFlockingSettings clamped = this;

            if (clamped.minSpacing <= 0f)
                clamped.minSpacing = 0.01f;
            if (clamped.spatialHashCellSize <= 0f)
            {
                float maxDist = Mathf.Max(clamped.alignmentDist, clamped.cohesionDist, clamped.separationDist);
                clamped.spatialHashCellSize = maxDist > 0f ? maxDist : 1f;
            }

            clamped.maxSteer = Mathf.Max(0f, clamped.maxSteer);
            clamped.alignmentDist = Mathf.Max(0f, clamped.alignmentDist);
            clamped.cohesionDist = Mathf.Max(0f, clamped.cohesionDist);
            clamped.separationDist = Mathf.Max(0f, clamped.separationDist);
            clamped.obstacleDist = Mathf.Max(0f, clamped.obstacleDist);
            return clamped;
        }
    }

    public struct AgentFlockingSnapshot
    {
        public Vector3 currentPos;
        public Vector3 plannedPos;
        public bool hasMovementIntent;

        public AgentFlockingSnapshot(Vector3 currentPos, Vector3 plannedPos, bool hasMovementIntent)
        {
            this.currentPos = currentPos;
            this.plannedPos = plannedPos;
            this.hasMovementIntent = hasMovementIntent;
        }
    }

    public struct NearbyAgentData
    {
        public int index;
        public Vector2 offset;
        public float sqrDistance;

        public NearbyAgentData(int index, Vector2 offset, float sqrDistance)
        {
            this.index = index;
            this.offset = offset;
            this.sqrDistance = sqrDistance;
        }
    }

    public static class AgentFlockingManager
    {
        const float SqrEpsilon = 0.000001f;

        static readonly Vector2Int[] NeighbourCellOffsets =
        {
            new Vector2Int(-1, -1), new Vector2Int(-1, 0), new Vector2Int(-1, 1),
            new Vector2Int(0, -1), new Vector2Int(0, 0), new Vector2Int(0, 1),
            new Vector2Int(1, -1), new Vector2Int(1, 0), new Vector2Int(1, 1)
        };

        public static void Apply(
            IReadOnlyList<AgentFlockingSnapshot> agents,
            AgentFlockingSettings settings,
            LayerMask obstacleMask,
            Collider[] obstacleBuffer,
            List<Vector3> output)
        {
            output.Clear();
            if (agents == null || agents.Count <= 0)
                return;

            settings = settings.ClampValues();

            for (int i = 0; i < agents.Count; i++)
                output.Add(agents[i].plannedPos);

            Dictionary<Vector2Int, List<int>> cells = BuildSpatialHash(agents, settings.spatialHashCellSize);
            float alignmentDistSqr = settings.alignmentDist * settings.alignmentDist;
            float cohesionDistSqr = settings.cohesionDist * settings.cohesionDist;
            float separationDistSqr = settings.separationDist * settings.separationDist;
            float maxNeighbourDistSqr = Mathf.Max(alignmentDistSqr, cohesionDistSqr, separationDistSqr);

            for (int i = 0; i < agents.Count; i++)
            {
                AgentFlockingSnapshot agent = agents[i];
                Vector2 baseStep = ToXZ(agent.plannedPos - agent.currentPos);
                if (!agent.hasMovementIntent || baseStep.sqrMagnitude <= SqrEpsilon)
                    continue;

                float baseSpeed = baseStep.magnitude;
                Vector2 direction = Direction(agent);
                if (direction.sqrMagnitude <= SqrEpsilon)
                    continue;

                List<int> candidates = GetNeighbourCandidatesFromSpatialHash(agent.currentPos, cells, settings.spatialHashCellSize);
                List<NearbyAgentData> nearbyAgents = GetInsideRadiusAgents(i, agents, candidates, maxNeighbourDistSqr);

                Vector2 alignment = Alignment(direction, agents, nearbyAgents, alignmentDistSqr);
                Vector2 cohesion = Cohesion(agent, agents, nearbyAgents, cohesionDistSqr);
                Vector2 separation = Separation(nearbyAgents, settings, separationDistSqr);
                Vector2 obstacle = Obstacle(agent.currentPos, settings, obstacleMask, obstacleBuffer);

                Vector2 ACS =
                    direction + //Direction to target, then modify direction towards...
                    alignment * settings.alignmentMod + //...avg speed of all neighbors
                    cohesion * settings.cohesionMod + //...avg center of all neighbors
                    separation * settings.separationMod + //...the sum of the disp away from each neighbor
                    obstacle * settings.obstacleMod; //...the sum of the disp away from each obstacle (if it's already touching the obst, it gets the obst center instead of closest point)

                Vector2 finalDirection = ApplySteering(direction, ACS, settings.maxSteer);

                Vector2 adjustedStep = finalDirection * baseSpeed;
                Vector3 adjustedPos = agent.currentPos;
                adjustedPos.x += adjustedStep.x;
                adjustedPos.z += adjustedStep.y;
                adjustedPos.y = agent.plannedPos.y;
                output[i] = adjustedPos;
            }
        }

        /// <summary>
        /// Takes next position in path as target and returns dir
        /// </summary>
        public static Vector2 Direction(AgentFlockingSnapshot agent)
        {
            Vector2 movement = ToXZ(agent.plannedPos - agent.currentPos);
            if (movement.sqrMagnitude <= SqrEpsilon)
                return Vector2.zero;

            return movement.normalized;
        }

        /// <summary>
        /// Average heading of nearby agents.
        /// Skips agents moving towards very different directions.
        /// </summary>
        public static Vector2 Alignment(Vector3 agentDir,
            IReadOnlyList<AgentFlockingSnapshot> agents,
            IReadOnlyList<NearbyAgentData> nearbyAgents,
            float alignmentDistSqr)
        {
            Vector2 avg = Vector2.zero;
            int count = 0;

            for (int i = 0; i < nearbyAgents.Count; i++)
            {
                NearbyAgentData nearby = nearbyAgents[i];
                if (nearby.sqrDistance > alignmentDistSqr)
                    continue;

                AgentFlockingSnapshot neighbour = agents[nearby.index];
                if (!neighbour.hasMovementIntent)
                    continue;

                Vector2 neighbourDirection = Direction(neighbour);
                if (neighbourDirection.sqrMagnitude <= SqrEpsilon)
                    continue;
                
                //If direction leans towards opposite, skip
                if(Vector2.Dot( neighbourDirection, agentDir ) < 0)
                    continue;

                avg += neighbourDirection;
                count++;
            }

            if (count <= 0)
                return Vector2.zero;

            return (avg / count).normalized;
        }

        /// <summary>
        /// Attraction towards the center of nearby agents.
        /// </summary>
        public static Vector2 Cohesion(
            AgentFlockingSnapshot sourceAgent,
            IReadOnlyList<AgentFlockingSnapshot> agents,
            IReadOnlyList<NearbyAgentData> nearbyAgents,
            float cohesionDistSqr)
        {
            Vector2 center = Vector2.zero;
            int count = 0;

            for (int i = 0; i < nearbyAgents.Count; i++)
            {
                NearbyAgentData nearby = nearbyAgents[i];
                if (nearby.sqrDistance > cohesionDistSqr)
                    continue;

                center += ToXZ(agents[nearby.index].currentPos);
                count++;
            }

            if (count <= 0)
                return Vector2.zero;

            center /= count;
            Vector2 cohesion = center - ToXZ(sourceAgent.currentPos);
            if (cohesion.sqrMagnitude <= SqrEpsilon)
                return Vector2.zero;

            return cohesion.normalized;
        }

        /// <summary>
        /// Repulsion away from nearby agents to avoid overlap.
        /// </summary>
        public static Vector2 Separation(
            IReadOnlyList<NearbyAgentData> nearbyAgents,
            AgentFlockingSettings settings,
            float separationDistSqr)
        {
            Vector2 totalRepulsion = Vector2.zero;

            for (int i = 0; i < nearbyAgents.Count; i++)
            {
                NearbyAgentData nearby = nearbyAgents[i];
                if (nearby.sqrDistance > separationDistSqr)
                    continue;

                //the further away the other agent is,
                //the less influence the offset will have
                float distance = Mathf.Sqrt(Mathf.Max(nearby.sqrDistance, SqrEpsilon));
                Vector2 away;
                away = -nearby.offset / distance; 

                //clamp to minimum safe distance
                float safeDistance = Mathf.Max(distance, settings.minSpacing); 
                //gives more weight the closer it is (caps at min safe space)
                float weight = 1f / safeDistance;
                
                // If it is closer than safe dist, add even more weight
                if (distance < settings.minSpacing)
                    weight += (settings.minSpacing - distance) / settings.minSpacing;

                totalRepulsion += away * weight;
            }

            if (totalRepulsion.sqrMagnitude <= SqrEpsilon)
                return Vector2.zero;

            ////Makes sure that it is VERY close to other boid,
            ////it will have a strong separation from it
            //return totalRepulsion;
            
            ////Binds separation strictly to inspector value
            //return Vector2.ClampMagnitude(totalRepulsion, 1);
            
            //Binds separation strictly to inspector value (but allows less value)
            return Vector2.ClampMagnitude(totalRepulsion, 1);
        }

        /// <summary>
        /// Repulsion away from nearby obstacle colliders.
        /// </summary>
        public static Vector2 Obstacle(
            Vector3 currentPos,
            AgentFlockingSettings settings,
            LayerMask obstacleMask,
            Collider[] obstacleBuffer)
        {
            List<Collider> nearbyObstacles = GetInsideRadiusObstacles(currentPos, settings.obstacleDist, obstacleMask, obstacleBuffer);
            if (nearbyObstacles.Count <= 0)
                return Vector2.zero;

            Vector2 repulsion = Vector2.zero;
            for (int i = 0; i < nearbyObstacles.Count; i++)
            {
                Collider obstacle = nearbyObstacles[i];
                if (!obstacle)
                    continue;

                Vector3 closestPoint = obstacle.ClosestPoint(currentPos);
                
                Vector2 toObstacle = FlattenVector(closestPoint - currentPos);
                
                float distance = toObstacle.magnitude;
                Vector2 awayDirection;

                //If boid is ON closest obstacle point,
                //make calculation with the obstacle center
                if (distance <= Mathf.Epsilon)
                {
                    Vector2 centerOffset = FlattenVector(obstacle.bounds.center - currentPos);
                    
                    if (centerOffset.sqrMagnitude <= SqrEpsilon)
                        continue;

                    awayDirection = -centerOffset.normalized;
                    distance = 0f;
                }
                else
                {
                    awayDirection = -toObstacle / distance;
                }

                //The closer it is, the more weight it will have
                float weight = 1f - Mathf.Clamp01(distance / settings.obstacleDist);
                repulsion += awayDirection * weight;
            }

            if (repulsion.sqrMagnitude <= SqrEpsilon)
                return Vector2.zero;

            //Clamp to normalized magnitude or less
            return Vector2.ClampMagnitude(repulsion, 1);
        }

        static Vector2 FlattenVector(Vector3 vector)
        {
            vector.y = 0f;
            return new Vector2(vector.x, vector.z);
        }

        /// <summary>
        /// Get neighbour candidates from the current cell and its immediate surrounding cells.
        /// </summary>
        public static List<int> GetNeighbourCandidatesFromSpatialHash(
            Vector3 currentPos,
            Dictionary<Vector2Int, List<int>> cells,
            float cellSize)
        {
            List<int> candidates = new List<int>();
            Vector2Int cell = GetCell(currentPos, cellSize);

            for (int i = 0; i < NeighbourCellOffsets.Length; i++)
            {
                Vector2Int neighbourCell = cell + NeighbourCellOffsets[i];
                if (!cells.TryGetValue(neighbourCell, out List<int> neighbourIndices))
                    continue;

                for (int n = 0; n < neighbourIndices.Count; n++)
                    candidates.Add(neighbourIndices[n]);
            }

            return candidates;
        }

        /// <summary>
        /// Filter spatial-hash candidates into in-radius neighbours with cached offset/distance data.
        /// </summary>
        public static List<NearbyAgentData> GetInsideRadiusAgents(
            int sourceIndex,
            IReadOnlyList<AgentFlockingSnapshot> agents,
            IReadOnlyList<int> candidateIndices,
            float maxDistanceSqr)
        {
            List<NearbyAgentData> nearbyAgents = new List<NearbyAgentData>();
            Vector2 sourcePos2D = ToXZ(agents[sourceIndex].currentPos);

            for (int i = 0; i < candidateIndices.Count; i++)
            {
                int candidateIndex = candidateIndices[i];
                if (candidateIndex == sourceIndex)
                    continue;

                Vector2 offset = ToXZ(agents[candidateIndex].currentPos) - sourcePos2D;
                float sqrDistance = offset.sqrMagnitude;
                if (sqrDistance > maxDistanceSqr)
                    continue;

                nearbyAgents.Add(new NearbyAgentData(candidateIndex, offset, sqrDistance));
            }

            return nearbyAgents;
        }

        /// <summary>
        /// Query obstacles within radius using the configured obstacle mask.
        /// </summary>
        public static List<Collider> GetInsideRadiusObstacles(
            Vector3 currentPos,
            float obstacleDistance,
            LayerMask obstacleMask,
            Collider[] obstacleBuffer)
        {
            List<Collider> obstacles = new List<Collider>();
            if (obstacleDistance <= 0f || obstacleMask.value == 0)
                return obstacles;

            if (obstacleBuffer == null || obstacleBuffer.Length <= 0)
                return obstacles;

            int hitCount = Physics.OverlapSphereNonAlloc(
                currentPos,
                obstacleDistance,
                obstacleBuffer,
                obstacleMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
                if (obstacleBuffer[i] != null)
                    obstacles.Add(obstacleBuffer[i]);

            return obstacles;
        }

        static Dictionary<Vector2Int, List<int>> BuildSpatialHash(IReadOnlyList<AgentFlockingSnapshot> agents, float cellSize)
        {
            Dictionary<Vector2Int, List<int>> cells = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < agents.Count; i++)
            {
                Vector2Int cell = GetCell(agents[i].currentPos, cellSize);
                if (!cells.TryGetValue(cell, out List<int> indices))
                {
                    indices = new List<int>();
                    cells.Add(cell, indices);
                }

                indices.Add(i);
            }

            return cells;
        }

        static Vector2 ApplySteering(Vector2 direction, Vector2 ACS, float maxSteer)
        {
            if (ACS.sqrMagnitude <= SqrEpsilon)
                ACS = direction;

            Vector2 desiredDirection = ACS.normalized;
            Vector2 steer = desiredDirection - direction;
            
            //If steering more than max, clamp
            if (steer.sqrMagnitude > maxSteer*maxSteer)
                steer = steer.normalized * maxSteer;

            Vector2 finalDirection = direction + steer;
            if (finalDirection.sqrMagnitude <= SqrEpsilon)
                return direction;

            return finalDirection.normalized;
        }

        static Vector2Int GetCell(Vector3 pos, float cellSize)
        {
            return new Vector2Int(
                Mathf.FloorToInt(pos.x / cellSize),
                Mathf.FloorToInt(pos.z / cellSize));
        }

        static Vector2 ToXZ(Vector3 v)
        {
            return new Vector2(v.x, v.z);
        }
    }
}

