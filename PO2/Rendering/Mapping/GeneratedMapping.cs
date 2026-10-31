using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Materials;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Rendering.Mapping
{
    // Runtime UVs for Repeat and Stretch mapping.
    // Authored surface coordinates keep their layout; other meshes get planar charts.
    // The source mesh stays untouched~ this creates render-only vertices where charts need seams.
    internal sealed class GeneratedMapping
    {
        private const float kChartAngleDegrees = 45f;
        private const float kUvSeamEpsilon = 0.0001f;
        private const float kHardNormalDot = 0.999f;
        private const float kDegenerateNormalEpsilon = 0.00000001f;
        private const float kHorizontalNormalDot = 0.9f;

        private static readonly float s_ChartNormalDot = Mathf.Cos(kChartAngleDegrees * Mathf.Deg2Rad);

        private readonly SurfaceTopology m_SurfaceTopology;
        private readonly bool m_UseSurfaceUVs;

        private int[] m_TriangleChartIds;
        private int[] m_PreviousTriangleChartIds;
        private int[] m_TraversalQueue;
        private Vector3[] m_TriangleNormals;
        private Vector3[] m_TriangleAreaNormals;

        private ChartData[] m_Charts = Array.Empty<ChartData>();
        private Vector3[] m_ChartNormalSums = Array.Empty<Vector3>();
        private int[] m_ChartLowestLogicalVertex = Array.Empty<int>();
        private bool[] m_ChartContainsInheritedOrigin = Array.Empty<bool>();
        private int[] m_InheritedChartIds = Array.Empty<int>();

        private readonly Dictionary<long, int> m_ChartOverlapCounts = new Dictionary<long, int>();

        private readonly Dictionary<long, int> m_RuntimeVertexLookup = new Dictionary<long, int>();

        private readonly List<int> m_SourceVertexIndices = new List<int>();

        private readonly List<int> m_RuntimeVertexChartIds = new List<int>();

        private readonly List<int> m_RuntimeTriangles = new List<int>();

        private int[] m_GeneratedSourceVertexIndices = Array.Empty<int>();
        private int[] m_GeneratedVertexChartIds = Array.Empty<int>();
        private int[] m_GeneratedTriangles = Array.Empty<int>();

        private Vector3[] m_GeneratedVertices = Array.Empty<Vector3>();
        private Vector3[] m_GeneratedNormals = Array.Empty<Vector3>();
        private Vector2[] m_GeneratedUVs = Array.Empty<Vector2>();

        private readonly SeededMapping m_Seeded;
        private readonly GeneratedMappingSeed m_SourceSeed;
        internal bool MatchesSource(MeshData mesh, VertexAttributeFlags attributes, GeneratedMappingSeed seed) =>
            m_UseSurfaceUVs == HasSurfaceUVs(mesh, attributes) && (m_UseSurfaceUVs || ReferenceEquals(m_SourceSeed, seed));
        internal bool UsesSurfaceUVs => m_UseSurfaceUVs;
        internal GeneratedMappingSeed PreservedSeed => m_Seeded?.Seed;
        internal int[] TriangleCharts => m_TriangleChartIds;
        internal int ChartCount => m_Charts.Length;

        internal Vector2 RawCoordinate(int vertex, Vector3 scale)
        {
            var chart = m_Charts[m_GeneratedVertexChartIds[vertex]];
            Vector3 relative = Vector3.Scale(m_GeneratedVertices[vertex], scale) - chart.ScaledOrigin;
            return new Vector2(Vector3.Dot(relative, chart.ScaledU), Vector3.Dot(relative, chart.ScaledV));
        }

        public Vector3[] Vertices => m_GeneratedVertices;
        public Vector3[] Normals => m_GeneratedNormals;
        public Vector2[] UVs => m_GeneratedUVs;
        public int[] Triangles => m_GeneratedTriangles;
        internal int[] SourceVertexIndices => m_GeneratedSourceVertexIndices;

        private static bool HasSurfaceUVs(MeshData mesh, VertexAttributeFlags attributes) =>
            (attributes & VertexAttributeFlags.SurfaceUVCoordinates) != 0 && mesh.UV0 != null && mesh.UV0.Length == mesh.Vertices.Length;

        internal static GeneratedMapping Create(MeshData mesh, VertexAttributeFlags attributes,
            GeneratedMappingSeed seed, int[] sourceIndices, out MeshTopology topology)
        {
            bool surfaceUVs = HasSurfaceUVs(mesh, attributes);
            // Bind once. Merely having a seed does not mean it fits this area.
            SeededMapping bound = surfaceUVs ? null : seed?.Bind(mesh, sourceIndices);
            topology = surfaceUVs || bound != null ? null : MeshTopology.Build(mesh);
            return new GeneratedMapping(mesh, topology, surfaceUVs, bound, seed);
        }

        public GeneratedMapping(MeshData mesh, MeshTopology topology, VertexAttributeFlags attributes = VertexAttributeFlags.None, GeneratedMappingSeed seed = null, int[] sourceIndices = null)
            : this(mesh, topology, HasSurfaceUVs(mesh, attributes), HasSurfaceUVs(mesh, attributes) ? null : seed?.Bind(mesh, sourceIndices), seed)
        {
        }

        private GeneratedMapping(MeshData mesh, MeshTopology topology, bool surfaceUVs, SeededMapping seeded, GeneratedMappingSeed sourceSeed)
        {
            m_SourceSeed = sourceSeed;
            m_UseSurfaceUVs = surfaceUVs;
            if (m_UseSurfaceUVs) return;
            m_Seeded = seeded;
            if (m_Seeded != null)
            {
                m_GeneratedVertices = m_Seeded.Vertices;
                m_GeneratedNormals = m_Seeded.Normals;
                m_GeneratedUVs = m_Seeded.UVs;
                m_GeneratedTriangles = m_Seeded.Triangles;
                m_GeneratedSourceVertexIndices = m_Seeded.SourceIndices;
                return;
            }

            if (topology == null) throw new ArgumentNullException(nameof(topology));
            m_SurfaceTopology = SurfaceTopology.Build(mesh, topology);

            int triangleCount = m_SurfaceTopology.TriangleCount;
            m_TriangleChartIds = new int[triangleCount];
            m_PreviousTriangleChartIds = new int[triangleCount];
            m_TraversalQueue = new int[Math.Max(1, triangleCount)];
            m_TriangleNormals = new Vector3[triangleCount];
            m_TriangleAreaNormals = new Vector3[triangleCount];

            for (int i = 0; i < m_PreviousTriangleChartIds.Length; i++)
            {
                m_PreviousTriangleChartIds[i] = -1;
            }
        }

        public bool Update(MeshData mesh, MeshTopology topology, Vector3 objectScale, TextureMappingData mapping, Vector2 effectiveTileSize, bool forceStructureRebuild)
        {
            if (m_Seeded != null)
            {
                m_Seeded.Update(mesh, objectScale, mapping, effectiveTileSize);
                return forceStructureRebuild;
            }

            if (m_UseSurfaceUVs)
            {
                bool rebuild = forceStructureRebuild || m_GeneratedVertices.Length != mesh.Vertices.Length;
                if (rebuild)
                {
                    int count = mesh.Vertices.Length;
                    m_GeneratedSourceVertexIndices = new int[count];
                    for (int i = 0; i < count; i++) m_GeneratedSourceVertexIndices[i] = i;
                    m_GeneratedTriangles = (int[])mesh.Triangles.Clone();
                    m_GeneratedVertices = new Vector3[count];
                    m_GeneratedNormals = new Vector3[count];
                    m_GeneratedUVs = new Vector2[count];
                }

                UpdateSurfaceUVs(mesh, objectScale, mapping, effectiveTileSize);
                return rebuild;
            }

            if (topology == null) throw new ArgumentNullException(nameof(topology));
            CalculateTriangleNormals(mesh);

            // Deformation can change which faces still belong in one chart.
            // Rebuild vertex/index structure only when that assignment changes~
            // ordinary movement just refreshes chart frames and streams.
            int chartCount = BuildCharts(mesh);
            bool structureChanged = forceStructureRebuild || !ChartAssignmentMatchesPrevious();

            if (structureChanged)
            {
                RebuildChartLineage(chartCount);
                BuildRuntimeStructure(mesh, chartCount);
            }

            UpdateChartFrames(mesh, topology, objectScale, chartCount);
            UpdateGeneratedVertexData(mesh, objectScale, mapping, effectiveTileSize);

            Array.Copy(m_TriangleChartIds, m_PreviousTriangleChartIds, m_TriangleChartIds.Length);

            return structureChanged;
        }

        public void UpdateMappingParameters(MeshData mesh, Vector3 objectScale, TextureMappingData mapping, Vector2 effectiveTileSize)
        {
            if (m_Seeded != null)
            {
                m_Seeded.Update(mesh, objectScale, mapping, effectiveTileSize);
                return;
            }

            if (m_UseSurfaceUVs)
            {
                UpdateSurfaceUVs(mesh, objectScale, mapping, effectiveTileSize);
                return;
            }

            UpdateGeneratedVertexData(mesh, objectScale, mapping, effectiveTileSize);
        }

        private void UpdateSurfaceUVs(MeshData mesh, Vector3 objectScale, TextureMappingData mapping, Vector2 effectiveTileSize)
        {
            bool stretch = mapping.Mode == TextureMappingMode.Stretch;
            Vector2 meters = stretch ? Vector2.one : MeasureSurfaceUVScale(mesh, objectScale);
            float tileWidth = Mathf.Max(0.001f, effectiveTileSize.x);
            float tileHeight = Mathf.Max(0.001f, effectiveTileSize.y);
            float angle = mapping.Rotation * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(angle);
            float sine = Mathf.Sin(angle);
            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                Vector4 source = mesh.UV0[i];
                float u = stretch ? source.x - 0.5f : source.z * meters.x;
                float v = stretch ? source.y - 0.5f : source.w * meters.y;
                float rotatedU = u * cosine - v * sine;
                float rotatedV = u * sine + v * cosine;
                m_GeneratedUVs[i] = stretch ? new Vector2(rotatedU + 0.5f, rotatedV + 0.5f) : new Vector2((rotatedU + mapping.Offset.x) / tileWidth, (rotatedV + mapping.Offset.y) / tileHeight);
                WriteGeneratedGeometry(i, i, mesh.Vertices[i], mesh.Vertices, mesh.Normals);
            }
        }

        private static Vector2 MeasureSurfaceUVScale(MeshData mesh, Vector3 objectScale)
        {
            // Keep one scale for the whole layout so neighboring strips retain
            // their horizontal rows. This also follows baked edits and object scale.
            // A deformed surface gets an average tile size, without re-cutting its UVs.
            double totalWeight = 0;
            double totalU = 0;
            double totalV = 0;
            for (int i = 0; i < mesh.Triangles.Length; i += 3)
            {
                int a = mesh.Triangles[i];
                int b = mesh.Triangles[i + 1];
                int c = mesh.Triangles[i + 2];
                Vector4 uvA = mesh.UV0[a];
                Vector4 uvB = mesh.UV0[b];
                Vector4 uvC = mesh.UV0[c];
                float du1 = uvB.z - uvA.z;
                float dv1 = uvB.w - uvA.w;
                float du2 = uvC.z - uvA.z;
                float dv2 = uvC.w - uvA.w;
                double determinant = (double)du1 * dv2 - (double)du2 * dv1;
                if (Math.Abs(determinant) < 1e-16) continue;
                Vector3 edge1 = Vector3.Scale(mesh.Vertices[b] - mesh.Vertices[a], objectScale);
                Vector3 edge2 = Vector3.Scale(mesh.Vertices[c] - mesh.Vertices[a], objectScale);
                Vector3 alongU = (edge1 * dv2 - edge2 * dv1) / (float)determinant;
                Vector3 alongV = (edge2 * du1 - edge1 * du2) / (float)determinant;
                float uLength = alongU.magnitude;
                if (uLength <= 0f) continue;
                // The taper introduces shear. Removing it avoids counting that
                // sideways movement as extra distance between horizontal rows.
                float vLength = Vector3.Cross(alongU, alongV).magnitude / uLength;
                if (float.IsNaN(uLength) || float.IsInfinity(uLength) || float.IsNaN(vLength) || float.IsInfinity(vLength)) continue;
                double weight = Math.Abs(determinant);
                totalU += uLength * weight;
                totalV += vLength * weight;
                totalWeight += weight;
            }

            return totalWeight > 0 ? new Vector2((float)(totalU / totalWeight), (float)(totalV / totalWeight)) : Vector2.one;
        }

        private void CalculateTriangleNormals(MeshData mesh)
        {
            Vector3[] vertices = mesh.Vertices;

            for (int triangleId = 0; triangleId < m_SurfaceTopology.TriangleCount; triangleId++)
            {
                SurfaceTopology.TriangleData triangle = m_SurfaceTopology.GetTriangle(triangleId);

                Vector3 a = vertices[triangle.GetRenderVertex(0)];
                Vector3 b = vertices[triangle.GetRenderVertex(1)];
                Vector3 c = vertices[triangle.GetRenderVertex(2)];

                Vector3 areaNormal = Vector3.Cross(b - a, c - a);
                m_TriangleAreaNormals[triangleId] = areaNormal;
                m_TriangleNormals[triangleId] = areaNormal.sqrMagnitude > kDegenerateNormalEpsilon ? areaNormal.normalized : Vector3.zero;
            }
        }

        private int BuildCharts(MeshData mesh)
        {
            for (int i = 0; i < m_TriangleChartIds.Length; i++)
            {
                m_TriangleChartIds[i] = -1;
            }

            // Flood fill across traversable triangle neighbors.
            // The seed normal limits cumulative bending so a curved surface
            // does not eventually wrap all the way around into one enormous chart.
            int chartId = 0;

            for (int seedTriangle = 0; seedTriangle < m_TriangleChartIds.Length; seedTriangle++)
            {
                if (m_TriangleChartIds[seedTriangle] >= 0)
                {
                    continue;
                }

                int queueStart = 0;
                int queueEnd = 0;
                Vector3 chartReferenceNormal = m_TriangleNormals[seedTriangle];

                m_TraversalQueue[queueEnd++] = seedTriangle;
                m_TriangleChartIds[seedTriangle] = chartId;

                while (queueStart < queueEnd)
                {
                    int triangleId = m_TraversalQueue[queueStart++];
                    SurfaceTopology.TriangleData triangle = m_SurfaceTopology.GetTriangle(triangleId);

                    for (int edge = 0; edge < 3; edge++)
                    {
                        int neighborId = triangle.GetNeighbor(edge);

                        if (neighborId < 0 || m_TriangleChartIds[neighborId] >= 0 || !CanShareChart(mesh, triangleId, edge, neighborId) || !IsWithinChartNormalRange(chartReferenceNormal, neighborId))
                        {
                            continue;
                        }

                        m_TriangleChartIds[neighborId] = chartId;
                        m_TraversalQueue[queueEnd++] = neighborId;
                    }
                }

                chartId++;
            }

            return chartId;
        }

        private bool IsWithinChartNormalRange(Vector3 chartReferenceNormal, int triangleId)
        {
            Vector3 triangleNormal = m_TriangleNormals[triangleId];

            if (chartReferenceNormal == Vector3.zero || triangleNormal == Vector3.zero)
            {
                return false;
            }

            return Vector3.Dot(chartReferenceNormal, triangleNormal) >= s_ChartNormalDot;
        }

        private bool CanShareChart(MeshData mesh, int triangleId, int edge, int neighborId)
        {
            Vector3 normalA = m_TriangleNormals[triangleId];
            Vector3 normalB = m_TriangleNormals[neighborId];

            if (normalA == Vector3.zero || normalB == Vector3.zero)
            {
                return false;
            }

            if (Vector3.Dot(normalA, normalB) < s_ChartNormalDot)
            {
                return false;
            }

            return !HasStrongAuthoredSeamHint(mesh, triangleId, edge, neighborId);
        }

        private bool HasStrongAuthoredSeamHint(MeshData mesh, int triangleId, int edge, int neighborId)
        {
            SurfaceTopology.TriangleData triangle = m_SurfaceTopology.GetTriangle(triangleId);

            SurfaceTopology.TriangleData neighbor = m_SurfaceTopology.GetTriangle(neighborId);

            triangle.GetEdgeLogicalVertices(edge, out int logicalA, out int logicalB);

            if (!triangle.TryGetRenderVertexForLogical(logicalA, out int renderA0) || !triangle.TryGetRenderVertexForLogical(logicalB, out int renderB0) || !neighbor.TryGetRenderVertexForLogical(logicalA, out int renderA1) || !neighbor.TryGetRenderVertexForLogical(logicalB, out int renderB1))
            {
                return false;
            }

            // Shared logical vertices with different render copies are only a hint.
            // Keep the seam when UVs or normals actually disagree.
            if (renderA0 == renderA1 && renderB0 == renderB1)
            {
                return false;
            }

            Vector4[] uv0 = mesh.UV0;

            if (uv0 != null && uv0.Length == mesh.Vertices.Length)
            {
                Vector2 uvA0 = new Vector2(uv0[renderA0].x, uv0[renderA0].y);
                Vector2 uvA1 = new Vector2(uv0[renderA1].x, uv0[renderA1].y);
                Vector2 uvB0 = new Vector2(uv0[renderB0].x, uv0[renderB0].y);
                Vector2 uvB1 = new Vector2(uv0[renderB1].x, uv0[renderB1].y);

                if ((uvA0 - uvA1).sqrMagnitude > kUvSeamEpsilon * kUvSeamEpsilon || (uvB0 - uvB1).sqrMagnitude > kUvSeamEpsilon * kUvSeamEpsilon)
                {
                    return true;
                }
            }

            Vector3[] normals = mesh.Normals;

            if (normals != null && normals.Length == mesh.Vertices.Length)
            {
                Vector3 normalA0 = normals[renderA0];
                Vector3 normalA1 = normals[renderA1];
                Vector3 normalB0 = normals[renderB0];
                Vector3 normalB1 = normals[renderB1];

                if (normalA0.sqrMagnitude > 0f && normalA1.sqrMagnitude > 0f && Vector3.Dot(normalA0.normalized, normalA1.normalized) < kHardNormalDot)
                {
                    return true;
                }

                if (normalB0.sqrMagnitude > 0f && normalB1.sqrMagnitude > 0f && Vector3.Dot(normalB0.normalized, normalB1.normalized) < kHardNormalDot)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ChartAssignmentMatchesPrevious()
        {
            if (m_PreviousTriangleChartIds.Length != m_TriangleChartIds.Length)
            {
                return false;
            }

            for (int i = 0; i < m_TriangleChartIds.Length; i++)
            {
                if (m_PreviousTriangleChartIds[i] != m_TriangleChartIds[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void BuildRuntimeStructure(MeshData mesh, int chartCount)
        {
            m_RuntimeVertexLookup.Clear();
            m_SourceVertexIndices.Clear();
            m_RuntimeVertexChartIds.Clear();
            m_RuntimeTriangles.Clear();

            // A source render vertex is reused inside a chart but duplicated across charts.
            // That is the minimum split needed for independent UVs.
            for (int triangleId = 0; triangleId < m_SurfaceTopology.TriangleCount; triangleId++)
            {
                int chartId = m_TriangleChartIds[triangleId];
                SurfaceTopology.TriangleData triangle = m_SurfaceTopology.GetTriangle(triangleId);

                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceRenderIndex = triangle.GetRenderVertex(corner);
                    long key = MakeRuntimeVertexKey(sourceRenderIndex, chartId);

                    if (!m_RuntimeVertexLookup.TryGetValue(key, out int generatedIndex))
                    {
                        generatedIndex = m_SourceVertexIndices.Count;
                        m_RuntimeVertexLookup.Add(key, generatedIndex);
                        m_SourceVertexIndices.Add(sourceRenderIndex);
                        m_RuntimeVertexChartIds.Add(chartId);
                    }

                    m_RuntimeTriangles.Add(generatedIndex);
                }
            }

            m_GeneratedSourceVertexIndices = m_SourceVertexIndices.ToArray();
            m_GeneratedVertexChartIds = m_RuntimeVertexChartIds.ToArray();
            m_GeneratedTriangles = m_RuntimeTriangles.ToArray();

            int vertexCount = m_GeneratedSourceVertexIndices.Length;
            m_GeneratedVertices = new Vector3[vertexCount];
            m_GeneratedNormals = new Vector3[vertexCount];
            m_GeneratedUVs = new Vector2[vertexCount];
        }

        private void UpdateChartFrames(MeshData mesh, MeshTopology topology, Vector3 objectScale, int chartCount)
        {
            EnsureChartCapacity(chartCount);
            EnsureChartScratchCapacity(chartCount);

            Array.Clear(m_ChartNormalSums, 0, chartCount);
            Array.Clear(m_ChartContainsInheritedOrigin, 0, chartCount);

            for (int i = 0; i < chartCount; i++)
            {
                m_ChartLowestLogicalVertex[i] = int.MaxValue;
            }

            // The chart origin and U direction survive normal deformation where possible.
            // Without that lineage, textures visibly jump when a face crosses a chart
            // threshold during vertex editing.
            for (int triangleId = 0; triangleId < m_SurfaceTopology.TriangleCount; triangleId++)
            {
                int chartId = m_TriangleChartIds[triangleId];
                m_ChartNormalSums[chartId] += m_TriangleAreaNormals[triangleId];

                SurfaceTopology.TriangleData triangle = m_SurfaceTopology.GetTriangle(triangleId);

                for (int corner = 0; corner < 3; corner++)
                {
                    int logicalId = triangle.GetLogicalVertex(corner);

                    if (logicalId < m_ChartLowestLogicalVertex[chartId])
                    {
                        m_ChartLowestLogicalVertex[chartId] = logicalId;
                    }

                    ChartData existingChart = m_Charts[chartId];

                    if (existingChart != null && existingChart.Initialized && logicalId == existingChart.OriginLogicalVertexId)
                    {
                        m_ChartContainsInheritedOrigin[chartId] = true;
                    }
                }
            }

            for (int chartId = 0; chartId < chartCount; chartId++)
            {
                ChartData chart = m_Charts[chartId] ?? new ChartData();
                Vector3 chartNormal = m_ChartNormalSums[chartId].sqrMagnitude > kDegenerateNormalEpsilon ? m_ChartNormalSums[chartId].normalized : Vector3.up;

                Vector3 localU;
                Vector3 localV;

                if (chart.Initialized)
                {
                    BuildLocalChartBasis(chartNormal, chart.LocalU, out localU, out localV);

                    if (!m_ChartContainsInheritedOrigin[chartId])
                    {
                        chart.OriginLogicalVertexId = m_ChartLowestLogicalVertex[chartId] == int.MaxValue ? 0 : m_ChartLowestLogicalVertex[chartId];
                    }
                }
                else
                {
                    BuildInitialSemanticChartBasis(chartNormal, out localU, out localV);

                    chart.OriginLogicalVertexId = m_ChartLowestLogicalVertex[chartId] == int.MaxValue ? 0 : m_ChartLowestLogicalVertex[chartId];
                    chart.Initialized = true;
                }

                Vector3 scaledU = Vector3.Scale(localU, objectScale);
                Vector3 scaledV = Vector3.Scale(localV, objectScale);

                if (scaledU.sqrMagnitude <= kDegenerateNormalEpsilon)
                {
                    scaledU = localU;
                }

                scaledU.Normalize();

                // Gram-Schmidt keeps the two projection axes orthogonal after a nonuniform object scale.
                scaledV -= scaledU * Vector3.Dot(scaledV, scaledU);

                if (scaledV.sqrMagnitude <= kDegenerateNormalEpsilon)
                {
                    Vector3 scaledNormal = Vector3.Cross(Vector3.Scale(localU, objectScale), Vector3.Scale(localV, objectScale));

                    if (scaledNormal.sqrMagnitude <= kDegenerateNormalEpsilon)
                    {
                        scaledNormal = chartNormal;
                    }

                    scaledV = Vector3.Cross(scaledNormal.normalized, scaledU);
                }

                scaledV.Normalize();

                Vertex originVertex = topology.GetVertex(chart.OriginLogicalVertexId);

                Vector3 originLocal = originVertex.GetPosition(mesh);

                chart.LocalU = localU;
                chart.LocalV = localV;
                chart.ScaledU = scaledU;
                chart.ScaledV = scaledV;
                chart.ScaledOrigin = Vector3.Scale(originLocal, objectScale);
                m_Charts[chartId] = chart;
            }
        }

        private void UpdateGeneratedVertexData(MeshData mesh, Vector3 objectScale, TextureMappingData mapping, Vector2 effectiveTileSize)
        {
            switch (mapping.Mode)
            {
                case TextureMappingMode.Stretch:
                    UpdateStretchVertexData(mesh, objectScale, mapping);
                    break;

                case TextureMappingMode.Repeat:
                default:
                    UpdateRepeatVertexData(mesh, objectScale, mapping, effectiveTileSize);
                    break;
            }
        }

        private void UpdateRepeatVertexData(MeshData mesh, Vector3 objectScale, TextureMappingData mapping, Vector2 effectiveTileSize)
        {
            Vector3[] sourceVertices = mesh.Vertices;
            Vector3[] sourceNormals = mesh.Normals;

            // Projection happens in scaled object space so tile size is really measured in meters,
            // including nonuniform object scale.
            float tileWidth = Mathf.Max(0.001f, effectiveTileSize.x);
            float tileHeight = Mathf.Max(0.001f, effectiveTileSize.y);
            float angle = mapping.Rotation * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(angle);
            float sine = Mathf.Sin(angle);
            Vector2 offset = mapping.Offset;

            for (int i = 0; i < m_GeneratedSourceVertexIndices.Length; i++)
            {
                int sourceIndex = m_GeneratedSourceVertexIndices[i];
                int chartId = m_GeneratedVertexChartIds[i];
                ChartData chart = m_Charts[chartId];

                Vector3 localPosition = sourceVertices[sourceIndex];
                Vector3 scaledPosition = Vector3.Scale(localPosition, objectScale);
                Vector3 relative = scaledPosition - chart.ScaledOrigin;

                float uMeters = Vector3.Dot(relative, chart.ScaledU);
                float vMeters = Vector3.Dot(relative, chart.ScaledV);

                float rotatedU = uMeters * cosine - vMeters * sine;
                float rotatedV = uMeters * sine + vMeters * cosine;

                WriteGeneratedGeometry(i, sourceIndex, localPosition, sourceVertices, sourceNormals);

                m_GeneratedUVs[i] = new Vector2((rotatedU + offset.x) / tileWidth, (rotatedV + offset.y) / tileHeight);
            }
        }

        private void UpdateStretchVertexData(MeshData mesh, Vector3 objectScale, TextureMappingData mapping)
        {
            Vector3[] sourceVertices = mesh.Vertices;
            Vector3[] sourceNormals = mesh.Normals;

            // Stretch deliberately ignores physical tile size and Offset.
            // It fits one complete texture tile to the current extent of each chart.
            for (int chartId = 0; chartId < m_Charts.Length; chartId++)
            {
                ChartData chart = m_Charts[chartId];
                chart.MinU = float.PositiveInfinity;
                chart.MaxU = float.NegativeInfinity;
                chart.MinV = float.PositiveInfinity;
                chart.MaxV = float.NegativeInfinity;
            }

            for (int i = 0; i < m_GeneratedSourceVertexIndices.Length; i++)
            {
                int sourceIndex = m_GeneratedSourceVertexIndices[i];
                int chartId = m_GeneratedVertexChartIds[i];
                ChartData chart = m_Charts[chartId];

                Vector3 localPosition = sourceVertices[sourceIndex];
                Vector3 scaledPosition = Vector3.Scale(localPosition, objectScale);
                Vector3 relative = scaledPosition - chart.ScaledOrigin;

                float u = Vector3.Dot(relative, chart.ScaledU);
                float v = Vector3.Dot(relative, chart.ScaledV);

                if (u < chart.MinU) chart.MinU = u;
                if (u > chart.MaxU) chart.MaxU = u;
                if (v < chart.MinV) chart.MinV = v;
                if (v > chart.MaxV) chart.MaxV = v;

                WriteGeneratedGeometry(i, sourceIndex, localPosition, sourceVertices, sourceNormals);
            }

            float angle = mapping.Rotation * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(angle);
            float sine = Mathf.Sin(angle);

            for (int i = 0; i < m_GeneratedSourceVertexIndices.Length; i++)
            {
                int sourceIndex = m_GeneratedSourceVertexIndices[i];
                int chartId = m_GeneratedVertexChartIds[i];
                ChartData chart = m_Charts[chartId];

                Vector3 scaledPosition = Vector3.Scale(sourceVertices[sourceIndex], objectScale);
                Vector3 relative = scaledPosition - chart.ScaledOrigin;

                float u = Vector3.Dot(relative, chart.ScaledU);
                float v = Vector3.Dot(relative, chart.ScaledV);

                float width = chart.MaxU - chart.MinU;
                float height = chart.MaxV - chart.MinV;

                float normalizedU = width > 0.000001f ? (u - chart.MinU) / width : 0f;
                float normalizedV = height > 0.000001f ? (v - chart.MinV) / height : 0f;

                // Rotation is around the center of the fitted tile so changing
                // orientation does not introduce an arbitrary shift.
                float centeredU = normalizedU - 0.5f;
                float centeredV = normalizedV - 0.5f;

                m_GeneratedUVs[i] = new Vector2(centeredU * cosine - centeredV * sine + 0.5f, centeredU * sine + centeredV * cosine + 0.5f);
            }
        }

        private void WriteGeneratedGeometry(int generatedIndex, int sourceIndex, Vector3 localPosition, Vector3[] sourceVertices, Vector3[] sourceNormals)
        {
            m_GeneratedVertices[generatedIndex] = localPosition;
            m_GeneratedNormals[generatedIndex] = sourceNormals != null && sourceNormals.Length == sourceVertices.Length ? sourceNormals[sourceIndex] : Vector3.up;
        }

        private void RebuildChartLineage(int chartCount)
        {
            ChartData[] previousCharts = m_Charts;
            ChartData[] newCharts = new ChartData[chartCount];

            EnsureInheritedChartCapacity(chartCount);

            for (int i = 0; i < chartCount; i++)
            {
                m_InheritedChartIds[i] = -1;
            }

            // New chart ids are traversal order details.
            // Match each new chart to the old chart with the most shared triangles
            // so its basis and origin do not randomly change after a split/merge.
            m_ChartOverlapCounts.Clear();

            for (int triangleId = 0; triangleId < m_TriangleChartIds.Length; triangleId++)
            {
                int newChartId = m_TriangleChartIds[triangleId];
                int oldChartId = m_PreviousTriangleChartIds[triangleId];

                if (oldChartId < 0 || oldChartId >= previousCharts.Length)
                {
                    continue;
                }

                long key = MakeChartOverlapKey(newChartId, oldChartId);
                m_ChartOverlapCounts.TryGetValue(key, out int count);
                m_ChartOverlapCounts[key] = count + 1;
            }

            foreach (KeyValuePair<long, int> pair in m_ChartOverlapCounts)
            {
                DecodeChartOverlapKey(pair.Key, out int newChartId, out int oldChartId);

                int inheritedOldChartId = m_InheritedChartIds[newChartId];

                if (inheritedOldChartId < 0)
                {
                    m_InheritedChartIds[newChartId] = oldChartId;
                    continue;
                }

                int inheritedCount = m_ChartOverlapCounts[MakeChartOverlapKey(newChartId, inheritedOldChartId)];

                if (pair.Value > inheritedCount || (pair.Value == inheritedCount && oldChartId < inheritedOldChartId))
                {
                    m_InheritedChartIds[newChartId] = oldChartId;
                }
            }

            for (int newChartId = 0; newChartId < chartCount; newChartId++)
            {
                int oldChartId = m_InheritedChartIds[newChartId];

                if (oldChartId >= 0 && oldChartId < previousCharts.Length && previousCharts[oldChartId] != null && previousCharts[oldChartId].Initialized)
                {
                    ChartData oldChart = previousCharts[oldChartId];
                    newCharts[newChartId] = new ChartData
                    {
                        Initialized = true,
                        OriginLogicalVertexId = oldChart.OriginLogicalVertexId,
                        LocalU = oldChart.LocalU,
                        LocalV = oldChart.LocalV,
                    };
                }
                else
                {
                    newCharts[newChartId] = new ChartData();
                }
            }

            m_Charts = newCharts;
        }

        private void EnsureChartCapacity(int chartCount)
        {
            if (m_Charts.Length == chartCount)
            {
                return;
            }

            ChartData[] newCharts = new ChartData[chartCount];
            int copyCount = Math.Min(m_Charts.Length, newCharts.Length);
            Array.Copy(m_Charts, newCharts, copyCount);
            m_Charts = newCharts;
        }

        private void EnsureInheritedChartCapacity(int chartCount)
        {
            if (m_InheritedChartIds.Length < chartCount)
            {
                m_InheritedChartIds = new int[chartCount];
            }
        }

        private void EnsureChartScratchCapacity(int chartCount)
        {
            if (m_ChartNormalSums.Length < chartCount)
            {
                m_ChartNormalSums = new Vector3[chartCount];
            }

            if (m_ChartLowestLogicalVertex.Length < chartCount)
            {
                m_ChartLowestLogicalVertex = new int[chartCount];
            }

            if (m_ChartContainsInheritedOrigin.Length < chartCount)
            {
                m_ChartContainsInheritedOrigin = new bool[chartCount];
            }
        }

        private static void BuildInitialSemanticChartBasis(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            /* New charts get an object-local semantic orientation.
             *
             * Wall-like surfaces keep texture V aligned with local Up so directional
             * materials such as masonry and siding remain upright around the object.
             *
             * Near-horizontal surfaces instead align U with local
             * Right, giving tops/bottoms a deterministic X/Z orientation.
             * Once initialized, chart lineage owns continuity and this method
             * is not consulted again for ordinary deformation.
             */
            float upDot = Mathf.Abs(Vector3.Dot(normal, Vector3.up));

            if (upDot < kHorizontalNormalDot)
            {
                Vector3 projectedUp = Vector3.up - normal * Vector3.Dot(Vector3.up, normal);

                if (projectedUp.sqrMagnitude > kDegenerateNormalEpsilon)
                {
                    v = projectedUp.normalized;
                    u = Vector3.Cross(v, normal);

                    if (u.sqrMagnitude > kDegenerateNormalEpsilon)
                    {
                        u.Normalize();
                        // Rebuild V from the final U so the basis is exactly orthonormal
                        // and follows the same handedness used by BuildLocalChartBasis()
                        v = Vector3.Cross(normal, u).normalized;
                        return;
                    }
                }
            }
            else
            {
                Vector3 projectedRight = Vector3.right - normal * Vector3.Dot(Vector3.right, normal);

                if (projectedRight.sqrMagnitude > kDegenerateNormalEpsilon)
                {
                    u = projectedRight.normalized;
                    v = Vector3.Cross(normal, u);

                    if (v.sqrMagnitude > kDegenerateNormalEpsilon)
                    {
                        v.Normalize();
                        return;
                    }
                }
            }

            // Degenerate/near-parallel geometry still gets the old robust cardinal-axis fallback.
            // This is a safety path, not the normal orientation rule.
            Vector3 referenceAxis = ChooseReferenceAxis(normal);
            BuildLocalChartBasis(normal, referenceAxis, out u, out v);
        }

        private static void BuildLocalChartBasis(Vector3 normal, Vector3 referenceAxis, out Vector3 u, out Vector3 v)
        {
            u = referenceAxis - normal * Vector3.Dot(referenceAxis, normal);

            if (u.sqrMagnitude <= kDegenerateNormalEpsilon)
            {
                referenceAxis = ChooseReferenceAxis(normal);
                u = referenceAxis - normal * Vector3.Dot(referenceAxis, normal);
            }

            u.Normalize();
            v = Vector3.Cross(normal, u).normalized;
        }

        private static Vector3 ChooseReferenceAxis(Vector3 normal)
        {
            float x = Mathf.Abs(Vector3.Dot(normal, Vector3.right));
            float y = Mathf.Abs(Vector3.Dot(normal, Vector3.up));
            float z = Mathf.Abs(Vector3.Dot(normal, Vector3.forward));

            if (x <= y && x <= z)
            {
                return Vector3.right;
            }

            if (y <= z)
            {
                return Vector3.up;
            }

            return Vector3.forward;
        }

        private static long MakeRuntimeVertexKey(int sourceRenderVertexIndex, int chartId)
        {
            return ((long)sourceRenderVertexIndex << 32) | (uint)chartId;
        }

        private static long MakeChartOverlapKey(int newChartId, int oldChartId)
        {
            return ((long)newChartId << 32) | (uint)oldChartId;
        }

        private static void DecodeChartOverlapKey(long key, out int newChartId, out int oldChartId)
        {
            newChartId = (int)(key >> 32);
            oldChartId = (int)(uint)key;
        }

        private sealed class ChartData
        {
            public bool Initialized;
            public int OriginLogicalVertexId;
            public Vector3 LocalU;
            public Vector3 LocalV;
            public Vector3 ScaledU;
            public Vector3 ScaledV;
            public Vector3 ScaledOrigin;
            public float MinU;
            public float MaxU;
            public float MinV;
            public float MaxV;
        }
    }
}