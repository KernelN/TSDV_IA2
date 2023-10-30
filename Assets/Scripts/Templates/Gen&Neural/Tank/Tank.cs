using UnityEngine;

public class Tank : TankBase
{
    float fitness = 0;
    protected override void OnReset()
    {
        fitness = 1;
    }

    protected override void OnThink(float dt)
    {
        float angleToMine = GetAngleToObj(goodMine);

        inputs[0] = angleToMine;
        
        fitness += ((180 - Mathf.Abs(angleToMine)) / 180) * .001f * dt;
        
        inputs[1] = GetSqrDistToObj(goodMine);
        
        inputs[2] = GetAngleToObj(badMine);
        
        float distToBadMine = GetSqrDistToObj(badMine);
        
        inputs[3] = distToBadMine;
        inputs[4] = distToBadMine;
        
        // if(stage >= Stages.BadMines)
        // {
        //     fitness -= distToBadMine * .0001f * dt;
        // }
        
        inputs[5] = GetSqrDistToObj(nearTank);

        if(stage >= Stages.Tanks)
            if (IsCloseToMine(nearTank))
            {
                fitness -= 30f;
                if(fitness < 0) fitness = 0.00001f;
                genome.fitness = fitness;
            }
        
        inputs[6] = GetSqrDistToObj(nearTree);
        
        
        if(stage >= Stages.Trees)
            if (IsCloseToMine(nearTank))
            {
                fitness -= 30f;
                if(fitness < 0) fitness = 0.00001f;
                genome.fitness = fitness;
            }

        float[] output = brain.Synapsis(inputs);

        SetForces(output[0], output[1], dt);
    }

    protected override void OnTakeMine(GameObject mine)
    {
        if (IsGoodMine(mine))
        {
            fitness *= 2f;
            genome.fitness = fitness;
        }
        else if(stage >= Stages.BadMines)
        {
            fitness -= 10f;
            if(fitness < 0) fitness = 0.00001f;
            genome.fitness = fitness;
        }
    }
}
