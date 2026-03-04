using System.Collections.Generic;
using IA.Pathfinding.Grid;
using UnityEngine;

namespace IA.Pathfinding.Voronoi
{
    internal class Voronoi
    {
        const int MaxIncrementalDirtyCells = 256;
        const float MaxIncrementalDirtyRatio = 0.01f;
        const float MaxIncrementalTouchedRatio = 0.25f;

        public int[] regionsByNodeIndex;
        public int[] nearestCostByNodeIndex;
        public int version;
        public int poiVersion;

        public Voronoi(int[] regionsByNodeIndex, int[] nearestCostByNodeIndex, int version, int poiVersion)
        {
            this.regionsByNodeIndex = regionsByNodeIndex;
            this.nearestCostByNodeIndex = nearestCostByNodeIndex;
            this.version = version;
            this.poiVersion = poiVersion;
        }

        internal Voronoi DeepClone()
        {
            if (regionsByNodeIndex == null || nearestCostByNodeIndex == null)
                return null;

            return new Voronoi(
                (int[])regionsByNodeIndex.Clone(),
                (int[])nearestCostByNodeIndex.Clone(),
                version,
                poiVersion);
        }

        internal static Voronoi BuildTerrainRecalculationVoronoi(int version, int snapshotPoiVersion, List<PoiSource> sources,
            bool[] walkableByNode, int[] weightByNode, List<NodeTerrainDelta> changes, Voronoi baselineVoronoi,
            int[][] neighbourIndicesByNode, PathGrid grid)
        {
            if (ShouldUseIncremental(changes, walkableByNode.Length) && baselineVoronoi != null)
            {
                Voronoi incremental = TryBuildIncrementalVoronoi(version, snapshotPoiVersion, sources,
                    walkableByNode, weightByNode, changes, baselineVoronoi, neighbourIndicesByNode, grid);

                if (incremental != null)
                    return incremental;
            }

            return BuildFullVoronoi(version, snapshotPoiVersion, sources, walkableByNode, weightByNode,
                neighbourIndicesByNode, grid);
        }

        internal static Voronoi BuildFullVoronoi(int version, int snapshotPoiVersion, List<PoiSource> sources,
            bool[] walkableByNode, int[] weightByNode, int[][] neighbourIndicesByNode, PathGrid grid)
        {
            int nodeCount = walkableByNode.Length;
            int[] regions = new int[nodeCount];
            int[] nearestCosts = new int[nodeCount];

            for (int i = 0; i < nodeCount; i++)
            {
                regions[i] = -1;
                nearestCosts[i] = int.MaxValue;
            }

            if (sources == null || sources.Count <= 0)
                return new Voronoi(regions, nearestCosts, version, snapshotPoiVersion);

            MinPriorityQueue queue = new MinPriorityQueue();
            for (int i = 0; i < sources.Count; i++)
            {
                PoiSource source = sources[i];
                if (source.nodeIndex < 0 || source.nodeIndex >= nodeCount)
                    continue;

                if (!walkableByNode[source.nodeIndex])
                    continue;

                queue.Enqueue(source.nodeIndex, source.poiId, 0);
            }

            while (queue.Count > 0)
            {
                RegionNode current = queue.Dequeue();

                if (!walkableByNode[current.nodeIndex])
                    continue;

                int bestDistance = nearestCosts[current.nodeIndex];
                int bestPoi = regions[current.nodeIndex];
                if (!IsBetterCandidate(current.distance, current.poiId, bestDistance, bestPoi))
                    continue;

                nearestCosts[current.nodeIndex] = current.distance;
                regions[current.nodeIndex] = current.poiId;

                int[] neighbours = neighbourIndicesByNode[current.nodeIndex];
                for (int i = 0; i < neighbours.Length; i++)
                {
                    int neighbour = neighbours[i];
                    if (!walkableByNode[neighbour])
                        continue;

                    int nextDistance = current.distance + GetMovementCostByIndex(current.nodeIndex, neighbour, weightByNode, grid);
                    if (!IsBetterCandidate(nextDistance, current.poiId, nearestCosts[neighbour], regions[neighbour]))
                        continue;

                    queue.Enqueue(neighbour, current.poiId, nextDistance);
                }
            }

            return new Voronoi(regions, nearestCosts, version, snapshotPoiVersion);
        }

        static bool ShouldUseIncremental(IReadOnlyList<NodeTerrainDelta> changes, int nodeCount)
        {
            if (changes == null || changes.Count == 0)
                return false;

            if (changes.Count > MaxIncrementalDirtyCells)
                return false;

            float dirtyRatio = (float)changes.Count / nodeCount;
            if (dirtyRatio > MaxIncrementalDirtyRatio)
                return false;

            for (int i = 0; i < changes.Count; i++)
            {
                NodeTerrainDelta change = changes[i];
                bool becameBlocked = change.oldWalkable && !change.newWalkable;
                bool increasedCost = change.oldWalkable && change.newWalkable && change.newWeight > change.oldWeight;
                if (becameBlocked || increasedCost)
                    return false;
            }

            return true;
        }

