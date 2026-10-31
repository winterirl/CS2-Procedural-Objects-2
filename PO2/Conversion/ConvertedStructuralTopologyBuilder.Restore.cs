using System;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Conversion
{
    internal static partial class ConvertedStructuralTopologyBuilder
    {
        // Saved and cloned parts already have exact faces. Rebuild only mappings and edges;
        // inferring faces again after vertex edits could change the topology on reload.
        public static ConvertedStructuralTopologyResult RestorePersisted(MeshData mesh, MeshTopology meshTopology, StructuralFace[] faces, StructuralTopologySource topologySource)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (meshTopology == null)
            {
                throw new ArgumentNullException(nameof(meshTopology));
            }

            if (faces == null)
            {
                throw new ArgumentNullException(nameof(faces));
            }

            if (topologySource != StructuralTopologySource.ConvertedTriangleBaseline && topologySource != PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred)
            {
                throw new ArgumentOutOfRangeException(nameof(topologySource));
            }

            StructuralFaceTriangleResolver.Build(faces, mesh.Triangles, out StructuralFaceTriangles[] faceTriangles, out int[] triangleFaceIds);

            StructuralTopology topology;
            try
            {
                // Stored faces can gain quads through editing without changing
                // the original converted source kind.
                bool[] suppressedLogicalEdges = BuildPersistedSuppressedEdges(meshTopology, faces, faceTriangles);
                topology = StructuralTopology.BuildFromConvertedFaces(meshTopology, mesh.Triangles, faces, faceTriangles, triangleFaceIds, suppressedLogicalEdges);
            }
            catch (InvalidOperationException)
            {
                // The general builder is a correctness fallback for an
                // older saved converted face arrangement.
                // TODO: probably safe to remove eventually
                topology = StructuralTopology.Build(meshTopology, mesh.Vertices.Length, faces);
            }

            return new ConvertedStructuralTopologyResult(topology, faceTriangles, triangleFaceIds, topologySource);
        }

        private static bool[] BuildPersistedSuppressedEdges(MeshTopology meshTopology, StructuralFace[] faces, StructuralFaceTriangles[] faceTriangles)
        {
            bool[] suppressed = new bool[meshTopology.LogicalEdgeCount];

            for (int faceId = 0; faceId < faces.Length; faceId++)
            {
                StructuralFace face = faces[faceId];
                if (!face.IsQuad)
                {
                    continue;
                }

                StructuralFaceTriangles mapping = faceTriangles[faceId];
                int logical0 = meshTopology.GetLogicalVertexIdForRenderVertex(face.GetRenderVertexIndex(0));
                int logical2 = meshTopology.GetLogicalVertexIdForRenderVertex(face.GetRenderVertexIndex(2));
                if (!meshTopology.TryGetEdge(logical0, logical2, out LogicalEdge diagonal) || diagonal.TriangleIndices.Count != 2 || !ContainsBothTriangles(diagonal.TriangleIndices, mapping.TriangleA, mapping.TriangleB) || suppressed[diagonal.Id])
                {
                    throw new InvalidOperationException($"Persisted converted quad {faceId} does not have one unique logical diagonal.");
                }

                suppressed[diagonal.Id] = true;
            }

            return suppressed;
        }

        private static bool ContainsBothTriangles(IndexSpan triangleIds, int triangleA, int triangleB)
        {
            return triangleIds.Count == 2 && ((triangleIds[0] == triangleA && triangleIds[1] == triangleB) || (triangleIds[0] == triangleB && triangleIds[1] == triangleA));
        }

    }
}
