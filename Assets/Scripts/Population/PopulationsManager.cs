using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace IA.Population
{
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
            }
            
            //THIS IS WRONG
            for (int i = 0; i < TurnsPerSecond; i++)
            {
                pop1.Update();
                pop2.Update();

                Turn++;
                if (Turn >= TurnsPerGeneration || map.food.Count == 0)
                {
                    Turn -= TurnsPerGeneration;
                    if (Turn < 0) Turn = 0;
                    
                    CreateFood();
                    if (!pop1.Epoch()) //needs to use survivors of other pop ASAP
                    {
                        Debug.Break();
                        StopSimulation();
                        return;
                    }
                    if (pop2.Epoch())
                    {
                        Debug.Break();
                        StopSimulation();
                        return;
                    }
                    
                    GenerationChanged?.Invoke();
                    //break;
                }
            }
            
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