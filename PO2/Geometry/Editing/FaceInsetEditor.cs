using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Tools.Gizmos;
using PO2.Tools.MeshEdit;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Geometry.Editing
{
    // Inset keeps the old perimeter in place, moves a copy of the selected
    // region boundary inward, and fills the space between them with quads.
    internal static class FaceInsetEditor
    {
        private const float kDirectionCrossEpsilon = 0.000001f;
        private const float kBarycentricTolerance = 0.0001f;
        private const float kMinimumWorldTolerance = 0.000001f;

        public static bool TryCreatePlan(MeshPart part, FaceRegionPlan regionPlan, out FaceInsetPlan plan)
        {
            plan = null;

            if (part == null || regionPlan == null || !part.SupportsDestructiveTopologyEditing || !part.TopologyEditCapabilities.CanEditStructuralFaces || regionPlan.SelectedFaces.Length != part.StructuralTopology.FaceCount || regionPlan.BoundaryLoops.Length == 0 || regionPlan.BoundaryEdgeCount < 3)
            {
                return false;
            }

            FaceRegionBoundaryEdge longestEdge = default;
            float longestLength = 0f;
            for (int loopIndex = 0; loopIndex < regionPlan.BoundaryLoops.Length; loopIndex++)
            {
                FaceRegionBoundaryLoop loop = regionPlan.BoundaryLoops[loopIndex];
                for (int edgeIndex = 0; edgeIndex < loop.EdgeCount; edgeIndex++)
                {
                    FaceRegionBoundaryEdge edge = loop.Edges[edgeIndex];
                    if (edge.Length > longestLength)
                    {
                        longestLength = edge.Length;
                        longestEdge = edge;
                    }
                }
            }

            if (!IsFinite(longestLength) || longestLength <= 0f)
            {
                return false;
            }

            Vector3 longestWorldA = regionPlan.SourceTransform.TransformPoint(longestEdge.LocalPositionA);
            Vector3 longestWorldB = regionPlan.SourceTransform.TransformPoint(longestEdge.LocalPositionB);
            Vector3 worldAxisU = longestWorldB - longestWorldA;
            if (!IsFinite(worldAxisU) || worldAxisU.sqrMagnitude <= 0.000000000001f)
            {
                return false;
            }

            worldAxisU.Normalize();

            Vector3 worldAxisV = Vector3.Cross(regionPlan.WorldNormal, worldAxisU);
            if (!IsFinite(worldAxisV) || worldAxisV.sqrMagnitude <= 0.000000000001f)
            {
                return false;
            }

            worldAxisV.Normalize();

            float worldTolerance = Mathf.Max(kMinimumWorldTolerance, regionPlan.WorldSpan * 0.000001f);
            FaceInsetLoop[] loops = new FaceInsetLoop[regionPlan.BoundaryLoops.Length];
            int outerLoopIndex = -1;
            int positiveLoopCount = 0;

            for (int loopIndex = 0; loopIndex < loops.Length; loopIndex++)
            {
                FaceRegionBoundaryLoop regionLoop = regionPlan.BoundaryLoops[loopIndex];
                Vector2[] points = new Vector2[regionLoop.EdgeCount];

                for (int edgeIndex = 0; edgeIndex < regionLoop.EdgeCount; edgeIndex++)
                {
                    FaceRegionBoundaryEdge edge = regionLoop.Edges[edgeIndex];
                    Vector3 world = regionPlan.SourceTransform.TransformPoint(edge.LocalPositionA);
                    points[edgeIndex] = ProjectWorldPoint(world, regionPlan.WorldPivot, worldAxisU, worldAxisV);

                    FaceRegionBoundaryEdge next = regionLoop.Edges[(edgeIndex + 1) % regionLoop.EdgeCount];
                    Vector3 worldB = regionPlan.SourceTransform.TransformPoint(edge.LocalPositionB);
                    Vector3 nextWorldA = regionPlan.SourceTransform.TransformPoint(next.LocalPositionA);
                    if ((worldB - nextWorldA).sqrMagnitude > worldTolerance * worldTolerance)
                    {
                        return false;
                    }
                }

                float signedArea = GetSignedArea(points);
                float areaTolerance = GetAreaTolerance(worldTolerance, regionPlan.WorldSpan);
                if (!IsFinite(signedArea) || Mathf.Abs(signedArea) <= areaTolerance || !IsSimpleLoop(points, worldTolerance))
                {
                    return false;
                }

                if (signedArea > 0f)
                {
                    outerLoopIndex = loopIndex;
                    positiveLoopCount++;
                }

                loops[loopIndex] = new FaceInsetLoop(regionLoop, points, signedArea);
            }

            if (positiveLoopCount != 1 || !ValidateLoopRelationships(GetLoopPointArrays(loops), outerLoopIndex, worldTolerance))
            {
                return false;
            }

            plan = new FaceInsetPlan(regionPlan, loops, worldAxisU, worldAxisV, part.Topology.LogicalVertexCount, outerLoopIndex, worldTolerance);
            return true;
        }

        public static bool TryGetDragReference(FaceInsetPlan plan, bool hasWorldRay, Ray worldRay, out Vector3 worldOrigin, out Vector3 worldAxis)
        {
            worldOrigin = Vector3.zero;
            worldAxis = Vector3.zero;

            if (plan == null || plan.Loops.Length == 0)
            {
                return false;
            }

            Vector3 worldHit = Vector3.zero;
            bool hasPlaneHit = hasWorldRay && GizmoMath.TryRayPlane(worldRay, new Plane(plan.RegionPlan.WorldNormal, plan.RegionPlan.WorldPivot), out worldHit);
            Vector2 planeHit = hasPlaneHit ? plan.ProjectWorldPoint(worldHit) : Vector2.zero;

            int bestLoopIndex = -1;
            int bestEdgeIndex = -1;
            float bestMetric = float.PositiveInfinity;
            float bestEdgeLength = -1f;
            Vector2 bestPlanePoint = Vector2.zero;

            for (int loopIndex = 0; loopIndex < plan.Loops.Length; loopIndex++)
            {
                FaceInsetLoop loop = plan.Loops[loopIndex];
                for (int edgeIndex = 0; edgeIndex < loop.OuterPoints.Length; edgeIndex++)
                {
                    Vector2 a = loop.OuterPoints[edgeIndex];
                    Vector2 b = loop.OuterPoints[(edgeIndex + 1) % loop.OuterPoints.Length];
                    float edgeLength = (b - a).magnitude;

                    if (hasPlaneHit)
                    {
                        Vector2 closest = ClosestPointOnSegment(planeHit, a, b);
                        float metric = (planeHit - closest).sqrMagnitude;
                        if (metric < bestMetric)
                        {
                            bestMetric = metric;
                            bestLoopIndex = loopIndex;
                            bestEdgeIndex = edgeIndex;
                            bestPlanePoint = closest;
                        }
                    }
                    else if (edgeLength > bestEdgeLength)
                    {
                        bestEdgeLength = edgeLength;
                        bestLoopIndex = loopIndex;
                        bestEdgeIndex = edgeIndex;
                        bestPlanePoint = (a + b) * 0.5f;
                    }
                }
            }

            if (bestLoopIndex < 0 || bestEdgeIndex < 0)
            {
                return false;
            }

            FaceInsetLoop bestLoop = plan.Loops[bestLoopIndex];
            Vector2 edgeA = bestLoop.OuterPoints[bestEdgeIndex];
            Vector2 edgeB = bestLoop.OuterPoints[(bestEdgeIndex + 1) % bestLoop.OuterPoints.Length];
            Vector2 planeDirection = edgeB - edgeA;
            if (planeDirection.sqrMagnitude <= plan.WorldTolerance * plan.WorldTolerance)
            {
                return false;
            }

            planeDirection.Normalize();
            Vector2 planeInward = new Vector2(-planeDirection.y, planeDirection.x);

            worldOrigin = plan.ExpandWorldPoint(bestPlanePoint);
            worldAxis = plan.WorldAxisU * planeInward.x + plan.WorldAxisV * planeInward.y;
            if (!IsFinite(worldOrigin) || !IsFinite(worldAxis) || worldAxis.sqrMagnitude <= 0.000000000001f)
            {
                return false;
            }

            worldAxis.Normalize();
            return true;
        }

        public static bool TryEvaluate(MeshPart part, FaceInsetPlan plan, float amount, out FaceInsetEvaluation evaluation)
        {
            evaluation = null;

            if (part == null || plan == null || !IsFinite(amount) || amount < 0f || plan.LogicalVertexCount != part.Topology.LogicalVertexCount || plan.RegionPlan.SelectedFaces.Length != part.StructuralTopology.FaceCount)
            {
                return false;
            }

            bool[] movedLogicalVertices = new bool[plan.LogicalVertexCount];
            Vector2[] innerPlanePositions = new Vector2[plan.LogicalVertexCount];
            Vector3[] innerLocalPositions = new Vector3[plan.LogicalVertexCount];
            Vector2[][] innerLoops = new Vector2[plan.Loops.Length][];

            for (int loopIndex = 0; loopIndex < plan.Loops.Length; loopIndex++)
            {
                FaceInsetLoop loop = plan.Loops[loopIndex];
                int pointCount = loop.OuterPoints.Length;
                Vector2[] innerPoints = new Vector2[pointCount];

                for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
                {
                    Vector2 innerPoint;
                    if (amount <= 0f)
                    {
                        innerPoint = loop.OuterPoints[pointIndex];
                    }
                    else if (!TryOffsetVertex(loop.OuterPoints, pointIndex, amount, plan.WorldTolerance, out innerPoint))
                    {
                        return false;
                    }

                    FaceRegionBoundaryEdge edge = loop.RegionLoop.Edges[pointIndex];
                    int logicalVertex = edge.LogicalVertexA;
                    if (logicalVertex < 0 || logicalVertex >= movedLogicalVertices.Length || movedLogicalVertices[logicalVertex] || !IsFinite(innerPoint))
                    {
                        return false;
                    }

                    Vector3 worldPoint = plan.ExpandWorldPoint(innerPoint);
                    Vector3 localPoint = plan.RegionPlan.SourceTransform.InverseTransformPoint(worldPoint);
                    if (!IsFinite(worldPoint) || !IsFinite(localPoint))
                    {
                        return false;
                    }

                    movedLogicalVertices[logicalVertex] = true;
                    innerPlanePositions[logicalVertex] = innerPoint;
                    innerLocalPositions[logicalVertex] = localPoint;
                    innerPoints[pointIndex] = innerPoint;
                }

                innerLoops[loopIndex] = innerPoints;
            }

            LoopCutPreviewLine[] previewLines = BuildPreviewLines(plan, movedLogicalVertices, innerLocalPositions);
            bool canCommit = amount > 0f && ValidateEvaluation(part, plan, movedLogicalVertices, innerPlanePositions, innerLocalPositions, innerLoops);

            evaluation = new FaceInsetEvaluation(amount, canCommit, movedLogicalVertices, innerPlanePositions, innerLocalPositions, innerLoops, previewLines);
            return true;
        }

        public static bool TryCreateMutation(MeshPart part, FaceInsetPlan plan, float amount, out FaceInsetMutation result)
        {
            result = null;

            if (part == null || plan == null || !part.SupportsDestructiveTopologyEditing || !part.TopologyEditCapabilities.CanEditStructuralFaces || !TryEvaluate(part, plan, amount, out FaceInsetEvaluation evaluation) || !evaluation.CanCommit)
            {
                return false;
            }

            MeshData mesh = part.Mesh;
            MeshTopology meshTopology = part.Topology;
            StructuralTopology topology = part.StructuralTopology;

            List<Vector3> vertices = new List<Vector3>(mesh.Vertices);
            List<Vector3> normals = mesh.Normals != null ? new List<Vector3>(mesh.Normals) : null;
            List<Vector4> tangents = mesh.Tangents != null ? new List<Vector4>(mesh.Tangents) : null;
            List<Vector4> uv0 = mesh.UV0 != null ? new List<Vector4>(mesh.UV0) : null;
            List<Color32> vertexColors = mesh.VertexColors != null ? new List<Color32>(mesh.VertexColors) : null;
            List<Vector4> texCoord1 = mesh.TexCoord1 != null ? new List<Vector4>(mesh.TexCoord1) : null;
            List<Vector4> texCoord3 = mesh.TexCoord3 != null ? new List<Vector4>(mesh.TexCoord3) : null;

            List<StructuralFace> faces = new List<StructuralFace>(topology.FaceCount + plan.RegionPlan.BoundaryEdgeCount);
            List<int> faceSurfaceAreaIds = new List<int>(topology.FaceCount + plan.RegionPlan.BoundaryEdgeCount);
            int[] sourceToInnerRender = new int[mesh.Vertices.Length];
            for (int i = 0; i < sourceToInnerRender.Length; i++)
            {
                sourceToInnerRender[i] = -1;
            }

            List<int> innerRenderVertices = new List<int>();

            for (int faceId = 0; faceId < topology.FaceCount; faceId++)
            {
                StructuralFace sourceFace = topology.GetFace(faceId);
                if (!part.TryGetStructuralFaceSurfaceAreaId(faceId, out int surfaceAreaId))
                {
                    return false;
                }

                if (!plan.RegionPlan.SelectedFaces[faceId])
                {
                    faces.Add(sourceFace.CloneWithId(faces.Count));
                    faceSurfaceAreaIds.Add(surfaceAreaId);
                    continue;
                }

                int[] innerCorners = new int[sourceFace.CornerCount];
                for (int cornerIndex = 0; cornerIndex < sourceFace.CornerCount; cornerIndex++)
                {
                    int sourceRenderVertex = sourceFace.GetRenderVertexIndex(cornerIndex);
                    int logicalVertex = meshTopology.GetLogicalVertexIdForRenderVertex(sourceRenderVertex);
                    int innerRenderVertex = sourceToInnerRender[sourceRenderVertex];

                    if (innerRenderVertex < 0)
                    {
                        bool moved = evaluation.MovedLogicalVertices[logicalVertex];
                        Vector3 targetLocalPosition = moved ? evaluation.InnerLocalPositions[logicalVertex] : mesh.Vertices[sourceRenderVertex];
                        Vector2 targetPlanePosition = moved ? evaluation.InnerPlanePositions[logicalVertex] : plan.ProjectWorldPoint(plan.RegionPlan.SourceTransform.TransformPoint(targetLocalPosition));

                        innerRenderVertex = AppendInnerVertex(sourceRenderVertex, sourceFace, targetLocalPosition, targetPlanePosition, moved, mesh, plan, vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3);
                        if (innerRenderVertex < 0)
                        {
                            return false;
                        }

                        sourceToInnerRender[sourceRenderVertex] = innerRenderVertex;
                        innerRenderVertices.Add(innerRenderVertex);
                    }

                    innerCorners[cornerIndex] = innerRenderVertex;
                }

                faces.Add(CreateFace(faces.Count, innerCorners));
                faceSurfaceAreaIds.Add(surfaceAreaId);
            }

            List<int> innerBoundaryRenderPairs = new List<int>(plan.RegionPlan.BoundaryEdgeCount * 2);

            for (int loopIndex = 0; loopIndex < plan.RegionPlan.BoundaryLoops.Length; loopIndex++)
            {
                FaceRegionBoundaryLoop loop = plan.RegionPlan.BoundaryLoops[loopIndex];

                for (int edgeIndex = 0; edgeIndex < loop.EdgeCount; edgeIndex++)
                {
                    FaceRegionBoundaryEdge edge = loop.Edges[edgeIndex];
                    if (!part.TryGetStructuralFaceSurfaceAreaId(edge.SourceFaceId, out int surfaceAreaId))
                    {
                        return false;
                    }

                    int innerA = sourceToInnerRender[edge.SourceRenderVertexA];
                    int innerB = sourceToInnerRender[edge.SourceRenderVertexB];
                    if (innerA < 0 || innerB < 0)
                    {
                        return false;
                    }

                    faces.Add(new StructuralFace(faces.Count, edge.SourceRenderVertexA, edge.SourceRenderVertexB, innerB, innerA));
                    faceSurfaceAreaIds.Add(surfaceAreaId);

                    innerBoundaryRenderPairs.Add(innerA);
                    innerBoundaryRenderPairs.Add(innerB);
                }
            }

            return TryCompactAndCreateResult(mesh, vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3, faces, faceSurfaceAreaIds, innerRenderVertices, innerBoundaryRenderPairs, out result);
        }

        private static bool ValidateEvaluation(MeshPart part, FaceInsetPlan plan, bool[] movedLogicalVertices, Vector2[] innerPlanePositions, Vector3[] innerLocalPositions, Vector2[][] innerLoops)
        {
            if (!ValidateInsetLoopGeometry(plan, innerLoops) || !ValidateLocalWeldSeparation(part, movedLogicalVertices, innerLocalPositions) || !ValidateSelectedFaces(part, plan, movedLogicalVertices, innerPlanePositions) || !ValidateRingFaces(plan, innerLoops))
            {
                return false;
            }

            return true;
        }

        private static bool ValidateInsetLoopGeometry(FaceInsetPlan plan, Vector2[][] innerLoops)
        {
            if (innerLoops == null || innerLoops.Length != plan.Loops.Length)
            {
                return false;
            }

            float oldSelectedArea = 0f;
            float newSelectedArea = 0f;
            for (int loopIndex = 0; loopIndex < innerLoops.Length; loopIndex++)
            {
                Vector2[] points = innerLoops[loopIndex];
                if (points == null || points.Length != plan.Loops[loopIndex].OuterPoints.Length || !IsSimpleLoop(points, plan.WorldTolerance))
                {
                    return false;
                }

                float oldArea = plan.Loops[loopIndex].SignedArea;
                float newArea = GetSignedArea(points);
                float areaTolerance = GetAreaTolerance(plan.WorldTolerance, plan.RegionPlan.WorldSpan);
                if (!IsFinite(newArea) || Mathf.Abs(newArea) <= areaTolerance || Mathf.Sign(newArea) != Mathf.Sign(oldArea))
                {
                    return false;
                }

                if (loopIndex == plan.OuterLoopIndex)
                {
                    if (newArea >= oldArea - areaTolerance)
                    {
                        return false;
                    }
                }
                else if (newArea >= oldArea - areaTolerance)
                {
                    // Hole loops are negative. A larger hole has a more negative signed area.
                    return false;
                }

                oldSelectedArea += oldArea;
                newSelectedArea += newArea;
            }

            float selectedAreaTolerance = GetAreaTolerance(plan.WorldTolerance, plan.RegionPlan.WorldSpan);
            if (newSelectedArea <= selectedAreaTolerance || newSelectedArea >= oldSelectedArea - selectedAreaTolerance || !ValidateLoopRelationships(innerLoops, plan.OuterLoopIndex, plan.WorldTolerance))
            {
                return false;
            }

            return true;
        }

        private static bool ValidateLocalWeldSeparation(MeshPart part, bool[] movedLogicalVertices, Vector3[] innerLocalPositions)
        {
            float minimumDistance = MeshTopology.kDefaultWeldEpsilon * 2f;
            float minimumDistanceSquared = minimumDistance * minimumDistance;
            MeshTopology topology = part.Topology;
            MeshData mesh = part.Mesh;

            for (int logicalA = 0; logicalA < movedLogicalVertices.Length; logicalA++)
            {
                if (!movedLogicalVertices[logicalA])
                {
                    continue;
                }

                Vector3 innerA = innerLocalPositions[logicalA];
                for (int logicalB = 0; logicalB < topology.LogicalVertexCount; logicalB++)
                {
                    Vector3 oldB = topology.GetVertex(logicalB).GetPosition(mesh);
                    if ((innerA - oldB).sqrMagnitude <= minimumDistanceSquared)
                    {
                        return false;
                    }

                    if (logicalB > logicalA && movedLogicalVertices[logicalB] && (innerA - innerLocalPositions[logicalB]).sqrMagnitude <= minimumDistanceSquared)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ValidateSelectedFaces(MeshPart part, FaceInsetPlan plan, bool[] movedLogicalVertices, Vector2[] innerPlanePositions)
        {
            StructuralTopology topology = part.StructuralTopology;
            MeshTopology meshTopology = part.Topology;
            MeshData mesh = part.Mesh;
            float areaTolerance = GetAreaTolerance(plan.WorldTolerance, plan.RegionPlan.WorldSpan);

            for (int selectionIndex = 0; selectionIndex < plan.RegionPlan.SelectedFaceIds.Length; selectionIndex++)
            {
                StructuralFace face = topology.GetFace(plan.RegionPlan.SelectedFaceIds[selectionIndex]);
                Vector2[] points = new Vector2[face.CornerCount];

                for (int cornerIndex = 0; cornerIndex < face.CornerCount; cornerIndex++)
                {
                    int renderVertex = face.GetRenderVertexIndex(cornerIndex);
                    int logicalVertex = meshTopology.GetLogicalVertexIdForRenderVertex(renderVertex);
                    points[cornerIndex] = movedLogicalVertices[logicalVertex] ? innerPlanePositions[logicalVertex] : plan.ProjectWorldPoint(plan.RegionPlan.SourceTransform.TransformPoint(mesh.Vertices[renderVertex]));
                }

                if (Cross(points[1] - points[0], points[2] - points[0]) <= areaTolerance)
                {
                    return false;
                }

                if (face.IsQuad && Cross(points[2] - points[0], points[3] - points[0]) <= areaTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateRingFaces(FaceInsetPlan plan, Vector2[][] innerLoops)
        {
            float areaTolerance = GetAreaTolerance(plan.WorldTolerance, plan.RegionPlan.WorldSpan);

            for (int loopIndex = 0; loopIndex < plan.Loops.Length; loopIndex++)
            {
                Vector2[] outer = plan.Loops[loopIndex].OuterPoints;
                Vector2[] inner = innerLoops[loopIndex];
                for (int edgeIndex = 0; edgeIndex < outer.Length; edgeIndex++)
                {
                    int nextIndex = (edgeIndex + 1) % outer.Length;
                    Vector2 outerA = outer[edgeIndex];
                    Vector2 outerB = outer[nextIndex];
                    Vector2 innerB = inner[nextIndex];
                    Vector2 innerA = inner[edgeIndex];

                    if (Cross(outerB - outerA, innerB - outerA) <= areaTolerance || Cross(innerB - outerA, innerA - outerA) <= areaTolerance)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static LoopCutPreviewLine[] BuildPreviewLines(FaceInsetPlan plan, bool[] movedLogicalVertices, Vector3[] innerLocalPositions)
        {
            FaceRegionPlan region = plan.RegionPlan;
            int lineCount = checked(region.BoundaryEdgeCount * 2 + region.SelectedEdges.Length);
            LoopCutPreviewLine[] lines = new LoopCutPreviewLine[lineCount];
            int lineIndex = 0;

            for (int loopIndex = 0; loopIndex < region.BoundaryLoops.Length; loopIndex++)
            {
                FaceRegionBoundaryLoop loop = region.BoundaryLoops[loopIndex];
                for (int edgeIndex = 0; edgeIndex < loop.EdgeCount; edgeIndex++)
                {
                    FaceRegionBoundaryEdge edge = loop.Edges[edgeIndex];
                    Vector3 outerA = region.SourceTransform.TransformPoint(edge.LocalPositionA);
                    Vector3 outerB = region.SourceTransform.TransformPoint(edge.LocalPositionB);
                    Vector3 innerA = region.SourceTransform.TransformPoint(innerLocalPositions[edge.LogicalVertexA]);

                    lines[lineIndex++] = new LoopCutPreviewLine(outerA, outerB, isTargetEdge: false);
                    lines[lineIndex++] = new LoopCutPreviewLine(outerA, innerA, isTargetEdge: false);
                }
            }

            for (int edgeIndex = 0; edgeIndex < region.SelectedEdges.Length; edgeIndex++)
            {
                FaceRegionPreviewEdge edge = region.SelectedEdges[edgeIndex];
                Vector3 localA = movedLogicalVertices[edge.LogicalVertexA] ? innerLocalPositions[edge.LogicalVertexA] : edge.LocalPositionA;
                Vector3 localB = movedLogicalVertices[edge.LogicalVertexB] ? innerLocalPositions[edge.LogicalVertexB] : edge.LocalPositionB;
                lines[lineIndex++] = new LoopCutPreviewLine(region.SourceTransform.TransformPoint(localA), region.SourceTransform.TransformPoint(localB), isTargetEdge: false);
            }

            return lines;
        }

        private static int AppendInnerVertex(int sourceRenderVertex, StructuralFace sourceFace, Vector3 targetLocalPosition, Vector2 targetPlanePosition, bool interpolatePayload, MeshData sourceMesh, FaceInsetPlan plan, List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents, List<Vector4> uv0, List<Color32> vertexColors, List<Vector4> texCoord1, List<Vector4> texCoord3)
        {
            int newIndex = vertices.Count;
            vertices.Add(targetLocalPosition);

            if (!interpolatePayload)
            {
                if (normals != null)
                {
                    normals.Add(sourceMesh.Normals[sourceRenderVertex]);
                }

                if (tangents != null)
                {
                    tangents.Add(sourceMesh.Tangents[sourceRenderVertex]);
                }

                if (uv0 != null)
                {
                    uv0.Add(sourceMesh.UV0[sourceRenderVertex]);
                }

                if (vertexColors != null)
                {
                    vertexColors.Add(sourceMesh.VertexColors[sourceRenderVertex]);
                }

                if (texCoord1 != null)
                {
                    texCoord1.Add(sourceMesh.TexCoord1[sourceRenderVertex]);
                }

                if (texCoord3 != null)
                {
                    texCoord3.Add(sourceMesh.TexCoord3[sourceRenderVertex]);
                }

                return newIndex;
            }

            if (!TryGetFaceSample(sourceFace, targetPlanePosition, sourceMesh, plan, out int renderA, out int renderB, out int renderC, out Vector3 weights))
            {
                return -1;
            }

            if (normals != null)
            {
                Vector3 normal = sourceMesh.Normals[renderA] * weights.x + sourceMesh.Normals[renderB] * weights.y + sourceMesh.Normals[renderC] * weights.z;
                normals.Add(normal.sqrMagnitude > 0.000000000001f ? normal.normalized : sourceMesh.Normals[sourceRenderVertex]);
            }

            if (tangents != null)
            {
                tangents.Add(sourceMesh.Tangents[renderA] * weights.x + sourceMesh.Tangents[renderB] * weights.y + sourceMesh.Tangents[renderC] * weights.z);
            }

            if (uv0 != null)
            {
                uv0.Add(sourceMesh.UV0[renderA] * weights.x + sourceMesh.UV0[renderB] * weights.y + sourceMesh.UV0[renderC] * weights.z);
            }

            if (vertexColors != null)
            {
                Color color = (Color)sourceMesh.VertexColors[renderA] * weights.x + (Color)sourceMesh.VertexColors[renderB] * weights.y + (Color)sourceMesh.VertexColors[renderC] * weights.z;
                vertexColors.Add((Color32)color);
            }

            if (texCoord1 != null)
            {
                Vector4 value = sourceMesh.TexCoord1[renderA];
                if (!SubdivisionBasis.SameBits(value, sourceMesh.TexCoord1[renderB]) || !SubdivisionBasis.SameBits(value, sourceMesh.TexCoord1[renderC]))
                {
                    return -1;
                }

                texCoord1.Add(value);
            }

            if (texCoord3 != null)
            {
                Vector4 value = sourceMesh.TexCoord3[renderA] * weights.x + sourceMesh.TexCoord3[renderB] * weights.y + sourceMesh.TexCoord3[renderC] * weights.z;
                float magnitude = value.magnitude;
                texCoord3.Add(magnitude > 0.000001f ? value / magnitude : new Vector4(0f, 0f, 0f, 1f));
            }

            return newIndex;
        }

        private static bool TryGetFaceSample(StructuralFace face, Vector2 target, MeshData mesh, FaceInsetPlan plan, out int renderA, out int renderB, out int renderC, out Vector3 weights)
        {
            renderA = face.GetRenderVertexIndex(0);
            renderB = face.GetRenderVertexIndex(1);
            renderC = face.GetRenderVertexIndex(2);
            weights = Vector3.zero;

            if (!TryGetTriangleSample(renderA, renderB, renderC, target, mesh, plan, out Vector3 firstWeights, out float firstScore))
            {
                return false;
            }

            if (face.IsTriangle || firstScore >= -kBarycentricTolerance)
            {
                weights = firstWeights;
                return true;
            }

            int secondA = face.GetRenderVertexIndex(0);
            int secondB = face.GetRenderVertexIndex(2);
            int secondC = face.GetRenderVertexIndex(3);
            if (!TryGetTriangleSample(secondA, secondB, secondC, target, mesh, plan, out Vector3 secondWeights, out float secondScore))
            {
                return false;
            }

            if (secondScore > firstScore)
            {
                renderA = secondA;
                renderB = secondB;
                renderC = secondC;
                weights = secondWeights;
            }
            else
            {
                weights = firstWeights;
            }

            return true;
        }

        private static bool TryGetTriangleSample(int renderA, int renderB, int renderC, Vector2 target, MeshData mesh, FaceInsetPlan plan, out Vector3 weights, out float score)
        {
            Vector2 a = plan.ProjectWorldPoint(plan.RegionPlan.SourceTransform.TransformPoint(mesh.Vertices[renderA]));
            Vector2 b = plan.ProjectWorldPoint(plan.RegionPlan.SourceTransform.TransformPoint(mesh.Vertices[renderB]));
            Vector2 c = plan.ProjectWorldPoint(plan.RegionPlan.SourceTransform.TransformPoint(mesh.Vertices[renderC]));
            float denominator = Cross(b - a, c - a);
            float areaTolerance = GetAreaTolerance(plan.WorldTolerance, plan.RegionPlan.WorldSpan);
            if (!IsFinite(denominator) || Mathf.Abs(denominator) <= areaTolerance)
            {
                weights = Vector3.zero;
                score = float.NegativeInfinity;
                return false;
            }

            float weightB = Cross(target - a, c - a) / denominator;
            float weightC = Cross(b - a, target - a) / denominator;
            float weightA = 1f - weightB - weightC;
            weights = new Vector3(weightA, weightB, weightC);
            score = Mathf.Min(weightA, Mathf.Min(weightB, weightC));
            return IsFinite(weights);
        }

        private static bool TryCompactAndCreateResult(MeshData sourceMesh, List<Vector3> workingVertices, List<Vector3> workingNormals, List<Vector4> workingTangents, List<Vector4> workingUV0, List<Color32> workingVertexColors, List<Vector4> workingTexCoord1, List<Vector4> workingTexCoord3, List<StructuralFace> workingFaces, List<int> faceSurfaceAreaIds, List<int> uncompactInnerRenderVertices, List<int> uncompactInnerBoundaryRenderPairs, out FaceInsetMutation result)
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

            int[] innerRenderVertices = RemapRenderVertices(uncompactInnerRenderVertices, oldToNew);
            int[] innerBoundaryRenderPairs = RemapRenderVertices(uncompactInnerBoundaryRenderPairs, oldToNew);
            if (innerRenderVertices == null || innerBoundaryRenderPairs == null)
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

            result = new FaceInsetMutation(new MeshMutationResult(resultMesh.Vertices, resultMesh.Normals, resultMesh.Tangents, resultMesh.UV0, resultMesh.VertexColors, resultMesh.TexCoord1, resultMesh.TexCoord3, resultMesh.Triangles, faces, triangleSurfaceAreaIds), innerRenderVertices, innerBoundaryRenderPairs);
            result.MeshMutation.SetHistoryVertexMap(sourceMesh.Vertices.Length, oldToNew);
            return true;
        }

        private static bool TryOffsetVertex(Vector2[] points, int pointIndex, float amount, float tolerance, out Vector2 result)
        {
            result = Vector2.zero;
            int count = points.Length;
            Vector2 previous = points[(pointIndex + count - 1) % count];
            Vector2 current = points[pointIndex];
            Vector2 next = points[(pointIndex + 1) % count];
            Vector2 incoming = current - previous;
            Vector2 outgoing = next - current;

            if (incoming.sqrMagnitude <= tolerance * tolerance || outgoing.sqrMagnitude <= tolerance * tolerance)
            {
                return false;
            }

            incoming.Normalize();
            outgoing.Normalize();
            Vector2 incomingNormal = new Vector2(-incoming.y, incoming.x);
            Vector2 outgoingNormal = new Vector2(-outgoing.y, outgoing.x);
            float denominator = Cross(incoming, outgoing);

            if (Mathf.Abs(denominator) <= kDirectionCrossEpsilon)
            {
                if (Vector2.Dot(incoming, outgoing) <= 0f)
                {
                    return false;
                }

                Vector2 averageNormal = incomingNormal + outgoingNormal;
                if (averageNormal.sqrMagnitude <= kDirectionCrossEpsilon * kDirectionCrossEpsilon)
                {
                    averageNormal = incomingNormal;
                }

                averageNormal.Normalize();
                result = current + averageNormal * amount;
                return IsFinite(result);
            }

            Vector2 incomingPoint = current + incomingNormal * amount;
            Vector2 outgoingPoint = current + outgoingNormal * amount;
            float parameter = Cross(outgoingPoint - incomingPoint, outgoing) / denominator;
            result = incomingPoint + incoming * parameter;
            return IsFinite(result);
        }

        private static bool ValidateLoopRelationships(Vector2[][] loops, int outerLoopIndex, float tolerance)
        {
            if (loops == null || outerLoopIndex < 0 || outerLoopIndex >= loops.Length)
            {
                return false;
            }

            for (int loopA = 0; loopA < loops.Length; loopA++)
            {
                for (int loopB = loopA + 1; loopB < loops.Length; loopB++)
                {
                    if (LoopsIntersect(loops[loopA], loops[loopB], tolerance))
                    {
                        return false;
                    }
                }
            }

            Vector2[] outer = loops[outerLoopIndex];
            for (int loopIndex = 0; loopIndex < loops.Length; loopIndex++)
            {
                if (loopIndex == outerLoopIndex)
                {
                    continue;
                }

                Vector2[] hole = loops[loopIndex];
                if (hole.Length == 0 || !IsPointInsideLoop(hole[0], outer))
                {
                    return false;
                }

                for (int otherHoleIndex = 0; otherHoleIndex < loops.Length; otherHoleIndex++)
                {
                    if (otherHoleIndex == outerLoopIndex || otherHoleIndex == loopIndex)
                    {
                        continue;
                    }

                    if (IsPointInsideLoop(hole[0], loops[otherHoleIndex]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsSimpleLoop(Vector2[] points, float tolerance)
        {
            if (points == null || points.Length < 3)
            {
                return false;
            }

            float toleranceSquared = tolerance * tolerance;
            for (int i = 0; i < points.Length; i++)
            {
                int nextI = (i + 1) % points.Length;
                if ((points[nextI] - points[i]).sqrMagnitude <= toleranceSquared)
                {
                    return false;
                }

                for (int j = i + 1; j < points.Length; j++)
                {
                    int nextJ = (j + 1) % points.Length;
                    bool adjacent = j == i || nextI == j || nextJ == i;
                    if (adjacent)
                    {
                        continue;
                    }

                    if (SegmentsIntersect(points[i], points[nextI], points[j], points[nextJ], tolerance))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool LoopsIntersect(Vector2[] loopA, Vector2[] loopB, float tolerance)
        {
            for (int edgeA = 0; edgeA < loopA.Length; edgeA++)
            {
                Vector2 a0 = loopA[edgeA];
                Vector2 a1 = loopA[(edgeA + 1) % loopA.Length];
                for (int edgeB = 0; edgeB < loopB.Length; edgeB++)
                {
                    Vector2 b0 = loopB[edgeB];
                    Vector2 b1 = loopB[(edgeB + 1) % loopB.Length];
                    if (SegmentsIntersect(a0, a1, b0, b1, tolerance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool SegmentsIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, float tolerance)
        {
            float span = Mathf.Max((a1 - a0).magnitude, (b1 - b0).magnitude);
            float areaTolerance = tolerance * Mathf.Max(span, 1f);
            float o1 = Cross(a1 - a0, b0 - a0);
            float o2 = Cross(a1 - a0, b1 - a0);
            float o3 = Cross(b1 - b0, a0 - b0);
            float o4 = Cross(b1 - b0, a1 - b0);

            if (((o1 > areaTolerance && o2 < -areaTolerance) || (o1 < -areaTolerance && o2 > areaTolerance)) && ((o3 > areaTolerance && o4 < -areaTolerance) || (o3 < -areaTolerance && o4 > areaTolerance)))
            {
                return true;
            }

            return (Mathf.Abs(o1) <= areaTolerance && IsPointOnSegment(b0, a0, a1, tolerance)) || (Mathf.Abs(o2) <= areaTolerance && IsPointOnSegment(b1, a0, a1, tolerance)) || (Mathf.Abs(o3) <= areaTolerance && IsPointOnSegment(a0, b0, b1, tolerance)) || (Mathf.Abs(o4) <= areaTolerance && IsPointOnSegment(a1, b0, b1, tolerance));
        }

        private static bool IsPointOnSegment(Vector2 point, Vector2 a, Vector2 b, float tolerance)
        {
            return point.x >= Mathf.Min(a.x, b.x) - tolerance && point.x <= Mathf.Max(a.x, b.x) + tolerance && point.y >= Mathf.Min(a.y, b.y) - tolerance && point.y <= Mathf.Max(a.y, b.y) + tolerance;
        }

        private static bool IsPointInsideLoop(Vector2 point, Vector2[] loop)
        {
            bool inside = false;
            int previous = loop.Length - 1;
            for (int current = 0; current < loop.Length; current++)
            {
                Vector2 a = loop[current];
                Vector2 b = loop[previous];
                bool crosses = (a.y > point.y) != (b.y > point.y);
                if (crosses)
                {
                    float x = (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
                    if (point.x < x)
                    {
                        inside = !inside;
                    }
                }

                previous = current;
            }

            return inside;
        }

        private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            float denominator = edge.sqrMagnitude;
            if (denominator <= 0f)
            {
                return a;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, edge) / denominator);
            return a + edge * t;
        }

        private static Vector2 ProjectWorldPoint(Vector3 worldPoint, Vector3 worldOrigin, Vector3 worldAxisU, Vector3 worldAxisV)
        {
            Vector3 offset = worldPoint - worldOrigin;
            return new Vector2(Vector3.Dot(offset, worldAxisU), Vector3.Dot(offset, worldAxisV));
        }

        private static Vector2[][] GetLoopPointArrays(FaceInsetLoop[] loops)
        {
            Vector2[][] result = new Vector2[loops.Length][];
            for (int i = 0; i < loops.Length; i++)
            {
                result[i] = loops[i].OuterPoints;
            }

            return result;
        }

        private static float GetSignedArea(Vector2[] points)
        {
            float twiceArea = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Length];
                twiceArea += Cross(a, b);
            }

            return twiceArea * 0.5f;
        }

        private static float GetAreaTolerance(float worldTolerance, float worldSpan)
        {
            return worldTolerance * Mathf.Max(worldSpan, 1f);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
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
                throw new InvalidOperationException("Inset face Surface Area membership is incomplete.");
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

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}