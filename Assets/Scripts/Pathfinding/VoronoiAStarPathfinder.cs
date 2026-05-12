using System.Collections.Generic;
using IA.Pathfinding.Grid;
using UnityEngine;
using Random = UnityEngine.Random;

namespace IA.Pathfinding.Voronoi
{
    [System.Serializable]
    public class VoronoiAStarPathfinder : AStar.AStarPathfinder
    {
        List<PointOfInterest> pointsOfInterest;
        List<PointOfInterest> currentPOIs;
        Dictionary<int, PointOfInterest> pointsById;
        Dictionary<Vector2Int, PointOfInterest> pointsByPos;
        readonly object poiLock = new object();

        Voronoi voronoi;

        [Header("DEBUG")]
        [SerializeField, Range(0.001f, 1)] float nodeHeight = 0.5f;
        [SerializeField, Min(1)] int maxGizmoCellsPerTick = 100;
        [SerializeField, Min(0.01f)] float pointGizmoSphereRadius = 1f;
        [SerializeField] Color[] possibleRegionColors;
        Dictionary<int, Color> colorsByRegion;
        List<GizmoArea> gizmoAreaRowSpans = new List<GizmoArea>();
        int cachedGizmoVoronoiVersion = -1;
        int nextGizmoCellIndex;
        bool hasOpenGizmoAreaRowSpan;
        GizmoArea _openGizmoArea;

        struct GizmoArea
        {
            public int region;
            public Vector2Int firstGridPosition;
            public Vector2Int lastGridPosition;

            public GizmoArea(int region, Vector2Int firstGridPosition, Vector2Int lastGridPosition)
            {
                this.region = region;
                this.firstGridPosition = firstGridPosition;
                this.lastGridPosition = lastGridPosition;
            }
        }

        public override void Set(PathGrid grid)
        {
            // The base pathfinder owns the grid reference used by A*.
            // This must happen before any lookup turns world positions into grid cells.
            base.Set(grid);

            // Runtime collections are rebuilt from the serialized point list.
            // This keeps old session data from leaking into a fresh grid setup.
            InitializeRuntimeCollections();
            BuildPointsLookup();

            // At startup every known point is an active Voronoi site.
            // Later calls can narrow this list for caravans or remove depleted mines.
            lock (poiLock)
            {
                currentPOIs.Clear();
                currentPOIs.AddRange(pointsOfInterest);
                CreateFreshVoronoi();
            }

#if UNITY_EDITOR
            SetGizmoColors();
#endif
        }

        public void SetPointsOfInterest(List<PointOfInterest> points)
        {
            // Store a private list so outside callers cannot accidentally change the
            // pathfinder's source collection while it is building lookup tables.
            pointsOfInterest = points != null ? new List<PointOfInterest>(points) : new List<PointOfInterest>();
        }

        public void DrawGizmos()
        {
            // Gizmos are only useful once a grid, Voronoi object, and visible POIs exist.
            // Returning early keeps the scene view quiet while setup is still incomplete.
            if (grid == null || grid.grid == null || voronoi == null)
                return;

            if (currentPOIs == null || currentPOIs.Count <= 0)
                return;

            if (possibleRegionColors == null || possibleRegionColors.Length <= 0)
                return;

            lock (poiLock)
            {
                // Colors are rebuilt lazily so editor gizmos survive domain reloads.
                // The count check also covers active POI changes made while the editor is open.
                if (colorsByRegion == null || colorsByRegion.Count != currentPOIs.Count)
                    SetGizmoColors();

                // The row-span cache belongs to one exact Voronoi version.
                // When the regions change, old spans are cleared before new cells are scanned.
                EnsureGizmoAreaCacheMatchesActiveVoronoi();

                // Only a small amount of cells are scanned per draw call.
                // Finished row spans are saved and reused by later gizmo calls.
                ProcessGizmoAreaCellsForThisTick();

                // Areas are drawn first so grid obstacle and weight gizmos can appear above them.
                // Sites and midpoints are drawn last because they are the important landmarks.
                DrawCachedGizmoAreaSpans();
                DrawGizmoSitesAndMidpoints();
            }
        }

