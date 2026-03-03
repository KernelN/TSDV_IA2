using System.Collections.Generic;
using System.Threading.Tasks;
using IA.Pathfinding.Grid;
using UnityEngine;
using Random = UnityEngine.Random;

namespace IA.Pathfinding.Voronoi
{
    [System.Serializable]
    public class PointOfInterest
    {
        public Transform t;
        public Vector2Int gridPos;
        public int id;
    }
    
    [System.Serializable]
    public class VoronoiAStarPathfinder : AStar.AStarPathfinder
    {
        //[Header("Runtime Values")]
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
        [Header("DEBUG")]
        [SerializeField, Range(0.001f,1)] float nodeHeight = 0.5f;
        [SerializeField] Color[] possibleRegionColors;
        Dictionary<int, Color> colorsByRegion;

        /// <summary>
        /// Stores the index of the node in the grid,
        /// the Id of the owner POI
        /// and the distance to the POI
        /// </summary>
        struct RegionNode
        {
            public int nodeIndex, poiId, distance;

            public RegionNode(int index, int poiId, int distance)
            {
                nodeIndex = index;
                this.poiId = poiId;
                this.distance = distance;
            }
        }
        /// <summary>
        /// Binary Tree which sorts nodes from smallest to biggest
        /// </summary>
        class MinPriorityQueue
        {
            //Don't use a real queue because it wouldn't let us sort it
            List<RegionNode> queue = new List<RegionNode>();

            public int Count => queue.Count;

            /// <summary>
            /// Insert node at the end of the queue, then Sort with BubbleUp
            /// </summary>
            public void Enqueue(int nodeIndex, int poiId, int distance)
            {
                queue.Add(new RegionNode(nodeIndex, poiId, distance));
                BubbleUp(queue.Count - 1);
            }
            
            /// <summary>
            /// Extract node from the beginning of the queue (the smallest one),
            /// move the last node to the beginning of the queue (the biggest one)
            /// and then sort it down until everything is sorted again
            /// </summary>
            public RegionNode Dequeue()
            {
                //Extract root / smallest node
                RegionNode root = queue[0];
                
                //Move the last node to the beginning of the queue
                int lastIndex = queue.Count - 1;
                queue[0] = queue[lastIndex];

                queue.RemoveAt(lastIndex); //Clean duplicate
                
                //Sort / sink the value down the tree
                if (queue.Count > 0)
                    BubbleDown(0);

                //Return the light node
                return root;
            }

            /// <summary>
            /// Check and sort through all of the parents of the node
            /// until it hits with a node of smaller distance
            /// (parents with a bigger value than the current node get swapped)
            /// </summary>
            /// <param name="index">index of the node in the list</param>
            void BubbleUp(int index)
            {
                //https://youtu.be/PkCBI4BKeb8?si=0-ndqONcZYKuuC8y BubbleUp/Down
                while (index > 0)
                {
                    //BinaryTrees nodes, by convention, are sorted breath-first in the collection
                    //regardless of how the algorithm later explores them (in the BinaryTree)
                    //see: https://youtube.com/shorts/4NYk5vW_5yc?si=f7Euvxi3VGL3utwJ
                    //for a graphical example
                    int parent = (index - 1) / 2;
                    
                    //If parent is smaller, end the sort
                    if (queue[parent].distance <= queue[index].distance) 
                        break;

                    //Swap the nodes value and continue sorting 
                    (queue[parent], queue[index]) = (queue[index], queue[parent]);
                    
                    //As the values where sorted,
                    //the index of the inserted node is now the index of its old parent
                    index = parent;
                }
            }

            /// <summary>
            /// Check and sort through the smallest childs of the node (down their branches)
            /// until it hits with a node of bigger distance / value
            /// </summary>
            /// <param name="index"></param>
            void BubbleDown(int index)
            {
                //https://youtu.be/PkCBI4BKeb8?si=0-ndqONcZYKuuC8y BubbleUp/Down
                int last = queue.Count - 1;
                while (true)
                {
                    int left = index * 2 + 1; //inverted find parent formula
                    if (left > last)
                        return;

                    int right = left + 1; //(as it goes from L > R, the right child is left + 1
                    
                    //Get the index of the smallest child
                    int smallest = left;
                    if (right <= last && queue[right].distance < queue[left].distance)
                        smallest = right;

                    //If the smallest child is smaller than the current node, we good
                    if (queue[index].distance <= queue[smallest].distance)
                        return;

                    //If it's bigger than the current node (it should usually be), keep sorting
                    (queue[index], queue[smallest]) = (queue[smallest], queue[index]);
                    index = smallest;
                }
            }
        }

