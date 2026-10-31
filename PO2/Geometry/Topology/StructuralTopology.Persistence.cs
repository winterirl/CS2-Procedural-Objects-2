namespace PO2.Geometry.Topology
{
    public sealed partial class StructuralTopology
    {
        internal int[] PersistenceBoundaryOffsets => m_FaceBoundaryOffsets;
        internal int[] PersistenceBoundaryEdges => m_FaceBoundaryEdgeIds;
        internal byte[] PersistenceBoundaryForward => m_FaceBoundaryForward;
        internal int[] PersistenceIncidentOffsets => m_VertexIncidentEdgeOffsets;
        internal int[] PersistenceIncidentEdges => m_VertexIncidentEdgeIds;

        internal static StructuralTopology RestorePacked(PackedTopologyData data, StructuralFace[] faces)
        {
            if (faces.Length == 0) return Empty;
            var edges = new StructuralEdge[data.StructuralEdgeCount];
            for (int i = 0; i < edges.Length; i++)
                edges[i] = new StructuralEdge(i, data[11][i], data[12][i], data[14], data[13][i], data[13][i + 1] - data[13][i]);
            return new StructuralTopology(faces, edges, data[15], data[16], data.Forward, data[18], data[19]);
        }
    }
}