        void EnsureGizmoAreaCacheMatchesActiveVoronoi()
        {
            // The active Voronoi object can be replaced when POIs change.
            // The version can also change when the same object recalculates after terrain edits.
            int activeVoronoiVersion = voronoi != null ? voronoi.VoronoiVersion : -1;
            if (cachedGizmoVoronoiVersion == activeVoronoiVersion)
                return;

            ResetGizmoAreaCache(activeVoronoiVersion);
        }

        void ResetGizmoAreaCache(int voronoiVersion)
        {
            // Cached spans are built from left to right, row by row.
            // Clearing them makes the next scan start from the first grid cell again.
            if (gizmoAreaRowSpans == null)
                gizmoAreaRowSpans = new List<GizmoArea>();
            else
                gizmoAreaRowSpans.Clear();

            cachedGizmoVoronoiVersion = voronoiVersion;
            nextGizmoCellIndex = 0;
            hasOpenGizmoAreaRowSpan = false;
            _openGizmoArea = default;
        }

        void ProcessGizmoAreaCellsForThisTick()
        {
            // The flat index walks horizontally across a row before moving to the next row.
            // This makes it possible to collapse same-region cells into one long cube.
            int gridWidth = grid.gridSize.x;
            int gridHeight = grid.gridSize.y;
            if (gridWidth <= 0 || gridHeight <= 0)
                return;

            int totalCells = gridWidth * gridHeight;
            int processedCells = 0;
            int cellBudget = Mathf.Max(1, maxGizmoCellsPerTick);

            while (nextGizmoCellIndex < totalCells && processedCells < cellBudget)
            {
                Vector2Int gridPosition = GetGizmoGridPositionFromIndex(nextGizmoCellIndex, gridWidth);
                int region = FindPointRegion(gridPosition);
                AddCellToGizmoAreaRowSpan(gridPosition, region);

                nextGizmoCellIndex++;
                processedCells++;
            }

            // The last open span is complete once the scan reaches the end of the grid.
            // Finalizing it here makes sure the final row is not left invisible.
            if (nextGizmoCellIndex >= totalCells)
                FinishOpenGizmoAreaRowSpan();
        }

        Vector2Int GetGizmoGridPositionFromIndex(int cellIndex, int gridWidth)
        {
            // The index is stored as one number so the scan can stop and continue later.
            // Dividing by the width gives the row, and the remainder gives the column.
            int y = cellIndex / gridWidth;
            int x = cellIndex % gridWidth;
            return new Vector2Int(x, y);
        }

        void AddCellToGizmoAreaRowSpan(Vector2Int gridPosition, int region)
        {
            // Cells without a region cannot belong to an area span.
            // Any currently open span is finished before the invalid cell is skipped.
            if (region < 0)
            {
                FinishOpenGizmoAreaRowSpan();
                return;
            }

            // The first valid cell starts the first span.
            // Later cells either extend this span or close it and start a new one.
            if (!hasOpenGizmoAreaRowSpan)
            {
                StartGizmoAreaRowSpan(gridPosition, region);
                return;
            }

            bool isSameRow = _openGizmoArea.lastGridPosition.y == gridPosition.y;
            bool isSameRegion = _openGizmoArea.region == region;
            bool isNextCellInRow = _openGizmoArea.lastGridPosition.x + 1 == gridPosition.x;
            if (!isSameRow || !isSameRegion || !isNextCellInRow)
            {
                FinishOpenGizmoAreaRowSpan();
                StartGizmoAreaRowSpan(gridPosition, region);
                return;
            }

            // Same-region neighbors in the same row become one long span.
            // The first cell stays fixed and the last cell moves forward.
            _openGizmoArea.lastGridPosition = gridPosition;

            // A row cannot continue after its last column.
            // Finishing here lets the row cube draw without waiting for the next tick.
            if (gridPosition.x >= grid.gridSize.x - 1)
                FinishOpenGizmoAreaRowSpan();
        }

