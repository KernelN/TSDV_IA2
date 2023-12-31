
using System;
using UnityEngine;

namespace IA.Pathfinding
{
    public class PathManager : MonoBehaviour
    {
        [Header("Set Values")] 
        [SerializeField] Transform gridTransform;
        [SerializeField] Vector2Int gridWorldSize;
        [SerializeField] Grid.PathGrid[] grids;
        [SerializeField] Voronoi.VoronoiAStarPathfinder[] pathfinders;
        [Header("DEBUG")]
        [SerializeField, Min(0)] int gridGizmo;
        [SerializeField, Min(0)] int pathfinderGizmo;
        
        void Awake()
        {
            for (int i = 0; i < grids.Length; i++)
            {
                grids[i].Set(gridTransform, gridWorldSize);
            }

            for (int i = 0; i < pathfinders.Length; i++)
            {
                pathfinders[i].Set(grids[i]);
            }
        }
        void OnDrawGizmos()
        {
            if(grids.Length > 0)
                grids[gridGizmo].DrawGizmos(gridTransform, gridWorldSize);
            
            if(pathfinders.Length > 0)
                pathfinders[pathfinderGizmo].DrawGizmos();
        }

        public Voronoi.VoronoiAStarPathfinder GetPathfinder(int index)
        {
            return pathfinders[index];
        }
        public float GetNodeDiameter(int index)
        {
            return grids[index].NodeDiameter;
        }
        public Vector2Int GetGridPos(Vector3 worldPos, int index)
        {
            return grids[index].NodeFromWorldPoint(worldPos).gridPos;
        }
        public void RemovePointOfInterest(Vector2Int worldPos)
        {
            for (int i = 0; i < pathfinders.Length; i++)
            {
                pathfinders[i].RemovePointOfInterest(worldPos);
            }
        }
    }
}