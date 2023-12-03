using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IA.Pathfinding.Grid
{
    public class PathGrid : MonoBehaviour
    {
        [Header("Set Values")]
        public Transform player;
        public LayerMask unwalkableMask;
        public Vector2 gridWorldSize;
        public float nodeRadius;
        //[Header("Runtime Values")]
        PathNode[,] grid;
        Vector2Int gridSize;
        [Header("DEBUG")]
        public List<PathNode> path;

        float NodeDiameter { get { return nodeRadius * 2; } }
        
        //Unity Events
        void Start()
        {
            gridSize = new Vector2Int();
            gridSize.x = Mathf.RoundToInt(gridWorldSize.x / NodeDiameter);
            gridSize.y = Mathf.RoundToInt(gridWorldSize.y / NodeDiameter);
            
            CreateGrid();
        }
        void OnDrawGizmos()
        {
            //Draw grid
            Vector3 worldSize = new Vector3(gridWorldSize.x, 1, gridWorldSize.y);
            Gizmos.DrawWireCube(transform.position, worldSize);
            
            //Draw nodes
            if (grid == null) return;
            
            float nodeSize = NodeDiameter*.9f;
            Vector3 nodeWorldSize = new Vector3(nodeSize, transform.localScale.y*1.1f, nodeSize);
            PathNode playerNode = NodeFromWorldPoint(player.position);
            for (int x = 0; x < gridSize.x; x++)
            {
                for (int y = 0; y < gridSize.y; y++)
                {
                    PathNode node = grid[x, y];
                    
                    if(node == playerNode)
                        Gizmos.color = Color.cyan;
                    else if (path != null && path.Contains(node))
                        Gizmos.color = Color.green;
                    else
                        Gizmos.color = (node.walkable) ? Color.white : Color.red;
                    
                    Gizmos.DrawCube(node.worldPos, nodeWorldSize);
                }
            }
        }
        
        //Methods
        public PathNode NodeFromWorldPoint(Vector3 worldPos)
        {
            //pos + half size gives pos as if center was botLeft, / gridsize gives pos in grid in percentage
            float percentX = (worldPos.x + gridWorldSize.x / 2) / gridWorldSize.x;
            float percentY = (worldPos.z + gridWorldSize.y / 2) / gridWorldSize.y;
            
            //Clamp to make sure we dont go out of bounds
            percentX = Mathf.Clamp01(percentX);
            percentY = Mathf.Clamp01(percentY);
            
            //Get grid pos and round to int (to get index)
            int x = Mathf.RoundToInt((gridSize.x - 1) * percentX);
            int y = Mathf.RoundToInt((gridSize.y - 1) * percentY);
            
            return grid[x, y];
        }
        void CreateGrid()
        {
            grid = new PathNode[gridSize.x, gridSize.y];
            Vector3 worldBotLeft = transform.position;
            worldBotLeft -= Vector3.right * gridWorldSize.x / 2;
            worldBotLeft -= Vector3.forward * gridWorldSize.y / 2;

            //Create nodes
            for (int i = 0; i < gridSize.x; i++)
            {
                for (int j = 0; j < gridSize.y; j++)
                {
                    Vector3 worldPoint = worldBotLeft; //start at bottom left
                    
                    //add number of nodes in x and add half of current one
                    worldPoint += Vector3.right * (i * NodeDiameter + nodeRadius);
                    
                    //add number of nodes in y and add half of current one
                    worldPoint += Vector3.forward * (j * NodeDiameter + nodeRadius);
                    
                    //Check if node is walkable
                    bool walkable = !(Physics.CheckSphere(worldPoint, nodeRadius, unwalkableMask));
                    
                    //Create node
                    grid[i, j] = new PathNode(walkable, worldPoint, new Vector2Int(i, j));
                }
            }
            
            //Set neighbours
            for (int x = 0; x < gridSize.x; x++)
            {
                for (int y = 0; y < gridSize.y; y++)
                {
                    grid[x, y].neighbours = GetNeighbours(grid[x, y]);
                }
            }
        }
        List<PathNode> GetNeighbours(PathNode node)
        {
            List<PathNode> neighbours = new List<PathNode>();
            
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;
                    
                    int checkX = node.gridPos.x + x;
                    if(checkX < 0 || checkX >= gridSize.x)
                        continue;

                    int checkY = node.gridPos.y + y;
                    if(checkY < 0 || checkY >= gridSize.y)
                        continue;

                    neighbours.Add(grid[checkX, checkY]);
                }
            }
            
            return neighbours;
        }
    }
}