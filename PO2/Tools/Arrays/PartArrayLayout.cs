using System;
using UnityEngine;
using PO2.Core;
using PO2.Localization;

namespace PO2.Tools.Arrays
{
    internal enum PartArrayMode
    {
        FixedGap,
        FitEndpoint,
        FixedCount,
    }

    internal readonly struct PartArrayLayout
    {
        internal readonly Vector3 Start, Direction;
        internal readonly Quaternion Rotation;
        internal readonly int Count;
        internal readonly bool CapacityLimited;
        internal readonly double FirstOffset, Stride, Gap;

        private PartArrayLayout(Vector3 start, Vector3 direction, Quaternion rotation, int count, bool limited, double first, double stride, double gap)
        {
            Start = start;
            Direction = direction;
            Rotation = rotation;
            Count = count;
            CapacityLimited = limited;
            FirstOffset = first;
            Stride = stride;
            Gap = gap;
        }

        internal Vector3 Offset(int index) => Direction * (float)(FirstOffset + index * Stride);
        internal Matrix4x4 WorldDelta(int index) => Matrix4x4.Translate(Offset(index)) * Matrix4x4.Translate(Start) * Matrix4x4.Rotate(Rotation) * Matrix4x4.Translate(-Start);

        internal Matrix4x4 LocalTransform(ObjectTransform owner, Vector3 pivot)
        {
            if (Rotation.Equals(Quaternion.identity)) return Matrix4x4.identity;
            Matrix4x4 basis = Matrix4x4.TRS(Vector3.zero, owner.Rotation, owner.Scale);
            return Matrix4x4.Translate(pivot) * basis.inverse * Matrix4x4.Rotate(Rotation) * basis * Matrix4x4.Translate(-pivot);
        }

        internal Vector3 PreviewPosition(ObjectTransform owner, int index) =>
            Start + Rotation * (owner.Position - Start) + Offset(index);

        internal static bool TryCreate(Bounds bounds, Vector3 pivot, ObjectTransform owner, Vector3 endpoint, Quaternion rotation, float gap, PartArrayMode mode, int copies, int capacity, out PartArrayLayout layout, out LocalizedText reason)
        {
            layout = default;
            reason = LocalizedText.Empty;
            if (!Finite(endpoint) || !Finite(pivot) || !Finite(bounds.center) || !Finite(bounds.extents) || !Finite(owner.Position) || !Finite(owner.Scale) || !Finite(gap) || gap < 0 || capacity < 0 || mode < PartArrayMode.FixedGap || mode > PartArrayMode.FixedCount || Math.Abs(owner.Scale.x) < 0.000001f || Math.Abs(owner.Scale.y) < 0.000001f || Math.Abs(owner.Scale.z) < 0.000001f)
            {
                reason = LocalizedText.For("PO2.PartArray.Error.InvalidTransform");
                return false;
            }

            double magnitude = (double)rotation.x * rotation.x + (double)rotation.y * rotation.y + (double)rotation.z * rotation.z + (double)rotation.w * rotation.w;
            if (double.IsNaN(magnitude) || double.IsInfinity(magnitude) || magnitude < 1e-12)
            {
                reason = LocalizedText.For("PO2.PartArray.Error.InvalidRotation");
                return false;
            }

            float inverse = (float)(1 / Math.Sqrt(magnitude));
            rotation = new Quaternion(rotation.x * inverse, rotation.y * inverse, rotation.z * inverse, rotation.w * inverse);
            Vector3 start = owner.TransformPoint(pivot);
            Vector3 delta = endpoint - start;
            double length = Math.Sqrt((double)delta.x * delta.x + (double)delta.y * delta.y + (double)delta.z * delta.z);
            if (length < 0.00001 || double.IsNaN(length) || double.IsInfinity(length))
            {
                reason = LocalizedText.For("PO2.PartArray.Line.Error.ZeroLength");
                return false;
            }

            Vector3 direction = delta / (float)length;
            // Count mode spaces pivots along the line, independently of the part's rotated bounds.
            if (mode == PartArrayMode.FixedCount)
            {
                int requestedCopies = Math.Max(1, copies);
                int fixedCount = Math.Min(capacity, requestedCopies);
                double spacing = fixedCount > 0 ? length / fixedCount : 0;
                layout = new PartArrayLayout(start, direction, rotation, fixedCount, requestedCopies > capacity, spacing, spacing, 0);
                return true;
            }

            double rear = double.PositiveInfinity, front = double.NegativeInfinity;
            Vector3 min = bounds.min, max = bounds.max;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                Vector3 relative = owner.TransformPoint(corner) - start;
                double projected = Dot(rotation * relative, direction);
                rear = Math.Min(rear, projected);
                front = Math.Max(front, projected);
            }

            double width = front - rear, stride = width + gap;
            if (double.IsNaN(stride) || double.IsInfinity(stride) || stride < 0.000001)
            {
                reason = LocalizedText.For("PO2.PartArray.Error.ZeroSpacing");
                return false;
            }

            double available = length - front;
            // The tolerance is a distance, so it does not grow with copy count.
            double requested = Math.Max(0, Math.Floor((available + 0.00001) / stride));
            int count = (int)Math.Min(capacity, requested);
            double effectiveGap = mode == PartArrayMode.FitEndpoint && count > 0 ? Math.Max(gap, available / count - width) : gap;
            layout = new PartArrayLayout(start, direction, rotation, count, requested > capacity, width + effectiveGap, width + effectiveGap, effectiveGap);
            return true;
        }

        private static double Dot(Vector3 a, Vector3 b) => (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
        internal static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        internal static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }
}