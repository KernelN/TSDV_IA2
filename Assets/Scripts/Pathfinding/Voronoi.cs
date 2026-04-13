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

        // -----------------------------------------------------------------------
        // BuildBowyerWatsonVoronoi
        // Builds a Voronoi diagram from a set of 2D seed positions using the
        // Bowyer-Watson algorithm (Delaunay triangulation dual) for adjacency and
        // edge lines, plus a multi-source BFS flood fill to assign each grid cell
        // to its nearest region.
        // -----------------------------------------------------------------------
        internal static BowyerWatsonResult BuildBowyerWatsonVoronoi(Vector2[] seeds, Vector2Int gridSize)
        {
            int regionCount = seeds != null ? seeds.Length : 0;
            List<(Vector2 from, Vector2 to)> edgeLines = new List<(Vector2, Vector2)>();
            int[][] adjacencyByRegion = new int[regionCount][];
            for (int i = 0; i < regionCount; i++)
                adjacencyByRegion[i] = new int[0];

            if (regionCount >= 1)
            {
                float w = gridSize.x, h = gridSize.y;

                // ── Phase 1: Create the super-triangle ──────────────────────────
                float margin = Mathf.Max(w, h) * 10f;
                Vector2 stA = new Vector2(w * 0.5f,  h + margin);
                Vector2 stB = new Vector2(-margin,   -margin);
                Vector2 stC = new Vector2(w + margin, -margin);

                List<Triangle> triangulation = new List<Triangle> { new Triangle(stA, stB, stC, -1, -2, -3) };

                // ── Phase 2: Insert each seed point one at a time ───────────────
                for (int i = 0; i < regionCount; i++)
                {
                    Vector2 p = seeds[i];

                    List<Triangle> bad = new List<Triangle>();
                    foreach (Triangle t in triangulation)
                        if (t.CircumcircleContains(p))
                            bad.Add(t);

                    List<(Vector2 a, Vector2 b, int idA, int idB)> polygon =
                        new List<(Vector2, Vector2, int, int)>();

                    foreach (Triangle t in bad)
                    {
                        for (int e = 0; e < 3; e++)
                        {
                            Vector2 ea = t.verts[e],  eb = t.verts[(e + 1) % 3];
                            int    eia = t.ids[e],    eib = t.ids[(e + 1) % 3];

                            bool shared = false;
                            foreach (Triangle other in bad)
                            {
                                if (ReferenceEquals(other, t)) continue;
                                for (int f = 0; f < 3; f++)
                                {
                                    Vector2 fa = other.verts[f], fb = other.verts[(f + 1) % 3];
                                    if (((ea - fa).sqrMagnitude < 1e-6f && (eb - fb).sqrMagnitude < 1e-6f) ||
                                        ((ea - fb).sqrMagnitude < 1e-6f && (eb - fa).sqrMagnitude < 1e-6f))
                                    { shared = true; break; }
                                }
                                if (shared) break;
                            }
                            if (!shared) polygon.Add((ea, eb, eia, eib));
                        }
                    }

                    foreach (Triangle t in bad) triangulation.Remove(t);
                    foreach ((Vector2 a, Vector2 b, int ia, int ib) in polygon)
                        triangulation.Add(new Triangle(a, b, p, ia, ib, i));
                }

                // ── Phase 3: Clean up the super-triangle ────────────────────────
                triangulation.RemoveAll(t => t.IsSuperTriangle);

                // ── Phase 4: Build the region adjacency graph ───────────────────
                HashSet<int>[] adjacencySets = new HashSet<int>[regionCount];
                for (int i = 0; i < regionCount; i++)
                    adjacencySets[i] = new HashSet<int>();

                foreach (Triangle tri in triangulation)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int idA = tri.ids[e], idB = tri.ids[(e + 1) % 3];
                        if (idA >= 0 && idB >= 0 && idA != idB)
                        {
                            adjacencySets[idA].Add(idB);
                            adjacencySets[idB].Add(idA);
                        }
                    }
                }

                for (int i = 0; i < regionCount; i++)
                {
                    adjacencyByRegion[i] = new int[adjacencySets[i].Count];
                    adjacencySets[i].CopyTo(adjacencyByRegion[i]);
                }

                // ── Phase 5: Derive Voronoi edges from the Delaunay dual ─────────
                HashSet<string> emitted = new HashSet<string>();
                for (int i = 0; i < triangulation.Count; i++)
                {
                    Triangle triA = triangulation[i];
                    for (int e = 0; e < 3; e++)
                    {
                        Vector2 ea = triA.verts[e], eb = triA.verts[(e + 1) % 3];
                        int neighborIdx = -1;

                        for (int j = 0; j < triangulation.Count; j++)
                        {
                            if (j == i) continue;
                            Triangle triB = triangulation[j];
                            bool hasA = false, hasB = false;
                            for (int v = 0; v < 3; v++)
                            {
                                if ((triB.verts[v] - ea).sqrMagnitude < 1e-6f) hasA = true;
                                if ((triB.verts[v] - eb).sqrMagnitude < 1e-6f) hasB = true;
                            }
                            if (hasA && hasB) { neighborIdx = j; break; }
                        }

                        if (neighborIdx >= 0)
                        {
                            string key = Mathf.Min(i, neighborIdx) + "," + Mathf.Max(i, neighborIdx);
                            if (!emitted.Contains(key))
                            {
                                emitted.Add(key);
                                edgeLines.Add((triA.Circumcenter(), triangulation[neighborIdx].Circumcenter()));
                            }
                        }
                        else
                        {
                            Vector2 edgeMid = (ea + eb) * 0.5f;
                            Vector2 edgeDir = (eb - ea).normalized;
                            Vector2 perp    = new Vector2(-edgeDir.y, edgeDir.x);
                            int     oppIdx  = (e + 2) % 3;
                            Vector2 inward  = (triA.verts[oppIdx] - edgeMid).normalized;
                            if (Vector2.Dot(perp, inward) > 0f) perp = -perp;

                            Vector2 cc     = triA.Circumcenter();
                            Vector2 rayEnd = cc + perp * (Mathf.Max(w, h) * 4f);
                            if (ClipSegmentToRect(ref cc, ref rayEnd, new Rect(0f, 0f, w, h)))
                                edgeLines.Add((cc, rayEnd));
                        }
                    }
                }
            }

            // ── BFS Flood Fill ──────────────────────────────────────────────────
            int[,] regionMap = new int[gridSize.x, gridSize.y];
            for (int x = 0; x < gridSize.x; x++)
                for (int y = 0; y < gridSize.y; y++)
                    regionMap[x, y] = -1;

            Queue<Vector2Int> frontier = new Queue<Vector2Int>();
            for (int i = 0; i < regionCount; i++)
            {
                int sx = Mathf.Clamp(Mathf.RoundToInt(seeds[i].x), 0, gridSize.x - 1);
                int sy = Mathf.Clamp(Mathf.RoundToInt(seeds[i].y), 0, gridSize.y - 1);
                if (regionMap[sx, sy] != -1) continue;
                regionMap[sx, sy] = i;
                frontier.Enqueue(new Vector2Int(sx, sy));
            }

            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (frontier.Count > 0)
            {
                Vector2Int cell  = frontier.Dequeue();
                int        owner = regionMap[cell.x, cell.y];
                foreach (Vector2Int d in dirs)
                {
                    Vector2Int nb = cell + d;
                    if (nb.x < 0 || nb.x >= gridSize.x || nb.y < 0 || nb.y >= gridSize.y) continue;
                    if (regionMap[nb.x, nb.y] != -1) continue;
                    regionMap[nb.x, nb.y] = owner;
                    frontier.Enqueue(nb);
                }
            }

            return new BowyerWatsonResult { regionMap = regionMap, edgeLines = edgeLines, adjacencyByRegion = adjacencyByRegion };
        }

        static bool ClipSegmentToRect(ref Vector2 p0, ref Vector2 p1, Rect rect)
        {
            float dx = p1.x - p0.x, dy = p1.y - p0.y;
            float[] pArr = { -dx,  dx, -dy,  dy };
            float[] qArr = { p0.x - rect.xMin, rect.xMax - p0.x,
                             p0.y - rect.yMin, rect.yMax - p0.y };
            float t0 = 0f, t1 = 1f;

            for (int k = 0; k < 4; k++)
            {
                if (Mathf.Abs(pArr[k]) < 1e-10f)
                {
                    if (qArr[k] < 0f) return false;
                    continue;
                }
                float t = qArr[k] / pArr[k];
                if (pArr[k] < 0f) t0 = Mathf.Max(t0, t);
                else              t1 = Mathf.Min(t1, t);
            }

            if (t0 > t1) return false;
            p1 = p0 + t1 * new Vector2(dx, dy);
            p0 = p0 + t0 * new Vector2(dx, dy);
            return true;
        }

        class Triangle
        {
            public readonly Vector2[] verts = new Vector2[3];
            public readonly int[]     ids   = new int[3];

            public Triangle(Vector2 a, Vector2 b, Vector2 c, int ia, int ib, int ic)
            {
                verts[0] = a; verts[1] = b; verts[2] = c;
                ids[0]   = ia; ids[1]  = ib; ids[2]  = ic;
            }

            public bool IsSuperTriangle => ids[0] < 0 || ids[1] < 0 || ids[2] < 0;

            public Vector2 Circumcenter()
            {
                double ax = verts[0].x, ay = verts[0].y;
                double bx = verts[1].x, by = verts[1].y;
                double cx = verts[2].x, cy = verts[2].y;

                double D = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
                if (System.Math.Abs(D) < 1e-10)
                    return new Vector2((float)((ax + bx + cx) / 3.0), (float)((ay + by + cy) / 3.0));

                double a2 = ax * ax + ay * ay;
                double b2 = bx * bx + by * by;
                double c2 = cx * cx + cy * cy;

                double ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / D;
                double uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / D;

                return new Vector2((float)ux, (float)uy);
            }

            public bool CircumcircleContains(Vector2 p)
            {
                Vector2 cc = Circumcenter();
                double r2 = (double)(verts[0].x - cc.x) * (verts[0].x - cc.x)
                          + (double)(verts[0].y - cc.y) * (verts[0].y - cc.y);
                double d2 = (double)(p.x - cc.x) * (p.x - cc.x)
                          + (double)(p.y - cc.y) * (p.y - cc.y);
                return d2 <= r2 + 1e-6;
            }

            public bool SharesEdgeWith(Triangle other)
            {
                int count = 0;
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                        if ((verts[i] - other.verts[j]).sqrMagnitude < 1e-6f) count++;
                return count == 2;
            }
        }
    }

    internal class BowyerWatsonResult
    {
        public int[,] regionMap;
        public List<(Vector2 from, Vector2 to)> edgeLines;
        public int[][] adjacencyByRegion;
    }
}
