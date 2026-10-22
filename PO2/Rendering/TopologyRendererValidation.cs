using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Persistence.Model;
using Point = PO2.Geometry.Editing.DissolvePolygon.Point;

namespace PO2.Rendering
{
    internal sealed class DissolveAppearanceCheck : ITopologyAppearanceCheck
    {
        private readonly VertexDissolvePlan m_Plan;

        internal DissolveAppearanceCheck(VertexDissolvePlan plan)
        {
            m_Plan = plan;
        }

        public void Validate(PersistencePartData before, int[] oldToNew, ITopologyRenderData candidate, CancellationToken token) =>
            TopologyRendererValidation.ValidateDissolve(before, m_Plan, oldToNew, (TopologyRendererPreparation)candidate, token);
    }

    internal static class TopologyRendererValidation
    {
        internal static bool SameTangent(Vector4 a, Vector4 b) => a.w == b.w && Math.Abs((double)a.x - b.x) <= 1e-4 && Math.Abs((double)a.y - b.y) <= 1e-4 && Math.Abs((double)a.z - b.z) <= 1e-4;

        internal static void ValidateDissolve(PersistencePartData before, VertexDissolvePlan plan, int[] oldToNew, TopologyRendererPreparation candidate, CancellationToken token)
        {
            var oldGroups = TopologyRendererWork.GroupTriangles(before.TriangleSurfaceAreaIds, token);
            var oldLocal = new int[before.TriangleSurfaceAreaIds.Length];
            foreach (var group in oldGroups.Values)
                for (int i = 0; i < group.Length; i++)
                    oldLocal[group[i]] = i;
            var newToOld = new int[plan.Removal.VertexCount];
            for (int i = 0; i < oldToNew.Length; i++)
                if (oldToNew[i] >= 0)
                    newToOld[oldToNew[i]] = i;
            var oldTriangleForNew = new int[plan.Removal.TriangleCount];
            var patchForNew = new int[plan.Removal.TriangleCount];
            int cursor = 0, nextPatch = 0;
            for (int t = 0; t < plan.Removal.RemovedTriangles.Length; t++)
            {
                if ((t & 4095) == 0) token.ThrowIfCancellationRequested();
                if (nextPatch < plan.Patches.Length && plan.Patches[nextPatch].FirstTriangle == t)
                {
                    var patch = plan.Patches[nextPatch];
                    for (int i = 0; i < patch.Triangles.Length / 3; i++)
                    {
                        oldTriangleForNew[cursor] = -1;
                        patchForNew[cursor++] = nextPatch;
                    }

                    nextPatch++;
                }

                if (!plan.Removal.RemovedTriangles[t])
                {
                    oldTriangleForNew[cursor] = t;
                    patchForNew[cursor++] = -1;
                }
            }

            var patchSamples = new Dictionary<int, int>[plan.Patches.Length];
            var patchTolerance = new double[plan.Patches.Length];
            var work = new DissolvePolygon.Work(token);
            foreach (var area in candidate.Areas)
            {
                token.ThrowIfCancellationRequested();
                var current = area.Settings.Current;
                if (current == null || !oldGroups.TryGetValue(area.Settings.Id, out var oldTriangles) || current.Triangles.Length != oldTriangles.Length * 3)
                    throw new InvalidOperationException("The current renderer layout changed during dissolve preparation.");
                for (int p = 0; p < plan.Patches.Length; p++)
                {
                    var patch = plan.Patches[p];
                    if (patch.AreaId != area.Settings.Id) continue;
                    if (area.Settings.UnsupportedInterpolation) throw new InvalidOperationException("Dissolve does not yet support this native shader's vertex payload.");
                    int[] localTriangles = new int[patch.RemovedTriangles.Length];
                    var samples = new Dictionary<int, int>();
                    double maximumArea = 0;
                    int basis = -1;
                    for (int i = 0; i < localTriangles.Length; i++)
                    {
                        int t = oldLocal[patch.RemovedTriangles[i]];
                        localTriangles[i] = t;
                        int a = current.Triangles[t * 3], b = current.Triangles[t * 3 + 1], c = current.Triangles[t * 3 + 2];
                        Point ab = Point.Local(current.Vertices[b], current.Vertices[a]), ac = Point.Local(current.Vertices[c], current.Vertices[a]);
                        double cross = Point.Cross(ab, ac).LengthSquared;
                        if (cross > maximumArea)
                        {
                            maximumArea = cross;
                            basis = t;
                        }

                        for (int corner = 0; corner < 3; corner++)
                        {
                            int r = current.Triangles[t * 3 + corner];
                            samples[area.Settings.RuntimeToPart[r]] = r;
                        }
                    }

                    if (basis < 0) throw new InvalidOperationException("The current rendered patch has no stable plane.");
                    int baseRender = current.Triangles[basis * 3];
                    Vector3 origin = current.Vertices[baseRender];
                    Point normal = Point.Cross(Point.Local(current.Vertices[current.Triangles[basis * 3 + 1]], origin), Point.Local(current.Vertices[current.Triangles[basis * 3 + 2]], origin));
                    int drop = Math.Abs(normal.X) >= Math.Abs(normal.Y) && Math.Abs(normal.X) >= Math.Abs(normal.Z) ? 0 : Math.Abs(normal.Y) >= Math.Abs(normal.Z) ? 1 : 2;
                    Func<int, Point> project = r =>
                    {
                        Point point = Point.Local(current.Vertices[r], origin);
                        return drop == 0 ? new Point(point.Y, point.Z) : drop == 1 ? new Point(point.X, point.Z) : new Point(point.X, point.Y);
                    };
                    TopologyEditor.ValidateDissolveAttributes(current, before.SurfaceAreas, localTriangles, patch.AreaId, basis, project, work);
                    patchSamples[p] = samples;
                    patchTolerance[p] = UVTolerance(current, localTriangles);
                }

                for (int t = 0; t < area.SourceTriangleIds.Length; t++)
                {
                    if ((t & 4095) == 0) token.ThrowIfCancellationRequested();
                    int newGlobal = area.SourceTriangleIds[t], oldGlobal = oldTriangleForNew[newGlobal], patchId = patchForNew[newGlobal];
                    double tolerance = oldGlobal >= 0 ? TriangleUVTolerance(current, oldLocal[oldGlobal]) : patchTolerance[patchId];
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int newRender = area.Runtime.Triangles[t * 3 + corner];
                        int oldRender;
                        if (oldGlobal >= 0) oldRender = current.Triangles[oldLocal[oldGlobal] * 3 + corner];
                        else if (!patchSamples[patchId].TryGetValue(newToOld[area.RuntimeToPart[newRender]], out oldRender))
                            throw new InvalidOperationException("The replacement renderer lost a boundary vertex.");
                        if (!SameUV(current.UV0, oldRender, area.Runtime.UV0, newRender, tolerance))
                            throw new InvalidOperationException("Dissolve would shift a generated UV chart or distort the texture.");
                        if (current.Normals != null && area.Runtime.Normals != null && (Point.Local(current.Normals[oldRender], area.Runtime.Normals[newRender])).LengthSquared > 1e-10)
                            throw new InvalidOperationException("Dissolve would change rendered normals.");
                        if (area.Settings.RequiresTangents && current.Tangents != null && area.Runtime.Tangents != null && !SameTangent(current.Tangents[oldRender], area.Runtime.Tangents[newRender]))
                            throw new InvalidOperationException("Dissolve would change the rendered tangent basis.");
                    }
                }
            }
        }

