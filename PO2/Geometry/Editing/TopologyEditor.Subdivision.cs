using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Localization;
using PO2.Materials;
using PO2.Persistence.Model;
using Point = PO2.Geometry.Editing.DissolvePolygon.Point;

namespace PO2.Geometry.Editing
{
    public static partial class TopologyEditor
    {
        internal static bool TryAnalyzeEdgeSubdivision(TopologyEditIndex index, IReadOnlyList<SubdivisionEdge> selected, CancellationToken token, out EdgeSubdivisionPlan plan, out LocalizedText reason)
        {
            plan = null;
            reason = LocalizedText.For("PO2.Edit.Selection.Required.EdgesInPart");
            if (index == null || selected == null || selected.Count == 0) return false;
            try
            {
                var source = index.Source;
                var mesh = index.Streams;
                bool converted = source.StructuralTopologySource != PO2.Geometry.Topology.StructuralTopologySource.Authored;
                if (!converted)
                    for (int t = 0; t < index.TriangleFaces.Length; t++)
                    {
                        if ((t & 4095) == 0) token.ThrowIfCancellationRequested();
                        if (index.TriangleFaces[t] < 0) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.StructuralFacesRequired"));
                    }

                var selectedEdges = new Dictionary<ulong, SubdivisionEdge>(TopologyEditIndex.EdgeComparer.s_Instance);
                var affected = new HashSet<int>();
                for (int i = 0; i < selected.Count; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    var edge = selected[i];
                    if (edge.Id < 0 || edge.A == edge.B || !index.Edges.TryGetValue(edge.Key, out var incidence))
                        throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.SelectionChanged"));
                    if (incidence.Count < 1 || incidence.Count > 2 || converted && incidence.Count == 2 && incidence.Direction != 0)
                        throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.NonManifoldSelection"));
                    selectedEdges[edge.Key] = edge;
                    AddAffected(index, affected, incidence.FirstTriangle);
                    if (incidence.Count == 2) AddAffected(index, affected, incidence.SecondTriangle);
                }

                var edges = new SubdivisionEdge[selectedEdges.Count];
                selectedEdges.Values.CopyTo(edges, 0);
                Array.Sort(edges, (a, b) => a.Id.CompareTo(b.Id));
                var areas = new Dictionary<int, PersistenceSurfaceAreaData>();
                foreach (var area in source.SurfaceAreas) areas.Add(area.Id, area);
                var regions = new List<EdgeSubdivisionPlan.Region>(affected.Count);
                var paired = converted ? PairSubdivisionTriangles(index, affected, selectedEdges, token) : null;
                var renderPairs = new HashSet<ulong>(TopologyEditIndex.EdgeComparer.s_Instance);
                long faceCount = source.StructuralFaces.Length, triangleCount = source.Triangles.Length / 3;
                int centers = 0;
                foreach (int id in affected)
                {
                    token.ThrowIfCancellationRequested();
                    var region = new EdgeSubdivisionPlan.Region { FaceId = id >= 0 ? id : -1, TriangleB = -1 };
                    if (paired != null && paired.TryGetValue(id, out var pair))
                    {
                        if (id != pair.FaceId) continue;
                        region = pair;
                    }
                    else if (id >= 0)
                    {
                        var face = source.StructuralFaces[id];
                        var mapping = index.FaceTriangles[id];
                        region.Corners = new int[face.CornerCount];
                        for (int c = 0; c < face.CornerCount; c++) region.Corners[c] = face.GetRenderVertexIndex(c);
                        region.TriangleA = mapping.TriangleA;
                        region.TriangleB = mapping.TriangleB;
                    }
                    else
                    {
                        region.TriangleA = ~id;
                        int at = region.TriangleA * 3;
                        region.Corners = new[] { source.Triangles[at], source.Triangles[at + 1], source.Triangles[at + 2] };
                    }

                    region.FirstTriangle = region.TriangleB < 0 ? region.TriangleA : Math.Min(region.TriangleA, region.TriangleB);
                    region.Area = source.TriangleSurfaceAreaIds[region.TriangleA];
                    if (!areas.TryGetValue(region.Area, out var area) || region.TriangleB >= 0 && source.TriangleSurfaceAreaIds[region.TriangleB] != region.Area)
                        throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.SurfaceAreaMembership"));
                    ValidateSubdivisionRegion(mesh, region, area, converted);
                    int count = region.Corners.Length, selectedCount = 0;
                    for (int c = 0; c < count; c++)
                    {
                        int a = region.Corners[c], b = region.Corners[(c + 1) % count];
                        if (!selectedEdges.ContainsKey(TopologyEditIndex.Key(source.RenderToLogical[a], source.RenderToLogical[b]))) continue;
                        region.Mask |= 1 << c;
                        selectedCount++;
                        if (!CanInsertSubdivisionMidpoint(mesh, a, b))
                            throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.EdgeDirections"));
                        renderPairs.Add(SubdivisionRenderPair(a, b));
                    }

                    if (selectedCount == 0) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.EdgeOutsideFace"));
                    region.QuadTemplate = count == 4 && (!converted || CanRetessellateConvertedQuad(mesh, region.Corners));
                    int resultTriangles, resultFaces;
                    if (region.QuadTemplate && region.Mask == 15)
                    {
                        if (!CanInsertSubdivisionMidpoint(mesh, region.Corners[0], region.Corners[2]))
                            throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.FaceCenterDirections"));
                        centers++;
                        resultFaces = 4;
                        resultTriangles = 8;
                    }
                    else if (region.QuadTemplate && (region.Mask == 5 || region.Mask == 10))
                    {
                        resultFaces = 2;
                        resultTriangles = 4;
                    }
                    else
                    {
                        resultTriangles = (count == 4 ? 2 : 1) + selectedCount;
                        resultFaces = resultTriangles;
                    }

                    faceCount += resultFaces - (id >= 0 ? 1 : 0) - (region.SecondFaceId >= 0 ? 1 : 0);
                    triangleCount += resultTriangles - (count == 4 ? 2 : 1);
                    regions.Add(region);
                }

                regions.Sort((a, b) => a.FirstTriangle.CompareTo(b.FirstTriangle));
                long newVertices = (long)source.Vertices.Length + renderPairs.Count + centers;
                long logical = (long)source.LogicalVertexCount + edges.Length + centers;
                if (newVertices > int.MaxValue || logical > int.MaxValue || faceCount > int.MaxValue || triangleCount > int.MaxValue / 3)
                    throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.MeshSizeLimit"));
                plan = new EdgeSubdivisionPlan(index, edges, regions.ToArray(), checked(renderPairs.Count + centers), centers, (int)faceCount, (int)triangleCount);
                reason = LocalizedText.Empty;
                return true;
            }
            catch (InvalidOperationException e)
            {
                plan = null;
                reason = LocalizedText.FromException(e);
                return false;
            }
            catch (OverflowException)
            {
                plan = null;
                reason = LocalizedText.For("PO2.Edit.Subdivide.Error.MeshSizeLimit");
                return false;
            }
        }

