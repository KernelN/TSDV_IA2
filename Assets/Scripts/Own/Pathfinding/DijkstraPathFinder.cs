using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IA.Pathfinding
{
    [System.Serializable]
    public struct Position
    {
        public int x;
        public int y;
        
        //Constructor
        public Position(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
        
        //Methods
        public int DistanceTo(Position other)
        {
            return Mathf.Abs(x - other.x) + Mathf.Abs(y - other.y);
        }
        
        //Operators
        public static bool operator ==(Position a, Position b)
        {
            return a.x == b.x && a.y == b.y;
        }
        public static bool operator !=(Position a, Position b)
        {
            return a.x != b.x || a.y != b.y;
        }
    }
    
    public enum Direction
    {
        Up,
        Right,
        Down,
        Left
    }
    
    public enum NodeState
    {
        Closed,
        Open,
        Visited,
        Blocked
    }
}

namespace IA.Pathfinding.Dijkstra
{
    [System.Serializable]
    public class DijkstraPathFinder
    {
        //STEP BY STEP:
        //
        
        class PathNode{}
        
        class Node
        {
            public Position pos;
            public int dist;
            public NodeState state = NodeState.Closed;
            public Node openedBy = null;
            public Node[] neighbours = new Node[4];
            
            //Constructors
            public Node(Position pos, int dist)
            {
                this.pos = pos;
                this.dist = dist;
            }
            public Node(Position pos, Position target)
            {
                this.pos = pos;
                dist = pos.DistanceTo(target);
            }
            
            //Methods
            public void SetNeighbours(Node[][] map)
            {
                //If neighbour pos is inside map, set neighbour
                if (pos.x + 1 < map.Length)
                    neighbours[(int)Direction.Right] = map[pos.x + 1][pos.y];
                if (pos.x - 1 >= 0)
                    neighbours[(int)Direction.Left] = map[pos.x - 1][pos.y];
                if (pos.y + 1 < map[pos.x].Length)
                    neighbours[(int)Direction.Up] = map[pos.x][pos.y + 1];
                if (pos.y - 1 >= 0)
                    neighbours[(int)Direction.Down] = map[pos.x][pos.y - 1];
            }
            public bool Open(Node opener)
            {
                if (state == NodeState.Blocked) return false;
                if(state == NodeState.Open) return false; //is this okey?
                
                state = NodeState.Open;
                openedBy = opener;
                return true;
            }
            public void Close()
            {
                state = NodeState.Closed;
                openedBy = null;
            }
        }
        
        Position origin;
        Position target;
        Node[][] nodeMap;
        List<PathNode> path;
        List<PathNode> shortestNodes;

        public void Set(Position origin, Position target, Position[][] map)
        {
            this.origin = origin;
            this.target = target;
            
            nodeMap = new Node[map.Length][];
            
            //Create node map
            for (int i = 0; i < map.Length; i++)
            {
                nodeMap[i] = new Node[map[i].Length];
                for (int j = 0; j < map[i].Length; j++)
                {
                    nodeMap[i][j] = new Node(map[i][j], target);
                }
            }
            
            //Set neighbours
            for (int i = 0; i < nodeMap.Length; i++)
            {
                for (int j = 0; j < nodeMap[i].Length; j++)
                {
                    nodeMap[i][j].SetNeighbours(nodeMap);
                }
            }
        }
        public Position[] GetPath()
        {
            // //If pathfinder is not setted, return null
            // if(nodeMap == null || (nodeMap.Length == 0 && nodeMap[0].Length == 0))
            //     return null;
            //
            // path = new List<PathNode>();
            //
            // //Add source path node
            // path.Add(new PathNode(nodeMap[origin.x][origin.y], null));
            //
            // while (path[^1].node.pos != target)
            // {
            //     int[] dists = new int[4];
            //     
            //     //Get dist of each position (if neighbour null or visited, set dist to max)
            //     for (int i = 0; i < dists.Length; i++)
            //     {
            //         if (path[^1].node.neighbours[i] == null)
            //             dists[i] = int.MaxValue;
            //
            //         if(path[^1].node.neighbours[i].state != NodeState.Unvisited)
            //             dists[i] = int.MaxValue;
            //
            //         dists[i] = path[^1].node.neighbours[i].dist;
            //     }
            //
            //     Direction shorterstDir;
            //     Node shortestNode;
            //     int shortestDist = int.MaxValue;
            //     
            //     //Get shortest dist of neighbours
            //     for (int i = 0; i < dists.Length; i++)
            //     {
            //         if (dists[i] > shortestDist) continue;
            //         
            //         if (dists[i] < shortestDist)
            //         {
            //             shorterstDir = (Direction)i;
            //             shortestDist = dists[i];
            //         }
            //     }
            //     
            //     if(shortestNodes == null)
            //         shortestNodes = new List<PathNode>();
            //     
            //     //If there are no shortest nodes, add next node
            //     if(shortestNodes.Count == 0)
            //         shortestNodes.Add(path[^1].node);
            //     
            //     //If next node is at the same dist than the shortest nodes, add it
            //     else if(shortestNodes[0].node.dist == shortestDist)
            //         shortestNodes.Add(path[^1].node);
            //     
            //     //If next node is closer to target than the shortest nodes, replace shortest node by next node
            //     else if(shortestNodes[0].node.dist < shortestDist)
            //     {
            //         shortestNodes.Clear();
            //         shortestNodes.Add(path[^1].node);
            //     }
            //     
            //     //If next node is farther to target than the shortest nodes, SEARCH PATH
            //     else
            //     {
            //         shortestNodes.Clear();
            //         shortestNodes.Add(path[^1].node);
            //     }
            // }
            //
            return null;
        }
    }
}