        internal static void ValidateRestoredArea(MeshData current, MeshData restored, CancellationToken token)
        {
            if (current.Triangles.Length != restored.Triangles.Length || current.Tangents == null || restored.Tangents == null)
                throw new InvalidOperationException("An emptied area cannot yet be restored with the same tangent layout.");
            for (int t = 0; t < current.Triangles.Length / 3; t++)
            {
                if ((t & 4095) == 0) token.ThrowIfCancellationRequested();
                double tolerance = TriangleUVTolerance(current, t);
                for (int c = 0; c < 3; c++)
                {
                    int oldIndex = current.Triangles[t * 3 + c], newIndex = restored.Triangles[t * 3 + c];
                    if (!SameUV(current.UV0, oldIndex, restored.UV0, newIndex, tolerance) || !SameTangent(current.Tangents[oldIndex], restored.Tangents[newIndex]))
                        throw new InvalidOperationException("An emptied area's generated mapping cannot yet be restored unchanged.");
                }
            }
        }

        private static bool SameUV(Vector4[] a, int i, Vector4[] b, int j, double tolerance)
        {
            Vector4 left = a == null ? Vector4.zero : a[i], right = b == null ? Vector4.zero : b[j];
            return Math.Abs((double)left.x - right.x) <= tolerance && Math.Abs((double)left.y - right.y) <= tolerance && Math.Abs((double)left.z - right.z) <= tolerance && Math.Abs((double)left.w - right.w) <= tolerance;
        }

        private static double TriangleUVTolerance(MeshData mesh, int triangle)
        {
            if (mesh.UV0 == null) return 1e-7;
            var a = mesh.UV0[mesh.Triangles[triangle * 3]];
            double range = 0;
            for (int c = 1; c < 3; c++)
            {
                var b = mesh.UV0[mesh.Triangles[triangle * 3 + c]];
                range = Math.Max(range, Math.Max(Math.Abs((double)a.x - b.x), Math.Abs((double)a.y - b.y)));
            }

            return Math.Max(1e-7, range * 1e-5);
        }

        private static double UVTolerance(MeshData mesh, int[] triangles)
        {
            double tolerance = 1e-7;
            foreach (int t in triangles) tolerance = Math.Max(tolerance, TriangleUVTolerance(mesh, t));
            return tolerance;
        }
    }
}