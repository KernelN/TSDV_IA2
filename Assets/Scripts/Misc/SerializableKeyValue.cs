namespace Universal.FileManaging
{
    [System.Serializable]
    public class SerializableKeyValue<TKey, TValue>
    {
        public TKey key;
        public TValue value;
    }
}