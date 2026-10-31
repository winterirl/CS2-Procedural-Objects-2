namespace PO2.Geometry.Topology
{
    public sealed partial class MeshTopology
    {
        internal static MeshTopology RestorePacked(PackedTopologyData data, int[] mapping)
        {
            var vertices = new Vertex[data.LogicalCount];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vertex(i, data[2], data[1][i], data[1][i + 1] - data[1][i],
                    data[4], data[3][i], data[3][i + 1] - data[3][i],
                    data[6], data[5][i], data[5][i + 1] - data[5][i]);
            var edges = new LogicalEdge[data.EdgeCount];
            for (int i = 0; i < edges.Length; i++)
                edges[i] = new LogicalEdge(i, data[7][i], data[8][i], data[10], data[9][i], data[9][i + 1] - data[9][i]);
            return CreateFromSortedData(vertices, edges, mapping);
        }
    }
}
