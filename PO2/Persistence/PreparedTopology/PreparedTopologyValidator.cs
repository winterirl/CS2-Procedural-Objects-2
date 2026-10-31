using System;
using System.Collections.Generic;
using PO2.Geometry.Topology;
using PO2.Persistence.Model;

namespace PO2.Persistence.PreparedTopology
{
    internal static class PreparedTopologyValidator
    {
        private static void Require(bool condition) => PreparedTopologyCodec.Require(condition, "Prepared topology does not match its saved geometry.");

        internal static ValidatedPartTopology Validate(EncodedPartTopology encoded, PersistencePartData source)
        {
            var data = PreparedTopologyCodec.Decode(encoded, source);
            if (encoded.Version == PreparedTopologyLayout.FullVersion)
            {
                ValidateLogical(data, source);
                ValidateStructural(data, source);
            }
            bool canonical = ValidateMaps(data, source);
            return new ValidatedPartTopology(source, data, canonical);
        }

        private static void Offsets(int[] offsets, int rows, int length)
        {
            Require(offsets.Length == rows + 1 && offsets[0] == 0 && offsets[rows] == length);
            for (int i = 0; i < rows; i++) Require(offsets[i] <= offsets[i + 1]);
        }

        private static void Consume(int[] offsets, int[] values, int[] cursors, int row, int value)
        {
            int at = cursors[row];
            Require(at < offsets[row + 1] - offsets[row] && values[offsets[row] + at] == value);
            cursors[row] = at + 1;
        }

        private static void Finished(int[] offsets, int[] cursors)
        {
            for (int i = 0; i < cursors.Length; i++) Require(cursors[i] == offsets[i + 1] - offsets[i]);
        }

        private static void Edges(int[] a, int[] b, int vertices)
        {
            for (int i = 0; i < a.Length; i++)
            {
                Require(a[i] >= 0 && a[i] < b[i] && b[i] < vertices);
                if (i != 0) Require(a[i] > a[i - 1] || (a[i] == a[i - 1] && b[i] > b[i - 1]));
            }
        }

        private static void ValidateLogical(PackedTopologyData d, PersistencePartData p)
        {
            int l = d.LogicalCount, e = d.EdgeCount;
            Require(p.RenderToLogical.Length == d.RenderCount && p.Triangles.Length % 3 == 0);
            Offsets(d[1], l, d[2].Length);
            Offsets(d[3], l, d[4].Length);
            Offsets(d[5], l, d[6].Length);
            Offsets(d[9], e, d[10].Length);
            var cursors = new int[l];
            for (int r = 0; r < d.RenderCount; r++)
            {
                int v = p.RenderToLogical[r];
                Require(v >= 0 && v < l);
                Consume(d[1], d[2], cursors, v, r);
            }
            Finished(d[1], cursors);
            for (int i = 0; i < l; i++) Require(cursors[i] != 0);
            Array.Clear(cursors, 0, l);
            long edgeIncidences = 0;
            for (int t = 0; t < d.TriangleCount; t++)
            {
                int at = t * 3;
                for (int k = 0; k < 3; k++) Require(p.Triangles[at + k] >= 0 && p.Triangles[at + k] < d.RenderCount);
                int a = p.RenderToLogical[p.Triangles[at]], b = p.RenderToLogical[p.Triangles[at + 1]], c = p.RenderToLogical[p.Triangles[at + 2]];
                Consume(d[3], d[4], cursors, a, t);
                if (b != a) Consume(d[3], d[4], cursors, b, t);
                if (c != a && c != b) Consume(d[3], d[4], cursors, c, t);
                edgeIncidences += a == b && b == c ? 0 : a == b || b == c || a == c ? 1 : 3;
            }
            Finished(d[3], cursors);
            Require(d[10].LongLength == edgeIncidences);
            Edges(d[7], d[8], l);
            Array.Clear(cursors, 0, l);
            for (int i = 0; i < e; i++)
            {
                int a = d[7][i], b = d[8][i], previous = -1;
                Require(d[9][i] < d[9][i + 1]);
                for (int j = d[9][i]; j < d[9][i + 1]; j++)
                {
                    int t = d[10][j];
                    Require(t > previous && t < d.TriangleCount);
                    previous = t;
                    int at = t * 3;
                    int x = p.RenderToLogical[p.Triangles[at]], y = p.RenderToLogical[p.Triangles[at + 1]], z = p.RenderToLogical[p.Triangles[at + 2]];
                    Require((a == x || a == y || a == z) && (b == x || b == y || b == z));
                }
                Consume(d[5], d[6], cursors, a, b);
                Consume(d[5], d[6], cursors, b, a);
            }
            Finished(d[5], cursors);
        }

