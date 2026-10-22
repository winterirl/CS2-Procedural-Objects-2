using System;
using System.Threading;
using UnityEngine;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Persistence.Model;

namespace PO2.Rendering
{
    internal sealed class SubdivisionAppearanceCheck : ITopologyAppearanceCheck
    {
        private readonly EdgeSubdivisionPlan m_Plan;
        private readonly SubdivisionTriangleSource[] m_Sources;

        internal SubdivisionAppearanceCheck(EdgeSubdivisionPlan plan, SubdivisionTriangleSource[] sources)
        {
            m_Plan = plan;
            m_Sources = sources;
        }

        public void Validate(PersistencePartData before, int[] oldToNew, ITopologyRenderData data, CancellationToken token)
        {
            var candidate = (TopologyRendererPreparation)data;
            var groups = TopologyRendererWork.GroupTriangles(before.TriangleSurfaceAreaIds, token);
            var local = new int[before.Triangles.Length / 3];
            foreach (var group in groups.Values)
                for (int i = 0; i < group.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    local[group[i]] = i;
                }

            var affected = new bool[local.Length];
            foreach (var region in m_Plan.Regions)
            {
                affected[region.TriangleA] = true;
                if (region.TriangleB >= 0) affected[region.TriangleB] = true;
            }

            var checkedQuads = new bool[local.Length];
            foreach (var area in candidate.Areas)
            {
                token.ThrowIfCancellationRequested();
                var current = area.Settings.Current;
                var rendered = area.Settings.Isolated ? area.Runtime : candidate.Combined;
                int offset = area.Settings.Isolated ? 0 : area.CombinedOffset;
                if (current == null || !groups.TryGetValue(area.Settings.Id, out var oldTriangles) || current.Triangles.Length != oldTriangles.Length * 3)
                    throw new InvalidOperationException("The current renderer layout changed during subdivision preparation.");
                for (int t = 0; t < area.SourceTriangleIds.Length; t++)
                {
                    if ((t & 4095) == 0) token.ThrowIfCancellationRequested();
                    var parent = m_Sources[area.SourceTriangleIds[t]];
                    int oldTriangle = local[parent.Triangle];
                    if (affected[parent.Triangle] && area.Settings.UnsupportedInterpolation)
                        throw new InvalidOperationException("Subdivision does not yet support this native shader's vertex payload.");
                    if (parent.QuadTriangle >= 0 && !checkedQuads[parent.Triangle])
                    {
                        ValidateQuad(current, oldTriangle, local[parent.QuadTriangle], area.Settings.RequiresTangents);
                        checkedQuads[parent.Triangle] = true;
                    }

                    if (!affected[parent.Triangle])
                    {
                        // Untouched records can contain degenerate triangles or
                        // special payloads. They need no interpolation at all.
                        for (int c = 0; c < 3; c++)
                        {
                            int a = current.Triangles[oldTriangle * 3 + c], b = area.Runtime.Triangles[t * 3 + c] + offset;
                            if (!SameVertex(current, a, rendered, b, area.Settings.RequiresTangents))
                                throw new InvalidOperationException("Subdivision would change an untouched rendered region.");
                        }

                        continue;
                    }

                    int x = current.Triangles[oldTriangle * 3], y = current.Triangles[oldTriangle * 3 + 1], z = current.Triangles[oldTriangle * 3 + 2];
                    var basis = new SubdivisionBasis(current.Vertices[x], current.Vertices[y], current.Vertices[z]);
                    for (int c = 0; c < 3; c++)
                    {
                        int next = area.Runtime.Triangles[t * 3 + c] + offset;
                        if (!basis.Weights(rendered.Vertices[next], out double u, out double v) || parent.QuadTriangle < 0 && (u < -1e-5 || v < -1e-5 || u + v > 1.00001) || !SameSample(current, x, y, z, rendered, next, u, v, area.Settings.RequiresTangents))
                            throw new InvalidOperationException("Subdivision would shift a generated UV chart or change rendered shading.");
                    }
                }
            }
        }

        private static void ValidateQuad(MeshData mesh, int first, int second, bool tangents)
        {
            int a = mesh.Triangles[first * 3], b = mesh.Triangles[first * 3 + 1], c = mesh.Triangles[first * 3 + 2];
            var basis = new SubdivisionBasis(mesh.Vertices[a], mesh.Vertices[b], mesh.Vertices[c]);
            for (int i = 0; i < 3; i++)
            {
                int d = mesh.Triangles[second * 3 + i];
                if (!basis.Weights(mesh.Vertices[d], out double u, out double v) || !SameSample(mesh, a, b, c, mesh, d, u, v, tangents))
                    throw new InvalidOperationException("This inferred quad crosses a rendered texture or shading boundary.");
            }
        }

        private static bool SameVertex(MeshData a, int i, MeshData b, int j, bool tangents) =>
            a.Vertices[i].Equals(b.Vertices[j]) && SameSample(a, i, i, i, b, j, 0, 0, tangents);

        private static bool SameSample(MeshData a, int x, int y, int z, MeshData b, int i, double u, double v, bool tangents)
        {
            if ((a.Normals == null) != (b.Normals == null) || (a.UV0 == null) != (b.UV0 == null) || (a.VertexColors == null) != (b.VertexColors == null) || (a.TexCoord1 == null) != (b.TexCoord1 == null) || (a.TexCoord3 == null) != (b.TexCoord3 == null)) return false;
            if (a.Normals != null && !SubdivisionBasis.Same(a.Normals[x], a.Normals[y], a.Normals[z], b.Normals[i], u, v) || a.UV0 != null && !SubdivisionBasis.Same(a.UV0[x], a.UV0[y], a.UV0[z], b.UV0[i], u, v)) return false;
            if (tangents && ((a.Tangents == null) != (b.Tangents == null) || a.Tangents != null && !SubdivisionBasis.Same(a.Tangents[x], a.Tangents[y], a.Tangents[z], b.Tangents[i], u, v))) return false;
            if (a.TexCoord1 != null && !SubdivisionBasis.SameBits(a.TexCoord1[x], b.TexCoord1[i])) return false;
            if (a.TexCoord3 != null && !SubdivisionBasis.SameBits(a.TexCoord3[x], b.TexCoord3[i])) return false;
            if (a.VertexColors != null)
            {
                var w = a.VertexColors[x];
                var n = a.VertexColors[y];
                var p = a.VertexColors[z];
                var q = b.VertexColors[i];
                if (!SubdivisionBasis.Same(w.r, n.r, p.r, q.r, u, v, 0.51) || !SubdivisionBasis.Same(w.g, n.g, p.g, q.g, u, v, 0.51) || !SubdivisionBasis.Same(w.b, n.b, p.b, q.b, u, v, 0.51) || !SubdivisionBasis.Same(w.a, n.a, p.a, q.a, u, v, 0.51)) return false;
            }

            return true;
        }
    }
}