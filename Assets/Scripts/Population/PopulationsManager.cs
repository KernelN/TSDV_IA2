using System.Collections.Generic;
using System.Linq;
using IA.Agent;
using UnityEngine;
using Random = UnityEngine.Random;

namespace IA.Population
{
    [System.Serializable]
    public class PopulationData
    {
        public GeneAlgo.Genome[] genomes;
        public int stage;
    }
    
    public class PopulationsManager : MonoBehaviour
    {
        [SerializeField] Game.Map map;
        [SerializeField] int divineInterventionsPerAutosave = 10;
        [SerializeField] bool autoSave;
        
        [Header("Population")]
        [SerializeField, Min(3)] int TurnsPerGeneration = 50;
        [SerializeField, Min(2)] int InitialPopulationCount = 10;
        [SerializeField] PopulationManager pop1;
        [SerializeField] PopulationManager pop2;

        int divineInterventions;
        float turnTimer;
        bool isRunning;

        public static PopulationsManager Instance { get; private set; }
        public int Turn { get; private set; }
        public int TurnsPerSecond { get; set; } = 5;
        
        public Game.Map Map => map;
        public PopulationManager Pop1 => pop1;
        public PopulationManager Pop2 => pop2;
        
        float SecondsPerTurn { get { return 1f / TurnsPerSecond; } }

        public System.Action SimulationStarted;
        public System.Action SimulationUpdated;
        public System.Action GenerationChanged;
        
        //Unity Events
        void Awake()
        {
            if(PopulationsManager.Instance != null)
                Destroy(this);
            else
                PopulationsManager.Instance = this;
        }
        public void Update()
        {
            if(!isRunning) return;

            float dt = Time.deltaTime;
            
            //If time delta wasn't enough to make a turn, accumulate it
            if (dt < SecondsPerTurn)
            {
                turnTimer += dt;
                
                if(turnTimer < SecondsPerTurn) return;
                
                turnTimer -= SecondsPerTurn;
                UpdateSimulation();
            }
            else
            {
                int turns = (int)(dt / SecondsPerTurn);
                for (int i = 0; i < turns; i++)
                {
                    UpdateSimulation();
                }
            }
            
            //THIS IS WRONG
            
            SimulationUpdated?.Invoke();
        }
        
