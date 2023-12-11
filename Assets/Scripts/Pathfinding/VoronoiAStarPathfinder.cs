using System.Collections.Generic;
using System.Threading.Tasks;
using IA.Pathfinding.Grid;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace IA.Pathfinding.Voronoi
{
    [System.Serializable]
    public class PointOfInterest
    {
        [Header("Set Values")]
        public Transform t;
        [Header("Runtime Values")]
        public Vector2Int gridPos;
        public int id;
    }
    
    [System.Serializable]
    public class VoronoiAStarPathfinder : AStar.AStarPathfinder
    {
        [Header("Set Values")]
        public List<PointOfInterest> pointsOfInterest;
        [SerializeField] bool addAllPOIAtStart = true;
        //[Header("Runtime Values")]
        List<PointOfInterest> currentPOIs;
        Dictionary<int, PointOfInterest> pointsById;
        Dictionary<Vector2Int, PointOfInterest> pointsByPos;
        Dictionary<Vector2Int, int> regionsByNode;
        Dictionary<Vector2Int, Dictionary<int, float>> regionsCostByNode;
        [Header("DEBUG")]
        [SerializeField, Range(0.001f,1)] float nodeHeight = 0.5f;
        [SerializeField] Color[] possibleRegionColors;
        Dictionary<int, Color> colorsByRegion;

        //Unity Methods
        public override void Set(PathGrid grid)
        {
            base.Set(grid);

            currentPOIs = new List<PointOfInterest>();
            pointsByPos = new Dictionary<Vector2Int, PointOfInterest>();
            pointsById = new Dictionary<int, PointOfInterest>();
            
            regionsByNode = new Dictionary<Vector2Int, int>();
            regionsCostByNode = new Dictionary<Vector2Int, Dictionary<int, float>>();

            //Set positions of interest
            for (int i = 0; i < pointsOfInterest.Count; i++)
            {
                if (pointsOfInterest[i].t == null)
                {
                    Debug.LogError("Point of interest " + i + " has no transform");
                    continue;
                }
                
                pointsOfInterest[i].id = pointsOfInterest[i].t.GetInstanceID();
                pointsOfInterest[i].gridPos = grid.GetGridPosition(pointsOfInterest[i].t.position);
                pointsById.Add(pointsOfInterest[i].id, pointsOfInterest[i]);
                pointsByPos.Add(pointsOfInterest[i].gridPos, pointsOfInterest[i]);
            }
            
            if(addAllPOIAtStart)
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
                
                pointsOfInterest[i].id = pointsOfInterest[i].t.GetInstanceID();
                pointsOfInterest[i].gridPos = grid.GetGridPosition(pointsOfInterest[i].t.position);
                pointsById.Add(pointsOfInterest[i].id, pointsOfInterest[i]);
                pointsByPos.Add(pointsOfInterest[i].gridPos, pointsOfInterest[i]);
            }
            
            if(addAllPOIAtStart)
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
            if (regionsByNode.TryGetValue(gridPos, out int region))
                return region;
            
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
            int region = FindPointRegion(startPos);
            
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
                        needsToBeUpdated = true;
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
            Dictionary<Vector2Int, Dictionary<int, float>> copy;
            copy = regionsCostByNode;
            return copy;
        }
        
        //Private Methods
        void CalculateVoronoi()
        {
            if (pointsOfInterest.Count <= 0) return;

            regionsCostByNode = new Dictionary<Vector2Int, Dictionary<int, float>>();
            
            //Calculate cost of each grid point to each point of interest
            for (int x = 0; x < grid.gridSize.x; x++)
            {
                for (int y = 0; y < grid.gridSize.y; y++)
                {
                    Vector2Int gridPos = new Vector2Int(x, y);

                    //Create costs dictionary and add to grid dictionary
                    Dictionary<int, float> newCosts = new Dictionary<int, float>();
                    regionsCostByNode.Add(gridPos, newCosts);

                    PathNode node = grid.grid[x, y];
                    PathNode target = grid.grid[pointsOfInterest[0].gridPos.x, pointsOfInterest[0].gridPos.y];
                    float cost = GetCost(node, target);
                    newCosts.Add(pointsOfInterest[0].id, cost);

                    for (int i = 1; i < pointsOfInterest.Count; i++)
                    {
                        target = grid.grid[pointsOfInterest[i].gridPos.x, pointsOfInterest[i].gridPos.y];
                        cost = GetCost(node, target);

                        newCosts.TryAdd(pointsOfInterest[i].id, cost);
                    }
                }
            }
            
            //Assign each node a region
            UpdateVoronoi();
        }
        void UpdateVoronoi()
        {
            if(regionsByNode == null)
                regionsByNode = new Dictionary<Vector2Int, int>();
            else
                regionsByNode.Clear();
            
            if (currentPOIs.Count <= 0) return;
            
            //Calculate current region of each grid point
            Parallel.For(0, grid.gridSize.x, x =>
            { Parallel.For(0, grid.gridSize.y, y =>
              { Vector2Int gridPos = new Vector2Int(x, y);
                int cheapestPoint = currentPOIs[0].id;

                if (!regionsCostByNode.TryGetValue(gridPos, out Dictionary<int, float> costs))
                    return; //this return exits the Parallel.For y, not the UpdateVoronoi method

                int regionID = currentPOIs[0].id;
                costs.TryGetValue(regionID, out float cost);

                // costs.Remove(regionID);
                // costs.Add(regionID, cost);

                float smallestCost = cost;

                for (int i = 1; i < currentPOIs.Count; i++)
                {
                    regionID = currentPOIs[i].id;
                    costs.TryGetValue(regionID, out cost);

                    if (cost < smallestCost)
                    {
                        smallestCost = cost;
                        cheapestPoint = regionID;
                    }

                    // costs.Remove(regionID);
                    // costs.Add(regionID, cost);
                }

                lock (regionsByNode)
                {
                    regionsByNode.Remove(gridPos);
                    regionsByNode.Add(gridPos, cheapestPoint);
                } 
              }); 
            });
        }
        float GetCost(PathNode start, PathNode end)
        {
            List<PathNode> path = FindPath(start, end);

            if (path == null) return -1;
            
            return path[^1].FCost;
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