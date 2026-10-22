using System;
using System.Threading;
using UnityEngine;

namespace PO2.Rendering
{
    internal sealed class RenderBounds
    {
        internal const int kBlockSize = 256;
        private readonly Vector3[] m_Min, m_Max;
        private readonly byte[] m_Valid;
        private readonly uint[] m_Nodes;
        internal readonly RenderDirtyIndices Pending;
        private readonly int m_Leaf, m_Count;
        internal int ScannedVertices { get; private set; }
        internal int ChangedBlocks { get; private set; }
        internal int ReducedNodes { get; private set; }
        internal bool Dense { get; private set; }
        internal long Bytes => m_Min.Length * 25L + Pending.Bytes + m_Nodes.Length * 4L;

        internal Bounds Value
        {
            get
            {
                if (m_Valid[1] == 0) return new Bounds(Vector3.zero, Vector3.zero);
                var bounds = new Bounds();
                bounds.SetMinMax(m_Min[1], m_Max[1]);
                return bounds;
            }
        }

        internal RenderBounds(Vector3[] positions, CancellationToken token = default(CancellationToken))
        {
            m_Count = positions.Length;
            int blocks = (int)(((long)m_Count + kBlockSize - 1) / kBlockSize);
            m_Leaf = 1;
            while (m_Leaf < blocks) m_Leaf = checked(m_Leaf * 2);
            m_Min = new Vector3[checked(m_Leaf * 2)];
            m_Max = new Vector3[m_Min.Length];
            m_Valid = new byte[m_Min.Length];
            m_Nodes = new uint[(m_Leaf + 31) / 32];
            Pending = new RenderDirtyIndices(blocks);
            Rebuild(positions, token);
        }

        internal void Rebuild(Vector3[] positions, CancellationToken token = default(CancellationToken))
        {
            token.ThrowIfCancellationRequested();
            Run(positions, true, true);
            token.ThrowIfCancellationRequested();
        }

        // Callers without a preflight footprint can still request a checked update.
        internal void Update(Vector3[] positions, RenderDirtyIndices changed)
        {
            if (changed.Length != m_Count) throw new InvalidOperationException("Renderer bounds layout changed.");
            foreach (int index in changed) Pending.Add(index / kBlockSize);
            Run(positions, false, true);
        }

        internal void UpdatePrepared(Vector3[] positions, bool bulk) => Run(positions, bulk, false);

        private unsafe void Run(Vector3[] positions, bool full, bool validate)
        {
            if (positions.Length != m_Count) throw new InvalidOperationException("Renderer bounds layout changed.");
            ChangedBlocks = Pending.Count;
            Dense = full || ChangedBlocks * 2L >= Pending.Length;
            if (Dense) ChangedBlocks = Pending.Length;
            ScannedVertices = ReducedNodes = 0;
            if (!Dense && ChangedBlocks == 0) return;
            fixed (Vector3* points = positions)
            fixed (Vector3* min = m_Min)
            fixed (Vector3* max = m_Max)
            fixed (byte* valid = m_Valid)
            fixed (uint* nodes = m_Nodes)
            fixed (uint* bits = Pending.Bits)
            fixed (int* words = Pending.Words)
            {
                var context = new RenderGeometryKernels.BoundsContext
                {
                    Positions = (RenderGeometryKernels.Point*)points,
                    Min = (RenderGeometryKernels.Point*)min, Max = (RenderGeometryKernels.Point*)max,
                    Valid = valid, Nodes = nodes, Blocks = Pending.Describe(bits, words),
                    Count = m_Count, Leaf = m_Leaf, Dense = Dense ? 1 : 0, Validate = validate ? 1 : 0
                };
                RenderGeometryKernels.Scan(ref context);
                if (context.Error != 0)
                {
                    Array.Clear(m_Nodes, 0, m_Nodes.Length);
                    throw new InvalidOperationException("Non-finite renderer position.");
                }

                RenderGeometryKernels.Reduce(ref context);
                ScannedVertices = context.Scanned;
                ReducedNodes = context.Reduced;
            }

            Pending.Clear();
        }
    }
}