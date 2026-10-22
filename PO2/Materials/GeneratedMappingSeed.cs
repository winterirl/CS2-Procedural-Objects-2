using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PO2.Geometry;
using PO2.Persistence.Format;

namespace PO2.Materials
{
    // Raw chart coordinates, before material rotation, offset and tile size.
    // Immutable seeds can be shared by array copies. UV0 remains the original
    // mesh payload, including the extra channels used by converted materials.
    internal sealed class GeneratedMappingSeed
    {
        internal readonly int[] Corners, Charts;
        internal readonly Vector2[] Coordinates, ReferenceMetric;
        internal long Bytes => 16L + Corners.Length * 12L + Charts.Length * 4L + ReferenceMetric.Length * 8L;

        internal GeneratedMappingSeed(int[] corners, int[] charts, Vector2[] coordinates, Vector2[] metric)
        {
            if (corners == null || charts == null || coordinates == null || metric == null || charts.Length == 0 || corners.Length != checked(charts.Length * 3) || coordinates.Length != corners.Length || metric.Length == 0 || metric.Length > charts.Length) throw new InvalidDataException("Invalid mapping seed layout.");
            for (int i = 0; i < corners.Length; i++)
                if (corners[i] < 0 || corners[i] >= SnapshotFormat.kMaxRenderVerticesPerPart || !Finite(coordinates[i].x) || !Finite(coordinates[i].y))
                    throw new InvalidDataException("Invalid mapping seed coordinate.");
            for (int i = 0; i < charts.Length; i++)
                if (charts[i] < 0 || charts[i] >= metric.Length)
                    throw new InvalidDataException("Invalid mapping seed chart.");
            for (int i = 0; i < metric.Length; i++)
                if (!Finite(metric[i].x) || !Finite(metric[i].y) || metric[i].x <= 0 || metric[i].y <= 0)
                    throw new InvalidDataException("Invalid mapping seed scale.");
            Corners = corners;
            Charts = charts;
            Coordinates = coordinates;
            ReferenceMetric = metric;
        }

        internal SeededMapping Bind(MeshData mesh, int[] sourceIndices)
        {
            if (sourceIndices == null || mesh.Triangles.Length != Corners.Length) return null;
            var coordinates = new Vector2[Corners.Length];
            var charts = new int[Charts.Length];
            bool ordered = true;
            for (int i = 0; i < Corners.Length; i += 3)
            {
                if (!MapTriangle(mesh, sourceIndices, i, i, coordinates))
                {
                    ordered = false;
                    break;
                }

                charts[i / 3] = Charts[i / 3];
            }

            if (!ordered)
            {
                // Mirroring can exchange the two triangles of a quad as well as
                // reverse their winding. Match connectivity, not triangle order.
                var lookup = new Dictionary<TriangleKey, List<int>>();
                for (int i = 0; i < Corners.Length; i += 3)
                {
                    var key = new TriangleKey(Corners[i], Corners[i + 1], Corners[i + 2]);
                    if (!lookup.TryGetValue(key, out var matches)) lookup.Add(key, matches = new List<int>());
                    matches.Add(i);
                }

                for (int i = 0; i < Corners.Length; i += 3)
                {
                    var key = new TriangleKey(sourceIndices[mesh.Triangles[i]], sourceIndices[mesh.Triangles[i + 1]], sourceIndices[mesh.Triangles[i + 2]]);
                    if (!lookup.TryGetValue(key, out var matches) || matches.Count == 0) return null;
                    int at = matches[matches.Count - 1];
                    matches.RemoveAt(matches.Count - 1);
                    if (!MapTriangle(mesh, sourceIndices, i, at, coordinates)) return null;
                    charts[i / 3] = Charts[at / 3];
                }
            }

            return new SeededMapping(this, mesh, coordinates, charts);
        }

        private bool MapTriangle(MeshData mesh, int[] sourceIndices, int target, int source, Vector2[] coordinates)
        {
            int used = 0;
            for (int c = 0; c < 3; c++)
            {
                int vertex = sourceIndices[mesh.Triangles[target + c]], match = -1;
                for (int k = 0; k < 3; k++)
                    if ((used & (1 << k)) == 0 && Corners[source + k] == vertex)
                    {
                        match = k;
                        break;
                    }

                if (match < 0) return false;
                used |= 1 << match;
                coordinates[target + c] = Coordinates[source + match];
            }

            return true;
        }

        private readonly struct TriangleKey : IEquatable<TriangleKey>
        {
            private readonly int m_A, m_B, m_C;

            internal TriangleKey(int a, int b, int c)
            {
                if (a > b)
                {
                    int t = a;
                    a = b;
                    b = t;
                }

                if (b > c)
                {
                    int t = b;
                    b = c;
                    c = t;
                }

                if (a > b)
                {
                    int t = a;
                    a = b;
                    b = t;
                }

                m_A = a;
                m_B = b;
                m_C = c;
            }

            public bool Equals(TriangleKey other) => m_A == other.m_A && m_B == other.m_B && m_C == other.m_C;
            public override bool Equals(object other) => other is TriangleKey key && Equals(key);
            public override int GetHashCode() => unchecked((m_A * 397 ^ m_B) * 397 ^ m_C);
        }

