using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BirdBase : MonoBehaviour
{
    public enum State
    {
        Alive,
        Dead
    }

    public State state
    {
        get; private set;
    }

    protected Genome genome;
    protected NeuralNetwork brain;
    protected BirdBehaviour birdBehaviour;

    private void Awake()
    {
        birdBehaviour = GetComponent<BirdBehaviour>();
    }

    public void SetBrain(Genome genome, NeuralNetwork brain)
    {
        this.genome = genome;
        this.brain = brain;
        state = State.Alive;
        birdBehaviour.Reset();
        OnReset();
    }

    public void Flap()
    {
        if (state == State.Alive)
            birdBehaviour.Flap();
    }

    public void Think(float dt)
    {
        if (state == State.Alive)
        {
            Obstacle obstacle = ObstacleManager.Instance.GetNextObstacle(this.transform.position);

            if (obstacle == null)
            {
                Debug.Log("No Obstacle");
                return;
            }

            OnThink(dt, birdBehaviour, obstacle);

            birdBehaviour.UpdateBird(dt);

            if (this.transform.position.y > 5f || this.transform.position.y < -5f || ObstacleManager.Instance.IsColliding(this.transform.position))
            {
                OnDead();
                state = State.Dead;
            }
        }
    }

    protected virtual void OnDead()
    {

    }

    protected virtual void OnThink(float dt, BirdBehaviour birdBehaviour, Obstacle obstacle)
    {

    }

    protected virtual void OnReset()
    {

    }

}
