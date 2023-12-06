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
        public PathTerrain[] terrains;
        //[Header("Runtime Values")]
        PathNode[,] grid;
        Vector2Int gridSize;
        LayerMask terrainsMask;
        Dictionary<int, int> terrainsDictionary;
        [Header("DEBUG")]
        [SerializeField, Min(1)] int maxNodeWeight = 100;
        public List<PathNode> path;

        float NodeDiameter { get { return nodeRadius * 2; } }
        
        //Unity Events
        void Start()
        {
            gridSize = new Vector2Int();
            gridSize.x = Mathf.RoundToInt(gridWorldSize.x / NodeDiameter);
            gridSize.y = Mathf.RoundToInt(gridWorldSize.y / NodeDiameter);

            terrainsDictionary = new Dictionary<int, int>();
            for (int i = 0; i < terrains.Length; i++)
            {
                //Add all terrains layers to the mask
                terrainsMask |= terrains[i].mask;

                //Add all terrains to the dictionary
                int layerIndex = (int)Mathf.Log(terrains[i].mask.value, 2);
                terrainsDictionary.Add(layerIndex, terrains[i].weight); 
            }
            
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
                    else if (!node.walkable)
                        Gizmos.color = Color.red;
                    else
                        Gizmos.color = Color.Lerp(Color.white, Color.black, 
                                                       (float)node.weight / maxNodeWeight);
                    
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

            Ray terrainRay = new Ray(Vector3.zero, Vector3.down);
            
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

                    if (!walkable)
                    {
                        //Create node
                        grid[i, j] = new PathNode(false, worldPoint, new Vector2Int(i, j));
                        continue;
                    }

                    int mPenalty = 0;
                    const int rayHeightOffset = 50;
                    terrainRay.origin = worldPoint + Vector3.up * rayHeightOffset;
                    if (Physics.Raycast(terrainRay, out RaycastHit hit, 1000, terrainsMask))
                        terrainsDictionary.TryGetValue(hit.collider.gameObject.layer, out mPenalty);
                    
                    //Create node
                    grid[i, j] = new PathNode(walkable, worldPoint, new Vector2Int(i, j), mPenalty);
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