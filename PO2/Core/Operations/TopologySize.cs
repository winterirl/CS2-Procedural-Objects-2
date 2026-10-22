using System;
using PO2.Geometry;
using PO2.Persistence.Model;

namespace PO2.Core.Operations
{
    // Keep both shapes so the same checks work for the edit, undo and redo.
    internal sealed class TopologySize
    {
        internal readonly int MaximumVertices, MaximumLogicalVertices, MaximumIndices, MaximumFaces;

        internal TopologySize(int vertices, int logical, int indices, int faces, int resultVertices, int resultLogical, int resultIndices, int resultFaces)
        {
            if (vertices < 0 || logical < 0 || indices < 0 || faces < 0 || resultVertices < 0 || resultLogical < 0 || resultIndices < 0 || resultFaces < 0)
                throw new InvalidOperationException("Invalid topology output size.");
            MaximumVertices = Math.Max(vertices, resultVertices);
            MaximumLogicalVertices = Math.Max(logical, resultLogical);
            MaximumIndices = Math.Max(indices, resultIndices);
            MaximumFaces = Math.Max(faces, resultFaces);
        }

        internal static TopologySize ForPart(MeshPart part) =>
            new TopologySize(part.Mesh.Vertices.Length, part.Topology.LogicalVertexCount, part.Mesh.Triangles.Length, part.StructuralTopology.FaceCount, part.Mesh.Vertices.Length, part.Topology.LogicalVertexCount, part.Mesh.Triangles.Length, part.StructuralTopology.FaceCount);

        internal static TopologySize ForOutput(PersistencePartData source, int vertices, int logical, int indices, int faces) =>
            new TopologySize(source.Vertices.Length, source.LogicalVertexCount, source.Triangles.Length, source.StructuralFaces.Length, vertices, logical, indices, faces);

        internal void ValidateCandidate(MeshPart part)
        {
            if (part.Mesh.Vertices.Length > MaximumVertices || part.Topology.LogicalVertexCount > MaximumLogicalVertices || part.Mesh.Triangles.Length > MaximumIndices || part.StructuralTopology.FaceCount > MaximumFaces)
                throw new InvalidOperationException("The topology result exceeded its planned size.");
        }
    }
}