        private static void AddAffected(TopologyEditIndex index, HashSet<int> affected, int triangle)
        {
            int face = index.TriangleFaces[triangle];
            affected.Add(face >= 0 ? face : ~triangle);
        }

        private static Dictionary<int, EdgeSubdivisionPlan.Region> PairSubdivisionTriangles(TopologyEditIndex index, HashSet<int> affected, Dictionary<ulong, SubdivisionEdge> selected, CancellationToken token)
        {
            var result = new Dictionary<int, EdgeSubdivisionPlan.Region>();
            foreach (int face in affected)
            {
                token.ThrowIfCancellationRequested();
                if (face < 0 || index.Source.StructuralFaces[face].CornerCount != 3) continue;
                int partner = FindSubdivisionPartner(index, affected, selected, face, out var corners);
                if (partner <= face || FindSubdivisionPartner(index, affected, selected, partner, out _) != face) continue;
                // Only selection-local, mutually unambiguous pairs participate.
                // Existing quads and unselected diagonals elsewhere are unchanged.
                var region = new EdgeSubdivisionPlan.Region
                {
                    FaceId = face, SecondFaceId = partner, Corners = corners,
                    TriangleA = index.FaceTriangles[face].TriangleA,
                    TriangleB = index.FaceTriangles[partner].TriangleA
                };
                result.Add(face, region);
                result.Add(partner, region);
            }

            return result;
        }

