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
        Vec2 lastDir;
        
        //Fitness Values
        int moved;
        int survivedEnemy;
        int helpedAlly;
        int gotCloserToFood;
        int gotAwayFromFood;
        int movedStraight;
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
            helpedAlly++;
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
            fitness += 15 * gotCloserToFood;
            if(movedStraight > 0)
                fitness *= UnityEngine.Mathf.Pow(.97f, movedStraight);
            
            fitness += foodCount * 200;
            if(gotAwayFromFood > 0)
                fitness *= UnityEngine.Mathf.Pow(.95f, gotAwayFromFood);
            
            fitness += survivedEnemy * 2;
            fitness += helpedAlly * 10;
            
            // if(foodsLost > 0)
            //     fitness *= UnityEngine.Mathf.Pow(.9f, foodsLost);

            // if(foodCount > 0)
            //     fitness *= UnityEngine.Mathf.Pow(1.1f, foodCount);

            //Reward agents on reproducing, but not on being greedy
            if (foodCount >= 2)
            {
                if (foodCount < 6)
                    fitness *= 5f;
                else
                    fitness = UnityEngine.Mathf.Pow(0.9f, foodCount);
            }

            if(fitness < System.Single.Epsilon) fitness = System.Single.Epsilon;
            genome.fitness = fitness;
        }
        public bool CanAdvanceGen()
        {
            if (generation >= 3) return false; //THIS SHIT DOESN'T RESTART

            return willSurvive;
        }
        public void AdvanceGen()
        {
            if (generation >= 3)
            {
                Die();
                return;
            }
            
            generation++;
            OnReset();
        }
        public void ForceEat(float mod = 1) => OnEat(mod);

        public void UnEat()
        {
            foodCount--;
            
            if(foodCount < 2)
                canReproduce = false;

            if (foodCount == 0)
                willSurvive = false;
        }
        public void ReturnToLastPos() => position = prevPos;
        public void Die() => Died?.Invoke(this);

        //Protected Methods
        protected void Move(Vec2 dir)
        {
            prevPos = position;
            Vec2 newPos = position + dir;
            
            //Punish MOVING in a straight line
            if(dir.IntSqrMagnitude() > 0 && dir == lastDir)
                movedStraight++;
            lastDir = dir;
            
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
            if(cardinals > 0.775f)
                return new Vec2(0, -up);
            if(cardinals > 0.55f)
                return new Vec2(0, up);
            if(cardinals > 0.325f)
                return new Vec2(-1, 0);
            if(cardinals > 0.1f)
                return new Vec2(1, 0);
            
            return new Vec2(0, 0);
        }
        protected float GetCardinal(Vec2 v)
        {
            //Get absolute value of vector
            float ax = v.x > System.Single.Epsilon ? v.x : -v.x;
            float ay = v.y > System.Single.Epsilon ? v.y : -v.y;

            //Check biggest direction
            if (ax >= ay)
            {
                if (ax < System.Single.Epsilon) return 0f;      
                return v.x > 0f ? 0.2f : 0.4f; //if og x is positive 0.2f, else 0.4f
            }

            return v.y * up > 0f ? 0.6f : 0.8f; //if og y is positive 0.6f, else 0.8f
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
            lastDir = new Vec2(0,0);
            
            moved = 0;
            survivedEnemy = 0;
            gotAwayFromFood = 0;
            gotCloserToFood = 0;
            movedStraight = 0;
            foodID = -1;
            helpedAlly = 0;
            foodCount = 0;
        }
        protected virtual void OnThink()
        {
            inputs.Clear();

            // inputs.Add(position.y == maxPos.y ? 1 : 0);
            // inputs.Add(position.y == minPos.y ? 1 : 0);
            
            Vec2 dist = nearFoodPos - position;
            dist.y *= up;
            Vec2 dir = dist.IntNormalized();
            inputs.Add(dir.x);
            inputs.Add(dir.y);
            inputs.Add(dir.x);
            inputs.Add(dir.y);
            //inputs.Add(GetCardinal(dir));
            
            int distMag = dist.IntSqrMagnitude();
            inputs.Add(distMag);
            
            inputs.Add(willSurvive ? 1 : 0);
            inputs.Add(canReproduce ? 1 : 0);
            inputs.Add(canReproduce ? 1 : 0);

            if (distMag < lastDistToFood)
                gotCloserToFood++;
            else
                gotAwayFromFood++;
            lastDistToFood = distMag;

            if (stage >= Stage.Enemies)
            {
                inputs.Add((nearEnemy.position - position).IntSqrMagnitude() < 5 ? 1 : 0);
                //inputs.Add(0);
            }
            else
            {
                inputs.Add(0);
            }
            if (stage >= Stage.Allies)
            {
                inputs.Add((nearAlly.position - position).IntSqrMagnitude() < 5 ? 1 : 0);
                //inputs.Add(0);
            }
            else
            {
                inputs.Add(0);
            }

            float[] outputs = brain.Synapsis(inputs.ToArray());
            
            Vec2 moveDir = GetDir(outputs);
            Move(moveDir);

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
        public virtual void OnSurvivedEnemyEncounter()
        {
            survivedEnemy++;
        }
        public virtual void OnGaveFoodToALly()
        {
            //Only reward it if it can reproduce
            if(canReproduce)
                helpedAlly++;
        }
    }
}