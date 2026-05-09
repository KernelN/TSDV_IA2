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

        string dataRoot;

        LayerData[] layerData;
        bool shouldLoadSavedData;
        List<Mine> mines = new List<Mine>();
        Dictionary<int, HashSet<Vector2Int>> queuedTerrainChangesByLayer = new Dictionary<int, HashSet<Vector2Int>>();
        bool generationFailed;
        [Header("DEBUG")]
        [SerializeField, Min(0)] int gizmosIndex;

        public System.Action<int> OnVoronoiLayerCommitted;
        public System.Action OnWholeMapRegen;

        //Unity Events
        void Awake()
        {
            // Save files live in Unity's persistent data folder.
            // If loading fails for any reason, the map is generated from scratch below.
            dataRoot = System.IO.Path.Combine(Application.persistentDataPath);
            shouldLoadSavedData = useSavedData && TryLoadAndValidateData();

            // Each grid is either restored from compatible saved cells or rebuilt from the scene.
            // The pathfinder will always rebuild Voronoi data from current mines after this.
            for (int i = 0; i < grids.Length; i++)
            {
                if (shouldLoadSavedData)
                    grids[i].Set(gridTransform, gridWorldSize, layerData[i].grid);
                else
                    grids[i].Set(gridTransform, gridWorldSize);
            }

            // Mines must exist before pathfinders are initialized because mines become Voronoi sites.
            BuildMines(shouldLoadSavedData);

            // Saved region/cost data no longer exists.
            // Every layer creates its on-demand Voronoi from the current grid and mines.
            for (int i = 0; i < pathfinders.Length; i++)
            {
                pathfinders[i].SetPointsOfInterest(BuildPointsOfInterestFromMines());
                pathfinders[i].Set(grids[i]);
            }
        }
        void Start()
        {
            // Saving is optional because the scene can always rebuild its grid and Voronoi data.
            if (!saveData)
                return;

            for (int i = 0; i < grids.Length; i++)
            {
                string dataPath = System.IO.Path.Combine(dataRoot, "_GridLayer_" + i + ".bin");

                // Only durable map and mine data is saved.
                // Voronoi regions are intentionally recalculated at runtime on demand.
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

                FileManager<LayerData>.SaveDataToFile(newData, dataPath);
            }
        }
        void OnDrawGizmos()
        {
            // Walkability and terrain weight
            if (grids.Length > 0)
                grids[gizmosIndex].DrawGizmos(gridTransform, gridWorldSize);

            // Voronoi regions
            if (pathfinders.Length > 0)
                pathfinders[gizmosIndex].DrawGizmos();
        }
        void OnValidate()
        {
            // Keep the debug index inside the serialized array bounds.
            // This prevents editor gizmo calls from indexing outside the configured layers.
            if(gizmosIndex >= grids.Length)
                gizmosIndex = grids.Length - 1;
        }

        //Methods
        /// <summary>
        /// Tag all cells for a rescan (to confirm whether they need recalculation or not)
        /// </summary>
        public void QueueAllTerrainCellsForRescan()
        {
            // Every layer gets its own set so duplicate cells are naturally merged.
            // This keeps the later refresh pass small and unordered.
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
            // If mine generation failed earlier, the safest recovery is to rebuild all grids and sites.
            // Agents are told about the whole-map refresh after the new pathfinders are ready.
            if (generationFailed)
            {
                for (int i = 0; i < grids.Length; i++) 
                    grids[i].Set(gridTransform, gridWorldSize);

                BuildMines(false);

                for (int i = 0; i < pathfinders.Length; i++)
                {
                    pathfinders[i].SetPointsOfInterest(BuildPointsOfInterestFromMines());
                    pathfinders[i].Set(grids[i]);
                }

                generationFailed = false;
                OnWholeMapRegen?.Invoke();
            }
            
            // No queued cells means no grid data changed.
            // Without changed grid data, there is no Voronoi refresh or reroute event to send.
            if (queuedTerrainChangesByLayer.Count <= 0)
                return;

            foreach (KeyValuePair<int, HashSet<Vector2Int>> entry in queuedTerrainChangesByLayer)
            {
                int layer = entry.Key;
                if (layer < 0 || layer >= grids.Length || layer >= pathfinders.Length)
                    continue;
                
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

                // Terrain changes are already applied to the grid.
                // Voronoi now refreshes synchronously and agents are redirected right away.
                if (pathfinders[layer].RecalculateVoronoiAfterTerrainChange(deltas))
                    OnVoronoiLayerCommitted?.Invoke(layer);
            }

            queuedTerrainChangesByLayer.Clear();
        }
        bool TryLoadAndValidateData()
        {
            // Each layer has its own saved grid file.
            // Any missing, old, or invalid file makes the whole map regenerate cleanly.
            layerData = new LayerData[grids.Length];
            for (int i = 0; i < grids.Length; i++)
            {
                string dataPath = System.IO.Path.Combine(dataRoot, "_GridLayer_" + i + ".bin");
                try
                {
                    layerData[i] = FileManager<LayerData>.LoadDataFromFile(dataPath);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Saved pathfinding/mine data could not be loaded. Recalculating from scratch. " + exception.Message);
                    return false;
                }

                if (!layerData[i].isSetted || layerData[i].grid == null)
                    return false;
            }

            // Even when a file loads, its shape must match the current scene settings.
            // Incompatible saves are ignored so runtime data stays coherent.
            for (int i = 0; i < grids.Length; i++)
                if (!IsSaveCompatibleWithCurrent(layerData[i], grids[i]))
                {
                    Debug.LogWarning("Saved pathfinding/mine data is incompatible with current settings. Recalculating from scratch.");
                    return false;
                }

            return true;
        }
        bool IsSaveCompatibleWithCurrent(LayerData savedData, Grid.PathGrid currentGrid)
        {
            // Grid dimensions and mine settings are the contract for saved map data.
            // If any of these change, old cells and mine ids may point at the wrong places.
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
            // Mine objects are rebuilt as scene objects every time the manager starts.
            // Saved data only decides their positions and ids.
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
            // Random mine placement only uses walkable cells from the first layer.
            // The same mines are then shared as points of interest for all pathfinder layers.
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
                generationFailed = true;
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
            // Saved ids are encoded grid positions.
            // Rebuilding from ids makes saved mines land on the same cells.
            if (mineIDs == null) return;

            for (int i = 0; i < mineIDs.Count; i++)
            {
                Vector2Int gridPos = IdToGridPos(mineIDs[i], grids[0].gridSize.y);
                SpawnMine(gridPos, mineIDs[i]);
            }
        }
        void SpawnMine(Vector2Int gridPos, int forcedId = int.MinValue)
        {
            // Mine spawning only accepts cells that exist on the base grid.
            // Invalid saved ids are ignored instead of creating unreachable POIs.
            if (gridPos.x < 0 || gridPos.x >= grids[0].gridSize.x) return;
            if (gridPos.y < 0 || gridPos.y >= grids[0].gridSize.y) return;

            // Mine ids are stable grid ids unless a saved id is being restored.
            // That keeps save/load and exact POI lookup aligned.
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
            // Pathfinders consume mines through PointOfInterest objects.
            // The same mine transform supplies both world position and stable id.
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
            // The saved grid stores a rectangular array of cells.
            // Cell count checks whether the current node size still creates the same amount.
            int width = Mathf.RoundToInt(gridWorldSize.x / grid.NodeDiameter);
            int height = Mathf.RoundToInt(gridWorldSize.y / grid.NodeDiameter);
            return width * height;
        }
        int GetExpectedGridHeight(Grid.PathGrid grid)
        {
            // Grid ids encode y using the grid height.
            // If this changes, saved mine ids decode to different cells.
            return Mathf.RoundToInt(gridWorldSize.y / grid.NodeDiameter);
        }
        public int GridPosToId(Vector2Int gridPos, int gridHeight)
        {
            // The id flattens a grid coordinate into one stable integer.
            // It is used for saved mines and exact POI lookup.
            return gridPos.x * gridHeight + gridPos.y;
        }
        public Vector2Int IdToGridPos(int id, int gridHeight)
        {
            // This reverses GridPosToId using the same grid height.
            // Saved mine ids become grid coordinates again during load.
            int x = id / gridHeight;
            int y = id % gridHeight;
            return new Vector2Int(x, y);
        }
        public VoronoiAStarPathfinder GetPathfinder(int index)
        {
            // Agents ask the manager for the layer-specific pathfinder they use.
            return pathfinders[index];
        }
        public float GetNodeDiameter(int index)
        {
            // Movement states use node diameter as their arrival threshold.
            return grids[index].NodeDiameter;
        }
        public LayerMask GetUnwalkableMask(int index)
        {
            // Flocking uses the same obstacle mask as the pathfinding grid.
            if (index < 0 || index >= grids.Length)
                return 0;

            return grids[index].unwalkableMask;
        }
        public Vector2Int GetGridPos(Vector3 worldPos, int index)
        {
            // World positions are resolved through the selected layer's grid.
            return grids[index].NodeFromWorldPoint(worldPos).gridPos;
        }
        public void RemovePointOfInterest(Vector2Int gridPos, int layer)
        {
            // Removing by cell first asks that layer which active region owns the cell.
            // The id-based removal then updates every pathfinder layer.
            int poiIndex;
            poiIndex = pathfinders[layer].FindPointRegion(gridPos);

            RemovePointOfInterest(poiIndex, layer);
        }
        public void RemovePointOfInterest(int id, int layer)
        {
            // A depleted mine must disappear from every pathfinder layer.
            // Each layer that changed sends an immediate reroute event to its agents.
            for (int i = 0; i < pathfinders.Length; i++)
            {
                if (pathfinders[i].RemovePointOfInterest(id))
                    OnVoronoiLayerCommitted?.Invoke(i);
            }
        }
        public void UpdatePointsOfInterest(List<int> pointIds, int layer)
        {
            // Some layers only use a subset of all known mines as active destinations.
            // Updating through the manager keeps the Voronoi refresh and reroute event together.
            if (layer < 0 || layer >= pathfinders.Length)
                return;

            // The pathfinder reports whether its active site set really changed.
            // Agents are redirected only when the closest-site answers may be different.
            if (pathfinders[layer].UpdatePointsOfInterest(pointIds))
                OnVoronoiLayerCommitted?.Invoke(layer);
        }
        public MineSettings GetMineSettings()
        {
            // AgentManager copies these values into runtime mine state.
            return mineSettings;
        }
        public List<Mine> GetRuntimeMines()
        {
            // The runtime mine list is shared so AgentManager can mirror mine state.
            return mines;
        }
    }
}