        private static int FindSubdivisionPartner(TopologyEditIndex index, HashSet<int> affected, Dictionary<ulong, SubdivisionEdge> selected, int faceId, out int[] corners)
        {
            corners = null;
            int partner = -1;
            var source = index.Source;
            var face = source.StructuralFaces[faceId];
            int triangle = index.FaceTriangles[faceId].TriangleA;
            for (int c = 0; c < 3; c++)
            {
                int a = face.GetRenderVertexIndex(c), b = face.GetRenderVertexIndex((c + 1) % 3);
                ulong diagonal = TopologyEditIndex.Key(source.RenderToLogical[a], source.RenderToLogical[b]);
                if (selected.ContainsKey(diagonal) || !index.Edges.TryGetValue(diagonal, out var edge) || edge.Count != 2 || edge.Direction != 0) continue;
                int otherTriangle = edge.FirstTriangle == triangle ? edge.SecondTriangle : edge.FirstTriangle;
                int otherId = index.TriangleFaces[otherTriangle];
                if (otherId < 0 || otherId == faceId || !affected.Contains(otherId) || source.StructuralFaces[otherId].CornerCount != 3 || source.TriangleSurfaceAreaIds[triangle] != source.TriangleSurfaceAreaIds[otherTriangle]) continue;
                var other = source.StructuralFaces[otherId];
                for (int d = 0; d < 3; d++)
                {
                    // Exact render endpoints preserve seams and source streams.
                    if (other.GetRenderVertexIndex(d) != b || other.GetRenderVertexIndex((d + 1) % 3) != a) continue;
                    var quad = new[] { b, face.GetRenderVertexIndex((c + 2) % 3), a, other.GetRenderVertexIndex((d + 2) % 3) };
                    if (source.RenderToLogical[quad[1]] == source.RenderToLogical[quad[3]]) continue;
                    int mask = 0;
                    for (int i = 0; i < 4; i++)
                        if (selected.ContainsKey(TopologyEditIndex.Key(source.RenderToLogical[quad[i]], source.RenderToLogical[quad[(i + 1) % 4]])))
                            mask |= 1 << i;
                    if (mask != 5 && mask != 10 && mask != 15 || !CanRetessellateConvertedQuad(index.Streams, quad)) continue;
                    if (partner >= 0)
                    {
                        corners = null;
                        return -1;
                    }

                    partner = otherId;
                    corners = quad;
                }
            }

            return partner;
        }

        internal static MeshMutationResult CreateEdgeSubdivisionMutation(EdgeSubdivisionPlan plan, CancellationToken token, out SubdividedEdge[] children, out SubdivisionTriangleSource[] triangleSources)
        {
            var builder = new SubdivisionBuilder(plan, token);
            var result = builder.Build();
            children = builder.Children;
            triangleSources = builder.TriangleSources;
            return result;
        }

        private static ulong SubdivisionRenderPair(int a, int b)
        {
            return ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
        }

        private static bool SubdivisionFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool SubdivisionFinite(Vector3 value)
        {
            return SubdivisionFinite(value.x) && SubdivisionFinite(value.y) && SubdivisionFinite(value.z);
        }

        private static bool SubdivisionFinite(Vector4 value)
        {
            return SubdivisionFinite(value.x) && SubdivisionFinite(value.y) && SubdivisionFinite(value.z) && SubdivisionFinite(value.w);
        }

        private static Vector3 SubdivisionMidpoint(Vector3 a, Vector3 b)
        {
            // Half first so large finite endpoints cannot overflow when added.
            return a * 0.5f + b * 0.5f;
        }

        private static Vector4 SubdivisionMidpoint(Vector4 a, Vector4 b)
        {
            return a * 0.5f + b * 0.5f;
        }

        private static bool CanInsertSubdivisionMidpoint(MeshData mesh, int a, int b)
        {
            Vector3 midpoint = SubdivisionMidpoint(mesh.Vertices[a], mesh.Vertices[b]);
            if (!SubdivisionFinite(midpoint) || midpoint.Equals(mesh.Vertices[a]) || midpoint.Equals(mesh.Vertices[b])) return false;
            if (mesh.Normals != null)
            {
                Vector3 normal = SubdivisionMidpoint(mesh.Normals[a], mesh.Normals[b]);
                if (!SubdivisionFinite(normal) || normal.sqrMagnitude < 0.000000000001f) return false;
            }

            if (mesh.Tangents != null)
            {
                Vector4 tangent = SubdivisionMidpoint(mesh.Tangents[a], mesh.Tangents[b]);
                if (!SubdivisionFinite(tangent) || mesh.Tangents[a].w != mesh.Tangents[b].w || (tangent.w != -1f && tangent.w != 1f) || tangent.x * tangent.x + tangent.y * tangent.y + tangent.z * tangent.z < 0.000000000001f) return false;
            }

            return true;
        }


