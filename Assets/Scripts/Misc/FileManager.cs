using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

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
        public static T LoadDataFromFile(string dataPath)
        {
            // Missing files are treated as empty data.
            // The caller can decide whether default data should trigger a regeneration.
            if (!File.Exists(dataPath))
            {
                UnityEngine.Debug.LogError("Data file not found at path: " + dataPath);
                return default;
            }

            // The file stream is wrapped in using so invalid or outdated binary data
            // cannot leave the save file open after deserialization fails.
            BinaryFormatter bf = new BinaryFormatter();
            using (FileStream file = File.Open(dataPath, FileMode.Open))
            {
                return (T)bf.Deserialize(file);
            }
        }
        public static void DeleteFile(string dataPath)
        {
            if (!File.Exists(dataPath)) { return; }

            File.Delete(dataPath);
        }
    }
}
