using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Geometry.Editing
{
    public static partial class TopologyEditor
    {
        public static bool TryCreateLoopCutMutation(MeshPart part, int startStructuralEdgeId, float t, out MeshMutationResult mutation, out int[] newRenderVertexIndices, out StructuralLoop loop)
        {
            return TryCreateLoopCutMutation(part, startStructuralEdgeId, 1, t, out mutation, out newRenderVertexIndices, out loop);
        }

        public static bool TryCreateLoopCutMutation(MeshPart part, int startStructuralEdgeId, int cutCount, float t, out MeshMutationResult mutation, out int[] newRenderVertexIndices, out StructuralLoop loop)
        {
            mutation = null;
            newRenderVertexIndices = Array.Empty<int>();
            loop = null;

            if (part == null || !TryBuildClosedStructuralLoop(part.StructuralTopology, startStructuralEdgeId, out loop))
            {
                return false;
            }

            return TryCreateQuadStripCutMutation(part, loop, cutCount, t, out mutation, out newRenderVertexIndices);
        }

        public static bool TryCreateSurfaceCutMutation(MeshPart part, int startStructuralEdgeId, float t, out MeshMutationResult mutation, out int[] newRenderVertexIndices, out StructuralLoop strip)
        {
            return TryCreateSurfaceCutMutation(part, startStructuralEdgeId, 1, t, out mutation, out newRenderVertexIndices, out strip);
        }

        public static bool TryCreateSurfaceCutMutation(MeshPart part, int startStructuralEdgeId, int cutCount, float t, out MeshMutationResult mutation, out int[] newRenderVertexIndices, out StructuralLoop strip)
        {
            mutation = null;
            newRenderVertexIndices = Array.Empty<int>();
            strip = null;

            if (part == null || !TryBuildOpenStructuralStrip(part.StructuralTopology, startStructuralEdgeId, out strip))
            {
                return false;
            }

            return TryCreateQuadStripCutMutation(part, strip, cutCount, t, out mutation, out newRenderVertexIndices);
        }

        private static bool TryCreateQuadStripCutMutation(MeshPart part, StructuralLoop loop, int cutCount, float t, out MeshMutationResult mutation, out int[] newRenderVertexIndices)
        {
            mutation = null;
            newRenderVertexIndices = Array.Empty<int>();

            if (part == null || loop == null || !CutLayout.IsValid(cutCount, t) || !IsFullyStructuralMesh(part))
            {
                return false;
            }

            // Cuts only work while structural faces still describe the entire triangle buffer.
            // Trying to preserve a half-structural mesh here would turn every later edit into an archaeology expedition.
            MeshData mesh = part.Mesh;

            List<Vector3> vertices = new List<Vector3>(mesh.Vertices);
            List<Vector4> uv0 = mesh.UV0 != null ? new List<Vector4>(mesh.UV0) : null;
            List<Vector3> normals = mesh.Normals != null ? new List<Vector3>(mesh.Normals) : null;
            List<Vector4> tangents = mesh.Tangents != null ? new List<Vector4>(mesh.Tangents) : null;
            List<Color32> vertexColors = mesh.VertexColors != null ? new List<Color32>(mesh.VertexColors) : null;
            List<Vector4> texCoord1 = mesh.TexCoord1 != null ? new List<Vector4>(mesh.TexCoord1) : null;
            List<Vector4> texCoord3 = mesh.TexCoord3 != null ? new List<Vector4>(mesh.TexCoord3) : null;

            Dictionary<int, StructuralLoopStep> affectedFaces = new Dictionary<int, StructuralLoopStep>();

            for (int i = 0; i < loop.Steps.Count; i++)
            {
                StructuralLoopStep step = loop.Steps[i];
                if (affectedFaces.ContainsKey(step.QuadId))
                {
                    return false;
                }

                affectedFaces.Add(step.QuadId, step);
            }

            List<StructuralFace> newFaces = new List<StructuralFace>(checked(part.StructuralTopology.FaceCount + loop.StepCount * cutCount));
            List<int> newFaceSurfaceAreaIds = new List<int>(checked(part.StructuralTopology.FaceCount + loop.StepCount * cutCount));
            List<int> createdRenderVertices = new List<int>(checked(loop.StepCount * cutCount * 2));

            for (int faceIndex = 0; faceIndex < part.StructuralTopology.FaceCount; faceIndex++)
            {
                StructuralFace face = part.StructuralTopology.GetFace(faceIndex);

                if (!part.TryGetStructuralFaceSurfaceAreaId(face.Id, out int sourceSurfaceAreaId))
                {
                    return false;
                }

                if (!affectedFaces.TryGetValue(face.Id, out StructuralLoopStep step))
                {
                    newFaces.Add(face.CloneWithId(newFaces.Count));
                    newFaceSurfaceAreaIds.Add(sourceSurfaceAreaId);
                    continue;
                }

                if (!face.IsQuad)
                {
                    return false;
                }

                int i0 = step.EntryBoundaryIndex;
                int i1 = (i0 + 1) % 4;
                int i2 = (i0 + 2) % 4;
                int i3 = (i0 + 3) % 4;

                int v0 = face.GetRenderVertexIndex(i0);
                int v1 = face.GetRenderVertexIndex(i1);
                int v2 = face.GetRenderVertexIndex(i2);
                int v3 = face.GetRenderVertexIndex(i3);

                int previousA = v0;
                int previousB = v3;
                for (int cutIndex = 0; cutIndex < cutCount; cutIndex++)
                {
                    // Reversed faces visit the global cuts backwards so their
                    // children keep the source face's winding.
                    int globalIndex = step.ReverseParameter ? cutCount - 1 - cutIndex : cutIndex;
                    float fraction = CutLayout.GetFraction(cutCount, globalIndex, t);
                    float localT = step.ReverseParameter ? 1f - fraction : fraction;

                    // Keep render vertices separate at UV and normal seams.
                    // Each adjacent pair also identifies one actual cut edge.
                    int cutA = AppendInterpolatedVertex(v0, v1, localT, vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3);
                    int cutB = AppendInterpolatedVertex(v3, v2, localT, vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3);
                    if (cutA < 0 || cutB < 0) return false;
                    createdRenderVertices.Add(cutA);
                    createdRenderVertices.Add(cutB);

                    newFaces.Add(new StructuralQuad(newFaces.Count, previousA, cutA, cutB, previousB));
                    newFaceSurfaceAreaIds.Add(sourceSurfaceAreaId);
                    previousA = cutA;
                    previousB = cutB;
                }

                newFaces.Add(new StructuralQuad(newFaces.Count, previousA, v1, v2, previousB));
                newFaceSurfaceAreaIds.Add(sourceSurfaceAreaId);
            }

            int[] triangles = BuildTriangles(newFaces);
            int[] triangleSurfaceAreaIds = BuildTriangleSurfaceAreaMembership(newFaces, newFaceSurfaceAreaIds);

            MeshData resultMesh = new MeshData(vertices.ToArray(), normals != null ? normals.ToArray() : null, tangents != null ? tangents.ToArray() : null, uv0 != null ? uv0.ToArray() : null, vertexColors != null ? vertexColors.ToArray() : null, texCoord1 != null ? texCoord1.ToArray() : null, texCoord3 != null ? texCoord3.ToArray() : null, triangles);

            if (resultMesh.Normals != null)
            {
                resultMesh.RecalculateNormals();
            }

            if (resultMesh.Tangents != null && resultMesh.UV0 != null && resultMesh.Normals != null)
            {
                resultMesh.RecalculateTangents();
            }

            mutation = new MeshMutationResult(resultMesh.Vertices, resultMesh.Normals, resultMesh.Tangents, resultMesh.UV0, resultMesh.VertexColors, resultMesh.TexCoord1, resultMesh.TexCoord3, resultMesh.Triangles, newFaces.ToArray(), triangleSurfaceAreaIds);
            mutation.SetHistoryVertexMap(mesh.Vertices.Length, null);

            newRenderVertexIndices = createdRenderVertices.ToArray();
            return true;
        }


        public static int[] ResolveCutEdges(MeshPart part, int[] createdRenderVertices)
        {
            var edgeIds = new HashSet<int>();
            for (int i = 0; i + 1 < createdRenderVertices.Length; i += 2)
            {
                int a = part.Topology.GetLogicalVertexIdForRenderVertex(createdRenderVertices[i]);
                int b = part.Topology.GetLogicalVertexIdForRenderVertex(createdRenderVertices[i + 1]);
                if (part.StructuralTopology.TryGetEdgeByLogicalVertices(a, b, out var edge))
                {
                    edgeIds.Add(edge.Id);
                }
            }

            int[] result = new int[edgeIds.Count];
            edgeIds.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        private static int AppendInterpolatedVertex(int renderA, int renderB, float t, List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector4> uv0, List<Color32> vertexColors, List<Vector4> texCoord1, List<Vector4> texCoord3)
        {
            int newIndex = vertices.Count;

            vertices.Add(Vector3.Lerp(vertices[renderA], vertices[renderB], t));

            if (uv0 != null)
            {
                uv0.Add(Vector4.Lerp(uv0[renderA], uv0[renderB], t));
            }

            if (vertexColors != null)
            {
                Color color = Color.Lerp(vertexColors[renderA], vertexColors[renderB], t);
                vertexColors.Add((Color32)color);
            }

            if (texCoord1 != null)
            {
                if (!SubdivisionBasis.SameBits(texCoord1[renderA], texCoord1[renderB]))
                    return -1;
                texCoord1.Add(texCoord1[renderA]);
            }

            if (texCoord3 != null)
            {
                Vector4 value = Vector4.Lerp(texCoord3[renderA], texCoord3[renderB], t);
                float magnitude = value.magnitude;
                texCoord3.Add(magnitude > 0.000001f ? value / magnitude : new Vector4(0f, 0f, 0f, 1f));
            }

            if (normals != null)
            {
                Vector3 normal = Vector3.Lerp(normals[renderA], normals[renderB], t);
                normals.Add(normal.sqrMagnitude > 0f ? normal.normalized : Vector3.up);
            }

            if (tangents != null)
            {
                tangents.Add(Vector4.Lerp(tangents[renderA], tangents[renderB], t));
            }

            return newIndex;
        }
    }
}