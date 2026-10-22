using System;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Conversion
{
    internal sealed class ConvertedStructuralTopologyResult
    {
        public StructuralTopology Topology { get; }
        public StructuralFaceTriangles[] FaceTriangles { get; }
        public int[] TriangleFaceIds { get; }
        public StructuralTopologySource TopologySource { get; }

        public ConvertedStructuralTopologyResult(StructuralTopology topology, StructuralFaceTriangles[] faceTriangles, int[] triangleFaceIds, StructuralTopologySource topologySource)
        {
            Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            FaceTriangles = faceTriangles ?? throw new ArgumentNullException(nameof(faceTriangles));
            TriangleFaceIds = triangleFaceIds ?? throw new ArgumentNullException(nameof(triangleFaceIds));
            TopologySource = topologySource;
        }
    }

    // Converted structural topology is built from detached PO arrays on the conversion worker.
    // No Unity object or ECS state is touched here.
    internal static class ConvertedStructuralTopologyBuilder
    {
        private const int kCompatibilityValidationVertexLimit = 4096;

        public static ConvertedStructuralTopologyResult Build(MeshData mesh, MeshTopology meshTopology, int[] triangleSurfaceAreaIds, ConversionPartProfile profile)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (meshTopology == null)
            {
                throw new ArgumentNullException(nameof(meshTopology));
            }

            StructuralFace[] faces;
            StructuralFaceTriangles[] faceTriangles;
            int[] triangleFaceIds;
            bool[] suppressedLogicalEdges;
            StructuralTopologySource topologySource;

            try
            {
                ConvertedFaceInferenceResult inference = ConvertedFaceInference.Build(mesh, meshTopology, triangleSurfaceAreaIds);
                faces = inference.Faces;
                faceTriangles = inference.FaceTriangles;
                triangleFaceIds = inference.TriangleFaceIds;
                suppressedLogicalEdges = inference.SuppressedLogicalEdges;
                topologySource = inference.PairedQuadCount > 0 ? PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred : StructuralTopologySource.ConvertedTriangleBaseline;
            }
            catch (InvalidOperationException exception)
            {
                BuildTriangleFaces(mesh, meshTopology, out faces, out faceTriangles, out triangleFaceIds);
                suppressedLogicalEdges = new bool[meshTopology.LogicalEdgeCount];
                topologySource = StructuralTopologySource.ConvertedTriangleBaseline;
                AppendFallbackReason(profile, "Quad inference failed: " + exception.Message);
            }

            StructuralTopology topology;
            bool usedLinearPacker = false;
            try
            {
                topology = StructuralTopology.BuildFromConvertedFaces(meshTopology, mesh.Triangles, faces, faceTriangles, triangleFaceIds, suppressedLogicalEdges);
                usedLinearPacker = true;
            }
            catch (InvalidOperationException exception)
            {
                AppendFallbackReason(profile, "Linear converted-face packing failed: " + exception.Message);

                topology = StructuralTopology.Build(meshTopology, mesh.Vertices.Length, faces);
                StructuralFaceTriangleResolver.Build(topology.Faces, mesh.Triangles, out faceTriangles, out triangleFaceIds);
            }

            if (usedLinearPacker && mesh.Vertices.Length <= kCompatibilityValidationVertexLimit)
            {
                StructuralTopology referenceTopology = StructuralTopology.Build(meshTopology, mesh.Vertices.Length, faces);
                StructuralFaceTriangleResolver.Build(referenceTopology.Faces, mesh.Triangles, out StructuralFaceTriangles[] referenceFaceTriangles, out int[] referenceTriangleFaceIds);

                try
                {
                    ValidateEquivalent(topology, faceTriangles, triangleFaceIds, referenceTopology, referenceFaceTriangles, referenceTriangleFaceIds, meshTopology.LogicalVertexCount);
                }
                catch (InvalidOperationException exception)
                {
                    topology = referenceTopology;
                    faceTriangles = referenceFaceTriangles;
                    triangleFaceIds = referenceTriangleFaceIds;
                    AppendFallbackReason(profile, "Linear converted-face validation failed: " + exception.Message);
                }
            }

            return new ConvertedStructuralTopologyResult(topology, faceTriangles, triangleFaceIds, topologySource);
        }

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

        private static void AppendFallbackReason(ConversionPartProfile profile, string reason)
        {
            if (profile == null || string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            profile.StructuralTopologyFallbackReason = string.IsNullOrWhiteSpace(profile.StructuralTopologyFallbackReason) ? reason : profile.StructuralTopologyFallbackReason + " | " + reason;
        }

        private static void BuildTriangleFaces(MeshData mesh, MeshTopology meshTopology, out StructuralFace[] faces, out StructuralFaceTriangles[] faceTriangles, out int[] triangleFaceIds)
        {
            int[] triangles = mesh.Triangles;
            if (triangles.Length % 3 != 0)
            {
                throw new InvalidOperationException("Converted mesh triangle index count is not divisible by three.");
            }

            int triangleCount = triangles.Length / 3;
            int validFaceCount = 0;
            for (int triangleId = 0; triangleId < triangleCount; triangleId++)
            {
                if (IsUsableTriangle(mesh, meshTopology, triangleId))
                {
                    validFaceCount++;
                }
            }

            faces = new StructuralFace[validFaceCount];
            faceTriangles = new StructuralFaceTriangles[validFaceCount];
            triangleFaceIds = new int[triangleCount];
            for (int triangleId = 0; triangleId < triangleFaceIds.Length; triangleId++)
            {
                triangleFaceIds[triangleId] = -1;
            }

            int faceId = 0;
            for (int triangleId = 0; triangleId < triangleCount; triangleId++)
            {
                if (!IsUsableTriangle(mesh, meshTopology, triangleId))
                {
                    continue;
                }

                int triangleOffset = triangleId * 3;
                faces[faceId] = new StructuralFace(faceId, triangles[triangleOffset], triangles[triangleOffset + 1], triangles[triangleOffset + 2]);
                faceTriangles[faceId] = new StructuralFaceTriangles(triangleId, -1, 1);
                triangleFaceIds[triangleId] = faceId;
                faceId++;
            }

            if (faceId != validFaceCount)
            {
                throw new InvalidOperationException("Converted structural face count changed while building.");
            }
        }

        private static bool IsUsableTriangle(MeshData mesh, MeshTopology meshTopology, int triangleId)
        {
            int triangleOffset = checked(triangleId * 3);
            int[] triangles = mesh.Triangles;
            int renderA = triangles[triangleOffset];
            int renderB = triangles[triangleOffset + 1];
            int renderC = triangles[triangleOffset + 2];

            if (renderA < 0 || renderA >= mesh.Vertices.Length || renderB < 0 || renderB >= mesh.Vertices.Length || renderC < 0 || renderC >= mesh.Vertices.Length)
            {
                throw new InvalidOperationException($"Converted triangle {triangleId} references an invalid render vertex.");
            }

            if (renderA == renderB || renderA == renderC || renderB == renderC)
            {
                return false;
            }

            int logicalA = meshTopology.GetLogicalVertexIdForRenderVertex(renderA);
            int logicalB = meshTopology.GetLogicalVertexIdForRenderVertex(renderB);
            int logicalC = meshTopology.GetLogicalVertexIdForRenderVertex(renderC);

            // A collapsed boundary cannot become a structural edge.
            // Keep the render triangle, but leave it outside the editable face layer.
            return logicalA != logicalB && logicalA != logicalC && logicalB != logicalC;
        }

        private static void ValidateEquivalent(StructuralTopology actual, StructuralFaceTriangles[] actualFaceTriangles, int[] actualTriangleFaceIds, StructuralTopology expected, StructuralFaceTriangles[] expectedFaceTriangles, int[] expectedTriangleFaceIds, int logicalVertexCount)
        {
            if (actual.FaceCount != expected.FaceCount || actual.StructuralEdgeCount != expected.StructuralEdgeCount || actualFaceTriangles.Length != expectedFaceTriangles.Length || actualTriangleFaceIds.Length != expectedTriangleFaceIds.Length)
            {
                throw new InvalidOperationException("structural topology counts differ from the reference");
            }

            for (int faceId = 0; faceId < actual.FaceCount; faceId++)
            {
                StructuralFace actualFace = actual.GetFace(faceId);
                StructuralFace expectedFace = expected.GetFace(faceId);
                if (actualFace.Id != expectedFace.Id || actualFace.CornerCount != expectedFace.CornerCount)
                {
                    throw new InvalidOperationException($"structural face {faceId} differs from the reference");
                }

                for (int boundaryIndex = 0; boundaryIndex < actualFace.CornerCount; boundaryIndex++)
                {
                    if (actualFace.GetRenderVertexIndex(boundaryIndex) != expectedFace.GetRenderVertexIndex(boundaryIndex) || actual.GetBoundaryEdgeId(faceId, boundaryIndex) != expected.GetBoundaryEdgeId(faceId, boundaryIndex) || actual.IsBoundaryForward(faceId, boundaryIndex) != expected.IsBoundaryForward(faceId, boundaryIndex))
                    {
                        throw new InvalidOperationException($"structural face {faceId} boundary " + $"{boundaryIndex} differs from the reference");
                    }
                }

                ValidateFaceTriangles(actualFaceTriangles[faceId], expectedFaceTriangles[faceId], faceId);
            }

            for (int edgeId = 0; edgeId < actual.StructuralEdgeCount; edgeId++)
            {
                StructuralEdge actualEdge = actual.GetEdge(edgeId);
                StructuralEdge expectedEdge = expected.GetEdge(edgeId);
                if (actualEdge.Id != expectedEdge.Id || actualEdge.LogicalVertexA != expectedEdge.LogicalVertexA || actualEdge.LogicalVertexB != expectedEdge.LogicalVertexB)
                {
                    throw new InvalidOperationException($"structural edge {edgeId} differs from the reference");
                }

                ValidateSpan(actualEdge.AdjacentFaceIds, expectedEdge.AdjacentFaceIds, $"structural edge {edgeId} adjacency");
            }

            for (int logicalVertexId = 0; logicalVertexId < logicalVertexCount; logicalVertexId++)
            {
                ValidateSpan(actual.GetIncidentEdgeIds(logicalVertexId), expected.GetIncidentEdgeIds(logicalVertexId), $"logical vertex {logicalVertexId} structural edges");
            }

            for (int triangleId = 0; triangleId < actualTriangleFaceIds.Length; triangleId++)
            {
                if (actualTriangleFaceIds[triangleId] != expectedTriangleFaceIds[triangleId])
                {
                    throw new InvalidOperationException($"triangle {triangleId} maps to a different structural face");
                }
            }
        }

        private static void ValidateFaceTriangles(StructuralFaceTriangles actual, StructuralFaceTriangles expected, int faceId)
        {
            if (actual.Count != expected.Count || actual.TriangleA != expected.TriangleA || actual.TriangleB != expected.TriangleB)
            {
                throw new InvalidOperationException($"structural face {faceId} maps to different render triangles");
            }
        }

        private static void ValidateSpan(IndexSpan actual, IndexSpan expected, string label)
        {
            if (actual.Count != expected.Count)
            {
                throw new InvalidOperationException($"{label} count differs from the reference");
            }

            for (int i = 0; i < actual.Count; i++)
            {
                if (actual[i] != expected[i])
                {
                    throw new InvalidOperationException($"{label} differs at entry {i}");
                }
            }
        }
    }
}