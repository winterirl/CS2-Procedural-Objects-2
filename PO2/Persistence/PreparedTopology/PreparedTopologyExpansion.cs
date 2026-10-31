using System;
using PO2.Geometry.Topology;
using PO2.Persistence.Model;

namespace PO2.Persistence.PreparedTopology
{
    internal static class PreparedTopologyExpansion
    {
        private static void Require(bool condition) => PreparedTopologyCodec.Require(condition, "Compact topology does not match its saved geometry.");

        internal static void ExpandAndValidate(PackedTopologyData data, PersistencePartData source, EncodedTopologySection[] sections)
        {
            int[] cursors = ExpandLogical(data, source, sections);
            ExpandStructural(data, source, sections, cursors);
            ExpandReverseMap(data, source);
            for (int id = 1; id <= PreparedTopologyCodec.SectionCount; id++)
                Require((id == 17 ? data.Forward.Length : data[id].Length) == sections[id - 1].Count);
        }

        // Counts become row starts. The final cell is the total, not another row.
        private static void Prefix(int[] counts)
        {
            int total = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                int count = counts[i];
                counts[i] = total;
                total = checked(total + count);
            }
        }

        private static void CheckOffsets(int[] offsets, int rows, int length)
        {
            Require(offsets.Length == rows + 1 && offsets[0] == 0 && offsets[rows] == length);
            for (int i = 0; i < rows; i++) Require(offsets[i] <= offsets[i + 1]);
        }

        private static int[] ExpandLogical(PackedTopologyData d, PersistencePartData p, EncodedTopologySection[] sections)
        {
            int l = d.LogicalCount, e = d.EdgeCount;
            int[] mapping = p.RenderToLogical, triangles = p.Triangles;
            Require(mapping.Length == d.RenderCount && triangles.Length % 3 == 0);
            d[1] = new int[checked(l + 1)];
            d[3] = new int[checked(l + 1)];
            d[5] = new int[checked(l + 1)];
            for (int r = 0; r < mapping.Length; r++)
            {
                int v = mapping[r];
                Require(v >= 0 && v < l);
                d[1][v] = checked(d[1][v] + 1);
            }
            for (int v = 0; v < l; v++) Require(d[1][v] != 0);
            Prefix(d[1]);
            d[2] = new int[mapping.Length];
            var cursors = (int[])d[1].Clone();
            for (int r = 0; r < mapping.Length; r++) d[2][cursors[mapping[r]]++] = r;

            long edgeIncidences = 0;
            for (int t = 0; t < d.TriangleCount; t++)
            {
                int at = t * 3, x = triangles[at], y = triangles[at + 1], z = triangles[at + 2];
                Require(x >= 0 && x < mapping.Length && y >= 0 && y < mapping.Length && z >= 0 && z < mapping.Length);
                int a = mapping[x], b = mapping[y], c = mapping[z];
                d[3][a] = checked(d[3][a] + 1);
                if (b != a) d[3][b] = checked(d[3][b] + 1);
                if (c != a && c != b) d[3][c] = checked(d[3][c] + 1);
                edgeIncidences += a == b && b == c ? 0 : a == b || a == c || b == c ? 1 : 3;
            }
            Prefix(d[3]);
            Require(d[3][l] == sections[3].Count);
            d[4] = new int[d[3][l]];
            Array.Copy(d[3], cursors, cursors.Length);
            for (int t = 0; t < d.TriangleCount; t++)
            {
                int at = t * 3, a = mapping[triangles[at]], b = mapping[triangles[at + 1]], c = mapping[triangles[at + 2]];
                d[4][cursors[a]++] = t;
                if (b != a) d[4][cursors[b]++] = t;
                if (c != a && c != b) d[4][cursors[c]++] = t;
            }

            CheckOffsets(d[9], e, d[10].Length);
            Require(d[10].LongLength == edgeIncidences);
            for (int i = 0; i < e; i++)
            {
                int a = d[7][i], b = d[8][i];
                Require(a >= 0 && a < b && b < l);
                if (i != 0) Require(a > d[7][i - 1] || (a == d[7][i - 1] && b > d[8][i - 1]));
                Require(d[9][i] < d[9][i + 1]);
                int previous = -1;
                for (int j = d[9][i]; j < d[9][i + 1]; j++)
                {
                    int t = d[10][j];
                    Require(t > previous && t < d.TriangleCount);
                    previous = t;
                    int at = t * 3, x = mapping[triangles[at]], y = mapping[triangles[at + 1]], z = mapping[triangles[at + 2]];
                    Require((a == x || a == y || a == z) && (b == x || b == y || b == z));
                }
                d[5][a] = checked(d[5][a] + 1);
                d[5][b] = checked(d[5][b] + 1);
            }
            // Unique edges and per-edge triangle IDs, valid memberships, and the
            // exact incidence total prove that no required membership was dropped.
            Prefix(d[5]);
            Require(d[5][l] == sections[5].Count);
            d[6] = new int[d[5][l]];
            Array.Copy(d[5], cursors, cursors.Length);
            for (int i = 0; i < e; i++)
            {
                int a = d[7][i], b = d[8][i];
                d[6][cursors[a]++] = b;
                d[6][cursors[b]++] = a;
            }
            return cursors;
        }

