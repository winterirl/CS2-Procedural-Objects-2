using System;
using UnityEngine;
using PO2.Tools.Gizmos;

namespace PO2.Tools.Arrays
{
    // Keep the endpoints in world space. Measure angles clockwise from the reference frame's +Z.
    internal static class PartArrayPathMath
    {
        internal static float NormalizeAngle(float angle)
        {
            float wrapped = angle % 360f;
            if (wrapped < 0) wrapped += 360f;
            // A tiny negative input can round up to exactly 360 in float arithmetic.
            return wrapped >= 360f ? 0 : wrapped;
        }

        internal static float Length(Vector3 delta) =>
            (float)Math.Sqrt((double)delta.x * delta.x + (double)delta.y * delta.y + (double)delta.z * delta.z);

        internal static Vector3 Heading(TransformFrame frame, float angle)
        {
            double radians = angle * (Math.PI / 180);
            return frame.XAxis * (float)Math.Sin(radians) + frame.ZAxis * (float)Math.Cos(radians);
        }

        internal static float Angle(Vector3 delta, TransformFrame frame, float fallback)
        {
            double x = Vector3.Dot(delta, frame.XAxis), z = Vector3.Dot(delta, frame.ZAxis);
            if (x * x + z * z < 1e-10) return NormalizeAngle(fallback);
            return NormalizeAngle((float)(Math.Atan2(x, z) * (180 / Math.PI)));
        }

        internal static Vector3 WithAngle(Vector3 delta, TransformFrame frame, float angle)
        {
            double x = Vector3.Dot(delta, frame.XAxis), z = Vector3.Dot(delta, frame.ZAxis);
            float radius = (float)Math.Sqrt(x * x + z * z);
            return Heading(frame, angle) * radius + frame.YAxis * Vector3.Dot(delta, frame.YAxis);
        }

        internal static Vector3 WithLength(Vector3 delta, TransformFrame frame, float angle, float length)
        {
            float previous = Length(delta);
            return previous > 0.000001f ? delta * (length / previous) : Heading(frame, angle) * length;
        }

        internal static Vector3 SnapDrawing(Vector3 delta, TransformFrame frame, float fallbackAngle, float distanceIncrement, float angleIncrement)
        {
            float angle = Angle(delta, frame, fallbackAngle);
            angle = NormalizeAngle(Mathf.Round(angle / angleIncrement) * angleIncrement);
            float length = Mathf.Round(Length(delta) / distanceIncrement) * distanceIncrement;
            // Snap the line's angle and length, rather than each world coordinate independently.
            return WithLength(WithAngle(delta, frame, angle), frame, angle, length);
        }

        internal static bool SameFrame(TransformFrame a, TransformFrame b) =>
            a.XAxis.Equals(b.XAxis) && a.YAxis.Equals(b.YAxis) && a.ZAxis.Equals(b.ZAxis);
    }
}