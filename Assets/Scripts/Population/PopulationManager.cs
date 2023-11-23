using UnityEngine;
using System.Collections.Generic;
using IA.Agent;
using IA.GeneAlgo;
using IA.Math;
using IA.NeuralNet;

namespace IA.Population
{
    [System.Serializable]
    public class PopulationManager
    {
        [Header("Genetic")] 
        [Range(0,1)] public float MutationChance = 0.10f;
        [Range(0,1)] public float MutationRate = 0.01f;
        [Min(1)] public int FirstStageElitePairs;
        [Tooltip("This won't be for the traditional elites, but for the genomes that are stored in case of a restart")]
        [Min(.1f)] public float MinEliteFitness = 1;

        [Header("Neural Network")] 
        [Min(1)] public int InputsCount = 4;
        [Min(1)] public int OutputsCount = 2;
        [Range(0,10)] public int HiddenLayers = 1;
        [Range(0,100)] public int NeuronsCountPerHL = 7;
        [Tooltip("Multiplies each neuron (so the more neurons, bigger the effect).\n" +
                 "The bigger the value, more outputs will be beyond 0 or beyond 1 (then sigmoid to almost 0 or 1).\n" +
                 "(The smaller the value, more outputs will be closer to 0-1 then sigmoid to 0-1)")]
        [Range(0,-10)] public float Bias = -1f;
        [Tooltip("Divides(? each neuron (so the less neurons, bigger the effect).\n" +
                 "The bigger the value, more outputs will be between 0-1.\n" +
                 "(The smaller the value, more outputs will be almost 0 or almost 1)")]
        [Range(0.01f,1)] public float P = 0.5f;

        // [Header("Stages")] 
        // public float BadMinesAvgFitness = 100;
        // public float TanksAvgFitness = 200;
        // public float TreesAvgFitness = 300;

        public Stage Stage { get; protected set; } = Stage.Hunger;

        GeneticAlgorithm genAlg;
        Game.Map map;

        List<Genome> bestGenomes = new List<Genome>();
        
        List<Agent.AgentBase> populationControllers = new List<Agent.AgentBase>();
        List<Genome> population = new List<Genome>();
        List<NeuralNetwork> brains = new List<NeuralNetwork>();

        bool isTeam1;
        int initialPopCount;

        public int generation { get; private set; }
        public float bestFitness { get; private set; }
        public float avgFitness { get; private set; }
        public float worstFitness { get; private set; }

        private float getBestFitness()
        {
            float fitness = 0;
            Genome bestG = null;
            for (int i = 0; i < population.Count; i++)
            {
                Genome g = population[i];
                if (fitness < g.fitness)
                {
                    fitness = g.fitness;
                    if(fitness < MinEliteFitness) continue; //only add agents with the minimum fitness
                    if(!populationControllers[i].willSurvive) continue; //only add agents that ate
                    bestG = g;
                }
            }

            //If no genome was beyond min requirements, don't add it to the list
            if(bestG == null) return fitness;
            
            if(bestGenomes.Count < initialPopCount)
                bestGenomes.Add(bestG);
            else
            {
                float worstFitness = bestGenomes[0].fitness;
                Genome g = bestGenomes[0];
                for (int i = 1; i < bestGenomes.Count; i++)
                {
                    if(bestGenomes[i].fitness >= worstFitness) continue;
                    
                    worstFitness = bestGenomes[i].fitness;
                    g = bestGenomes[i];
                }

                if (bestG.fitness > worstFitness)
                {
                    bestGenomes.Remove(g);
                    bestGenomes.Add(bestG);
                }
            }
            
            return fitness;
        }
        private float getAvgFitness()
        {
            float fitness = 0;
            foreach (Genome g in population)
            {
                fitness += g.fitness;
            }

            return fitness / population.Count;
        }
        private float getWorstFitness()
        {
            float fitness = float.MaxValue;
            foreach (Genome g in population)
            {
                if (fitness > g.fitness)
                    fitness = g.fitness;
            }

            return fitness;
        }

