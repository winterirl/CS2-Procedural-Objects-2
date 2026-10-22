using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry.Topology;

namespace PO2.Geometry.Editing
{
    public static partial class TopologyEditor
    {
        private static void MarkStripEdgesExamined(StructuralLoop strip, bool[] examinedEdges)
        {
            if (strip == null || examinedEdges == null)
            {
                return;
            }

            for (int i = 0; i < strip.Steps.Count; i++)
            {
                StructuralLoopStep step = strip.Steps[i];

                if (step.EntryEdgeId >= 0 && step.EntryEdgeId < examinedEdges.Length)
                {
                    examinedEdges[step.EntryEdgeId] = true;
                }

                if (step.ExitEdgeId >= 0 && step.ExitEdgeId < examinedEdges.Length)
                {
                    examinedEdges[step.ExitEdgeId] = true;
                }
            }
        }

        public static bool TryBuildStructuralLoop(StructuralTopology topology, int startEdgeId, out StructuralLoop loop)
        {
            loop = null;

            if (topology == null || startEdgeId < 0 || startEdgeId >= topology.StructuralEdgeCount)
            {
                return false;
            }

            StructuralEdge startEdge = topology.GetEdge(startEdgeId);

            if (!startEdge.IsManifold || startEdge.AdjacentFaceCount == 0 || startEdge.AdjacentFaceCount > 2)
            {
                return false;
            }

            // Start in one adjacent quad and walk straight across every quad.
            // If it ends at a boundary, walk the other side of the starting
            // edge too so the returned open strip contains both directions.
            List<StructuralLoopStep> steps = new List<StructuralLoopStep>();
            HashSet<int> visitedQuads = new HashSet<int>();

            int firstQuad = startEdge.AdjacentFaceIds[0];
            if (!TryWalkLoopDirection(topology, startEdgeId, firstQuad, true, visitedQuads, steps, out bool closed))
            {
                return false;
            }

            if (!closed && startEdge.AdjacentFaceCount == 2)
            {
                int secondQuad = startEdge.AdjacentFaceIds[1];

                if (!visitedQuads.Contains(secondQuad))
                {
                    if (!TryWalkLoopDirection(topology, startEdgeId, secondQuad, true, visitedQuads, steps, out bool secondClosed))
                    {
                        return false;
                    }

                    if (secondClosed)
                    {
                        return false;
                    }
                }
            }

            if (steps.Count == 0)
            {
                return false;
            }

            loop = new StructuralLoop(steps.ToArray(), closed);
            return true;
        }

        public static bool TryBuildClosedStructuralLoop(StructuralTopology topology, int startEdgeId, out StructuralLoop loop)
        {
            if (!TryBuildStructuralLoop(topology, startEdgeId, out loop) || !loop.IsClosed)
            {
                loop = null;
                return false;
            }

            return true;
        }

        public static bool TryBuildOpenStructuralStrip(StructuralTopology topology, int startEdgeId, out StructuralLoop strip)
        {
            strip = null;

            if (!TryBuildStructuralLoop(topology, startEdgeId, out StructuralLoop candidate) || candidate.IsClosed || !HasExactlyTwoBoundaryTermini(topology, candidate))
            {
                return false;
            }

            strip = candidate;
            return true;
        }