        private static void ValidateSubdivisionRegion(MeshData mesh, EdgeSubdivisionPlan.Region region, PersistenceSurfaceAreaData area, bool converted)
        {
            bool packed = !converted && VertexAttributeCapabilities.HasWindowVertexPayload(area.SourceVertexAttributes);
            if (converted && (area.NativeWindowKind != NativeWindowKind.None || area.Material.VanillaSpecialKind != VanillaSpecialMaterialKind.None))
                throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.NativePayloadUnsupported"));
            int first = region.Corners[0];
            foreach (int index in region.Corners)
            {
                if (!SubdivisionFinite(mesh.Vertices[index]) || mesh.Normals != null && !SubdivisionFinite(mesh.Normals[index]) || mesh.Tangents != null && !SubdivisionFinite(mesh.Tangents[index]) || mesh.UV0 != null && !SubdivisionFinite(mesh.UV0[index]))
                    throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.FaceGeometry"));
                if (mesh.Tangents != null && (mesh.Tangents[index].w != mesh.Tangents[first].w || mesh.Tangents[index].w != -1f && mesh.Tangents[index].w != 1f))
                    throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.TangentHandedness"));
                if (mesh.TexCoord1 != null && !SubdivisionBasis.SameBits(mesh.TexCoord1[index], mesh.TexCoord1[first]) || mesh.TexCoord3 != null && !SubdivisionBasis.SameBits(mesh.TexCoord3[index], mesh.TexCoord3[first]) || packed && (mesh.VertexColors != null && !mesh.VertexColors[index].Equals(mesh.VertexColors[first]) || mesh.UV0 != null && !mesh.UV0[index].Equals(mesh.UV0[first])))
                    throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.MixedNativePayload"));
            }

            int[] corners = region.Corners;
            var a = mesh.Vertices[corners[0]];
            var b = mesh.Vertices[corners[1]];
            var c = mesh.Vertices[corners[2]];
            var basis = new SubdivisionBasis(a, b, c);
            if (!basis.Valid || !converted && Vector3.Cross(b - a, c - a).sqrMagnitude <= kMergeTriangleCrossSqrEpsilon)
                throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.DegenerateFace"));
            if (corners.Length == 4)
            {
                var second = new SubdivisionBasis(a, c, mesh.Vertices[corners[3]]);
                if (!second.Valid || (basis.UnitNormal + second.UnitNormal).sqrMagnitude <= 0.000001f)
                    throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.FoldedQuad"));
            }
        }

        internal static bool CanRetessellateConvertedQuad(MeshData mesh, int[] corners)
        {
            int a = corners[0], b = corners[1], c = corners[2], d = corners[3];
            var basis = new SubdivisionBasis(mesh.Vertices[a], mesh.Vertices[b], mesh.Vertices[c]);
            if (!basis.Weights(mesh.Vertices[d], out double u, out double v)) return false;
            for (int i = 0; i < 4; i++)
            {
                var ab = Point.Local(mesh.Vertices[corners[(i + 1) % 4]], mesh.Vertices[corners[i]]);
                var ac = Point.Local(mesh.Vertices[corners[(i + 2) % 4]], mesh.Vertices[corners[i]]);
                var cross = Point.Cross(ab, ac);
                var normal = basis.UnitNormal;
                if (cross.LengthSquared <= basis.ExtentSquared * basis.ExtentSquared * 1e-20 || cross.X * normal.x + cross.Y * normal.y + cross.Z * normal.z <= 0) return false;
            }

            if (mesh.Normals != null && !SubdivisionBasis.Same(mesh.Normals[a], mesh.Normals[b], mesh.Normals[c], mesh.Normals[d], u, v) || mesh.Tangents != null && !SubdivisionBasis.Same(mesh.Tangents[a], mesh.Tangents[b], mesh.Tangents[c], mesh.Tangents[d], u, v) || mesh.UV0 != null && !SubdivisionBasis.Same(mesh.UV0[a], mesh.UV0[b], mesh.UV0[c], mesh.UV0[d], u, v)) return false;
            if (mesh.VertexColors != null)
            {
                var x = mesh.VertexColors[a];
                var y = mesh.VertexColors[b];
                var z = mesh.VertexColors[c];
                var w = mesh.VertexColors[d];
                if (!SubdivisionBasis.Same(x.r, y.r, z.r, w.r, u, v, 0.5) || !SubdivisionBasis.Same(x.g, y.g, z.g, w.g, u, v, 0.5) || !SubdivisionBasis.Same(x.b, y.b, z.b, w.b, u, v, 0.5) || !SubdivisionBasis.Same(x.a, y.a, z.a, w.a, u, v, 0.5)) return false;
            }

            return true;
        }

