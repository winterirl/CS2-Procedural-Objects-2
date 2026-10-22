using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Core;
using PO2.Geometry.Editing;

namespace PO2.Tools.MeshEdit
{
    internal sealed partial class MeshEditGpuSession
    {
        private MeshEditGpuPreparation m_Preparation, m_RetiredPreparation;
        private Publication m_Publication;
        private MeshEditGpuGeometry m_PreparedGeometry;
        private int m_PreparationTreeSerial;
        internal bool Ready => Active && m_Geometry != null && m_Buffers != null && m_Controller.IsActive && m_Controller.VisibilityMode == m_Visibility && m_Geometry.Mode == m_Controller.SelectionMode && (!NeedsOcclusion || m_Geometry.OcclusionCurrent);
        internal bool Preparing => Active && m_Controller.IsActive && (m_Controller.SelectionMode == MeshSelectionMode.Vertex || m_Controller.SelectionMode == MeshSelectionMode.Edge) && !Ready;

        private void DrainPreparation()
        {
            if (m_RetiredPreparation == null || !m_RetiredPreparation.Completion.IsCompleted) return;
            m_RetiredPreparation.Release();
            m_RetiredPreparation = null;
        }

        private void CancelPreparation()
        {
            m_Publication?.Dispose();
            m_Publication = null;
            m_PreparedGeometry = null;
            if (m_Preparation == null) return;
            m_Preparation.Cancel();
            // Starting another preparation is forbidden until this worker drains.
            m_RetiredPreparation = m_Preparation;
            m_Preparation = null;
            DrainPreparation();
        }

        private void DetachGeneration()
        {
            m_Revision++;
            m_ClassifiedRevision = -1;
            m_HoverRevision = -1;
            m_HoverSlot = -1;
            CancelAction();
            if (m_Hover != null) m_Hover.Invalid = true;
            m_Hover = null;
            if (m_Buffers != null)
            {
                m_Retired.Add(m_Buffers);
                m_Buffers = null;
            }

            m_Geometry = null;
            if (m_Volume != null) m_Volume.enabled = false;
        }

        private void PrepareGeneration(ProceduralObject obj, SolidVisibilityBvh bvh)
        {
            DrainPreparation();
            if (m_Preparation != null && (!m_Preparation.Source.Matches(obj, m_Controller.SelectionMode) || m_Preparation.Source.Visibility != m_Visibility || (m_Preparation.ReusedTree && !bvh.HasSerial(m_PreparationTreeSerial)))) CancelPreparation();
            if (m_Preparation == null)
            {
                // A published old mode is no longer drawn or refitted while its
                // tree arrays are borrowed by the worker for the next mode.
                var reuse = NeedsOcclusion && bvh.CanReuse(m_Geometry) && ReferenceEquals(m_Geometry.Object, obj) ? m_Geometry : null;
                DetachGeneration();
                if (m_RetiredPreparation != null || m_Retired.Count >= 2) return;
                m_Preparation = new MeshEditGpuPreparation(obj, m_Controller.SelectionMode, reuse, m_Visibility);
                m_PreparationTreeSerial = bvh.BuildSerial;
            }

            if (m_PreparedGeometry == null)
            {
                m_PreparedGeometry = m_Preparation.Advance();
                if (m_PreparedGeometry == null) return;
                m_Publication = new Publication(m_PreparedGeometry, NeedsOcclusion && !m_Preparation.ReusedTree);
            }

            if (!m_Publication.Advance()) return;
            // Stamp checked at the start of EVERY capture/worker/publication tick.
            // No callbacks or asynchronous continuation publish into this session.
            m_Publication.Commit(bvh, m_PreparedGeometry);
            m_Buffers = m_Publication.TakeBuffers();
            m_SelectionBits = m_Publication.Selection;
            m_Geometry = m_PreparedGeometry;
            m_Publication.Dispose();
            m_Publication = null;
            m_PreparedGeometry = null;
            m_Preparation.Release();
            m_Preparation = null;
            m_SelectionVersion = -1;
            m_SubmittedWorkRevision = -1;
            m_SubmittedGeometryRevision = m_Geometry.GeometryRevision;
            m_ClassifiedGeometryRevision = -1;
            m_ActivatedAt = UnityEngine.Time.realtimeSinceStartupAsDouble; // watchdog starts when drawable
        }

        private sealed class Publication : IDisposable
        {
            private Buffers m_Buffers = new Buffers();
            private readonly SolidVisibilityBvh.Publication m_Tree;
            private readonly MeshEditGpuGeometry m_Data;
            private readonly IEnumerator<int> m_Upload;
            private bool m_TreeReady, m_Ready;
            internal readonly uint[] Selection;

