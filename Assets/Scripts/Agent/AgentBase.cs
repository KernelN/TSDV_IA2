using System.Collections.Generic;
using IA.Math;
using Stage = IA.Population.Stage;

namespace IA.Agent
{
    [System.Serializable]
    public class AgentBase
    {
        public Vec2 position { get; protected set; }
        public int up;
        public bool isTeam1;
        public int foodID = -1;
        
        protected Vec2 nearFoodPos;
        protected AgentBase nearAlly;
        protected AgentBase nearEnemy;
        protected Vec2 maxPos;
        protected Vec2 minPos;
        protected Vec2 prevPos;
        protected Stage stage;
        protected int generation;
        protected float fitness;
        int lastDistToFood;
        
        //Fitness Values
        int moved;
        int stayedStill;
        int gotCloserToFood;
        int foodsLost;
        float foodCount;

        public bool willSurvive { get; protected set; }
        public bool canReproduce { get; protected set; }
        public bool willFleeAgainstEnemy { get; protected set; }
        public bool willGiveFoodToAlly { get; protected set; }
        
        protected GeneAlgo.Genome genome;
        protected NeuralNet.NeuralNetwork brain;
        protected List<float> inputs;

        public System.Action FoodTaken;
        public System.Action<AgentBase> Died;

        //Constructor
        public AgentBase(GeneAlgo.Genome genome, NeuralNet.NeuralNetwork brain)
        {
            SetBrain(genome, brain);
            this.genome = genome;
            this.brain = brain;
        }
        
        //Public Methods
        public void Think()
        {
            OnThink();

            if (IsOnPos(nearFoodPos))
                OnEat();
        }
        public void SetNearFoodPos(Vec2 nearFoodPos, int index)
        {
            if(foodID == index) return;
            
            this.nearFoodPos = nearFoodPos;

            if (foodID < 0)
            {
                foodID = index;
                return;
            }

            foodID = index;
            foodsLost++;
        }
        public void SetNearAlly(AgentBase nearAlly)
        {
            this.nearAlly = nearAlly;
        }
        public void SetNearEnemy(AgentBase nearEnemy)
        {
            this.nearEnemy = nearEnemy;
        }
        public void SetMinAndMax(Vec2 minPos, Vec2 maxPos)
        {
            this.minPos = minPos;
            this.maxPos = maxPos;
        }
        public void SetStage(Stage stage)
        {
            this.stage = stage;
        }
        public void SetBrain(GeneAlgo.Genome genome, NeuralNet.NeuralNetwork brain)
        {
            this.genome = genome;
            this.brain = brain;
            inputs = new List<float>();
            OnReset();
        }
        public void SetPosition(Vec2 pos)
        {
            //Reset previous position
            prevPos = pos;

            // Set tank position
            position = pos;
        }
        public void CalcFitness()
        {
            fitness += .15f * gotCloserToFood;
            //fitness -= .005f * gotAwayFromFood;
            
            if(stayedStill > 0)
                fitness *= UnityEngine.Mathf.Pow(.65f, stayedStill);
            
            fitness += foodCount * 5;
            
            // if(foodsLost > 0)
            //     fitness *= UnityEngine.Mathf.Pow(.9f, foodsLost);

            if(foodCount > 0)
                fitness *= UnityEngine.Mathf.Pow(1.1f, foodCount);
            if(foodCount >= 3)
                fitness *= 2;

            if(fitness <= 0) fitness = System.Single.Epsilon;
            genome.fitness = fitness;
        }
        public bool CanAdvanceGen()
        {
            if (generation >= 3) return false;

            return willSurvive;
        }
        public void AdvanceGen()
        {
            generation++;
            OnReset();
        }
        public void ForceEat(float mod = 1)
        {
            OnEat();
        }
        public void UnEat()
        {
            foodCount--;
            
            if(foodCount < 2)
                canReproduce = false;

            if (foodCount == 0)
                willSurvive = false;
        }
        public void ReturnToLastPos()
        {
            position = prevPos;
        }
        public void Die()
        {
            Died?.Invoke(this);
        }