        public void StartSimulation(int initialPopCount, Game.Map map, bool isTeam1)
        {
            this.initialPopCount = initialPopCount;
            this.map = map;
            
            this.isTeam1 = isTeam1;

            // Create and configure the Genetic Algorithm
            int maxPopulation = map.width;
            genAlg = new GeneticAlgorithm(maxPopulation, MutationChance, MutationRate);

            GenerateInitialPopulation();
        }

        public void StopSimulation()
        {
            generation = 0;
            Stage = Stage.Hunger;
        }

        // Generate the random initial population
        void GenerateInitialPopulation()
        {
            generation = 0;
            brains.Clear();
            population.Clear();
            populationControllers.Clear();

            // Destroy previous tanks (if there are any)
            // DestroyTanks();

            for (int i = 0; i < initialPopCount; i++)
            {
                NeuralNetwork brain = CreateBrain();

                Genome genome = new Genome(brain.GetTotalWeightsCount());

                brain.SetWeights(genome.genome);
                brains.Add(brain);

                //Create genome and agent
                population.Add(genome);
                populationControllers.Add(CreateAgent(genome, brain));
                
                //Set agent
                SetAgent(i, false);
            }
            
            if(isTeam1)
                map.population1 = populationControllers;
            else
                map.population2 = populationControllers;
        }

        // Creates a new NeuralNetwork
        NeuralNetwork CreateBrain()
        {
            NeuralNetwork brain = new NeuralNetwork();

            // Add first neuron layer that has as many neurons as inputs
            brain.AddFirstNeuronLayer(InputsCount, Bias, P);

            for (int i = 0; i < HiddenLayers; i++)
            {
                // Add each hidden layer with custom neurons count
                brain.AddNeuronLayer(NeuronsCountPerHL, Bias, P);
            }

            // Add the output layer with as many neurons as outputs
            brain.AddNeuronLayer(OutputsCount, Bias, P);

            return brain;
        }

        // Evolve!!! (THIS SHOULD BE END OF TURN STAGE)
        public bool Epoch()
        {
            // Increment generation counter
            generation++;

            //Calculate the fitness of all agents
            for (int i = 0; i < populationControllers.Count; i++)
            {
                populationControllers[i].CalcFitness();
            }
            
            // Calculate best, average and worst fitness
            bestFitness = getBestFitness();
            avgFitness = getAvgFitness();
            worstFitness = getWorstFitness();

            Genome[] newGenomes;
            
            //Get elite and reproductive genomes
            List<Genome> eliteGenomes = new List<Genome>();
            List<Genome> reproGenomes = new List<Genome>();
            if (Stage >= Stage.Hunger)
            {
                //Check which ones survive and which ones reproduce
                for (int i = 0; i < populationControllers.Count; i++)
                {
                    if (populationControllers[i].CanAdvanceGen())
                        eliteGenomes.Add(population[i]);
                    if (populationControllers[i].canReproduce)
                        reproGenomes.Add(population[i]);
                }
                
                //If none will reproduce, check if someone will survive
                if(reproGenomes.Count < 2)
                    if(eliteGenomes.Count < 2) return false;
                
                // Evolve each genome and create a new array of genomes
                newGenomes = genAlg.Epoch(eliteGenomes.ToArray(), reproGenomes.ToArray());
            }
            else
            {
                //Add everyone for reproduction
                reproGenomes.AddRange(population);
                
                // Evolve each genome and create a new array of genomes
                newGenomes = genAlg.Epoch(reproGenomes.ToArray(), FirstStageElitePairs*2);
            }
            
            for (int i = 0; i < populationControllers.Count; i++)
            {
                populationControllers[i].AdvanceGen();
            }
            
            // Clear current population
            population.Clear();

            // switch (stage)
            // {
            //     case Stage.GoodMines:
            //         if (avgFitness >= BadMinesAvgFitness) stage++;
            //         break;
            //     case Stage.BadMines:
            //         if (avgFitness >= TanksAvgFitness) stage++;
            //         break;
            //     case Stage.Tanks:
            //         if (avgFitness >= TreesAvgFitness) stage++;
            //         break;
            // }

            // Add new population
            population.AddRange(newGenomes);
            
            
            while (populationControllers.Count < population.Count)
            {
                // Create a new NeuralNetwork
                NeuralNetwork brain = CreateBrain();
                Genome genome = population[populationControllers.Count];
                brain.SetWeights(genome.genome);
                brains.Add(brain);
                
                AgentBase agent = CreateAgent(genome, brain);
                populationControllers.Add(agent);
            }

            while (populationControllers.Count > population.Count)
            {
                brains.RemoveAt(populationControllers.Count-1);
                populationControllers.RemoveAt(populationControllers.Count-1);
            }

            // Set the new genomes as each NeuralNetwork weights
            for (int i = 0; i < population.Count; i++)
            {
                NeuralNetwork brain = brains[i];

                brain.SetWeights(newGenomes[i].genome);

                SetAgent(i);
            }
            
            if(isTeam1)
                map.population1 = populationControllers;
            else
                map.population2 = populationControllers;

            return true;
        }

