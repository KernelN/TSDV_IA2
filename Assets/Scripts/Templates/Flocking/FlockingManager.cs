using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IA.Flocking
{
    public class FlockingManager : MonoBehaviour
    {
        public enum CheckType { Alignment, Cohesion, Separation, Obstacle, _Count}
        
        public static FlockingManager instance;
        public GameObject flockPoint;
        public Transform[] flockObstacles;
        private Boid[] boids;

        private void Awake()
        {
            instance = this;
        }

        private void Start()
        {
            boids = FindObjectsOfType<Boid>();
        }

        public Vector2 Alignment(Boid boid)
        {
            List<Boid> insideRadiusBoids = GetInsideRadiusBoids(boid, CheckType.Alignment);
            Vector2 avg = Vector2.zero;
            foreach (Boid b in insideRadiusBoids)
                avg += (Vector2)b.transform.up.normalized;
            avg /= insideRadiusBoids.Count;
            avg.Normalize();
            return avg;
        }

        public Vector2 Cohesion(Boid boid)
        {
            List<Boid> insideRadiusBoids = GetInsideRadiusBoids(boid, CheckType.Cohesion);
            Vector2 avg = Vector2.zero;
            foreach (Boid b in insideRadiusBoids)
                avg += b.currentPosition;
            avg /= insideRadiusBoids.Count;
            return (avg - boid.currentPosition).normalized;
        }

        public Vector2 Separation(Boid boid)
        {
            List<Boid> insideRadiusBoids = GetInsideRadiusBoids(boid, CheckType.Separation);
            Vector2 avg = Vector2.zero;
            foreach (Boid b in insideRadiusBoids)
            {
                avg += (b.currentPosition - boid.currentPosition);
            }

            avg /= insideRadiusBoids.Count;
            avg *= -1;
            avg.Normalize();
            return avg;
        }
        
        public Vector2 Obstacle(Boid boid)
        {
            List<Vector2> insideRadiusBoids = GetInsideRadiusObstacles(boid);
            Vector2 avg = Vector2.zero;
            foreach (Vector2 b in insideRadiusBoids)
            {
                avg += (b - boid.currentPosition);
            }

            avg /= insideRadiusBoids.Count;
            avg *= -1;
            avg.Normalize();
            return avg;
        }

        public Vector2 Direction(Boid boid, GameObject target)
        {
            return ((Vector2)target.transform.position - boid.currentPosition).normalized;
        }

        public List<Boid> GetInsideRadiusBoids(Boid boid, CheckType checkType)
        {
            List<Boid> insideRadiusBoids = new List<Boid>();
            
            foreach (Boid b in boids)
                if (boid.circleCollider2D.OverlapPoint(b.currentPosition))
                {
                    float dist;
                    dist = Vector2.Distance(b.currentPosition, boid.currentPosition);

                    float maxDist = 0;
                    switch (checkType)
                    {
                        case CheckType.Alignment:
                            maxDist = boid.alignmentDist;
                            break;
                        case CheckType.Cohesion:
                            maxDist = boid.cohesionDist;
                            break;
                        case CheckType.Separation:
                            maxDist = boid.separationDist;
                            break;
                    }
                    
                    if(dist <= maxDist)
                        insideRadiusBoids.Add(b);
                }
            
            return insideRadiusBoids;
        }
        
        public List<Vector2> GetInsideRadiusObstacles(Boid boid)
        {
            List<Vector2> insideRadiusBoids = new List<Vector2>();
            
            foreach (Transform b in flockObstacles)
                if (boid.circleCollider2D.OverlapPoint(b.position))
                {
                    float dist;
                    dist = Vector2.Distance(b.position, boid.currentPosition);

                    if(dist <= boid.obstacleDist)
                        insideRadiusBoids.Add(b.position);
                }
            
            return insideRadiusBoids;
        }
    }
}