        private static void ExpandStructural(PackedTopologyData d, PersistencePartData p, EncodedTopologySection[] sections, int[] cursors)
        {
            int f = d.FaceCount, s = d.StructuralEdgeCount, l = d.LogicalCount;
            if (f == 0)
            {
                Require(s == 0);
                d[11] = d[12] = d[13] = d[14] = d[15] = d[18] = d[19] = Array.Empty<int>();
                d.Forward = Array.Empty<byte>();
                return;
            }
            d[11] = new int[s];
            d[12] = new int[s];
            d[13] = new int[checked(s + 1)];
            d[15] = new int[checked(f + 1)];
            d.Forward = new byte[d[16].Length];
            var seen = new int[s];
            int at = 0;
            for (int faceId = 0; faceId < f; faceId++)
            {
                var face = p.StructuralFaces[faceId];
                Require(face.Id == faceId && (face.IsTriangle || face.IsQuad));
                Require(face.CornerCount <= d[16].Length - at);
                for (int k = 0; k < face.CornerCount; k++, at++)
                {
                    int x = face.GetRenderVertexIndex(k), y = face.GetRenderVertexIndex((k + 1) % face.CornerCount);
                    Require(x >= 0 && x < d.RenderCount && y >= 0 && y < d.RenderCount);
                    int a = p.RenderToLogical[x], b = p.RenderToLogical[y], edge = d[16][at];
                    Require(a != b && edge >= 0 && edge < s);
                    int low = Math.Min(a, b), high = Math.Max(a, b);
                    if (seen[edge] == 0) { d[11][edge] = low; d[12][edge] = high; }
                    else Require(d[11][edge] == low && d[12][edge] == high);
                    d.Forward[at] = a < b ? (byte)1 : (byte)0;
                    if (seen[edge] != faceId + 1)
                    {
                        d[13][edge] = checked(d[13][edge] + 1);
                        seen[edge] = faceId + 1;
                    }
                }
                d[15][faceId + 1] = at;
            }
            Require(at == d[16].Length);
            d[18] = new int[checked(l + 1)];
            for (int i = 0; i < s; i++)
            {
                Require(seen[i] != 0);
                int a = d[11][i], b = d[12][i];
                if (i != 0) Require(a > d[11][i - 1] || (a == d[11][i - 1] && b > d[12][i - 1]));
                d[18][a] = checked(d[18][a] + 1);
                d[18][b] = checked(d[18][b] + 1);
            }
            Prefix(d[13]);
            Require(d[13][s] == sections[13].Count);
            d[14] = new int[d[13][s]];
            var edgeCursors = (int[])d[13].Clone();
            Array.Clear(seen, 0, seen.Length);
            for (int faceId = 0; faceId < f; faceId++)
                for (int k = d[15][faceId]; k < d[15][faceId + 1]; k++)
                {
                    int edge = d[16][k];
                    if (seen[edge] != faceId + 1)
                    {
                        d[14][edgeCursors[edge]++] = faceId;
                        seen[edge] = faceId + 1;
                    }
                }
            Prefix(d[18]);
            Require(d[18][l] == sections[18].Count);
            d[19] = new int[d[18][l]];
            Array.Copy(d[18], cursors, cursors.Length);
            for (int i = 0; i < s; i++)
            {
                d[19][cursors[d[11][i]]++] = i;
                d[19][cursors[d[12][i]]++] = i;
            }
        }

        private static void ExpandReverseMap(PackedTopologyData d, PersistencePartData p)
        {
            d[22] = new int[d.TriangleCount];
            for (int t = 0; t < d.TriangleCount; t++) d[22][t] = -1;
            for (int f = 0; f < d.FaceCount; f++)
            {
                Assign(d[22], d[20][f], f);
                if (p.StructuralFaces[f].IsQuad) Assign(d[22], d[21][f], f);
                else Require(d[21][f] == -1);
            }
        }

        private static void Assign(int[] reverse, int triangle, int face)
        {
            Require(triangle >= 0 && triangle < reverse.Length && reverse[triangle] == -1);
            reverse[triangle] = face;
        }
    }
}