        void StartGizmoAreaRowSpan(Vector2Int gridPosition, int region)
        {
            // A row span remembers only its owner and its two edge cells.
            // The cube center and size are calculated later from these two cells.
            _openGizmoArea = new GizmoArea(region, gridPosition, gridPosition);
            hasOpenGizmoAreaRowSpan = true;

            // A single-cell span can also be the last span in a row.
            // Finishing it immediately keeps row endings visible as soon as they are known.
            if (gridPosition.x >= grid.gridSize.x - 1)
                FinishOpenGizmoAreaRowSpan();
        }

        void FinishOpenGizmoAreaRowSpan()
        {
            // Only completed spans are stored for drawing.
            // An unfinished span can safely stay open between two 100-cell scan windows.
            if (!hasOpenGizmoAreaRowSpan)
                return;

            gizmoAreaRowSpans.Add(_openGizmoArea);
            hasOpenGizmoAreaRowSpan = false;
        }

        void DrawCachedGizmoAreaSpans()
        {
            // Each cached span draws as one cube instead of one cube per cell.
            // This keeps the editor visualization much cheaper on large grids.
            for (int i = 0; i < gizmoAreaRowSpans.Count; i++)
            {
                GizmoArea span = gizmoAreaRowSpans[i];
                if (!grid.IsValidGridPosition(span.firstGridPosition) ||
                    !grid.IsValidGridPosition(span.lastGridPosition))
                    continue;

                PathNode firstNode = grid.grid[span.firstGridPosition.x, span.firstGridPosition.y];
                PathNode lastNode = grid.grid[span.lastGridPosition.x, span.lastGridPosition.y];
                if (firstNode == null || lastNode == null)
                    continue;

                Vector3 firstWorldPosition = firstNode.worldPos;
                Vector3 lastWorldPosition = lastNode.worldPos;
                Vector3 spanCenter = (firstWorldPosition + lastWorldPosition) * 0.5f;
                int cellCount = Mathf.Abs(span.lastGridPosition.x - span.firstGridPosition.x) + 1;
                Vector3 spanSize = new Vector3(grid.NodeDiameter * cellCount, nodeHeight, grid.NodeDiameter);

                Gizmos.color = GetGizmoRegionColor(span.region);
                Gizmos.DrawCube(spanCenter, spanSize);
            }
        }

        void DrawGizmoSitesAndMidpoints()
        {
            // Midpoints are shared by two sites, so a set prevents drawing the same sphere twice.
            // Sites are still drawn for every active Voronoi site because each one is a landmark.
            HashSet<Voronoi.VoronoiMidpoint> drawnMidpoints = new HashSet<Voronoi.VoronoiMidpoint>();
            foreach (KeyValuePair<PointOfInterest, Voronoi.VoronoiSite> entry in voronoi.SitesByPoi)
            {
                Voronoi.VoronoiSite site = entry.Value;
                if (site == null || site.site == null)
                    continue;

                Color areaColor = GetGizmoRegionColor(site.site.id);
                Gizmos.color = GetDarkerGizmoColor(areaColor);
                DrawGizmoSphereAtGridPosition(site.gridPos);

                foreach (Voronoi.VoronoiMidpoint midpoint in site.midpointsByOtherSite.Values)
                {
                    if (midpoint == null || !drawnMidpoints.Add(midpoint))
                        continue;

                    Gizmos.color = Color.gray;
                    DrawGizmoSphereAtGridPosition(midpoint.gridPos);
                }
            }
        }

        void DrawGizmoSphereAtGridPosition(Vector2Int gridPosition)
        {
            // The sphere uses the node center so it lines up with the area cubes.
            // Invalid points are skipped because they cannot be converted to a safe world position.
            if (!grid.IsValidGridPosition(gridPosition))
                return;

            PathNode node = grid.grid[gridPosition.x, gridPosition.y];
            if (node == null)
                return;

            Vector3 worldPosition = node.worldPos;
            Gizmos.DrawSphere(worldPosition, pointGizmoSphereRadius);
        }