        // Change population for a new one
        public void Repopulate()
        {
            if (bestGenomes.Count > 0)
            {
                Genome[] newGenomes = new Genome[initialPopCount];
                
                int weightAmount = bestGenomes[0].genome.Length;
                for (int i = 0; i < initialPopCount; i++)
                {
                    if(i < bestGenomes.Count) //this ones are the real deal, will reproduce
                        newGenomes[i] = bestGenomes[i];
                    else //this ones won't reproduce, as have fit 0, they're only fillers
                        newGenomes[i] = new Genome(weightAmount); 
                }
                
                Repopulate(genAlg.Epoch(newGenomes), Stage);
            }
            else //If there are no best ones, generate a completely new batch of agents
                GenerateInitialPopulation();
        }
        public void Repopulate(Genome[] newGenomes, Stage stage)
        {
            generation = 0;
            population.Clear();
            
            Stage = stage;

            // Add new population
            population.AddRange(newGenomes);

            while (populationControllers.Count < population.Count)
            {
                // Create a new NeuralNetwork
                NeuralNetwork brain = CreateBrain();
                Genome genome = population[populationControllers.Count];
                brain.SetWeights(genome.genome);
                brains.Add(brain);
                
                AgentBase agent = CreateAgent(genome, brain);
                populationControllers.Add(agent);
            }

            while (populationControllers.Count > population.Count)
            {
                brains.RemoveAt(populationControllers.Count-1);
                populationControllers.RemoveAt(populationControllers.Count-1);
            }
            
            // Set the new genomes as each NeuralNetwork weights
            for (int i = 0; i < population.Count; i++)
            {
                NeuralNetwork brain = brains[i];

                brain.SetWeights(newGenomes[i].genome);

                SetAgent(i);
            }
            
            if(isTeam1)
                map.population1 = populationControllers;
            else
                map.population2 = populationControllers;
        }
        
        public Genome[] GetRandomPopulation()
        {
            return genAlg.GetRandomPopulation();
        }
        
        public Genome[] GetPopulation()
        {
            return population.ToArray();
        }

        // Update the population
        public void Update()
        {
            bool interactWithEnemies = Stage >= Stage.Enemies;
            
            for (int i = 0; i < populationControllers.Count; i++)
            {
                if(map.food.Count == 0) return;
                
                AgentBase a = populationControllers[i];
                Vec2 agentPos = a.position;
                
                a.SetNearAlly(GetAllyPos(agentPos));
                a.SetNearEnemy(GetEnemyPos(agentPos));

                if (Stage == Stage.Tutorial && a.foodID >= 0)
                {
                    a.Think();
                    return;
                }
                
                int foodIndex = GetFood(agentPos);
                a.SetNearFoodPos(map.food[foodIndex], foodIndex);

                void OnFoodTaken()
                {
                    //If can interact with enemies, don't eat, add agent to eaters list and hope for the best
                    if (interactWithEnemies)
                    {
                        a.UnEat();
                        List<AgentBase> list;

                        if (map.foodTaken.TryGetValue(foodIndex, out list))
                        {
                            //If there's already 2 or more agents in the list, cell is overcrowded, return
                            if(list.Count > 1)
                                a.ReturnToLastPos();
                            
                            //else go to food
                            else
                                list.Add(a);
                        }
                        else 
                        {
                            list = new List<AgentBase> { a };
                            map.foodTaken.TryAdd(foodIndex, list);
                        }
                    }

                    //If CAN'T interact with enemies yet, eat food, and be happy
                    else
                    {
                        map.foodTaken.TryAdd(foodIndex, new List<AgentBase> { a });
                        map.food.RemoveAt(foodIndex);
                    }
                }

                a.FoodTaken += OnFoodTaken;
                
                a.Think();
                
                a.FoodTaken -= OnFoodTaken;
            }
        }

