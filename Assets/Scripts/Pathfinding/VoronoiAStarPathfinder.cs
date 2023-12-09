using System.Collections.Generic;
using IA.Pathfinding.Grid;
using UnityEngine;

namespace IA.Pathfinding.Voronoi
{
    [System.Serializable]
    public class VoronoiAStarPathfinder : AStar.AStarPathfinder
    {
        [Header("Set Values")]
        public List<Transform> pointsOfInterest;
        //[Header("Runtime Values")]
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

            regionsByNode = new Dictionary<Vector2Int, int>();
            regionsCostByNode = new Dictionary<Vector2Int, Dictionary<int, float>>();
            
            CalculateVoronoi();
        }
        public void DrawGizmos()
        {
            if(regionsByNode == null) return;
            if (pointsOfInterest.Count <= 0) return;
            if (possibleRegionColors.Length <= 0) return;

            if (colorsByRegion == null)
            {
                colorsByRegion = new Dictionary<int, Color>();

                //If enough colors for each region, use one for each or less
                if (pointsOfInterest.Count <= possibleRegionColors.Length)
                {
                    for (int i = 0; i < pointsOfInterest.Count; i++)
                    {
                        colorsByRegion.Add(i, possibleRegionColors[i]);
                    }
                }
                
                //If not enough colors, use random for the rest
                else
                {
                    for (int i = 0; i < possibleRegionColors.Length; i++)
                    {
                        colorsByRegion.Add(i, possibleRegionColors[i]);
                    }

                    for (int i = possibleRegionColors.Length; i < pointsOfInterest.Count; i++)
                    {
                        int rIndex = Random.Range(0, possibleRegionColors.Length);
                        colorsByRegion.Add(i, possibleRegionColors[rIndex]);
                    }
                }
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

            if (regionsByNode.TryGetValue(gridPos, out int region))
                return region;
            
            return 0;
        }
        public PathNode GetPositionOfInterest(int region)
        {
            if (pointsOfInterest.Count <= region) return null;
            
            return grid.NodeFromWorldPoint(pointsOfInterest[region].position);
        }
        public List<PathNode> FindPathToPOI(Vector3 startPos)
        {
            int region = FindPointRegion(startPos);
            
            return FindPathToPOI(startPos, region);
        }
        public List<PathNode> FindPathToPOI(Vector3 startPos, int region)
        {
            return FindPath(startPos, pointsOfInterest[region].position);
        }
        public void RemovePointOfInterest(Vector2Int gridPos)
        {
            int region;

            if(!regionsByNode.TryGetValue(gridPos, out region)) return;

            pointsOfInterest.RemoveAt(region);
            
            CalculateVoronoi(true);
        }
        
        //Private Methods
        void CalculateVoronoi(bool recalculate = false)
        {
            if (pointsOfInterest.Count <= 0) return;
            
            for (int x = 0; x < grid.gridSize.x; x++)
            {
                for (int y = 0; y < grid.gridSize.y; y++)
                {
                    Vector2Int gridPos = new Vector2Int(x, y);
                    int cheapestPoint = 0;
                    
                    if (regionsCostByNode.TryGetValue(gridPos, out Dictionary<int, float> costs))
                    {
                        int regionID = pointsOfInterest[0].GetInstanceID();
                        costs.TryGetValue(regionID, out float cost);
                        if (recalculate)
                        {
                            costs.Remove(regionID);
                            costs.Add(regionID, cost);
                        }
                        
                        float smallestCost = cost;
                        
                        for (int i = 1; i < pointsOfInterest.Count; i++)
                        {
                            regionID = pointsOfInterest[i].GetInstanceID();
                            costs.TryGetValue(regionID, out cost);
                            
                            if (cost < smallestCost)
                            {
                                smallestCost = cost;
                                cheapestPoint = i;
                            }

                            if (recalculate)
                            {
                                costs.Remove(regionID);
                                costs.Add(regionID, cost);
                            }
                        }
                    }
                    else
                    {
                        Dictionary<int, float> newCosts = new Dictionary<int, float>();
                        regionsCostByNode.Add(gridPos, newCosts);

                        PathNode node = grid.grid[x, y];
                        PathNode target = grid.NodeFromWorldPoint(pointsOfInterest[0].position);
                        float cost = GetCost(node, target);
                        newCosts.Add(pointsOfInterest[0].GetInstanceID(), cost);

                        float smallestCost = cost;

                        for (int i = 1; i < pointsOfInterest.Count; i++)
                        {
                            target = grid.NodeFromWorldPoint(pointsOfInterest[i].position);
                            cost = GetCost(node, target);

                            if (cost < smallestCost)
                            {
                                smallestCost = cost;
                                cheapestPoint = i;
                            }
                            
                            newCosts.TryAdd(pointsOfInterest[i].GetInstanceID(), cost);
                        }
                    }

                    if (recalculate)
                    {
                        regionsByNode.Remove(gridPos);
                        regionsByNode.Add(gridPos, cheapestPoint);
                    }
                    else
                        regionsByNode.Add(gridPos, cheapestPoint);
                }
            }
        }
        float GetCost(PathNode start, PathNode end)
        {
            List<PathNode> path = FindPath(start, end);

            if (path == null) return -1;
            
            return path[^1].FCost;
        }
    }
}