        private sealed class SubdivisionBuilder
        {
            private readonly EdgeSubdivisionPlan m_Plan;
            private readonly PersistencePartData m_Source;
            private readonly CancellationToken m_Token;
            private readonly List<Vector3> m_Vertices, m_Normals;
            private readonly List<Vector4> m_Tangents, m_UV0, m_TexCoord1, m_TexCoord3;
            private readonly List<Color32> m_Colors;
            private readonly List<int> m_LogicalIds;
            private readonly List<StructuralFace> m_Faces = new List<StructuralFace>();
            private readonly List<int> m_Triangles = new List<int>();
            private readonly List<SubdivisionTriangleSource> m_Parents = new List<SubdivisionTriangleSource>();
            private readonly Dictionary<ulong, int> m_MidpointLogicalIds, m_RenderMidpoints;
            private int m_NextLogicalId, m_ParentTriangle, m_ParentQuadTriangle;
            private Vector3 m_SourceFaceNormal;
            private double m_AreaEpsilon;
            internal readonly SubdividedEdge[] Children;
            internal SubdivisionTriangleSource[] TriangleSources;

            internal SubdivisionBuilder(EdgeSubdivisionPlan plan, CancellationToken token)
            {
                m_Plan = plan;
                m_Source = plan.Source;
                m_Token = token;
                int capacity = checked(m_Source.Vertices.Length + plan.AddedRenderVertexCount);
                m_Vertices = CopyStream(m_Source.Vertices, capacity);
                m_Normals = CopyStream(m_Source.Normals, capacity);
                m_Tangents = CopyStream(m_Source.Tangents, capacity);
                m_UV0 = CopyStream(m_Source.UV0, capacity);
                m_Colors = CopyStream(m_Source.VertexColors, capacity);
                m_TexCoord1 = CopyStream(m_Source.TexCoord1, capacity);
                m_TexCoord3 = CopyStream(m_Source.TexCoord3, capacity);
                m_LogicalIds = CopyStream(m_Source.RenderToLogical, capacity);
                m_MidpointLogicalIds = new Dictionary<ulong, int>(plan.Edges.Length, TopologyEditIndex.EdgeComparer.s_Instance);
                m_RenderMidpoints = new Dictionary<ulong, int>(plan.AddedRenderVertexCount, TopologyEditIndex.EdgeComparer.s_Instance);
                m_NextLogicalId = m_Source.LogicalVertexCount;
                Children = new SubdividedEdge[plan.Edges.Length];
                for (int i = 0; i < plan.Edges.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    var edge = plan.Edges[i];
                    int midpoint = m_NextLogicalId++;
                    m_MidpointLogicalIds.Add(edge.Key, midpoint);
                    Children[i] = new SubdividedEdge(edge.A, edge.B, midpoint);
                }
            }

            private List<T> CopyStream<T>(T[] source, int capacity)
            {
                m_Token.ThrowIfCancellationRequested();
                if (source == null) return null;
                var result = new List<T>(capacity);
                result.AddRange(source);
                return result;
            }

