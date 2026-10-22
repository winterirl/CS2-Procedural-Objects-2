using System;
using System.Threading;

namespace PO2.Geometry.Topology
{
    public sealed partial class StructuralTopology
    {
        internal StructuralTopology ReverseMirrorFaces(StructuralFace[] faces, CancellationToken token)
        {
            if (faces.Length != m_Faces.Length) throw new InvalidOperationException("Mirror changed the structural face count.");
            if (faces.Length == 0) return this;
            var boundaryEdges = new int[m_FaceBoundaryEdgeIds.Length];
            var forward = new byte[m_FaceBoundaryForward.Length];
            for (int i = 0; i < faces.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                var before = m_Faces[i];
                var after = faces[i];
                int count = before.CornerCount;
                if (after.Id != before.Id || after.CornerCount != count) throw new InvalidOperationException("Mirror changed a structural face.");
                int offset = m_FaceBoundaryOffsets[i];
                for (int j = 0; j < count; j++)
                {
                    if (after.GetRenderVertexIndex(j) != before.GetRenderVertexIndex(j == 0 ? 0 : count - j))
                        throw new InvalidOperationException("Mirror face corners are not reversed.");
                    int old = offset + count - 1 - j;
                    boundaryEdges[offset + j] = m_FaceBoundaryEdgeIds[old];
                    forward[offset + j] = m_FaceBoundaryForward[old] == 0 ? (byte)1 : (byte)0;
                }
            }

            return new StructuralTopology(faces, m_Edges, m_FaceBoundaryOffsets, boundaryEdges, forward, m_VertexIncidentEdgeOffsets, m_VertexIncidentEdgeIds);
        }
    }
}