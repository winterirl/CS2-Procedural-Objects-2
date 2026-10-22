using System;

namespace PO2.Geometry.Topology
{
    // An edge between two logical vertices. 
    // This comes from triangle adjacency so it includes triangulation diagonals as well as visible/modeling edges.
    // Like Vertex this lives directly in one packed topology array.
    // The spans still point at shared triangle storage, so copying the value does not copy any adjacency data.
    public readonly struct LogicalEdge
    {
        private readonly IndexSpan m_TriangleIndices;

        public int Id { get; }
        public int VertexA { get; }
        public int VertexB { get; }

        public IndexSpan TriangleIndices =>
            m_TriangleIndices;

        public int AdjacentTriangleCount =>
            m_TriangleIndices.Count;

        public bool IsBoundary =>
            m_TriangleIndices.Count == 1;

        public bool IsManifold =>
            m_TriangleIndices.Count <= 2;

        internal LogicalEdge WithTriangleIndices(IndexSpan triangles) => new LogicalEdge(this, triangles);

        private LogicalEdge(LogicalEdge source, IndexSpan triangles)
        {
            Id = source.Id;
            m_TriangleIndices = triangles;
            VertexA = source.VertexA;
            VertexB = source.VertexB;
        }

        public LogicalEdge(int id, int vertexA, int vertexB, int[] triangleIndices)
        {
            if (vertexA < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexA));
            }

            if (vertexB < 0 || vertexB == vertexA)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexB));
            }

            Id = id;
            // Edge direction does not matter for identity.
            // Always store the smaller endpoint first so A-B and B-A cannot become two edges.
            VertexA = Math.Min(vertexA, vertexB);
            VertexB = Math.Max(vertexA, vertexB);
            m_TriangleIndices = new IndexSpan(triangleIndices);
        }

        internal LogicalEdge(int id, int vertexA, int vertexB, int[] triangleIndices, int triangleOffset, int triangleCount)
        {
            if (vertexA < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexA));
            }

            if (vertexB < 0 || vertexB == vertexA)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexB));
            }

            Id = id;
            VertexA = Math.Min(vertexA, vertexB);
            VertexB = Math.Max(vertexA, vertexB);
            m_TriangleIndices = new IndexSpan(triangleIndices, triangleOffset, triangleCount);
        }
    }
}