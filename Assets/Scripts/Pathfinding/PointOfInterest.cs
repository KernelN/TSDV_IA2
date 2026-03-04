using UnityEngine;

namespace IA.Pathfinding.Voronoi
{
    [System.Serializable]
    public class PointOfInterest
    {
        public Transform t;
        public Vector2Int gridPos;
        public int id;
    }
}
