namespace Universal.FileManaging
{
    [System.Serializable]
    public struct Vec2Int
    {
        public int x;
        public int y;


        public Vec2Int(int _x, int _y)
        {
            x = _x;
            y = _y;
        }
        
        public static implicit operator UnityEngine.Vector2Int(Vec2Int vec)
        {
            return new UnityEngine.Vector2Int(vec.x, vec.y);
        }
        public static implicit operator Vec2Int(UnityEngine.Vector2Int vec)
        {
            return new Vec2Int(vec.x, vec.y);
        }
    }
    
    [System.Serializable]
    public struct Vec3
    {
        public float x;
        public float y;
        public float z;
        
        public Vec3(float _x, float _y, float _z)
        {
            x = _x;
            y = _y;
            z = _z;
        }
        
        public static implicit operator UnityEngine.Vector3(Vec3 vec)
        {
            return new UnityEngine.Vector3(vec.x, vec.y, vec.z);
        }
        public static implicit operator Vec3(UnityEngine.Vector3 vec)
        {
            return new Vec3(vec.x, vec.y, vec.z);
        }
    }
}