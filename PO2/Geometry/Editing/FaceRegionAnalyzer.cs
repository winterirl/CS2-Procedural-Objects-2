using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Topology;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Geometry.Editing
{
    // Extrude and Inset both start from the same kind of planar face region.
    // Keep that proof in one place so the two tools cannot drift apart.
    internal static class FaceRegionAnalyzer
    {
        private const float kTriangleCrossSqrEpsilon = 0.000000000001f;
        private const float kNormalAlignment = 0.9999f;
        private const float kMinimumWorldPlaneTolerance = 0.0001f;
        private const float kRelativeWorldPlaneTolerance = 0.00001f;
        private const float kMinimumBoundaryLength = 0.000001f;

        private readonly struct BoundaryRecord
        {
            public FaceRegionBoundaryEdge Edge { get; }

            public BoundaryRecord(FaceRegionBoundaryEdge edge)
            {
                Edge = edge;
            }
        }

        public static bool TryAnalyze(ProceduralObject proceduralObject, int partIndex, bool[] selectedFaces, out FaceRegionPlan plan)
        {
            plan = null;

            if (proceduralObject == null || partIndex < 0 || partIndex >= proceduralObject.Parts.Count || selectedFaces == null)
            {
                return false;
            }

            MeshPart part = proceduralObject.Parts[partIndex];
            StructuralTopology structuralTopology = part.StructuralTopology;
            MeshTopology meshTopology = part.Topology;
            MeshData mesh = part.Mesh;

            if (!part.SupportsDestructiveTopologyEditing || !part.TopologyEditCapabilities.CanEditStructuralFaces || selectedFaces.Length != structuralTopology.FaceCount || structuralTopology.FaceCount == 0)
            {
                return false;
            }

            List<int> selectedFaceIds = new List<int>();
            for (int faceId = 0; faceId < selectedFaces.Length; faceId++)
            {
                if (selectedFaces[faceId])
                {
                    selectedFaceIds.Add(faceId);
                }
            }

            if (selectedFaceIds.Count == 0)
            {
                return false;
            }

            ObjectTransform transform = proceduralObject.Transform;
            Vector3 firstWorldPoint = Vector3.zero;
            Vector3 firstTriangleNormal = Vector3.zero;
            Vector3 accumulatedNormal = Vector3.zero;
            Vector3 boundsMin = Vector3.zero;
            Vector3 boundsMax = Vector3.zero;
            bool hasWorldPoint = false;
            bool hasTriangleNormal = false;

            HashSet<int> selectedLogicalVertices = new HashSet<int>();

            for (int selectionIndex = 0; selectionIndex < selectedFaceIds.Count; selectionIndex++)
            {
                StructuralFace face = structuralTopology.GetFace(selectedFaceIds[selectionIndex]);

                Vector3[] worldCorners = new Vector3[face.CornerCount];

                for (int cornerIndex = 0; cornerIndex < face.CornerCount; cornerIndex++)
                {
                    int renderVertexIndex = face.GetRenderVertexIndex(cornerIndex);
                    if (renderVertexIndex < 0 || renderVertexIndex >= mesh.Vertices.Length)
                    {
                        return false;
                    }

                    Vector3 world = transform.TransformPoint(mesh.Vertices[renderVertexIndex]);
                    if (!IsFinite(world))
                    {
                        return false;
                    }

                    worldCorners[cornerIndex] = world;
                    int logicalVertexId = meshTopology.GetLogicalVertexIdForRenderVertex(renderVertexIndex);
                    selectedLogicalVertices.Add(logicalVertexId);

                    if (!hasWorldPoint)
                    {
                        firstWorldPoint = world;
                        boundsMin = world;
                        boundsMax = world;
                        hasWorldPoint = true;
                    }
                    else
                    {
                        boundsMin = Vector3.Min(boundsMin, world);
                        boundsMax = Vector3.Max(boundsMax, world);
                    }
                }

                if (!TryAccumulateTriangleNormal(worldCorners[0], worldCorners[1], worldCorners[2], ref firstTriangleNormal, ref accumulatedNormal, ref hasTriangleNormal))
                {
                    return false;
                }

                if (face.IsQuad && !TryAccumulateTriangleNormal(worldCorners[0], worldCorners[2], worldCorners[3], ref firstTriangleNormal, ref accumulatedNormal, ref hasTriangleNormal))
                {
                    return false;
                }
            }

            if (!hasWorldPoint || !hasTriangleNormal || accumulatedNormal.sqrMagnitude <= kTriangleCrossSqrEpsilon)
            {
                return false;
            }

            Vector3 worldNormal = accumulatedNormal.normalized;
            float worldDiagonal = (boundsMax - boundsMin).magnitude;
            float planeTolerance = Mathf.Max(kMinimumWorldPlaneTolerance, worldDiagonal * kRelativeWorldPlaneTolerance);

            if (!ValidateSelectedFacePlane(structuralTopology, mesh, transform, selectedFaceIds, firstWorldPoint, worldNormal, planeTolerance))
            {
                return false;
            }

            bool[] processedEdges = new bool[structuralTopology.StructuralEdgeCount];
            List<int>[] selectedNeighbors = new List<int>[structuralTopology.FaceCount];
            List<BoundaryRecord> boundaryRecords = new List<BoundaryRecord>();
            List<FaceRegionPreviewEdge> selectedPreviewEdges = new List<FaceRegionPreviewEdge>();

            for (int selectionIndex = 0; selectionIndex < selectedFaceIds.Count; selectionIndex++)
            {
                int faceId = selectedFaceIds[selectionIndex];
                StructuralFace face = structuralTopology.GetFace(faceId);

                for (int boundaryIndex = 0; boundaryIndex < face.CornerCount; boundaryIndex++)
                {
                    int structuralEdgeId = structuralTopology.GetBoundaryEdgeId(faceId, boundaryIndex);
                    if (structuralEdgeId < 0 || structuralEdgeId >= processedEdges.Length || processedEdges[structuralEdgeId])
                    {
                        continue;
                    }

                    processedEdges[structuralEdgeId] = true;
                    StructuralEdge structuralEdge = structuralTopology.GetEdge(structuralEdgeId);

                    if (!structuralEdge.IsManifold || structuralEdge.AdjacentFaceCount < 1 || structuralEdge.AdjacentFaceCount > 2)
                    {
                        return false;
                    }

                    int selectedAdjacentCount = 0;
                    int selectedAdjacentA = -1;
                    int selectedAdjacentB = -1;
                    for (int adjacentIndex = 0; adjacentIndex < structuralEdge.AdjacentFaceCount; adjacentIndex++)
                    {
                        int adjacentFaceId = structuralEdge.AdjacentFaceIds[adjacentIndex];
                        if (adjacentFaceId < 0 || adjacentFaceId >= selectedFaces.Length)
                        {
                            return false;
                        }

                        if (selectedFaces[adjacentFaceId])
                        {
                            if (selectedAdjacentCount == 0)
                            {
                                selectedAdjacentA = adjacentFaceId;
                            }
                            else
                            {
                                selectedAdjacentB = adjacentFaceId;
                            }

                            selectedAdjacentCount++;
                        }
                    }

                    if (selectedAdjacentCount == 0 || selectedAdjacentCount > 2)
                    {
                        return false;
                    }

                    Vector3 localEdgeA = meshTopology.GetVertex(structuralEdge.LogicalVertexA).GetPosition(mesh);
                    Vector3 localEdgeB = meshTopology.GetVertex(structuralEdge.LogicalVertexB).GetPosition(mesh);
                    selectedPreviewEdges.Add(new FaceRegionPreviewEdge(structuralEdge.LogicalVertexA, structuralEdge.LogicalVertexB, localEdgeA, localEdgeB));

                    if (selectedAdjacentCount == 2)
                    {
                        if (!TryValidateInternalEdgeWinding(structuralTopology, structuralEdgeId, selectedAdjacentA, selectedAdjacentB))
                        {
                            return false;
                        }

                        AddNeighbor(selectedNeighbors, selectedAdjacentA, selectedAdjacentB);
                        AddNeighbor(selectedNeighbors, selectedAdjacentB, selectedAdjacentA);
                        continue;
                    }

                    int ownerFaceId = selectedAdjacentA;
                    if (!structuralTopology.TryGetBoundaryIndex(ownerFaceId, structuralEdgeId, out int ownerBoundaryIndex))
                    {
                        return false;
                    }

                    StructuralFace ownerFace = structuralTopology.GetFace(ownerFaceId);
                    int sourceRenderA = ownerFace.GetRenderVertexIndex(ownerBoundaryIndex);
                    int sourceRenderB = ownerFace.GetRenderVertexIndex((ownerBoundaryIndex + 1) % ownerFace.CornerCount);
                    int logicalA = meshTopology.GetLogicalVertexIdForRenderVertex(sourceRenderA);
                    int logicalB = meshTopology.GetLogicalVertexIdForRenderVertex(sourceRenderB);

                    Vector3 localA = mesh.Vertices[sourceRenderA];
                    Vector3 localB = mesh.Vertices[sourceRenderB];
                    float edgeLength = Vector3.Distance(transform.TransformPoint(localA), transform.TransformPoint(localB));
                    if (!IsFinite(edgeLength) || edgeLength <= kMinimumBoundaryLength)
                    {
                        return false;
                    }

                    boundaryRecords.Add(new BoundaryRecord(new FaceRegionBoundaryEdge(ownerFaceId, ownerBoundaryIndex, structuralEdgeId, logicalA, logicalB, sourceRenderA, sourceRenderB, sourceRenderA, sourceRenderB, localA, localB, edgeLength)));
                }
            }

            if (boundaryRecords.Count == 0 || !IsSelectedRegionConnected(selectedFaceIds, selectedNeighbors))
            {
                return false;
            }

            if (!TryBuildBoundaryLoops(boundaryRecords, out FaceRegionBoundaryLoop[] boundaryLoops))
            {
                return false;
            }

            int[] sortedLogicalVertexIds = new int[selectedLogicalVertices.Count];
            selectedLogicalVertices.CopyTo(sortedLogicalVertexIds);
            Array.Sort(sortedLogicalVertexIds);

            Vector3 worldPivot = Vector3.zero;
            for (int i = 0; i < sortedLogicalVertexIds.Length; i++)
            {
                int logicalVertexId = sortedLogicalVertexIds[i];
                worldPivot += transform.TransformPoint(meshTopology.GetVertex(logicalVertexId).GetPosition(mesh));
            }

            if (selectedLogicalVertices.Count == 0)
            {
                return false;
            }

            worldPivot /= selectedLogicalVertices.Count;
            Vector3 localNormalPerWorldMeter = transform.InverseTransformVector(worldNormal);
            if (!IsFinite(worldPivot) || !IsFinite(localNormalPerWorldMeter) || localNormalPerWorldMeter.sqrMagnitude <= kTriangleCrossSqrEpsilon)
            {
                return false;
            }

            plan = new FaceRegionPlan(selectedFaceIds.ToArray(), (bool[])selectedFaces.Clone(), boundaryLoops, selectedPreviewEdges.ToArray(), transform, worldNormal, worldPivot, localNormalPerWorldMeter, worldDiagonal, boundaryRecords.Count);
            return true;
        }

        private static bool TryAccumulateTriangleNormal(Vector3 a, Vector3 b, Vector3 c, ref Vector3 firstTriangleNormal, ref Vector3 accumulatedNormal, ref bool hasTriangleNormal)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float magnitudeSquared = cross.sqrMagnitude;
            if (!IsFinite(cross) || magnitudeSquared <= kTriangleCrossSqrEpsilon)
            {
                return false;
            }

            Vector3 normal = cross / Mathf.Sqrt(magnitudeSquared);
            if (!hasTriangleNormal)
            {
                firstTriangleNormal = normal;
                hasTriangleNormal = true;
            }
            else if (Vector3.Dot(firstTriangleNormal, normal) < kNormalAlignment)
            {
                return false;
            }

            accumulatedNormal += cross;
            return true;
        }

        private static bool ValidateSelectedFacePlane(StructuralTopology topology, MeshData mesh, ObjectTransform transform, IReadOnlyList<int> selectedFaceIds, Vector3 planePoint, Vector3 planeNormal, float tolerance)
        {
            for (int selectionIndex = 0; selectionIndex < selectedFaceIds.Count; selectionIndex++)
            {
                StructuralFace face = topology.GetFace(selectedFaceIds[selectionIndex]);

                for (int cornerIndex = 0; cornerIndex < face.CornerCount; cornerIndex++)
                {
                    Vector3 world = transform.TransformPoint(mesh.Vertices[face.GetRenderVertexIndex(cornerIndex)]);
                    float distance = Mathf.Abs(Vector3.Dot(world - planePoint, planeNormal));
                    if (!IsFinite(distance) || distance > tolerance)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TryValidateInternalEdgeWinding(StructuralTopology topology, int structuralEdgeId, int faceA, int faceB)
        {
            return topology.TryGetBoundaryIndex(faceA, structuralEdgeId, out int boundaryA) && topology.TryGetBoundaryIndex(faceB, structuralEdgeId, out int boundaryB) && topology.IsBoundaryForward(faceA, boundaryA) != topology.IsBoundaryForward(faceB, boundaryB);
        }

        private static void AddNeighbor(List<int>[] neighbors, int faceId, int neighborFaceId)
        {
            if (neighbors[faceId] == null)
            {
                neighbors[faceId] = new List<int>();
            }

            neighbors[faceId].Add(neighborFaceId);
        }

        private static bool IsSelectedRegionConnected(IReadOnlyList<int> selectedFaceIds, List<int>[] neighbors)
        {
            bool[] visited = new bool[neighbors.Length];
            // Queue<T> is exposed twice by the game's reference set.
            // A list with a read cursor gives the same FIFO walk without CS0433.
            List<int> pending = new List<int>(selectedFaceIds.Count);
            pending.Add(selectedFaceIds[0]);
            visited[selectedFaceIds[0]] = true;
            int visitedCount = 0;
            int pendingIndex = 0;

            while (pendingIndex < pending.Count)
            {
                int faceId = pending[pendingIndex++];
                visitedCount++;
                List<int> faceNeighbors = neighbors[faceId];
                if (faceNeighbors == null)
                {
                    continue;
                }

                for (int i = 0; i < faceNeighbors.Count; i++)
                {
                    int neighbor = faceNeighbors[i];
                    if (!visited[neighbor])
                    {
                        visited[neighbor] = true;
                        pending.Add(neighbor);
                    }
                }
            }

            return visitedCount == selectedFaceIds.Count;
        }

        private static bool TryBuildBoundaryLoops(List<BoundaryRecord> records, out FaceRegionBoundaryLoop[] loops)
        {
            loops = Array.Empty<FaceRegionBoundaryLoop>();

            records.Sort(CompareBoundaryRecords);
            Dictionary<int, int> outgoingByVertex = new Dictionary<int, int>(records.Count);
            Dictionary<int, int> incomingByVertex = new Dictionary<int, int>(records.Count);

            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                FaceRegionBoundaryEdge edge = records[recordIndex].Edge;
                if (edge.LogicalVertexA == edge.LogicalVertexB || outgoingByVertex.ContainsKey(edge.LogicalVertexA) || incomingByVertex.ContainsKey(edge.LogicalVertexB))
                {
                    return false;
                }

                outgoingByVertex.Add(edge.LogicalVertexA, recordIndex);
                incomingByVertex.Add(edge.LogicalVertexB, recordIndex);
            }

            foreach (KeyValuePair<int, int> outgoing in outgoingByVertex)
            {
                if (!incomingByVertex.ContainsKey(outgoing.Key))
                {
                    return false;
                }
            }

            bool[] visited = new bool[records.Count];
            List<FaceRegionBoundaryLoop> result = new List<FaceRegionBoundaryLoop>();

            for (int startIndex = 0; startIndex < records.Count; startIndex++)
            {
                if (visited[startIndex])
                {
                    continue;
                }

                List<FaceRegionBoundaryEdge> loopEdges = new List<FaceRegionBoundaryEdge>();
                int currentIndex = startIndex;
                int startVertex = records[startIndex].Edge.LogicalVertexA;
                float totalLength = 0f;

                while (!visited[currentIndex])
                {
                    visited[currentIndex] = true;
                    FaceRegionBoundaryEdge edge = records[currentIndex].Edge;
                    loopEdges.Add(edge);
                    totalLength += edge.Length;

                    if (!outgoingByVertex.TryGetValue(edge.LogicalVertexB, out int nextIndex))
                    {
                        return false;
                    }

                    currentIndex = nextIndex;
                }

                if (currentIndex != startIndex || loopEdges.Count < 3 || loopEdges[loopEdges.Count - 1].LogicalVertexB != startVertex || !IsFinite(totalLength) || totalLength <= kMinimumBoundaryLength)
                {
                    return false;
                }

                result.Add(new FaceRegionBoundaryLoop(loopEdges.ToArray(), totalLength));
            }

            loops = result.ToArray();
            return loops.Length > 0;
        }

        private static int CompareBoundaryRecords(BoundaryRecord left, BoundaryRecord right)
        {
            int comparison = left.Edge.LogicalVertexA.CompareTo(right.Edge.LogicalVertexA);
            if (comparison == 0)
            {
                comparison = left.Edge.LogicalVertexB.CompareTo(right.Edge.LogicalVertexB);
            }

            if (comparison == 0)
            {
                comparison = left.Edge.SourceFaceId.CompareTo(right.Edge.SourceFaceId);
            }

            if (comparison == 0)
            {
                comparison = left.Edge.SourceBoundaryIndex.CompareTo(right.Edge.SourceBoundaryIndex);
            }

            return comparison;
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