            internal MeshMutationResult Build()
            {
                // Regions are built in stored-face order so authored midpoint
                // ordering stays compatible with the existing templates.
                var buildOrder = new EdgeSubdivisionPlan.Region[m_Plan.Regions.Length];
                for (int i = 0; i < buildOrder.Length; i++)
                {
                    if ((i & 4095) == 0) m_Token.ThrowIfCancellationRequested();
                    buildOrder[i] = m_Plan.Regions[i].CopyForBuild();
                }

                Array.Sort(buildOrder, (a, b) =>
                {
                    if (a.FaceId >= 0 && b.FaceId >= 0) return a.FaceId.CompareTo(b.FaceId);
                    if (a.FaceId >= 0) return -1;
                    if (b.FaceId >= 0) return 1;
                    return a.FirstTriangle.CompareTo(b.FirstTriangle);
                });
                foreach (var region in buildOrder)
                {
                    m_Token.ThrowIfCancellationRequested();
                    region.FaceStart = m_Faces.Count;
                    region.OutputStart = m_Parents.Count;
                    BuildRegion(region);
                    region.FaceCount = m_Faces.Count - region.FaceStart;
                    region.OutputCount = m_Parents.Count - region.OutputStart;
                }

                var byFace = new Dictionary<int, EdgeSubdivisionPlan.Region>();
                var byTriangle = new Dictionary<int, EdgeSubdivisionPlan.Region>();
                foreach (var region in buildOrder)
                {
                    if (region.FaceId >= 0) byFace.Add(region.FaceId, region);
                    if (region.SecondFaceId >= 0) byFace.Add(region.SecondFaceId, region);
                    byTriangle.Add(region.TriangleA, region);
                    if (region.TriangleB >= 0) byTriangle.Add(region.TriangleB, region);
                }

                var faces = new List<StructuralFace>(m_Plan.ResultFaceCount);
                for (int f = 0; f < m_Source.StructuralFaces.Length; f++)
                {
                    if ((f & 4095) == 0) m_Token.ThrowIfCancellationRequested();
                    if (byFace.TryGetValue(f, out var region))
                    {
                        if (f == region.FaceId) CopyFaces(region, faces);
                    }
                    else faces.Add(m_Source.StructuralFaces[f].CloneWithId(faces.Count));
                }

                foreach (var region in buildOrder)
                    if (region.FaceId < 0)
                        CopyFaces(region, faces);
                var triangles = new int[checked(m_Plan.ResultTriangleCount * 3)];
                var areas = new int[m_Plan.ResultTriangleCount];
                TriangleSources = new SubdivisionTriangleSource[areas.Length];
                int output = 0;
                for (int t = 0; t < m_Source.Triangles.Length / 3; t++)
                {
                    if ((t & 4095) == 0) m_Token.ThrowIfCancellationRequested();
                    if (byTriangle.TryGetValue(t, out var region))
                    {
                        if (region.FirstTriangle != t) continue;
                        m_Triangles.CopyTo(region.OutputStart * 3, triangles, output * 3, region.OutputCount * 3);
                        for (int n = 0; n < region.OutputCount; n++)
                        {
                            areas[output] = region.Area;
                            TriangleSources[output++] = m_Parents[region.OutputStart + n];
                        }
                    }
                    else
                    {
                        Array.Copy(m_Source.Triangles, t * 3, triangles, output * 3, 3);
                        areas[output] = m_Source.TriangleSurfaceAreaIds[t];
                        TriangleSources[output++] = new SubdivisionTriangleSource(t);
                    }
                }

                if (output != areas.Length || faces.Count != m_Plan.ResultFaceCount || m_Vertices.Count != m_Source.Vertices.Length + m_Plan.AddedRenderVertexCount)
                    throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.PlanMismatch"));
                ValidateNewEdges(triangles);
                var result = new MeshMutationResult(m_Vertices.ToArray(), m_Normals?.ToArray(), m_Tangents?.ToArray(), m_UV0?.ToArray(), m_Colors?.ToArray(), m_TexCoord1?.ToArray(), m_TexCoord3?.ToArray(), triangles, faces.ToArray(), areas);
                result.SetLogicalVertexMap(m_LogicalIds.ToArray(), m_NextLogicalId);
                result.SetHistoryVertexMap(m_Source.Vertices.Length, null);
                return result;
            }

            private void CopyFaces(EdgeSubdivisionPlan.Region region, List<StructuralFace> output)
            {
                for (int i = 0; i < region.FaceCount; i++) output.Add(m_Faces[region.FaceStart + i].CloneWithId(output.Count));
            }

