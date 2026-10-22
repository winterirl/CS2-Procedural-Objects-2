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
        public static bool TryCreateLoopDissolveMutation(MeshPart part, IReadOnlyList<int> selectedLogicalVertexIds, out MeshMutationResult mutation, out LoopDissolvePlan plan)
        {
            mutation = null;
            plan = null;

            if (!TryAnalyzeLoopDissolve(part, selectedLogicalVertexIds, out plan))
            {
                return false;
            }

            return TryCreateDissolveMutation(part, plan.Segments, out mutation);
        }

        public static bool TryCreateSurfaceCutDissolveMutation(MeshPart part, IReadOnlyList<int> selectedLogicalVertexIds, out MeshMutationResult mutation, out SurfaceCutDissolvePlan plan)
        {
            mutation = null;
            plan = null;

            if (!TryAnalyzeSurfaceCutDissolve(part, selectedLogicalVertexIds, out plan))
            {
                return false;
            }

            return TryCreateDissolveMutation(part, plan.Segments, out mutation);
        }

        private static bool TryCreateDissolveMutation(MeshPart part, IReadOnlyList<DissolveSegment> segments, out MeshMutationResult mutation)
        {
            mutation = null;

            if (part == null || segments == null || segments.Count == 0)
            {
                return false;
            }

            MeshData mesh = part.Mesh;
            StructuralTopology structuralTopology = part.StructuralTopology;

            // Analysis checked the loop, but overlapping faces must still be rejected
            // before building replacement arrays.
            Dictionary<int, int> affectedFaceToSegment = new Dictionary<int, int>(segments.Count * 2);
            StructuralQuad[] mergedOldIndexQuads = new StructuralQuad[segments.Count];

            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                DissolveSegment segment = segments[segmentIndex];

                if (affectedFaceToSegment.ContainsKey(segment.SideAQuadId) || affectedFaceToSegment.ContainsKey(segment.SideBQuadId))
                {
                    return false;
                }

                affectedFaceToSegment.Add(segment.SideAQuadId, segmentIndex);
                affectedFaceToSegment.Add(segment.SideBQuadId, segmentIndex);

                if (!TryBuildDissolvedQuad(part, segment, out StructuralQuad mergedQuad))
                {
                    return false;
                }

                mergedOldIndexQuads[segmentIndex] = mergedQuad;
            }

            List<StructuralFace> uncompactFaces = new List<StructuralFace>(structuralTopology.FaceCount - segments.Count);
            List<int> uncompactFaceSurfaceAreaIds = new List<int>(structuralTopology.FaceCount - segments.Count);
            bool[] emittedMergedSegment = new bool[segments.Count];

            for (int faceIndex = 0; faceIndex < structuralTopology.FaceCount; faceIndex++)
            {
                StructuralFace face = structuralTopology.GetFace(faceIndex);

                if (!part.TryGetStructuralFaceSurfaceAreaId(face.Id, out int sourceSurfaceAreaId))
                {
                    return false;
                }

                if (!affectedFaceToSegment.TryGetValue(face.Id, out int segmentIndex))
                {
                    uncompactFaces.Add(face.CloneWithId(uncompactFaces.Count));
                    uncompactFaceSurfaceAreaIds.Add(sourceSurfaceAreaId);
                    continue;
                }

                if (!face.IsQuad)
                {
                    return false;
                }

                if (emittedMergedSegment[segmentIndex])
                {
                    continue;
                }

                DissolveSegment segment = segments[segmentIndex];
                int firstAffectedFaceId = Math.Min(segment.SideAQuadId, segment.SideBQuadId);

                if (face.Id != firstAffectedFaceId)
                {
                    continue;
                }

                StructuralQuad mergedQuad = mergedOldIndexQuads[segmentIndex];

                int otherFaceId = face.Id == segment.SideAQuadId ? segment.SideBQuadId : segment.SideAQuadId;
                if (!part.TryGetStructuralFaceSurfaceAreaId(otherFaceId, out int otherSurfaceAreaId) || otherSurfaceAreaId != sourceSurfaceAreaId)
                {
                    return false;
                }

                uncompactFaces.Add(new StructuralQuad(uncompactFaces.Count, mergedQuad.GetRenderVertexIndex(0), mergedQuad.GetRenderVertexIndex(1), mergedQuad.GetRenderVertexIndex(2), mergedQuad.GetRenderVertexIndex(3)));
                uncompactFaceSurfaceAreaIds.Add(sourceSurfaceAreaId);
                emittedMergedSegment[segmentIndex] = true;
            }

            for (int i = 0; i < emittedMergedSegment.Length; i++)
            {
                if (!emittedMergedSegment[i])
                {
                    return false;
                }
            }

            // Dissolving leaves the old cut vertices orphaned. Compact every
            // stream together so render indices remain aligned afterwards.
            bool[] usedRenderVertices = new bool[mesh.Vertices.Length];

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

            int[] oldToNewRenderIndex = new int[mesh.Vertices.Length];
            for (int i = 0; i < oldToNewRenderIndex.Length; i++)
            {
                oldToNewRenderIndex[i] = -1;
            }

            List<Vector3> vertices = new List<Vector3>(mesh.Vertices.Length);
            List<Vector3> normals = mesh.Normals != null ? new List<Vector3>(mesh.Normals.Length) : null;
            List<Vector4> tangents = mesh.Tangents != null ? new List<Vector4>(mesh.Tangents.Length) : null;
            List<Vector4> uv0 = mesh.UV0 != null ? new List<Vector4>(mesh.UV0.Length) : null;
            List<Color32> vertexColors = mesh.VertexColors != null ? new List<Color32>(mesh.VertexColors.Length) : null;
            List<Vector4> texCoord1 = mesh.TexCoord1 != null ? new List<Vector4>(mesh.TexCoord1.Length) : null;
            List<Vector4> texCoord3 = mesh.TexCoord3 != null ? new List<Vector4>(mesh.TexCoord3.Length) : null;

            for (int oldIndex = 0; oldIndex < usedRenderVertices.Length; oldIndex++)
            {
                if (!usedRenderVertices[oldIndex])
                {
                    continue;
                }

                int newIndex = vertices.Count;
                oldToNewRenderIndex[oldIndex] = newIndex;
                vertices.Add(mesh.Vertices[oldIndex]);

                if (normals != null)
                {
                    normals.Add(mesh.Normals[oldIndex]);
                }

                if (tangents != null)
                {
                    tangents.Add(mesh.Tangents[oldIndex]);
                }

                if (uv0 != null)
                {
                    uv0.Add(mesh.UV0[oldIndex]);
                }

                if (vertexColors != null)
                {
                    vertexColors.Add(mesh.VertexColors[oldIndex]);
                }

                if (texCoord1 != null)
                {
                    texCoord1.Add(mesh.TexCoord1[oldIndex]);
                }

                if (texCoord3 != null)
                {
                    texCoord3.Add(mesh.TexCoord3[oldIndex]);
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

            mutation = new MeshMutationResult(resultMesh.Vertices, resultMesh.Normals, resultMesh.Tangents, resultMesh.UV0, resultMesh.VertexColors, resultMesh.TexCoord1, resultMesh.TexCoord3, resultMesh.Triangles, compactFaces.ToArray(), triangleSurfaceAreaIds);
            mutation.SetHistoryVertexMap(mesh.Vertices.Length, oldToNewRenderIndex);
            return true;
        }

        private static bool TryGetDissolveQuadSide(MeshPart part, int quadId, int loopEdgeId, int segmentStartLogicalVertexId, int segmentEndLogicalVertexId, HashSet<int> selectedVertices, out DissolveQuadSide side)
        {
            side = default;

            StructuralTopology structuralTopology = part.StructuralTopology;
            MeshTopology meshTopology = part.Topology;

            if (!structuralTopology.TryGetBoundaryIndex(quadId, loopEdgeId, out int boundaryIndex))
            {
                return false;
            }

            if (!structuralTopology.TryGetQuad(quadId, out StructuralFace quad))
            {
                return false;
            }

            int render0 = quad.GetRenderVertexIndex(boundaryIndex);
            int render1 = quad.GetRenderVertexIndex((boundaryIndex + 1) % 4);
            int render2 = quad.GetRenderVertexIndex((boundaryIndex + 2) % 4);
            int render3 = quad.GetRenderVertexIndex((boundaryIndex + 3) % 4);

            int logical0 = meshTopology.GetLogicalVertexIdForRenderVertex(render0);
            int logical1 = meshTopology.GetLogicalVertexIdForRenderVertex(render1);
            int logical2 = meshTopology.GetLogicalVertexIdForRenderVertex(render2);
            int logical3 = meshTopology.GetLogicalVertexIdForRenderVertex(render3);

            if (!selectedVertices.Contains(logical0) || !selectedVertices.Contains(logical1) || selectedVertices.Contains(logical2) || selectedVertices.Contains(logical3))
            {
                return false;
            }

            int outerStartLogicalVertexId;
            int outerEndLogicalVertexId;

            if (logical0 == segmentStartLogicalVertexId && logical1 == segmentEndLogicalVertexId)
            {
                outerStartLogicalVertexId = logical3;
                outerEndLogicalVertexId = logical2;
            }
            else if (logical0 == segmentEndLogicalVertexId && logical1 == segmentStartLogicalVertexId)
            {
                outerStartLogicalVertexId = logical2;
                outerEndLogicalVertexId = logical3;
            }
            else
            {
                return false;
            }

            if (outerStartLogicalVertexId == outerEndLogicalVertexId || selectedVertices.Contains(outerStartLogicalVertexId) || selectedVertices.Contains(outerEndLogicalVertexId))
            {
                return false;
            }

            int oppositeEdgeId = structuralTopology.GetOppositeBoundaryEdgeId(quadId, boundaryIndex);
            StructuralEdge oppositeEdge = structuralTopology.GetEdge(oppositeEdgeId);

            bool oppositeMatches = (oppositeEdge.LogicalVertexA == outerStartLogicalVertexId && oppositeEdge.LogicalVertexB == outerEndLogicalVertexId) || (oppositeEdge.LogicalVertexA == outerEndLogicalVertexId && oppositeEdge.LogicalVertexB == outerStartLogicalVertexId);

            if (!oppositeMatches)
            {
                return false;
            }

            side = new DissolveQuadSide(quadId, boundaryIndex, outerStartLogicalVertexId, outerEndLogicalVertexId);
            return true;
        }


        private static bool TryBuildDissolvedQuad(MeshPart part, DissolveSegment segment, out StructuralQuad mergedQuad)
        {
            mergedQuad = default;

            MeshTopology meshTopology = part.Topology;
            StructuralTopology structuralTopology = part.StructuralTopology;

            if (!structuralTopology.TryGetQuad(segment.SideAQuadId, out StructuralFace sideAQuad) || !structuralTopology.TryGetQuad(segment.SideBQuadId, out StructuralFace sideBQuad))
            {
                return false;
            }

            int aBoundary = segment.SideABoundaryIndex;
            int bBoundary = segment.SideBBoundaryIndex;

            int aLoop0 = sideAQuad.GetRenderVertexIndex(aBoundary);
            int aLoop1 = sideAQuad.GetRenderVertexIndex((aBoundary + 1) % 4);
            int aOuter1 = sideAQuad.GetRenderVertexIndex((aBoundary + 2) % 4);
            int aOuter0 = sideAQuad.GetRenderVertexIndex((aBoundary + 3) % 4);

            int aLoopLogical0 = meshTopology.GetLogicalVertexIdForRenderVertex(aLoop0);
            int aLoopLogical1 = meshTopology.GetLogicalVertexIdForRenderVertex(aLoop1);

            if (!((aLoopLogical0 == segment.StartLogicalVertexId && aLoopLogical1 == segment.EndLogicalVertexId) || (aLoopLogical0 == segment.EndLogicalVertexId && aLoopLogical1 == segment.StartLogicalVertexId)))
            {
                return false;
            }

            int bLoop0 = sideBQuad.GetRenderVertexIndex(bBoundary);
            int bLoop1 = sideBQuad.GetRenderVertexIndex((bBoundary + 1) % 4);
            int bOuter1 = sideBQuad.GetRenderVertexIndex((bBoundary + 2) % 4);
            int bOuter0 = sideBQuad.GetRenderVertexIndex((bBoundary + 3) % 4);

            int bLoopLogical0 = meshTopology.GetLogicalVertexIdForRenderVertex(bLoop0);
            int bLoopLogical1 = meshTopology.GetLogicalVertexIdForRenderVertex(bLoop1);

            if (!((bLoopLogical0 == segment.StartLogicalVertexId && bLoopLogical1 == segment.EndLogicalVertexId) || (bLoopLogical0 == segment.EndLogicalVertexId && bLoopLogical1 == segment.StartLogicalVertexId)))
            {
                return false;
            }

            int sideBOuterForALoop0;
            int sideBOuterForALoop1;

            if (bLoopLogical0 == aLoopLogical0 && bLoopLogical1 == aLoopLogical1)
            {
                sideBOuterForALoop0 = bOuter0;
                sideBOuterForALoop1 = bOuter1;
            }
            else if (bLoopLogical0 == aLoopLogical1 && bLoopLogical1 == aLoopLogical0)
            {
                sideBOuterForALoop0 = bOuter1;
                sideBOuterForALoop1 = bOuter0;
            }
            else
            {
                return false;
            }

            int sideAOuterLogical0 = meshTopology.GetLogicalVertexIdForRenderVertex(aOuter0);
            int sideAOuterLogical1 = meshTopology.GetLogicalVertexIdForRenderVertex(aOuter1);
            int sideBOuterLogical0 = meshTopology.GetLogicalVertexIdForRenderVertex(sideBOuterForALoop0);
            int sideBOuterLogical1 = meshTopology.GetLogicalVertexIdForRenderVertex(sideBOuterForALoop1);

            if (sideAOuterLogical0 == sideBOuterLogical0 || sideAOuterLogical1 == sideBOuterLogical1)
            {
                return false;
            }

            // Keep Side A's winding by replacing its loop endpoints with Side B's outer endpoints.
            // The remaining render corners and UV seams stay intact.
            mergedQuad = new StructuralQuad(0, sideBOuterForALoop0, sideBOuterForALoop1, aOuter1, aOuter0);
            return true;
        }

        private static int FindMinimumVertexId(HashSet<int> vertexIds)
        {
            int minimum = int.MaxValue;

            foreach (int vertexId in vertexIds)
            {
                if (vertexId < minimum)
                {
                    minimum = vertexId;
                }
            }

            return minimum;
        }

        private readonly struct DissolveQuadSide
        {
            public int QuadId { get; }
            public int BoundaryIndex { get; }
            public int OuterStartLogicalVertexId { get; }
            public int OuterEndLogicalVertexId { get; }

            public DissolveQuadSide(int quadId, int boundaryIndex, int outerStartLogicalVertexId, int outerEndLogicalVertexId)
            {
                QuadId = quadId;
                BoundaryIndex = boundaryIndex;
                OuterStartLogicalVertexId = outerStartLogicalVertexId;
                OuterEndLogicalVertexId = outerEndLogicalVertexId;
            }
        }
    }
}