        public static bool TryBuildStructuralEdgeLoop(StructuralTopology topology, int startEdgeId, out int[] edgeIds, out bool isClosed)
        {
            edgeIds = Array.Empty<int>();
            isClosed = false;

            if (topology == null || startEdgeId < 0 || startEdgeId >= topology.StructuralEdgeCount)
            {
                return false;
            }

            StructuralEdge startEdge = topology.GetEdge(startEdgeId);

            List<int> collected = new List<int>();
            HashSet<int> visited = new HashSet<int>();

            // This is an edge loop through structural vertices, not the quad strip used by loop cut.
            // Similar name, annoyingly different walk.
            collected.Add(startEdgeId);
            visited.Add(startEdgeId);

            if (!TryWalkStructuralEdgeLoopDirection(topology, startEdgeId, startEdge.LogicalVertexA, visited, collected, out bool firstClosed))
            {
                return false;
            }

            if (firstClosed)
            {
                edgeIds = collected.ToArray();
                isClosed = true;
                return true;
            }

            if (!TryWalkStructuralEdgeLoopDirection(topology, startEdgeId, startEdge.LogicalVertexB, visited, collected, out bool secondClosed))
            {
                return false;
            }

            if (secondClosed)
            {
                // A loop cannot end in one direction and close only after independently walking the other side of the start edge.
                // Treat that as ambiguous topology instead of returning a partial loop.
                return false;
            }

            edgeIds = collected.ToArray();
            return true;
        }

        private static bool TryWalkStructuralEdgeLoopDirection(StructuralTopology topology, int startEdgeId, int startVertexId, HashSet<int> visited, List<int> collected, out bool closed)
        {
            closed = false;

            int currentEdgeId = startEdgeId;
            int currentVertexId = startVertexId;

            while (true)
            {
                if (!TryGetStructuralEdgeLoopContinuation(topology, currentEdgeId, currentVertexId, out int nextEdgeId, out int nextVertexId, out bool hasContinuation))
                {
                    return false;
                }

                if (!hasContinuation)
                {
                    return true;
                }

                if (nextEdgeId == startEdgeId)
                {
                    closed = true;
                    return true;
                }

                if (!visited.Add(nextEdgeId))
                {
                    return false;
                }

                collected.Add(nextEdgeId);
                currentEdgeId = nextEdgeId;
                currentVertexId = nextVertexId;
            }
        }

        private static bool TryGetStructuralEdgeLoopContinuation(StructuralTopology topology, int currentEdgeId, int currentVertexId, out int nextEdgeId, out int nextVertexId, out bool hasContinuation)
        {
            nextEdgeId = -1;
            nextVertexId = -1;
            hasContinuation = false;

            if (currentEdgeId < 0 || currentEdgeId >= topology.StructuralEdgeCount)
            {
                return false;
            }

            StructuralEdge currentEdge = topology.GetEdge(currentEdgeId);

            if (currentEdge.LogicalVertexA != currentVertexId && currentEdge.LogicalVertexB != currentVertexId)
            {
                return false;
            }

            int candidateCount = 0;
            IndexSpan incidentEdgeIds = topology.GetIncidentEdgeIds(currentVertexId);

            for (int incidentIndex = 0; incidentIndex < incidentEdgeIds.Count; incidentIndex++)
            {
                int edgeId = incidentEdgeIds[incidentIndex];
                if (edgeId == currentEdgeId)
                {
                    continue;
                }

                StructuralEdge candidate = topology.GetEdge(edgeId);

                int otherVertexId;

                if (candidate.LogicalVertexA == currentVertexId)
                {
                    otherVertexId = candidate.LogicalVertexB;
                }
                else if (candidate.LogicalVertexB == currentVertexId)
                {
                    otherVertexId = candidate.LogicalVertexA;
                }
                else
                {
                    continue;
                }

                // Adjacent edges around the same structural face turn a corner.
                // An edge loop continues straight through the structural vertex, so
                // its continuation is the incident edge that shares no face with the incoming edge.
                if (StructuralEdgesShareFace(currentEdge, candidate))
                {
                    continue;
                }

                candidateCount++;
                nextEdgeId = edgeId;
                nextVertexId = otherVertexId;

                if (candidateCount > 1)
                {
                    // crazy topology has no unique edge-loop continuation.
                    // Stop isntead of guessing.
                    return false;
                }
            }

            hasContinuation = candidateCount == 1;
            return true;
        }

