using System.Collections.Generic;
using IA.Math;
using Stage = IA.Population.Stage;

namespace IA.Agent
{
    [System.Serializable]
    public class AgentBase
    {
        public Vec2 position;// { get; protected set; }
        public int up;
        
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

        public bool willSurvive { get; protected set; }
        public bool canReproduce { get; protected set; }
        
        protected GeneAlgo.Genome genome;
        protected NeuralNet.NeuralNetwork brain;
        protected List<float> inputs;

        public System.Action FoodTaken;

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
        public void SetNearFoodPos(Vec2 nearFoodPos)
        {
            this.nearFoodPos = nearFoodPos;
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
        public bool CanAdvanceGen()
        {
            generation++;
            if (generation > 3) return false;

            return willSurvive;
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
        protected Vec2 GetDir(float[] cardinals)
        {
            if (cardinals[4] > cardinals[0]
                && cardinals[4] > cardinals[1]
                && cardinals[4] > cardinals[2]
                && cardinals[4] > cardinals[3])
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
        protected void DecreaseFitness(float decrease)
        {
            fitness -= decrease;
            if (fitness < 0)
                fitness = 0;
        }
        
        //Virtual / Abstract Methods
        protected virtual void OnReset()
        {
            fitness = 1;
        }
        protected virtual void OnThink()
        {
            inputs.Clear();
            
            Vec2 dist = nearFoodPos - position;
            inputs.Add(dist.x);
            inputs.Add(dist.y);
            
            int distMag = dist.SqrMagnitude();
            
            if(lastDistToFood > distMag)
                fitness += .05f;
            // else
            //     fitness *= .9f;
            genome.fitness = fitness;

            lastDistToFood = distMag;
            
            if (stage >= Stage.Enemies)
            {
                dist = nearEnemy.position - position;
                inputs.Add(dist.x);
                inputs.Add(dist.y);
            }
            else
            {
                inputs.Add(0);
                inputs.Add(0);
            }

            if (stage >= Stage.Allies)
            {
                dist = nearAlly.position - position;
                inputs.Add(dist.x);
                inputs.Add(dist.y);
            }
            else
            {
                inputs.Add(0);
                inputs.Add(0);
            }

            float[] outputs = brain.Synapsis(inputs.ToArray());
            
            Move(GetDir(outputs));
        }
        protected virtual void OnEat()
        {
            if (!willSurvive)
                willSurvive = true;
            else
                canReproduce = true;
            
            lastDistToFood = int.MaxValue;
            
            //fitness += 5;
            fitness *= 2;
            genome.fitness = fitness;
                
            FoodTaken?.Invoke();
        }
    }
}