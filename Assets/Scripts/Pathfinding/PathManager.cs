using System;
using System.Collections.Generic;
using IA.Pathfinding.Voronoi;
using UnityEngine;
using Universal.FileManaging;
using Random = UnityEngine.Random;

namespace IA.Pathfinding
{
    [Serializable]
    public struct MineSettings
    {
        public int minerals;
        public int initialFood;
    }

    [Serializable]
    public struct TerrainCellChange
    {
        public Vector2Int gridPos;
        public int layerIndex;

        public TerrainCellChange(Vector2Int gridPos, int layerIndex)
        {
            this.gridPos = gridPos;
            this.layerIndex = layerIndex;
        }
    }

    [Serializable]
    public struct LayerData
    {
        //Grid
        public Grid.PathNode[,] grid;

        //Pathfinder
        public List<SerializableKeyValue<Vec2Int, List<SerializableKeyValue<int, float>>>>
            regionsCostByNode;

        //Mine data
        public List<int> mineIDs;
        public MineSettings mineSettings;
        public int mineCount;

        //Validation data
        public int cellCount;
        public int gridHeight;
        public int layerCount;

        //LayerData values
        public bool isSetted;

        public Dictionary<Vector2Int, Dictionary<int, float>> GetDictionary()
        {
            Dictionary<Vector2Int, Dictionary<int, float>> dictionary;
            dictionary = new Dictionary<Vector2Int, Dictionary<int, float>>();

            if (regionsCostByNode == null) return dictionary;

            for (int i = 0; i < regionsCostByNode.Count; i++)
            {
                SerializableKeyValue<Vec2Int, List<SerializableKeyValue<int, float>>> costsByPos;
                costsByPos = regionsCostByNode[i];

                Dictionary<int, float> costs;
                costs = new Dictionary<int, float>();
                for (int j = 0; j < costsByPos.value.Count; j++)
                {
                    costs.TryAdd(costsByPos.value[j].key, costsByPos.value[j].value);
                }

                dictionary.TryAdd(costsByPos.key, costs);
            }

            return dictionary;
        }

        public void SetDictionary(Dictionary<Vector2Int, Dictionary<int, float>> dictionary)
        {
            regionsCostByNode = new List<SerializableKeyValue<Vec2Int,
                List<SerializableKeyValue<int, float>>>>();

            if (dictionary == null) return;

            //Get all keys
            foreach (Vector2Int key in dictionary.Keys)
            {
                SerializableKeyValue<Vec2Int, List<SerializableKeyValue<int, float>>> costsByPos;
                costsByPos = new SerializableKeyValue<Vec2Int, List<SerializableKeyValue<int, float>>>();
                costsByPos.key = key;
                costsByPos.value = new List<SerializableKeyValue<int, float>>();
                regionsCostByNode.Add(costsByPos);
            }

            //Get all values
            for (int i = 0; i < regionsCostByNode.Count; i++)
            {
                if (!dictionary.TryGetValue(regionsCostByNode[i].key, out var costs))
                {
                    Debug.LogError("Key not found: " + regionsCostByNode[i].key + " ID: " + i);
                    continue;
                }

                foreach (int IDs in costs.Keys)
                {
                    SerializableKeyValue<int, float> costsByID;
                    costsByID = new SerializableKeyValue<int, float>();
                    costsByID.key = IDs;
                    costs.TryGetValue(costsByID.key, out costsByID.value);
                    regionsCostByNode[i].value.Add(costsByID);
                }
            }
        }
    }

    public class PathManager : MonoBehaviour
    {
        [Serializable]
        public class Mine
        {
            public int id;
            public Vector2Int gridPos;
            public Transform transform;
        }

        [Header("Set Values")]
        [SerializeField] Transform gridTransform;
        [SerializeField] Vector2Int gridWorldSize;
        [SerializeField] Grid.PathGrid[] grids;
        [SerializeField] VoronoiAStarPathfinder[] pathfinders;
        [SerializeField] bool useSavedData;
        [SerializeField] bool saveData;
        [Header("Mine Generation")]
        [SerializeField] GameObject minePrefab;
        [SerializeField, Min(1)] int mineCount = 5;
        [SerializeField] MineSettings mineSettings;
        [Header("Terrain Recalculation")]
        [SerializeField, Min(0f)] float terrainRecalcMaxDuration = 0.1f;

        string dataRoot;

        LayerData[] layerData;
        bool shouldLoadSavedData;
        List<Mine> mines = new List<Mine>();
        readonly Dictionary<int, HashSet<Vector2Int>> queuedTerrainChangesByLayer = new Dictionary<int, HashSet<Vector2Int>>();

        [Header("DEBUG")]
        [SerializeField, Min(0)] int gizmosIndex;

        public event Action<int> OnVoronoiLayerCommitted;