        private static bool StructuralEdgesShareFace(StructuralEdge first, StructuralEdge second)
        {
            for (int i = 0; i < first.AdjacentFaceIds.Count; i++)
            {
                int faceId = first.AdjacentFaceIds[i];

                for (int j = 0; j < second.AdjacentFaceIds.Count; j++)
                {
                    if (second.AdjacentFaceIds[j] == faceId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasExactlyTwoBoundaryTermini(StructuralTopology topology, StructuralLoop strip)
        {
            if (topology == null || strip == null || strip.IsClosed)
            {
                return false;
            }

            HashSet<int> boundaryEdgeIds = new HashSet<int>();

            for (int i = 0; i < strip.Steps.Count; i++)
            {
                StructuralLoopStep step = strip.Steps[i];

                StructuralEdge entryEdge = topology.GetEdge(step.EntryEdgeId);
                StructuralEdge exitEdge = topology.GetEdge(step.ExitEdgeId);

                if (entryEdge.IsBoundary)
                {
                    boundaryEdgeIds.Add(entryEdge.Id);
                }

                if (exitEdge.IsBoundary)
                {
                    boundaryEdgeIds.Add(exitEdge.Id);
                }
            }

            return boundaryEdgeIds.Count == 2;
        }

        private static bool TryWalkLoopDirection(StructuralTopology topology, int startEdgeId, int startQuadId, bool startEdgeForwardCanonical, HashSet<int> visitedQuads, List<StructuralLoopStep> steps, out bool closed)
        {
            closed = false;

            int currentEntryEdgeId = startEdgeId;
            int currentQuadId = startQuadId;
            bool currentEntryForwardCanonical = startEdgeForwardCanonical;

            while (true)
            {
                if (!topology.TryGetQuad(currentQuadId, out StructuralFace currentQuad) || !visitedQuads.Add(currentQuadId) || !topology.TryGetBoundaryIndex(currentQuadId, currentEntryEdgeId, out int boundaryIndex))
                {
                    return false;
                }

                int exitBoundaryIndex = (boundaryIndex + 2) % 4;

                int exitEdgeId = topology.GetBoundaryEdgeId(currentQuadId, exitBoundaryIndex);

                bool entryBoundaryForward = topology.IsBoundaryForward(currentQuadId, boundaryIndex);

                bool reverseParameter = currentEntryForwardCanonical != entryBoundaryForward;

                // With the quad ordered as v0,v1,v2,v3, the paired point on the opposite boundary is interpolated from v3 -> v2.
                // That direction is opposite the authored exit boundary v2 -> v3.
                // If this step reverses the global parameter on entry the global direction on exit flips accordingly.
                bool exitBoundaryForward = topology.IsBoundaryForward(currentQuadId, exitBoundaryIndex);

                bool exitForwardCanonical = reverseParameter ? exitBoundaryForward : !exitBoundaryForward;

                steps.Add(new StructuralLoopStep(currentQuadId, currentEntryEdgeId, exitEdgeId, boundaryIndex, reverseParameter));

                if (exitEdgeId == startEdgeId)
                {
                    // A closed strip must return to the starting edge with the same parameter direction.
                    // Otherwise it is a twisted/non-orientable strip where arbitrary t values cannot produce cut points around the seam.
                    if (exitForwardCanonical != startEdgeForwardCanonical)
                    {
                        return false;
                    }

                    closed = true;
                    return true;
                }

                StructuralEdge exitEdge = topology.GetEdge(exitEdgeId);

                if (!exitEdge.IsManifold)
                {
                    return false;
                }

                if (exitEdge.IsBoundary)
                {
                    return true;
                }

                if (!topology.TryGetOtherAdjacentFace(exitEdgeId, currentQuadId, out int nextQuadId))
                {
                    return false;
                }

                if (visitedQuads.Contains(nextQuadId))
                {
                    return false;
                }

                currentEntryEdgeId = exitEdgeId;
                currentQuadId = nextQuadId;
                currentEntryForwardCanonical = exitForwardCanonical;
            }
        }
    }
}