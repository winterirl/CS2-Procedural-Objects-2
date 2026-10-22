using System;
using UnityEngine;
using PO2.Core;
using PO2.Localization;

namespace PO2.Tools.Arrays
{
    internal readonly struct PartArrayPlacement
    {
        internal readonly Vector3 Pivot;

        // World rotation relative to the captured source, not its owner rotation.
        internal readonly Quaternion Rotation;

        internal PartArrayPlacement(Vector3 pivot, Quaternion rotation)
        {
            Pivot = pivot;
            Rotation = rotation;
        }

        internal Matrix4x4 WorldDelta(Vector3 sourcePivot) => Matrix4x4.Translate(Pivot) * Matrix4x4.Rotate(Rotation) * Matrix4x4.Translate(-sourcePivot);

        internal Matrix4x4 LocalTransform(ObjectTransform owner, Vector3 localPivot)
        {
            // Cancel the owner's world translation before multiplying matrices.
            // Baking a small part far from the map origin otherwise loses detail.
            Vector3 offset = owner.InverseTransformVector(Pivot - owner.TransformPoint(localPivot));
            if (Rotation.Equals(Quaternion.identity)) return Matrix4x4.Translate(offset);
            Matrix4x4 basis = Matrix4x4.TRS(Vector3.zero, owner.Rotation, owner.Scale);
            return Matrix4x4.Translate(localPivot + offset) * basis.inverse * Matrix4x4.Rotate(Rotation) * basis * Matrix4x4.Translate(-localPivot);
        }

        internal Vector3 PreviewPosition(ObjectTransform owner, Vector3 sourcePivot) =>
            Pivot + Rotation * (owner.Position - sourcePivot);
    }

    internal sealed class PartArrayPlacements
    {
        private readonly PartArrayPlacement[] m_BaseCopies;
        private readonly PartArrayPlacement m_BaseSource;
        private readonly Quaternion m_Offset;
        internal int Revision { get; }
        internal int Count => m_BaseCopies.Length;
        internal bool CapacityLimited { get; }
        internal bool EndpointFitFallback { get; }

        internal double Gap { get; }

        // Zero for chord-fitted placement, where there is no uniform pivot spacing.
        internal double Spacing { get; }
        internal bool CanReuseRotation { get; }
        internal PartArrayPlacement Source => Rotate(m_BaseSource);
        internal PartArrayPlacement this[int index] => Rotate(m_BaseCopies[index]);

        // The builder transfers this array. No mutable view escapes the result.
        internal PartArrayPlacements(int revision, PartArrayPlacement source, PartArrayPlacement[] copies, Quaternion offset, bool limited, double gap, double spacing, bool canReuseRotation, bool endpointFitFallback = false)
        {
            Revision = revision;
            m_BaseSource = source;
            m_BaseCopies = copies;
            m_Offset = offset;
            CapacityLimited = limited;
            EndpointFitFallback = endpointFitFallback;
            Gap = gap;
            Spacing = spacing;
            CanReuseRotation = canReuseRotation;
        }

        private PartArrayPlacement Rotate(PartArrayPlacement placement) =>
            new PartArrayPlacement(placement.Pivot, placement.Rotation * m_Offset);

        internal PartArrayPlacements WithRotationOffset(Quaternion offset, int revision)
        {
            if (!CanReuseRotation) throw new LocalizedTextException(LocalizedText.For("PO2.PartArray.Error.RotatedBoundsRequired"));
            return WithOffset(offset, revision);
        }

        // Visual feedback while a new footprint/spacing calculation is running.
        // Keep its old revision: these positions are not the completed new fit.
        internal PartArrayPlacements WithPreviewRotationOffset(Quaternion offset) => WithOffset(offset, Revision);

        private PartArrayPlacements WithOffset(Quaternion offset, int revision)
        {
            if (!PartArrayCurveMath.TryRotation(offset, out offset)) throw new ArgumentOutOfRangeException(nameof(offset));
            return new PartArrayPlacements(revision, m_BaseSource, m_BaseCopies, offset, CapacityLimited, Gap, Spacing, CanReuseRotation, EndpointFitFallback);
        }
    }

    // Measure the source once when the initial direction is chosen. Bending the
    // path must not quietly pick a different pair of ends on the part.
    internal sealed class PartArraySource
    {
        private readonly Vector3[] m_Corners;
        internal Vector3 Start { get; }
        internal Vector3 Forward { get; }
        internal Quaternion ReferenceFrame { get; }
        internal double Rear { get; }
        internal double Front { get; }
        internal double Width => Front - Rear;

        private PartArraySource(Vector3 start, Vector3 forward, Quaternion frame, Vector3[] corners)
        {
            Start = start;
            Forward = forward;
            ReferenceFrame = frame;
            m_Corners = corners;
            Measure(Quaternion.identity, out double rear, out double front);
            Rear = rear;
            Front = front;
        }

        internal void Measure(Quaternion rotation, out double rear, out double front)
        {
            rear = double.PositiveInfinity;
            front = double.NegativeInfinity;
            for (int i = 0; i < m_Corners.Length; i++)
            {
                double d = ArrayPoint.Dot(new ArrayPoint(rotation * m_Corners[i]), new ArrayPoint(Forward));
                rear = Math.Min(rear, d);
                front = Math.Max(front, d);
            }
        }

        internal static bool TryCreate(Bounds bounds, Vector3 pivot, ObjectTransform owner, Vector3 direction, Vector3 up, out PartArraySource source, out LocalizedText reason)
        {
            source = null;
            reason = LocalizedText.Empty;
            if (!PartArrayLayout.Finite(bounds.center) || !PartArrayLayout.Finite(bounds.extents) || !PartArrayLayout.Finite(pivot) || !PartArrayLayout.Finite(owner.Position) || !PartArrayLayout.Finite(owner.Scale) || !PartArrayLayout.Finite(direction) || !PartArrayLayout.Finite(up) || bounds.extents.x < 0 || bounds.extents.y < 0 || bounds.extents.z < 0 || Math.Abs(owner.Scale.x) < .000001f || Math.Abs(owner.Scale.y) < .000001f || Math.Abs(owner.Scale.z) < .000001f || new ArrayPoint(direction).Length < 1e-12 || !PartArrayCurveMath.TryRotation(owner.Rotation, out owner.Rotation))
            {
                reason = LocalizedText.For("PO2.PartArray.Error.InvalidSourceBounds");
                return false;
            }

            Vector3 forward = PartArrayCurveMath.Unit(direction, Vector3.forward), start = owner.TransformPoint(pivot);
            var corners = new Vector3[8];
            Vector3 min = bounds.min, max = bounds.max;
            for (int i = 0; i < corners.Length; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                corners[i] = owner.Rotation * Vector3.Scale(corner - pivot, owner.Scale);
                if (!PartArrayLayout.Finite(corners[i]) || !PartArrayLayout.Finite(start))
                {
                    reason = LocalizedText.For("PO2.PartArray.Error.BoundsTooLarge");
                    return false;
                }
            }

            source = new PartArraySource(start, forward, PartArrayCurveMath.Frame(forward, up), corners);
            return true;
        }
    }
}