using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Geometry.Editing
{
    // This is the destructive mesh-editing side of the topology code,
    // though it never destructively edits the source part.
    // Everything here either analyzes an operation or builds a complete replacement mutation.
    public static partial class TopologyEditor
    {
        internal static TopologyEditCapabilities AnalyzeTopologyEditCapabilities(MeshPart part)
        {
            if (part == null || !part.SupportsDestructiveTopologyEditing)
            {
                return TopologyEditCapabilities.Blocked(TopologyEditBlockReason.ConvertedTopology);
            }

            // The cut builders require every render triangle to be represented by the structural faces.
            // Check it once here and cache the answer on the part instead of letting an apparently valid preview fail
            // when the user clicks to commit it.
            if (!IsFullyStructuralMesh(part))
            {
                return TopologyEditCapabilities.Blocked(TopologyEditBlockReason.IncompleteStructuralTopology);
            }

            StructuralTopology topology = part.StructuralTopology;
            bool canLoopCut = false;
            bool canSurfaceCut = false;
            bool[] examinedEdges = new bool[topology.StructuralEdgeCount];

            // Each successful walk marks all of its cross-strip edges.
            // A grid can contain many parallel strips, but there is no reason to walk
            // the same strip again just to answer a pair of UI booleans.
            for (int edgeId = 0; edgeId < topology.StructuralEdgeCount; edgeId++)
            {
                if (examinedEdges[edgeId])
                {
                    continue;
                }

                examinedEdges[edgeId] = true;

                if (!TryBuildStructuralLoop(topology, edgeId, out StructuralLoop strip))
                {
                    continue;
                }

                MarkStripEdgesExamined(strip, examinedEdges);

                if (strip.IsClosed)
                {
                    canLoopCut = true;
                }
                else if (HasExactlyTwoBoundaryTermini(topology, strip))
                {
                    canSurfaceCut = true;
                }

                if (canLoopCut && canSurfaceCut)
                {
                    break;
                }
            }

            return new TopologyEditCapabilities(canLoopCut, canSurfaceCut, true, TopologyEditBlockReason.NoClosedQuadStrip, TopologyEditBlockReason.NoOpenQuadStrip, TopologyEditBlockReason.None);
        }

        private static bool IsFullyStructuralMesh(MeshPart part)
        {
            if (part == null || !part.SupportsDestructiveTopologyEditing)
            {
                return false;
            }

            StructuralTopology topology = part.StructuralTopology;
            int[] triangles = part.Mesh.Triangles;

            if (topology.FaceCount == 0)
            {
                return false;
            }

            // Face ids alone are not enough. The structural face order and
            // triangulation have to match the render triangle buffer exactly.
            int triangleOffset = 0;

            for (int faceIndex = 0; faceIndex < topology.FaceCount; faceIndex++)
            {
                StructuralFace face = topology.GetFace(faceIndex);

                if (face.IsTriangle)
                {
                    if (triangleOffset + 3 > triangles.Length || triangles[triangleOffset] != face.GetRenderVertexIndex(0) || triangles[triangleOffset + 1] != face.GetRenderVertexIndex(1) || triangles[triangleOffset + 2] != face.GetRenderVertexIndex(2))
                    {
                        return false;
                    }

                    triangleOffset += 3;
                    continue;
                }

                if (!face.IsQuad || triangleOffset + 6 > triangles.Length || triangles[triangleOffset] != face.GetRenderVertexIndex(0) || triangles[triangleOffset + 1] != face.GetRenderVertexIndex(1) || triangles[triangleOffset + 2] != face.GetRenderVertexIndex(2) || triangles[triangleOffset + 3] != face.GetRenderVertexIndex(0) || triangles[triangleOffset + 4] != face.GetRenderVertexIndex(2) || triangles[triangleOffset + 5] != face.GetRenderVertexIndex(3))
                {
                    return false;
                }

                triangleOffset += 6;
            }

            return triangleOffset == triangles.Length;
        }

        private static int[] BuildTriangleSurfaceAreaMembership(IReadOnlyList<StructuralFace> faces, IReadOnlyList<int> faceSurfaceAreaIds)
        {
            if (faces == null || faceSurfaceAreaIds == null || faces.Count != faceSurfaceAreaIds.Count)
            {
                throw new ArgumentException("Structural faces and Surface Area ids must align.");
            }

            int triangleCount = 0;
            for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
            {
                triangleCount += faces[faceIndex].IsTriangle ? 1 : 2;
            }

            int[] memberships = new int[triangleCount];
            int triangleIndex = 0;

            for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
            {
                int surfaceAreaId = faceSurfaceAreaIds[faceIndex];
                memberships[triangleIndex++] = surfaceAreaId;

                if (faces[faceIndex].IsQuad)
                {
                    memberships[triangleIndex++] = surfaceAreaId;
                }
            }

            return memberships;
        }

        private static int[] BuildTriangles(IReadOnlyList<StructuralFace> faces)
        {
            int triangleIndexCount = 0;

            for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
            {
                StructuralFace face = faces[faceIndex];
                triangleIndexCount += face.IsTriangle ? 3 : 6;
            }

            // Structural quads have a fixed 0-1-2 / 0-2-3 split everywhere in
            // the editor. IsFullyStructuralMesh relies on this exact ordering.
            int[] triangles = new int[triangleIndexCount];
            int offset = 0;

            for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
            {
                StructuralFace face = faces[faceIndex];
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

        private static StructuralFace CreateStructuralFace(int id, int[] renderVertexIndices)
        {
            if (renderVertexIndices == null)
            {
                throw new ArgumentNullException(nameof(renderVertexIndices));
            }

            if (renderVertexIndices.Length == 3)
            {
                return new StructuralFace(id, renderVertexIndices[0], renderVertexIndices[1], renderVertexIndices[2]);
            }

            if (renderVertexIndices.Length == 4)
            {
                return new StructuralQuad(id, renderVertexIndices[0], renderVertexIndices[1], renderVertexIndices[2], renderVertexIndices[3]);
            }

            throw new ArgumentException("Structural faces must contain three or four render vertices.", nameof(renderVertexIndices));
        }
    }
}