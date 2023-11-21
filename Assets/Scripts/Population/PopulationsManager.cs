using System.Collections.Generic;
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
        
        [Header("Population")]
        [SerializeField, Min(3)] int TurnsPerGeneration = 50;
        [SerializeField, Min(2)] int InitialPopulationCount = 10;
        [SerializeField] PopulationManager pop1;
        [SerializeField] PopulationManager pop2;

        float turnTimer;
        bool isRunning;

        public static PopulationsManager Instance { get; private set; }
        public int Turn { get; private set; }
        public int TurnsPerSecond { get; set; } = 1;
        
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

            // // Destroy previous tanks (if there are any)
            // DestroyAgents();
            //
            // // Destroy all mines
            // DestroyFood();
        }
        public void SavePopulations(string fileName)
        {
            PopulationData pop1Data = new PopulationData();
            PopulationData pop2Data = new PopulationData();
            
            pop1Data.genomes = pop1.GetPopulation();
            pop2Data.genomes = pop2.GetPopulation();
            
            pop1Data.stage = (int)pop1.Stage;
            pop2Data.stage = (int)pop2.Stage;
            
            string dataPath = Application.persistentDataPath + "pop1Data_" + fileName + ".bin";
            Universal.FileManaging.FileManager<PopulationData>.SaveDataToFile(pop1Data, dataPath);
            
            dataPath = Application.persistentDataPath + "pop2Data_" + fileName + ".bin";
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

            Turn++;
            if (Turn >= TurnsPerGeneration || map.food.Count == 0)
            {
                Turn -= TurnsPerGeneration;
                if (Turn < 0) Turn = 0;
                    
                CreateFood();
                    
                bool pop1Survived = pop1.Epoch();
                bool pop2Survived = pop2.Epoch();
                    
                //If neither population survived, end
                if (!(pop1Survived || pop2Survived)) 
                {
                    Debug.Break();
                    StopSimulation();
                    return;
                }
                    
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
                map.foodTaken = new List<int>();
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