        //Unity Events
        void Awake()
        {
            dataRoot = System.IO.Path.Combine(Application.persistentDataPath);
            shouldLoadSavedData = useSavedData && TryLoadAndValidateData();

            for (int i = 0; i < grids.Length; i++)
            {
                if (shouldLoadSavedData)
                    grids[i].Set(gridTransform, gridWorldSize, layerData[i].grid);
                else
                    grids[i].Set(gridTransform, gridWorldSize);
            }

            BuildMines(shouldLoadSavedData);

            for (int i = 0; i < pathfinders.Length; i++)
            {
                pathfinders[i].SetPointsOfInterest(BuildPointsOfInterestFromMines());

                if (shouldLoadSavedData)
                    pathfinders[i].Load(grids[i], layerData[i].GetDictionary());
                else
                    pathfinders[i].Set(grids[i]);
            }
        }
        void Start()
        {
            //Try to save data on start, if it needs to
            if (!saveData) return;

            for (int i = 0; i < grids.Length; i++)
            {
                string dataPath = System.IO.Path.Combine(dataRoot, "_GridLayer_" + i + ".bin");

                LayerData newData = new LayerData();
                newData.isSetted = true;
                newData.grid = grids[i].grid;
                newData.cellCount = grids[i].gridSize.x * grids[i].gridSize.y;
                newData.gridHeight = grids[i].gridSize.y;
                newData.layerCount = grids.Length;
                newData.mineCount = mines.Count;
                newData.mineSettings = mineSettings;
                newData.mineIDs = new List<int>(mines.Count);
                for (int j = 0; j < mines.Count; j++)
                    newData.mineIDs.Add(mines[j].id);

                newData.SetDictionary(pathfinders[i].GetRegionsCostByNode());

                FileManager<LayerData>.SaveDataToFile(newData, dataPath);
            }
        }
        void Update()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < pathfinders.Length; i++)
            {
                pathfinders[i].TickTerrainRecalculation(now);
                if (pathfinders[i].CommitPendingTerrainVoronoi())
                    OnVoronoiLayerCommitted?.Invoke(i);
            }
        }
        void OnDrawGizmos()
        {
            if (grids.Length > 0)
                grids[gizmosIndex].DrawGizmos(gridTransform, gridWorldSize);

            if (pathfinders.Length > 0)
                pathfinders[gizmosIndex].DrawGizmos();
        }
        void OnValidate()
        {
            if(gizmosIndex >= grids.Length)
                gizmosIndex = grids.Length - 1;
        }

        //Methods
        /// <summary>
        /// Tag all cells for a rescan (to confirm whether they need recalculation or not)
        /// </summary>
        public void QueueAllTerrainCellsForRescan()
        {
            for (int layer = 0; layer < grids.Length; layer++)
            {
                //Make sure all layers have a queue
                if (!queuedTerrainChangesByLayer.TryGetValue(layer, out HashSet<Vector2Int> queuedCells))
                {
                    queuedCells = new HashSet<Vector2Int>();
                    queuedTerrainChangesByLayer[layer] = queuedCells;
                }

                //Add all cells to queue
                for (int x = 0; x < grids[layer].gridSize.x; x++)
                for (int y = 0; y < grids[layer].gridSize.y; y++)
                    queuedCells.Add(new Vector2Int(x, y));
            }
        }
        public void SetCellsForRecalculation()
        {
            if (queuedTerrainChangesByLayer.Count <= 0)
                return;

            foreach (KeyValuePair<int, HashSet<Vector2Int>> entry in queuedTerrainChangesByLayer)
            {
                int layer = entry.Key;
                
                //Use hash set as it's much faster than list for an unordered collection
                //( O(1) vs O(n) )
                HashSet<Vector2Int> queuedCells = entry.Value;
                if (queuedCells == null || queuedCells.Count <= 0)
                    continue;

                //Get deltas off all nodes that changed
                List<Grid.NodeTerrainDelta> deltas = grids[layer].RefreshCells(queuedCells);
                queuedCells.Clear();

                if (deltas.Count <= 0)
                    continue;

                pathfinders[layer].RequestTerrainRecalculation(deltas, terrainRecalcMaxDuration);
            }

            queuedTerrainChangesByLayer.Clear();
        }
        bool TryLoadAndValidateData()
        {
            layerData = new LayerData[grids.Length];
            for (int i = 0; i < grids.Length; i++)
            {
                string dataPath = System.IO.Path.Combine(dataRoot, "_GridLayer_" + i + ".bin");
                layerData[i] = FileManager<LayerData>.LoadDataFromFile(dataPath);

                if (!layerData[i].isSetted)
                    return false;
            }

            for (int i = 0; i < grids.Length; i++)
            {
                if (!IsCompatibleWithCurrentConfig(layerData[i], grids[i]))
                {
                    Debug.LogWarning("Saved pathfinding/mine data is incompatible with current settings. Recalculating from scratch.");
                    return false;
                }
            }

            return true;
        }
        bool IsCompatibleWithCurrentConfig(LayerData savedData, Grid.PathGrid currentGrid)
        {
            int expectedCellCount = GetExpectedCellCount(currentGrid);
            int expectedGridHeight = GetExpectedGridHeight(currentGrid);

            if (savedData.cellCount != expectedCellCount) return false;
            if (savedData.gridHeight != expectedGridHeight) return false;
            if (savedData.layerCount != grids.Length) return false;
            if (savedData.mineCount != mineCount) return false;
            if (savedData.mineSettings.minerals != mineSettings.minerals) return false;
            if (savedData.mineSettings.initialFood != mineSettings.initialFood) return false;
            if (savedData.mineIDs == null || savedData.mineIDs.Count != mineCount) return false;

            return true;
        }
        void BuildMines(bool fromSavedData)
        {
            mines.Clear();

            if (minePrefab == null)
            {
                Debug.LogWarning("Mine prefab is null. Mine generation skipped.");
                return;
            }

            if (fromSavedData)
            {
                InstantiateMinesFromIDs(layerData[0].mineIDs);
                return;
            }

            GenerateRandomMines();
        }
        void GenerateRandomMines()
        {
            List<Vector2Int> validPositions = new List<Vector2Int>();
            Grid.PathNode[,] gridNodes = grids[0].grid;
            for (int x = 0; x < grids[0].gridSize.x; x++)
            {
                for (int y = 0; y < grids[0].gridSize.y; y++)
                {
                    if (gridNodes[x, y].walkable)
                        validPositions.Add(new Vector2Int(x, y));
                }
            }

            if (validPositions.Count == 0)
            {
                Debug.LogWarning("No walkable cells available for mine generation.");
                return;
            }

            int spawnCount = Mathf.Min(mineCount, validPositions.Count);
            if (spawnCount < mineCount)
                Debug.LogWarning("Mine count exceeds walkable node count. Clamping generated mines to walkable nodes.");

            for (int i = 0; i < spawnCount; i++)
            {
                int randomIndex = Random.Range(0, validPositions.Count);
                Vector2Int gridPos = validPositions[randomIndex];
                validPositions.RemoveAt(randomIndex);

                SpawnMine(gridPos);
            }
        }
        void InstantiateMinesFromIDs(List<int> mineIDs)
        {
            if (mineIDs == null) return;

            for (int i = 0; i < mineIDs.Count; i++)
            {
                Vector2Int gridPos = IdToGridPos(mineIDs[i], grids[0].gridSize.y);
                SpawnMine(gridPos, mineIDs[i]);
            }
        }
        void SpawnMine(Vector2Int gridPos, int forcedId = int.MinValue)
        {
            if (gridPos.x < 0 || gridPos.x >= grids[0].gridSize.x) return;
            if (gridPos.y < 0 || gridPos.y >= grids[0].gridSize.y) return;

            int id = forcedId == int.MinValue ? GridPosToId(gridPos, grids[0].gridSize.y) : forcedId;
            Transform mineTransform = Instantiate(minePrefab).transform;
            mineTransform.parent = transform;
            mineTransform.position = grids[0].grid[gridPos.x, gridPos.y].worldPos;

            mines.Add(new Mine
            {
                id = id,
                gridPos = gridPos,
                transform = mineTransform
            });
        }
        List<PointOfInterest> BuildPointsOfInterestFromMines()
        {
            List<PointOfInterest> points = new List<PointOfInterest>(mines.Count);
            for (int i = 0; i < mines.Count; i++)
            {
                points.Add(new PointOfInterest
                {
                    id = mines[i].id,
                    gridPos = mines[i].gridPos,
                    t = mines[i].transform
                });
            }

            return points;
        }
        int GetExpectedCellCount(Grid.PathGrid grid)
        {
            int width = Mathf.RoundToInt(gridWorldSize.x / grid.NodeDiameter);
            int height = Mathf.RoundToInt(gridWorldSize.y / grid.NodeDiameter);
            return width * height;
        }
        int GetExpectedGridHeight(Grid.PathGrid grid)
        {
            return Mathf.RoundToInt(gridWorldSize.y / grid.NodeDiameter);
        }
        public int GridPosToId(Vector2Int gridPos, int gridHeight)
        {
            return gridPos.x * gridHeight + gridPos.y;
        }
        public Vector2Int IdToGridPos(int id, int gridHeight)
        {
            int x = id / gridHeight;
            int y = id % gridHeight;
            return new Vector2Int(x, y);
        }
        public VoronoiAStarPathfinder GetPathfinder(int index)
        {
            return pathfinders[index];
        }
        public float GetNodeDiameter(int index)
        {
            return grids[index].NodeDiameter;
        }
        public Vector2Int GetGridPos(Vector3 worldPos, int index)
        {
            return grids[index].NodeFromWorldPoint(worldPos).gridPos;
        }
        public void RemovePointOfInterest(Vector2Int gridPos, int layer)
        {
            int poiIndex;
            poiIndex = pathfinders[layer].FindPointRegion(gridPos);

            RemovePointOfInterest(poiIndex, layer);
        }
        public void RemovePointOfInterest(int id, int layer)
        {
            for (int i = 0; i < pathfinders.Length; i++)
            {
                pathfinders[i].RemovePointOfInterest(id);
            }
        }
        public MineSettings GetMineSettings()
        {
            return mineSettings;
        }
        public List<Mine> GetRuntimeMines()
        {
            return mines;
        }
    }
}

