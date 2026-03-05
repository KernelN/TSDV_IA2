using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IA.FSM.Flocking;
using UnityEngine;

namespace IA.FSM
{
    public class AgentManager : MonoBehaviour
    {
        [Serializable]
        class Mine
        {
            [Header("Runtime Values")]
            public Transform t;
            public Vector2Int gridPos;
            public int id;
            public int minerals;
            public int food;
            public bool isActive = true;
        }

        [Header("General Settings")]
        [SerializeField] Pathfinding.PathManager pathManager;
        [SerializeField] Transform urbanCenter;
        [SerializeField] float mineInUseCheckInterval;
        [Header("Miner Settings")]
        [SerializeField] Miner.AMiner minerTemplate;
        [SerializeField] GameObject minerPrefab;
        [Header("Caravan Settings")]
        [SerializeField] Caravan.ACaravan caravanTemplate;
        [SerializeField] GameObject caravanPrefab;
        [Header("Flocking Settings")]
        [SerializeField] AgentFlockingSettings minerFlockingSettings = AgentFlockingSettings.CreateDefaultMiner();
        [SerializeField] AgentFlockingSettings caravanFlockingSettings = AgentFlockingSettings.CreateDefaultCaravan();
        [SerializeField, Min(8)] int flockingObstacleBufferSize = 64;

        //[Header("Runtime Values")]
        List<Miner.AMiner> miners;
        List<Caravan.ACaravan> caravans;
        List<Mine> mines;
        float mineCheckTimer;
        Dictionary<int, Mine> minesByID;
        bool isOnEmergency;
        Collider[] minerObstacleBuffer;
        Collider[] caravanObstacleBuffer;

        readonly List<AgentFlockingSnapshot> minerFlockingSnapshots = new List<AgentFlockingSnapshot>();
        readonly List<AgentFlockingSnapshot> caravanFlockingSnapshots = new List<AgentFlockingSnapshot>();
        readonly List<Vector3> minerFlockingOutput = new List<Vector3>();
        readonly List<Vector3> caravanFlockingOutput = new List<Vector3>();

        //Unity Events
        void Start()
        {
            miners = new List<Miner.AMiner>();
            caravans = new List<Caravan.ACaravan>();
            mines = new List<Mine>();

            minesByID = new Dictionary<int, Mine>();
            Pathfinding.MineSettings mineSettings = pathManager.GetMineSettings();
            List<Pathfinding.PathManager.Mine> generatedMines = pathManager.GetRuntimeMines();
            for (int i = 0; i < generatedMines.Count; i++)
            {
                Mine mine = new Mine
                {
                    t = generatedMines[i].transform,
                    gridPos = generatedMines[i].gridPos,
                    id = generatedMines[i].id,
                    minerals = mineSettings.minerals,
                    food = mineSettings.initialFood,
                    isActive = true
                };

                mines.Add(mine);
                minesByID.TryAdd(mine.id, mine);
            }

            InitializeFlocking();

            SpawnMiner();
            SpawnCaravan();

            mineCheckTimer = mineInUseCheckInterval - 1;
            pathManager.OnVoronoiLayerCommitted += OnVoronoiLayerCommitted;
        }
        void Update()
        {
            float dt = Time.deltaTime;

            Parallel.ForEach(miners, miner =>
            {
                lock (miner) miner.UpdateFSM(dt);
            });
            Parallel.ForEach(caravans, caravan =>
            {
                lock (caravan) caravan.UpdateFSM(dt);
            });

            ApplyFlockingToMiners();
            ApplyFlockingToCaravans();

            for (int i = 0; i < miners.Count; i++)
                miners[i].UpdateTransform();
            for (int i = 0; i < caravans.Count; i++)
                caravans[i].UpdateTransform();

            //Update Mines
            for (int i = 0; i < mines.Count; i++)
                if (mines[i].isActive != mines[i].t.gameObject.activeSelf)
                    mines[i].t.gameObject.SetActive(mines[i].isActive);

            mineCheckTimer += dt;
            if (mineCheckTimer >= mineInUseCheckInterval)
            {
                mineCheckTimer = 0;
                CheckMinesInUse();
            }
        }
        void OnDestroy()
        {
            if (pathManager != null)
                pathManager.OnVoronoiLayerCommitted -= OnVoronoiLayerCommitted;
        }

