using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace PO2.Tools.MeshEdit
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SolidSourcePatch
    {
        internal uint Index;
        internal Vector3 Value;
    }

    internal sealed partial class MeshEditGpuSession
    {
        private const int kPatchCapacity = 4096;
        private readonly SolidSourcePatch[] m_PatchData = new SolidSourcePatch[kPatchCapacity];
        private static readonly uint[] s_ClearFault = new uint[1];
        private int m_SubmittedWorkRevision = -1;

        private void UploadGeometryChanges(CommandBuffer cmd)
        {
            var g = m_Geometry;
            var b = m_Buffers;
            if (!g.HasUploads) return;
            cmd.BeginSample("PO2 mesh source updates");
            // Faults stay latched until this generation is replaced.
            cmd.SetComputeIntParam(m_Compute, "_SourceCount", g.SourcePositions.Length);
            Bind(cmd, m_Patch, "_SourcePatches", b.Patches);
            Bind(cmd, m_Patch, "_UpdateFault", b.UpdateFault);
            UploadSource(cmd, false);
            if (NeedsOcclusion) UploadSource(cmd, true);
            if (m_SubmittedWorkRevision != g.WorkRevision)
            {
                if (g.CandidateWork.Length != g.Candidates.Length) RecordUpload(cmd, b.CandidateWork, g.CandidateWork, 0, 0, g.CandidateWork.Length, 2);
                if (NeedsOcclusion)
                {
                    if (g.FacingWork.Length != g.Normals.Length) RecordUpload(cmd, b.FacingWork, g.FacingWork, 0, 0, g.FacingWork.Length, 2);
                    if (g.TriangleWork.Length != g.Triangles.Length) RecordUpload(cmd, b.TriangleWork, g.TriangleWork, 0, 0, g.TriangleWork.Length, 2);
                    RecordUpload(cmd, b.NodeWork, g.NodeWork, 0, 0, g.NodeWork.Length, 2);
                }

                m_SubmittedWorkRevision = g.WorkRevision;
            }

            cmd.EndSample("PO2 mesh source updates");
            cmd.BeginSample("PO2 mesh derived records");
            Bind(cmd, m_UpdateCandidates, "_SourcePositions", b.SourcePositions);
            Bind(cmd, m_UpdateCandidates, "_CandidateSources", b.CandidateSources);
            Bind(cmd, m_UpdateCandidates, "_UpdateCandidates", b.Candidates);
            DispatchWork(cmd, m_UpdateCandidates, b.CandidateWork, g.CandidateWork.Length, g.Candidates.Length);
            if (NeedsOcclusion)
            {
                Bind(cmd, m_UpdateFacing, "_SourcePositions", b.SourcePositions);
                Bind(cmd, m_UpdateFacing, "_SourceNormals", b.SourceNormals);
                Bind(cmd, m_UpdateFacing, "_FacingSources", b.FacingSources);
                Bind(cmd, m_UpdateFacing, "_UpdateFacing", b.Normals);
                DispatchWork(cmd, m_UpdateFacing, b.FacingWork, g.FacingWork.Length, g.Normals.Length);
                Bind(cmd, m_UpdateTriangles, "_SourcePositions", b.SourcePositions);
                Bind(cmd, m_UpdateTriangles, "_TriangleSources", b.TriangleSources);
                Bind(cmd, m_UpdateTriangles, "_UpdateTriangles", b.Triangles);
                DispatchWork(cmd, m_UpdateTriangles, b.TriangleWork, g.TriangleWork.Length, g.Triangles.Length);
            }

            cmd.EndSample("PO2 mesh derived records");
            if (NeedsOcclusion && g.NodeWork.Length != 0)
            {
                cmd.BeginSample("PO2 mesh BVH refit");
                Bind(cmd, m_RefitNodes, "_RefitTriangles", b.Triangles);
                Bind(cmd, m_RefitNodes, "_UpdateNodes", b.Nodes);
                cmd.SetComputeIntParam(m_Compute, "_RefitTriangleCount", g.Triangles.Length);
                for (int level = g.LevelStarts.Length - 2; level >= 0; level--)
                {
                    int start = g.LevelStarts[level], count = g.LevelStarts[level + 1] - start;
                    DispatchWork(cmd, m_RefitNodes, b.NodeWork, count, g.Nodes.Length, start);
                }

                cmd.EndSample("PO2 mesh BVH refit");
            }

            g.Uploaded();
        }

        private void DispatchWork(CommandBuffer cmd, int kernel, ComputeBuffer work, int count, int limit, int offset = 0)
        {
            if (count == 0) return;
            Bind(cmd, kernel, "_UpdateWork", work);
            Bind(cmd, kernel, "_UpdateFault", m_Buffers.UpdateFault);
            cmd.SetComputeIntParam(m_Compute, "_UpdateCount", count);
            cmd.SetComputeIntParam(m_Compute, "_UpdateOffset", offset);
            cmd.SetComputeIntParam(m_Compute, "_UpdateLimit", limit);
            cmd.SetComputeIntParam(m_Compute, "_UpdateDense", kernel != m_RefitNodes && count == limit ? 1 : 0);
            cmd.DispatchCompute(m_Compute, kernel, (count + 63) / 64, 1, 1);
        }

        private void UploadSource(CommandBuffer cmd, bool normals)
        {
            var g = m_Geometry;
            var target = normals ? m_Buffers.SourceNormals : m_Buffers.SourcePositions;
            var lists = normals ? g.NormalUploads : g.PositionUploads;
            var ranges = normals ? g.NormalRanges : g.PositionRanges;
            int used = 0;
            Bind(cmd, m_Patch, "_SourceDestination", target);
            for (int p = 0; p < g.Parts.Length; p++)
            {
                var indices = lists[p];
                if (indices == null || indices.Length == 0) continue;
                var values = normals ? g.Parts[p].Mesh.Normals : g.Parts[p].Mesh.Vertices;
                int length = Math.Min(values?.Length ?? 0, g.Parts[p].Mesh.Vertices.Length);
                int start = g.SourceOffset(p);
                if (ranges != null && ranges[p] != null)
                {
                    var spans = ranges[p];
                    for (int r = 0; r < spans.Length; r += 2)
                        RecordUpload(cmd, target, values, spans[r], start + spans[r], spans[r + 1], normals ? 1 : 0);
                    continue;
                }

                // Bulk copies use the source stream directly, avoiding a second
                // full CPU packing pass. Sparse entries cost 16 bytes each.
                if (indices.Length * 16L >= length * 12L)
                {
                    RecordUpload(cmd, target, values, 0, start, length, normals ? 1 : 0);
                    continue;
                }

                for (int i = 0; i < indices.Length; i++)
                {
                    int index = indices[i];
                    if ((uint)index >= (uint)length) throw new InvalidOperationException("Mesh source update is out of range.");
                    m_PatchData[used++] = new SolidSourcePatch { Index = (uint)(start + index), Value = values[index] };
                    if (used == kPatchCapacity)
                    {
                        SubmitSourceChunk(cmd, used, normals);
                        used = 0;
                    }
                }
            }

            if (used != 0) SubmitSourceChunk(cmd, used, normals);
        }

        private void SubmitSourceChunk(CommandBuffer cmd, int count, bool normals)
        {
            RecordUpload(cmd, m_Buffers.Patches, m_PatchData, 0, 0, count, normals ? 1 : 0);
            cmd.SetComputeIntParam(m_Compute, "_SourcePatchCount", count);
            cmd.DispatchCompute(m_Compute, m_Patch, (count + 63) / 64, 1, 1);
        }

        private static void RecordUpload(CommandBuffer cmd, ComputeBuffer buffer, Array data, int source, int destination, int count, int kind)
        {
            if (count == 0) return;
            cmd.SetBufferData(buffer, data, source, destination, count);
        }
    }
}