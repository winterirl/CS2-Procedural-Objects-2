using System;
namespace UnityEngine
{
    public partial struct Vector2
    {
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator *(Vector2 a, float b) => new Vector2(a.x * b, a.y * b);
        public static Vector2 operator /(Vector2 a, float b) => new Vector2(a.x / b, a.y / b);
    }
    public partial struct Vector3
    {
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object obj) => obj is Vector3 b && x == b.x && y == b.y && z == b.z;
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public void Normalize() { this = normalized; }
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 operator /(Vector3 a, float b) => a * (1f / b);
        public static Vector3 operator -(Vector3 a) => a * -1f;
    }
    public partial struct Vector4
    {
        public static implicit operator Vector2(Vector4 a) => new Vector2(a.x, a.y);
    }
    public static partial class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Sin(float x) => (float)Math.Sin(x);
    }
}
