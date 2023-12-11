using System;
using System.Collections.Generic;
using UnityEngine;
using Universal.FileManaging;

namespace IA.Pathfinding
{
    [Serializable]
    public struct LayerData
    {
        //Grid
        public Grid.PathNode[,] grid;
        
        //Pathfinder
        public List<SerializableKeyValue<Vec2Int, List<SerializableKeyValue<int, float>>>>
            regionsCostByNode;
        
        //LayerData values
        public bool isSetted;

        public Dictionary<Vector2Int, Dictionary<int, float>> GetDictionary()
        {
            Dictionary<Vector2Int, Dictionary<int, float>> dictionary;
            dictionary = new Dictionary<Vector2Int, Dictionary<int, float>>();
            
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
        [Header("Set Values")] 
        [SerializeField] Transform gridTransform;
        [SerializeField] Vector2Int gridWorldSize;
        [SerializeField] Grid.PathGrid[] grids;
        [SerializeField] Voronoi.VoronoiAStarPathfinder[] pathfinders;
        [SerializeField] bool useSavedData;
        [SerializeField] bool saveData;
        //[Header("Runtime Values")]
        LayerData[] layerData;
        [Header("DEBUG")]
        [SerializeField, Min(0)] int gizmosIndex;
        
        void Awake()
        {
            if (useSavedData)
            {
                layerData = new LayerData[grids.Length];
                for (int i = 0; i < grids.Length; i++)
                {
                    string dataPath = Application.persistentDataPath + "_GridLayer_" + i + ".bin";
                    layerData[i] = FileManager<LayerData>.LoadDataFromFile(dataPath);
                }
            }
            
            for (int i = 0; i < grids.Length; i++)
            {
                if (useSavedData)
                    if (layerData[i].isSetted)
                        grids[i].Set(gridTransform, gridWorldSize, layerData[i].grid);
                    else
                        grids[i].Set(gridTransform, gridWorldSize);
                else
                    grids[i].Set(gridTransform, gridWorldSize);
            }

            for (int i = 0; i < pathfinders.Length; i++)
            {
                if (useSavedData)
                    if (layerData[i].isSetted)
                        pathfinders[i].Load(grids[i], layerData[i].GetDictionary());
                    else 
                        pathfinders[i].Set(grids[i]);
                else
                    pathfinders[i].Set(grids[i]);
            }
        }
        void Start()
        {
            //Try to save data on start, if it needs to
            if(!saveData) return;

            for (int i = 0; i < grids.Length; i++)
            {
                string dataPath = Application.persistentDataPath + "_GridLayer_" + i + ".bin";

                LayerData newData = new LayerData();
                newData.isSetted = true;
                newData.grid = grids[i].grid;
                
                newData.SetDictionary(pathfinders[i].GetRegionsCostByNode());

                FileManager<LayerData>.SaveDataToFile(newData, dataPath);
            }
        }

        void OnDrawGizmos()
        {
            if(grids.Length > 0)
                grids[gizmosIndex].DrawGizmos(gridTransform, gridWorldSize);
            
            if(pathfinders.Length > 0)
                pathfinders[gizmosIndex].DrawGizmos();
        }

        public Voronoi.VoronoiAStarPathfinder GetPathfinder(int index)
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
    }
}