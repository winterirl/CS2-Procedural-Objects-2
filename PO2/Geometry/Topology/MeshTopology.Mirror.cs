using System;
using System.Threading;

namespace PO2.Geometry.Topology
{
    public sealed partial class MeshTopology
    {
        internal MeshTopology RemapMirrorTriangles(int[] map, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (map == null) return this;
            int vertexCount = 0, edgeCount = 0;
            for (int i = 0; i < m_Vertices.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                vertexCount = checked(vertexCount + m_Vertices[i].TriangleIndices.Count);
            }

            for (int i = 0; i < m_Edges.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                edgeCount = checked(edgeCount + m_Edges[i].TriangleIndices.Count);
            }

            // One buffer for each set of triangle memberships, not one array
            // per vertex or edge. Everything unrelated to triangle IDs is shared.
            var vertexTriangles = new int[vertexCount];
            var edgeTriangles = new int[edgeCount];
            var vertices = new Vertex[m_Vertices.Length];
            var edges = new LogicalEdge[m_Edges.Length];
            int offset = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                var source = m_Vertices[i];
                var span = source.TriangleIndices;
                Remap(span, map, vertexTriangles, offset, token);
                vertices[i] = source.WithTriangleIndices(new IndexSpan(vertexTriangles, offset, span.Count));
                offset += span.Count;
            }

            offset = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                var source = m_Edges[i];
                var span = source.TriangleIndices;
                Remap(span, map, edgeTriangles, offset, token);
                edges[i] = source.WithTriangleIndices(new IndexSpan(edgeTriangles, offset, span.Count));
                offset += span.Count;
            }

            return new MeshTopology(vertices, edges, m_RenderVertexToLogicalVertex, m_EdgeLookup);
        }

        private static void Remap(IndexSpan source, int[] map, int[] target, int offset, CancellationToken token)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                target[offset + i] = map[source[i]];
            }

            // Adjacency consumers expect ascending triangle IDs after a swap.
            Array.Sort(target, offset, source.Count);
        }
    }
}