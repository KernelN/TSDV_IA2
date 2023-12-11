using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace IA.FSM
{
    public class AgentManager : MonoBehaviour
    {
        [Serializable]
        class Mine
        {
            [Header("Set Values")]
            public Transform t;
            public int minerals = 30;
            public int food = (15/3)*2;
            [Header("Runtime Values")]
            public int id;

            public Mine()
            {
                t = null;
                id = 0;
                
                minerals = 30;
                food = (15/3)*2;
            }
        }
        
        [Header("General Settings")]
        [SerializeField] Pathfinding.PathManager pathManager;
        [SerializeField] Transform urbanCenter;
        [SerializeField] float mineInUseCheckInterval;
        [SerializeField] Mine[] mines;
        [Header("Miner Settings")]
        [SerializeField] Miner.AMiner minerTemplate;
        [SerializeField] GameObject minerPrefab;
        [SerializeField] int maxMiners = 10;
        [SerializeField] float minerSpawnInterval;
        [Header("Caravan Settings")]
        [SerializeField] Caravan.ACaravan caravanTemplate;
        [SerializeField] GameObject caravanPrefab;
        [SerializeField] int maxCaravans = 3;
        [SerializeField] float caravanSpawnInterval;
        //[Header("Runtime Values")]
        List<Miner.AMiner> miners;
        List<Caravan.ACaravan> caravans;
        float minerSpawnTimer;
        float caravanSpawnTimer;
        float mineCheckTimer;
        Dictionary<int, Mine> minesByID;

        //Unity Events
        void Start()
        {
            miners = new List<Miner.AMiner>();
            caravans = new List<Caravan.ACaravan>();
            
            minesByID = new Dictionary<int, Mine>();
            for (int i = 0; i < mines.Length; i++)
            {
                Vector2Int gridPos = pathManager.GetGridPos(mines[i].t.position, 0);
                mines[i].id = pathManager.GetPathfinder(0).FindPointRegion(gridPos);
                minesByID.Add(mines[i].id, mines[i]);
            }
            
            SpawnMiner();
            SpawnCaravan();
            
            mineCheckTimer = mineInUseCheckInterval - 1;
        }
        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < miners.Count; i++)
            {
                miners[i].UpdateFSM(dt);
                miners[i].UpdateTransform();
            }
            for (int i = 0; i < caravans.Count; i++)
            {
                caravans[i].UpdateFSM(dt);
                caravans[i].UpdateTransform();
            }
            
            minerSpawnTimer += dt;
            if (minerSpawnTimer >= minerSpawnInterval)
            {
                minerSpawnTimer = 0;
                SpawnMiner();
            }
            caravanSpawnTimer += dt;
            if (caravanSpawnTimer >= caravanSpawnInterval)
            {
                caravanSpawnTimer = 0;
                SpawnCaravan();
            }
            mineCheckTimer += dt;
            if (mineCheckTimer >= mineInUseCheckInterval)
            {
                mineCheckTimer = 0;
                CheckMinesInUse();
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
            
            Transform minerBody = Instantiate(minerPrefab).transform;
            minerBody.parent = transform;
            minerBody.position = urbanCenter.position;
            Func<Vector2Int, bool> tryMine = TryMine;
            Func<Vector2Int, bool> tryEat = TryEat;
            miner.Set(pathManager, 0, urbanCenter, urbanCenter, minerBody, tryMine, tryEat);
            
            miners.Add(miner);
        }
        void SpawnCaravan()
        {
            if(caravans.Count >= maxCaravans) return;
            
            Caravan.ACaravan caravan = new Caravan.ACaravan();

            caravan.moveSpeed = caravanTemplate.moveSpeed;
            caravan.loadDuration = caravanTemplate.loadDuration;
            caravan.depositDuration = caravanTemplate.depositDuration;
            caravan.onDepositSuccess += OnFoodDeposited;
            
            Transform caravanBody = Instantiate(caravanPrefab).transform;
            caravanBody.parent = transform;
            caravanBody.position = urbanCenter.position;
            caravan.Set(pathManager, 1, urbanCenter, urbanCenter, caravanBody);
            
            caravans.Add(caravan);
        }
        bool TryMine(Vector2Int minePos)
        {
            Mine mine;
            
            int mineID = pathManager.GetPathfinder(0).FindPointRegion(minePos);
            if (mineID < 0) return false;
            
            if (minesByID.TryGetValue(mineID, out mine))
            {
                lock (mine)
                    mine.minerals--;
                
                if (mine.minerals <= 0)
                {
                    lock (minesByID)
                        minesByID.Remove(mineID);
                    
                    lock (mine)
                        mine.t.gameObject.SetActive(false); //this needs to change

                    lock (pathManager)
                        pathManager.RemovePointOfInterest(mineID, 0);
                    
                    for (int i = 0; i < miners.Count; i++)
                        miners[i].OnMineEmpty(minePos);
                    
                    if(minesByID.Count <= 0)
                    {
                        for (int i = 0; i < miners.Count; i++)
                            miners[i].OnNoMoreMines();
                        for (int i = 0; i < caravans.Count; i++)
                            caravans[i].OnNoMoreMines();
                    }
                }
                
                return mine.minerals >= 0;
            }
            
            return false;
        }
        bool TryEat(Vector2Int foodStoragePos)
        {
            Mine mine;
            
            int mineID = pathManager.GetPathfinder(0).FindPointRegion(foodStoragePos);
            if (mineID < 0) return false;
            
            //If founds mine and has food, eat 1 and return true
            if (minesByID.TryGetValue(mineID, out mine))
                if (mine.food > 0)
                {
                    lock (mine)
                        mine.food--;
                    
                    return true;
                }

            //If either not found or has no food, return false
            return false;
        }
        void CheckMinesInUse()
        {
            ConcurrentBag<Vector2Int> mines = new ConcurrentBag<Vector2Int>();
            
            Parallel.ForEach(miners, miner =>
            {
              if (!miner.hasMine) return;
            
              Vector2Int minePos = miner.minePos;
            
              if (mines.Contains(minePos)) return;

              lock (mines)
                  mines.Add(minePos);
            });
            
            ConcurrentBag<int> regions = new ConcurrentBag<int>();
            Parallel.ForEach(mines, minePos =>
            {
                int region;
            
                region = pathManager.GetPathfinder(0).FindPointRegion(minePos);
            
                if(region < 0) return;
            
                lock (regions)
                    regions.Add(region);
            });
            
            pathManager.GetPathfinder(1).UpdatePointsOfInterest(regions.ToList());

            for (int i = 0; i < caravans.Count; i++)
                caravans[i].OnMapUpdated();
            // Parallel.ForEach(caravans, caravan =>
            // { caravan.OnMapUpdated(); });
        }
        
        //Event Receivers
        void OnFoodDeposited(Vector2Int gridPos)
        {
            Mine mine;
            
            int mineID = pathManager.GetPathfinder(1).FindPointRegion(gridPos);
            if (mineID < 0) return;
            
            if (!minesByID.TryGetValue(mineID, out mine)) return;

            lock (mine)
                mine.food += 10; //hardcoded food amount
        }
    }
}