            private void SetNormal(int a, int b, int c)
            {
                var basis = new SubdivisionBasis(m_Vertices[a], m_Vertices[b], m_Vertices[c]);
                m_SourceFaceNormal = basis.UnitNormal;
                m_AreaEpsilon = basis.ExtentSquared * basis.ExtentSquared * 1e-20;
            }

            private void BuildRegion(EdgeSubdivisionPlan.Region region)
            {
                var corners = region.Corners;
                int count = corners.Length, mask = region.Mask;
                int[] midpoints = { -1, -1, -1, -1 };
                for (int i = 0; i < count; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    int a = corners[i], b = corners[(i + 1) % count];
                    ulong pair = SubdivisionRenderPair(a, b);
                    if (!m_RenderMidpoints.TryGetValue(pair, out int midpoint))
                    {
                        midpoint = AppendMidpoint(a, b, m_MidpointLogicalIds[TopologyEditIndex.Key(m_LogicalIds[a], m_LogicalIds[b])]);
                        m_RenderMidpoints.Add(pair, midpoint);
                    }

                    midpoints[i] = midpoint;
                }

                SetNormal(corners[0], corners[1], corners[2]);
                m_ParentTriangle = region.TriangleA;
                m_ParentQuadTriangle = region.QuadTemplate ? region.TriangleB : -1;
                if (count == 4 && !m_Plan.Converted) m_SourceFaceNormal += new SubdivisionBasis(m_Vertices[corners[0]], m_Vertices[corners[2]], m_Vertices[corners[3]]).UnitNormal;
                bool success;
                if (region.QuadTemplate && mask == 15)
                {
                    int center = AppendMidpoint(corners[0], corners[2], m_NextLogicalId++);
                    success = true;
                    for (int i = 0; i < 4 && success; i++) success = AddQuad(corners[i], midpoints[i], center, midpoints[(i + 3) % 4]);
                }
                else if (region.QuadTemplate && (mask == 5 || mask == 10))
                {
                    int start = mask == 5 ? 0 : 1, next = (start + 1) % 4, opposite = (start + 2) % 4, last = (start + 3) % 4;
                    success = AddQuad(corners[start], midpoints[start], midpoints[opposite], corners[last]) && AddQuad(midpoints[start], corners[next], corners[opposite], midpoints[opposite]);
                }
                else if (region.QuadTemplate && (mask & (mask - 1)) == 0)
                {
                    int start = 0;
                    while ((mask & (1 << start)) == 0) start++;
                    int midpoint = midpoints[start], next = (start + 1) % 4, opposite = (start + 2) % 4, last = (start + 3) % 4;
                    success = AddTriangle(midpoint, corners[next], corners[opposite]) && AddTriangle(midpoint, corners[opposite], corners[last]) && AddTriangle(midpoint, corners[last], corners[start]);
                }
                else if (count == 4)
                {
                    // Keeping the source diagonal preserves folded geometry and
                    // piecewise interpolation when a converted quad cannot retessellate.
                    m_ParentQuadTriangle = -1;
                    success = SplitTriangle(corners[0], corners[1], corners[2], midpoints[0], midpoints[1], -1);
                    m_ParentTriangle = region.TriangleB;
                    if (m_Plan.Converted) SetNormal(corners[0], corners[2], corners[3]);
                    success = success && SplitTriangle(corners[0], corners[2], corners[3], -1, midpoints[2], midpoints[3]);
                }
                else success = SplitTriangle(corners[0], corners[1], corners[2], midpoints[0], midpoints[1], midpoints[2]);

                if (!success) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.InvertedResult"));
            }

            private int AppendMidpoint(int a, int b, int logicalId)
            {
                int index = m_Vertices.Count;
                m_Vertices.Add(SubdivisionMidpoint(m_Vertices[a], m_Vertices[b]));
                m_LogicalIds.Add(logicalId);
                if (m_Normals != null) m_Normals.Add(SubdivisionMidpoint(m_Normals[a], m_Normals[b]));
                if (m_Tangents != null) m_Tangents.Add(SubdivisionMidpoint(m_Tangents[a], m_Tangents[b]));
                if (m_UV0 != null) m_UV0.Add(SubdivisionMidpoint(m_UV0[a], m_UV0[b]));
                if (m_Colors != null) m_Colors.Add((Color32)Color.Lerp(m_Colors[a], m_Colors[b], 0.5f));
                if (m_TexCoord1 != null) m_TexCoord1.Add(m_TexCoord1[a]);
                if (m_TexCoord3 != null) m_TexCoord3.Add(m_TexCoord3[a]);
                return index;
            }

