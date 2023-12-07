using System.Collections.Generic;
using IA.Pathfinding.Grid;
using UnityEngine;

namespace IA.Pathfinding.AStar
{
    /// <summary>
    /// Based on Lague's A* Pathfinding:
    /// https://youtube.com/playlist?list=PLFt_AvWsXl0cq5Umv3pMC9SPnKjfp9eGW&amp;si=OmsMlMnHXmTXOmU1
    /// https://github.com/SebLague/Pathfinding
    /// </summary>
    [System.Serializable]
    public class AStarPathfinder
    {
        //[Header("Set Values")]
        //[Header("Runtime Values")]
        internal PathGrid grid;
        // PathNode startNode;
        // PathNode targetNode;

        //Unity Methods
        public virtual void Set(PathGrid grid)
        {
            this.grid = grid;
        }
        
        //Methods
        public List<PathNode> FindPath(Vector3 startPos, Vector3 targetPos)
        {
            PathNode startNode = grid.NodeFromWorldPoint(startPos);
            PathNode targetNode = grid.NodeFromWorldPoint(targetPos);
         
            return FindPath(startNode, targetNode);
        }
        public List<PathNode> FindPath(PathNode startNode, PathNode targetNode)
        {
            List<PathNode> openList = new List<PathNode>();
            List<PathNode> closedList = new List<PathNode>();
            
            openList.Add(startNode);

            while (openList.Count > 0)
            {
                PathNode currentNode = openList[0];
                for (int i = 1; i < openList.Count; i++)
                {
                    bool hasLowerFCost = openList[i].FCost < currentNode.FCost;
                    bool hasLowerHCost = openList[i].FCost == currentNode.FCost &&
                                         openList[i].hCost < currentNode.hCost;
                    if (hasLowerFCost || hasLowerHCost)
                    {
                        currentNode = openList[i];
                    }
                }
                
                openList.Remove(currentNode);
                closedList.Add(currentNode);
                
                if (currentNode == targetNode)
                {
                    return RetracePath(startNode, targetNode);
                }

                for (int i = 0; i < currentNode.neighbours.Count; i++)
                { 
                    PathNode neighbour = currentNode.neighbours[i];
                    
                    if (!neighbour.walkable || closedList.Contains(neighbour)) continue;
                    
                    int moveCost = currentNode.gCost + GetDistance(currentNode, neighbour) + neighbour.weight;
                    if (moveCost < neighbour.gCost || !openList.Contains(neighbour))
                    {
                        neighbour.gCost = moveCost;
                        neighbour.hCost = GetDistance(neighbour, targetNode);
                        neighbour.parent = currentNode;

                        if (!openList.Contains(neighbour))
                        {
                            openList.Add(neighbour);
                        }
                    }
                }
            }
            
            return null;
        }
        List<PathNode> RetracePath(PathNode startNode, PathNode endNode)
        {
            List<PathNode> path = new List<PathNode>();
            path.Add(endNode);
            PathNode currentNode = endNode;
            
            //Retrace until we get back to start
            while (currentNode != startNode)
            {
                path.Add(currentNode.parent);
                currentNode = currentNode.parent;
            }
            
            path.Reverse();
            return path;
        }
        int GetDistance(PathNode nodeA, PathNode nodeB)
        {
            int dstX = Mathf.Abs(nodeA.gridPos.x - nodeB.gridPos.x);
            int dstY = Mathf.Abs(nodeA.gridPos.y - nodeB.gridPos.y);

            //The lesser distance will be made diagonally
            //Moving diagonally costs 14
            //Moving horizontally or vertically costs 10
            
            if (dstX > dstY)
            {
                //14 * Y (diagonals) + 10 * (quantity of _only_ horizontal moves)
                return 14 * dstY + 10 * (dstX - dstY);
            }
            
            //14 * X (diagonals) + 10 * (quantity of _only_ vertical moves)
            return 14 * dstX + 10 * (dstY - dstX);
        }
    }
}