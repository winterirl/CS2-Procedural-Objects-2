using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Tools.MeshEdit;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Geometry.Editing
{
    // Region analysis is shared with Inset.
    // This class only owns the pieces that are specific to moving the cap along its normal and adding walls.
    internal static class FaceExtrusionEditor
    {
        private const float kTriangleCrossSqrEpsilon = 0.000000000001f;
        private const float kWallSmoothDotThreshold = 0.70710678f;

        private readonly struct WallCornerPair
        {
            public int BaseRenderVertex { get; }
            public int CapRenderVertex { get; }
            public int LogicalVertex { get; }
            public int PayloadRenderVertex { get; }
            public int SurfaceAreaId { get; }
            public Vector3 WallNormal { get; }

            public WallCornerPair(int baseRenderVertex, int capRenderVertex, int logicalVertex, int payloadRenderVertex, int surfaceAreaId, Vector3 wallNormal)
            {
                BaseRenderVertex = baseRenderVertex;
                CapRenderVertex = capRenderVertex;
                LogicalVertex = logicalVertex;
                PayloadRenderVertex = payloadRenderVertex;
                SurfaceAreaId = surfaceAreaId;
                WallNormal = wallNormal;
            }
        }

        public static bool CanCommit(FaceRegionPlan plan, float distance)
        {
            if (plan == null || !IsFinite(distance))
            {
                return false;
            }

            Vector3 localDelta = plan.LocalNormalPerWorldMeter * distance;
            float minimumDisplacement = MeshTopology.kDefaultWeldEpsilon * 2f;
            return IsFinite(localDelta) && localDelta.sqrMagnitude > minimumDisplacement * minimumDisplacement;
        }

        public static LoopCutPreviewLine[] BuildPreviewLines(FaceRegionPlan plan, float distance)
        {
            if (plan == null || !IsFinite(distance))
            {
                return Array.Empty<LoopCutPreviewLine>();
            }

            Vector3 localDelta = plan.LocalNormalPerWorldMeter * distance;
            int lineCount = checked(plan.BoundaryEdgeCount * 2 + plan.SelectedEdges.Length);
            LoopCutPreviewLine[] lines = new LoopCutPreviewLine[lineCount];
            int lineIndex = 0;

            for (int loopIndex = 0; loopIndex < plan.BoundaryLoops.Length; loopIndex++)
            {
                FaceRegionBoundaryLoop loop = plan.BoundaryLoops[loopIndex];

                for (int edgeIndex = 0; edgeIndex < loop.EdgeCount; edgeIndex++)
                {
                    FaceRegionBoundaryEdge edge = loop.Edges[edgeIndex];
                    Vector3 baseA = plan.SourceTransform.TransformPoint(edge.LocalPositionA);
                    Vector3 baseB = plan.SourceTransform.TransformPoint(edge.LocalPositionB);
                    Vector3 capA = plan.SourceTransform.TransformPoint(edge.LocalPositionA + localDelta);

                    lines[lineIndex++] = new LoopCutPreviewLine(baseA, baseB, isTargetEdge: false);
                    lines[lineIndex++] = new LoopCutPreviewLine(baseA, capA, isTargetEdge: false);
                }
            }

            for (int edgeIndex = 0; edgeIndex < plan.SelectedEdges.Length; edgeIndex++)
            {
                FaceRegionPreviewEdge edge = plan.SelectedEdges[edgeIndex];
                lines[lineIndex++] = new LoopCutPreviewLine(plan.SourceTransform.TransformPoint(edge.LocalPositionA + localDelta), plan.SourceTransform.TransformPoint(edge.LocalPositionB + localDelta), isTargetEdge: false);
            }

            return lines;
        }

        public static bool TryCreateMutation(MeshPart part, FaceRegionPlan plan, float distance, out FaceExtrusionMutation result)
        {
            result = null;

            if (part == null || plan == null || !part.SupportsDestructiveTopologyEditing || !part.TopologyEditCapabilities.CanEditStructuralFaces || !CanCommit(plan, distance) || plan.SelectedFaces.Length != part.StructuralTopology.FaceCount)
            {
                return false;
            }

            MeshData mesh = part.Mesh;
            StructuralTopology topology = part.StructuralTopology;
            Vector3 localDelta = plan.LocalNormalPerWorldMeter * distance;

            List<Vector3> vertices = new List<Vector3>(mesh.Vertices);
            List<Vector3> normals = mesh.Normals != null ? new List<Vector3>(mesh.Normals) : null;
            List<Vector4> tangents = mesh.Tangents != null ? new List<Vector4>(mesh.Tangents) : null;
            List<Vector4> uv0 = mesh.UV0 != null ? new List<Vector4>(mesh.UV0) : null;
            List<Color32> vertexColors = mesh.VertexColors != null ? new List<Color32>(mesh.VertexColors) : null;
            List<Vector4> texCoord1 = mesh.TexCoord1 != null ? new List<Vector4>(mesh.TexCoord1) : null;
            List<Vector4> texCoord3 = mesh.TexCoord3 != null ? new List<Vector4>(mesh.TexCoord3) : null;

            List<StructuralFace> faces = new List<StructuralFace>(topology.FaceCount + plan.BoundaryEdgeCount);
            List<int> faceSurfaceAreaIds = new List<int>(topology.FaceCount + plan.BoundaryEdgeCount);
            int[] sourceToCapRender = new int[mesh.Vertices.Length];
            for (int i = 0; i < sourceToCapRender.Length; i++)
            {
                sourceToCapRender[i] = -1;
            }

            List<int> capRenderVertices = new List<int>();

            for (int faceId = 0; faceId < topology.FaceCount; faceId++)
            {
                StructuralFace sourceFace = topology.GetFace(faceId);
                if (!part.TryGetStructuralFaceSurfaceAreaId(faceId, out int surfaceAreaId))
                {
                    return false;
                }

                if (!plan.SelectedFaces[faceId])
                {
                    faces.Add(sourceFace.CloneWithId(faces.Count));
                    faceSurfaceAreaIds.Add(surfaceAreaId);
                    continue;
                }

                int[] capCorners = new int[sourceFace.CornerCount];
                for (int cornerIndex = 0; cornerIndex < sourceFace.CornerCount; cornerIndex++)
                {
                    int sourceRenderVertex = sourceFace.GetRenderVertexIndex(cornerIndex);
                    int capRenderVertex = sourceToCapRender[sourceRenderVertex];
                    if (capRenderVertex < 0)
                    {
                        capRenderVertex = AppendCopiedVertex(sourceRenderVertex, mesh.Vertices[sourceRenderVertex] + localDelta, vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3);
                        sourceToCapRender[sourceRenderVertex] = capRenderVertex;
                        capRenderVertices.Add(capRenderVertex);
                    }

                    capCorners[cornerIndex] = capRenderVertex;
                }

                faces.Add(CreateFace(faces.Count, capCorners));
                faceSurfaceAreaIds.Add(surfaceAreaId);
            }

            List<int> capBoundaryRenderPairs = new List<int>(plan.BoundaryEdgeCount * 2);

            for (int loopIndex = 0; loopIndex < plan.BoundaryLoops.Length; loopIndex++)
            {
                FaceRegionBoundaryLoop loop = plan.BoundaryLoops[loopIndex];
                float accumulatedLength = 0f;
                WallCornerPair previousEnd = default;
                bool hasPreviousEnd = false;

                for (int edgeIndex = 0; edgeIndex < loop.EdgeCount; edgeIndex++)
                {
                    FaceRegionBoundaryEdge edge = loop.Edges[edgeIndex];
                    if (!part.TryGetStructuralFaceSurfaceAreaId(edge.SourceFaceId, out int surfaceAreaId))
                    {
                        return false;
                    }

                    Vector3 wallCross = Vector3.Cross(edge.LocalPositionB - edge.LocalPositionA, localDelta);
                    if (wallCross.sqrMagnitude <= kTriangleCrossSqrEpsilon)
                    {
                        return false;
                    }

                    Vector3 wallNormal = wallCross.normalized;
                    float startU = loop.TotalLength > 0f ? accumulatedLength / loop.TotalLength : 0f;
                    accumulatedLength += edge.Length;
                    float endU = loop.TotalLength > 0f ? accumulatedLength / loop.TotalLength : 1f;

                    WallCornerPair startPair;
                    if (hasPreviousEnd && CanShareWallCorner(previousEnd, edge.LogicalVertexA, edge.PayloadRenderVertexA, surfaceAreaId, wallNormal))
                    {
                        startPair = previousEnd;
                    }
                    else
                    {
                        startPair = AppendWallCornerPair(edge.LocalPositionA, localDelta, edge.LogicalVertexA, edge.PayloadRenderVertexA, surfaceAreaId, wallNormal, startU, mesh, vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3);
                    }

                    WallCornerPair endPair = AppendWallCornerPair(edge.LocalPositionB, localDelta, edge.LogicalVertexB, edge.PayloadRenderVertexB, surfaceAreaId, wallNormal, endU, mesh, vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3);

                    faces.Add(new StructuralFace(faces.Count, startPair.BaseRenderVertex, endPair.BaseRenderVertex, endPair.CapRenderVertex, startPair.CapRenderVertex));
                    faceSurfaceAreaIds.Add(surfaceAreaId);

                    int capA = sourceToCapRender[edge.SourceRenderVertexA];
                    int capB = sourceToCapRender[edge.SourceRenderVertexB];
                    if (capA < 0 || capB < 0)
                    {
                        return false;
                    }

                    capBoundaryRenderPairs.Add(capA);
                    capBoundaryRenderPairs.Add(capB);
                    previousEnd = endPair;
                    hasPreviousEnd = true;
                }
            }

            return TryCompactAndCreateResult(mesh, vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3, faces, faceSurfaceAreaIds, capRenderVertices, capBoundaryRenderPairs, out result);
        }

        private static int AppendCopiedVertex(int sourceRenderVertex, Vector3 position, List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector4> uv0, List<Color32> vertexColors, List<Vector4> texCoord1, List<Vector4> texCoord3)
        {
            int newIndex = vertices.Count;
            vertices.Add(position);

            if (normals != null)
            {
                normals.Add(normals[sourceRenderVertex]);
            }

            if (tangents != null)
            {
                tangents.Add(tangents[sourceRenderVertex]);
            }

            if (uv0 != null)
            {
                uv0.Add(uv0[sourceRenderVertex]);
            }

            if (vertexColors != null)
            {
                vertexColors.Add(vertexColors[sourceRenderVertex]);
            }

            if (texCoord1 != null)
            {
                texCoord1.Add(texCoord1[sourceRenderVertex]);
            }

            if (texCoord3 != null)
            {
                texCoord3.Add(texCoord3[sourceRenderVertex]);
            }

            return newIndex;
        }

        private static WallCornerPair AppendWallCornerPair(Vector3 basePosition, Vector3 localDelta, int logicalVertex, int payloadRenderVertex, int surfaceAreaId, Vector3 wallNormal, float u, MeshData sourceMesh, List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector4> uv0, List<Color32> vertexColors, List<Vector4> texCoord1, List<Vector4> texCoord3)
        {
            int baseRenderVertex = vertices.Count;
            AppendWallVertex(basePosition, payloadRenderVertex, u, 0f, sourceMesh, vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3);

            int capRenderVertex = vertices.Count;
            AppendWallVertex(basePosition + localDelta, payloadRenderVertex, u, 1f, sourceMesh, vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3);

            return new WallCornerPair(baseRenderVertex, capRenderVertex, logicalVertex, payloadRenderVertex, surfaceAreaId, wallNormal);
        }

        private static void AppendWallVertex(Vector3 position, int payloadRenderVertex, float u, float v, MeshData sourceMesh, List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector4> uv0, List<Color32> vertexColors, List<Vector4> texCoord1, List<Vector4> texCoord3)
        {
            vertices.Add(position);

            if (normals != null)
            {
                normals.Add(sourceMesh.Normals[payloadRenderVertex]);
            }

            if (tangents != null)
            {
                tangents.Add(sourceMesh.Tangents[payloadRenderVertex]);
            }

            if (uv0 != null)
            {
                Vector4 sourceUV = sourceMesh.UV0[payloadRenderVertex];
                uv0.Add(new Vector4(u, v, sourceUV.z, sourceUV.w));
            }

            if (vertexColors != null)
            {
                vertexColors.Add(sourceMesh.VertexColors[payloadRenderVertex]);
            }

            if (texCoord1 != null)
            {
                texCoord1.Add(sourceMesh.TexCoord1[payloadRenderVertex]);
            }

            if (texCoord3 != null)
            {
                texCoord3.Add(sourceMesh.TexCoord3[payloadRenderVertex]);
            }
        }

        private static bool CanShareWallCorner(WallCornerPair previous, int logicalVertex, int payloadRenderVertex, int surfaceAreaId, Vector3 wallNormal)
        {
            return previous.LogicalVertex == logicalVertex && previous.PayloadRenderVertex == payloadRenderVertex && previous.SurfaceAreaId == surfaceAreaId && Vector3.Dot(previous.WallNormal, wallNormal) >= kWallSmoothDotThreshold;
        }

        private static bool TryCompactAndCreateResult(MeshData sourceMesh, List<Vector3> workingVertices, List<Vector3> workingNormals, List<Vector4> workingTangents, List<Vector4> workingUV0, List<Color32> workingVertexColors, List<Vector4> workingTexCoord1, List<Vector4> workingTexCoord3, List<StructuralFace> workingFaces, List<int> faceSurfaceAreaIds, List<int> uncompactCapRenderVertices, List<int> uncompactCapBoundaryRenderPairs, out FaceExtrusionMutation result)
        {
            result = null;
            bool[] used = new bool[workingVertices.Count];

            for (int faceIndex = 0; faceIndex < workingFaces.Count; faceIndex++)
            {
                StructuralFace face = workingFaces[faceIndex];
                for (int cornerIndex = 0; cornerIndex < face.CornerCount; cornerIndex++)
                {
                    int renderVertex = face.GetRenderVertexIndex(cornerIndex);
                    if (renderVertex < 0 || renderVertex >= used.Length)
                    {
                        return false;
                    }

                    used[renderVertex] = true;
                }
            }

            int[] oldToNew = new int[workingVertices.Count];
            for (int i = 0; i < oldToNew.Length; i++)
            {
                oldToNew[i] = -1;
            }

            List<Vector3> vertices = new List<Vector3>(workingVertices.Count);
            List<Vector3> normals = workingNormals != null ? new List<Vector3>(workingNormals.Count) : null;
            List<Vector4> tangents = workingTangents != null ? new List<Vector4>(workingTangents.Count) : null;
            List<Vector4> uv0 = workingUV0 != null ? new List<Vector4>(workingUV0.Count) : null;
            List<Color32> vertexColors = workingVertexColors != null ? new List<Color32>(workingVertexColors.Count) : null;
            List<Vector4> texCoord1 = workingTexCoord1 != null ? new List<Vector4>(workingTexCoord1.Count) : null;
            List<Vector4> texCoord3 = workingTexCoord3 != null ? new List<Vector4>(workingTexCoord3.Count) : null;

            for (int oldIndex = 0; oldIndex < used.Length; oldIndex++)
            {
                if (!used[oldIndex])
                {
                    continue;
                }

                oldToNew[oldIndex] = vertices.Count;
                vertices.Add(workingVertices[oldIndex]);
                if (normals != null)
                {
                    normals.Add(workingNormals[oldIndex]);
                }

                if (tangents != null)
                {
                    tangents.Add(workingTangents[oldIndex]);
                }

                if (uv0 != null)
                {
                    uv0.Add(workingUV0[oldIndex]);
                }

                if (vertexColors != null)
                {
                    vertexColors.Add(workingVertexColors[oldIndex]);
                }

                if (texCoord1 != null)
                {
                    texCoord1.Add(workingTexCoord1[oldIndex]);
                }

                if (texCoord3 != null)
                {
                    texCoord3.Add(workingTexCoord3[oldIndex]);
                }
            }

            StructuralFace[] faces = new StructuralFace[workingFaces.Count];
            for (int faceIndex = 0; faceIndex < workingFaces.Count; faceIndex++)
            {
                StructuralFace face = workingFaces[faceIndex];
                int[] corners = new int[face.CornerCount];
                for (int cornerIndex = 0; cornerIndex < face.CornerCount; cornerIndex++)
                {
                    int remapped = oldToNew[face.GetRenderVertexIndex(cornerIndex)];
                    if (remapped < 0)
                    {
                        return false;
                    }

                    corners[cornerIndex] = remapped;
                }

                faces[faceIndex] = CreateFace(faceIndex, corners);
            }

            int[] capRenderVertices = RemapRenderVertices(uncompactCapRenderVertices, oldToNew);
            int[] capBoundaryRenderPairs = RemapRenderVertices(uncompactCapBoundaryRenderPairs, oldToNew);
            if (capRenderVertices == null || capBoundaryRenderPairs == null)
            {
                return false;
            }

            int[] triangles = BuildTriangles(faces);
            int[] triangleSurfaceAreaIds = BuildTriangleSurfaceAreaMembership(faces, faceSurfaceAreaIds);
            MeshData resultMesh = new MeshData(vertices.ToArray(), normals != null ? normals.ToArray() : null, tangents != null ? tangents.ToArray() : null, uv0 != null ? uv0.ToArray() : null, vertexColors != null ? vertexColors.ToArray() : null, texCoord1 != null ? texCoord1.ToArray() : null, texCoord3 != null ? texCoord3.ToArray() : null, triangles);

            if (sourceMesh.Normals != null)
            {
                resultMesh.RecalculateNormals();
            }

            if (sourceMesh.Tangents != null && resultMesh.UV0 != null && resultMesh.Normals != null)
            {
                resultMesh.RecalculateTangents();
            }

            result = new FaceExtrusionMutation(new MeshMutationResult(resultMesh.Vertices, resultMesh.Normals, resultMesh.Tangents, resultMesh.UV0, resultMesh.VertexColors, resultMesh.TexCoord1, resultMesh.TexCoord3, resultMesh.Triangles, faces, triangleSurfaceAreaIds), capRenderVertices, capBoundaryRenderPairs);
            result.MeshMutation.SetHistoryVertexMap(sourceMesh.Vertices.Length, oldToNew);
            return true;
        }

        private static int[] RemapRenderVertices(IReadOnlyList<int> source, int[] oldToNew)
        {
            int[] result = new int[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                int sourceIndex = source[i];
                if (sourceIndex < 0 || sourceIndex >= oldToNew.Length || oldToNew[sourceIndex] < 0)
                {
                    return null;
                }

                result[i] = oldToNew[sourceIndex];
            }

            return result;
        }

        private static StructuralFace CreateFace(int id, int[] corners)
        {
            return corners.Length == 3 ? new StructuralFace(id, corners[0], corners[1], corners[2]) : new StructuralFace(id, corners[0], corners[1], corners[2], corners[3]);
        }

        private static int[] BuildTriangles(IReadOnlyList<StructuralFace> faces)
        {
            int indexCount = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                indexCount += faces[i].IsTriangle ? 3 : 6;
            }

            int[] triangles = new int[indexCount];
            int offset = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                StructuralFace face = faces[i];
                int v0 = face.GetRenderVertexIndex(0);
                int v1 = face.GetRenderVertexIndex(1);
                int v2 = face.GetRenderVertexIndex(2);
                triangles[offset++] = v0;
                triangles[offset++] = v1;
                triangles[offset++] = v2;
                if (face.IsQuad)
                {
                    triangles[offset++] = v0;
                    triangles[offset++] = v2;
                    triangles[offset++] = face.GetRenderVertexIndex(3);
                }
            }

            return triangles;
        }

        private static int[] BuildTriangleSurfaceAreaMembership(IReadOnlyList<StructuralFace> faces, IReadOnlyList<int> faceSurfaceAreaIds)
        {
            if (faces.Count != faceSurfaceAreaIds.Count)
            {
                throw new InvalidOperationException("Extrusion face Surface Area membership is incomplete.");
            }

            int triangleCount = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                triangleCount += faces[i].IsTriangle ? 1 : 2;
            }

            int[] result = new int[triangleCount];
            int triangleIndex = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                result[triangleIndex++] = faceSurfaceAreaIds[i];
                if (faces[i].IsQuad)
                {
                    result[triangleIndex++] = faceSurfaceAreaIds[i];
                }
            }

            return result;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}