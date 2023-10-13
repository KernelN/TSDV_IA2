using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace IA.Flocking
{
    public class Boid : MonoBehaviour
    {
        FlockingManager fM;
        [Header("General")]
        public float speed = 2.5f;
        public float turnSpeed = 5f;
        public Vector2 currentPosition;
        public CircleCollider2D circleCollider2D;
        [Header("Distances")]
        public float alignmentDist;
        public float cohesionDist;
        public float separationDist;
        public float obstacleDist;
        [Header("Weights")]
        public float alignmentMod = 1;
        public float cohesionMod = 1;
        public float separationMod = 1;
        public float obstacleMod = 1;

        private void Start()
        {
            fM = FlockingManager.instance;
        }

        private void Update()
        {
            transform.position += transform.up * (speed * Time.deltaTime);
            currentPosition = transform.position;
            transform.up = Vector3.Lerp(transform.up, ACS(), turnSpeed * Time.deltaTime);
        }

        public Vector2 ACS()
        {
            Vector2 ACS =
                fM.Alignment(this) * alignmentMod
                + fM.Cohesion(this) * cohesionMod
                + fM.Separation(this) * separationMod
                + fM.Obstacle(this) * obstacleMod
                + fM.Direction(this, fM.flockPoint);

            ACS.Normalize();

            return ACS;
        }
    }
}