using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    // Explicit lanes match PO2SolidTypes.hlsl. Slots only change on publication.
    [StructLayout(LayoutKind.Sequential)]
    internal struct SolidCandidate
    {
        internal Vector4 A, B;
        internal uint FirstNormal, NormalCount, AlwaysFacing, Part;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SolidNormal
    {
        internal Vector4 A, B, C, D;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SolidNode
    {
        internal Vector4 Min, Max;
        internal int FirstTriangle, TriangleCount, Child, Escape;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SolidTriangle
    {
        internal Vector4 A, B, C;
    }

    internal sealed partial class MeshEditGpuGeometry
    {
        internal ProceduralObject Object;
        internal MeshSelectionMode Mode;
        internal MeshPart[] Parts;
        internal uint[] GeometryVersions, TopologyVersions;
        internal SolidCandidate[] Candidates;
        internal SolidNormal[] Normals;
        internal SolidNode[] Nodes;
        internal SolidTriangle[] Triangles;
        internal VertexRef[] Vertices;
        internal StructuralEdgeRef[] Edges;
        internal Dictionary<VertexRef, int> VertexSlots = new Dictionary<VertexRef, int>();
        internal Dictionary<StructuralEdgeRef, int> EdgeSlots = new Dictionary<StructuralEdgeRef, int>();
        internal float OrthoLength;
        internal bool HasOcclusion, OcclusionCurrent;

        internal bool Matches(ProceduralObject obj, MeshSelectionMode mode)
        {
            if (!MatchesLayout(obj, mode)) return false;
            for (int i = 0; i < Parts.Length; i++)
                if (Parts[i].GeometryVersion != GeometryVersions[i])
                    return false;
            return true;
        }

        internal static Vector4 Lane(Vector3 v, float w = 0) => new Vector4(v.x, v.y, v.z, w);
        internal static float SafeScale(float v) => Mathf.Abs(v) < 0.000001f ? (v < 0 ? -0.000001f : 0.000001f) : v;

        internal static void Finite(Vector4 value)
        {
            if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z) || !Finite(value.w))
                throw new InvalidOperationException("Solid geometry contains a non-finite value.");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // Worker entry. Mutable streams are detached; topology instances are immutable.
        internal static MeshEditGpuGeometry Build(MeshEditGpuSource source, MeshEditGpuGeometry tree, CancellationToken token)
        {
            var mode = source.Mode;
            var data = new MeshEditGpuGeometry
            {
                Object = source.Object, Mode = mode, HasOcclusion = source.NeedsOcclusion,
                OcclusionCurrent = source.NeedsOcclusion, Parts = new MeshPart[source.Parts.Length],
                GeometryVersions = new uint[source.Parts.Length], TopologyVersions = new uint[source.Parts.Length]
            };
            long candidateCount = source.CandidateCount;
            var candidateSources = new List<SolidCandidateSource>((int)candidateCount);
            var facingSources = new List<SolidFacingSource>();
            var endpoints = new List<SolidEndpoints>(mode == MeshSelectionMode.Edge ? (int)candidateCount : 0);
            var candidates = new List<SolidCandidate>((int)candidateCount);
            var normals = new List<SolidNormal>();
            var vertices = new List<VertexRef>(mode == MeshSelectionMode.Vertex ? (int)candidateCount : 0);
            var edges = new List<StructuralEdgeRef>(mode == MeshSelectionMode.Edge ? (int)candidateCount : 0);
            for (int p = 0; p < data.Parts.Length; p++)
            {
                token.ThrowIfCancellationRequested();
                var part = source.Parts[p];
                data.Parts[p] = part.Identity;
                data.GeometryVersions[p] = part.GeometryVersion;
                data.TopologyVersions[p] = part.TopologyVersion;
                if (mode == MeshSelectionMode.Vertex)
                    for (int v = 0; v < part.Topology.LogicalVertexCount; v++)
                    {
                        if ((v & 255) == 0) token.ThrowIfCancellationRequested();
                        var vertex = part.Topology.GetVertex(v);
                        var reference = new VertexRef(p, vertex.Id);
                        Vector3 position = part.Vertices[vertex.RenderVertexIndices[0]];
                        var c = new SolidCandidate
                        {
                            A = Lane(position), B = Lane(position), Part = (uint)p,
                            FirstNormal = (uint)normals.Count
                        };
                        var values = part.Normals;
                        if (!source.NeedsOcclusion || values == null || values.Length == 0) c.AlwaysFacing = 1;
                        else
                            foreach (int index in vertex.RenderVertexIndices)
                            {
                                if (index >= 0 && index < values.Length)
                                {
                                    normals.Add(new SolidNormal { A = Lane(values[index]) });
                                    facingSources.Add(new SolidFacingSource { Part = p, A = index });
                                }
                            }

                        c.NormalCount = (uint)normals.Count - c.FirstNormal;
                        data.VertexSlots.Add(reference, candidates.Count);
                        candidates.Add(c);
                        vertices.Add(reference);
                        candidateSources.Add(new SolidCandidateSource
                        {
                            Part = p,
                            A = vertex.RenderVertexIndices[0], B = vertex.RenderVertexIndices[0]
                        });
                    }

                if (mode != MeshSelectionMode.Edge) continue;
                var topology = part.StructuralTopology;
                for (int e = 0; e < topology.StructuralEdgeCount; e++)
                {
                    if ((e & 255) == 0) token.ThrowIfCancellationRequested();
                    var edge = topology.GetEdge(e);
                    var a = new VertexRef(p, edge.LogicalVertexA);
                    var b = new VertexRef(p, edge.LogicalVertexB);
                    var reference = new StructuralEdgeRef(p, e);
                    int sourceA = part.Topology.GetVertex(a.VertexId).RenderVertexIndices[0];
                    int sourceB = part.Topology.GetVertex(b.VertexId).RenderVertexIndices[0];
                    var c = new SolidCandidate
                    {
                        A = Lane(part.Vertices[sourceA]), B = Lane(part.Vertices[sourceB]),
                        Part = (uint)p, FirstNormal = (uint)normals.Count
                    };
                    if (!source.NeedsOcclusion || edge.AdjacentFaceCount == 0) c.AlwaysFacing = 1;
                    if (source.NeedsOcclusion)
                        foreach (int f in edge.AdjacentFaceIds)
                        {
                            if (f < 0 || f >= topology.FaceCount)
                            {
                                c.AlwaysFacing = 1;
                                continue;
                            }

                            var face = topology.GetFace(f);
                            var positions = part.Vertices;
                            if (face.CornerCount < 3 || face.CornerCount > 4)
                            {
                                c.AlwaysFacing = 1;
                                continue;
                            }

                            int ia = face.GetRenderVertexIndex(0), ib = face.GetRenderVertexIndex(1), ic = face.GetRenderVertexIndex(2);
                            int id = face.IsQuad ? face.GetRenderVertexIndex(3) : ia;
                            if (ia < 0 || ib < 0 || ic < 0 || id < 0 || ia >= positions.Length || ib >= positions.Length || ic >= positions.Length || id >= positions.Length)
                            {
                                c.AlwaysFacing = 1;
                                continue;
                            }

                            normals.Add(new SolidNormal
                            {
                                A = Lane(positions[ia], face.CornerCount), B = Lane(positions[ib]),
                                C = Lane(positions[ic]), D = Lane(positions[id])
                            });
                            facingSources.Add(new SolidFacingSource { Part = p, A = ia, B = ib, C = ic, D = id, Corners = face.CornerCount });
                        }

                    c.NormalCount = (uint)normals.Count - c.FirstNormal;
                    data.EdgeSlots.Add(reference, candidates.Count);
                    endpoints.Add(new SolidEndpoints { A = a, B = b });
                    candidateSources.Add(new SolidCandidateSource { Part = p, A = sourceA, B = sourceB });
                    candidates.Add(c);
                    edges.Add(reference);
                }
            }

            data.CandidateSources = candidateSources.ToArray();
            data.FacingSources = facingSources.ToArray();
            data.Endpoints = endpoints.ToArray();
            data.Candidates = candidates.ToArray();
            data.Normals = normals.ToArray();
            data.Vertices = vertices.ToArray();
            data.Edges = edges.ToArray();
            token.ThrowIfCancellationRequested();
            if (!source.NeedsOcclusion)
            {
                data.Nodes = Array.Empty<SolidNode>();
                data.Triangles = Array.Empty<SolidTriangle>();
                data.Parents = Array.Empty<int>();
                data.TriangleLeaves = Array.Empty<int>();
                data.TriangleSources = Array.Empty<SolidBvhTriangleSource>();
            }
            else if (tree == null) SolidVisibilityBvh.BuildDetached(source, data, token);
            else
            {
                data.Nodes = tree.Nodes;
                data.Triangles = tree.Triangles;
                data.Parents = tree.Parents;
                data.TriangleLeaves = tree.TriangleLeaves;
                data.TriangleSources = tree.TriangleSources;
                data.OrthoLength = tree.OrthoLength;
                data.TreeSerial = tree.TreeSerial;
            }

            if (candidates.Count > 1048576)
                throw new InvalidOperationException("Solid GPU geometry exceeds the marker count limit.");
            for (int i = 0; i < data.Candidates.Length; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                Finite(data.Candidates[i].A);
                Finite(data.Candidates[i].B);
            }

            for (int i = 0; i < data.Normals.Length; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                var n = data.Normals[i];
                Finite(n.A);
                Finite(n.B);
                Finite(n.C);
                Finite(n.D);
            }

            data.InitializeUpdates(source, token);
            token.ThrowIfCancellationRequested();
            return data;
        }
    }
}