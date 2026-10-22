using System;
using System.Collections.Generic;

namespace PO2.Geometry.Editing
{
    // A successful analysis result.
    // Keeping this separate from the mutation means the tool can answer "is this legal?" without changing anything.
    public sealed class LoopDissolvePlan
    {
        private readonly int[] m_LoopVertexIds;
        private readonly DissolveSegment[] m_Segments;

        public IReadOnlyList<int> LoopVertexIds =>
            m_LoopVertexIds;

        public IReadOnlyList<DissolveSegment> Segments =>
            m_Segments;

        public int VertexCount =>
            m_LoopVertexIds.Length;

        public int SegmentCount =>
            m_Segments.Length;

        internal LoopDissolvePlan(int[] loopVertexIds, DissolveSegment[] segments)
        {
            m_LoopVertexIds = loopVertexIds ?? Array.Empty<int>();
            m_Segments = segments ?? Array.Empty<DissolveSegment>();
        }
    }
}