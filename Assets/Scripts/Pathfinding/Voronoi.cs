using System.Collections.Generic;
using IA.Pathfinding.Grid;
using UnityEngine;

namespace IA.Pathfinding.Voronoi
{
    public class Voronoi
    {
        const float PolygonEpsilon = 0.0001f;

        public class VoronoiMidpoint
        {
            public Vector2Int gridPos;
            public Vector2 pos;
            public float sqrRadius;
            public VoronoiSite siteA;
            public VoronoiSite siteB;

            public VoronoiMidpoint(Vector2Int gridPos, Vector2 pos, float sqrRadius,
                VoronoiSite siteA, VoronoiSite siteB)
            {
                this.gridPos = gridPos;
                this.pos = pos;
                this.sqrRadius = sqrRadius;
                this.siteA = siteA;
                this.siteB = siteB;
            }
        }

        public class VoronoiSite
        {
            public PointOfInterest site;
            public Vector2Int gridPos => site.gridPos;
            public List<Vector2> polygonVertices;
            public Dictionary<VoronoiSite, VoronoiMidpoint> midpointsByOtherSite;

            public VoronoiSite(PointOfInterest site)
            {
                this.site = site;
                polygonVertices = new List<Vector2>();
                midpointsByOtherSite = new Dictionary<VoronoiSite, VoronoiMidpoint>();
            }

            public bool HasMidpoint(VoronoiSite otherSite)
                => otherSite != null && midpointsByOtherSite.ContainsKey(otherSite);

            public bool TryGetMidpoint(VoronoiSite otherSite, out VoronoiMidpoint midpoint)
            {
                midpoint = null;
                return otherSite != null && midpointsByOtherSite.TryGetValue(otherSite, out midpoint);
            }

            public void AddMidpoint(VoronoiSite otherSite, VoronoiMidpoint midpoint)
            {
                if (otherSite == null || midpoint == null || otherSite == this)
                    return;

                //Add the midpoint to this site and the other site.
                if(midpointsByOtherSite.TryAdd(otherSite, midpoint))
                    otherSite.midpointsByOtherSite.TryAdd(this, midpoint);
            }

            public bool RemoveMidpoint(VoronoiSite otherSite)
            {
                if (otherSite == null || !midpointsByOtherSite.Remove(otherSite))
                    return false;

                otherSite.midpointsByOtherSite.Remove(this);

                return true;
            }
        }

        PointOfInterest[] sites;
        Vector2Int mapSize;
        PathNode[,] nodeGrid;

        List<VoronoiSite> validSites = new List<VoronoiSite>();
        Dictionary<PointOfInterest, VoronoiSite> sitesByPoi = new Dictionary<PointOfInterest, VoronoiSite>();

        int voronoiVersion;

        public int VoronoiVersion => voronoiVersion;
        public IReadOnlyDictionary<PointOfInterest, VoronoiSite> SitesByPoi => sitesByPoi;

        public Voronoi(PointOfInterest[] sites, Vector2Int mapSize, PathNode[,] nodeGrid)
        {
            this.sites = sites ?? new PointOfInterest[0];
            this.mapSize = new Vector2Int(Mathf.Max(1, mapSize.x), Mathf.Max(1, mapSize.y));
            this.nodeGrid = nodeGrid;

            voronoiVersion = 0;
            
            CalculateFullVoronoi();
        }

        public void CalculateFullVoronoi()
        {
            // Every full rebuild creates a new version. Nodes keep the version number of
            // their cached closest site, so old answers become stale automatically.
            voronoiVersion++;
            RebuildAllVoronoiData();
        }

