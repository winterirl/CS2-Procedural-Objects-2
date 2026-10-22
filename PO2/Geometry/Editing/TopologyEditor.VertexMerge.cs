using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Geometry.Editing
{
    public static partial class TopologyEditor
    {
        private const int kMergePreviewLogicalVertexId = -1;
        private const float kMergeTriangleCrossSqrEpsilon = 0.000000000001f;


        public static bool TryCreateVertexMergeMutation(MeshPart part, IReadOnlyList<int> selectedLogicalVertexIds, out MeshMutationResult mutation, out int[] mergedRenderVertexIndices, out VertexMergePlan plan)
        {
            mutation = null;
            mergedRenderVertexIndices = Array.Empty<int>();
            plan = null;

            if (!TryAnalyzeVertexMerge(part, selectedLogicalVertexIds, out plan))
            {
                return false;
            }

            MeshData mesh = part.Mesh;
            StructuralTopology structuralTopology = part.StructuralTopology;

            // Each affected face gets its own merged render vertex. They all
            // share the target position logically, but this preserves seams
            // and lets each face average its own attributes.
            List<Vector3> workingVertices = new List<Vector3>(mesh.Vertices);
            List<Vector3> workingNormals = mesh.Normals != null ? new List<Vector3>(mesh.Normals) : null;
            List<Vector4> workingTangents = mesh.Tangents != null ? new List<Vector4>(mesh.Tangents) : null;
            List<Vector4> workingUV0 = mesh.UV0 != null ? new List<Vector4>(mesh.UV0) : null;
            List<Color32> workingVertexColors = mesh.VertexColors != null ? new List<Color32>(mesh.VertexColors) : null;
            List<Vector4> workingTexCoord1 = mesh.TexCoord1 != null ? new List<Vector4>(mesh.TexCoord1) : null;
            List<Vector4> workingTexCoord3 = mesh.TexCoord3 != null ? new List<Vector4>(mesh.TexCoord3) : null;

            List<StructuralFace> uncompactFaces = new List<StructuralFace>(plan.ResultFaceCount);
            List<int> uncompactFaceSurfaceAreaIds = new List<int>(plan.ResultFaceCount);
            List<int> uncompactMergedRenderIndices = new List<int>(plan.AffectedFaceCount);

            for (int facePlanIndex = 0; facePlanIndex < plan.Faces.Count; facePlanIndex++)
            {
                VertexMergeFacePlan facePlan = plan.Faces[facePlanIndex];

                if (facePlan.SourceFaceId < 0 || facePlan.SourceFaceId >= structuralTopology.FaceCount)
                {
                    return false;
                }

                StructuralFace sourceFace = structuralTopology.GetFace(facePlan.SourceFaceId);

                if (facePlan.Result == VertexMergeFaceResult.Removed)
                {
                    continue;
                }

                if (!part.TryGetStructuralFaceSurfaceAreaId(sourceFace.Id, out int sourceSurfaceAreaId))
                {
                    return false;
                }

                int[] resultRenderIndices = new int[facePlan.ResultCornerCount];
                int mergedRenderIndex = -1;

                for (int cornerIndex = 0; cornerIndex < facePlan.ResultCornerCount; cornerIndex++)
                {
                    VertexMergeCornerPlan cornerPlan = facePlan.ResultCorners[cornerIndex];

                    if (!cornerPlan.IsMergedCorner)
                    {
                        if (cornerPlan.SourceCornerIndex < 0 || cornerPlan.SourceCornerIndex >= sourceFace.CornerCount)
                        {
                            return false;
                        }

                        resultRenderIndices[cornerIndex] = sourceFace.GetRenderVertexIndex(cornerPlan.SourceCornerIndex);
                        continue;
                    }

                    if (mergedRenderIndex >= 0 || facePlan.MergedSourceCornerIndices.Count == 0)
                    {
                        return false;
                    }

                    mergedRenderIndex = AppendMergedFaceVertex(mesh, sourceFace, facePlan.MergedSourceCornerIndices, plan.TargetPosition, workingVertices, workingNormals, workingTangents, workingUV0, workingVertexColors, workingTexCoord1, workingTexCoord3);

                    if (mergedRenderIndex < 0)
                    {
                        return false;
                    }

                    resultRenderIndices[cornerIndex] = mergedRenderIndex;
                    uncompactMergedRenderIndices.Add(mergedRenderIndex);
                }

                uncompactFaces.Add(CreateStructuralFace(uncompactFaces.Count, resultRenderIndices));
                uncompactFaceSurfaceAreaIds.Add(sourceSurfaceAreaId);
            }

            if (uncompactFaces.Count != plan.ResultFaceCount || uncompactMergedRenderIndices.Count == 0)
            {
                return false;
            }

            // Removed faces and replaced corners strand old render vertices,
            // so merge finishes with the same all-stream compaction as dissolve.
            bool[] usedRenderVertices = new bool[workingVertices.Count];

            for (int faceIndex = 0; faceIndex < uncompactFaces.Count; faceIndex++)
            {
                StructuralFace face = uncompactFaces[faceIndex];

                for (int cornerIndex = 0; cornerIndex < face.CornerCount; cornerIndex++)
                {
                    int renderVertexIndex = face.GetRenderVertexIndex(cornerIndex);

                    if (renderVertexIndex < 0 || renderVertexIndex >= usedRenderVertices.Length)
                    {
                        return false;
                    }

                    usedRenderVertices[renderVertexIndex] = true;
                }
            }

            int[] oldToNewRenderIndex = new int[workingVertices.Count];
            for (int i = 0; i < oldToNewRenderIndex.Length; i++)
            {
                oldToNewRenderIndex[i] = -1;
            }

            List<Vector3> vertices = new List<Vector3>(workingVertices.Count);
            List<Vector3> normals = workingNormals != null ? new List<Vector3>(workingNormals.Count) : null;
            List<Vector4> tangents = workingTangents != null ? new List<Vector4>(workingTangents.Count) : null;
            List<Vector4> uv0 = workingUV0 != null ? new List<Vector4>(workingUV0.Count) : null;
            List<Color32> vertexColors = workingVertexColors != null ? new List<Color32>(workingVertexColors.Count) : null;
            List<Vector4> texCoord1 = workingTexCoord1 != null ? new List<Vector4>(workingTexCoord1.Count) : null;
            List<Vector4> texCoord3 = workingTexCoord3 != null ? new List<Vector4>(workingTexCoord3.Count) : null;

            for (int oldIndex = 0; oldIndex < usedRenderVertices.Length; oldIndex++)
            {
                if (!usedRenderVertices[oldIndex])
                {
                    continue;
                }

                int newIndex = vertices.Count;
                oldToNewRenderIndex[oldIndex] = newIndex;
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

            List<StructuralFace> compactFaces = new List<StructuralFace>(uncompactFaces.Count);

            for (int faceIndex = 0; faceIndex < uncompactFaces.Count; faceIndex++)
            {
                StructuralFace face = uncompactFaces[faceIndex];
                int[] remapped = new int[face.CornerCount];

                for (int cornerIndex = 0; cornerIndex < face.CornerCount; cornerIndex++)
                {
                    int oldRenderIndex = face.GetRenderVertexIndex(cornerIndex);
                    int newRenderIndex = oldToNewRenderIndex[oldRenderIndex];

                    if (newRenderIndex < 0)
                    {
                        return false;
                    }

                    remapped[cornerIndex] = newRenderIndex;
                }

                compactFaces.Add(CreateStructuralFace(compactFaces.Count, remapped));
            }

            int[] compactMergedRenderIndices = new int[uncompactMergedRenderIndices.Count];

            for (int i = 0; i < uncompactMergedRenderIndices.Count; i++)
            {
                int compactIndex = oldToNewRenderIndex[uncompactMergedRenderIndices[i]];

                if (compactIndex < 0)
                {
                    return false;
                }

                compactMergedRenderIndices[i] = compactIndex;
            }

            int[] triangles = BuildTriangles(compactFaces);
            int[] triangleSurfaceAreaIds = BuildTriangleSurfaceAreaMembership(compactFaces, uncompactFaceSurfaceAreaIds);

            MeshData resultMesh = new MeshData(vertices.ToArray(), normals != null ? normals.ToArray() : null, tangents != null ? tangents.ToArray() : null, uv0 != null ? uv0.ToArray() : null, vertexColors != null ? vertexColors.ToArray() : null, texCoord1 != null ? texCoord1.ToArray() : null, texCoord3 != null ? texCoord3.ToArray() : null, triangles);

            if (resultMesh.Normals != null)
            {
                resultMesh.RecalculateNormals();
            }

            if (resultMesh.Tangents != null && resultMesh.UV0 != null && resultMesh.Normals != null)
            {
                resultMesh.RecalculateTangents();
            }

            MeshTopology resultTopology = MeshTopology.Build(resultMesh);
            StructuralTopology.Build(resultTopology, resultMesh.Vertices.Length, compactFaces.ToArray());

            int mergedLogicalVertexId = resultTopology.GetLogicalVertexIdForRenderVertex(compactMergedRenderIndices[0]);

            for (int i = 1; i < compactMergedRenderIndices.Length; i++)
            {
                if (resultTopology.GetLogicalVertexIdForRenderVertex(compactMergedRenderIndices[i]) != mergedLogicalVertexId)
                {
                    return false;
                }
            }

            mutation = new MeshMutationResult(resultMesh.Vertices, resultMesh.Normals, resultMesh.Tangents, resultMesh.UV0, resultMesh.VertexColors, resultMesh.TexCoord1, resultMesh.TexCoord3, resultMesh.Triangles, compactFaces.ToArray(), triangleSurfaceAreaIds);
            mutation.SetHistoryVertexMap(mesh.Vertices.Length, oldToNewRenderIndex);
            mergedRenderVertexIndices = compactMergedRenderIndices;
            return true;
        }


        public static bool TryAnalyzeVertexMerge(MeshPart part, IReadOnlyList<int> selectedLogicalVertexIds, out VertexMergePlan plan)
        {
            plan = null;

            if (part == null || selectedLogicalVertexIds == null || selectedLogicalVertexIds.Count < 2 || !IsFullyStructuralMesh(part))
            {
                return false;
            }

            MeshTopology meshTopology = part.Topology;
            StructuralTopology structuralTopology = part.StructuralTopology;

            // Do the annoying validity work before allocating the replacement
            // mesh. The resulting plan is deterministic and the mutation pass
            // should mostly just follow instructions.
            HashSet<int> selectedVertices = new HashSet<int>();
            int[] selectedIds = new int[selectedLogicalVertexIds.Count];
            Vector3 targetPosition = Vector3.zero;

            for (int i = 0; i < selectedLogicalVertexIds.Count; i++)
            {
                int logicalVertexId = selectedLogicalVertexIds[i];

                if (logicalVertexId < 0 || logicalVertexId >= meshTopology.LogicalVertexCount || !selectedVertices.Add(logicalVertexId))
                {
                    return false;
                }

                selectedIds[i] = logicalVertexId;
                targetPosition += meshTopology.GetVertex(logicalVertexId).GetPosition(part.Mesh);
            }

            targetPosition /= selectedIds.Length;
            Array.Sort(selectedIds);

            if (!AreSelectedVerticesStructurallyConnected(structuralTopology, selectedVertices, selectedIds[0]))
            {
                return false;
            }

            VertexMergeFacePlan[] facePlans = new VertexMergeFacePlan[structuralTopology.FaceCount];

            int resultFaceCount = 0;
            int resultQuadCount = 0;
            int resultTriangleCount = 0;
            int removedFaceCount = 0;
            int affectedFaceCount = 0;
            int survivingMergedCornerCount = 0;

            Dictionary<MergePreviewEdgeKey, int> edgeAdjacency = new Dictionary<MergePreviewEdgeKey, int>();
            HashSet<string> faceKeys = new HashSet<string>();

            for (int faceIndex = 0; faceIndex < structuralTopology.FaceCount; faceIndex++)
            {
                StructuralFace face = structuralTopology.GetFace(faceIndex);

                if (!TryAnalyzeMergeFace(part, face, selectedVertices, targetPosition, out VertexMergeFacePlan facePlan, out int[] previewLogicalCorners))
                {
                    return false;
                }

                facePlans[faceIndex] = facePlan;

                if (facePlan.Result != VertexMergeFaceResult.Unchanged)
                {
                    affectedFaceCount++;
                }

                if (facePlan.Result == VertexMergeFaceResult.Removed)
                {
                    removedFaceCount++;
                    continue;
                }

                resultFaceCount++;

                for (int cornerIndex = 0; cornerIndex < facePlan.ResultCorners.Count; cornerIndex++)
                {
                    if (facePlan.ResultCorners[cornerIndex].IsMergedCorner)
                    {
                        survivingMergedCornerCount++;
                    }
                }

                if (facePlan.ResultCornerCount == 3)
                {
                    resultTriangleCount++;
                }
                else if (facePlan.ResultCornerCount == 4)
                {
                    resultQuadCount++;
                }
                else
                {
                    return false;
                }

                if (!ValidateMergePreviewFace(previewLogicalCorners, facePlan, part, targetPosition, edgeAdjacency, faceKeys))
                {
                    return false;
                }
            }

            if (resultFaceCount == 0 || survivingMergedCornerCount == 0)
            {
                return false;
            }

            plan = new VertexMergePlan(selectedIds, targetPosition, facePlans, resultFaceCount, resultQuadCount, resultTriangleCount, removedFaceCount, affectedFaceCount);
            return true;
        }

        private static bool AreSelectedVerticesStructurallyConnected(StructuralTopology topology, HashSet<int> selectedVertices, int startLogicalVertexId)
        {
            HashSet<int> visited = new HashSet<int>();
            List<int> pending = new List<int>();
            int pendingIndex = 0;

            visited.Add(startLogicalVertexId);
            pending.Add(startLogicalVertexId);

            while (pendingIndex < pending.Count)
            {
                int current = pending[pendingIndex++];

                for (int edgeIndex = 0; edgeIndex < topology.StructuralEdgeCount; edgeIndex++)
                {
                    StructuralEdge edge = topology.GetEdge(edgeIndex);
                    int other = -1;

                    if (edge.LogicalVertexA == current)
                    {
                        other = edge.LogicalVertexB;
                    }
                    else if (edge.LogicalVertexB == current)
                    {
                        other = edge.LogicalVertexA;
                    }

                    if (other < 0 || !selectedVertices.Contains(other) || visited.Contains(other))
                    {
                        continue;
                    }

                    visited.Add(other);
                    pending.Add(other);
                }
            }

            return visited.Count == selectedVertices.Count;
        }

        private static bool TryAnalyzeMergeFace(MeshPart part, StructuralFace face, HashSet<int> selectedVertices, Vector3 targetPosition, out VertexMergeFacePlan facePlan, out int[] previewLogicalCorners)
        {
            facePlan = null;
            previewLogicalCorners = Array.Empty<int>();

            int cornerCount = face.CornerCount;
            int[] logicalCorners = new int[cornerCount];
            bool[] selectedCorners = new bool[cornerCount];
            int selectedCornerCount = 0;

            for (int cornerIndex = 0; cornerIndex < cornerCount; cornerIndex++)
            {
                int renderVertexIndex = face.GetRenderVertexIndex(cornerIndex);
                int logicalVertexId = part.Topology.GetLogicalVertexIdForRenderVertex(renderVertexIndex);

                logicalCorners[cornerIndex] = logicalVertexId;
                selectedCorners[cornerIndex] = selectedVertices.Contains(logicalVertexId);

                if (selectedCorners[cornerIndex])
                {
                    selectedCornerCount++;
                }
            }

            if (selectedCornerCount == 0)
            {
                VertexMergeCornerPlan[] unchangedCorners = new VertexMergeCornerPlan[cornerCount];

                for (int i = 0; i < cornerCount; i++)
                {
                    unchangedCorners[i] = new VertexMergeCornerPlan(false, i);
                }

                previewLogicalCorners = logicalCorners;
                facePlan = new VertexMergeFacePlan(face.Id, VertexMergeFaceResult.Unchanged, unchangedCorners, Array.Empty<int>());
                return true;
            }

            if (!SelectedCornersFormOneCyclicRun(selectedCorners))
            {
                return false;
            }

            List<VertexMergeCornerPlan> resultCorners = new List<VertexMergeCornerPlan>(cornerCount);
            List<int> previewCorners = new List<int>(cornerCount);
            List<int> mergedSourceCorners = new List<int>(selectedCornerCount);

            int firstUnselected = -1;
            for (int i = 0; i < cornerCount; i++)
            {
                if (!selectedCorners[i])
                {
                    firstUnselected = i;
                    break;
                }
            }

            if (firstUnselected < 0)
            {
                for (int i = 0; i < cornerCount; i++)
                {
                    mergedSourceCorners.Add(i);
                }
            }
            else
            {
                bool mergedCornerEmitted = false;

                for (int offset = 0; offset < cornerCount; offset++)
                {
                    int cornerIndex = (firstUnselected + offset) % cornerCount;

                    if (selectedCorners[cornerIndex])
                    {
                        mergedSourceCorners.Add(cornerIndex);

                        if (!mergedCornerEmitted)
                        {
                            resultCorners.Add(new VertexMergeCornerPlan(true, -1));
                            previewCorners.Add(kMergePreviewLogicalVertexId);
                            mergedCornerEmitted = true;
                        }
                    }
                    else
                    {
                        resultCorners.Add(new VertexMergeCornerPlan(false, cornerIndex));
                        previewCorners.Add(logicalCorners[cornerIndex]);
                    }
                }
            }

            if (resultCorners.Count < 3)
            {
                facePlan = new VertexMergeFacePlan(face.Id, VertexMergeFaceResult.Removed, Array.Empty<VertexMergeCornerPlan>(), mergedSourceCorners.ToArray());
                previewLogicalCorners = Array.Empty<int>();
                return true;
            }

            if (resultCorners.Count > 4)
            {
                return false;
            }

            VertexMergeFaceResult result = resultCorners.Count == 3 ? VertexMergeFaceResult.Triangle : VertexMergeFaceResult.Quad;

            facePlan = new VertexMergeFacePlan(face.Id, result, resultCorners.ToArray(), mergedSourceCorners.ToArray());
            previewLogicalCorners = previewCorners.ToArray();
            return true;
        }

        private static bool SelectedCornersFormOneCyclicRun(bool[] selectedCorners)
        {
            // The run is allowed to wrap from the last corner back to zero.
            // More than one run would create a face shape this merge plan
            // cannot represent safely.
            int selectedCount = 0;
            int runStarts = 0;

            for (int i = 0; i < selectedCorners.Length; i++)
            {
                if (!selectedCorners[i])
                {
                    continue;
                }

                selectedCount++;
                int previous = (i + selectedCorners.Length - 1) % selectedCorners.Length;

                if (!selectedCorners[previous])
                {
                    runStarts++;
                }
            }

            return selectedCount == selectedCorners.Length || runStarts == 1;
        }

        private static bool ValidateMergePreviewFace(int[] logicalCorners, VertexMergeFacePlan facePlan, MeshPart part, Vector3 targetPosition, Dictionary<MergePreviewEdgeKey, int> edgeAdjacency, HashSet<string> faceKeys)
        {
            if (logicalCorners == null || logicalCorners.Length < 3 || logicalCorners.Length > 4)
            {
                return false;
            }

            for (int i = 0; i < logicalCorners.Length; i++)
            {
                for (int j = i + 1; j < logicalCorners.Length; j++)
                {
                    if (logicalCorners[i] == logicalCorners[j])
                    {
                        return false;
                    }
                }
            }

            // Preview uses -1 as the synthetic merged logical vertex. Canonical
            // face/edge keys catch duplicates and non-manifold results before
            // any render vertices are actually created.
            string faceKey = BuildCanonicalMergeFaceKey(logicalCorners);
            if (!faceKeys.Add(faceKey))
            {
                return false;
            }

            for (int i = 0; i < logicalCorners.Length; i++)
            {
                MergePreviewEdgeKey edgeKey = new MergePreviewEdgeKey(logicalCorners[i], logicalCorners[(i + 1) % logicalCorners.Length]);

                if (!edgeAdjacency.TryGetValue(edgeKey, out int count))
                {
                    edgeAdjacency.Add(edgeKey, 1);
                }
                else
                {
                    count++;
                    if (count > 2)
                    {
                        return false;
                    }

                    edgeAdjacency[edgeKey] = count;
                }
            }

            Vector3 p0 = GetMergePreviewPosition(logicalCorners[0], part, targetPosition);
            Vector3 p1 = GetMergePreviewPosition(logicalCorners[1], part, targetPosition);
            Vector3 p2 = GetMergePreviewPosition(logicalCorners[2], part, targetPosition);

            if (!IsValidMergeTriangle(p0, p1, p2))
            {
                return false;
            }

            if (logicalCorners.Length == 4)
            {
                Vector3 p3 = GetMergePreviewPosition(logicalCorners[3], part, targetPosition);

                if (!IsValidMergeTriangle(p0, p2, p3))
                {
                    return false;
                }
            }

            return true;
        }

        private static Vector3 GetMergePreviewPosition(int logicalVertexId, MeshPart part, Vector3 targetPosition)
        {
            return logicalVertexId == kMergePreviewLogicalVertexId ? targetPosition : part.Topology.GetVertex(logicalVertexId).GetPosition(part.Mesh);
        }

        private static bool IsValidMergeTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            return cross.sqrMagnitude > kMergeTriangleCrossSqrEpsilon;
        }

        private static string BuildCanonicalMergeFaceKey(int[] logicalCorners)
        {
            string best = null;
            int count = logicalCorners.Length;

            for (int directionIndex = 0; directionIndex < 2; directionIndex++)
            {
                int direction = directionIndex == 0 ? 1 : -1;

                for (int start = 0; start < count; start++)
                {
                    System.Text.StringBuilder builder = new System.Text.StringBuilder(count * 12);

                    for (int offset = 0; offset < count; offset++)
                    {
                        int index = (start + direction * offset) % count;
                        if (index < 0)
                        {
                            index += count;
                        }

                        if (offset > 0)
                        {
                            builder.Append(',');
                        }

                        builder.Append(logicalCorners[index]);
                    }

                    string candidate = builder.ToString();
                    if (best == null || string.CompareOrdinal(candidate, best) < 0)
                    {
                        best = candidate;
                    }
                }
            }

            return best;
        }


        private readonly struct MergePreviewEdgeKey : IEquatable<MergePreviewEdgeKey>
        {
            public int VertexA { get; }
            public int VertexB { get; }

            public MergePreviewEdgeKey(int vertexA, int vertexB)
            {
                if (vertexA <= vertexB)
                {
                    VertexA = vertexA;
                    VertexB = vertexB;
                }
                else
                {
                    VertexA = vertexB;
                    VertexB = vertexA;
                }
            }

            public bool Equals(MergePreviewEdgeKey other)
            {
                return VertexA == other.VertexA && VertexB == other.VertexB;
            }

            public override bool Equals(object obj)
            {
                return obj is MergePreviewEdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (VertexA * 397) ^ VertexB;
                }
            }
        }

        private static int AppendMergedFaceVertex(MeshData sourceMesh, StructuralFace sourceFace, IReadOnlyList<int> mergedSourceCornerIndices, Vector3 targetPosition, List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector4> uv0, List<Color32> vertexColors, List<Vector4> texCoord1, List<Vector4> texCoord3)
        {
            if (sourceMesh == null || mergedSourceCornerIndices == null || mergedSourceCornerIndices.Count == 0)
            {
                return -1;
            }

            // Position is shared, attributes are averaged from only this face's
            // selected corners. Averaging across faces would smear UV seams.
            Vector4 mergedUV = Vector4.zero;
            Vector3 mergedNormal = Vector3.zero;
            Vector4 mergedTangent = Vector4.zero;
            Vector4 mergedColor = Vector4.zero;
            Vector4 mergedTexCoord1 = Vector4.zero;
            Vector4 mergedTexCoord3 = Vector4.zero;

            for (int i = 0; i < mergedSourceCornerIndices.Count; i++)
            {
                int sourceCornerIndex = mergedSourceCornerIndices[i];

                if (sourceCornerIndex < 0 || sourceCornerIndex >= sourceFace.CornerCount)
                {
                    return -1;
                }

                int sourceRenderIndex = sourceFace.GetRenderVertexIndex(sourceCornerIndex);

                if (sourceRenderIndex < 0 || sourceRenderIndex >= sourceMesh.Vertices.Length)
                {
                    return -1;
                }

                if (uv0 != null)
                {
                    mergedUV += sourceMesh.UV0[sourceRenderIndex];
                }

                if (normals != null)
                {
                    mergedNormal += sourceMesh.Normals[sourceRenderIndex];
                }

                if (tangents != null)
                {
                    mergedTangent += sourceMesh.Tangents[sourceRenderIndex];
                }

                if (vertexColors != null)
                {
                    Color32 color = sourceMesh.VertexColors[sourceRenderIndex];
                    mergedColor += new Vector4(color.r, color.g, color.b, color.a);
                }

                if (texCoord1 != null)
                {
                    if (i == 0) mergedTexCoord1 = sourceMesh.TexCoord1[sourceRenderIndex];
                    else if (!SubdivisionBasis.SameBits(mergedTexCoord1, sourceMesh.TexCoord1[sourceRenderIndex])) return -1;
                }

                if (texCoord3 != null)
                {
                    mergedTexCoord3 += sourceMesh.TexCoord3[sourceRenderIndex];
                }
            }

            float inverseCount = 1f / mergedSourceCornerIndices.Count;
            int newIndex = vertices.Count;

            vertices.Add(targetPosition);

            if (uv0 != null)
            {
                uv0.Add(mergedUV * inverseCount);
            }

            if (vertexColors != null)
            {
                Vector4 color = mergedColor * inverseCount;
                vertexColors.Add(new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(color.x), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(color.y), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(color.z), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(color.w), 0, 255)));
            }

            if (texCoord1 != null) texCoord1.Add(mergedTexCoord1);

            if (texCoord3 != null)
            {
                Vector4 value = mergedTexCoord3 * inverseCount;
                float magnitude = value.magnitude;
                texCoord3.Add(magnitude > 0.000001f ? value / magnitude : new Vector4(0f, 0f, 0f, 1f));
            }

            if (normals != null)
            {
                mergedNormal *= inverseCount;
                normals.Add(mergedNormal.sqrMagnitude > 0f ? mergedNormal.normalized : Vector3.up);
            }

            if (tangents != null)
            {
                tangents.Add(mergedTangent * inverseCount);
            }

            return newIndex;
        }
    }
}