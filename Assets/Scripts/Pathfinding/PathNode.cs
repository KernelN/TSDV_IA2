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
        public int weight;

        /// <summary> Distance from start </summary>
        public int gCost; 
        /// <summary> Distance to target </summary>
        public int hCost;
        
        public PathNode parent;
        public List<PathNode> neighbours = new List<PathNode>();
        
        /// <summary> Distance to target </summary>
        public int FCost { get { return gCost + hCost; } } 
        
        public PathNode(){ neighbours = new List<PathNode>(); }
        public PathNode(bool _walkable, Vector3 _worldPos, Vector2Int _gridPos, int _weight = 0)
        {
            walkable = _walkable;
            worldPos = _worldPos;
            gridPos = _gridPos;
            weight = _weight;
        }
        public PathNode(PathNode node)
        {
            walkable = node.walkable;
            worldPos = node.worldPos;
            gridPos = node.gridPos;
        }
    }
}