        //Set Methods
        public override void Set(PathGrid grid)
        {
            base.Set(grid);

            currentPOIs = new List<PointOfInterest>();
            pointsByPos = new Dictionary<Vector2Int, PointOfInterest>();
            pointsById = new Dictionary<int, PointOfInterest>();
            
            regionsByNode = new Dictionary<Vector2Int, int>();
            nearestCostByNode = new Dictionary<Vector2Int, int>();
            regionsCostByNode = new Dictionary<Vector2Int, Dictionary<int, float>>();

            //Set positions of interest
            for (int i = 0; i < pointsOfInterest.Count; i++)
            {
                if (pointsOfInterest[i].t == null)
                {
                    Debug.LogError("Point of interest " + i + " has no transform");
                    continue;
                }
                
                if (pointsOfInterest[i].id == 0)
                    pointsOfInterest[i].id = pointsOfInterest[i].t.GetInstanceID();

                pointsOfInterest[i].gridPos = grid.GetGridPosition(pointsOfInterest[i].t.position);
                pointsById.Add(pointsOfInterest[i].id, pointsOfInterest[i]);
                pointsByPos.Add(pointsOfInterest[i].gridPos, pointsOfInterest[i]);
            }
            
            currentPOIs.AddRange(pointsOfInterest);
            
            CalculateVoronoi();
        }
        public void Load(PathGrid grid, Dictionary<Vector2Int, Dictionary<int, float>> regionsCostByNode)
        {
            base.Set(grid);

            currentPOIs = new List<PointOfInterest>();
            pointsByPos = new Dictionary<Vector2Int, PointOfInterest>();
            pointsById = new Dictionary<int, PointOfInterest>();
            
            regionsByNode = new Dictionary<Vector2Int, int>();
            nearestCostByNode = new Dictionary<Vector2Int, int>();
            
            if(regionsCostByNode == null)
                this.regionsCostByNode = new Dictionary<Vector2Int, Dictionary<int, float>>();
            else
                this.regionsCostByNode = regionsCostByNode;

            //Set positions of interest
            for (int i = 0; i < pointsOfInterest.Count; i++)
            {
                if (pointsOfInterest[i].t == null)
                {
                    Debug.LogError("Point of interest " + i + " has no transform");
                    continue;
                }
                
                if (pointsOfInterest[i].id == 0)
                    pointsOfInterest[i].id = pointsOfInterest[i].t.GetInstanceID();

                pointsOfInterest[i].gridPos = grid.GetGridPosition(pointsOfInterest[i].t.position);
                pointsById.Add(pointsOfInterest[i].id, pointsOfInterest[i]);
                pointsByPos.Add(pointsOfInterest[i].gridPos, pointsOfInterest[i]);
            }
            
            currentPOIs.AddRange(pointsOfInterest);
            
            if(regionsCostByNode == null)
                CalculateVoronoi();
            else
            {
                UpdateVoronoi();
            }
            
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
            if(regionsByNode == null) return;
            if (currentPOIs.Count <= 0) return;
            if (possibleRegionColors.Length <= 0) return;

            if (colorsByRegion == null)
            {
                SetGizmoColors();
            }
            
            //Draw regions
            Vector3 nodeSize = new Vector3(grid.NodeDiameter, nodeHeight, grid.NodeDiameter);
            for (int x = 0; x < grid.gridSize.x; x++)
            {
                for (int y = 0; y < grid.gridSize.y; y++)
                {
                    int region = regionsByNode[new Vector2Int(x,y)];
                    
                    colorsByRegion.TryGetValue(region, out Color regionColor);
                    Gizmos.color = regionColor;
                    
                    Gizmos.DrawCube(grid.grid[x, y].worldPos, nodeSize);
                }
            }
        }
        
