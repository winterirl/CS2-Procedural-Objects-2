using System;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Conversion
{
    internal sealed class ConvertedFaceInferenceResult
    {
        public StructuralFace[] Faces { get; }
        public StructuralFaceTriangles[] FaceTriangles { get; }
        public int[] TriangleFaceIds { get; }
        public bool[] SuppressedLogicalEdges { get; }
        public int PairedQuadCount { get; }

        public ConvertedFaceInferenceResult(StructuralFace[] faces, StructuralFaceTriangles[] faceTriangles, int[] triangleFaceIds, bool[] suppressedLogicalEdges, int pairedQuadCount)
        {
            Faces = faces ?? throw new ArgumentNullException(nameof(faces));
            FaceTriangles = faceTriangles ?? throw new ArgumentNullException(nameof(faceTriangles));
            TriangleFaceIds = triangleFaceIds ?? throw new ArgumentNullException(nameof(triangleFaceIds));
            SuppressedLogicalEdges = suppressedLogicalEdges ?? throw new ArgumentNullException(nameof(suppressedLogicalEdges));
            PairedQuadCount = pairedQuadCount;
        }
    }

    // There is no surviving authored quad list in a GeometryAsset.
    // Keep this conservative~ a missed pair leaves one extra selectable diagonal
    // A bad pair removes a real edge from the editing layer.
    internal static class ConvertedFaceInference
    {
        private const float kMinimumNormalDot = 0.9961947f;
        private const float kLongestEdgeRelativeMargin = 0.00001f;
        private const float kShapeRelativeEpsilon = 0.000001f;

        private enum CandidateRejection : byte
        {
            None = 0,
            Material = 1,
            RenderSeam = 2,
            Shape = 3,
            Ambiguous = 4,
        }

        private readonly struct QuadCorners
        {
            public int Vertex0 { get; }
            public int Vertex1 { get; }
            public int Vertex2 { get; }
            public int Vertex3 { get; }

            public QuadCorners(int vertex0, int vertex1, int vertex2, int vertex3)
            {
                Vertex0 = vertex0;
                Vertex1 = vertex1;
                Vertex2 = vertex2;
                Vertex3 = vertex3;
            }
        }

        public static ConvertedFaceInferenceResult Build(MeshData mesh, MeshTopology meshTopology, int[] triangleSurfaceAreaIds)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (meshTopology == null)
            {
                throw new ArgumentNullException(nameof(meshTopology));
            }

            int[] triangles = mesh.Triangles;
            if (triangles.Length % 3 != 0)
            {
                throw new InvalidOperationException("Converted mesh triangle index count is not divisible by three.");
            }

            int triangleCount = triangles.Length / 3;
            if (triangleSurfaceAreaIds == null || triangleSurfaceAreaIds.Length != triangleCount)
            {
                throw new InvalidOperationException("Converted triangle Surface Area membership is incomplete.");
            }

            bool[] usableTriangles = new bool[triangleCount];
            int usableTriangleCount = 0;
            for (int triangleId = 0; triangleId < triangleCount; triangleId++)
            {
                bool usable = IsUsableTriangle(mesh, meshTopology, triangleId);
                usableTriangles[triangleId] = usable;
                if (usable)
                {
                    usableTriangleCount++;
                }
            }

            int[] pairedTriangles = new int[triangleCount];
            int[] pairLogicalEdges = new int[triangleCount];
            Fill(pairedTriangles, -1);
            Fill(pairLogicalEdges, -1);

            bool[] suppressedLogicalEdges = new bool[meshTopology.LogicalEdgeCount];
            int pairedQuadCount = 0;

            for (int logicalEdgeId = 0; logicalEdgeId < meshTopology.LogicalEdgeCount; logicalEdgeId++)
            {
                LogicalEdge edge = meshTopology.GetEdge(logicalEdgeId);
                if (edge.TriangleIndices.Count != 2)
                {
                    continue;
                }

                int triangleA = edge.TriangleIndices[0];
                int triangleB = edge.TriangleIndices[1];
                if (triangleA < 0 || triangleA >= triangleCount || triangleB < 0 || triangleB >= triangleCount || triangleA == triangleB)
                {
                    throw new InvalidOperationException($"Logical edge {logicalEdgeId} has invalid triangle adjacency.");
                }

                if (!usableTriangles[triangleA] || !usableTriangles[triangleB])
                {
                    continue;
                }

                int firstTriangle = Math.Min(triangleA, triangleB);
                int secondTriangle = Math.Max(triangleA, triangleB);
                CandidateRejection rejection = TryCreateQuad(mesh, meshTopology, triangleSurfaceAreaIds, edge, firstTriangle, secondTriangle, out QuadCorners _);

                if (rejection != CandidateRejection.None)
                {
                    continue;
                }

                // Reject multiple partners even if a malformed mesh passes the diagonal test.
                // Choosing whichever edge sorts first would make the result unstable.
                if (pairedTriangles[firstTriangle] >= 0 || pairedTriangles[secondTriangle] >= 0)
                {
                    continue;
                }

                pairedTriangles[firstTriangle] = secondTriangle;
                pairedTriangles[secondTriangle] = firstTriangle;
                pairLogicalEdges[firstTriangle] = logicalEdgeId;
                pairLogicalEdges[secondTriangle] = logicalEdgeId;
                suppressedLogicalEdges[logicalEdgeId] = true;
                pairedQuadCount++;
            }

            int faceCount = checked(usableTriangleCount - pairedQuadCount);
            StructuralFace[] faces = new StructuralFace[faceCount];
            StructuralFaceTriangles[] faceTriangles = new StructuralFaceTriangles[faceCount];
            int[] triangleFaceIds = new int[triangleCount];
            Fill(triangleFaceIds, -1);

            int faceId = 0;
            for (int triangleId = 0; triangleId < triangleCount; triangleId++)
            {
                if (!usableTriangles[triangleId])
                {
                    continue;
                }

                int pairedTriangle = pairedTriangles[triangleId];
                if (pairedTriangle >= 0)
                {
                    if (triangleId > pairedTriangle)
                    {
                        continue;
                    }

                    int logicalEdgeId = pairLogicalEdges[triangleId];
                    if (logicalEdgeId < 0 || logicalEdgeId >= meshTopology.LogicalEdgeCount)
                    {
                        throw new InvalidOperationException($"Converted quad at triangle {triangleId} lost its diagonal edge.");
                    }

                    CandidateRejection rejection = TryCreateQuad(mesh, meshTopology, triangleSurfaceAreaIds, meshTopology.GetEdge(logicalEdgeId), triangleId, pairedTriangle, out QuadCorners quad);
                    if (rejection != CandidateRejection.None)
                    {
                        throw new InvalidOperationException($"Converted quad at triangle {triangleId} changed while publishing.");
                    }

                    faces[faceId] = new StructuralFace(faceId, quad.Vertex0, quad.Vertex1, quad.Vertex2, quad.Vertex3);
                    faceTriangles[faceId] = new StructuralFaceTriangles(triangleId, pairedTriangle, 2);
                    triangleFaceIds[triangleId] = faceId;
                    triangleFaceIds[pairedTriangle] = faceId;
                    faceId++;
                    continue;
                }

                int triangleOffset = triangleId * 3;
                faces[faceId] = new StructuralFace(faceId, triangles[triangleOffset], triangles[triangleOffset + 1], triangles[triangleOffset + 2]);
                faceTriangles[faceId] = new StructuralFaceTriangles(triangleId, -1, 1);
                triangleFaceIds[triangleId] = faceId;
                faceId++;
            }

            if (faceId != faceCount)
            {
                throw new InvalidOperationException("Converted structural face count changed while publishing.");
            }

            return new ConvertedFaceInferenceResult(faces, faceTriangles, triangleFaceIds, suppressedLogicalEdges, pairedQuadCount);
        }

        private static CandidateRejection TryCreateQuad(MeshData mesh, MeshTopology meshTopology, int[] triangleSurfaceAreaIds, LogicalEdge edge, int triangleA, int triangleB, out QuadCorners quad)
        {
            quad = default(QuadCorners);
            if (triangleSurfaceAreaIds[triangleA] != triangleSurfaceAreaIds[triangleB])
            {
                return CandidateRejection.Material;
            }

            if (!TryGetDirectedEdge(mesh, meshTopology, triangleA, edge.VertexA, edge.VertexB, out int startA, out int endA, out int uniqueA) || !TryGetDirectedEdge(mesh, meshTopology, triangleB, edge.VertexA, edge.VertexB, out int startB, out int endB, out int uniqueB))
            {
                return CandidateRejection.Shape;
            }

            // A quad needs to reproduce both original render triangles after save/load.
            // Reject a merge across any vertex-stream seam.
            if (startA != endB || endA != startB)
            {
                return CandidateRejection.RenderSeam;
            }

            int uniqueLogicalA = meshTopology.GetLogicalVertexIdForRenderVertex(uniqueA);
            int uniqueLogicalB = meshTopology.GetLogicalVertexIdForRenderVertex(uniqueB);
            if (uniqueA == uniqueB || uniqueLogicalA == uniqueLogicalB || uniqueLogicalA == edge.VertexA || uniqueLogicalA == edge.VertexB || uniqueLogicalB == edge.VertexA || uniqueLogicalB == edge.VertexB)
            {
                return CandidateRejection.Shape;
            }

            Vector3[] positions = mesh.Vertices;
            Vector3 sharedStart = positions[startA];
            Vector3 sharedEnd = positions[endA];
            Vector3 firstUnique = positions[uniqueA];
            Vector3 secondUnique = positions[uniqueB];

            float diagonalLengthSquared = (sharedEnd - sharedStart).sqrMagnitude;
            float longestBoundarySquared = Math.Max(Math.Max((firstUnique - sharedStart).sqrMagnitude, (firstUnique - sharedEnd).sqrMagnitude), Math.Max((secondUnique - sharedStart).sqrMagnitude, (secondUnique - sharedEnd).sqrMagnitude));
            if (!IsFinite(diagonalLengthSquared) || !IsFinite(longestBoundarySquared) || diagonalLengthSquared <= 0f || diagonalLengthSquared <= longestBoundarySquared + Math.Max(0.0000000001f, diagonalLengthSquared * kLongestEdgeRelativeMargin))
            {
                return CandidateRejection.Ambiguous;
            }

            Vector3 normalA = GetTriangleNormal(mesh, triangleA);
            Vector3 normalB = GetTriangleNormal(mesh, triangleB);
            float normalLengthA = normalA.magnitude;
            float normalLengthB = normalB.magnitude;
            if (!IsFinite(normalLengthA) || !IsFinite(normalLengthB) || normalLengthA <= 0f || normalLengthB <= 0f)
            {
                return CandidateRejection.Shape;
            }

            Vector3 unitNormalA = normalA / normalLengthA;
            Vector3 unitNormalB = normalB / normalLengthB;
            if (Vector3.Dot(unitNormalA, unitNormalB) < kMinimumNormalDot)
            {
                return CandidateRejection.Shape;
            }

            // With A's directed diagonal start -> end the winding-preserving quad is:
            //  end, A-unique, start, B-unique.
            // Its two render triangles are cyclic matches for the source triangles.
            quad = new QuadCorners(endA, uniqueA, startA, uniqueB);
            Vector3 quadNormal = unitNormalA + unitNormalB;
            float quadNormalLength = quadNormal.magnitude;
            if (!IsFinite(quadNormalLength) || quadNormalLength <= 0f || !IsStrictlyConvex(positions, quad, quadNormal / quadNormalLength, longestBoundarySquared))
            {
                quad = default(QuadCorners);
                return CandidateRejection.Shape;
            }

            return CandidateRejection.None;
        }

        private static bool TryGetDirectedEdge(MeshData mesh, MeshTopology meshTopology, int triangleId, int logicalVertexA, int logicalVertexB, out int renderStart, out int renderEnd, out int renderUnique)
        {
            renderStart = -1;
            renderEnd = -1;
            renderUnique = -1;
            int triangleOffset = triangleId * 3;
            for (int boundaryIndex = 0; boundaryIndex < 3; boundaryIndex++)
            {
                int start = mesh.Triangles[triangleOffset + boundaryIndex];
                int end = mesh.Triangles[triangleOffset + ((boundaryIndex + 1) % 3)];
                int startLogical = meshTopology.GetLogicalVertexIdForRenderVertex(start);
                int endLogical = meshTopology.GetLogicalVertexIdForRenderVertex(end);
                if (Math.Min(startLogical, endLogical) != logicalVertexA || Math.Max(startLogical, endLogical) != logicalVertexB)
                {
                    continue;
                }

                renderStart = start;
                renderEnd = end;
                renderUnique = mesh.Triangles[triangleOffset + ((boundaryIndex + 2) % 3)];
                return true;
            }

            return false;
        }

        private static bool IsStrictlyConvex(Vector3[] positions, QuadCorners quad, Vector3 normal, float scaleSquared)
        {
            Vector3 vertex0 = positions[quad.Vertex0];
            Vector3 vertex1 = positions[quad.Vertex1];
            Vector3 vertex2 = positions[quad.Vertex2];
            Vector3 vertex3 = positions[quad.Vertex3];
            float minimumTurn = Math.Max(0.0000000001f, scaleSquared * kShapeRelativeEpsilon);

            return IsPositiveTurn(vertex0, vertex1, vertex2, normal, minimumTurn) && IsPositiveTurn(vertex1, vertex2, vertex3, normal, minimumTurn) && IsPositiveTurn(vertex2, vertex3, vertex0, normal, minimumTurn) && IsPositiveTurn(vertex3, vertex0, vertex1, normal, minimumTurn);
        }

        private static bool IsPositiveTurn(Vector3 previous, Vector3 current, Vector3 next, Vector3 normal, float minimumTurn)
        {
            float turn = Vector3.Dot(Vector3.Cross(current - previous, next - current), normal);
            return IsFinite(turn) && turn > minimumTurn;
        }

        private static Vector3 GetTriangleNormal(MeshData mesh, int triangleId)
        {
            int offset = triangleId * 3;
            Vector3 vertex0 = mesh.Vertices[mesh.Triangles[offset]];
            Vector3 vertex1 = mesh.Vertices[mesh.Triangles[offset + 1]];
            Vector3 vertex2 = mesh.Vertices[mesh.Triangles[offset + 2]];
            return Vector3.Cross(vertex1 - vertex0, vertex2 - vertex0);
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
            return logicalA != logicalB && logicalA != logicalC && logicalB != logicalC;
        }

        private static void Fill(int[] values, int value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = value;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}