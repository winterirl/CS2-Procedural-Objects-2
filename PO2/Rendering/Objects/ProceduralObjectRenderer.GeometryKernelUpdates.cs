using System;
using System.Runtime.InteropServices;
using UnityEngine;
using PO2.Rendering;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private sealed partial class RenderGeometryState
        {
            private RenderGeometryKernels.Area[] m_KernelAreas;
            private RenderGeometryKernels.Target[] m_KernelTargets;
            private GCHandle[] m_Pins;
            private int m_PinCount;
            private long KernelBytes => m_KernelAreas.Length * 160L + m_KernelTargets.Length * 160L + m_Pins.Length * IntPtr.Size;

            private void InitializeKernels()
            {
                if (m_Normals != null && m_Normals.Length != m_Positions.Length)
                    throw new InvalidOperationException("Invalid renderer source normal length.");
                m_KernelAreas = new RenderGeometryKernels.Area[Areas.Length];
                m_KernelTargets = new RenderGeometryKernels.Target[Targets.Length];
                m_Pins = new GCHandle[checked(16 + Areas.Length * 3 + Targets.Length * 8)];
                // Existing cached values become the checked base for later sparse updates.
                RenderGeometryKernels.Validate(m_Positions);
                RenderGeometryKernels.Validate(m_Normals);
                for (int i = 0; i < Areas.Length; i++)
                {
                    var area = Areas[i].Area;
                    Areas[i].TargetIndex = Array.IndexOf(Targets, Areas[i].Target);
                    if (area.SourceVertexIndices.Length != area.AreaMesh.Vertices.Length || (area.AreaMesh.Normals != null && area.AreaMesh.Normals.Length != area.AreaMesh.Vertices.Length))
                        throw new InvalidOperationException("Invalid renderer area stream length.");
                    if (area.GeneratedMapping != null) continue;
                    var target = Areas[i].Target;
                    int offset = area.IsolatedRenderer ? 0 : area.CombinedVertexOffset;
                    if (offset < 0 || area.SourceVertexIndices.Length > target.Positions.Length - offset || (area.AreaMesh.Normals != null && (target.Normals == null || target.Normals.Length != target.Positions.Length)))
                        throw new InvalidOperationException("Invalid renderer destination span.");
                    // Combined spans are assigned by construction. Verify once before
                    // kernels rely on unique destination copies and their exact counts.
                    for (int j = 0; j < i; j++)
                    {
                        var prior = Areas[j].Area;
                        if (Areas[j].Target != target || prior.GeneratedMapping != null) continue;
                        int other = prior.IsolatedRenderer ? 0 : prior.CombinedVertexOffset;
                        if (offset < other + prior.SourceVertexIndices.Length && other < offset + area.SourceVertexIndices.Length)
                            throw new InvalidOperationException("Overlapping renderer destination spans.");
                    }

                    target.MappedCopies = checked(target.MappedCopies + area.SourceVertexIndices.Length);
                }

                foreach (var target in Targets)
                {
                    target.BulkThreshold = Math.Max(RenderGeometryKernels.kMinimumBulkCopies, (int)(((long)target.MappedCopies + RenderGeometryKernels.kBulkCopyDivisor - 1) / RenderGeometryKernels.kBulkCopyDivisor));
                    target.FragmentedThreshold = Math.Max(RenderGeometryKernels.kMinimumBulkCopies, (int)(((long)target.MappedCopies + RenderGeometryKernels.kFragmentedCopyDivisor - 1) / RenderGeometryKernels.kFragmentedCopyDivisor));
                }
            }

            private unsafe void* Pin(Array array)
            {
                if (array == null || array.Length == 0) return null;
                if (m_PinCount == m_Pins.Length) throw new InvalidOperationException("Renderer pin storage exhausted.");
                var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
                m_Pins[m_PinCount++] = handle;
                return handle.AddrOfPinnedObject().ToPointer();
            }

            private void Unpin()
            {
                while (m_PinCount > 0)
                {
                    int at = --m_PinCount;
                    m_Pins[at].Free();
                    m_Pins[at] = default(GCHandle);
                }
            }

            private unsafe RenderGeometryKernels.Set Pin(RenderDirtyIndices set) =>
                set.Describe((uint*)Pin(set.Bits), (int*)Pin(set.Words));

            internal unsafe void PrepareValues()
            {
                try
                {
                    bool reuse = m_WorkPlan != null && m_WorkPlan.Matches(this);
                    if (reuse) m_WorkPlan.Restore(this);
                    for (int i = 0; i < Targets.Length; i++)
                    {
                        var target = Targets[i];
                        m_KernelTargets[i] = new RenderGeometryKernels.Target
                        {
                            Positions = (RenderGeometryKernels.Point*)Pin(target.Positions),
                            Normals = (RenderGeometryKernels.Point*)Pin(target.Normals),
                            DirtyPositions = Pin(target.DirtyPositions), DirtyNormals = Pin(target.DirtyNormals),
                            Blocks = Pin(target.Bounds.Pending),
                            Threshold = target.BulkThreshold, FragmentedThreshold = target.FragmentedThreshold,
                            BulkPositions = target.BulkPositionCause, BulkNormals = target.BulkNormalCause
                        };
                    }

                    for (int i = 0; i < Areas.Length; i++)
                    {
                        var update = Areas[i];
                        var area = update.Area;
                        m_KernelAreas[i] = new RenderGeometryKernels.Area
                        {
                            Positions = (RenderGeometryKernels.Point*)Pin(area.AreaMesh.Vertices),
                            Normals = (RenderGeometryKernels.Point*)Pin(area.AreaMesh.Normals),
                            Map = (int*)Pin(area.SourceVertexIndices), Length = area.SourceVertexIndices.Length,
                            Target = update.TargetIndex,
                            Offset = area.IsolatedRenderer ? 0 : area.CombinedVertexOffset,
                            Isolated = area.IsolatedRenderer ? 1 : 0, Generated = area.GeneratedMapping != null ? 1 : 0,
                            PositionCount = update.PositionCount, NormalCount = update.NormalCount
                        };
                    }

                    var context = new RenderGeometryKernels.UpdateContext
                    {
                        Positions = (RenderGeometryKernels.Point*)Pin(m_Positions),
                        Normals = (RenderGeometryKernels.Point*)Pin(m_Normals),
                        Offsets = (int*)Pin(Offsets), Copies = (RenderGeometryPreparation.Copy*)Pin(Copies),
                        DirtyPositions = Pin(Positions), DirtyNormals = Pin(Normals),
                        Areas = (RenderGeometryKernels.Area*)Pin(m_KernelAreas),
                        Targets = (RenderGeometryKernels.Target*)Pin(m_KernelTargets), AreaCount = Areas.Length
                    };
                    RenderGeometryKernels.ValidateCurrentValues(ref context);
                    if (context.Error != 0) throw new InvalidOperationException("Non-finite renderer geometry.");
                    if (!reuse) RenderGeometryKernels.Prepare(ref context);
                    // Adopt counts even on failure, so clearing can reach every word
                    // touched by a partially completed preflight.
                    for (int i = 0; i < Areas.Length; i++)
                    {
                        var a = m_KernelAreas[i];
                        var update = Areas[i];
                        update.PositionCount = a.PositionCount;
                        update.NormalCount = a.NormalCount;
                        if (a.Generated != 0)
                        {
                            if (a.PositionCount != 0) context.SparseRoles |= 1;
                            if (a.NormalCount != 0) context.SparseRoles |= 2;
                        }
                    }

                    for (int i = 0; i < Targets.Length; i++)
                    {
                        var t = m_KernelTargets[i];
                        var target = Targets[i];
                        target.DirtyPositions.Accept(t.DirtyPositions);
                        target.DirtyNormals.Accept(t.DirtyNormals);
                        target.Bounds.Pending.Accept(t.Blocks);
                        target.BulkPositions = t.BulkPositions != 0;
                        target.BulkNormals = t.BulkNormals != 0;
                        if (t.DirtyPositions.Count != 0 && t.BulkPositions == 0) context.SparseRoles |= 1;
                        if (t.DirtyNormals.Count != 0 && t.BulkNormals == 0) context.SparseRoles |= 2;
                        // Resolve the Unity stream signature before copying. Incompatible
                        // layouts use the existing compatible full writer after the copy.
                        target.RangesReady = target.CanUploadRanges();
                    }

                    if (!reuse && m_WorkPlan != null) m_WorkPlan.Save(this);
                    m_WorkPlan?.BindRanges(this);
                    if (context.Error != 0) throw new InvalidOperationException("Non-finite renderer geometry.");
                    RenderGeometryKernels.Sparse(ref context);
                    RenderGeometryKernels.Bulk(ref context);
                }
                catch
                {
                    Invalidate();
                    foreach (var area in Areas) area.Clear();
                    foreach (var target in Targets) target.Clear();
                    throw;
                }
                finally
                {
                    Unpin();
                    // Pointers are only valid while pinned, even in reusable descriptors.
                    Array.Clear(m_KernelAreas, 0, m_KernelAreas.Length);
                    Array.Clear(m_KernelTargets, 0, m_KernelTargets.Length);
                }
            }
        }
    }
}