            internal Publication(MeshEditGpuGeometry data, bool publishTree)
            {
                m_Data = data;
                m_TreeReady = !publishTree;
                if (publishTree) m_Tree = new SolidVisibilityBvh.Publication();
                Selection = new uint[(Math.Max(1, data.Candidates.Length) + 31) / 32];
                m_Upload = Upload().GetEnumerator();
            }

            internal bool Advance()
            {
                var clock = Stopwatch.StartNew();
                int bytes = 0, steps = 0;
                do
                {
                    if (!m_TreeReady) m_TreeReady = m_Tree.Step(m_Data);
                    else if (!m_Upload.MoveNext())
                    {
                        m_Ready = true;
                        return true;
                    }
                    else bytes += m_Upload.Current;

                    steps++;
                } while (clock.Elapsed.TotalMilliseconds < 2 && bytes < 4 * 1024 * 1024 && steps < 64);

                return false;
            }

            // Yield after each allocation and <=256 KiB SetData. A driver call is
            // indivisible: the budget bounds work BETWEEN calls, not a driver stall.
            private IEnumerable<int> Stream<T>(T[] data, int stride, Action<ComputeBuffer> assign) where T : struct
            {
                var buffer = new ComputeBuffer(Math.Max(1, data.Length), stride);
                assign(buffer);
                yield return 0;
                int chunk = Math.Max(1, 256 * 1024 / stride);
                for (int i = 0; i < data.Length; i += chunk)
                {
                    int count = Math.Min(chunk, data.Length - i);
                    buffer.SetData(data, i, i, count);
                    yield return count * stride;
                }
            }

            private IEnumerable<int> Upload()
            {
                var b = m_Buffers;
                var g = m_Data;
                int count = Math.Max(1, g.Candidates.Length);
                foreach (int bytes in Stream(g.Candidates, 48, v => b.Candidates = v)) yield return bytes;
                foreach (int bytes in Stream(g.Normals, 64, v => b.Normals = v)) yield return bytes;
                foreach (int bytes in Stream(g.Nodes, 48, v => b.Nodes = v)) yield return bytes;
                foreach (int bytes in Stream(g.Triangles, 48, v => b.Triangles = v)) yield return bytes;
                b.Results = new ComputeBuffer(count, 96);
                yield return 0;
                b.Visible = new ComputeBuffer(count, 4, ComputeBufferType.Append);
                yield return 0;
                b.Args = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);
                yield return 0;
                b.Args.SetData(new uint[] { 6, 0, 0, 0 });
                yield return 16;
                foreach (int bytes in Stream(Selection, 4, v => b.Selected = v)) yield return bytes;
                b.PickValues = new ComputeBuffer(count, 8);
                yield return 0;
                b.PickGroups = new ComputeBuffer((count + 63) / 64, 16);
                yield return 0;
                foreach (int bytes in Stream(g.SourcePositions, 12, v => b.SourcePositions = v)) yield return bytes;
                foreach (int bytes in Stream(g.SourceNormals, 12, v => b.SourceNormals = v)) yield return bytes;
                foreach (int bytes in Stream(g.CandidateRefs, 16, v => b.CandidateSources = v)) yield return bytes;
                foreach (int bytes in Stream(g.FacingRefs, 32, v => b.FacingSources = v)) yield return bytes;
                foreach (int bytes in Stream(g.TriangleRefs, 16, v => b.TriangleSources = v)) yield return bytes;
                b.CandidateWork = new ComputeBuffer(Math.Max(1, g.Candidates.Length), 4);
                yield return 0;
                b.FacingWork = new ComputeBuffer(Math.Max(1, g.Normals.Length), 4);
                yield return 0;
                b.TriangleWork = new ComputeBuffer(Math.Max(1, g.Triangles.Length), 4);
                yield return 0;
                b.NodeWork = new ComputeBuffer(Math.Max(1, g.Nodes.Length), 4);
                yield return 0;
                b.Patches = new ComputeBuffer(kPatchCapacity, 16);
                yield return 0;
                foreach (int bytes in Stream(s_ClearFault, 4, v => b.UpdateFault = v)) yield return bytes;
                b.Minimum = new ComputeBuffer(1, 16);
                yield return 0;
                foreach (int bytes in Stream(new uint[4], 4, v => b.Winner = v)) yield return bytes;
                foreach (int bytes in Stream(new uint[4], 4, v => b.Scratch = v)) yield return bytes;
            }

            internal void Commit(SolidVisibilityBvh bvh, MeshEditGpuGeometry data)
            {
                if (!m_Ready) throw new InvalidOperationException("Incomplete Solid buffer publication.");
                m_Tree?.Commit(bvh, data);
            }

            internal Buffers TakeBuffers()
            {
                var result = m_Buffers;
                m_Buffers = null;
                return result;
            }

            public void Dispose()
            {
                m_Upload.Dispose();
                m_Tree?.Dispose();
                m_Buffers?.Dispose();
                m_Buffers = null;
            }
        }
    }
}