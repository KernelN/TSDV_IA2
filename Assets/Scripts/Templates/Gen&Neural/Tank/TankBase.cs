using UnityEngine;
using System.Collections;

public class TankBase : MonoBehaviour
{
    public enum Stages
    {
        GoodMines,
        BadMines,
        Tanks,
        Trees,
        
        _count
    }
    
    public float Speed = 10.0f;
    public float RotSpeed = 20.0f;

    protected Genome genome;
	protected NeuralNetwork brain;
    protected GameObject nearTank;
    protected GameObject nearTree;
    protected GameObject nearMine;
    protected GameObject goodMine;
    protected GameObject badMine;
    protected float[] inputs;
    protected Stages stage;

    public void SetBrain(Genome genome, NeuralNetwork brain)
    {
        this.genome = genome;
        this.brain = brain;
        inputs = new float[brain.InputsCount];
        OnReset();
    }

    public void SetStage(int stage)
    {
        if(stage >= (int)Stages._count)
            stage = (int)Stages._count - 1;
            
        this.stage = (Stages)stage;
    }
    
    public void SetNearestMine(GameObject mine)
    {
        nearMine = mine;
    }

    public void SetGoodNearestMine(GameObject mine)
    {
        goodMine = mine;
    }

    public void SetBadNearestMine(GameObject mine)
    {
        badMine = mine;
    }
    
    public void SetNearestTank(GameObject tank)
    {
        nearMine = tank;
    }
    
    public void SetNearestTree(GameObject tree)
    {
        nearMine = tree;
    }

    protected bool IsGoodMine(GameObject mine)
    {
        return goodMine == mine;
    }

    protected Vector3 GetDirToMine(GameObject mine)
    {
        return (mine.transform.position - this.transform.position).normalized;
    }
    protected float GetAngleToObj(GameObject obj)
    {
        return Vector3.SignedAngle(transform.forward, GetDirToMine(obj), Vector3.up);
    }
    protected float GetSqrDistToObj(GameObject obj)
    {
        return (obj.transform.position - transform.position).sqrMagnitude;
    }
    
    protected bool IsCloseToMine(GameObject mine)
    {
        return (this.transform.position - nearMine.transform.position).sqrMagnitude <= 2.0f;
    }

    protected void SetForces(float leftForce, float rightForce, float dt)
    {
        Vector3 pos = this.transform.position;
        float rotFactor = Mathf.Clamp((rightForce - leftForce), -1.0f, 1.0f);
        this.transform.rotation *= Quaternion.AngleAxis(rotFactor * RotSpeed * dt, Vector3.up);
        pos += this.transform.forward * (Mathf.Abs(rightForce + leftForce) * 0.5f * Speed * dt);
        this.transform.position = pos;
    }

	public void Think(float dt) 
	{
        OnThink(dt);

        if(IsCloseToMine(nearMine))
        {
            OnTakeMine(nearMine);
            PopulationManager.Instance.RelocateMine(nearMine);
        }
	}

    protected virtual void OnThink(float dt)
    {

    }

    protected virtual void OnTakeMine(GameObject mine)
    {
    }

    protected virtual void OnReset()
    {

    }
}
