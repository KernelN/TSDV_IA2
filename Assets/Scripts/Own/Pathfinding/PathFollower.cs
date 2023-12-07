using System.Collections.Generic;
using UnityEngine;

namespace IA.Pathfinding.Voronoi
{
    public class PathFollower : MonoBehaviour
    {
        [Header("Set Values")]
        [SerializeField] PathManager pathManager;
        [SerializeField] int pathfinderIndex;
        //[Header("Runtime Values")]
        VoronoiAStarPathfinder pathfinder;       
        List<Grid.PathNode> path;
        [Header("DEBUG")]
        [SerializeField] float pathFindInterval;
        [SerializeField] float pathFindTimer;
        [SerializeField] bool drawGizmos;
        [SerializeField] float nodeSize;

        //Unity Events
        void Start()
        {
            pathFindTimer = pathFindInterval;
            pathfinder = pathManager.GetPathfinder(pathfinderIndex);
        } 
        void Update()
        {
            if (pathFindTimer > 0)
            {
                pathFindTimer -= Time.deltaTime;
                return;
            }

            pathFindTimer = pathFindInterval;
            path = pathfinder.FindPathToPOI(transform.position);
        }
        void OnDrawGizmos()
        {
            if(!drawGizmos) return;
            if (pathfinder == null) return;
            if(path == null) return;

            Gizmos.color = Color.green;
            for (int i = 1; i < path.Count - 1; i++)
            {
                Gizmos.DrawSphere(path[i].worldPos, nodeSize);
            }
        }
    }
}