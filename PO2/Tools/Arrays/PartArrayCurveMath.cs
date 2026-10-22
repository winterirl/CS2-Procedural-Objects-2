using System;
using UnityEngine;

namespace PO2.Tools.Arrays
{
    // Keep path calculations in doubles. Only the finished placements go back
    // to Unity floats, so small gaps don't disappear while measuring a big PO.
    internal readonly struct ArrayPoint
    {
        internal readonly double X, Y, Z;

        internal ArrayPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal ArrayPoint(Vector3 v) : this(v.x, v.y, v.z)
        {
        }

        internal double Squared => X * X + Y * Y + Z * Z;
        internal double Length => Math.Sqrt(Squared);
        internal Vector3 Vector => new Vector3((float)X, (float)Y, (float)Z);
        internal static double Dot(ArrayPoint a, ArrayPoint b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static ArrayPoint operator +(ArrayPoint a, ArrayPoint b) => new ArrayPoint(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static ArrayPoint operator -(ArrayPoint a, ArrayPoint b) => new ArrayPoint(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static ArrayPoint operator *(ArrayPoint a, double scale) => new ArrayPoint(a.X * scale, a.Y * scale, a.Z * scale);
        internal static ArrayPoint Lerp(ArrayPoint a, ArrayPoint b, double t) => a + (b - a) * t;
    }

    internal static class PartArrayCurveMath
    {
        internal static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        internal static bool TryRotation(Quaternion value, out Quaternion rotation)
        {
            double length = Math.Sqrt((double)value.x * value.x + (double)value.y * value.y + (double)value.z * value.z + (double)value.w * value.w);
            rotation = Quaternion.identity;
            if (!Finite(length) || length < 1e-12) return false;
            rotation = new Quaternion((float)(value.x / length), (float)(value.y / length), (float)(value.z / length), (float)(value.w / length));
            return true;
        }

        internal static Vector3 Unit(Vector3 value, Vector3 fallback)
        {
            double length = new ArrayPoint(value).Length;
            return length > 1e-12 && Finite(length) ? (new ArrayPoint(value) * (1 / length)).Vector : fallback;
        }

        internal static Vector3 Up(Vector3 forward, Vector3 up)
        {
            up = Unit(up, Vector3.up);
            up -= forward * Vector3.Dot(forward, up);
            if (up.sqrMagnitude < 1e-10f)
            {
                up = Math.Abs(forward.y) < .9f ? Vector3.up : Vector3.right;
                up -= forward * Vector3.Dot(forward, up);
            }

            return Unit(up, Vector3.up);
        }

        internal static Quaternion Frame(Vector3 forward, Vector3 up) => Quaternion.LookRotation(forward, Up(forward, up));

        internal static Vector3 TransportUp(Vector3 previous, Vector3 next, Vector3 up)
        {
            float dot = Math.Max(-1, Math.Min(1, Vector3.Dot(previous, next)));
            Quaternion turn;
            if (dot < -.99999f)
            {
                // At an exact reversal there isn't a unique shortest turn.
                // Retain the previous up axis instead of choosing a random roll.
                turn = Quaternion.AngleAxis(180, Up(previous, up));
            }
            else
            {
                Vector3 cross = Vector3.Cross(previous, next);
                TryRotation(new Quaternion(cross.x, cross.y, cross.z, 1 + dot), out turn);
            }

            return Up(next, turn * up);
        }
    }
}