        Color GetGizmoRegionColor(int region)
        {
            // Region colors are assigned by POI id.
            // White is a safe fallback if a color is missing during an editor refresh.
            if (colorsByRegion != null && colorsByRegion.TryGetValue(region, out Color regionColor))
                return regionColor;

            return Color.white;
        }

        Color GetDarkerGizmoColor(Color color)
        {
            // Site spheres use the same hue as the area, only darker.
            // Keeping alpha at one makes the site markers easy to read in the scene view.
            const float darkenAmount = 0.55f;
            return new Color(color.r * darkenAmount, color.g * darkenAmount, color.b * darkenAmount, 1f);
        }

        public int FindPointRegion(Vector3 point)
        {
            // World positions are converted once here so the rest of the code can
            // work with the same grid coordinates as the Voronoi cache.
            Vector2Int gridPosition = grid.GetGridPosition(point);
            return FindPointRegion(gridPosition);
        }

        public int FindPointRegion(Vector2Int gridPosition)
        {
            // Invalid cells and missing diagrams have no meaningful owning site.
            // Returning -1 lets callers fall back or report a failed path.
            if (voronoi == null || grid == null || !grid.IsValidGridPosition(gridPosition))
                return -1;

            lock (poiLock)
            {
                // The closest-site query is intentionally calculated only when needed.
                // If this node already cached the current Voronoi version, this is just a read.
                Vector2Int closestSiteGridPosition = voronoi.GetClosestSite(gridPosition);
                if (closestSiteGridPosition.x < 0 || closestSiteGridPosition.y < 0)
                    return -1;

                // Sites are still identified by their POI ids.
                // The Voronoi class returns a site position, so the pathfinder maps that back to the POI.
                if (pointsByPos.TryGetValue(closestSiteGridPosition, out PointOfInterest pointOfInterest))
                    return pointOfInterest.id;
            }

            return -1;
        }

        public int FindSafePointRegion(Vector3 point)
        {
            // The safe lookup starts from the exact world position and lets the
            // grid conversion choose the nearest cell.
            Vector2Int gridPosition = grid.GetGridPosition(point);
            return FindSafePointRegion(gridPosition);
        }

        public int FindSafePointRegion(Vector2Int gridPosition)
        {
            // First try the requested cell directly.
            // Most calls succeed here and benefit from the per-node Voronoi cache.
            int region = FindPointRegion(gridPosition);
            if (region != -1)
                return region;

            // If the requested cell cannot produce a region, nearby walkable cells
            // are checked as a small recovery path for edge and obstacle cases.
            if (!grid.IsValidGridPosition(gridPosition))
                return -1;

            PathNode startNode = grid.grid[gridPosition.x, gridPosition.y];
            for (int i = 0; i < startNode.neighbours.Count; i++)
            {
                PathNode neighbour = startNode.neighbours[i];
                if (!neighbour.walkable)
                    continue;

                int neighbourRegion = FindPointRegion(neighbour.gridPos);
                if (neighbourRegion != -1)
                    return neighbourRegion;
            }

            return -1;
        }

        public int GetPointOfInterestID(Vector3 point)
        {
            // Exact POI lookup is different from closest-site lookup.
            // This is used when agents are already at a mine/storage cell.
            Vector2Int gridPosition = grid.GetGridPosition(point);
            return GetPointOfInterestID(gridPosition);
        }

        public int GetPointOfInterestID(Vector2Int gridPosition)
        {
            // The lookup table contains every known active POI position by grid cell.
            // If there is no POI on this exact cell, callers receive -1.
            if (pointsByPos.TryGetValue(gridPosition, out PointOfInterest pointOfInterest))
                return pointOfInterest.id;

            return -1;
        }

        public List<PathNode> FindPathToPOI(Vector3 startPosition)
        {
            // The closest region chooses the destination POI.
            // A* still handles the actual walkable route through the grid.
            int region = FindSafePointRegion(startPosition);
            return FindPathToPOI(startPosition, region);
        }

