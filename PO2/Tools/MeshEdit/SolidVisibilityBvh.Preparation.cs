using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace PO2.Tools.MeshEdit
{
    internal sealed partial class SolidVisibilityBvh
    {
        internal bool HasSerial(int serial) => m_HasBuild && BuildSerial == serial;
        internal bool CanReuse(MeshEditGpuGeometry data) => data != null && data.GeometryRevision == 0 && data.OcclusionCurrent && m_HasBuild && BuildSerial == data.TreeSerial && ReferenceEquals(m_SourceObject, data.Object) && data.Matches(data.Object, data.Mode) && HasMatchingGeometry(data.Object);

        internal static void BuildDetached(MeshEditGpuSource source, MeshEditGpuGeometry data, CancellationToken token)
        {
            var triangles = new List<BuildTriangle>((int)source.TriangleCount);
            for (int p = 0; p < source.Parts.Length; p++)
            {
                var part = source.Parts[p];
                var vertices = part.Vertices;
                var indices = part.Triangles;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                    int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                    if ((uint)ia >= vertices.Length || (uint)ib >= vertices.Length || (uint)ic >= vertices.Length) continue;
                    MeshEditGpuGeometry.Finite(MeshEditGpuGeometry.Lane(vertices[ia]));
                    MeshEditGpuGeometry.Finite(MeshEditGpuGeometry.Lane(vertices[ib]));
                    MeshEditGpuGeometry.Finite(MeshEditGpuGeometry.Lane(vertices[ic]));
                    var a = ToFloat3(vertices[ia]);
                    var b = ToFloat3(vertices[ib]);
                    var c = ToFloat3(vertices[ic]);
                    triangles.Add(new BuildTriangle
                    {
                        Triangle = new SolidBvhTriangle { A = a, B = b, C = c },
                        Source = new SolidBvhTriangleSource { PartIndex = p, VertexA = ia, VertexB = ib, VertexC = ic },
                        Min = math.min(a, math.min(b, c)), Max = math.max(a, math.max(b, c)), Centroid = (a + b + c) / 3f,
                    });
                }
            }

            var sorted = triangles.ToArray();
            var nodes = new List<SolidBvhNode>(Math.Max(1, sorted.Length / 2));
            if (sorted.Length != 0) BuildNode(sorted, 0, sorted.Length, nodes, token);
            token.ThrowIfCancellationRequested();
            data.Nodes = new SolidNode[nodes.Count];
            data.Parents = new int[nodes.Count];
            data.Triangles = new SolidTriangle[sorted.Length];
            data.TriangleLeaves = new int[sorted.Length];
            data.TriangleSources = new SolidBvhTriangleSource[sorted.Length];
            data.OrthoLength = CalculateOrthographicRayLength(nodes);
            if (nodes.Count != 0) ExportNode(0, nodes.Count, -1, nodes, data, token);
            for (int i = 0; i < sorted.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                var t = sorted[i].Triangle;
                data.Triangles[i] = new SolidTriangle { A = Lane(t.A), B = Lane(t.B), C = Lane(t.C) };
                data.TriangleSources[i] = sorted[i].Source;
            }
        }

        private static Vector4 Lane(float3 v) => new Vector4(v.x, v.y, v.z, 0);
        private static float3 Point(Vector4 v) => new float3(v.x, v.y, v.z);

        private static void ExportNode(int index, int escape, int parent, List<SolidBvhNode> nodes, MeshEditGpuGeometry data, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var n = nodes[index];
            data.Parents[index] = parent;
            data.Nodes[index] = new SolidNode
            {
                Min = Lane(n.Min), Max = Lane(n.Max), FirstTriangle = n.FirstTriangle,
                TriangleCount = n.TriangleCount, Child = n.LeftChild, Escape = escape
            };
            MeshEditGpuGeometry.Finite(data.Nodes[index].Min);
            MeshEditGpuGeometry.Finite(data.Nodes[index].Max);
            if (n.TriangleCount > 0)
            {
                for (int i = n.FirstTriangle; i < n.FirstTriangle + n.TriangleCount; i++) data.TriangleLeaves[i] = index;
            }
            else
            {
                ExportNode(n.LeftChild, n.RightChild, index, nodes, data, token);
                ExportNode(n.RightChild, escape, index, nodes, data, token);
            }
        }

        // Private native arrays become visible to CPU queries/refits only at Commit.
        // One allocation or <=1024 records per step; caller shares a frame budget
        // with GPU publication. No job or readback can see an incomplete tree.
        internal sealed class Publication : IDisposable
        {
            private NativeArray<SolidBvhNode> m_Nodes;
            private NativeArray<SolidBvhTriangle> m_Triangles;
            private NativeArray<SolidBvhTriangleSource> m_Sources;
            private int m_Stage, m_Cursor;

            internal bool Step(MeshEditGpuGeometry data)
            {
                switch (m_Stage)
                {
                    case 0:
                        m_Nodes = new NativeArray<SolidBvhNode>(data.Nodes.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        m_Stage++;
                        return false;
                    case 1:
                        m_Triangles = new NativeArray<SolidBvhTriangle>(data.Triangles.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        m_Stage++;
                        return false;
                    case 2:
                        m_Sources = new NativeArray<SolidBvhTriangleSource>(data.Triangles.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        m_Stage++;
                        return false;
                    case 3:
                        int end = Math.Min(data.Nodes.Length, m_Cursor + 1024);
                        for (; m_Cursor < end; m_Cursor++)
                        {
                            var n = data.Nodes[m_Cursor];
                            m_Nodes[m_Cursor] = new SolidBvhNode
                            {
                                Min = Point(n.Min), Max = Point(n.Max), LeftChild = n.Child,
                                RightChild = n.Child < 0 ? -1 : data.Nodes[n.Child].Escape,
                                FirstTriangle = n.FirstTriangle, TriangleCount = n.TriangleCount
                            };
                        }

                        if (m_Cursor == data.Nodes.Length)
                        {
                            m_Cursor = 0;
                            m_Stage++;
                        }

                        return false;
                    case 4:
                        end = Math.Min(data.Triangles.Length, m_Cursor + 1024);
                        for (; m_Cursor < end; m_Cursor++)
                        {
                            var t = data.Triangles[m_Cursor];
                            m_Triangles[m_Cursor] = new SolidBvhTriangle { A = Point(t.A), B = Point(t.B), C = Point(t.C) };
                            m_Sources[m_Cursor] = data.TriangleSources[m_Cursor];
                        }

                        if (m_Cursor == data.Triangles.Length) m_Stage++;
                        return m_Stage == 5;
                    default: return true;
                }
            }

            internal void Commit(SolidVisibilityBvh bvh, MeshEditGpuGeometry data)
            {
                if (m_Stage != 5) throw new InvalidOperationException("Incomplete Solid tree publication.");
                bvh.DisposeBuildBuffers();
                bvh.m_Nodes = m_Nodes;
                bvh.m_Triangles = m_Triangles;
                bvh.m_TriangleSources = m_Sources;
                m_Nodes = default;
                m_Triangles = default;
                m_Sources = default;
                data.TreeSerial = ++bvh.BuildSerial;
                bvh.m_ObjectId = data.Object.Id;
                bvh.m_GeometryVersion = data.Object.GeometryVersion;
                bvh.CaptureTopologyVersions(data.Object);
                bvh.m_OrthographicRayLength = data.OrthoLength;
                bvh.m_HasBuild = true;
            }

            public void Dispose()
            {
                if (m_Nodes.IsCreated) m_Nodes.Dispose();
                if (m_Triangles.IsCreated) m_Triangles.Dispose();
                if (m_Sources.IsCreated) m_Sources.Dispose();
            }
        }
    }
}