        //Methods
        public void StartSimulation()
        {
            CreateFood();

            pop1.StartSimulation(InitialPopulationCount, map, true);
            pop2.StartSimulation(InitialPopulationCount, map, false);
            
            if(autoSave)
                SavePopulations("START");

            isRunning = true;
            
            SimulationStarted?.Invoke();
        }
        public void PauseSimulation()
        {
            isRunning = !isRunning;
        }
        public void StopSimulation()
        {
            isRunning = false;
            Turn = 0;
            divineInterventions = 0;
            
            pop1.StopSimulation();
            pop2.StopSimulation();
        }
        public void SavePopulations(string fileName)
        {
            SavePopulations(fileName, pop1.GetPopulation(), pop2.GetPopulation());
        }
        public void SavePopulations(string fileName, GeneAlgo.Genome[] pop1, GeneAlgo.Genome[] pop2)
        {
            PopulationData pop1Data = new PopulationData();
            PopulationData pop2Data = new PopulationData();
            
            pop1Data.genomes = pop1;
            pop2Data.genomes = pop2;
            
            pop1Data.stage = (int)this.pop1.Stage;
            pop2Data.stage = (int)this.pop2.Stage;
            
            string dataPath = Application.persistentDataPath + "_pop1Data_" + fileName + ".bin";
            Universal.FileManaging.FileManager<PopulationData>.SaveDataToFile(pop1Data, dataPath);
            
            dataPath = Application.persistentDataPath + "_pop2Data_" + fileName + ".bin";
            Universal.FileManaging.FileManager<PopulationData>.SaveDataToFile(pop2Data, dataPath);
        }
        public void LoadPopulations(string fileName)
        {
            StartSimulation();
            
            PopulationData pop1Data = new PopulationData();
            PopulationData pop2Data = new PopulationData();
            
            string dataPath = Application.persistentDataPath + "pop1Data_" + fileName + ".bin";
            pop1Data = Universal.FileManaging.FileManager<PopulationData>.LoadDataFromFile(dataPath);
            
            dataPath = Application.persistentDataPath + "pop2Data_" + fileName + ".bin";
            pop2Data = Universal.FileManaging.FileManager<PopulationData>.LoadDataFromFile(dataPath);
            
            pop1.Repopulate(pop1Data.genomes, (Stage)pop1Data.stage);
            pop2.Repopulate(pop2Data.genomes, (Stage)pop2Data.stage);
        }
        void UpdateSimulation()
        {
            pop1.Update();
            pop2.Update();

            //Manage food eating
            bool bothPopsCanSeeEnemies = pop1.Stage >= Stage.Enemies && pop2.Stage >= Stage.Enemies;
            bool bothPopsCanSeeAllies = pop1.Stage >= Stage.Allies && pop2.Stage >= Stage.Allies;
            while (map.foodTaken.Count > 0)
            {
                List<AgentBase> agents = map.foodTaken.Values.First();

                //If there's only one agent, let it eat and be happy
                if (agents.Count == 1)
                {
                    agents[0].ForceEat();
                    map.foodTaken.Remove(map.foodTaken.Keys.First());
                    continue;
                }

                //If can't interact with enemies yet, only the first one will eat
                if (!bothPopsCanSeeEnemies)
                {
                    agents[0].ForceEat();
                    map.foodTaken.Remove(map.foodTaken.Keys.First());
                    break;
                }

                //If they're not the same team, fight
                if (agents[0].isTeam1 != agents[1].isTeam1)
                {
                    //If agent A flees...
                    if (agents[0].willFleeAgainstEnemy)
                    {
                        //...and agent B flees too, nobody eats, nobody dies
                        if (agents[1].willFleeAgainstEnemy)
                        {
                            agents[0].ReturnToLastPos();
                            agents[1].ReturnToLastPos();
                        }

                        //...and agent B doesn't flee, agent B eats
                        else
                        {
                            agents[0].ReturnToLastPos();
                            agents[1].ForceEat();
                        }
                    }

                    //If agent A doesn't flee...
                    else
                    {
                        //...and agent B flees, agent A eats
                        if (agents[1].willFleeAgainstEnemy)
                        {
                            agents[1].ReturnToLastPos();
                            agents[0].ForceEat();
                        }

                        //...and agent B doesn't flee, somebody dies, somebody eats
                        else
                        {
                            float survChance = Random.Range(0, 1f);

                            if (survChance < 0.5f)
                            {
                                agents[0].Die();
                                agents[1].ForceEat();
                            }
                            else
                            {
                                agents[1].Die();
                                agents[0].ForceEat();
                            }
                        }
                    }
                }

                //Else, share (Unless they can't interact with allies yet, then only the first one will eat)
                else if (!bothPopsCanSeeAllies)
                {
                    agents[0].ForceEat();
                    map.foodTaken.Remove(map.foodTaken.Keys.First());
                    break;
                }
                else
                {
                    if (agents[0].willGiveFoodToAlly)
                    {
                        if (agents[1].willGiveFoodToAlly)
                        {
                            agents[0].ReturnToLastPos();
                            agents[1].ReturnToLastPos();
                        }
                        else
                        {
                            agents[0].ReturnToLastPos();
                            agents[1].ForceEat();
                        }
                    }
                    else
                    {
                        if (agents[1].willGiveFoodToAlly)
                        {
                            agents[1].ReturnToLastPos();
                            agents[0].ForceEat();
                        }
                        else
                        {
                            agents[0].ForceEat(.5f);
                            agents[1].ForceEat(.5f);
                        }
                    }
                }
            }

            Turn++;
            if (Turn >= TurnsPerGeneration || map.food.Count == 0)
            {
                Turn -= TurnsPerGeneration;
                if (Turn < 0) Turn = 0;
                    
                CreateFood();
                    
                GeneAlgo.Genome[] pop1Gs = pop1.GetPopulation();
                GeneAlgo.Genome[] pop2Gs = pop2.GetPopulation();
                
                bool pop1Survived = pop1.Epoch();
                bool pop2Survived = pop2.Epoch();
                    
                //If neither population survived, make a divine intervention
                if (!(pop1Survived || pop2Survived))
                {
                    divineInterventions++;
                    if(autoSave && divineInterventions % divineInterventionsPerAutosave == 0)
                        SavePopulations("DI_N" + divineInterventions, pop1Gs, pop2Gs);
                    
                    
                    List<GeneAlgo.Genome> bestOfBest = new List<GeneAlgo.Genome>();
                    
                    GeneAlgo.Genome[] pop1Best = pop1.GetBestGenomes();
                    GeneAlgo.Genome[] pop2Best = pop2.GetBestGenomes();

                    //If both have a selection of best genomes, make a mix of both
                    if (pop1Best.Length > 0 && pop2Best.Length > 0)
                    {
                        bestOfBest.AddRange(pop1Best);
                        bestOfBest.AddRange(pop2Best);
                        
                        bestOfBest.Sort(GeneAlgo.GeneticAlgorithm.HandleComparison);
                        
                        
                        GeneAlgo.Genome[] newGenomes = new GeneAlgo.Genome[InitialPopulationCount];
                
                        int weightAmount = bestOfBest[0].genome.Length;
                        for (int i = 0; i < InitialPopulationCount; i++)
                        {
                            if(i < bestOfBest.Count) //this ones are the real deal, will reproduce
                                newGenomes[i] = bestOfBest[i];
                            else //this ones won't reproduce, as have fit 0, they're only fillers
                                newGenomes[i] = new GeneAlgo.Genome(weightAmount); 
                        }
                        
                        pop1.Repopulate(newGenomes, pop1.Stage);
                        pop2.Repopulate(newGenomes, pop2.Stage);
                    }
                    
                    //Else, let them handle the repopulation
                    else
                    {
                        pop1.Repopulate();
                        pop2.Repopulate();
                    }
                    
                    GenerationChanged?.Invoke();
                    return;
                }
                    
                //If only one population survived, repopulate the other with random genes of the survivor
                if(!pop1Survived)
                {
                    pop1.Repopulate(pop2.GetRandomPopulation(), pop2.Stage);
                }
                else if(!pop2Survived)
                {
                    pop2.Repopulate(pop1.GetRandomPopulation(), pop1.Stage);
                }
                    
                GenerationChanged?.Invoke();
                //break;
            }
        }
        void CreateFood()
        {
            if (InitialPopulationCount > map.width)
            {
                Debug.LogError("POPULATION IS TOO BIG, SHOULD BE LESS THAN WIDTH");
                return;
            }

            if (map.food == null)
            {
                map.food = new List<Math.Vec2>();
                map.foodTaken = new Dictionary<int, List<AgentBase>>();
            }
            else
            {
                map.food.Clear();
                map.foodTaken.Clear();
            }

            int minX = 1;
            int maxX = map.width - 1;
            int minY = 1;
            int maxY = map.height - 1;

            for (int i = 0; i < InitialPopulationCount * 2; i++)
            {
                Math.Vec2 pos;
                do
                {
                    pos = new Math.Vec2(Random.Range(minX, maxX), Random.Range(minY, maxY));
                } while (PosHasFood(pos));
                map.food.Add(pos);
            }
        }
        bool PosHasFood(Math.Vec2 pos)
        {
            for (int i = 0; i < map.food.Count; i++)
            {
                if (map.food[i] == pos) return true;
            }
            
            return false;
        }
    }
}