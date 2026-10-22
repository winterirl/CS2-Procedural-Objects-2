using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using PO2.Core;
using PO2.Geometry.Topology;

namespace PO2.Tools.Snapping
{
    internal sealed class MeshTranslationSnapLayout
    {
        internal readonly VertexRef[] References;
        internal readonly int[] A, B;
        internal readonly MeshSnapTargetFilter TargetFilter;

        internal MeshTranslationSnapLayout(MeshTranslationSnapVertex[] vertices, MeshTranslationSnapCapture.Edge[] edges, CancellationToken token)
        {
            References = new VertexRef[vertices.Length];
            var slots = new Dictionary<VertexRef, int>(vertices.Length);
            int count = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                References[i] = vertices[i].Ref;
                if (vertices[i].Valid)
                {
                    slots[vertices[i].Ref] = i;
                    count++;
                }
            }

            int validEdges = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                if (edges[i].Valid && slots.ContainsKey(edges[i].A) && slots.ContainsKey(edges[i].B)) validEdges++;
            }

            A = new int[count + validEdges];
            B = new int[A.Length];
            int next = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                if (vertices[i].Valid) A[next] = B[next++] = i;
            }

            for (int i = 0; i < edges.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                if (!edges[i].Valid || !slots.TryGetValue(edges[i].A, out int a) || !slots.TryGetValue(edges[i].B, out int b)) continue;
                A[next] = a;
                B[next++] = b;
            }

            TargetFilter = new MeshSnapTargetFilter(References, token);
        }
    }

    // Coordinates are copied once per logical vertex. Edges carry references,
    // so a worker never rereads endpoints from geometry that is already moving.
    internal sealed class MeshTranslationSnapCapture : IDisposable
    {
        internal const int kInlineLimit = 256;
        internal const int kPrimitiveLimit = 1048576;
        internal const long kMemoryLimit = 128L * 1024 * 1024;

        internal struct Edge
        {
            internal VertexRef A, B;
            internal bool Valid;
        }

        internal sealed class Input
        {
            internal readonly MeshTranslationSnapVertex[] Vertices;
            internal readonly Edge[] Edges;
            internal readonly ObjectTransform Transform;
            internal MeshTranslationSnapLayout Layout { get; private set; }
            internal long EstimatedBytes => (long)Vertices.Length * 24 + (long)Edges.Length * 20 + (Layout == null ? 0 : (long)Layout.A.Length * 8 + (long)Layout.References.Length * 48);

            internal Input(MeshTranslationSnapVertex[] vertices, Edge[] edges, ObjectTransform transform, MeshTranslationSnapLayout layout = null)
            {
                Vertices = vertices;
                Edges = edges;
                Transform = transform;
                Layout = layout;
            }

            internal Input Translate(Vector3 delta, CancellationToken token)
            {
                var vertices = new MeshTranslationSnapVertex[Vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    vertices[i] = Vertices[i];
                    // Match the exact local addition used to write the mesh.
                    vertices[i].Position = Vertices[i].Position + delta;
                }

                return new Input(vertices, Edges, Transform, Layout);
            }

            internal TranslationSnapSourceData Build(CancellationToken token, TranslationSnapSourceData reuse = null, MeshTranslationSnapBudget.Reservation reservation = null)
            {
                token.ThrowIfCancellationRequested();
                if (Layout == null) Layout = new MeshTranslationSnapLayout(Vertices, Edges, token);
                if (Layout.References.Length != Vertices.Length) throw new InvalidOperationException("The snapping selection changed.");
                var world = new Vector3[Vertices.Length];
                for (int i = 0; i < Vertices.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    if (!Layout.References[i].Equals(Vertices[i].Ref)) throw new InvalidOperationException("The snapping selection changed.");
                    if (!Vertices[i].Valid) continue;
                    world[i] = Transform.TransformPoint(Vertices[i].Position);
                    CheckFinite(world[i]);
                }

                var primitives = new TranslationSnapSourceData.Primitive[Layout.A.Length];
                for (int i = 0; i < primitives.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    Vector3 a = world[Layout.A[i]], b = world[Layout.B[i]];
                    CheckFinite((a + b) * 0.5f);
                    primitives[i] = new TranslationSnapSourceData.Primitive(a, b);
                }

                return new TranslationSnapSourceData(primitives, primitives.Length, Layout.References, token, this, reuse, reservation);
            }

            private static void CheckFinite(Vector3 p)
            {
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z))
                    throw new InvalidOperationException("The mesh contains invalid snapping coordinates.");
            }
        }

        internal readonly BudgetedCopy Copy = new BudgetedCopy();
        internal readonly long EstimatedBytes;
        private readonly ObjectTransform m_Transform;
        private readonly BudgetedCopy.Stream<MeshTranslationSnapVertex> m_Vertices;
        private readonly BudgetedCopy.Stream<Edge> m_Edges;
        private readonly MeshTranslationSnapVertex[] m_Frozen;
        private readonly MeshTranslationSnapLayout m_Layout;
        private IEnumerator<VertexRef> m_VertexIterator;
        private IEnumerator<StructuralEdgeRef> m_EdgeIterator;

        internal static long EstimateBytes(int vertices, int edges)
        {
            long count = checked((long)vertices + edges);
            if (vertices <= 0 || edges < 0 || count > kPrimitiveLimit)
                throw new InvalidOperationException("The selection is too large to prepare snapping.");
            long leaves = 1;
            while (leaves * 8 < count) leaves *= 2;
            // Includes input, layout/map/filter, world points, build scratch,
            // output and room for translating a compact snapshot on a worker.
            long bytes = checked((long)vertices * 160 + (long)edges * 24 + count * 72 + (leaves * 2 - 1) * 40 + 65536);
            if (bytes > kMemoryLimit) throw new InvalidOperationException("Snapping preparation exceeds its memory limit.");
            return bytes;
        }

        internal MeshTranslationSnapCapture(ProceduralObject obj, int vertexCount, IEnumerable<VertexRef> vertices, int edgeCount, IEnumerable<StructuralEdgeRef> edges, MeshTranslationSnapVertex[] frozen = null, MeshTranslationSnapLayout layout = null)
        {
            EstimatedBytes = EstimateBytes(vertexCount, edgeCount);
            m_Transform = obj.Transform;
            m_Frozen = frozen;
            m_Layout = layout;
            try
            {
                if (frozen == null)
                {
                    m_VertexIterator = vertices.GetEnumerator();
                    m_Vertices = Copy.Add(vertexCount, 24, i =>
                    {
                        if (!m_VertexIterator.MoveNext()) throw new InvalidOperationException("The vertex selection changed.");
                        return ReadVertex(obj, m_VertexIterator.Current);
                    });
                }

                if (layout == null)
                {
                    m_EdgeIterator = edges.GetEnumerator();
                    m_Edges = Copy.Add(edgeCount, 20, i =>
                    {
                        if (!m_EdgeIterator.MoveNext()) throw new InvalidOperationException("The edge selection changed.");
                        return ReadEdge(obj, m_EdgeIterator.Current);
                    });
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Start(CancellationToken token) => Copy.Start(token);
        internal bool Poll(Func<bool> matches, CancellationToken token) => Copy.Poll(matches, token, 1024 * 1024);

        internal Input Detach()
        {
            if (!Copy.IsComplete) throw new InvalidOperationException("Snapping capture is not complete.");
            var input = new Input(m_Frozen ?? m_Vertices.Data, m_Edges?.Data ?? Array.Empty<Edge>(), m_Transform, m_Layout);
            Dispose();
            return input;
        }

        internal static Input CaptureSmall(ProceduralObject obj, int vertexCount, IEnumerable<VertexRef> vertices, int edgeCount, IEnumerable<StructuralEdgeRef> edges)
        {
            EstimateBytes(vertexCount, edgeCount);
            if ((long)vertexCount + edgeCount > kInlineLimit) throw new InvalidOperationException("Large snapping sources must be prepared in the background.");
            var v = new MeshTranslationSnapVertex[vertexCount];
            var e = new Edge[edgeCount];
            int index = 0;
            foreach (var item in vertices)
            {
                if (index == v.Length) throw new InvalidOperationException("The vertex selection changed.");
                v[index++] = ReadVertex(obj, item);
            }

            if (index != v.Length) throw new InvalidOperationException("The vertex selection changed.");
            index = 0;
            foreach (var item in edges)
            {
                if (index == e.Length) throw new InvalidOperationException("The edge selection changed.");
                e[index++] = ReadEdge(obj, item);
            }

            if (index != e.Length) throw new InvalidOperationException("The edge selection changed.");
            return new Input(v, e, obj.Transform);
        }

        internal static MeshTranslationSnapVertex ReadVertex(ProceduralObject obj, VertexRef item)
        {
            var result = new MeshTranslationSnapVertex { Ref = item };
            if ((uint)item.PartIndex >= (uint)obj.Parts.Count) return result;
            var part = obj.Parts[item.PartIndex];
            if ((uint)item.VertexId >= (uint)part.Topology.LogicalVertexCount) return result;
            result.Position = part.Topology.GetVertex(item.VertexId).GetPosition(part.Mesh);
            result.Valid = true;
            return result;
        }

        private static Edge ReadEdge(ProceduralObject obj, StructuralEdgeRef item)
        {
            if ((uint)item.PartIndex >= (uint)obj.Parts.Count) return default;
            var part = obj.Parts[item.PartIndex];
            if ((uint)item.EdgeId >= (uint)part.StructuralTopology.StructuralEdgeCount) return default;
            var edge = part.StructuralTopology.GetEdge(item.EdgeId);
            return new Edge
            {
                A = new VertexRef(item.PartIndex, edge.LogicalVertexA),
                B = new VertexRef(item.PartIndex, edge.LogicalVertexB), Valid = true
            };
        }

        public void Dispose()
        {
            m_VertexIterator?.Dispose();
            m_VertexIterator = null;
            m_EdgeIterator?.Dispose();
            m_EdgeIterator = null;
        }
    }
}