        private static void ValidateStructural(PackedTopologyData d, PersistencePartData p)
        {
            if (d.FaceCount == 0) return;
            int s = d.StructuralEdgeCount;
            Offsets(d[13], s, d[14].Length);
            Offsets(d[15], d.FaceCount, d[16].Length);
            Offsets(d[18], d.LogicalCount, d[19].Length);
            Edges(d[11], d[12], d.LogicalCount);
            var cursors = new int[s];
            var seen = new int[s];
            for (int f = 0; f < d.FaceCount; f++)
            {
                var face = p.StructuralFaces[f];
                Require(face.Id == f && (face.IsTriangle || face.IsQuad));
                Require(d[15][f + 1] - d[15][f] == face.CornerCount);
                for (int k = 0; k < face.CornerCount; k++)
                {
                    int r = face.GetRenderVertexIndex(k), next = face.GetRenderVertexIndex((k + 1) % face.CornerCount);
                    Require(r >= 0 && r < d.RenderCount && next >= 0 && next < d.RenderCount);
                    int a = p.RenderToLogical[r], b = p.RenderToLogical[next];
                    int at = d[15][f] + k, edge = d[16][at];
                    Require(edge >= 0 && edge < s && a != b);
                    Require(d[11][edge] == Math.Min(a, b) && d[12][edge] == Math.Max(a, b));
                    Require(d.Forward[at] == (a < b ? 1 : 0));
                    if (seen[edge] != f + 1)
                    {
                        Consume(d[13], d[14], cursors, edge, f);
                        seen[edge] = f + 1;
                    }
                }
            }
            Finished(d[13], cursors);
            for (int i = 0; i < s; i++) Require(cursors[i] != 0);
            cursors = new int[d.LogicalCount];
            for (int i = 0; i < s; i++)
            {
                Consume(d[18], d[19], cursors, d[11][i], i);
                Consume(d[18], d[19], cursors, d[12][i], i);
            }
            Finished(d[18], cursors);
        }

        private static bool ValidateMaps(PackedTopologyData d, PersistencePartData p)
        {
            // First prove the forward and reverse relationships. A noncanonical
            // duplicate choice can reuse adjacency but must rebuild these maps.
            long assigned = 0;
            bool sequential = true;
            int next = 0;
            for (int f = 0; f < d.FaceCount; f++)
            {
                var face = p.StructuralFaces[f];
                ValidateSlot(d, p, f, d[20][f], new TriangleKey(face.GetRenderVertexIndex(0), face.GetRenderVertexIndex(1), face.GetRenderVertexIndex(2)));
                assigned++;
                sequential &= d[20][f] == next++;
                if (face.IsQuad)
                {
                    Require(d[20][f] != d[21][f]);
                    ValidateSlot(d, p, f, d[21][f], new TriangleKey(face.GetRenderVertexIndex(0), face.GetRenderVertexIndex(2), face.GetRenderVertexIndex(3)));
                    assigned++;
                    sequential &= d[21][f] == next++;
                }
                else Require(d[21][f] == -1);
            }
            long reverseCount = 0;
            for (int t = 0; t < d.TriangleCount; t++)
            {
                int f = d[22][t];
                Require(f >= -1 && f < d.FaceCount);
                if (f == -1) continue;
                Require(d[20][f] == t || d[21][f] == t);
                reverseCount++;
            }
            Require(reverseCount == assigned);
            if (d.FaceCount == 0 || (sequential && next == d.TriangleCount)) return true;

            var states = new Dictionary<TriangleKey, MapOrder>();
            for (int f = 0; f < d.FaceCount; f++)
            {
                states[KeyAt(p.Triangles, d[20][f])] = default;
                if (d[21][f] >= 0) states[KeyAt(p.Triangles, d[21][f])] = default;
            }
            for (int t = 0; t < d.TriangleCount; t++)
            {
                var key = KeyAt(p.Triangles, t);
                if (!states.TryGetValue(key, out var state)) continue;
                int f = d[22][t];
                if (f == -1) state.Unassigned = true;
                else
                {
                    long ordinal = 2L * f + (d[20][f] == t ? 1 : 2);
                    if (state.Unassigned || ordinal <= state.LastOrdinal) return false;
                    state.LastOrdinal = ordinal;
                }
                states[key] = state;
            }
            return true;
        }

        private static void ValidateSlot(PackedTopologyData d, PersistencePartData p, int face, int triangle, TriangleKey key)
        {
            Require(triangle >= 0 && triangle < d.TriangleCount);
            Require(d[22][triangle] == face && key.Equals(KeyAt(p.Triangles, triangle)));
        }

        private struct MapOrder { internal long LastOrdinal; internal bool Unassigned; }
        private static TriangleKey KeyAt(int[] triangles, int id) => new TriangleKey(triangles[id * 3], triangles[id * 3 + 1], triangles[id * 3 + 2]);
        private readonly struct TriangleKey : IEquatable<TriangleKey>
        {
            private readonly int a, b, c;
            internal TriangleKey(int x, int y, int z)
            {
                if (x <= y && x <= z) { a = x; b = y; c = z; }
                else if (y <= z) { a = y; b = z; c = x; }
                else { a = z; b = x; c = y; }
            }
            public bool Equals(TriangleKey other) => a == other.a && b == other.b && c == other.c;
            public override bool Equals(object obj) => obj is TriangleKey other && Equals(other);
            public override int GetHashCode() { unchecked { return (a * 397 ^ b) * 397 ^ c; } }
        }
    }
}