        public Vector2Int GetClosestSite(Vector2Int nodeGridPosition)
        {
            // If the requested node is outside the known grid, there is no safe site to return.
            if (!IsGridPositionValid(nodeGridPosition) || validSites.Count == 0)
                return new Vector2Int(-1, -1);

            PathNode node = nodeGrid[nodeGridPosition.x, nodeGridPosition.y];
            if (node == null)
                return new Vector2Int(-1, -1);

            // A node can reuse its previous answer as long as the Voronoi version still matches.
            if (node.hasClosestVoronoiSite && node.closestVoronoiVersion == voronoiVersion)
                return node.closestVoronoiSiteGridPos;

            List<VoronoiSite> sortedSites = new List<VoronoiSite>(validSites);
            sortedSites.Sort((firstSite, secondSite) =>
            {
                float firstDistance = GetSquaredEuclideanDistance(nodeGridPosition, firstSite.gridPos);
                float secondDistance = GetSquaredEuclideanDistance(nodeGridPosition, secondSite.gridPos);
                if (!Mathf.Approximately(firstDistance, secondDistance))
                    return firstDistance.CompareTo(secondDistance);

                int xComparison = firstSite.gridPos.x.CompareTo(secondSite.gridPos.x);
                if (xComparison != 0)
                    return xComparison;

                int yComparison = firstSite.gridPos.y.CompareTo(secondSite.gridPos.y);
                if (yComparison != 0)
                    return yComparison;

                return firstSite.site.id.CompareTo(secondSite.site.id);
            });

            Vector2 pointInGridSpace = new Vector2(nodeGridPosition.x, nodeGridPosition.y);
            for (int i = 0; i < sortedSites.Count; i++)
            {
                VoronoiSite candidateSite = sortedSites[i];

                // The polygon check is a 2D ray-crossing test in grid space.
                // It is called a raycast here because a horizontal ray is counted against polygon edges.
                if (!IsPointInsidePolygon(pointInGridSpace, candidateSite.polygonVertices))
                    continue;

                CacheClosestSiteOnNode(node, candidateSite.gridPos);
                return candidateSite.gridPos;
            }

            // Temporary midpoint polygons can overlap or leave gaps while the weighted vertex stage is still pending.
            // If no polygon catches the node, the nearest sorted site keeps the query useful and deterministic.
            VoronoiSite fallbackSite = sortedSites[0];
            CacheClosestSiteOnNode(node, fallbackSite.gridPos);
            return fallbackSite.gridPos;
        }

        public void CalculatePartialVoronoi(PointOfInterest removedSite)
        {
            // A partial rebuild also creates a new version so cached node answers expire.
            voronoiVersion++;

            if (removedSite == null || !sitesByPoi.ContainsKey(removedSite) || !IsGridPositionValid(removedSite.gridPos))
            {
                // If the changed site is unknown, the affected region cannot be trusted.
                // Rebuilding all data under this version keeps the answer correct.
                RebuildAllVoronoiData();
                return;
            }

            VoronoiSite changedSite = sitesByPoi[removedSite];
            List<VoronoiSite> linkedSites = new List<VoronoiSite>(changedSite.midpointsByOtherSite.Keys);

            for (int i = 0; i < linkedSites.Count; i++)
                changedSite.RemoveMidpoint(linkedSites[i]);

            // Partial recalculation only restores the pairs that were already linked to the changed site.
            for (int i = 0; i < linkedSites.Count; i++)
                EnsureMidpointBetweenSites(changedSite, linkedSites[i]);

            // The partial path only checks pairs connected to the changed site.
            // This keeps the update small, even though a full rebuild is the only way to refresh every global radius test.
            RemoveInvalidMidpointsForSite(changedSite);

            // Terrain movement is applied only after the invalid midpoint pairs were removed.
            // This keeps blocked cells and heavy cells from rescuing a midpoint that already failed the geometric test.
            ApplyTerrainWeightsToValidMidpoints(changedSite);

            // Corner ownership is shared across the whole diagram, so polygon support must
            // be rebuilt in one deterministic pass even when midpoint edits were local.
            RebuildSitePolygons();
        }

        void RebuildAllVoronoiData()
        {
            // The site dictionary is rebuilt first so every later step can use clean lookups.
            validSites.Clear();
            sitesByPoi.Clear();

            for (int i = 0; i < sites.Length; i++)
            {
                PointOfInterest poi = sites[i];
                if (poi == null) continue;

                VoronoiSite site = new VoronoiSite(poi);
                sitesByPoi.TryAdd(poi, site);
                validSites.Add(site);
            }

            // Get the midpoint to every site.
            for (int i = 0; i < validSites.Count; i++)
            {
                VoronoiSite site1 = validSites[i];
                for (int j = i + 1; j < validSites.Count; j++)
                    EnsureMidpointBetweenSites(site1, validSites[j]);
            }

            RemoveInvalidMidpoints();

            // Terrain movement is applied after all midpoint pairs have passed the Euclidean radius test.
            // The polygon builder can then use the final, terrain-adjusted midpoint positions.
            ApplyTerrainWeightsToValidMidpoints();

            // The temporary polygon is assembled from valid midpoint positions.
            // The later vertex stage will replace this with shared vertices and terrain movement.
            RebuildSitePolygons();
        }