        //Protected Methods
        protected void Move(Vec2 dir)
        {
            prevPos = position;
            Vec2 newPos = position + dir;
            
            //If out of horizontal bounds, loop
            if (newPos.x < minPos.x) newPos.x = maxPos.x;
            else if (newPos.x > maxPos.x) newPos.x = minPos.x;
            
            //If out of vertical bounds, clamp
            if (newPos.y < minPos.y) newPos.y = minPos.y;
            else if (newPos.y > maxPos.y) newPos.y = maxPos.y;
            
            position = newPos;
        }
        protected bool IsOnPos(Vec2 pos)
        {
            return position.x - pos.x == 0 && position.y - pos.y == 0;
        }
        protected Vec2 GetDir(float cardinals)
        {
            if(cardinals > 0.1f)
                return new Vec2(1, 0);
            if(cardinals > 0.325f)
                return new Vec2(-1, 0);
            if(cardinals > 0.55f)
                return new Vec2(0, up);
            if(cardinals > 0.775f)
                return new Vec2(0, -up);
            
            return new Vec2(0, 0);
        }
        protected Vec2 GetDir(float[] cardinals)
        {
            const float stopThreshold = 0.15f;
            if (cardinals[0] < stopThreshold
                && cardinals[1] < stopThreshold
                && cardinals[2] < stopThreshold
                && cardinals[3] < stopThreshold)
            {
                return new Vec2(0, 0);
            }
            
            bool up = cardinals[0] > cardinals[1];
            bool right = cardinals[2] > cardinals[3];
            
            //Up or right
            //Up or left
            //Down or right
            //Down or left
            
            Vec2 dir = new Vec2();
            if(up && right)
            {
                if(cardinals[0] > cardinals[2])
                    dir.y = this.up;
                else
                    dir.x = 1;
            }
            else if(up && !right)
            {
                if(cardinals[0] > cardinals[3])
                    dir.y = this.up;
                else
                    dir.x = -1;
            }
            else if(!up && right)
            {
                if(cardinals[1] > cardinals[2])
                    dir.y = -this.up;
                else
                    dir.x = 1;
            }
            else
            {
                if(cardinals[1] > cardinals[3])
                    dir.y = -this.up;
                else
                    dir.x = -1;
            }
            
            return dir;
        }
        
        //Virtual / Abstract Methods
        protected virtual void OnReset()
        {
            fitness = 1;
            
            willSurvive = false;
            canReproduce = false;

            lastDistToFood = int.MaxValue;
            moved = 0;
            stayedStill = 0;
            //gotAwayFromFood = 0;
            gotCloserToFood = 0;
            foodID = -1;
            foodsLost = 0;
            foodCount = 0;
        }
        protected virtual void OnThink()
        {
            inputs.Clear();

            inputs.Add(position.y == maxPos.y ? 1 : 0);
            inputs.Add(position.y == minPos.y ? 1 : 0);
            
            Vec2 dist = nearFoodPos - position;
            Vec2 dir = dist.IntNormalized();
            inputs.Add(dir.x);
            inputs.Add(dir.y);
            
            int distMag = dist.SqrMagnitude();

            if (distMag < lastDistToFood)
            {
                lastDistToFood = distMag;
                gotCloserToFood++;
            }

            //You only need to know if you've eaten enough when deciding if you should stay or you should go
            if (stage >= Stage.Enemies)
            {
                inputs.Add(willSurvive ? 1 : 0);
                inputs.Add(canReproduce ? 1 : 0);
            }
            else
            {
                inputs.Add(0);
                inputs.Add(0);
            }

            float[] outputs = brain.Synapsis(inputs.ToArray());
            
            Vec2 moveDir = GetDir(outputs);
            Move(moveDir);
            
            if(position == prevPos)
                stayedStill++;

            willFleeAgainstEnemy = outputs[4] > 0.5f;
            willGiveFoodToAlly = outputs[5] > 0.5f;
        }
        protected virtual void OnEat(float foodEaten = 1)
        {
            //If couldn't survive, now it will, and if already could survive, now can reproduce
            if (!willSurvive)
                willSurvive = true;
            else
                canReproduce = true;

            foodCount += foodEaten;
            lastDistToFood = int.MaxValue;
            foodID = -1;
                
            FoodTaken?.Invoke();
        }
    }
}