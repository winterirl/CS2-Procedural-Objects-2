using System.Collections.Generic;
using Game.Simulation;
using Unity.Mathematics;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Tools.Snapping
{
    // Terrain placement is a one-shot operation, so it can inspect the moving
    // geometry directly instead of maintaining another live snap index.
    internal static class TerrainSnapCalculator
    {
        private const float kMinimumEdgeSampleSpacing = 0.25f;

        public static bool TryCalculateObjectWorldYOffset(ProceduralObject proceduralObject, ref TerrainHeightData terrainData, out float worldYOffset, out int sampleCount)
        {
            worldYOffset = 0f;
            sampleCount = 0;

            if (proceduralObject == null || !terrainData.isCreated)
            {
                return false;
            }

            float minimumClearance = float.MaxValue;
            float edgeSampleSpacing = GetEdgeSampleSpacing(ref terrainData);

            for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
            {
                IncludePart(proceduralObject, proceduralObject.Parts[partIndex], ref terrainData, edgeSampleSpacing, ref minimumClearance, ref sampleCount);
            }

            return TryFinish(minimumClearance, sampleCount, out worldYOffset);
        }

        public static bool TryCalculatePartWorldYOffset(ProceduralObject proceduralObject, int partIndex, ref TerrainHeightData terrainData, out float worldYOffset, out int sampleCount)
        {
            worldYOffset = 0f;
            sampleCount = 0;

            if (proceduralObject == null || partIndex < 0 || partIndex >= proceduralObject.Parts.Count || !terrainData.isCreated)
            {
                return false;
            }

            float minimumClearance = float.MaxValue;
            float edgeSampleSpacing = GetEdgeSampleSpacing(ref terrainData);

            IncludePart(proceduralObject, proceduralObject.Parts[partIndex], ref terrainData, edgeSampleSpacing, ref minimumClearance, ref sampleCount);

            return TryFinish(minimumClearance, sampleCount, out worldYOffset);
        }

        private static void IncludePart(ProceduralObject proceduralObject, MeshPart part, ref TerrainHeightData terrainData, float edgeSampleSpacing, ref float minimumClearance, ref int sampleCount)
        {
            int logicalVertexCount = part.Topology.LogicalVertexCount;

            Vector3[] worldPositions = new Vector3[logicalVertexCount];

            for (int vertexIndex = 0; vertexIndex < logicalVertexCount; vertexIndex++)
            {
                Vertex vertex = part.Topology.GetVertex(vertexIndex);

                Vector3 worldPosition = proceduralObject.Transform.TransformPoint(vertex.GetPosition(part.Mesh));

                worldPositions[vertexIndex] = worldPosition;

                IncludePoint(ref terrainData, worldPosition, ref minimumClearance, ref sampleCount);
            }

            if (part.StructuralTopology.StructuralEdgeCount > 0)
            {
                for (int edgeIndex = 0; edgeIndex < part.StructuralTopology.StructuralEdgeCount; edgeIndex++)
                {
                    StructuralEdge edge = part.StructuralTopology.GetEdge(edgeIndex);

                    IncludeEdgeInterior(ref terrainData, edge.LogicalVertexA, edge.LogicalVertexB, worldPositions, edgeSampleSpacing, ref minimumClearance, ref sampleCount);
                }

                return;
            }

            // Converted meshes may not have authored structural edges.
            // Their logical triangle edges still keep long spans from
            // clipping through terrain between endpoint samples.
            for (int edgeIndex = 0; edgeIndex < part.Topology.LogicalEdgeCount; edgeIndex++)
            {
                LogicalEdge edge = part.Topology.GetEdge(edgeIndex);

                IncludeEdgeInterior(ref terrainData, edge.VertexA, edge.VertexB, worldPositions, edgeSampleSpacing, ref minimumClearance, ref sampleCount);
            }
        }

        public static bool TryCalculateSelectionWorldYOffset(ProceduralObject proceduralObject, IReadOnlyList<VertexRef> selectedVertices, IReadOnlyList<StructuralEdgeRef> selectedEdges, ref TerrainHeightData terrainData, out float worldYOffset, out int sampleCount)
        {
            worldYOffset = 0f;
            sampleCount = 0;

            if (proceduralObject == null || selectedVertices == null || selectedVertices.Count == 0 || !terrainData.isCreated)
            {
                return false;
            }

            float minimumClearance = float.MaxValue;
            float edgeSampleSpacing = GetEdgeSampleSpacing(ref terrainData);

            for (int vertexIndex = 0; vertexIndex < selectedVertices.Count; vertexIndex++)
            {
                VertexRef vertexRef = selectedVertices[vertexIndex];

                if (!TryGetWorldVertexPosition(proceduralObject, vertexRef.PartIndex, vertexRef.VertexId, out Vector3 worldPosition))
                {
                    continue;
                }

                IncludePoint(ref terrainData, worldPosition, ref minimumClearance, ref sampleCount);
            }

            int selectedEdgeCount = selectedEdges != null ? selectedEdges.Count : 0;

            for (int edgeIndex = 0; edgeIndex < selectedEdgeCount; edgeIndex++)
            {
                StructuralEdgeRef edgeRef = selectedEdges[edgeIndex];

                if (edgeRef.PartIndex < 0 || edgeRef.PartIndex >= proceduralObject.Parts.Count)
                {
                    continue;
                }

                MeshPart part = proceduralObject.Parts[edgeRef.PartIndex];

                if (edgeRef.EdgeId < 0 || edgeRef.EdgeId >= part.StructuralTopology.StructuralEdgeCount)
                {
                    continue;
                }

                StructuralEdge edge = part.StructuralTopology.GetEdge(edgeRef.EdgeId);

                if (!TryGetWorldVertexPosition(proceduralObject, edgeRef.PartIndex, edge.LogicalVertexA, out Vector3 worldStart) || !TryGetWorldVertexPosition(proceduralObject, edgeRef.PartIndex, edge.LogicalVertexB, out Vector3 worldEnd))
                {
                    continue;
                }

                IncludeEdgeInterior(ref terrainData, worldStart, worldEnd, edgeSampleSpacing, ref minimumClearance, ref sampleCount);
            }

            return TryFinish(minimumClearance, sampleCount, out worldYOffset);
        }

        private static void IncludeEdgeInterior(ref TerrainHeightData terrainData, int vertexA, int vertexB, Vector3[] worldPositions, float sampleSpacing, ref float minimumClearance, ref int sampleCount)
        {
            if (vertexA < 0 || vertexA >= worldPositions.Length || vertexB < 0 || vertexB >= worldPositions.Length)
            {
                return;
            }

            IncludeEdgeInterior(ref terrainData, worldPositions[vertexA], worldPositions[vertexB], sampleSpacing, ref minimumClearance, ref sampleCount);
        }

        private static void IncludeEdgeInterior(ref TerrainHeightData terrainData, Vector3 worldStart, Vector3 worldEnd, float sampleSpacing, ref float minimumClearance, ref int sampleCount)
        {
            float horizontalDistance = new Vector2(worldEnd.x - worldStart.x, worldEnd.z - worldStart.z).magnitude;

            if (!IsFinite(horizontalDistance) || horizontalDistance <= sampleSpacing)
            {
                return;
            }

            int segmentCount = Mathf.Max(1, Mathf.CeilToInt(horizontalDistance / sampleSpacing));

            // Endpoints are already represented by the logical vertex pass.
            for (int segmentIndex = 1; segmentIndex < segmentCount; segmentIndex++)
            {
                Vector3 worldPosition = Vector3.Lerp(worldStart, worldEnd, segmentIndex / (float)segmentCount);

                IncludePoint(ref terrainData, worldPosition, ref minimumClearance, ref sampleCount);
            }
        }

        private static void IncludePoint(ref TerrainHeightData terrainData, Vector3 worldPosition, ref float minimumClearance, ref int sampleCount)
        {
            if (!IsFinite(worldPosition))
            {
                return;
            }

            float terrainHeight = TerrainUtils.SampleHeight(ref terrainData, new float3(worldPosition.x, worldPosition.y, worldPosition.z));

            float clearance = worldPosition.y - terrainHeight;

            if (!IsFinite(clearance))
            {
                return;
            }

            minimumClearance = Mathf.Min(minimumClearance, clearance);

            sampleCount++;
        }

        private static float GetEdgeSampleSpacing(ref TerrainHeightData terrainData)
        {
            float worldCellX = Mathf.Abs(terrainData.scale.x) > 0f ? 1f / Mathf.Abs(terrainData.scale.x) : kMinimumEdgeSampleSpacing;

            float worldCellZ = Mathf.Abs(terrainData.scale.z) > 0f ? 1f / Mathf.Abs(terrainData.scale.z) : kMinimumEdgeSampleSpacing;

            // Half a heightmap cell is enough to follow the bilinear terrain
            // representation without turning long-edge checks into tiny steps.
            return Mathf.Max(kMinimumEdgeSampleSpacing, Mathf.Min(worldCellX, worldCellZ) * 0.5f);
        }

        private static bool TryGetWorldVertexPosition(ProceduralObject proceduralObject, int partIndex, int vertexIndex, out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;

            if (partIndex < 0 || partIndex >= proceduralObject.Parts.Count)
            {
                return false;
            }

            MeshPart part = proceduralObject.Parts[partIndex];

            if (vertexIndex < 0 || vertexIndex >= part.Topology.LogicalVertexCount)
            {
                return false;
            }

            Vertex vertex = part.Topology.GetVertex(vertexIndex);

            worldPosition = proceduralObject.Transform.TransformPoint(vertex.GetPosition(part.Mesh));

            return IsFinite(worldPosition);
        }

        private static bool TryFinish(float minimumClearance, int sampleCount, out float worldYOffset)
        {
            worldYOffset = 0f;

            if (sampleCount == 0 || !IsFinite(minimumClearance))
            {
                return false;
            }

            worldYOffset = -minimumClearance;
            return IsFinite(worldYOffset);
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