using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IA.Pathfinding.Grid
{
    [System.Serializable]
    public class PathNode
    {
        // Static/authoring map data only.
        // Search bookkeeping is kept in AStarPathfinder.NodeRecord so PathNode stays immutable during queries.
        public bool walkable;
        public Universal.FileManaging.Vec3 worldPos;
        public Universal.FileManaging.Vec2Int gridPos;
        public int weight;

        public List<PathNode> neighbours = new List<PathNode>();
        
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