            private bool SplitTriangle(int a, int b, int c, int ab, int bc, int ca)
            {
                int count = (ab >= 0 ? 1 : 0) + (bc >= 0 ? 1 : 0) + (ca >= 0 ? 1 : 0);
                if (count == 0) return AddTriangle(a, b, c);
                if (count == 3)
                {
                    return AddTriangle(a, ab, ca) && AddTriangle(b, bc, ab) && AddTriangle(c, ca, bc) && AddTriangle(ab, bc, ca);
                }

                // Rotate into one template: AB for one split, AB + BC for two.
                if ((count == 1 && bc >= 0) || (count == 2 && ab < 0))
                {
                    return SplitTriangle(b, c, a, bc, ca, ab);
                }

                if ((count == 1 && ca >= 0) || (count == 2 && bc < 0))
                {
                    return SplitTriangle(c, a, b, ca, ab, bc);
                }

                if (count == 1) return AddTriangle(a, ab, c) && AddTriangle(ab, b, c);
                return AddTriangle(b, bc, ab) && AddTriangle(a, ab, bc) && AddTriangle(a, bc, c);
            }


            private bool ValidTriangle(int a, int b, int c)
            {
                var cross = Point.Cross(Point.Local(m_Vertices[b], m_Vertices[a]), Point.Local(m_Vertices[c], m_Vertices[a]));
                double squared = cross.LengthSquared;
                return !double.IsNaN(squared) && !double.IsInfinity(squared) && squared > (m_Plan.Converted ? m_AreaEpsilon : kMergeTriangleCrossSqrEpsilon) && cross.X * m_SourceFaceNormal.x + cross.Y * m_SourceFaceNormal.y + cross.Z * m_SourceFaceNormal.z > 0;
            }

            private bool AddTriangle(int a, int b, int c)
            {
                if (!ValidTriangle(a, b, c)) return false;
                m_Faces.Add(new StructuralFace(m_Faces.Count, a, b, c));
                AppendTriangle(a, b, c);
                return true;
            }

            private bool AddQuad(int a, int b, int c, int d)
            {
                if (!ValidTriangle(a, b, c) || !ValidTriangle(a, c, d)) return false;
                m_Faces.Add(new StructuralFace(m_Faces.Count, a, b, c, d));
                AppendTriangle(a, b, c);
                AppendTriangle(a, c, d);
                return true;
            }

            private void AppendTriangle(int a, int b, int c)
            {
                m_Triangles.Add(a);
                m_Triangles.Add(b);
                m_Triangles.Add(c);
                m_Parents.Add(new SubdivisionTriangleSource(m_ParentTriangle, m_ParentQuadTriangle));
            }

            private void ValidateNewEdges(int[] triangles)
            {
                var edges = new Dictionary<ulong, TopologyEditIndex.Edge>(TopologyEditIndex.EdgeComparer.s_Instance);
                for (int t = 0; t < triangles.Length / 3; t++)
                {
                    if ((t & 4095) == 0) m_Token.ThrowIfCancellationRequested();
                    for (int c = 0; c < 3; c++)
                    {
                        int a = m_LogicalIds[triangles[t * 3 + c]], b = m_LogicalIds[triangles[t * 3 + (c + 1) % 3]];
                        if (a < m_Source.LogicalVertexCount && b < m_Source.LogicalVertexCount) continue;
                        ulong key = TopologyEditIndex.Key(a, b);
                        edges.TryGetValue(key, out var edge);
                        edge.Add(a, b, t);
                        edges[key] = edge;
                        if (a == b || edge.Count > 2 || edge.Count == 2 && edge.Direction != 0)
                            throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.NonManifoldResult"));
                    }
                }

                foreach (var child in Children)
                {
                    if (!edges.ContainsKey(TopologyEditIndex.Key(child.LogicalA, child.LogicalMidpoint)) || !edges.ContainsKey(TopologyEditIndex.Key(child.LogicalMidpoint, child.LogicalB)))
                        throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Subdivide.Error.ChildEdgeMissing"));
                }
            }
        }
    }
}