using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BirdAI : BirdBase
{
    protected override void OnThink(float dt, BirdBehaviour birdBehaviour, Obstacle obstacle)
    {
        float[] inputs = new float[4];
        inputs[0] = (obstacle.transform.position - birdBehaviour.transform.position).x / 10.0f;
        inputs[1] = (obstacle.transform.position - birdBehaviour.transform.position).y / 10.0f;
        inputs[2] = (obstacle.transform.position - birdBehaviour.transform.position).y / 10.0f;
        inputs[3] = (obstacle.transform.position - birdBehaviour.transform.position).y / 10.0f;

        float[] outputs;
        outputs = brain.Synapsis(inputs);
        if (outputs[0] < 0.5f)
        {
            birdBehaviour.Flap();
        }

        Vector3 obstaclePos = obstacle.transform.position;
        Vector3 pos = birdBehaviour.transform.position;

        if (Vector3.Distance(obstacle.transform.position, birdBehaviour.transform.position) <= 1.0f)
        {
            genome.fitness *= 2;
        }

        genome.fitness += 50 - Mathf.Abs(obstaclePos.y - pos.y);

        genome.fitness += 100.0f - Vector3.Distance(obstaclePos, pos);
    }

    protected override void OnDead()
    {
    }

    protected override void OnReset()
    {
        genome.fitness = 0.0f;
    }
}