        public List<PathNode> FindPathToPOI(Vector3 startPosition, int region)
        {
            // If the region cannot be mapped to a current point of interest,
            // there is no destination for this path request.
            if (!pointsById.TryGetValue(region, out PointOfInterest pointOfInterest))
                return null;

            // A* works with start and end nodes.
            // The Voronoi region only selects which POI node should be the target.
            PathNode startNode = grid.NodeFromWorldPoint(startPosition);
            PathNode endNode = grid.grid[pointOfInterest.gridPos.x, pointOfInterest.gridPos.y];

            return FindPath(startNode, endNode);
        }

        public bool RemovePointOfInterest(Vector2Int gridPosition)
        {
            // Removing by position first finds the current owner of that cell.
            // The id-based method performs the actual data update.
            int region = FindPointRegion(gridPosition);
            if (region < 0)
                return false;

            return RemovePointOfInterest(region);
        }

        public bool RemovePointOfInterest(int region)
        {
            // A missing id means this pathfinder has already removed the POI
            // or never knew about it, so no Voronoi rebuild is needed.
            if (!pointsById.TryGetValue(region, out PointOfInterest pointOfInterest))
                return false;

            lock (poiLock)
            {
                // Remove the POI from every lookup that can influence future queries.
                // This prevents the next closest-site query from returning a depleted mine.
                pointsById.Remove(region);
                pointsByPos.Remove(pointOfInterest.gridPos);
                pointsOfInterest.Remove(pointOfInterest);
                currentPOIs.Remove(pointOfInterest);

                // A new Voronoi object receives a fresh version counter, so node caches
                // must be cleared before the object starts answering closest-site queries.
                CreateFreshVoronoi();
            }

#if UNITY_EDITOR
            SetGizmoColors();
#endif

            return true;
        }

        public bool UpdatePointsOfInterest(List<int> pointIds)
        {
            // Null means no active points.
            // The set is used for comparison, while the list order is preserved below when possible.
            if (pointIds == null)
                pointIds = new List<int>();

            HashSet<int> desiredPointIds = new HashSet<int>(pointIds);
            bool needsToBeUpdated = false;

            lock (poiLock)
            {
                // Compare by id because the same POI objects can be shared across layers.
                // If the active id set is unchanged, the current Voronoi data can stay in place.
                if (desiredPointIds.Count == currentPOIs.Count)
                {
                    for (int i = 0; i < currentPOIs.Count; i++)
                    {
                        if (!desiredPointIds.Contains(currentPOIs[i].id))
                        {
                            needsToBeUpdated = true;
                            break;
                        }
                    }
                }
                else
                {
                    needsToBeUpdated = true;
                }

                if (!needsToBeUpdated)
                    return false;

                // Rebuild the active POI list from known ids.
                // Duplicate ids are ignored so one mine cannot appear twice as a site.
                currentPOIs.Clear();
                HashSet<int> addedPointIds = new HashSet<int>();
                for (int i = 0; i < pointIds.Count; i++)
                {
                    int pointId = pointIds[i];
                    if (!addedPointIds.Add(pointId))
                        continue;

                    if (pointsById.TryGetValue(pointId, out PointOfInterest pointOfInterest))
                        currentPOIs.Add(pointOfInterest);
                }

                CreateFreshVoronoi();
            }

#if UNITY_EDITOR
            SetGizmoColors();
#endif

            return true;
        }

        public bool RecalculateVoronoiAfterTerrainChange(IReadOnlyList<NodeTerrainDelta> terrainChanges)
        {
            // The grid has already applied these terrain changes before this method is called.
            // The list is used as a simple signal that something relevant actually changed.
            if (terrainChanges == null || terrainChanges.Count <= 0)
                return false;

            lock (poiLock)
            {
                // If the pathfinder has not created a diagram yet, build one from the current POIs.
                // Otherwise the existing diagram can refresh its terrain-influenced midpoint data.
                if (voronoi == null)
                    CreateFreshVoronoi();
                else
                    voronoi.CalculateVoronoi();
            }

            return true;
        }

