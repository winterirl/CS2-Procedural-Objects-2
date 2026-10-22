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
        public static bool TryAnalyzeLoopDissolve(MeshPart part, IReadOnlyList<int> selectedLogicalVertexIds, out LoopDissolvePlan plan)
        {
            plan = null;

            if (part == null || selectedLogicalVertexIds == null || selectedLogicalVertexIds.Count < 3 || !IsFullyStructuralMesh(part))
            {
                return false;
            }

            MeshTopology meshTopology = part.Topology;
            StructuralTopology structuralTopology = part.StructuralTopology;

            // A loop dissolve is intentionally strict: one simple closed ring,
            // ordinary grid valence, two unique quads per segment, and matching
            // Surface Areas on the pair that will become one face.
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

            foreach (int logicalVertexId in selectedVertices)
            {
                selectedNeighbors.Add(logicalVertexId, new List<int>(2));
                structuralValence.Add(logicalVertexId, 0);
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
                }

                if (bSelected)
                {
                    structuralValence[edge.LogicalVertexB]++;
                }

                if (!aSelected || !bSelected)
                {
                    continue;
                }

                if (!edge.IsManifold || edge.AdjacentFaceCount != 2)
                {
                    return false;
                }

                selectedNeighbors[edge.LogicalVertexA].Add(edge.LogicalVertexB);
                selectedNeighbors[edge.LogicalVertexB].Add(edge.LogicalVertexA);
                selectedEdgeCount++;
            }

            if (selectedEdgeCount != selectedVertices.Count)
            {
                return false;
            }

            foreach (int logicalVertexId in selectedVertices)
            {
                // An interior loop vertex needs two ring edges and one edge leaving each side.
                if (selectedNeighbors[logicalVertexId].Count != 2 || structuralValence[logicalVertexId] != 4)
                {
                    return false;
                }
            }

            int[] orderedVertices = new int[selectedVertices.Count];
            int startVertexId = FindMinimumVertexId(selectedVertices);
            int previousVertexId = -1;
            int currentVertexId = startVertexId;

            for (int i = 0; i < orderedVertices.Length; i++)
            {
                orderedVertices[i] = currentVertexId;

                List<int> neighbors = selectedNeighbors[currentVertexId];

                int nextVertexId;

                if (previousVertexId < 0)
                {
                    nextVertexId = Math.Min(neighbors[0], neighbors[1]);
                }
                else if (neighbors[0] == previousVertexId)
                {
                    nextVertexId = neighbors[1];
                }
                else if (neighbors[1] == previousVertexId)
                {
                    nextVertexId = neighbors[0];
                }
                else
                {
                    return false;
                }

                if (i < orderedVertices.Length - 1)
                {
                    for (int visitedIndex = 0; visitedIndex <= i; visitedIndex++)
                    {
                        if (orderedVertices[visitedIndex] == nextVertexId)
                        {
                            return false;
                        }
                    }

                    if (nextVertexId == startVertexId)
                    {
                        return false;
                    }
                }
                else if (nextVertexId != startVertexId)
                {
                    return false;
                }

                previousVertexId = currentVertexId;
                currentVertexId = nextVertexId;
            }

            DissolveSegment[] segments = new DissolveSegment[orderedVertices.Length];

            int firstSideAOuterStart = -1;
            int firstSideBOuterStart = -1;
            int expectedSideAOuterStart = -1;
            int expectedSideBOuterStart = -1;

            for (int segmentIndex = 0; segmentIndex < orderedVertices.Length; segmentIndex++)
            {
                int startLogicalVertexId = orderedVertices[segmentIndex];
                int endLogicalVertexId = orderedVertices[(segmentIndex + 1) % orderedVertices.Length];

                if (!structuralTopology.TryGetEdgeByLogicalVertices(startLogicalVertexId, endLogicalVertexId, out StructuralEdge loopEdge) || !loopEdge.IsManifold || loopEdge.AdjacentFaceCount != 2 || !TryGetDissolveQuadSide(part, loopEdge.AdjacentFaceIds[0], loopEdge.Id, startLogicalVertexId, endLogicalVertexId, selectedVertices, out DissolveQuadSide firstSide) || !TryGetDissolveQuadSide(part, loopEdge.AdjacentFaceIds[1], loopEdge.Id, startLogicalVertexId, endLogicalVertexId, selectedVertices, out DissolveQuadSide secondSide))
                {
                    return false;
                }

                DissolveQuadSide sideA;
                DissolveQuadSide sideB;

                if (segmentIndex == 0)
                {
                    // Quad ids provide a deterministic but otherwise arbitrary A/B assignment for the first segment.
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

                    if (sideA.OuterStartLogicalVertexId == sideB.OuterStartLogicalVertexId || sideA.OuterEndLogicalVertexId == sideB.OuterEndLogicalVertexId)
                    {
                        return false;
                    }

                    firstSideAOuterStart = sideA.OuterStartLogicalVertexId;
                    firstSideBOuterStart = sideB.OuterStartLogicalVertexId;
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

                segments[segmentIndex] = new DissolveSegment(startLogicalVertexId, endLogicalVertexId, loopEdge.Id, sideA.QuadId, sideA.BoundaryIndex, sideA.OuterStartLogicalVertexId, sideA.OuterEndLogicalVertexId, sideB.QuadId, sideB.BoundaryIndex, sideB.OuterStartLogicalVertexId, sideB.OuterEndLogicalVertexId);

                expectedSideAOuterStart = sideA.OuterEndLogicalVertexId;
                expectedSideBOuterStart = sideB.OuterEndLogicalVertexId;
            }

            if (expectedSideAOuterStart != firstSideAOuterStart || expectedSideBOuterStart != firstSideBOuterStart)
            {
                return false;
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

            plan = new LoopDissolvePlan(orderedVertices, segments);
            return true;
        }
    }
}