        //Methods
        void InitializeFlocking()
        {
            minerFlockingSettings = EnsureFlockingDefaults(minerFlockingSettings, AgentFlockingSettings.CreateDefaultMiner());
            caravanFlockingSettings = EnsureFlockingDefaults(caravanFlockingSettings, AgentFlockingSettings.CreateDefaultCaravan());

            int obstacleBufferSize = Mathf.Max(8, flockingObstacleBufferSize);
            minerObstacleBuffer = new Collider[obstacleBufferSize];
            caravanObstacleBuffer = new Collider[obstacleBufferSize];
        }

        static AgentFlockingSettings EnsureFlockingDefaults(AgentFlockingSettings settings, AgentFlockingSettings defaults)
        {
            bool hasCustomValue =
                settings.alignmentDist > 0f || settings.cohesionDist > 0f || settings.separationDist > 0f || settings.obstacleDist > 0f ||
                settings.alignmentMod != 0f || settings.cohesionMod != 0f || settings.separationMod != 0f || settings.obstacleMod != 0f ||
                settings.minSpacing > 0f || settings.maxSteer > 0f || settings.spatialHashCellSize > 0f;

            return hasCustomValue ? settings : defaults;
        }

        void ApplyFlockingToMiners()
        {
            if (pathManager == null || miners.Count <= 0)
                return;

            if (minerObstacleBuffer == null || minerObstacleBuffer.Length <= 0)
                minerObstacleBuffer = new Collider[Mathf.Max(8, flockingObstacleBufferSize)];

            LayerMask obstacleMask = pathManager.GetUnwalkableMask(0);
            minerFlockingSnapshots.Clear();
            for (int i = 0; i < miners.Count; i++)
            {
                lock (miners[i])
                {
                    minerFlockingSnapshots.Add(new AgentFlockingSnapshot(
                        miners[i].CurrentPosition,
                        miners[i].PlannedPosition,
                        miners[i].HasMovementIntent()));
                }
            }

            AgentFlockingSolver.Apply(
                minerFlockingSnapshots,
                minerFlockingSettings,
                obstacleMask,
                minerObstacleBuffer,
                minerFlockingOutput);

            int applyCount = Mathf.Min(miners.Count, minerFlockingOutput.Count);
            for (int i = 0; i < applyCount; i++)
            {
                if (!minerFlockingSnapshots[i].hasMovementIntent)
                    continue;

                lock (miners[i])
                    miners[i].OverridePlannedPosition(minerFlockingOutput[i]);
            }
        }

        void ApplyFlockingToCaravans()
        {
            if (pathManager == null || caravans.Count <= 0)
                return;

            if (caravanObstacleBuffer == null || caravanObstacleBuffer.Length <= 0)
                caravanObstacleBuffer = new Collider[Mathf.Max(8, flockingObstacleBufferSize)];

            LayerMask obstacleMask = pathManager.GetUnwalkableMask(1);
            caravanFlockingSnapshots.Clear();
            for (int i = 0; i < caravans.Count; i++)
            {
                lock (caravans[i])
                {
                    caravanFlockingSnapshots.Add(new AgentFlockingSnapshot(
                        caravans[i].CurrentPosition,
                        caravans[i].PlannedPosition,
                        caravans[i].HasMovementIntent()));
                }
            }

            AgentFlockingSolver.Apply(
                caravanFlockingSnapshots,
                caravanFlockingSettings,
                obstacleMask,
                caravanObstacleBuffer,
                caravanFlockingOutput);

            int applyCount = Mathf.Min(caravans.Count, caravanFlockingOutput.Count);
            for (int i = 0; i < applyCount; i++)
            {
                if (!caravanFlockingSnapshots[i].hasMovementIntent)
                    continue;

                lock (caravans[i])
                    caravans[i].OverridePlannedPosition(caravanFlockingOutput[i]);
            }
        }

