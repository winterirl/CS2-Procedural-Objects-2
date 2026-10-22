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
    internal sealed class SolidDirtySet
    {
        internal readonly byte[] Marked;
        internal readonly int[] Values;
        internal ArraySegment<int> Indices => new ArraySegment<int>(Values, 0, Count);
        internal int Count { get; private set; }

        internal SolidDirtySet(int size)
        {
            Marked = new byte[size];
            Values = new int[size];
        }

        internal void Add(int index)
        {
            if ((uint)index >= (uint)Marked.Length) throw new InvalidOperationException("Solid update index is out of range.");
            if (Marked[index] != 0) return;
            Marked[index] = 1;
            Values[Count++] = index;
        }

        internal unsafe bool Collect(int[] input, int count, int operation = 0, int offset = 0, int limit = int.MaxValue, int[] offsets = null, int[] values = null)
        {
            fixed (int* source = input, target = Values, first = offsets, entries = values)
            fixed (byte* marked = Marked)
            {
                var c = new MeshDependencyKernels.Context
                {
                    Input = source, InputCount = count, Output = target, OutputCount = Count,
                    Offsets = first, Values = entries, Marked = marked, Capacity = Marked.Length, Operation = operation, Offset = offset, Limit = limit
                };
                MeshDependencyKernels.Run(ref c);
                Count = c.OutputCount;
                return c.Error == 0;
            }
        }

        internal void Clear()
        {
            Collect(Values, Count, 3);
            Count = 0;
        }

        internal void All()
        {
            Collect(Values, 0, 5);
        }
    }

    // Packed reverse memberships. Construction allocates once per layout;
    // following a dirty render vertex does not scan the mesh or allocate.
    internal sealed class SolidMembership
    {
        private readonly int[] m_Offsets, m_Values;

        private SolidMembership(int[] offsets, int[] values)
        {
            m_Offsets = offsets;
            m_Values = values;
        }

        internal static SolidMembership Build(int inputs, Action<Action<int, int>> visit, CancellationToken token)
        {
            var offsets = new int[checked(inputs + 1)];
            visit((input, output) => { offsets[input + 1] = checked(offsets[input + 1] + 1); });
            for (int i = 1; i < offsets.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                offsets[i] = checked(offsets[i] + offsets[i - 1]);
            }

            var values = new int[offsets[inputs]];
            var cursor = (int[])offsets.Clone();
            visit((input, output) => values[cursor[input]++] = output);
            return new SolidMembership(offsets, values);
        }

        internal void AddUsers(SolidDirtySet input, SolidDirtySet output)
        {
            if (!output.Collect(input.Values, input.Count, 1, offsets: m_Offsets, values: m_Values))
                throw new InvalidOperationException("Invalid mesh dependency map.");
        }
    }

    internal struct SolidCandidateSource
    {
        internal int Part, A, B;
    }

    internal struct SolidFacingSource
    {
        internal int Part, A, B, C, D, Corners;
    }

    internal struct SolidEndpoints
    {
        internal VertexRef A, B;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SolidSourceRefs
    {
        internal int A, B, C, D;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SolidFacingRefs
    {
        internal SolidSourceRefs Vertices;
        internal int Corners, Pad0, Pad1, Pad2;
    }

    internal sealed partial class MeshEditGpuGeometry
    {
        internal SolidCandidateSource[] CandidateSources;
        internal SolidFacingSource[] FacingSources;
        internal SolidBvhTriangleSource[] TriangleSources;
        internal int[] Parents, TriangleLeaves;
        internal int TreeSerial;
        internal int GeometryRevision;
        internal SolidEndpoints[] Endpoints;
        private object[] m_Meshes, m_Topologies, m_Structural;
        private object[] m_PositionArrays, m_NormalArrays, m_TriangleArrays;
        private int[] m_RenderOffsets, m_NormalCounts, m_TriangleCounts;
        private uint[] m_ReportedVersions;
        private bool[] m_UnknownParts;
        private Guid m_PersistentId;
        private SolidMembership m_PositionCandidates, m_PositionFacing, m_NormalFacing, m_PositionTriangles;
        private SolidDirtySet m_Positions, m_Directions;
        internal SolidDirtySet CandidateDirty, FacingDirty, TriangleDirty, NodeDirty;
        internal Vector3[] SourcePositions, SourceNormals;
        internal SolidSourceRefs[] CandidateRefs, TriangleRefs;
        internal SolidFacingRefs[] FacingRefs;
        internal int[][] PositionUploads, NormalUploads;
        internal int[][] PositionRanges, NormalRanges;
        internal int[] CandidateWork, FacingWork, TriangleWork, NodeWork, LevelStarts;
        internal int WorkRevision;
        internal long GpuBytes;
        private int[] m_Depths, m_LastPositions = Array.Empty<int>(), m_LastDirections = Array.Empty<int>();
        private bool m_WorkValid, m_WorkOcclusion, m_UploadPending;
        internal bool HasUploads => m_UploadPending;
        internal int SourceOffset(int part) => m_RenderOffsets[part];

        internal bool MatchesLayout(ProceduralObject obj, MeshSelectionMode mode)
        {
            if (!ReferenceEquals(Object, obj) || obj.PersistentId != m_PersistentId || Mode != mode || obj.Parts.Count != Parts.Length) return false;
            for (int p = 0; p < Parts.Length; p++)
            {
                var part = obj.Parts[p];
                if (!ReferenceEquals(part, Parts[p]) || part.TopologyVersion != TopologyVersions[p] || !ReferenceEquals(part.Mesh, m_Meshes[p]) || !ReferenceEquals(part.Topology, m_Topologies[p]) || !ReferenceEquals(part.StructuralTopology, m_Structural[p]) || !ReferenceEquals(part.Mesh.Vertices, m_PositionArrays[p]) || !ReferenceEquals(part.Mesh.Normals, m_NormalArrays[p]) || !ReferenceEquals(part.Mesh.Triangles, m_TriangleArrays[p]) || part.Mesh.Vertices.Length != m_RenderOffsets[p + 1] - m_RenderOffsets[p] || (part.Mesh.Normals?.Length ?? -1) != m_NormalCounts[p] || part.Mesh.Triangles.Length != m_TriangleCounts[p]) return false;
            }

            return true;
        }

        private int Key(int part, int render) => m_RenderOffsets[part] + render;

        internal void InitializeUpdates(MeshEditGpuSource source, CancellationToken token)
        {
            m_PersistentId = source.PersistentId;
            int parts = Parts.Length;
            m_Meshes = new object[parts];
            m_Topologies = new object[parts];
            m_Structural = new object[parts];
            m_PositionArrays = new object[parts];
            m_NormalArrays = new object[parts];
            m_TriangleArrays = new object[parts];
            m_RenderOffsets = new int[parts + 1];
            m_NormalCounts = new int[parts];
            m_TriangleCounts = new int[parts];
            m_ReportedVersions = (uint[])GeometryVersions.Clone();
            m_UnknownParts = new bool[parts];
            for (int p = 0; p < parts; p++)
            {
                var part = source.Parts[p];
                m_Meshes[p] = part.MeshIdentity;
                m_Topologies[p] = part.Topology;
                m_Structural[p] = part.StructuralTopology;
                m_PositionArrays[p] = part.LiveVertices;
                m_NormalArrays[p] = part.LiveNormals;
                m_TriangleArrays[p] = part.LiveTriangles;
                m_RenderOffsets[p + 1] = checked(m_RenderOffsets[p] + part.Vertices.Length);
                m_NormalCounts[p] = part.LiveNormals?.Length ?? -1;
                m_TriangleCounts[p] = part.LiveTriangles.Length;
            }

            int renders = m_RenderOffsets[parts];
            GpuBytes = checked((HasOcclusion ? 24L : 12L) * renders + 184L * Candidates.Length + 100L * Normals.Length + 68L * Triangles.Length + 52L * Nodes.Length + 4096L * 16 + 256);
            SourcePositions = new Vector3[renders];
            SourceNormals = new Vector3[HasOcclusion ? renders : 0];
            for (int p = 0; p < parts; p++)
            {
                token.ThrowIfCancellationRequested();
                Array.Copy(source.Parts[p].Vertices, 0, SourcePositions, m_RenderOffsets[p], source.Parts[p].Vertices.Length);
                var normals = source.Parts[p].Normals;
                if (HasOcclusion && normals != null) Array.Copy(normals, 0, SourceNormals, m_RenderOffsets[p], Math.Min(normals.Length, source.Parts[p].Vertices.Length));
            }

            CandidateRefs = new SolidSourceRefs[CandidateSources.Length];
            for (int i = 0; i < CandidateRefs.Length; i++)
            {
                var s = CandidateSources[i];
                CandidateRefs[i] = new SolidSourceRefs { A = Key(s.Part, s.A), B = Key(s.Part, s.B) };
            }

            FacingRefs = new SolidFacingRefs[FacingSources.Length];
            for (int i = 0; i < FacingRefs.Length; i++)
            {
                var s = FacingSources[i];
                FacingRefs[i] = new SolidFacingRefs
                {
                    Corners = s.Corners,
                    Vertices = new SolidSourceRefs { A = Key(s.Part, s.A), B = Key(s.Part, s.B), C = Key(s.Part, s.C), D = Key(s.Part, s.D) }
                };
            }

            TriangleRefs = new SolidSourceRefs[TriangleSources.Length];
            for (int i = 0; i < TriangleRefs.Length; i++)
            {
                var s = TriangleSources[i];
                TriangleRefs[i] = new SolidSourceRefs { A = Key(s.PartIndex, s.VertexA), B = Key(s.PartIndex, s.VertexB), C = Key(s.PartIndex, s.VertexC) };
            }

            m_Depths = new int[Nodes.Length];
            int deepest = 0;
            for (int i = 0; i < Nodes.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                int parent = Parents[i];
                if (parent >= i || parent < -1) throw new InvalidOperationException("Invalid mesh BVH parent order.");
                m_Depths[i] = parent < 0 ? 0 : m_Depths[parent] + 1;
                deepest = Math.Max(deepest, m_Depths[i]);
            }

            LevelStarts = new int[deepest + 2];
            NodeWork = Array.Empty<int>();
            CandidateWork = FacingWork = TriangleWork = Array.Empty<int>();
            PositionUploads = NormalUploads = new int[parts][];
            m_Positions = new SolidDirtySet(renders);
            m_Directions = new SolidDirtySet(HasOcclusion ? renders : 0);
            CandidateDirty = new SolidDirtySet(Candidates.Length);
            FacingDirty = new SolidDirtySet(Normals.Length);
            TriangleDirty = new SolidDirtySet(Triangles.Length);
            NodeDirty = new SolidDirtySet(Nodes.Length);
            m_PositionCandidates = SolidMembership.Build(renders, add =>
            {
                for (int i = 0; i < CandidateSources.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    var s = CandidateSources[i];
                    add(Key(s.Part, s.A), i);
                    if (s.B != s.A) add(Key(s.Part, s.B), i);
                }
            }, token);
            if (!HasOcclusion) return;
            m_PositionFacing = SolidMembership.Build(renders, add =>
            {
                for (int i = 0; i < FacingSources.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    var s = FacingSources[i];
                    if (s.Corners == 0) continue;
                    add(Key(s.Part, s.A), i);
                    add(Key(s.Part, s.B), i);
                    add(Key(s.Part, s.C), i);
                    if (s.Corners == 4) add(Key(s.Part, s.D), i);
                }
            }, token);
            m_NormalFacing = SolidMembership.Build(renders, add =>
            {
                for (int i = 0; i < FacingSources.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    var s = FacingSources[i];
                    if (s.Corners == 0) add(Key(s.Part, s.A), i);
                }
            }, token);
            m_PositionTriangles = SolidMembership.Build(renders, add =>
            {
                for (int i = 0; i < TriangleSources.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    var s = TriangleSources[i];
                    add(Key(s.PartIndex, s.VertexA), i);
                    add(Key(s.PartIndex, s.VertexB), i);
                    add(Key(s.PartIndex, s.VertexC), i);
                }
            }, token);
        }

        internal void Notice(GeometryChange change, bool occlusion = true)
        {
            int p = change.PartIndex;
            if ((uint)p >= (uint)Parts.Length || !ReferenceEquals(Parts[p], change.Part) || TopologyVersions[p] != change.Topology) return;
            if (!occlusion) OcclusionCurrent = false;
            if (change.Before != m_ReportedVersions[p]) m_UnknownParts[p] = true;
            m_ReportedVersions[p] = change.After;
            AddIndices(p, change.Positions, m_Positions);
            if (occlusion && HasOcclusion) AddIndices(p, change.Normals, m_Directions);
        }

        private void AddIndices(int p, IReadOnlyList<int> indices, SolidDirtySet target)
        {
            if (indices == null)
            {
                m_UnknownParts[p] = true;
                return;
            }

            int count = m_RenderOffsets[p + 1] - m_RenderOffsets[p];
            if (indices is int[] array)
            {
                if (!target.Collect(array, array.Length, offset: m_RenderOffsets[p], limit: count)) m_UnknownParts[p] = true;
                return;
            }

            for (int i = 0; i < indices.Count; i++)
            {
                if ((uint)indices[i] >= (uint)count)
                {
                    m_UnknownParts[p] = true;
                    continue;
                }

                target.Add(Key(p, indices[i]));
            }
        }

        internal void Update(bool occlusion = true)
        {
            if (!occlusion) OcclusionCurrent = false;
            for (int p = 0; p < Parts.Length; p++)
            {
                if (GeometryVersions[p] == Parts[p].GeometryVersion) continue;
                if (m_UnknownParts[p] || m_ReportedVersions[p] != Parts[p].GeometryVersion)
                {
                    for (int i = m_RenderOffsets[p]; i < m_RenderOffsets[p + 1]; i++)
                    {
                        m_Positions.Add(i);
                        if (occlusion && i - m_RenderOffsets[p] < m_NormalCounts[p]) m_Directions.Add(i);
                    }
                }
            }

            bool reuse = m_WorkValid && m_WorkOcclusion == occlusion && SameIndices(m_Positions, m_LastPositions) && SameIndices(m_Directions, m_LastDirections);
            if (!reuse)
            {
                CandidateDirty.Clear();
                FacingDirty.Clear();
                TriangleDirty.Clear();
                NodeDirty.Clear();
                bool broad = m_Positions.Count >= 2048 && m_Positions.Count * 3L >= SourcePositions.Length;
                if (broad)
                {
                    CandidateDirty.All();
                    if (occlusion)
                    {
                        FacingDirty.All();
                        TriangleDirty.All();
                        NodeDirty.All();
                    }
                }
                else
                {
                    m_PositionCandidates.AddUsers(m_Positions, CandidateDirty);
                    if (occlusion)
                    {
                        m_PositionFacing.AddUsers(m_Positions, FacingDirty);
                        m_PositionTriangles.AddUsers(m_Positions, TriangleDirty);
                        m_NormalFacing.AddUsers(m_Directions, FacingDirty);
                        if (!NodeDirty.Collect(TriangleDirty.Values, TriangleDirty.Count, 2, offsets: TriangleLeaves, values: Parents))
                            throw new InvalidOperationException("Invalid mesh BVH dependency map.");
                    }
                }

                m_LastPositions = CopyIndices(m_Positions, m_LastPositions);
                m_LastDirections = CopyIndices(m_Directions, m_LastDirections);
                PositionUploads = SplitByPart(m_LastPositions);
                NormalUploads = SplitByPart(m_LastDirections);
                PositionRanges = BuildRanges(PositionUploads);
                NormalRanges = BuildRanges(NormalUploads);
                CandidateWork = CopyIndices(CandidateDirty, CandidateWork);
                FacingWork = CopyIndices(FacingDirty, FacingWork);
                TriangleWork = CopyIndices(TriangleDirty, TriangleWork);
                Array.Clear(LevelStarts, 0, LevelStarts.Length);
                foreach (int node in NodeDirty.Indices) LevelStarts[m_Depths[node] + 1]++;
                for (int i = 1; i < LevelStarts.Length; i++) LevelStarts[i] += LevelStarts[i - 1];
                if (NodeWork.Length != NodeDirty.Count) NodeWork = new int[NodeDirty.Count];
                var cursor = (int[])LevelStarts.Clone();
                foreach (int node in NodeDirty.Indices) NodeWork[cursor[m_Depths[node]]++] = node;
                m_WorkValid = true;
                m_WorkOcclusion = occlusion;
                WorkRevision++;
            }

            for (int p = 0; p < Parts.Length; p++)
            {
                GeometryVersions[p] = m_ReportedVersions[p] = Parts[p].GeometryVersion;
                m_UnknownParts[p] = false;
            }

            m_UploadPending = true;
            GeometryRevision++;
        }

        private static unsafe bool SameIndices(SolidDirtySet set, int[] previous)
        {
            if (set.Count != previous.Length) return false;
            fixed (int* input = set.Values, output = previous)
            {
                var c = new MeshDependencyKernels.Context { Input = input, Output = output, InputCount = previous.Length, Operation = 4 };
                MeshDependencyKernels.Run(ref c);
                return c.Error == 0;
            }
        }

        private static int[] CopyIndices(SolidDirtySet set, int[] target)
        {
            if (target.Length != set.Count) target = new int[set.Count];
            Array.Copy(set.Values, target, set.Count);
            return target;
        }

        private int[][] SplitByPart(int[] keys)
        {
            if (Parts.Length == 1) return new[] { keys };
            var lists = new List<int>[Parts.Length];
            for (int p = 0; p < lists.Length; p++) lists[p] = new List<int>();
            foreach (int key in keys)
            {
                int lo = 0, hi = Parts.Length;
                while (lo + 1 < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (m_RenderOffsets[mid] <= key) lo = mid;
                    else hi = mid;
                }

                lists[lo].Add(key - m_RenderOffsets[lo]);
            }

            var result = new int[Parts.Length][];
            for (int p = 0; p < result.Length; p++) result[p] = lists[p].ToArray();
            return result;
        }

        private static int[][] BuildRanges(int[][] parts)
        {
            var result = new int[parts.Length][];
            for (int p = 0; p < parts.Length; p++)
            {
                var indices = parts[p];
                var ranges = new List<int>();
                for (int i = 0; i < indices.Length;)
                {
                    int start = indices[i++], count = 1;
                    while (i < indices.Length && indices[i] == start + count)
                    {
                        count++;
                        i++;
                    }

                    ranges.Add(start);
                    ranges.Add(count);
                    if (ranges.Count > 64) break;
                }

                // Null means fragmented. Keep its compact indexed upload.
                if (ranges.Count <= 64) result[p] = ranges.ToArray();
            }

            return result;
        }

        // Solid payloads may be retained for an immediate policy toggle. If a
        // Solid edit has not reached the render pass, those GPU payloads cannot
        // be reused as current. X-Ray never uploads or refits them.
        internal void EnterXRay()
        {
            if (m_UploadPending && FacingDirty.Count + TriangleDirty.Count + NodeDirty.Count != 0) OcclusionCurrent = false;
            m_WorkValid = false;
            FacingWork = TriangleWork = NodeWork = Array.Empty<int>();
            NormalUploads = new int[Parts.Length][];
            WorkRevision++;
            FacingDirty.Clear();
            TriangleDirty.Clear();
            NodeDirty.Clear();
            m_Directions.Clear();
        }

        internal void Uploaded()
        {
            m_Positions.Clear();
            m_Directions.Clear();
            m_UploadPending = false;
        }
    }
}