        void RebuildSitePolygons()
        {
            List<Vector2> availableMapCorners = CreateMapRectanglePolygon();
            for (int i = 0; i < validSites.Count; i++)
            {
                VoronoiSite site = validSites[i];
                site.polygonVertices = BuildPolygonForSite(site, availableMapCorners);
            }
        }

        VoronoiMidpoint CalculateMidpointBetweenSites(VoronoiSite siteA, VoronoiSite siteB)
        {
            Vector2 siteAPos = GetSiteGridPosition(siteA);
            Vector2 siteBPos = GetSiteGridPosition(siteB);
            
            //A + B / 2 is the same as A + dispToB / 2 
            Vector2 midpoint = (siteAPos + siteBPos) * 0.5f;

            Vector2Int gridMidpoint = RoundToGridPosition(midpoint);

            //Store sqr dist from site to midpoint
            //to later use as radius to check midpoint validity 
            float sqrRadius = (siteAPos - midpoint).sqrMagnitude;

            return new VoronoiMidpoint(gridMidpoint, midpoint, sqrRadius, siteA, siteB);
        }

        List<Vector2> BuildPolygonForSite(VoronoiSite site, List<Vector2> availableMapCorners)
        {
            if (validSites.Count == 1)
                return CreateMapRectanglePolygon();

            List<Vector2> polygon = new List<Vector2>();
            foreach (var midpoint in site.midpointsByOtherSite.Values)
                polygon.Add(midpoint.pos);

            AddBoundaryVertices(site, polygon, availableMapCorners);

            // Sorting by angle turns the loose set of points into a drawable loop around the site.
            // This is a temporary shape until the shared-vertex stage builds the real boundary graph.
            SortPolygonVerticesAroundSite(site, polygon);

            return polygon;
        }

        List<Vector2> CreateMapRectanglePolygon()
        {
            // The map rectangle is used when a single site owns the whole map,
            // and also as temporary support when a sparse midpoint set cannot close a polygon.
            float maxX = mapSize.x - 1;
            float maxY = mapSize.y - 1;

            return new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(maxX, 0f),
                new Vector2(maxX, maxY),
                new Vector2(0f, maxY)
            };
        }

        void AddBoundaryVertices(VoronoiSite site, List<Vector2> polygon, List<Vector2> availableMapCorners)
        {
            if (site == null || polygon == null)
                return;

            if (availableMapCorners != null)
            {
                List<Vector2> cornersToCheck = new List<Vector2>(availableMapCorners);
                for (int i = 0; i < cornersToCheck.Count; i++)
                {
                    Vector2 corner = cornersToCheck[i];
                    if (!IsBoundaryPointValidByRadius(site, corner))
                        continue;

                    polygon.Add(corner);

                    availableMapCorners.RemoveAt(i);
                }
            }

            Vector2 closestMapLimit = GetClosestMapLimitProjection(site);
            if (IsBoundaryPointValidByRadius(site, closestMapLimit) &&
                !ContainsVertex(polygon, closestMapLimit))
                polygon.Add(closestMapLimit);
        }

        void SortPolygonVerticesAroundSite(VoronoiSite site, List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 2)
                return;