        void SetAgent(int index, bool setBrain = true)
        {
            if(setBrain)
                populationControllers[index].SetBrain(population[index], brains[index]);
            
            SetAgent(populationControllers[index]);
            populationControllers[index].SetPosition(GetAgentFirstPos(index));
        }
        void SetAgent(AgentBase a)
        {
            a.SetStage(Stage);
            a.SetMinAndMax(new Vec2(0,0), new Vec2(map.width, map.height));
            a.up = isTeam1 ? 1 : -1;
            a.isTeam1 = isTeam1;

            a.Died += OnAgentDied;
        }
        
        void OnAgentDied(AgentBase agent)
        {
            int index = populationControllers.IndexOf(agent);
            
            brains.RemoveAt(index);
            population.RemoveAt(index);
            populationControllers.RemoveAt(index);
        }

        #region Helpers

        protected virtual AgentBase CreateAgent(Genome genome, NeuralNetwork brain)
        {
            AgentBase agent = new AgentBase(genome, brain);
            
            return agent;
        }
        Vec2 GetAgentFirstPos(int index)
        {
            if (isTeam1)
                return new Vec2(map.width - index, map.height);
            else
                return new Vec2(index, 0);
        }
        AgentBase GetAllyPos(Vec2 pos)
        {
            if (isTeam1)
            {
                AgentBase nearest = map.population1[0];
                float distance = Vec2.Distance(pos, nearest.position);

                for (int i = 1; i < map.population1.Count; i++)
                {
                    float newDist = Vec2.Distance(pos, map.population1[i].position);
                    if (newDist < distance)
                    {
                        distance = newDist;
                        nearest = map.population1[i];
                    }
                }
                
                return nearest;
            }
            else
            {
                AgentBase nearest = map.population2[0];
                float distance = Vec2.Distance(pos, nearest.position);
                
                for (int i = 1; i < map.population2.Count; i++)
                {
                    float newDist = Vec2.Distance(pos, map.population2[i].position);
                    if (newDist < distance)
                    {
                        distance = newDist;
                        nearest = map.population2[i];
                    }
                }
                
                return nearest;
            }
        }
        AgentBase GetEnemyPos(Vec2 pos)
        {
            if (isTeam1)
            {
                AgentBase nearest = map.population2[0];
                float distance = Vec2.Distance(pos, nearest.position);
                
                for (int i = 1; i < map.population2.Count; i++)
                {
                    float newDist = Vec2.Distance(pos, map.population2[i].position);
                    if (newDist < distance)
                    {
                        distance = newDist;
                        nearest = map.population2[i];
                    }
                }
                
                return nearest;
            }
            else
            {
                AgentBase nearest = map.population1[0];
                float distance = Vec2.Distance(pos, nearest.position);
                
                for (int i = 1; i < map.population1.Count; i++)
                {
                    float newDist = Vec2.Distance(pos, map.population1[i].position);
                    if (newDist < distance)
                    {
                        distance = newDist;
                        nearest = map.population1[i];
                    }
                }
                
                return nearest;
            }
        }
        int GetFood(Vec2 pos)
        {
            int nearest = 0;
            Vec2 foodPos = map.food[0];

            float distance = Vec2.Distance(pos, foodPos);
            
            for (int i = 1; i < map.food.Count; i++)
            {
                float newDist = Vec2.Distance(pos, map.food[i]);
                if (newDist < distance)
                {
                    distance = newDist;
                    nearest = i;
                }
            }
            
            return nearest;
        }

        #endregion
    }
}