        internal static Vector2[] Measure(MeshData mesh, Vector3 scale, int[] charts, Vector2[] corners, int count)
        {
            var weight = new double[count];
            var u = new double[count];
            var v = new double[count];
            for (int i = 0; i < mesh.Triangles.Length; i += 3)
            {
                Vector2 a = corners[i], b = corners[i + 1], c = corners[i + 2];
                double determinant = (double)(b.x - a.x) * (c.y - a.y) - (double)(c.x - a.x) * (b.y - a.y);
                if (Math.Abs(determinant) < 1e-16) continue;
                Vector3 p = mesh.Vertices[mesh.Triangles[i]];
                Vector3 e1 = Vector3.Scale(mesh.Vertices[mesh.Triangles[i + 1]] - p, scale);
                Vector3 e2 = Vector3.Scale(mesh.Vertices[mesh.Triangles[i + 2]] - p, scale);
                Vector3 alongU = (e1 * (c.y - a.y) - e2 * (b.y - a.y)) / (float)determinant;
                Vector3 alongV = (e2 * (b.x - a.x) - e1 * (c.x - a.x)) / (float)determinant;
                float lengthU = alongU.magnitude;
                if (lengthU <= 0) continue;
                float lengthV = Vector3.Cross(alongU, alongV).magnitude / lengthU;
                if (!Finite(lengthU) || !Finite(lengthV) || lengthV <= 0) continue;
                int chart = charts[i / 3];
                double w = Math.Abs(determinant);
                weight[chart] += w;
                u[chart] += lengthU * w;
                v[chart] += lengthV * w;
            }

            var result = new Vector2[count];
            for (int i = 0; i < count; i++) result[i] = weight[i] > 0 ? new Vector2((float)(u[i] / weight[i]), (float)(v[i] / weight[i])) : Vector2.one;
            return result;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal sealed class SeededMapping
    {
        internal readonly GeneratedMappingSeed Seed;
        internal readonly Vector3[] Vertices, Normals;
        internal readonly Vector2[] UVs;
        internal readonly int[] Triangles, SourceIndices;
        private readonly int[] m_Charts, m_TriangleCharts;
        private readonly Vector2[] m_BaseUV, m_CornerUV, m_Min, m_Max;

        internal SeededMapping(GeneratedMappingSeed seed, MeshData mesh, Vector2[] coordinates, int[] triangleCharts)
        {
            Seed = seed;
            m_CornerUV = coordinates;
            m_TriangleCharts = triangleCharts;
            var lookup = new Dictionary<long, int>();
            var sources = new List<int>();
            var charts = new List<int>();
            var uv = new List<Vector2>();
            Triangles = new int[mesh.Triangles.Length];
            m_Min = new Vector2[seed.ReferenceMetric.Length];
            m_Max = new Vector2[m_Min.Length];
            for (int i = 0; i < m_Min.Length; i++)
            {
                m_Min[i] = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                m_Max[i] = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            }

            for (int i = 0; i < mesh.Triangles.Length; i++)
            {
                int vertex = mesh.Triangles[i], chart = triangleCharts[i / 3];
                long key = ((long)chart << 32) | (uint)vertex;
                if (!lookup.TryGetValue(key, out int target))
                {
                    target = sources.Count;
                    lookup.Add(key, target);
                    sources.Add(vertex);
                    charts.Add(chart);
                    uv.Add(coordinates[i]);
                    m_Min[chart] = new Vector2(Math.Min(m_Min[chart].x, coordinates[i].x), Math.Min(m_Min[chart].y, coordinates[i].y));
                    m_Max[chart] = new Vector2(Math.Max(m_Max[chart].x, coordinates[i].x), Math.Max(m_Max[chart].y, coordinates[i].y));
                }

                Triangles[i] = target;
            }

            SourceIndices = sources.ToArray();
            m_Charts = charts.ToArray();
            m_BaseUV = uv.ToArray();
            Vertices = new Vector3[sources.Count];
            Normals = new Vector3[sources.Count];
            UVs = new Vector2[sources.Count];
        }

        internal void Update(MeshData mesh, Vector3 scale, TextureMappingData mapping, Vector2 tile)
        {
            bool stretch = mapping.Mode == TextureMappingMode.Stretch;
            var metric = stretch ? null : GeneratedMappingSeed.Measure(mesh, scale, m_TriangleCharts, m_CornerUV, Seed.ReferenceMetric.Length);
            float angle = mapping.Rotation * Mathf.Deg2Rad, cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle);
            for (int i = 0; i < Vertices.Length; i++)
            {
                int source = SourceIndices[i], chart = m_Charts[i];
                Vector2 value = m_BaseUV[i];
                Vertices[i] = mesh.Vertices[source];
                Normals[i] = mesh.Normals == null ? Vector3.up : mesh.Normals[source];
                if (stretch)
                {
                    Vector2 size = m_Max[chart] - m_Min[chart];
                    value = new Vector2(size.x > 0.000001f ? (value.x - m_Min[chart].x) / size.x : 0, size.y > 0.000001f ? (value.y - m_Min[chart].y) / size.y : 0);
                    value -= new Vector2(.5f, .5f);
                }
                else value = new Vector2(value.x * metric[chart].x / Seed.ReferenceMetric[chart].x, value.y * metric[chart].y / Seed.ReferenceMetric[chart].y);

                float u = value.x * cosine - value.y * sine, v = value.x * sine + value.y * cosine;
                UVs[i] = stretch ? new Vector2(u + .5f, v + .5f) : new Vector2((u + mapping.Offset.x) / Mathf.Max(.001f, tile.x), (v + mapping.Offset.y) / Mathf.Max(.001f, tile.y));
            }
        }
    }
}