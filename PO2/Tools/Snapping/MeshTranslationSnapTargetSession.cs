using System;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;

namespace PO2.Tools.Snapping
{
    // Destination leases can arrive independently of the source. Only a later
    // pointer move or effective snap-enable may use a newly acquired lease.
    internal sealed class MeshTranslationSnapTargetSession : IDisposable
    {
        private const float kTargetTieEpsilon = 0.0001f;

        private sealed class Target
        {
            internal ProceduralObject Owner;
            internal ObjectSnapIndexLease Lease;
            internal ObjectTransform Transform;
            internal uint Geometry, PartVersion;
            internal MeshPart[] Parts;
            internal uint[] Topology;
            internal long ReadySequence;
        }

        private readonly SnapIndexSystem m_System;
        private readonly Target[] m_Targets;
        private readonly ProceduralObject m_Edited;
        private MeshSnapTargetFilter m_Filter;
        private int m_Cursor;
        private long m_Sequence;
        private bool m_Disposed;
        internal int TargetCount => m_Targets.Length;
        internal int MissingTargets { get; private set; }
        internal int LateAdoptions { get; private set; }

        internal MeshTranslationSnapTargetSession(SnapIndexSystem system, ProceduralObject[] owners, ProceduralObject edited, MeshSnapTargetFilter filter, long initialSequence = -1)
        {
            m_System = system;
            m_Edited = edited;
            m_Filter = filter;
            m_Targets = new Target[owners.Length];
            try
            {
                for (int i = 0; i < owners.Length; i++)
                {
                    m_Targets[i] = new Target { Owner = owners[i] };
                    Acquire(m_Targets[i], initialSequence);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void SetFilter(MeshSnapTargetFilter filter) => m_Filter = filter;

        private bool LayoutCurrent(Target target)
        {
            var owner = target.Owner;
            if (owner.PartVersion != target.PartVersion || owner.Parts.Count != target.Parts.Length || !owner.Transform.Position.Equals(target.Transform.Position) || !owner.Transform.Rotation.Equals(target.Transform.Rotation) || !owner.Transform.Scale.Equals(target.Transform.Scale)) return false;
            for (int i = 0; i < target.Parts.Length; i++)
                if (!ReferenceEquals(owner.Parts[i], target.Parts[i]) || owner.Parts[i].TopologyVersion != target.Topology[i])
                    return false;
            return true;
        }

        private bool Valid(Target target) => target.Lease != null && m_System.IsMeshTargetOwnerCurrent(target.Owner) && target.Owner.GeometryVersion == target.Geometry && LayoutCurrent(target);

        private void Acquire(Target target, long sequence)
        {
            if (!m_System.IsMeshTargetOwnerCurrent(target.Owner) || !m_System.TryAcquireMeshTarget(target.Owner, out var lease)) return;
            target.Lease = lease;
            target.Transform = target.Owner.Transform;
            target.Geometry = target.Owner.GeometryVersion;
            target.PartVersion = target.Owner.PartVersion;
            target.Parts = new MeshPart[target.Owner.Parts.Count];
            target.Topology = new uint[target.Parts.Length];
            for (int i = 0; i < target.Parts.Length; i++)
            {
                target.Parts[i] = target.Owner.Parts[i];
                target.Topology[i] = target.Parts[i].TopologyVersion;
            }

            target.ReadySequence = sequence;
            if (sequence >= 0) LateAdoptions++;
        }

        internal void Refresh(long sequence)
        {
            if (m_Disposed) return;
            m_Sequence = sequence;
            for (int count = 0; count < Math.Min(4, m_Targets.Length); count++)
            {
                var target = m_Targets[m_Cursor];
                m_Cursor = (m_Cursor + 1) % m_Targets.Length;
                if (Valid(target)) continue;
                target.Lease?.Dispose();
                target.Lease = null;
                Acquire(target, sequence);
            }
        }

        // The live drag only writes filtered vertices. All unfiltered features
        // in this lease remain exact, including stationary edges on the same PO.
        internal void AcceptOwnGeometry(ProceduralObject obj)
        {
            foreach (var target in m_Targets)
            {
                if (!ReferenceEquals(target.Owner, obj) || !ReferenceEquals(obj, m_Edited) || target.Lease == null) continue;
                if (LayoutCurrent(target)) target.Geometry = obj.GeometryVersion;
                else
                {
                    target.Lease.Dispose();
                    target.Lease = null;
                }
            }
        }

        internal bool IsTargetCurrent(in SnapTarget candidate)
        {
            if (candidate.Kind == SnapTargetKind.GameSurface) return true;
            foreach (var target in m_Targets)
                if (target.Owner.Id == candidate.PrimaryId)
                    return Valid(target) && m_Sequence > target.ReadySequence && (!ReferenceEquals(target.Owner, m_Edited) || m_Filter != null);
            return false;
        }

        public bool TryFindTarget(Camera camera, Vector2 cursorPosition, float maximumDistancePixels, bool verticesEnabled, bool edgesEnabled, out SnapTarget target)
        {
            target = default;
            MissingTargets = 0;
            if (m_Disposed || camera == null || maximumDistancePixels <= 0f || (!verticesEnabled && !edgesEnabled)) return false;
            bool found = false;
            float bestDistanceSquared = maximumDistancePixels * maximumDistancePixels, screenQueryRadius = -1f;
            ObjectSnapScreenQuery screenQuery = default;
            foreach (var entry in m_Targets)
            {
                bool edited = ReferenceEquals(entry.Owner, m_Edited);
                if (!Valid(entry) || (edited && m_Filter == null))
                {
                    MissingTargets++;
                    continue;
                }

                if (m_Sequence <= entry.ReadySequence) continue;
                float queryRadius = found ? Mathf.Sqrt(bestDistanceSquared + kTargetTieEpsilon) : maximumDistancePixels;
                if (!queryRadius.Equals(screenQueryRadius))
                {
                    if (!ObjectSnapScreenQuery.TryCreate(camera, cursorPosition, queryRadius, out screenQuery)) continue;
                    screenQueryRadius = queryRadius;
                }

                if (!entry.Lease.Index.TryFindTarget(camera, entry.Transform, entry.Owner.Id, screenQuery, verticesEnabled, edgesEnabled, -1, edited ? m_Filter : null, out SnapTarget candidate)) continue;
                ConsiderTarget(candidate, ref found, ref bestDistanceSquared, ref target);
            }

            return found;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            foreach (var target in m_Targets) target?.Lease?.Dispose();
        }

        private static void ConsiderTarget(in SnapTarget candidate, ref bool found, ref float bestDistanceSquared, ref SnapTarget best)
        {
            float candidateDistanceSquared = candidate.CursorDistancePixels * candidate.CursorDistancePixels;

            bool closer = candidateDistanceSquared < bestDistanceSquared - kTargetTieEpsilon;

            bool tied = Mathf.Abs(candidateDistanceSquared - bestDistanceSquared) <= kTargetTieEpsilon;

            if (!closer && !(tied && IsPreferredTarget(candidate, best)))
            {
                return;
            }

            found = true;
            bestDistanceSquared = candidateDistanceSquared;
            best = candidate;
        }

        private static bool IsPreferredTarget(in SnapTarget candidate, in SnapTarget best)
        {
            if (!best.IsValid)
            {
                return true;
            }

            if (candidate.Kind != best.Kind)
            {
                return candidate.Kind == SnapTargetKind.POVertex;
            }

            if (candidate.PrimaryId != best.PrimaryId)
            {
                return candidate.PrimaryId < best.PrimaryId;
            }

            if (candidate.SecondaryId != best.SecondaryId)
            {
                return candidate.SecondaryId < best.SecondaryId;
            }

            return candidate.TertiaryId < best.TertiaryId;
        }
    }
}