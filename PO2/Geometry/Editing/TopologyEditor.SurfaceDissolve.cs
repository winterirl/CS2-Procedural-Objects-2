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
        public static bool TryAnalyzeSurfaceCutDissolve(MeshPart part, IReadOnlyList<int> selectedLogicalVertexIds, out SurfaceCutDissolvePlan plan)
        {
            plan = null;

            if (part == null || selectedLogicalVertexIds == null || selectedLogicalVertexIds.Count < 2 || !IsFullyStructuralMesh(part))
            {
                return false;
            }

            MeshTopology meshTopology = part.Topology;
            StructuralTopology structuralTopology = part.StructuralTopology;

            // Surface Cut dissolve is the open-chain version. Its two endpoint
            // vertices must be exactly the boundary splits created by the cut.
            HashSet<int> selectedVertices = new HashSet<int>();

            for (int i = 0; i < selectedLogicalVertexIds.Count; i++)
            {
                int logicalVertexId = selectedLogicalVertexIds[i];

                if (logicalVertexId < 0 || logicalVertexId >= meshTopology.LogicalVertexCount || !selectedVertices.Add(logicalVertexId))
                {
                    return false;
                }
            }

            Dictionary<int, List<int>> selectedNeighbors = new Dictionary<int, List<int>>(selectedVertices.Count);
            Dictionary<int, int> structuralValence = new Dictionary<int, int>(selectedVertices.Count);
            Dictionary<int, int> boundaryValence = new Dictionary<int, int>(selectedVertices.Count);

            foreach (int logicalVertexId in selectedVertices)
            {
                selectedNeighbors.Add(logicalVertexId, new List<int>(2));
                structuralValence.Add(logicalVertexId, 0);
                boundaryValence.Add(logicalVertexId, 0);
            }

            int selectedEdgeCount = 0;

            for (int edgeIndex = 0; edgeIndex < structuralTopology.StructuralEdgeCount; edgeIndex++)
            {
                StructuralEdge edge = structuralTopology.GetEdge(edgeIndex);

                bool aSelected = selectedVertices.Contains(edge.LogicalVertexA);
                bool bSelected = selectedVertices.Contains(edge.LogicalVertexB);

                if (aSelected)
                {
                    structuralValence[edge.LogicalVertexA]++;
                    if (edge.AdjacentFaceCount == 1)
                    {
                        boundaryValence[edge.LogicalVertexA]++;
                    }
                }

                if (bSelected)
                {
                    structuralValence[edge.LogicalVertexB]++;
                    if (edge.AdjacentFaceCount == 1)
                    {
                        boundaryValence[edge.LogicalVertexB]++;
                    }
                }

                if (!aSelected || !bSelected)
                {
                    continue;
                }

                // A dissolvable Surface Cut runs through the interior of the surface.
                // Its chain edges always separate exactly two quads.
                if (!edge.IsManifold || edge.AdjacentFaceCount != 2)
                {
                    return false;
                }

                selectedNeighbors[edge.LogicalVertexA].Add(edge.LogicalVertexB);
                selectedNeighbors[edge.LogicalVertexB].Add(edge.LogicalVertexA);
                selectedEdgeCount++;
            }

            if (selectedEdgeCount != selectedVertices.Count - 1)
            {
                return false;
            }

            List<int> endpoints = new List<int>(2);

            foreach (int logicalVertexId in selectedVertices)
            {
                int selectedNeighborCount = selectedNeighbors[logicalVertexId].Count;

                if (selectedNeighborCount == 1)
                {
                    // Surface Cut endpoints split an existing boundary edge, leaving two boundary edges plus the interior cut edge.
                    if (structuralValence[logicalVertexId] != 3 || boundaryValence[logicalVertexId] != 2)
                    {
                        return false;
                    }

                    endpoints.Add(logicalVertexId);
                }
                else if (selectedNeighborCount == 2)
                {
                    // Interior cut vertices remain ordinary valence-4 grid vertices, even where the cut crosses another cut.
                    if (structuralValence[logicalVertexId] != 4 || boundaryValence[logicalVertexId] != 0)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            if (endpoints.Count != 2)
            {
                return false;
            }

            int startVertexId = Math.Min(endpoints[0], endpoints[1]);
            int endVertexId = Math.Max(endpoints[0], endpoints[1]);
            int[] orderedVertices = new int[selectedVertices.Count];
            int previousVertexId = -1;
            int currentVertexId = startVertexId;

            for (int i = 0; i < orderedVertices.Length; i++)
            {
                orderedVertices[i] = currentVertexId;

                if (i == orderedVertices.Length - 1)
                {
                    if (currentVertexId != endVertexId)
                    {
                        return false;
                    }

                    break;
                }

                List<int> neighbors = selectedNeighbors[currentVertexId];
                int nextVertexId = -1;

                for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
                {
                    int candidate = neighbors[neighborIndex];
                    if (candidate == previousVertexId)
                    {
                        continue;
                    }

                    if (nextVertexId >= 0)
                    {
                        return false;
                    }

                    nextVertexId = candidate;
                }

                if (nextVertexId < 0)
                {
                    return false;
                }

                for (int visitedIndex = 0; visitedIndex <= i; visitedIndex++)
                {
                    if (orderedVertices[visitedIndex] == nextVertexId)
                    {
                        return false;
                    }
                }

                previousVertexId = currentVertexId;
                currentVertexId = nextVertexId;
            }

            DissolveSegment[] segments = new DissolveSegment[orderedVertices.Length - 1];

            int expectedSideAOuterStart = -1;
            int expectedSideBOuterStart = -1;

            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                int segmentStartLogicalVertexId = orderedVertices[segmentIndex];
                int segmentEndLogicalVertexId = orderedVertices[segmentIndex + 1];

                if (!structuralTopology.TryGetEdgeByLogicalVertices(segmentStartLogicalVertexId, segmentEndLogicalVertexId, out StructuralEdge cutEdge) || !cutEdge.IsManifold || cutEdge.AdjacentFaceCount != 2 || !TryGetDissolveQuadSide(part, cutEdge.AdjacentFaceIds[0], cutEdge.Id, segmentStartLogicalVertexId, segmentEndLogicalVertexId, selectedVertices, out DissolveQuadSide firstSide) || !TryGetDissolveQuadSide(part, cutEdge.AdjacentFaceIds[1], cutEdge.Id, segmentStartLogicalVertexId, segmentEndLogicalVertexId, selectedVertices, out DissolveQuadSide secondSide))
                {
                    return false;
                }

                DissolveQuadSide sideA;
                DissolveQuadSide sideB;

                if (segmentIndex == 0)
                {
                    if (firstSide.QuadId < secondSide.QuadId)
                    {
                        sideA = firstSide;
                        sideB = secondSide;
                    }
                    else
                    {
                        sideA = secondSide;
                        sideB = firstSide;
                    }
                }
                else if (firstSide.OuterStartLogicalVertexId == expectedSideAOuterStart && secondSide.OuterStartLogicalVertexId == expectedSideBOuterStart)
                {
                    sideA = firstSide;
                    sideB = secondSide;
                }
                else if (secondSide.OuterStartLogicalVertexId == expectedSideAOuterStart && firstSide.OuterStartLogicalVertexId == expectedSideBOuterStart)
                {
                    sideA = secondSide;
                    sideB = firstSide;
                }
                else
                {
                    return false;
                }

                if (sideA.OuterStartLogicalVertexId == sideB.OuterStartLogicalVertexId || sideA.OuterEndLogicalVertexId == sideB.OuterEndLogicalVertexId)
                {
                    return false;
                }

                if (!part.TryGetStructuralFaceSurfaceAreaId(sideA.QuadId, out int sideASurfaceAreaId) || !part.TryGetStructuralFaceSurfaceAreaId(sideB.QuadId, out int sideBSurfaceAreaId) || sideASurfaceAreaId != sideBSurfaceAreaId)
                {
                    return false;
                }

                segments[segmentIndex] = new DissolveSegment(segmentStartLogicalVertexId, segmentEndLogicalVertexId, cutEdge.Id, sideA.QuadId, sideA.BoundaryIndex, sideA.OuterStartLogicalVertexId, sideA.OuterEndLogicalVertexId, sideB.QuadId, sideB.BoundaryIndex, sideB.OuterStartLogicalVertexId, sideB.OuterEndLogicalVertexId);

                expectedSideAOuterStart = sideA.OuterEndLogicalVertexId;
                expectedSideBOuterStart = sideB.OuterEndLogicalVertexId;
            }

            HashSet<int> affectedQuads = new HashSet<int>();

            for (int i = 0; i < segments.Length; i++)
            {
                if (!affectedQuads.Add(segments[i].SideAQuadId) || !affectedQuads.Add(segments[i].SideBQuadId))
                {
                    return false;
                }
            }

            if (affectedQuads.Count != segments.Length * 2)
            {
                return false;
            }

            plan = new SurfaceCutDissolvePlan(orderedVertices, segments);
            return true;
        }
    }
}