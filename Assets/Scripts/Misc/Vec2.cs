using System;
using UnityEngine;

namespace IA.Math
{
    [System.Serializable]
    public struct Vec2
    {
        public int x;
        public int y;

        #region Constructors
        public Vec2(int x, int y) { this.x = x; this.y = y; }
        #endregion

        #region Operators
        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.x + b.x, a.y + b.y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.x - b.x, a.y - b.y); }
        public static implicit operator Vector2(Vec2 a) { return new Vector2(a.x, a.y); }
        public static implicit operator Vec2(Vector2 a) { return new Vec2((int)a.x, (int)a.y); }
        public static bool operator ==(Vec2 a, Vec2 b) { return a.x == b.x && a.y == b.y; }
        public static bool operator !=(Vec2 a, Vec2 b) { return a.x != b.x || a.y != b.y; }
        
        
        public bool Equals(Vec2 other)
        {
            return x == other.x && y == other.y;
        }
        public override bool Equals(object obj)
        {
            return obj is Vec2 other && Equals(other);
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(x, y);
        }
        #endregion

        #region Methods
        public float Magnitude() { return Mathf.Sqrt(x * x + y * y); }
        public int SqrMagnitude() { return x * x + y * y; }
        public Vector2 Normalized()
        {
            Vector2 normalized = new Vector2();
            float magnitude = Magnitude();
            normalized.x = x / magnitude;
            normalized.y = y / magnitude;
            return normalized;
        }
        #endregion

        #region Static Methods
        public static float Distance(Vec2 a, Vec2 b) { return (a - b).Magnitude(); }
        #endregion
    }
}