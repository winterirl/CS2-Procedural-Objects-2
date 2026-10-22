using System;
using UnityEngine;
using PO2.Geometry;

namespace PO2.Geometry.Topology
{
    // One editable vertex. 
    // It groups every render vertex that should move as the same point, plus the adjacency needed by selection/edit tools.
    // Keep this as a value inside the topology array. Converted buildings can
    // produce hundreds of thousands of these, and one managed object per
    // logical vertex turns final publication into a GC allocation shit-show.
    public readonly struct Vertex
    {
        private readonly IndexSpan m_RenderVertexIndices;
        private readonly IndexSpan m_TriangleIndices;
        private readonly IndexSpan m_AdjacentVertexIds;

        public int Id { get; }

        public IndexSpan RenderVertexIndices =>
            m_RenderVertexIndices;

        public IndexSpan TriangleIndices =>
            m_TriangleIndices;

        public IndexSpan AdjacentVertexIds =>
            m_AdjacentVertexIds;

        public Vector3 GetPosition(MeshData mesh)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (m_RenderVertexIndices.Count == 0)
            {
                throw new InvalidOperationException($"Logical vertex {Id} has no render vertices.");
            }

            // All members should be coincident.
            // Using the first one avoids averaging tiny differences into a new position.
            int renderVertexIndex = m_RenderVertexIndices[0];

            if (renderVertexIndex < 0 || renderVertexIndex >= mesh.Vertices.Length)
            {
                throw new InvalidOperationException($"Logical vertex {Id} references invalid render " + $"vertex index {renderVertexIndex}.");
            }

            return mesh.Vertices[renderVertexIndex];
        }

        internal Vertex WithTriangleIndices(IndexSpan triangles) => new Vertex(this, triangles);

        private Vertex(Vertex source, IndexSpan triangles)
        {
            Id = source.Id;
            m_TriangleIndices = triangles;
            m_RenderVertexIndices = source.m_RenderVertexIndices;
            m_AdjacentVertexIds = source.m_AdjacentVertexIds;
        }

        public Vertex(int id, int[] renderVertexIndices, int[] triangleIndices, int[] adjacentVertexIds)
        {
            Id = id;

            m_RenderVertexIndices = new IndexSpan(renderVertexIndices);
            m_TriangleIndices = new IndexSpan(triangleIndices);
            m_AdjacentVertexIds = new IndexSpan(adjacentVertexIds);
        }

        internal Vertex(int id, int[] renderVertexIndices, int renderVertexOffset, int renderVertexCount, int[] triangleIndices, int triangleOffset, int triangleCount, int[] adjacentVertexIds, int adjacentVertexOffset, int adjacentVertexCount)
        {
            Id = id;
            m_RenderVertexIndices = new IndexSpan(renderVertexIndices, renderVertexOffset, renderVertexCount);
            m_TriangleIndices = new IndexSpan(triangleIndices, triangleOffset, triangleCount);
            m_AdjacentVertexIds = new IndexSpan(adjacentVertexIds, adjacentVertexOffset, adjacentVertexCount);
        }
    }
}