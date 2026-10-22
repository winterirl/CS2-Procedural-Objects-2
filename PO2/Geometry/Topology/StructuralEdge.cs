using System;

namespace PO2.Geometry.Topology
{
    // A modeling edge derived from structural face boundaries.
    // Adjacent faces are spans into one topology-owned buffer.
    public readonly struct StructuralEdge
    {
        private readonly IndexSpan m_AdjacentFaceIds;

        public int Id { get; }
        public int LogicalVertexA { get; }
        public int LogicalVertexB { get; }
        public IndexSpan AdjacentFaceIds => m_AdjacentFaceIds;
        public int AdjacentFaceCount => m_AdjacentFaceIds.Count;
        public bool IsBoundary => m_AdjacentFaceIds.Count == 1;
        public bool IsManifold => m_AdjacentFaceIds.Count <= 2;

        public StructuralEdge(int id, int logicalVertexA, int logicalVertexB, int[] adjacentFaceIds) : this(id, logicalVertexA, logicalVertexB, adjacentFaceIds ?? Array.Empty<int>(), 0, adjacentFaceIds?.Length ?? 0)
        {
        }

        internal StructuralEdge(int id, int logicalVertexA, int logicalVertexB, int[] adjacentFaceIds, int adjacentFaceOffset, int adjacentFaceCount)
        {
            if (id < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            if (logicalVertexA < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalVertexA));
            }

            if (logicalVertexB < 0 || logicalVertexB == logicalVertexA)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalVertexB));
            }

            Id = id;
            LogicalVertexA = Math.Min(logicalVertexA, logicalVertexB);
            LogicalVertexB = Math.Max(logicalVertexA, logicalVertexB);
            m_AdjacentFaceIds = new IndexSpan(adjacentFaceIds, adjacentFaceOffset, adjacentFaceCount);
        }
    }
}