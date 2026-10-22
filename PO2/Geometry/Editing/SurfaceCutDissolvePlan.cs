using System;
using System.Collections.Generic;

namespace PO2.Geometry.Editing
{
    // Same idea as a loop dissolve plan, except this is an open chain whose
    // ends terminate on the mesh boundary.
    public sealed class SurfaceCutDissolvePlan
    {
        private readonly int[] m_CutVertexIds;
        private readonly DissolveSegment[] m_Segments;

        public IReadOnlyList<int> CutVertexIds =>
            m_CutVertexIds;

        public IReadOnlyList<DissolveSegment> Segments =>
            m_Segments;

        public int VertexCount =>
            m_CutVertexIds.Length;

        public int SegmentCount =>
            m_Segments.Length;

        internal SurfaceCutDissolvePlan(int[] cutVertexIds, DissolveSegment[] segments)
        {
            m_CutVertexIds = cutVertexIds ?? Array.Empty<int>();
            m_Segments = segments ?? Array.Empty<DissolveSegment>();
        }
    }
}