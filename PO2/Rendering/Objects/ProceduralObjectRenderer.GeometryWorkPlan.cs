using System;
using PO2.Rendering;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private sealed partial class RenderGeometryState
        {
            // Cache one footprint per layout generation within the shared memory budget.
            // If it does not fit, preflight runs without caching.
            private WorkPlan m_WorkPlan;

            private void InitializeWorkPlan()
            {
                foreach (var area in Areas)
                    if (area.Area.GeneratedMapping != null)
                    {
                        return;
                    }

                long bytes = 1024L + Positions.Bytes + Normals.Bytes + Areas.Length * 8L + Targets.Length * 1024L;
                foreach (var target in Targets) bytes += target.DirtyPositions.Bytes + target.DirtyNormals.Bytes + target.Bounds.Pending.Bytes;
                if (bytes > kUpdatePartBytes - m_Budget.Bytes)
                {
                    return;
                }

                var reservation = RenderGeometryPreparation.Reservation.TryAcquire(bytes);
                if (reservation == null)
                {
                    return;
                }

                try
                {
                    m_WorkPlan = new WorkPlan(this, reservation, bytes);
                }
                catch
                {
                    reservation.Dispose();
                    throw;
                }
            }

            private sealed class WorkPlan : IDisposable
            {
                private readonly RenderGeometryPreparation.Reservation m_Reservation;
                private readonly RenderDirtyIndices m_Positions, m_Normals;
                private readonly int[] m_AreaPositions, m_AreaNormals;
                private readonly TargetPlan[] m_Targets;
                internal readonly long Bytes;
                internal bool Valid;
                internal int Builds, Reuses;

                internal WorkPlan(RenderGeometryState owner, RenderGeometryPreparation.Reservation reservation, long bytes)
                {
                    m_Reservation = reservation;
                    Bytes = bytes;
                    m_Positions = new RenderDirtyIndices(owner.Positions.Length);
                    m_Normals = new RenderDirtyIndices(owner.Normals.Length);
                    m_AreaPositions = new int[owner.Areas.Length];
                    m_AreaNormals = new int[owner.Areas.Length];
                    m_Targets = new TargetPlan[owner.Targets.Length];
                    for (int i = 0; i < m_Targets.Length; i++) m_Targets[i] = new TargetPlan(owner.Targets[i]);
                }

                internal bool Matches(RenderGeometryState owner) => Valid && m_Positions.SameMembers(owner.Positions) && m_Normals.SameMembers(owner.Normals);

                internal unsafe void Save(RenderGeometryState owner)
                {
                    Valid = false;
                    m_Positions.CopyFrom(owner.Positions);
                    m_Normals.CopyFrom(owner.Normals);
                    for (int i = 0; i < m_AreaPositions.Length; i++)
                    {
                        m_AreaPositions[i] = owner.Areas[i].PositionCount;
                        m_AreaNormals[i] = owner.Areas[i].NormalCount;
                    }

                    for (int i = 0; i < m_Targets.Length; i++) m_Targets[i].Save(owner.Targets[i], owner.m_KernelTargets[i]);
                    Builds++;
                    Valid = true;
                }

                internal void Restore(RenderGeometryState owner)
                {
                    for (int i = 0; i < m_AreaPositions.Length; i++)
                    {
                        owner.Areas[i].PositionCount = m_AreaPositions[i];
                        owner.Areas[i].NormalCount = m_AreaNormals[i];
                    }

                    for (int i = 0; i < m_Targets.Length; i++) m_Targets[i].Restore(owner.Targets[i]);
                    Reuses++;
                }

                internal void BindRanges(RenderGeometryState owner)
                {
                    for (int i = 0; i < m_Targets.Length; i++)
                    {
                        owner.Targets[i].PositionPlan = m_Targets[i].PositionRanges;
                        owner.Targets[i].NormalPlan = m_Targets[i].NormalRanges;
                    }
                }

                public void Dispose()
                {
                    Valid = false;
                    m_Reservation.Dispose();
                }

                private sealed class TargetPlan
                {
                    private readonly RenderDirtyIndices m_Positions, m_Normals, m_Blocks;
                    private int m_BulkPositions, m_BulkNormals;
                    internal readonly RenderUploadPlan PositionRanges = new RenderUploadPlan(), NormalRanges = new RenderUploadPlan();

                    internal TargetPlan(RenderMeshUpdate target)
                    {
                        m_Positions = new RenderDirtyIndices(target.DirtyPositions.Length);
                        m_Normals = new RenderDirtyIndices(target.DirtyNormals.Length);
                        m_Blocks = new RenderDirtyIndices(target.Bounds.Pending.Length);
                    }

                    internal unsafe void Save(RenderMeshUpdate target, RenderGeometryKernels.Target descriptor)
                    {
                        m_Positions.CopyFrom(target.DirtyPositions);
                        m_Normals.CopyFrom(target.DirtyNormals);
                        m_Blocks.CopyFrom(target.Bounds.Pending);
                        m_BulkPositions = descriptor.BulkPositions;
                        m_BulkNormals = descriptor.BulkNormals;
                        PositionRanges.Build(m_Positions, 12, m_BulkPositions != 0);
                        NormalRanges.Build(m_Normals, 12, m_BulkNormals != 0);
                    }

                    internal void Restore(RenderMeshUpdate target)
                    {
                        target.DirtyPositions.CopyFrom(m_Positions);
                        target.DirtyNormals.CopyFrom(m_Normals);
                        target.Bounds.Pending.CopyFrom(m_Blocks);
                        target.BulkPositions = m_BulkPositions != 0;
                        target.BulkNormals = m_BulkNormals != 0;
                        target.BulkPositionCause = m_BulkPositions;
                        target.BulkNormalCause = m_BulkNormals;
                    }
                }
            }
        }
    }
}