        static Voronoi TryBuildIncrementalVoronoi(int version, int snapshotPoiVersion, List<PoiSource> sources,
            bool[] walkableByNode, int[] weightByNode, IReadOnlyList<NodeTerrainDelta> changes,
            Voronoi baselineVoronoi, int[][] neighbourIndicesByNode, PathGrid grid)
        {
            if (baselineVoronoi.regionsByNodeIndex == null || baselineVoronoi.nearestCostByNodeIndex == null)
                return null;

            int nodeCount = walkableByNode.Length;
            if (baselineVoronoi.regionsByNodeIndex.Length != nodeCount ||
                baselineVoronoi.nearestCostByNodeIndex.Length != nodeCount)
                return null;

            int[] regions = (int[])baselineVoronoi.regionsByNodeIndex.Clone();
            int[] nearestCosts = (int[])baselineVoronoi.nearestCostByNodeIndex.Clone();

            Dictionary<int, int> poiByNodeIndex = PoiSource.BuildLookupByNodeIndex(sources);
            HashSet<int> seedNodes = new HashSet<int>();

            for (int i = 0; i < changes.Count; i++)
            {
                int changedNode = changes[i].nodeIndex;
                seedNodes.Add(changedNode);

                int[] neighbours = neighbourIndicesByNode[changedNode];
                for (int j = 0; j < neighbours.Length; j++)
                    seedNodes.Add(neighbours[j]);
            }

            MinPriorityQueue queue = new MinPriorityQueue();
            int touchedNodes = 0;
            int touchedOverflowThreshold = (int)(nodeCount * MaxIncrementalTouchedRatio);
            if (touchedOverflowThreshold < 1)
                touchedOverflowThreshold = 1;

            foreach (int seedNode in seedNodes)
            {

                if (!walkableByNode[seedNode])
                {
                    if (regions[seedNode] != -1 || nearestCosts[seedNode] != int.MaxValue)
                    {
                        regions[seedNode] = -1;
                        nearestCosts[seedNode] = int.MaxValue;
                        touchedNodes++;
                        if (touchedNodes > touchedOverflowThreshold)
                            return null;
                    }
                    continue;
                }

                int bestRegion = regions[seedNode];
                int bestCost = nearestCosts[seedNode];

                if (poiByNodeIndex.TryGetValue(seedNode, out int poiOnNode))
                {
                    if (IsBetterCandidate(0, poiOnNode, bestCost, bestRegion))
                    {
                        bestCost = 0;
                        bestRegion = poiOnNode;
                    }
                }

                int[] neighbours = neighbourIndicesByNode[seedNode];
                for (int i = 0; i < neighbours.Length; i++)
                {
                    int neighbour = neighbours[i];
                    if (!walkableByNode[neighbour])
                        continue;

                    int neighbourRegion = regions[neighbour];
                    int neighbourCost = nearestCosts[neighbour];
                    if (neighbourRegion == -1 || neighbourCost == int.MaxValue)
                        continue;

                    int candidateCost = neighbourCost + GetMovementCostByIndex(neighbour, seedNode, weightByNode, grid);
                    if (IsBetterCandidate(candidateCost, neighbourRegion, bestCost, bestRegion))
                    {
                        bestCost = candidateCost;
                        bestRegion = neighbourRegion;
                    }
                }

                if (bestRegion != -1 && IsBetterCandidate(bestCost, bestRegion, nearestCosts[seedNode], regions[seedNode]))
                {
                    nearestCosts[seedNode] = bestCost;
                    regions[seedNode] = bestRegion;
                    touchedNodes++;
                    if (touchedNodes > touchedOverflowThreshold)
                        return null;

                    queue.Enqueue(seedNode, bestRegion, bestCost);
                }

                if (poiByNodeIndex.TryGetValue(seedNode, out poiOnNode))
                    queue.Enqueue(seedNode, poiOnNode, 0);
            }

            while (queue.Count > 0)
            {
                RegionNode current = queue.Dequeue();

                if (!walkableByNode[current.nodeIndex])
                    continue;

                int currentBestCost = nearestCosts[current.nodeIndex];
                int currentBestRegion = regions[current.nodeIndex];
                if (!IsBetterCandidate(current.distance, current.poiId, currentBestCost, currentBestRegion))
                    continue;

                nearestCosts[current.nodeIndex] = current.distance;
                regions[current.nodeIndex] = current.poiId;

                touchedNodes++;
                if (touchedNodes > touchedOverflowThreshold)
                    return null;

                int[] neighbours = neighbourIndicesByNode[current.nodeIndex];
                for (int i = 0; i < neighbours.Length; i++)
                {
                    int neighbour = neighbours[i];
                    if (!walkableByNode[neighbour])
                        continue;

                    int nextDistance = current.distance + GetMovementCostByIndex(current.nodeIndex, neighbour, weightByNode, grid);
                    if (!IsBetterCandidate(nextDistance, current.poiId, nearestCosts[neighbour], regions[neighbour]))
                        continue;

                    queue.Enqueue(neighbour, current.poiId, nextDistance);
                }
            }

            return new Voronoi(regions, nearestCosts, version, snapshotPoiVersion);
        }

        static bool IsBetterCandidate(int candidateCost, int candidatePoi, int currentCost, int currentPoi)
        {
            if (candidateCost < currentCost)
                return true;

            if (candidateCost > currentCost)
                return false;

            if (currentPoi == -1)
                return true;

            return candidatePoi < currentPoi;
        }

        static int GetMovementCostByIndex(int fromNodeIndex, int toNodeIndex, int[] weightByNode, PathGrid grid)
        {
            Vector2Int fromPos = grid.GetGridPositionFromIndex(fromNodeIndex);
            Vector2Int toPos = grid.GetGridPositionFromIndex(toNodeIndex);

            int dx = fromPos.x - toPos.x;
            if (dx < 0) dx = -dx;
            int dy = fromPos.y - toPos.y;
            if (dy < 0) dy = -dy;

            int sqrDistance = dx * dx + dy * dy;
            int travelCost = sqrDistance == 2 ? 14 : 10;
            return travelCost + weightByNode[toNodeIndex];
        }
    }
}