        void InitializeRuntimeCollections()
        {
            // All runtime collections are created once and then cleared on setup.
            // This keeps allocations simple while still avoiding stale data.
            if (pointsOfInterest == null)
                pointsOfInterest = new List<PointOfInterest>();

            if (currentPOIs == null)
                currentPOIs = new List<PointOfInterest>();
            else
                currentPOIs.Clear();

            if (pointsByPos == null)
                pointsByPos = new Dictionary<Vector2Int, PointOfInterest>();
            else
                pointsByPos.Clear();

            if (pointsById == null)
                pointsById = new Dictionary<int, PointOfInterest>();
            else
                pointsById.Clear();
        }

        void BuildPointsLookup()
        {
            // Each configured point is normalized into grid space before pathfinding begins.
            // The same POI can then be found either by id or by exact grid position.
            for (int i = 0; i < pointsOfInterest.Count; i++)
            {
                PointOfInterest pointOfInterest = pointsOfInterest[i];
                if (pointOfInterest == null || pointOfInterest.t == null)
                {
                    Debug.LogError("Point of interest " + i + " has no transform");
                    continue;
                }

                // A zero id means the point was not assigned a stable id by the caller.
                // The transform instance id keeps it usable for this runtime session.
                if (pointOfInterest.id == 0)
                    pointOfInterest.id = pointOfInterest.t.GetInstanceID();

                pointOfInterest.gridPos = grid.GetGridPosition(pointOfInterest.t.position);
                pointsById[pointOfInterest.id] = pointOfInterest;
                pointsByPos[pointOfInterest.gridPos] = pointOfInterest;
            }
        }

        void CreateFreshVoronoi()
        {
            // New Voronoi instances start their version counter from the beginning.
            // Clearing node caches prevents an old matching version number from being reused by accident.
            ClearClosestSiteCacheOnGrid();

            // The Voronoi class owns the closest-site logic.
            // Passing a snapshot array keeps it stable even if the active list changes later.
            PointOfInterest[] activeSites = currentPOIs.ToArray();
            
            if(voronoi == null)
                voronoi = new Voronoi(activeSites, grid.gridSize, grid.grid);
            else
                voronoi.CalculateNewVoronoi(activeSites);
        }

        void ClearClosestSiteCacheOnGrid()
        {
            // Every node stores the closest site and the Voronoi version that produced it.
            // Resetting these fields makes the next query calculate and cache a fresh answer.
            if (grid == null || grid.grid == null)
                return;

            for (int x = 0; x < grid.gridSize.x; x++)
            {
                for (int y = 0; y < grid.gridSize.y; y++)
                {
                    PathNode node = grid.grid[x, y];
                    if (node == null)
                        continue;

                    node.hasClosestVoronoiSite = false;
                    node.closestVoronoiSiteGridPos = new Vector2Int(-1, -1);
                    node.closestVoronoiVersion = -1;
                }
            }
        }

        void SetGizmoColors()
        {
            // The color table is regenerated from the currently active POIs.
            // This keeps editor visualization aligned with active closest-site queries.
            if (colorsByRegion == null)
                colorsByRegion = new Dictionary<int, Color>();
            else
                colorsByRegion.Clear();

            if (currentPOIs == null || currentPOIs.Count == 0)
                return;

            if (possibleRegionColors == null || possibleRegionColors.Length == 0)
                return;

            lock (poiLock)
            {
                // Use the configured palette in order while it has enough colors.
                // When there are more regions than colors, reuse random palette entries.
                if (currentPOIs.Count <= possibleRegionColors.Length)
                {
                    for (int i = 0; i < currentPOIs.Count; i++)
                        colorsByRegion.TryAdd(currentPOIs[i].id, possibleRegionColors[i]);
                }
                else
                {
                    for (int i = 0; i < possibleRegionColors.Length; i++)
                        colorsByRegion.TryAdd(currentPOIs[i].id, possibleRegionColors[i]);

                    for (int i = possibleRegionColors.Length; i < currentPOIs.Count; i++)
                    {
                        int randomColorIndex = Random.Range(0, possibleRegionColors.Length);
                        colorsByRegion.TryAdd(currentPOIs[i].id, possibleRegionColors[randomColorIndex]);
                    }
                }
            }
        }
    }
}
