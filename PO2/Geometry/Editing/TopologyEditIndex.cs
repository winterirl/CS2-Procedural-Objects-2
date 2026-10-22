using System;
using System.Collections.Generic;
using System.Threading;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Persistence.Model;

namespace PO2.Geometry.Editing
{
    // One immutable index per captured part, reused as the selection changes.
    // Logical IDs describe connectivity; render IDs retain the original streams.
    internal sealed class TopologyEditIndex
    {
        internal sealed class EdgeComparer : IEqualityComparer<ulong>
        {
            internal static readonly EdgeComparer s_Instance = new EdgeComparer();

            public bool Equals(ulong a, ulong b) => a == b;

            // Match the existing topology edge hash. UInt64's default XOR hash
            // collapses sequential neighboring IDs into a few long buckets.
            public int GetHashCode(ulong key)
            {
                unchecked
                {
                    return ((int)(key >> 32) * 397) ^ (int)key;
                }
            }
        }

        internal struct Edge
        {
            internal int Count, Direction, FirstTriangle, SecondTriangle;

            internal void Add(int a, int b, int triangle)
            {
                if (Count == 0) FirstTriangle = triangle;
                else if (Count == 1) SecondTriangle = triangle;
                Count++;
                Direction += a < b ? 1 : -1;
            }
        }

        internal readonly PersistencePartData Source;
        internal readonly MeshData Streams;
        internal readonly int[] Offsets, IncidentTriangles, RenderHeads, NextRender;
        internal readonly StructuralFaceTriangles[] FaceTriangles;
        internal readonly int[] TriangleFaces;
        internal readonly Dictionary<ulong, Edge> Edges;

        internal TopologyEditIndex(PersistencePartData source, CancellationToken token)
        {
            Source = source;
            Streams = new MeshData(source.Vertices, source.Normals, source.Tangents, source.UV0, source.VertexColors, source.TexCoord1, source.TexCoord3, source.Triangles);
            int count = source.LogicalVertexCount;
            if (source.RenderToLogical.Length != source.Vertices.Length || source.Triangles.Length % 3 != 0 || source.TriangleSurfaceAreaIds.Length != source.Triangles.Length / 3)
                throw new InvalidOperationException("The mesh layout is inconsistent.");
            Offsets = new int[count + 1];
            RenderHeads = new int[count];
            NextRender = new int[source.Vertices.Length];
            for (int i = 0; i < count; i++) RenderHeads[i] = -1;
            for (int i = source.Vertices.Length - 1; i >= 0; i--)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                int id = source.RenderToLogical[i];
                if (id < 0 || id >= count) throw new InvalidOperationException("Invalid logical vertex mapping.");
                NextRender[i] = RenderHeads[id];
                RenderHeads[id] = i;
            }

            Edges = new Dictionary<ulong, Edge>(source.Triangles.Length, EdgeComparer.s_Instance);
            for (int t = 0; t < source.Triangles.Length / 3; t++)
            {
                if ((t & 4095) == 0) token.ThrowIfCancellationRequested();
                for (int c = 0; c < 3; c++)
                {
                    int render = source.Triangles[t * 3 + c];
                    if (render < 0 || render >= NextRender.Length) throw new InvalidOperationException("Invalid triangle index.");
                    Offsets[source.RenderToLogical[render] + 1]++;
                }

                for (int c = 0; c < 3; c++)
                {
                    int a = Logical(t, c), b = Logical(t, (c + 1) % 3);
                    ulong key = Key(a, b);
                    Edges.TryGetValue(key, out var edge);
                    edge.Add(a, b, t);
                    Edges[key] = edge;
                }
            }

            for (int i = 1; i < Offsets.Length; i++) Offsets[i] += Offsets[i - 1];
            IncidentTriangles = new int[source.Triangles.Length];
            var cursor = (int[])Offsets.Clone();
            for (int t = 0; t < source.Triangles.Length / 3; t++)
            {
                if ((t & 4095) == 0) token.ThrowIfCancellationRequested();
                for (int c = 0; c < 3; c++) IncidentTriangles[cursor[Logical(t, c)]++] = t;
            }

            token.ThrowIfCancellationRequested();
            StructuralFaceTriangleResolver.Build(source.StructuralFaces, source.Triangles, out FaceTriangles, out TriangleFaces);
            token.ThrowIfCancellationRequested();
        }

        internal int Logical(int triangle, int corner) => Source.RenderToLogical[Source.Triangles[triangle * 3 + corner]];
        internal static ulong Key(int a, int b) => ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
    }
}