using System.Collections.Generic;
using IA.Pathfinding.Grid;
using UnityEngine;

namespace IA.Pathfinding.AStar
{
    /// <summary>
    /// Based on Lague's A* Pathfinding:
    /// https://youtube.com/playlist?list=PLFt_AvWsXl0cq5Umv3pMC9SPnKjfp9eGW&amp;si=OmsMlMnHXmTXOmU1
    /// https://github.com/SebLague/Pathfinding
    ///
    /// Thread-safety guarantee:
    /// Path search state (costs, parent link, open/closed membership) is stored per-call in NodeRecord
    /// dictionaries keyed by grid position. PathNode instances are treated as read-only map data by the
    /// search itself, allowing concurrent FindPath calls that share the same grid topology.
    /// </summary>
    [System.Serializable]
    public class AStarPathfinder
    {
        protected struct NodeRecord
        {
            public int gCost;
            public int hCost;
            public int parentIndex;
            public bool isOpen;
            public bool isClosed;

            public int FCost => gCost + hCost;
        }

        //[Header("Set Values")]
        //[Header("Runtime Values")]
        internal PathGrid grid;

        //Unity Methods
        public virtual void Set(PathGrid grid)
        {
            this.grid = grid;
        }

        //Methods
        public List<PathNode> FindPath(Vector3 startPos, Vector3 targetPos)
        {
            //If path is from A to A, return list with A node
            if(startPos == targetPos)
                return new List<PathNode>{grid.NodeFromWorldPoint(startPos)};

            PathNode startNode = grid.NodeFromWorldPoint(startPos);
            PathNode targetNode = grid.NodeFromWorldPoint(targetPos);

            return FindPath(startNode, targetNode);
        }

        public List<PathNode> FindPath(PathNode startNode, PathNode targetNode)
        {
            if(startNode == targetNode)
                return new List<PathNode>{startNode};

            if (!TrySearch(startNode, targetNode, out Dictionary<Vector2Int, NodeRecord> records, out _))
                return null;

            return RetracePath(startNode, targetNode, records);
        }

        public bool TryFindPathCost(PathNode startNode, PathNode targetNode, out int totalCost)
        {
            if(startNode == targetNode)
            {
                totalCost = 0;
                return true;
            }

            return TrySearch(startNode, targetNode, out _, out totalCost);
        }

        bool TrySearch(PathNode startNode, PathNode targetNode, out Dictionary<Vector2Int, NodeRecord> records, out int totalCost)
        {
            records = new Dictionary<Vector2Int, NodeRecord>();
            totalCost = -1;

            var openList = new List<PathNode>();

            NodeRecord startRecord = CreateDefaultRecord();
            startRecord.gCost = 0;
            startRecord.hCost = GetDistance(startNode, targetNode);
            startRecord.isOpen = true;
            startRecord.parentIndex = NodeToIndex(startNode.gridPos);
            records[startNode.gridPos] = startRecord;
            openList.Add(startNode);

            while (openList.Count > 0)
            {
                PathNode currentNode = openList[0];
                NodeRecord currentRecord = records[currentNode.gridPos];

                for (int i = 1; i < openList.Count; i++)
                {
                    PathNode candidateNode = openList[i];
                    NodeRecord candidateRecord = records[candidateNode.gridPos];
                    bool hasLowerFCost = candidateRecord.FCost < currentRecord.FCost;
                    bool hasLowerHCost = candidateRecord.FCost == currentRecord.FCost &&
                                         candidateRecord.hCost < currentRecord.hCost;
                    if (hasLowerFCost || hasLowerHCost)
                    {
                        currentNode = candidateNode;
                        currentRecord = candidateRecord;
                    }
                }

                openList.Remove(currentNode);
                currentRecord.isOpen = false;
                currentRecord.isClosed = true;
                records[currentNode.gridPos] = currentRecord;

                if (currentNode == targetNode)
                {
                    totalCost = currentRecord.gCost;
                    return true;
                }

                for (int i = 0; i < currentNode.neighbours.Count; i++)
                {
                    PathNode neighbour = currentNode.neighbours[i];
                    if (!neighbour.walkable) continue;

                    NodeRecord neighbourRecord = GetOrCreateRecord(records, neighbour.gridPos);
                    if (neighbourRecord.isClosed) continue;

                    int moveCost = currentRecord.gCost + GetDistance(currentNode, neighbour) + neighbour.weight;
                    if (moveCost < neighbourRecord.gCost || !neighbourRecord.isOpen)
                    {
                        neighbourRecord.gCost = moveCost;
                        neighbourRecord.hCost = GetDistance(neighbour, targetNode);
                        neighbourRecord.parentIndex = NodeToIndex(currentNode.gridPos);

                        if (!neighbourRecord.isOpen)
                        {
                            neighbourRecord.isOpen = true;
                            openList.Add(neighbour);
                        }

                        records[neighbour.gridPos] = neighbourRecord;
                    }
                }
            }

            return false;
        }

        List<PathNode> RetracePath(PathNode startNode, PathNode endNode, Dictionary<Vector2Int, NodeRecord> records)
        {
            var path = new List<PathNode>();
            PathNode currentNode = endNode;
            path.Add(currentNode);

            // Retrace until we get back to start via NodeRecord.parentIndex, not mutable PathNode fields.
            while (currentNode != startNode)
            {
                if (!records.TryGetValue(currentNode.gridPos, out NodeRecord currentRecord))
                    return null;

                Vector2Int parentGridPos = IndexToGridPos(currentRecord.parentIndex);
                if (parentGridPos == currentNode.gridPos)
                    break;

                currentNode = grid.grid[parentGridPos.x, parentGridPos.y];
                path.Add(currentNode);
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

        NodeRecord GetOrCreateRecord(Dictionary<Vector2Int, NodeRecord> records, Vector2Int gridPos)
        {
            if (records.TryGetValue(gridPos, out NodeRecord record))
                return record;

            record = CreateDefaultRecord();
            records[gridPos] = record;
            return record;
        }

        static NodeRecord CreateDefaultRecord()
        {
            return new NodeRecord
            {
                gCost = int.MaxValue,
                hCost = 0,
                parentIndex = -1,
                isOpen = false,
                isClosed = false
            };
        }

        int NodeToIndex(Vector2Int gridPos)
        {
            return gridPos.x * grid.gridSize.y + gridPos.y;
        }

        Vector2Int IndexToGridPos(int index)
        {
            int ySize = grid.gridSize.y;
            return new Vector2Int(index / ySize, index % ySize);
        }
    }
}
