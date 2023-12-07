using System;
using System.Collections.Generic;
using UnityEngine;

namespace IA.FSM
{
    public class AgentManager : MonoBehaviour
    {
        [System.Serializable]
        class Mine
        {
            public Transform t;
            public int minerals = 30;
            public int food = (15/3)*2;
            public Vector2Int gridPos;

            public Mine()
            {
                t = null;
                gridPos = Vector2Int.zero;
                
                minerals = 30;
                food = (15/3)*2;
            }
        }
        
        [Header("Set Values")]
        [SerializeField] Pathfinding.PathManager pathManager;
        [SerializeField] Transform urbanCenter;
        [SerializeField] Miner.AMiner minerTemplate;
        [SerializeField] GameObject minerPrefab;
        [SerializeField] int maxMiners = 10;
        [SerializeField] float minerSpawnInterval;
        [SerializeField] Mine[] mines;
        //[Header("Runtime Values")]
        List<Miner.AMiner> miners;
        float spawnTimer;
        Dictionary<Vector2Int, Mine> minesByGridPos;

        
        void Start()
        {
            miners = new List<Miner.AMiner>();
            
            minesByGridPos = new Dictionary<Vector2Int, Mine>();
            for (int i = 0; i < mines.Length; i++)
            {
                Vector2Int gridPos = pathManager.GetGridPos(mines[i].t.position, 0);
                mines[i].gridPos = gridPos;
                minesByGridPos.Add(gridPos, mines[i]);
            }
            
            SpawnMiner();
        }
        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < miners.Count; i++)
            {
                miners[i].UpdateFSM(dt);
                miners[i].UpdateTransform();
            }
            
            spawnTimer += dt;
            if (spawnTimer >= minerSpawnInterval)
            {
                spawnTimer = 0;
                SpawnMiner();
            }
        }
        
        
        //Methods
        void SpawnMiner()
        {
            if(miners.Count >= maxMiners) return;
            
            Miner.AMiner miner = new Miner.AMiner();

            miner.moveSpeed = minerTemplate.moveSpeed;
            miner.mineInterval = minerTemplate.mineInterval;
            miner.eatDuration = minerTemplate.eatDuration;
            miner.depositDuration = minerTemplate.depositDuration;
            
            GameObject minerBody = Instantiate(minerPrefab);
            minerBody.transform.parent = transform;
            minerBody.transform.position = urbanCenter.position;
            Func<Vector2Int, bool> tryMine = TryMine;
            Func<Vector2Int, bool> tryEat = TryEat;
            miner.Set(pathManager, 0, urbanCenter, urbanCenter, minerBody.transform, tryMine, tryEat);
            
            miners.Add(miner);
        }
        bool TryMine(Vector2Int minePos)
        {
            Mine mine;
            if (minesByGridPos.TryGetValue(minePos, out mine))
            {
                mine.minerals--;
                
                if (mine.minerals < 0)
                {
                    minesByGridPos.Remove(minePos);
                    mine.t.gameObject.SetActive(false);

                    pathManager.RemovePointOfInterest(mine.gridPos);
                    
                    if(minesByGridPos.Count <= 0)
                        for (int i = 0; i < miners.Count; i++)
                            miners[i].OnNoMoreMines();
                    
                    return false;
                }
                
                return mine.minerals >= 0;
            }
            
            return false;
        }
        bool TryEat(Vector2Int foodStoragePos)
        {
            Mine mine;
            if (minesByGridPos.TryGetValue(foodStoragePos, out mine))
            {
                mine.food--;
                return mine.food >= 0;
            }
            return false;
        }
    }
}