        //Public Methods
        public int FindPointRegion(Vector3 point)
        {
            Vector2Int gridPos = grid.GetGridPosition(point);
            return FindPointRegion(gridPos);
        }
        public int FindPointRegion(Vector2Int gridPos)
        {
            int index = GetFlatNodeIndex(gridPos);
            if (index >= 0 && regionsByNodeIndex != null)
                return regionsByNodeIndex[index];
            
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

            if (!IsValidGridPosition(gridPos))
                return -1;

            // Keep fallback local: only check direct neighbours around the original world-point cell.
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
        public PathNode GetPositionOfInterest(int region)
        {
            if (currentPOIs.Count <= 0) return null;

            PointOfInterest poi;
            pointsById.TryGetValue(region, out poi);

            if (poi == null) return null;
            
            return grid.grid[poi.gridPos.x, poi.gridPos.y];
        }
        public List<PathNode> FindPathToPOI(Vector3 startPos)
        {
            int region = FindSafePointRegion(startPos);
            
            return FindPathToPOI(startPos, region);
        }
        public List<PathNode> FindPathToPOI(Vector3 startPos, int region)
        {
            PointOfInterest poi;
            pointsById.TryGetValue(region, out poi);
            
            if(poi == null) return null;
            
            //Necessary for multithreading
            PathNode startNode = grid.NodeFromWorldPoint(startPos);
            PathNode endNode = grid.grid[poi.gridPos.x, poi.gridPos.y];
            
            return FindPath(startNode, endNode);
        }
        public void RemovePointOfInterest(Vector2Int gridPos)
        {
            int region;

            if(!regionsByNode.TryGetValue(gridPos, out region)) return;

            RemovePointOfInterest(region);
        }
        public void RemovePointOfInterest(int region)
        {
            PointOfInterest poi;
            pointsById.TryGetValue(region, out poi);
            
            if(poi == null) return;
            
            pointsById.Remove(region);
            currentPOIs.Remove(poi);
            
            UpdateVoronoi();
        }
        public void UpdatePointsOfInterest(List<int> pointsID)
        {
            bool needsToBeUpdated = false;

            if (pointsID.Count == currentPOIs.Count)
            {
                //Check if positions are the same (even if in different order)
                for (int i = 0; i < pointsID.Count; i++)
                {
                    if (!pointsID.Contains(currentPOIs[i].id))
                    {
                        needsToBeUpdated = true;
                        break;
                    }
                }
            }
            else 
                needsToBeUpdated = true;
            
            if(!needsToBeUpdated) return;
            
            currentPOIs.Clear();
            
            //Get new points of interest
            for (int i = 0; i < pointsID.Count; i++)
            {
                PointOfInterest poi;
                if(pointsById.TryGetValue(pointsID[i], out poi))
                {
                    currentPOIs.Add(poi);
                }
            }
            
            UpdateVoronoi();
            
            //THIS NEED TO BE UPDATED
            // Dictionary<int, PointOfInterest> pointsById;
            // Dictionary<Vector2Int, int> regionsByNode;
            
            
            #if UNITY_EDITOR //UPDATE GIZMO COLORS
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
        
        //Private Methods
        void CalculateVoronoi()
        {
            if (pointsOfInterest.Count <= 0) return;

            regionsCostByNode.Clear();

            BuildNearestRegionMap(currentPOIs);
        }
        void UpdateVoronoi()
        {
            //Update calls are typically POI-filter changes.
            //Rebuild nearest labels via multi-source Dijkstra
            BuildNearestRegionMap(currentPOIs);
        }
        void BuildNearestRegionMap(List<PointOfInterest> sources)
        {
            //Initialize nearest node lists
            int nodeCount = grid.gridSize.x * grid.gridSize.y;
            EnsureNearestNodeBuffers(nodeCount);

            //If there are no POIs, clean up dictionaries 
            if (sources == null || sources.Count <= 0)
            {
                SyncNearestRegionDictionary();
                return;
            }

            MinPriorityQueue queue = new MinPriorityQueue();

            // Seed all active POIs into one global queue.
            for (int i = 0; i < sources.Count; i++)
            {
                PointOfInterest source = sources[i];
                if (source == null)
                    continue;

                if (!IsValidGridPosition(source.gridPos))
                    continue;

                PathNode sourceNode = grid.grid[source.gridPos.x, source.gridPos.y];
                if (!sourceNode.walkable)
                    continue;

                //POI ids are stable mine identities and are intentionally decoupled from per-grid node indices. 
                queue.Enqueue(GetFlatNodeIndex(source.gridPos), source.id, 0);
            }

            while (queue.Count > 0)
            {
                RegionNode current = queue.Dequeue();

                //If there's already a better target for this node, skip
                int bestDistance = nearestCostByNodeIndex[current.nodeIndex];
                if (current.distance > bestDistance)
                    continue;

                if (current.distance == bestDistance)
                {
                    int currentBestPoi = regionsByNodeIndex[current.nodeIndex];
                    if (currentBestPoi != -1 && current.poiId >= currentBestPoi)
                        continue;
                }

                nearestCostByNodeIndex[current.nodeIndex] = current.distance;
                regionsByNodeIndex[current.nodeIndex] = current.poiId;

                //Store all node neighbours in the queue
                Vector2Int currentPos = GetGridPositionFromIndex(current.nodeIndex);
                PathNode currentNode = grid.grid[currentPos.x, currentPos.y];
                for (int i = 0; i < currentNode.neighbours.Count; i++)
                {
                    PathNode neighbour = currentNode.neighbours[i]; //get neighbor
                    
                    if (!neighbour.walkable) continue; //check if walkable

                    //Calculate cost
                    int edgeCost = GetMovementCost(currentNode, neighbour);
                    int nextDistance = current.distance + edgeCost;
                    
                    //Get neighbour grid id
                    int neighbourIndex = GetFlatNodeIndex(neighbour.gridPos);

                    //If it has a better distance to another POI, skip
                    if (nextDistance > nearestCostByNodeIndex[neighbourIndex])
                        continue;

                    //If it doesn't, add to queue
                    queue.Enqueue(neighbourIndex, current.poiId, nextDistance);
                }
            }

            SyncNearestRegionDictionary();
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
                Vector2Int gridPos = GetGridPositionFromIndex(nodeIndex);
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
        void EnsureNearestNodeBuffers(int nodeCount)
        {
            if (regionsByNodeIndex == null || regionsByNodeIndex.Length != nodeCount)
                regionsByNodeIndex = new int[nodeCount];

            if (nearestCostByNodeIndex == null || nearestCostByNodeIndex.Length != nodeCount)
                nearestCostByNodeIndex = new int[nodeCount];

            for (int i = 0; i < nodeCount; i++)
            {
                regionsByNodeIndex[i] = -1;
                nearestCostByNodeIndex[i] = int.MaxValue;
            }
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
                Vector2Int pos = GetGridPositionFromIndex(nodeIndex);
                regionsByNode[pos] = regionsByNodeIndex[nodeIndex];
                nearestCostByNode[pos] = nearestCostByNodeIndex[nodeIndex];
            }
        }
        bool IsValidGridPosition(Vector2Int pos)
        {
            return pos.x >= 0 && pos.x < grid.gridSize.x && pos.y >= 0 && pos.y < grid.gridSize.y;
        }
        int GetFlatNodeIndex(Vector2Int gridPos)
        {
            if (!IsValidGridPosition(gridPos))
                return -1;

            return gridPos.x * grid.gridSize.y + gridPos.y;
        }
        Vector2Int GetGridPositionFromIndex(int nodeIndex)
        {
            int height = grid.gridSize.y;
            int x = nodeIndex / height;
            int y = nodeIndex % height;
            return new Vector2Int(x, y);
        }
        int GetMovementCost(PathNode from, PathNode to)
        {
            int dst = Universal.FileManaging.Vec2Int.GetSqrDistance(from.gridPos, to.gridPos);
            int travelCost = dst == 2 ? 14 : 10; //if diagonal, sqr distance will always be 2
            return travelCost + to.weight;
        }
        float GetCost(PathNode start, PathNode end)
        {
            if (!TryFindPathCost(start, end, out int totalCost))
                return -1;

            return totalCost;
        }
        
        //DEBUG
        void SetGizmoColors()
        {
            if(colorsByRegion == null)
                colorsByRegion = new Dictionary<int, Color>();
            else 
                colorsByRegion.Clear();

            //If enough colors for each region, use one for each or less
            if (currentPOIs.Count <= possibleRegionColors.Length)
            {
                for (int i = 0; i < currentPOIs.Count; i++)
                {
                    colorsByRegion.TryAdd(currentPOIs[i].id, possibleRegionColors[i]);
                }
            }
                
            //If not enough colors, use random for the rest
            else
            {
                for (int i = 0; i < possibleRegionColors.Length; i++)
                {
                    colorsByRegion.TryAdd(currentPOIs[i].id, possibleRegionColors[i]);
                }

                for (int i = possibleRegionColors.Length; i < currentPOIs.Count; i++)
                {
                    int rIndex = Random.Range(0, possibleRegionColors.Length);
                    colorsByRegion.TryAdd(currentPOIs[i].id, possibleRegionColors[rIndex]);
                }
            }
        }
    }
}

