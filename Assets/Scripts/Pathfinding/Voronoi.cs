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

            public void RemoveMidpoint(VoronoiSite otherSite)
            {
                if (otherSite == null || !midpointsByOtherSite.Remove(otherSite))
                    return;

                otherSite.midpointsByOtherSite.Remove(this);
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
            
            CalculateVoronoi();
        }

        public void CalculateNewVoronoi(PointOfInterest[] sites)
        {
            this.sites = sites ?? new PointOfInterest[0];
            CalculateVoronoi();
        }

        public void CalculateVoronoi()
        {
            // Every full rebuild creates a new version. Nodes keep the version number of
            // their cached closest site, so old answers become stale automatically.
            voronoiVersion++;

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

            // Terrain movement is applied after all valid midpoint pairs have been found
            ApplyTerrainWeightsToValidMidpoints();

            //Once the midpoints have been moved, build site polygons
            List<Vector2> availableMapCorners = CreateMapRectanglePolygon();
            for (int i = 0; i < validSites.Count; i++)
            {
                VoronoiSite site = validSites[i];
                site.polygonVertices = BuildPolygonForSite(site, availableMapCorners);
            }
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
                float firstDistance = GetSqrDistance(nodeGridPosition, firstSite.gridPos);
                float secondDistance = GetSqrDistance(nodeGridPosition, secondSite.gridPos);
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
                int cornerIndex = 0;
                while (cornerIndex < availableMapCorners.Count)
                {
                    Vector2 corner = availableMapCorners[cornerIndex];
                    if (IsBoundaryPointValidByRadius(site, corner))
                    {
                        polygon.Add(corner);
                        availableMapCorners.RemoveAt(cornerIndex);
                    }
                    else
                        cornerIndex++;
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

                if (!IsMidpointValid(midpoint))
                    invalidOtherSites.Add(otherSite);
            }

            foreach (VoronoiSite invalidSite in invalidOtherSites)
                site.RemoveMidpoint(invalidSite);
        }

        bool IsMidpointValid(VoronoiMidpoint midpoint)
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

        void ApplyTerrainWeightsToValidMidpoints()
        {
            if (nodeGrid == null)
                return;

            HashSet<VoronoiMidpoint> checkedMidpoints = new HashSet<VoronoiMidpoint>();
            
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

        int CountUnwalkableCellsBetweenIndexes(List<Vector2Int> lineCells, int fromIndex, int toIndex)
        {
            if (lineCells == null || lineCells.Count == 0)
                return 0;

            int unwalkableCount = 0;
            int stepDirection = GetStepSign(toIndex - fromIndex);

            // The midpoint cell is skipped because it belongs to neither side during comparison.
            // Counting only the cells between the site and midpoint keeps both sides symmetrical.
            for (int index = fromIndex; index != toIndex; index += stepDirection)
            {
                PathNode node = GetNodeAtGridPosition(lineCells[index]);
                if (node == null || !node.walkable)
                    unwalkableCount++;
            }

            return unwalkableCount;
        }

        float GetAverageWalkableWeightBetweenIndexes(List<Vector2Int> lineCells, int fromIndex, int toIndex,
            out int walkableCellCount)
        {
            walkableCellCount = 0;
            if (lineCells == null || lineCells.Count == 0)
                return 0f;

            int totalWeight = 0;
            int stepDirection = GetStepSign(toIndex - fromIndex);

            // The midpoint is skipped for the same reason as the unwalkable pass.
            // Only walkable cells add weight, because blocked cells already moved the midpoint in the first pass.
            for (int index = fromIndex; index != toIndex; index += stepDirection)
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

        /// <summary>
        /// Use a special method to keep decimals, even using Vec2Int
        /// </summary>
        /// <returns></returns>
        float GetSqrDistance(Vector2Int firstGridPosition, Vector2Int secondGridPosition)
        {
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
}
