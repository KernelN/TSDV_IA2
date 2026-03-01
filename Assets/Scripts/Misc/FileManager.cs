using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using UnityEngine;

namespace Universal.FileManaging
{
    public static class FileManager<T>
    {
        public static void SaveDataToFile(T objectToSave, string dataPath)
        {
            BinaryFormatter bf = new BinaryFormatter();
            FileStream file = File.Create(dataPath);
            bf.Serialize(file, objectToSave);
            file.Close();
        }
        public static void SaveDataToJson(T objectToSave, string dataPath)
        {
            string json = JsonUtility.ToJson(objectToSave);
            File.WriteAllText(dataPath, json);
        }
        public static T LoadDataFromFile(string dataPath)
        {
            if (!File.Exists(dataPath))
            {
                UnityEngine.Debug.LogError("Data file not found at path: " + dataPath);
                return default;
            }

            BinaryFormatter bf = new BinaryFormatter();
            FileStream file = File.Open(dataPath, FileMode.Open);
            T objectToLoad = (T)bf.Deserialize(file);
            file.Close();

            return objectToLoad;
        }
        public static void DeleteFile(string dataPath)
        {
            if (!File.Exists(dataPath)) { return; }

            File.Delete(dataPath);
        }
    }
}