        public void SetEmergency()
        {
            isOnEmergency = !isOnEmergency;

            Parallel.ForEach(miners, miner =>
            {
                if (isOnEmergency) lock (miner) miner.Emergency();
                else lock (miner) miner.EmergencyOver();
            });

            Parallel.ForEach(caravans, caravan =>
            {
                if (isOnEmergency) lock (caravan) caravan.Emergency();
                else lock (caravan) caravan.EmergencyOver();
            });
        }

        void OnVoronoiLayerCommitted(int layer)
        {
            if (layer == 0)
            {
                for (int i = 0; i < miners.Count; i++)
                    lock (miners[i])
                        miners[i].OnMapUpdated();

                return;
            }

            if (layer == 1)
            {
                for (int i = 0; i < caravans.Count; i++)
                    lock (caravans[i])
                        caravans[i].OnMapUpdated();
            }
        }

        public void SpawnMiner()
        {
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

        public void SpawnCaravan()
        {
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
            int mineID = pathManager.GetPathfinder(0).GetPointOfInterestID(minePos);
            if (mineID < 0) return false;

            if (minesByID.TryGetValue(mineID, out Mine mine))
            {
                lock (mine)
                    mine.minerals--;

                if (mine.minerals <= 0)
                {
                    lock (minesByID)
                        minesByID.Remove(mineID);

                    lock (mine)
                        mine.isActive = false;

                    lock (pathManager)
                        pathManager.RemovePointOfInterest(mineID, 0);

                    for (int i = 0; i < miners.Count; i++)
                        lock (miners[i])
                            miners[i].OnMineEmpty(minePos);

                    if (minesByID.Count <= 0)
                    {
                        for (int i = 0; i < miners.Count; i++)
                            lock (miners[i])
                                miners[i].OnNoMoreMines();
                        for (int i = 0; i < caravans.Count; i++)
                            lock (caravans[i])
                                caravans[i].OnNoMoreMines();
                    }
                }

                return mine.minerals >= 0;
            }

            return false;
        }

        bool TryEat(Vector2Int foodStoragePos)
        {
            int mineID = pathManager.GetPathfinder(0).GetPointOfInterestID(foodStoragePos);
            if (mineID < 0) return false;

            //If founds mine and has food, eat 1 and return true
            if (minesByID.TryGetValue(mineID, out Mine mine))
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
            ConcurrentBag<Vector2Int> minesInUse = new ConcurrentBag<Vector2Int>();

            Parallel.ForEach(miners, miner =>
            {
                if (!miner.hasMine) return;

                Vector2Int minePos = miner.minePos;

                if (minesInUse.Contains(minePos)) return;

                lock (minesInUse)
                    minesInUse.Add(minePos);
            });

            ConcurrentBag<int> regions = new ConcurrentBag<int>();
            Parallel.ForEach(minesInUse, minePos =>
            {
                int region;

                region = pathManager.GetPathfinder(0).GetPointOfInterestID(minePos);

                if (region < 0) return;

                lock (regions)
                    regions.Add(region);
            });

            pathManager.GetPathfinder(1).UpdatePointsOfInterest(regions.ToList());

            for (int i = 0; i < caravans.Count; i++)
                caravans[i].OnMapUpdated();
        }

        //Event Receivers
        void OnFoodDeposited(Vector2Int gridPos)
        {
            int mineID = pathManager.GetPathfinder(1).GetPointOfInterestID(gridPos);
            if (mineID < 0) return;

            if (!minesByID.TryGetValue(mineID, out Mine mine)) return;

            lock (mine)
                mine.food += 10; //hardcoded food amount
        }
    }
}