            Vector2 sitePosition = GetSiteGridPosition(site);
            polygon.Sort((firstVertex, secondVertex) =>
            {
                float firstAngle = Mathf.Atan2(firstVertex.y - sitePosition.y, firstVertex.x - sitePosition.x);
                float secondAngle = Mathf.Atan2(secondVertex.y - sitePosition.y, secondVertex.x - sitePosition.x);
                if (!Mathf.Approximately(firstAngle, secondAngle))
                    return firstAngle.CompareTo(secondAngle);

                float firstDistance = (firstVertex - sitePosition).sqrMagnitude;
                float secondDistance = (secondVertex - sitePosition).sqrMagnitude;
                return firstDistance.CompareTo(secondDistance);
            });
        }

        bool IsPointInsidePolygon(Vector2 point, List<Vector2> polygonVertices)
        {
            if (polygonVertices == null || polygonVertices.Count < 3)
                return false;

            bool isInside = false;
            int previousIndex = polygonVertices.Count - 1;

            for (int currentIndex = 0; currentIndex < polygonVertices.Count; currentIndex++)
            {
                Vector2 previousVertex = polygonVertices[previousIndex];
                Vector2 currentVertex = polygonVertices[currentIndex];

                if (IsPointOnSegment(point, previousVertex, currentVertex))
                    return true;

                bool crossesPointHeight = (currentVertex.y > point.y) != (previousVertex.y > point.y);
                if (crossesPointHeight)
                {
                    float xIntersection = (previousVertex.x - currentVertex.x) * (point.y - currentVertex.y) /
                                          (previousVertex.y - currentVertex.y) + currentVertex.x;

                    if (point.x < xIntersection)
                        isInside = !isInside;
                }

                previousIndex = currentIndex;
            }

            return isInside;
        }

        bool IsPointOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            Vector2 pointFromStart = point - segmentStart;
            float cross = segment.x * pointFromStart.y - segment.y * pointFromStart.x;
            if (Mathf.Abs(cross) > PolygonEpsilon)
                return false;

            float dot = Vector2.Dot(pointFromStart, segment);
            if (dot < -PolygonEpsilon)
                return false;

            return dot <= segment.sqrMagnitude + PolygonEpsilon;
        }

        void EnsureMidpointBetweenSites(VoronoiSite firstSite, VoronoiSite secondSite)
        {
            if (firstSite.HasMidpoint(secondSite))
                return;

            VoronoiMidpoint midpoint = CalculateMidpointBetweenSites(firstSite, secondSite);
            firstSite.AddMidpoint(secondSite, midpoint);
        }

        void RemoveInvalidMidpoints()
        {
            HashSet<VoronoiMidpoint> checkedMidpoints = new HashSet<VoronoiMidpoint>();

            for (int i = 0; i < validSites.Count; i++)
                RemoveInvalidMidpointsForSite(validSites[i], checkedMidpoints);
        }

        /// <summary>
        /// Runs through all midpoints of the site
        /// If any midpoint has another site in its radius, it is invalid and is removed
        /// </summary>
        /// <param name="site"></param>
        /// <param name="checkedMidpoints">OPTIONAL:
        /// hashset of midpoints that were already checked and approved,
        /// so they can be skipped</param>
        void RemoveInvalidMidpointsForSite(VoronoiSite site, HashSet<VoronoiMidpoint> checkedMidpoints = null)
        {
            if (site == null)
                return;

            HashSet<VoronoiSite> invalidOtherSites = new HashSet<VoronoiSite>();
            List<VoronoiSite> linkedSites = new List<VoronoiSite>(site.midpointsByOtherSite.Keys);

            // Partial recalculation intentionally stays local to the changed site.
            // Full rebuilds pass a shared hash set so the same linked midpoint is only validated once.
            for (int i = 0; i < linkedSites.Count; i++)
            {
                VoronoiSite otherSite = linkedSites[i];
                if (!site.TryGetMidpoint(otherSite, out VoronoiMidpoint midpoint))
                    continue;

                if (checkedMidpoints != null && !checkedMidpoints.Add(midpoint))
                    continue;

                if (!IsMidpointValidByEuclideanRadius(midpoint))
                    invalidOtherSites.Add(otherSite);
            }

            foreach (VoronoiSite invalidSite in invalidOtherSites)
                site.RemoveMidpoint(invalidSite);
        }

        bool IsMidpointValidByEuclideanRadius(VoronoiMidpoint midpoint)
        {
            if (midpoint == null)
                return false;

            for (int i = 0; i < validSites.Count; i++)
            {
                VoronoiSite site = validSites[i];
                if (site == midpoint.siteA || site == midpoint.siteB)
                    continue;

                //If any other site besides the parents are inside the radius,
                //the midpoint is not valid, as there is a closer site in direction
                Vector2 sitePosition = GetSiteGridPosition(site);
                float distanceToMidpointSqr = (sitePosition - midpoint.pos).sqrMagnitude;
                if (distanceToMidpointSqr < midpoint.sqrRadius - PolygonEpsilon)
                    return false;
            }

            return true;
        }

        void ApplyTerrainWeightsToValidMidpoints(VoronoiSite singleSite = null)
        {
            if (nodeGrid == null)
                return;

            HashSet<VoronoiMidpoint> checkedMidpoints = new HashSet<VoronoiMidpoint>();

            // A partial update passes one site, while a full rebuild walks every valid site.
            // In both cases the hash set keeps a shared midpoint from being moved twice.
            if (singleSite != null)
            {
                foreach (VoronoiMidpoint midpoint in singleSite.midpointsByOtherSite.Values)
                {
                    if (midpoint == null || !checkedMidpoints.Add(midpoint))
                        continue;

                    ApplyTerrainShiftToMidpoint(midpoint);
                }

                return;
            }

            for (int i = 0; i < validSites.Count; i++)
            {
                VoronoiSite site = validSites[i];
                foreach (VoronoiMidpoint midpoint in site.midpointsByOtherSite.Values)
                {
                    if (midpoint == null || !checkedMidpoints.Add(midpoint))
                        continue;

                    ApplyTerrainShiftToMidpoint(midpoint);
                }
            }
        }

        void ApplyTerrainShiftToMidpoint(VoronoiMidpoint midpoint)
        {
            if (midpoint == null || midpoint.siteA == null || midpoint.siteB == null)
                return;

            List<Vector2Int> lineCells = GetGridLineCellsBetweenSites(midpoint.siteA, midpoint.siteB);
            if (lineCells.Count == 0)
                return;

            int siteAIndex = 0;
            int siteBIndex = lineCells.Count - 1;
            int midpointIndex = lineCells.Count / 2;

            // The first pass looks only at blocked cells.
            // More blocked cells on one side means that side should receive less Voronoi space.
            int siteAUnwalkableCount = CountUnwalkableCellsBetweenIndexes(lineCells, siteAIndex, midpointIndex);
            int siteBUnwalkableCount = CountUnwalkableCellsBetweenIndexes(lineCells, siteBIndex, midpointIndex);
            int unwalkableDifference = siteAUnwalkableCount - siteBUnwalkableCount;

            if (unwalkableDifference > 0)
                midpointIndex = MoveIndexTowardsTarget(midpointIndex, siteAIndex, unwalkableDifference);
            else if (unwalkableDifference < 0)
                midpointIndex = MoveIndexTowardsTarget(midpointIndex, siteBIndex, -unwalkableDifference);

            // The second pass looks only at walkable terrain weights.
            // Unwalkable cells are skipped here because they already had their own movement pass.
            float siteAAverageWeight = GetAverageWalkableWeightBetweenIndexes(lineCells, siteAIndex, midpointIndex,
                out int siteAWalkableCellCount);
            float siteBAverageWeight = GetAverageWalkableWeightBetweenIndexes(lineCells, siteBIndex, midpointIndex,
                out int siteBWalkableCellCount);
            float averageWeightDifference = siteAAverageWeight - siteBAverageWeight;

            if (!Mathf.Approximately(averageWeightDifference, 0f))
            {
                bool siteAHasHigherWeight = averageWeightDifference > 0f;
                int heavierSideCellCount = siteAHasHigherWeight ? siteAWalkableCellCount : siteBWalkableCellCount;
                int weightMovement = heavierSideCellCount > 0
                    ? Mathf.RoundToInt(Mathf.Abs(averageWeightDifference) / heavierSideCellCount)
                    : 0;

                if (weightMovement > 0)
                {
                    int targetIndex = siteAHasHigherWeight ? siteAIndex : siteBIndex;
                    midpointIndex = MoveIndexTowardsTarget(midpointIndex, targetIndex, weightMovement);
                }
            }

            midpointIndex = Mathf.Clamp(midpointIndex, 0, lineCells.Count - 1);
            Vector2Int movedGridPosition = lineCells[midpointIndex];

            // The selected grid cell becomes the stored midpoint position used by the polygon builder.
            // The original radius stays unchanged because radius validation happened before terrain movement.
            midpoint.gridPos = movedGridPosition;
            midpoint.pos = new Vector2(movedGridPosition.x, movedGridPosition.y);
        }

        List<Vector2Int> GetGridLineCellsBetweenSites(VoronoiSite siteA, VoronoiSite siteB)
        {
            List<Vector2Int> lineCells = new List<Vector2Int>();
            if (siteA == null || siteB == null)
                return lineCells;

            Vector2Int startCell = ClampGridPosition(siteA.gridPos);
            Vector2Int endCell = ClampGridPosition(siteB.gridPos);
            Vector2Int displacement = endCell - startCell;
            int xDistance = Mathf.Abs(displacement.x);
            int yDistance = Mathf.Abs(displacement.y);

            // When both sites are on the same cell, the line is just that one cell.
            // This avoids division or accumulator logic for a line with no length.
            if (xDistance == 0 && yDistance == 0)
            {
                AddGridLineCellIfValid(lineCells, startCell);
                return lineCells;
            }

            AddGridLineCellIfValid(lineCells, startCell);

            // This is a DDA-style walk over the dominant axis.
            // The larger movement advances every loop, and the smaller movement advances when its accumulator fills up.
            if (xDistance >= yDistance)
            {
                int xStep = GetStepSign(displacement.x);
                int yStep = GetStepSign(displacement.y);
                int currentX = startCell.x;
                int currentY = startCell.y;
                int yAccumulator = 0;

                for (int step = 0; step < xDistance; step++)
                {
                    currentX += xStep;
                    yAccumulator += yDistance;

                    if (yAccumulator >= xDistance)
                    {
                        currentY += yStep;
                        yAccumulator -= xDistance;
                    }

                    AddGridLineCellIfValid(lineCells, new Vector2Int(currentX, currentY));
                }
            }
            else
            {
                int xStep = GetStepSign(displacement.x);
                int yStep = GetStepSign(displacement.y);
                int currentX = startCell.x;
                int currentY = startCell.y;
                int xAccumulator = 0;

                for (int step = 0; step < yDistance; step++)
                {
                    currentY += yStep;
                    xAccumulator += xDistance;

                    if (xAccumulator >= yDistance)
                    {
                        currentX += xStep;
                        xAccumulator -= yDistance;
                    }

                    AddGridLineCellIfValid(lineCells, new Vector2Int(currentX, currentY));
                }
            }

            return lineCells;
        }

        int CountUnwalkableCellsBetweenIndexes(List<Vector2Int> lineCells, int fromIndex, int midpointIndex)
        {
            if (lineCells == null || lineCells.Count == 0)
                return 0;

            int unwalkableCount = 0;
            int stepDirection = GetStepSign(midpointIndex - fromIndex);

            // The midpoint cell is skipped because it belongs to neither side during comparison.
            // Counting only the cells between the site and midpoint keeps both sides symmetrical.
            for (int index = fromIndex; index != midpointIndex; index += stepDirection)
            {
                PathNode node = GetNodeAtGridPosition(lineCells[index]);
                if (node == null || !node.walkable)
                    unwalkableCount++;
            }

            return unwalkableCount;
        }

        float GetAverageWalkableWeightBetweenIndexes(List<Vector2Int> lineCells, int fromIndex, int midpointIndex,
            out int walkableCellCount)
        {
            walkableCellCount = 0;
            if (lineCells == null || lineCells.Count == 0)
                return 0f;

            int totalWeight = 0;
            int stepDirection = GetStepSign(midpointIndex - fromIndex);

            // The midpoint is skipped for the same reason as the unwalkable pass.
            // Only walkable cells add weight, because blocked cells already moved the midpoint in the first pass.
            for (int index = fromIndex; index != midpointIndex; index += stepDirection)
            {
                PathNode node = GetNodeAtGridPosition(lineCells[index]);
                if (node == null || !node.walkable)
                    continue;

                totalWeight += node.weight;
                walkableCellCount++;
            }

            if (walkableCellCount == 0)
                return 0f;

            return (float)totalWeight / walkableCellCount;
        }

        int MoveIndexTowardsTarget(int currentIndex, int targetIndex, int amount)
        {
            if (amount <= 0 || currentIndex == targetIndex)
                return currentIndex;

            int direction = GetStepSign(targetIndex - currentIndex);
            int movedIndex = currentIndex + direction * amount;

            // The moved index must stay between the old midpoint and the site it is moving toward.
            // Min and max make the clamp work the same way from left-to-right and right-to-left.
            int minIndex = Mathf.Min(currentIndex, targetIndex);
            int maxIndex = Mathf.Max(currentIndex, targetIndex);
            return Mathf.Clamp(movedIndex, minIndex, maxIndex);
        }

        int GetStepSign(int value)
        {
            if (value > 0)
                return 1;

            if (value < 0)
                return -1;

            return 0;
        }

        PathNode GetNodeAtGridPosition(Vector2Int gridPosition)
        {
            if (!IsGridPositionValid(gridPosition))
                return null;

            return nodeGrid[gridPosition.x, gridPosition.y];
        }

        void AddGridLineCellIfValid(List<Vector2Int> lineCells, Vector2Int gridPosition)
        {
            if (lineCells == null || !IsGridPositionValid(gridPosition))
                return;

            if (lineCells.Count > 0 && lineCells[lineCells.Count - 1] == gridPosition)
                return;

            lineCells.Add(gridPosition);
        }

        bool IsBoundaryPointValidByRadius(VoronoiSite ownerSite, Vector2 point)
        {
            if (ownerSite == null)
                return false;

            Vector2 ownerSitePosition = GetSiteGridPosition(ownerSite);
            float pointRadiusSqr = (ownerSitePosition - point).sqrMagnitude;

            for (int i = 0; i < validSites.Count; i++)
            {
                VoronoiSite otherSite = validSites[i];
                if (otherSite == ownerSite)
                    continue;

                Vector2 otherSitePos = GetSiteGridPosition(otherSite);
                float distanceToPointSqr = (otherSitePos - point).sqrMagnitude;
                if (distanceToPointSqr < pointRadiusSqr - PolygonEpsilon)
                    return false;
            }

            return true;
        }

        void CacheClosestSiteOnNode(PathNode node, Vector2Int closestSiteGridPosition)
        {
            node.hasClosestVoronoiSite = true;
            node.closestVoronoiSiteGridPos = closestSiteGridPosition;
            node.closestVoronoiVersion = voronoiVersion;
        }

        bool IsGridPositionValid(Vector2Int gridPosition)
        {
            if (nodeGrid == null)
                return false;

            int gridWidth = nodeGrid.GetLength(0);
            int gridHeight = nodeGrid.GetLength(1);
            if (gridPosition.x < 0 || gridPosition.x >= gridWidth)
                return false;

            if (gridPosition.y < 0 || gridPosition.y >= gridHeight)
                return false;

            return gridPosition.x < mapSize.x && gridPosition.y < mapSize.y;
        }

        Vector2Int ClampGridPosition(Vector2Int gridPosition)
        {
            int gridWidth = nodeGrid != null ? nodeGrid.GetLength(0) : mapSize.x;
            int gridHeight = nodeGrid != null ? nodeGrid.GetLength(1) : mapSize.y;
            int maxX = Mathf.Max(0, Mathf.Min(mapSize.x, gridWidth) - 1);
            int maxY = Mathf.Max(0, Mathf.Min(mapSize.y, gridHeight) - 1);
            int clampedX = Mathf.Clamp(gridPosition.x, 0, maxX);
            int clampedY = Mathf.Clamp(gridPosition.y, 0, maxY);

            return new Vector2Int(clampedX, clampedY);
        }

        Vector2Int RoundToGridPosition(Vector2 gridPosition)
        {
            Vector2Int roundedGridPosition = new Vector2Int(
                Mathf.RoundToInt(gridPosition.x),
                Mathf.RoundToInt(gridPosition.y));

            return ClampGridPosition(roundedGridPosition);
        }

        Vector2 GetSiteGridPosition(VoronoiSite site) 
            => new(site.gridPos.x, site.gridPos.y);

        Vector2 GetClosestMapLimitProjection(VoronoiSite site)
        {
            Vector2 pos = GetSiteGridPosition(site);
            float maxX = mapSize.x - 1f;
            float maxY = mapSize.y - 1f;

            // Check if site is closer to left limit or right limit
            float distToRight = maxX - pos.x;
            float minHorDist;
            float horEdge;
            //if right is closer than left, save dist to max as min hor dist
            if (distToRight < pos.x) 
            {
                minHorDist = distToRight;
                horEdge = maxX;
            }
            else //if left closer is, save pos (dist to 0) as hor dist
            {
                minHorDist = pos.x;
                horEdge = 0f;
            }

            // Check if site is closer to bottom limit or top limit
            float distToTop = maxY - pos.y;
            float minVerDist;
            float verEdge;
            if (distToTop < pos.y) //if top is closer than bottom
            {
                minVerDist = distToTop;
                verEdge = maxY;
            }
            else //if bottom is closer
            {
                minVerDist = pos.y;
                verEdge = 0f;
            }

            // if vertical distance is smaller, use the vertical edge
            if (minVerDist < minHorDist)
                return new Vector2(pos.x, verEdge);

            return new Vector2(horEdge, pos.y);
        }

        float GetSquaredEuclideanDistance(Vector2Int firstGridPosition, Vector2Int secondGridPosition)
        {
            // Squared distance keeps the same ordering as Euclidean distance.
            // Avoiding the square root makes repeated nearest-site sorting cheaper.
            Vector2Int difference = firstGridPosition - secondGridPosition;
            return difference.x * difference.x + difference.y * difference.y;
        }

        bool ContainsVertex(List<Vector2> vertices, Vector2 candidateVertex)
        {
            // Vertices are floats, so exact equality is too strict.
            // A tiny epsilon treats points in the same place as duplicates.
            if (vertices == null)
                return false;

            for (int i = 0; i < vertices.Count; i++)
            {
                if ((vertices[i] - candidateVertex).sqrMagnitude <= PolygonEpsilon)
                    return true;
            }

            return false;
        }

    }
    internal class VoronoiOLD
    {
        const int MaxIncrementalDirtyCells = 256;
        const float MaxIncrementalDirtyRatio = 0.01f;
        const float MaxIncrementalTouchedRatio = 0.25f;

        public int[] regionsByNodeIndex;
        public int[] nearestCostByNodeIndex;
        public int version;
        public int poiVersion;

        public VoronoiOLD(int[] regionsByNodeIndex, int[] nearestCostByNodeIndex, int version, int poiVersion)
        {
            this.regionsByNodeIndex = regionsByNodeIndex;
            this.nearestCostByNodeIndex = nearestCostByNodeIndex;
            this.version = version;
            this.poiVersion = poiVersion;
        }

        internal VoronoiOLD DeepClone()
        {
            if (regionsByNodeIndex == null || nearestCostByNodeIndex == null)
                return null;

            return new VoronoiOLD(
                (int[])regionsByNodeIndex.Clone(),
                (int[])nearestCostByNodeIndex.Clone(),
                version,
                poiVersion);
        }

        internal static VoronoiOLD BuildTerrainRecalculationVoronoi(int version, int snapshotPoiVersion, List<PoiSource> sources,
            bool[] walkableByNode, int[] weightByNode, List<NodeTerrainDelta> changes, VoronoiOLD baselineVoronoi,
            int[][] neighbourIndicesByNode, PathGrid grid)
        {
            if (ShouldUseIncremental(changes, walkableByNode.Length) && baselineVoronoi != null)
            {
                VoronoiOLD incremental = TryBuildIncrementalVoronoi(version, snapshotPoiVersion, sources,
                    walkableByNode, weightByNode, changes, baselineVoronoi, neighbourIndicesByNode, grid);

                if (incremental != null)
                    return incremental;
            }

            return BuildFullVoronoi(version, snapshotPoiVersion, sources, walkableByNode, weightByNode,
                neighbourIndicesByNode, grid);
        }

        internal static VoronoiOLD BuildFullVoronoi(int version, int snapshotPoiVersion, List<PoiSource> sources,
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
                return new VoronoiOLD(regions, nearestCosts, version, snapshotPoiVersion);

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

            return new VoronoiOLD(regions, nearestCosts, version, snapshotPoiVersion);
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

        static VoronoiOLD TryBuildIncrementalVoronoi(int version, int snapshotPoiVersion, List<PoiSource> sources,
            bool[] walkableByNode, int[] weightByNode, IReadOnlyList<NodeTerrainDelta> changes,
            VoronoiOLD baselineVoronoi, int[][] neighbourIndicesByNode, PathGrid grid)
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

            return new VoronoiOLD(regions, nearestCosts, version, snapshotPoiVersion);
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
