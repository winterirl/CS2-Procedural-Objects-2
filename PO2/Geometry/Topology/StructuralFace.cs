using System;

namespace PO2.Geometry.Topology
{
    // Packed triangle or quad records avoid a managed object and corner array per face.
    public readonly struct StructuralFace
    {
        private readonly int m_Vertex0;
        private readonly int m_Vertex1;
        private readonly int m_Vertex2;
        private readonly int m_Vertex3;
        private readonly byte m_CornerCount;

        public int Id { get; }
        public int CornerCount => m_CornerCount;
        public bool IsTriangle => m_CornerCount == 3;
        public bool IsQuad => m_CornerCount == 4;

        public StructuralFace(int id, int vertex0, int vertex1, int vertex2) : this(id, vertex0, vertex1, vertex2, -1, 3)
        {
        }

        public StructuralFace(int id, int vertex0, int vertex1, int vertex2, int vertex3) : this(id, vertex0, vertex1, vertex2, vertex3, 4)
        {
        }

        private StructuralFace(int id, int vertex0, int vertex1, int vertex2, int vertex3, byte cornerCount)
        {
            if (id < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            ValidateVertex(vertex0, nameof(vertex0));
            ValidateVertex(vertex1, nameof(vertex1));
            ValidateVertex(vertex2, nameof(vertex2));

            if (cornerCount == 4)
            {
                ValidateVertex(vertex3, nameof(vertex3));
            }

            ValidateDistinctVertices(vertex0, vertex1, vertex2, vertex3, cornerCount);

            Id = id;
            m_Vertex0 = vertex0;
            m_Vertex1 = vertex1;
            m_Vertex2 = vertex2;
            m_Vertex3 = vertex3;
            m_CornerCount = cornerCount;
        }

        public int GetRenderVertexIndex(int cornerIndex)
        {
            if (cornerIndex < 0 || cornerIndex >= m_CornerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cornerIndex));
            }

            switch (cornerIndex)
            {
                case 0:
                    return m_Vertex0;
                case 1:
                    return m_Vertex1;
                case 2:
                    return m_Vertex2;
                default:
                    return m_Vertex3;
            }
        }

        public StructuralFace CloneWithId(int id)
        {
            return IsTriangle ? new StructuralFace(id, m_Vertex0, m_Vertex1, m_Vertex2) : new StructuralFace(id, m_Vertex0, m_Vertex1, m_Vertex2, m_Vertex3);
        }

        public StructuralFace Clone()
        {
            return this;
        }

        private static void ValidateVertex(int vertex, string parameterName)
        {
            if (vertex < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Structural face render vertex indices must be non-negative.");
            }
        }

        private static void ValidateDistinctVertices(int vertex0, int vertex1, int vertex2, int vertex3, int cornerCount)
        {
            if (vertex0 == vertex1 || vertex0 == vertex2 || vertex1 == vertex2 || (cornerCount == 4 && (vertex0 == vertex3 || vertex1 == vertex3 || vertex2 == vertex3)))
            {
                throw new ArgumentException("A structural face must reference distinct render vertices.");
            }
        }
    }
}