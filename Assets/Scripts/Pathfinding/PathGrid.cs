using System.Collections.Generic;
using UnityEngine;

namespace IA.Pathfinding.Grid
{
    [System.Serializable]
    public class PathGrid
    {
        [Header("Set Values")]
        public LayerMask unwalkableMask;
        public float nodeRadius;
        public PathTerrain[] terrains;
        //[Header("Runtime Values")]
        Transform gridT;
        Vector2 gridWorldSize;
        LayerMask terrainsMask;
        Dictionary<int, int> terrainsDictionary;
        [Header("DEBUG")]
        [SerializeField] Transform player;
        [SerializeField, Min(1)] int maxNodeWeight = 100;
        [SerializeField, Range(0.001f, 1)] float nodeHeight = 0.5f;

        public PathNode[,] grid { get; private set; }
        public Vector2Int gridSize { get; private set; }
        public float NodeDiameter { get { return nodeRadius * 2; } }
        
        //Unity Methods
        public void Set(Transform gridTransform, Vector2 gridWorldSize)
        {
            gridT = gridTransform;
            this.gridWorldSize = gridWorldSize;
            
            Vector2Int gridSize = new Vector2Int();
            gridSize.x = Mathf.RoundToInt(gridWorldSize.x / NodeDiameter);
            gridSize.y = Mathf.RoundToInt(gridWorldSize.y / NodeDiameter);
            this.gridSize = gridSize;

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
        public void DrawGizmos(Transform gridT, Vector2Int gridWorldSize)
        {
            //Draw grid
            Vector3 worldSize = new Vector3(gridWorldSize.x, 1, gridWorldSize.y);
            Gizmos.DrawWireCube(gridT.position, worldSize);

            //If grid is not initialized, draw scheme of nodes
            if (grid == null)
            {
                Gizmos.color = Color.gray;
                Vector3 lineStart;
                Vector3 lineEnd;
                Vector3 worldBotLeft = gridT.position;
                worldBotLeft.y = gridT.localScale.y * .55f;
                worldBotLeft -= Vector3.right * gridWorldSize.x / 2;
                worldBotLeft -= Vector3.forward * gridWorldSize.y / 2;
                for (int x = 0; x <= gridWorldSize.x / (NodeDiameter); x++)
                {
                    lineStart = worldBotLeft;
                    lineEnd = worldBotLeft;

                    float xPos = x * NodeDiameter;
                    lineStart += new Vector3(xPos, 0, 0);
                    lineEnd += new Vector3(xPos, 0, gridWorldSize.y);

                    Gizmos.DrawLine(lineStart, lineEnd);
                }

                for (int y = 0; y <= gridWorldSize.y / (NodeDiameter); y++)
                {
                    lineStart = worldBotLeft;
                    lineEnd = worldBotLeft;

                    float yPos = y * NodeDiameter;
                    lineStart += new Vector3(0, 0, yPos);
                    lineEnd += new Vector3(gridWorldSize.x, 0, yPos);

                    Gizmos.DrawLine(lineStart, lineEnd);
                }

                return;
            }

            //Draw nodes
            float nodeSize = NodeDiameter * .9f;
            Vector3 nodeWorldSize = new Vector3(nodeSize, nodeHeight, nodeSize);
            PathNode playerNode = null;
            if (player)
                playerNode = NodeFromWorldPoint(player.position);
            
            for (int x = 0; x < gridSize.x; x++)
            {
                for (int y = 0; y < gridSize.y; y++)
                {
                    PathNode node = grid[x, y];

                    if (node == playerNode)
                        Gizmos.color = Color.cyan;
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
        public Vector2Int GetGridPosition(Vector3 worldPos)
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
            
            return new Vector2Int(x, y);
        }
        void CreateGrid()
        {
            grid = new PathNode[gridSize.x, gridSize.y];
            Vector3 worldBotLeft = gridT.position;
            worldBotLeft.y = gridT.localScale.y*.55f;
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