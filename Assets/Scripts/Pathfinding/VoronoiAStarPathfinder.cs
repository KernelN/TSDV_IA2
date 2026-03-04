using System.Collections.Generic;
using System.Threading.Tasks;
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
        Dictionary<Vector2Int, int> regionsByNode;
        Dictionary<Vector2Int, int> nearestCostByNode;
        Dictionary<Vector2Int, Dictionary<int, float>> regionsCostByNode;
        int[] regionsByNodeIndex;
        int[] nearestCostByNodeIndex;
        float[][] regionsCostByNodeIndex;
        int[] poiIdsByCostColumn;
        int[][] neighbourIndicesByNode;
        readonly object poiLock = new object();
        readonly object terrainStateLock = new object();
        readonly Dictionary<int, NodeTerrainDelta> queuedTerrainChangesByNode = new Dictionary<int, NodeTerrainDelta>();

        Voronoi activeVoronoi;
        Voronoi stagedTerrainVoronoi;

        Task<Voronoi> runningTerrainTask;
        bool terrainTaskRunning;
        bool terrainRequestPending;
        float terrainRequestDeadline;

        int terrainRequestVersion;
        int poiVersion;
        [Header("DEBUG")]
        [SerializeField, Range(0.001f, 1)] float nodeHeight = 0.5f;
        [SerializeField] Color[] possibleRegionColors;
        Dictionary<int, Color> colorsByRegion;

        public override void Set(PathGrid grid)
        {
            base.Set(grid);

            InitializeRuntimeCollections();
            BuildPointsLookup();
            neighbourIndicesByNode = grid.BuildNeighbourIndicesByNode();

            lock (poiLock)
            {
                currentPOIs.Clear();
                currentPOIs.AddRange(pointsOfInterest);
            }

            CalculateVoronoi();

#if UNITY_EDITOR
            SetGizmoColors();
#endif
        }
        public void Load(PathGrid grid, Dictionary<Vector2Int, Dictionary<int, float>> regionsCostByNode)
        {
            base.Set(grid);

            InitializeRuntimeCollections();
            BuildPointsLookup();
            neighbourIndicesByNode = grid.BuildNeighbourIndicesByNode();

            if (regionsCostByNode == null)
                this.regionsCostByNode = new Dictionary<Vector2Int, Dictionary<int, float>>();
            else
                this.regionsCostByNode = regionsCostByNode;

            lock (poiLock)
            {
                currentPOIs.Clear();
                currentPOIs.AddRange(pointsOfInterest);
            }

            if (regionsCostByNode == null)
                CalculateVoronoi();
            else
                UpdateVoronoi();

#if UNITY_EDITOR
            SetGizmoColors();
#endif
        }

        public void SetPointsOfInterest(List<PointOfInterest> points)
        {
            pointsOfInterest = points ?? new List<PointOfInterest>();
        }
        public void DrawGizmos()
        {
            Voronoi snapshot = activeVoronoi;
            if (snapshot == null || snapshot.regionsByNodeIndex == null)
                return;

            if (currentPOIs == null || currentPOIs.Count <= 0)
                return;

            if (possibleRegionColors == null || possibleRegionColors.Length <= 0)
                return;

            if (colorsByRegion == null)
                SetGizmoColors();

            Vector3 nodeSize = new Vector3(grid.NodeDiameter, nodeHeight, grid.NodeDiameter);
            for (int x = 0; x < grid.gridSize.x; x++)
            {
                for (int y = 0; y < grid.gridSize.y; y++)
                {
                    int region = FindPointRegion(new Vector2Int(x, y));

                    colorsByRegion.TryGetValue(region, out Color regionColor);
                    Gizmos.color = regionColor;

                    Gizmos.DrawCube(grid.grid[x, y].worldPos, nodeSize);
                }
            }
        }

        public int FindPointRegion(Vector3 point)
        {
            Vector2Int gridPos = grid.GetGridPosition(point);
            return FindPointRegion(gridPos);
        }
        public int FindPointRegion(Vector2Int gridPos)
        {
            Voronoi snapshot = activeVoronoi;
            if (snapshot == null || snapshot.regionsByNodeIndex == null)
                return -1;

            int index = grid.GetFlatNodeIndex(gridPos);
            if (index >= 0)
                return snapshot.regionsByNodeIndex[index];

            return -1;
        }
        public int FindSafePointRegion(Vector3 point)
        {
            Vector2Int gridPos = grid.GetGridPosition(point);
            return FindSafePointRegion(gridPos);
        }
        public int FindSafePointRegion(Vector2Int gridPos)
        {
            int region = FindPointRegion(gridPos);
            if (region != -1)
                return region;

            if (!grid.IsValidGridPosition(gridPos))
                return -1;

            PathNode startNode = grid.grid[gridPos.x, gridPos.y];
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
            Vector2Int gridPos = grid.GetGridPosition(point);
            return GetPointOfInterestID(gridPos);
        }
        public int GetPointOfInterestID(Vector2Int gridPos)
        {
            if (pointsByPos.TryGetValue(gridPos, out PointOfInterest poi))
                return poi.id;

            return -1;
        }
        public List<PathNode> FindPathToPOI(Vector3 startPos)
        {
            int region = FindSafePointRegion(startPos);
            return FindPathToPOI(startPos, region);
        }
        public List<PathNode> FindPathToPOI(Vector3 startPos, int region)
        {
            if (!pointsById.TryGetValue(region, out PointOfInterest poi))
                return null;

            PathNode startNode = grid.NodeFromWorldPoint(startPos);
            PathNode endNode = grid.grid[poi.gridPos.x, poi.gridPos.y];

            return FindPath(startNode, endNode);
        }
        public void RemovePointOfInterest(Vector2Int gridPos)
        {
            int region = FindPointRegion(gridPos);
            if (region < 0)
                return;

            RemovePointOfInterest(region);
        }
        public void RemovePointOfInterest(int region)
        {
            if (!pointsById.TryGetValue(region, out PointOfInterest poi))
                return;

            pointsById.Remove(region);
            pointsByPos.Remove(poi.gridPos);

            lock (poiLock)
            {
                currentPOIs.Remove(poi);
            }

            InvalidateTerrainRecalculationForPoiChange();
            UpdateVoronoi();
        }
        public void UpdatePointsOfInterest(List<int> pointsID)
        {
            if (pointsID == null)
                pointsID = new List<int>();

            bool needsToBeUpdated = false;

            lock (poiLock)
            {
                if (pointsID.Count == currentPOIs.Count)
                {
                    HashSet<int> newIds = new HashSet<int>(pointsID);
                    for (int i = 0; i < currentPOIs.Count; i++)
                    {
                        if (!newIds.Contains(currentPOIs[i].id))
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
                    return;

                currentPOIs.Clear();
                for (int i = 0; i < pointsID.Count; i++)
                {
                    if (pointsById.TryGetValue(pointsID[i], out PointOfInterest poi))
                        currentPOIs.Add(poi);
                }
            }

            InvalidateTerrainRecalculationForPoiChange();
            UpdateVoronoi();

#if UNITY_EDITOR
            SetGizmoColors();
#endif
        }
        public Dictionary<Vector2Int, Dictionary<int, float>> GetRegionsCostByNode()
        {
            if (regionsCostByNode == null)
                regionsCostByNode = new Dictionary<Vector2Int, Dictionary<int, float>>();

            if (regionsCostByNode.Count == 0 && regionsCostByNodeIndex != null)
                BuildRegionsCostDictionaryFromIndexBuffer();

            return regionsCostByNode;
        }
        public void RequestTerrainRecalculation(IReadOnlyList<NodeTerrainDelta> changes, float maxCalcSeconds)
        {
            if (changes == null || changes.Count <= 0)
                return;

            lock (terrainStateLock)
            {
                for (int i = 0; i < changes.Count; i++)
                    queuedTerrainChangesByNode[changes[i].nodeIndex] = changes[i];

                terrainRequestPending = true;
                terrainRequestVersion++;
                terrainRequestDeadline = Time.realtimeSinceStartup + Mathf.Max(0f, maxCalcSeconds);
            }
        }
        public void TickTerrainRecalculation(float now)
        {
            Task<Voronoi> completedTask = null;
            List<NodeTerrainDelta> jobChanges = null;
            int jobVersion = 0;
            int jobPoiVersion = 0;
            bool shouldStartJob = false;

            lock (terrainStateLock)
            {
                //If task is complete, tag it
                if (terrainTaskRunning && runningTerrainTask != null && runningTerrainTask.IsCompleted)
                {
                    completedTask = runningTerrainTask;
                    runningTerrainTask = null;
                    terrainTaskRunning = false;
                }

                //If there are no tasks running and there IS a task pending,
                //queue node changes and flag the task for starting immediately 
                if (!terrainTaskRunning && terrainRequestPending && queuedTerrainChangesByNode.Count > 0 && now >= terrainRequestDeadline)
                {
                    jobChanges = new List<NodeTerrainDelta>(queuedTerrainChangesByNode.Values);
                    queuedTerrainChangesByNode.Clear();
                    terrainRequestPending = false;

                    jobVersion = terrainRequestVersion;
                    jobPoiVersion = poiVersion;
                    shouldStartJob = true;
                }
            }

            //Get result / voronoi snapshot of task tagged as complete
            if (completedTask != null)
                HandleCompletedTerrainTask(completedTask);

            if (!shouldStartJob)
                return;

            grid.GetCurrentGridValues(out bool[] walkableByNode, out int[] weightByNode);
            
            
            List<PoiSource> sources = GetCurrentPOIs();
            
            Voronoi baselineVoronoi = activeVoronoi.DeepClone();

            Task<Voronoi> startedTask = Task.Run(
                () => Voronoi.BuildTerrainRecalculationVoronoi(jobVersion, jobPoiVersion, sources, walkableByNode, weightByNode,
                    jobChanges, baselineVoronoi, neighbourIndicesByNode, grid));

            lock (terrainStateLock)
            {
                runningTerrainTask = startedTask;
                terrainTaskRunning = true;
            }
        }
        public bool CommitPendingTerrainVoronoi()
        {
            Voronoi voronoiToCommit;

            lock (terrainStateLock)
            {
                if (stagedTerrainVoronoi == null)
                    return false;

                if (stagedTerrainVoronoi.version < terrainRequestVersion)
                {
                    stagedTerrainVoronoi = null;
                    return false;
                }

                if (stagedTerrainVoronoi.poiVersion != poiVersion)
                {
                    stagedTerrainVoronoi = null;
                    return false;
                }

                voronoiToCommit = stagedTerrainVoronoi;
                stagedTerrainVoronoi = null;
            }

            SetActiveVoronoi(voronoiToCommit);

#if UNITY_EDITOR
            SetGizmoColors();
#endif

            return true;
        }
        void InitializeRuntimeCollections()
        {
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

            if (regionsByNode == null)
                regionsByNode = new Dictionary<Vector2Int, int>();
            else
                regionsByNode.Clear();

            if (nearestCostByNode == null)
                nearestCostByNode = new Dictionary<Vector2Int, int>();
            else
                nearestCostByNode.Clear();

            if (regionsCostByNode == null)
                regionsCostByNode = new Dictionary<Vector2Int, Dictionary<int, float>>();
            else
                regionsCostByNode.Clear();
        }
        void BuildPointsLookup()
        {
            for (int i = 0; i < pointsOfInterest.Count; i++)
            {
                PointOfInterest poi = pointsOfInterest[i];
                if (poi == null || poi.t == null)
                {
                    Debug.LogError("Point of interest " + i + " has no transform");
                    continue;
                }

                if (poi.id == 0)
                    poi.id = poi.t.GetInstanceID();

                poi.gridPos = grid.GetGridPosition(poi.t.position);
                pointsById[poi.id] = poi;
                pointsByPos[poi.gridPos] = poi;
            }
        }
        void CalculateVoronoi()
        {
            grid.GetCurrentGridValues(out bool[] walkableByNode, out int[] weightByNode);
            List<PoiSource> sources = GetCurrentPOIs();

            ReadTerrainStateVersions(out int snapshotVersion, out int snapshotPoiVersion);
            Voronoi snapshot = Voronoi.BuildFullVoronoi(snapshotVersion, snapshotPoiVersion, sources, walkableByNode, weightByNode, neighbourIndicesByNode, grid);
            SetActiveVoronoi(snapshot);
        }
        void UpdateVoronoi()
        {
            CalculateVoronoi();
        }
        List<PoiSource> GetCurrentPOIs()
        {
            List<PoiSource> sources = new List<PoiSource>();

            lock (poiLock)
            {
                for (int i = 0; i < currentPOIs.Count; i++)
                {
                    PointOfInterest poi = currentPOIs[i];
                    if (poi == null)
                        continue;

                    if (!grid.IsValidGridPosition(poi.gridPos))
                        continue;

                    sources.Add(new PoiSource(poi.id, grid.GetFlatNodeIndex(poi.gridPos)));
                }
            }

            return sources;
        }
        void SetActiveVoronoi(Voronoi snapshot)
        {
            activeVoronoi = snapshot;
            regionsByNodeIndex = snapshot.regionsByNodeIndex;
            nearestCostByNodeIndex = snapshot.nearestCostByNodeIndex;
            SyncNearestRegionDictionary();
        }
        void SyncNearestRegionDictionary()
        {
            if (regionsByNode == null)
                regionsByNode = new Dictionary<Vector2Int, int>();
            else
                regionsByNode.Clear();

            if (nearestCostByNode == null)
                nearestCostByNode = new Dictionary<Vector2Int, int>();
            else
                nearestCostByNode.Clear();

            if (regionsByNodeIndex == null || nearestCostByNodeIndex == null)
                return;

            for (int nodeIndex = 0; nodeIndex < regionsByNodeIndex.Length; nodeIndex++)
            {
                Vector2Int pos = grid.GetGridPositionFromIndex(nodeIndex);
                regionsByNode[pos] = regionsByNodeIndex[nodeIndex];
                nearestCostByNode[pos] = nearestCostByNodeIndex[nodeIndex];
            }
        }
        void InvalidateTerrainRecalculationForPoiChange()
        {
            lock (terrainStateLock)
            {
                poiVersion++;
                terrainRequestVersion++;
                stagedTerrainVoronoi = null;
            }
        }
        void ReadTerrainStateVersions(out int requestVersion, out int currentPoiVersion)
        {
            lock (terrainStateLock)
            {
                requestVersion = terrainRequestVersion;
                currentPoiVersion = poiVersion;
            }
        }
        ///Try get Voronoi from completed task
        void HandleCompletedTerrainTask(Task<Voronoi> completedTask)
        {
            Voronoi result;
            try
            {
                result = completedTask.GetAwaiter().GetResult();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Voronoi terrain recalculation failed: {ex.Message}");
                return;
            }

            if (result == null)
                return;

            lock (terrainStateLock)
            {
                if (result.version < terrainRequestVersion)
                    return;

                if (result.poiVersion != poiVersion)
                    return;

                if (stagedTerrainVoronoi == null || result.version >= stagedTerrainVoronoi.version)
                    stagedTerrainVoronoi = result;
            }
        }
        void BuildRegionsCostDictionaryFromIndexBuffer()
        {
            if (regionsCostByNode == null)
                regionsCostByNode = new Dictionary<Vector2Int, Dictionary<int, float>>();
            else
                regionsCostByNode.Clear();

            if (regionsCostByNodeIndex == null || poiIdsByCostColumn == null)
                return;

            for (int nodeIndex = 0; nodeIndex < regionsCostByNodeIndex.Length; nodeIndex++)
            {
                Vector2Int gridPos = grid.GetGridPositionFromIndex(nodeIndex);
                float[] costs = regionsCostByNodeIndex[nodeIndex];
                Dictionary<int, float> newCosts = new Dictionary<int, float>(poiIdsByCostColumn.Length);
                regionsCostByNode[gridPos] = newCosts;

                for (int i = 0; i < poiIdsByCostColumn.Length; i++)
                {
                    int poiId = poiIdsByCostColumn[i];
                    newCosts[poiId] = costs[i];
                }
            }
        }
        void SetGizmoColors()
        {
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
                        int rIndex = Random.Range(0, possibleRegionColors.Length);
                        colorsByRegion.TryAdd(currentPOIs[i].id, possibleRegionColors[rIndex]);
                    }
                }
            }
        }
    }
}
