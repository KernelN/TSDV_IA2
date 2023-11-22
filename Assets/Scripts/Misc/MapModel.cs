using System;
using System.Collections.Generic;
using UnityEngine;

namespace IA.Game
{
    public class MapModel : MonoBehaviour
    {
        class SpriteController
        {
            public Transform t;
            public SpriteRenderer sprite;

            public SpriteController() { }
            public SpriteController(Transform t, SpriteRenderer sprite)
            {
                this.t = t;
                this.sprite = sprite;
            }
        }

        // [SerializeField] Transform foodHolder;
        // [SerializeField] Transform pop1Holder;
        // [SerializeField] Transform pop2Holder;
        [SerializeField] Transform mapRenderer;
        [SerializeField] GameObject foodPrefab;
        [SerializeField] GameObject agent1Prefab;
        [SerializeField] GameObject agent2Prefab;

        Population.PopulationsManager popsManager;
        Map data;
        List<SpriteController> food;
        List<SpriteController> pop1;
        List<SpriteController> pop2;
        int foodCount;
        int agent1Count;
        int agent2Count;


        //Unity Events
        void Start()
        {
            popsManager = Population.PopulationsManager.Instance;
            
            popsManager.SimulationStarted += OnSimStart;
            popsManager.SimulationUpdated += OnSimUpdate;
            popsManager.GenerationChanged += OnNewGeneration;
        }
        void OnEnable()
        {
            if(data == null) return;
            
            OnNewGeneration();
        }

        //Methods
        SpriteController CreateSprite(GameObject prefab, Math.Vec2 pos)
        {
            Transform t = Instantiate(prefab).transform;
            t.SetParent(transform);

            SpriteRenderer renderer = t.GetComponent<SpriteRenderer>();

            SpriteController sc = new SpriteController(t, renderer);
            sc.t.position = new Vector3(pos.x, pos.y, 0);

            return sc;
        }

        //Event Receivers
        void OnSimStart()
        {
            if (data != null)
            {
                OnNewGeneration();
                return;
            }
                
            food = new List<SpriteController>();
            pop1 = new List<SpriteController>();
            pop2 = new List<SpriteController>();

            data = popsManager.Map;
            mapRenderer.localScale = new Vector3(data.width, data.height, 1);
            mapRenderer.position = new Vector3(data.width / 2, data.height / 2, 0);
            Camera.main.orthographicSize = data.height / 1.75f;

            foodCount = data.food.Count;
            agent1Count = data.population1.Count;
            agent2Count = data.population2.Count;

            for (int i = 0; i < data.food.Count; i++)
            {
                Math.Vec2 pos = data.food[i];
                food.Add(CreateSprite(foodPrefab, pos));
                food[i].t.name = "F" + i + " " + pos.x + " " + pos.y;
            }

            for (int i = 0; i < data.population1.Count; i++)
            {
                Math.Vec2 pos = data.population1[i].position;
                pop1.Add(CreateSprite(agent1Prefab, pos));
                pop1[i].t.name = "Agent1 " + i;// + " " + pos.x + " " + pos.y;
            }

            for (int i = 0; i < data.population2.Count; i++)
            {
                Math.Vec2 pos = data.population2[i].position;
                pop2.Add(CreateSprite(agent2Prefab, pos));
                pop2[i].t.name = "Agent2 " + i;// + " " + pos.x + " " + pos.y;
            }
        }
        void OnSimUpdate()
        {
            if(this.enabled == false) return;
            //Remove dead agents and empty food
            if(foodCount > data.food.Count)
            {
                for (int i = 0; i < data.food.Count; i++)
                {
                    food[i].t.position = new Vector3(data.food[i].x, data.food[i].y, 0);
                }
                for (int i = data.food.Count; i < foodCount; i++)
                {
                    food[i].sprite.enabled = false;
                }
                
                foodCount = data.food.Count;
            }

            while (agent1Count > data.population1.Count)
            {
                pop1[agent1Count - 1].sprite.enabled = false;
                agent1Count--;
            }

            while (agent2Count > data.population2.Count)
            {
                pop2[agent2Count - 1].sprite.enabled = false;
                agent2Count--;
            }

            List<Agent.AgentBase> pop = data.population1;
            for (int i = 0; i < agent1Count; i++)
            {
                Vector3 pos = new Vector3(pop[i].position.x, pop[i].position.y, 0);
                pop1[i].t.position = pos;
            }

            pop = data.population2;
            for (int i = 0; i < agent2Count; i++)
            {
                Vector3 pos = new Vector3(pop[i].position.x, pop[i].position.y, 0);
                pop2[i].t.position = pos;
            }
        }
        void OnNewGeneration()
        {
            if(this.enabled == false) return;
            //Remove dead agents and empty food
            while (agent1Count > data.population1.Count)
            {
                pop1[agent1Count - 1].sprite.enabled = false;
                agent1Count--;
            }

            while (agent2Count > data.population2.Count)
            {
                pop2[agent2Count - 1].sprite.enabled = false;
                agent2Count--;
            }

            //Add new agents and recover food
            while (food.Count < data.food.Count)
            {
                food.Add(CreateSprite(foodPrefab, data.food[food.Count]));
            }
            for (int i = 0; i < data.food.Count; i++)
            {
                food[i].sprite.enabled = true;
            }
            foodCount = data.food.Count;

            while (agent1Count < data.population1.Count)
            {
                if (pop1.Count > agent1Count)
                    pop1[agent1Count - 1].sprite.enabled = true;
                else
                    pop1.Add(CreateSprite(agent1Prefab, data.population1[agent1Count - 1].position));

                agent1Count++;
            }

            while (agent2Count < data.population2.Count)
            {
                if (pop2.Count > agent2Count)
                    pop2[agent2Count - 1].sprite.enabled = true;
                else
                    pop2.Add(CreateSprite(agent2Prefab, data.population2[agent2Count - 1].position));

                agent2Count++;
            }
            
            //Update positions
            for (int i = 0; i < food.Count; i++)
            {
                Vector3 pos = new Vector3(data.food[i].x, data.food[i].y, 0);
                food[i].t.position = pos;
                food[i].t.name = "F" + i + " " + data.food[i].x + " " + data.food[i].y;
            }

            List<Agent.AgentBase> pop = data.population1;
            for (int i = 0; i < agent1Count; i++)
            {
                Vector3 pos = new Vector3(pop[i].position.x, pop[i].position.y, 0);
                pop1[i].t.position = pos;
                pop1[i].t.name = "Agent1 " + i;// + " " + pop[i].position.x + " " + pop[i].position.y;
            }

            pop = data.population2;
            for (int i = 0; i < agent2Count; i++)
            {
                Vector3 pos = new Vector3(pop[i].position.x, pop[i].position.y, 0);
                pop2[i].t.position = pos;

                pop2[i].t.name = "Agent2 " + i;// + " " + pop[i].position.x + " " + pop[i].position.y;
            }
        }
    }
}