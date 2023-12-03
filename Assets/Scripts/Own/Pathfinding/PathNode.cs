using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IA.Pathfinding.Grid
{
    public class PathNode
    {
        public bool walkable;
        public Vector3 worldPos;
        public Vector2Int gridPos;

        public int gCost; //Distance from start
        public int hCost; //Distance to target
        
        public PathNode parent;
        public List<PathNode> neighbours = new List<PathNode>();
        
        public int FCost { get { return gCost + hCost; } } //Total cost
        
        public PathNode(){ neighbours = new List<PathNode>(); }
        public PathNode(bool _walkable, Vector3 _worldPos, Vector2Int _gridPos)
        {
            walkable = _walkable;
            worldPos = _worldPos;
            gridPos = _gridPos;
        }
        public PathNode(PathNode node)
        {
            walkable = node.walkable;
            worldPos = node.worldPos;
            gridPos = node.gridPos;
        }
    }
}