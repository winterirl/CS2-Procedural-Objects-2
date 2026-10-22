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
using Patch = PO2.Geometry.Editing.VertexDissolvePlan.Patch;

namespace PO2.Geometry.Editing
{
    public static partial class TopologyEditor
    {
        internal static bool TryAnalyzeVertexDissolution(TopologyEditIndex index, int[] selected, CancellationToken token, out VertexDissolvePlan plan, out LocalizedText reason)
        {
            plan = null;
            reason = LocalizedText.For("PO2.Edit.Selection.Required.VerticesInPart");
            if (index == null || selected == null || selected.Length == 0) return false;
            try
            {
                var source = index.Source;
                var targets = new HashSet<int>(selected);
                var ids = new int[targets.Count];
                targets.CopyTo(ids);
                Array.Sort(ids);
                var patches = new Patch[ids.Length];
                var removedTriangles = new bool[source.Triangles.Length / 3];
                var removedFaces = new bool[source.StructuralFaces.Length];
                var work = new DissolvePolygon.Work(token);
                int triangleCount = removedTriangles.Length, faceCount = removedFaces.Length;
                for (int i = 0; i < ids.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var patch = BuildDissolvePatch(index, ids[i], targets, work);
                    patches[i] = patch;
                    foreach (int t in patch.RemovedTriangles)
                    {
                        if (removedTriangles[t]) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.OverlappingPatches"));
                        removedTriangles[t] = true;
                        triangleCount--;
                        int face = index.TriangleFaces[t];
                        if (face >= 0 && !removedFaces[face])
                        {
                            removedFaces[face] = true;
                            faceCount--;
                        }
                    }

                    triangleCount += patch.Triangles.Length / 3;
                    faceCount += patch.IsQuad ? 1 : patch.Triangles.Length / 3;
                }

                Array.Sort(patches, (a, b) => a.FirstTriangle.CompareTo(b.FirstTriangle));
                ValidateDissolveEdges(index, patches, removedTriangles, token);
                var usedBefore = new bool[source.Vertices.Length];
                var usedAfter = new bool[usedBefore.Length];
                for (int t = 0; t < removedTriangles.Length; t++)
                {
                    if ((t & 4095) == 0) token.ThrowIfCancellationRequested();
                    for (int c = 0; c < 3; c++)
                    {
                        int r = source.Triangles[t * 3 + c];
                        usedBefore[r] = true;
                        if (!removedTriangles[t]) usedAfter[r] = true;
                    }
                }

                foreach (var patch in patches)
                foreach (int r in patch.Triangles)
                    usedAfter[r] = true;
                var map = new int[usedBefore.Length];
                var represented = new bool[source.LogicalVertexCount];
                int vertexCount = 0;
                for (int i = 0; i < map.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    int logical = source.RenderToLogical[i];
                    bool keep = !targets.Contains(logical) && (usedAfter[i] || !usedBefore[i]);
                    map[i] = keep ? vertexCount++ : -1;
                    if (keep) represented[logical] = true;
                }

                for (int i = 0; i < represented.Length; i++)
                {
                    if (!targets.Contains(i) && !represented[i])
                        throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.UnselectedRemoval"));
                }

                plan = new VertexDissolvePlan(new VertexDeletePlan(source, removedTriangles, removedFaces, map, vertexCount, triangleCount, faceCount), patches);
                reason = LocalizedText.Empty;
                return true;
            }
            catch (InvalidOperationException e)
            {
                reason = LocalizedText.FromException(e);
                return false;
            }
        }

        private static Patch BuildDissolvePatch(TopologyEditIndex index, int target, HashSet<int> selected, DissolvePolygon.Work work)
        {
            var source = index.Source;
            if (target < 0 || target >= source.LogicalVertexCount) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Selection.Error.VertexUnavailable"));
            if (index.Offsets[target] == index.Offsets[target + 1]) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.LooseVertices"));
            var region = new HashSet<int>();
            for (int i = index.Offsets[target]; i < index.Offsets[target + 1]; i++)
            {
                work.Step();
                int t = index.IncidentTriangles[i], face = index.TriangleFaces[t];
                region.Add(t);
                if (face >= 0)
                {
                    var pair = index.FaceTriangles[face];
                    region.Add(pair.TriangleA);
                    if (pair.TriangleB >= 0) region.Add(pair.TriangleB);
                }

                if (region.Count > 1024) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.PatchTooLarge"));
            }

            int[] triangles = new int[region.Count];
            region.CopyTo(triangles);
            Array.Sort(triangles);
            var localEdges = new Dictionary<ulong, TopologyEditIndex.Edge>(TopologyEditIndex.EdgeComparer.s_Instance);
            var renders = new Dictionary<int, int>();
            int area = source.TriangleSurfaceAreaIds[triangles[0]], firstFace = int.MaxValue;
            Vector3 origin = source.Vertices[index.RenderHeads[target]];
            double scaleSquared = 0, largestCross = 0;
            Point normal = default;
            int basisTriangle = -1;
            foreach (int t in triangles)
            {
                work.Step();
                if (source.TriangleSurfaceAreaIds[t] != area) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.SurfaceAreaBoundary"));
                if (index.TriangleFaces[t] >= 0) firstFace = Math.Min(firstFace, index.TriangleFaces[t]);
                Point a = Point.Local(source.Vertices[source.Triangles[t * 3]], origin);
                Point b = Point.Local(source.Vertices[source.Triangles[t * 3 + 1]], origin);
                Point c = Point.Local(source.Vertices[source.Triangles[t * 3 + 2]], origin);
                if (!a.Finite || !b.Finite || !c.Finite) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.NonFinitePositions"));
                Point cross = Point.Cross(b - a, c - a);
                if (cross.LengthSquared > largestCross)
                {
                    largestCross = cross.LengthSquared;
                    normal = cross;
                    basisTriangle = t;
                }

                for (int corner = 0; corner < 3; corner++)
                {
                    int render = source.Triangles[t * 3 + corner], logical = source.RenderToLogical[render];
                    int next = index.Logical(t, (corner + 1) % 3);
                    if (logical == next) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.DegenerateTriangle"));
                    Point p = Point.Local(source.Vertices[render], origin);
                    scaleSquared = Math.Max(scaleSquared, p.LengthSquared);
                    if (!renders.TryGetValue(logical, out int previous) || render < previous) renders[logical] = render;
                    ulong key = TopologyEditIndex.Key(logical, next);
                    localEdges.TryGetValue(key, out var edge);
                    edge.Add(logical, next, t);
                    localEdges[key] = edge;
                }
            }

            double scale = Math.Sqrt(scaleSquared), distance = Math.Max(scale * 1e-6, 1e-12), areaTolerance = Math.Max(scaleSquared * 1e-10, 1e-24);
            if (basisTriangle < 0 || Math.Sqrt(largestCross) <= areaTolerance) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.UnstablePlane"));
            normal = normal / Math.Sqrt(largestCross);
            int drop = Math.Abs(normal.X) >= Math.Abs(normal.Y) && Math.Abs(normal.X) >= Math.Abs(normal.Z) ? 0 : Math.Abs(normal.Y) >= Math.Abs(normal.Z) ? 1 : 2;
            double sign = (drop == 0 ? normal.X : drop == 1 ? -normal.Y : normal.Z) > 0 ? 1 : -1;
            Func<int, Point> project = render =>
            {
                Point p = Point.Local(source.Vertices[render], origin);
                return drop == 0 ? new Point(p.Y, p.Z * sign) : drop == 1 ? new Point(p.X, p.Z * sign) : new Point(p.X, p.Y * sign);
            };
            var points = new Dictionary<int, Point>();
            foreach (var pair in renders)
            {
                work.Step();
                Point p = Point.Local(source.Vertices[pair.Value], origin);
                if (Math.Abs(Point.Dot(p, normal)) > distance) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.NonPlanarPatch"));
                // Include all members, not just the render record chosen for output.
                for (int r = index.RenderHeads[pair.Key]; r >= 0; r = index.NextRender[r])
                {
                    work.Step();
                    if ((Point.Local(source.Vertices[r], origin) - p).LengthSquared > distance * distance)
                        throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.SeparatedRenderVertices"));
                }

                points[pair.Key] = project(pair.Value);
            }

            if (renders.Count - localEdges.Count + triangles.Length != 1)
                throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.NonDiskPatch"));
            var visited = new HashSet<int> { triangles[0] };
            var traversal = new List<int> { triangles[0] };
            for (int i = 0; i < traversal.Count; i++)
            {
                work.Step();
                int t = traversal[i];
                for (int c = 0; c < 3; c++)
                {
                    var edge = localEdges[TopologyEditIndex.Key(index.Logical(t, c), index.Logical(t, (c + 1) % 3))];
                    if (edge.Count != 2) continue;
                    int neighbor = edge.FirstTriangle == t ? edge.SecondTriangle : edge.FirstTriangle;
                    if (visited.Add(neighbor)) traversal.Add(neighbor);
                }
            }

            if (visited.Count != triangles.Length) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.DisconnectedFans"));
            double oldArea = 0;
            foreach (int t in triangles)
            {
                double triangleArea = DissolvePolygon.Orient(project(source.Triangles[t * 3]), project(source.Triangles[t * 3 + 1]), project(source.Triangles[t * 3 + 2]));
                if (triangleArea <= areaTolerance) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.FoldedPatch"));
                oldArea += triangleArea;
            }

            var nextCorner = new Dictionary<int, int>();
            var incoming = new HashSet<int>();
            foreach (var pair in localEdges)
            {
                work.Step();
                var edge = pair.Value;
                var global = index.Edges[pair.Key];
                if (edge.Count > 2 || edge.Count == 2 && edge.Direction != 0 || global.Count > 2 || global.Count == 2 && global.Direction != 0)
                    throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.NonManifoldPatch"));
                if (edge.Count != 1) continue;
                int a = (int)(pair.Key >> 32), b = (int)(pair.Key & uint.MaxValue);
                if (edge.Direction < 0)
                {
                    int temp = a;
                    a = b;
                    b = temp;
                }

                if (nextCorner.ContainsKey(a) || !incoming.Add(b)) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.BranchingBoundary"));
                nextCorner.Add(a, b);
            }

            if (nextCorner.Count < 3 || nextCorner.Count > DissolvePolygon.kMaximumCorners + 1)
                throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.BoundaryComplexity"));
            int start = int.MaxValue;
            foreach (int id in nextCorner.Keys) start = Math.Min(start, id);
            var loop = new List<int>(nextCorner.Count);
            int current = start;
            do
            {
                work.Step();
                loop.Add(current);
                if (!nextCorner.TryGetValue(current, out current) || loop.Count > nextCorner.Count)
                    throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.OpenBoundary"));
            } while (current != start);

            if (loop.Count != nextCorner.Count) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.Holes"));
            foreach (int id in renders.Keys)
            {
                if (id != target && !nextCorner.ContainsKey(id)) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.UnselectedInteriorVertex"));
                if (id != target && selected.Contains(id)) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.OverlappingPatches"));
            }

            int boundaryTarget = loop.IndexOf(target);
            if (boundaryTarget >= 0)
            {
                Point a = points[loop[(boundaryTarget + loop.Count - 1) % loop.Count]], b = points[target], c = points[loop[(boundaryTarget + 1) % loop.Count]];
                if (Math.Abs(DissolvePolygon.Orient(a, b, c)) > distance * Math.Sqrt((c - a).LengthSquared) || Point.Dot(b - a, b - c) >= -distance * distance)
                    throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.CornerVertex"));
                loop.RemoveAt(boundaryTarget);
            }

            var polygon = new Point[loop.Count];
            var boundary = new int[loop.Count];
            for (int i = 0; i < loop.Count; i++)
            {
                polygon[i] = points[loop[i]];
                boundary[i] = renders[loop[i]];
            }

            if (boundaryTarget < 0 && !DissolvePolygon.ContainsStrict(polygon, points[target], distance, areaTolerance, work))
                throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.InteriorVertexOutside"));
            int[] clipped = DissolvePolygon.Triangulate(polygon, distance, areaTolerance, work, out bool quad);
            var output = new int[clipped.Length];
            double newArea = 0;
            for (int i = 0; i < clipped.Length; i += 3)
            {
                newArea += DissolvePolygon.Orient(polygon[clipped[i]], polygon[clipped[i + 1]], polygon[clipped[i + 2]]);
                for (int c = 0; c < 3; c++) output[i + c] = boundary[clipped[i + c]];
            }

            if (Math.Abs(oldArea - newArea) > Math.Max(areaTolerance * triangles.Length, oldArea * 1e-6))
                throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.AreaChanged"));
            ValidateDissolveAttributes(index.Streams, source.SurfaceAreas, triangles, area, basisTriangle, project, work);
            return new Patch
            {
                Vertex = target, AreaId = area, FirstTriangle = triangles[0], FirstFace = firstFace,
                RemovedTriangles = triangles, Boundary = boundary, Triangles = output, IsQuad = quad
            };
        }

        private static void ValidateDissolveEdges(TopologyEditIndex index, Patch[] patches, bool[] removed, CancellationToken token)
        {
            var added = new Dictionary<ulong, TopologyEditIndex.Edge>(TopologyEditIndex.EdgeComparer.s_Instance);
            foreach (var patch in patches)
            {
                token.ThrowIfCancellationRequested();
                var boundary = new HashSet<ulong>(TopologyEditIndex.EdgeComparer.s_Instance);
                for (int i = 0; i < patch.Boundary.Length; i++) boundary.Add(TopologyEditIndex.Key(index.Source.RenderToLogical[patch.Boundary[i]], index.Source.RenderToLogical[patch.Boundary[(i + 1) % patch.Boundary.Length]]));
                for (int t = 0; t < patch.Triangles.Length / 3; t++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        int a = index.Source.RenderToLogical[patch.Triangles[t * 3 + c]], b = index.Source.RenderToLogical[patch.Triangles[t * 3 + (c + 1) % 3]];
                        ulong key = TopologyEditIndex.Key(a, b);
                        index.Edges.TryGetValue(key, out var old);
                        int outside = old.Count;
                        if (old.Count > 0 && removed[old.FirstTriangle]) outside--;
                        if (old.Count > 1 && removed[old.SecondTriangle]) outside--;
                        if (outside > 0 && !boundary.Contains(key)) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.ExistingDiagonal"));
                        added.TryGetValue(key, out var edge);
                        edge.Add(a, b, 0);
                        added[key] = edge;
                    }
                }
            }

            foreach (var pair in added)
            {
                token.ThrowIfCancellationRequested();
                var edge = pair.Value;
                if (index.Edges.TryGetValue(pair.Key, out var old))
                {
                    for (int i = 0; i < Math.Min(old.Count, 2); i++)
                    {
                        int t = i == 0 ? old.FirstTriangle : old.SecondTriangle;
                        if (removed[t]) continue;
                        for (int c = 0; c < 3; c++)
                        {
                            int a = index.Logical(t, c), b = index.Logical(t, (c + 1) % 3);
                            if (TopologyEditIndex.Key(a, b) == pair.Key) edge.Add(a, b, t);
                        }
                    }

                    if (old.Count > 2) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.NonManifoldNeighbor"));
                }

                if (edge.Count > 2 || edge.Count == 2 && edge.Direction != 0)
                    throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.NonManifoldResult"));
            }
        }

        internal static void ValidateDissolveAttributes(MeshData source, PersistenceSurfaceAreaData[] areas, int[] triangles, int areaId, int basis, Func<int, Point> project, DissolvePolygon.Work work)
        {
            PersistenceSurfaceAreaData area = null;
            foreach (var candidate in areas)
                if (candidate.Id == areaId)
                {
                    area = candidate;
                    break;
                }

            if (area == null) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.MissingSurfaceArea"));
            if (area.NativeWindowKind != NativeWindowKind.None || area.Material.VanillaSpecialKind != VanillaSpecialMaterialKind.None)
                throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.NativePayloadUnsupported"));
            int a = source.Triangles[basis * 3], b = source.Triangles[basis * 3 + 1], c = source.Triangles[basis * 3 + 2];
            Point pa = project(a), pb = project(b), pc = project(c);
            double determinant = DissolvePolygon.Orient(pa, pb, pc);
            var uvRange = new double[4];
            if (source.UV0 != null)
            {
                var low = new double[] { double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue };
                var high = new double[] { double.MinValue, double.MinValue, double.MinValue, double.MinValue };
                foreach (int t in triangles)
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int r = source.Triangles[t * 3 + corner];
                        for (int channel = 0; channel < 4; channel++)
                        {
                            double v = UVComponent(source.UV0[r], channel);
                            if (double.IsNaN(v) || double.IsInfinity(v)) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.NonFiniteUV"));
                            low[channel] = Math.Min(low[channel], v);
                            high[channel] = Math.Max(high[channel], v);
                        }
                    }

                for (int channel = 0; channel < 4; channel++) uvRange[channel] = Math.Max(1e-7, (high[channel] - low[channel]) * 1e-5);
            }

            foreach (int t in triangles)
                for (int corner = 0; corner < 3; corner++)
                {
                    work.Step();
                    int r = source.Triangles[t * 3 + corner];
                    if (source.Normals != null && !DissolveSameBasis(source.Normals[a], source.Normals[r]))
                        throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.NormalSeam"));
                    if (source.Tangents != null && (!DissolveSameBasis(new Vector3(source.Tangents[a].x, source.Tangents[a].y, source.Tangents[a].z), new Vector3(source.Tangents[r].x, source.Tangents[r].y, source.Tangents[r].z)) || source.Tangents[a].w != source.Tangents[r].w))
                        throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.TangentSeam"));
                    if (source.VertexColors != null && !source.VertexColors[a].Equals(source.VertexColors[r]) || source.TexCoord1 != null && !SubdivisionBasis.SameBits(source.TexCoord1[a], source.TexCoord1[r]) || source.TexCoord3 != null && !DissolveSamePayload(source.TexCoord3[a], source.TexCoord3[r]))
                        throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.AttributeSeam"));
                    if (source.UV0 == null) continue;
                    Point p = project(r);
                    double u = DissolvePolygon.Orient(pa, p, pc) / determinant;
                    double v = DissolvePolygon.Orient(pa, pb, p) / determinant;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        double baseUV = UVComponent(source.UV0[a], channel);
                        double expected = (UVComponent(source.UV0[b], channel) - baseUV) * u + (UVComponent(source.UV0[c], channel) - baseUV) * v;
                        if (Math.Abs((UVComponent(source.UV0[r], channel) - baseUV) - expected) > uvRange[channel])
                            throw new LocalizedTextException(LocalizedText.For("PO2.Edit.DissolveVertices.Error.UVSeam"));
                    }
                }
        }

        private static unsafe bool DissolveSamePayload(Vector4 a, Vector4 b)
        {
            uint* left = (uint*)&a;
            uint* right = (uint*)&b;
            return left[0] == right[0] && left[1] == right[1] && left[2] == right[2] && left[3] == right[3];
        }

        private static double UVComponent(Vector4 value, int channel) => channel == 0 ? value.x : channel == 1 ? value.y : channel == 2 ? value.z : value.w;

        private static bool DissolveSameBasis(Vector3 a, Vector3 b)
        {
            Point p = Point.Local(a, Vector3.zero), q = Point.Local(b, Vector3.zero);
            return p.Finite && q.Finite && p.LengthSquared > 1e-12 && q.LengthSquared > 1e-12 && (p - q).LengthSquared <= 1e-10;
        }

        internal static MeshMutationResult CreateVertexDissolutionMutation(VertexDissolvePlan plan, CancellationToken token)
        {
            var removal = plan.Removal;
            var source = plan.Source;
            var map = removal.OldToNew;
            var triangles = new int[removal.TriangleCount * 3];
            var memberships = new int[removal.TriangleCount];
            var faces = new StructuralFace[removal.FaceCount];
            var trianglePatches = new Dictionary<int, Patch>();
            var facePatches = new Dictionary<int, Patch>();
            foreach (var patch in plan.Patches)
            {
                trianglePatches.Add(patch.FirstTriangle, patch);
                if (patch.FirstFace != int.MaxValue) facePatches.Add(patch.FirstFace, patch);
            }

            int nextTriangle = 0, nextFace = 0;
            for (int t = 0; t < removal.RemovedTriangles.Length; t++)
            {
                if ((t & 4095) == 0) token.ThrowIfCancellationRequested();
                if (trianglePatches.TryGetValue(t, out var patch))
                {
                    for (int p = 0; p < patch.Triangles.Length / 3; p++)
                    {
                        for (int c = 0; c < 3; c++) triangles[nextTriangle * 3 + c] = map[patch.Triangles[p * 3 + c]];
                        memberships[nextTriangle++] = patch.AreaId;
                    }
                }

                if (removal.RemovedTriangles[t]) continue;
                for (int c = 0; c < 3; c++) triangles[nextTriangle * 3 + c] = map[source.Triangles[t * 3 + c]];
                memberships[nextTriangle++] = source.TriangleSurfaceAreaIds[t];
            }

            for (int f = 0; f < removal.RemovedFaces.Length; f++)
            {
                if ((f & 4095) == 0) token.ThrowIfCancellationRequested();
                if (facePatches.TryGetValue(f, out var patch)) EmitDissolveFaces(patch, map, faces, ref nextFace);
                if (removal.RemovedFaces[f]) continue;
                var face = source.StructuralFaces[f];
                int a = map[face.GetRenderVertexIndex(0)], b = map[face.GetRenderVertexIndex(1)], c = map[face.GetRenderVertexIndex(2)];
                faces[nextFace] = face.IsQuad ? new StructuralFace(nextFace, a, b, c, map[face.GetRenderVertexIndex(3)]) : new StructuralFace(nextFace, a, b, c);
                nextFace++;
            }

            foreach (var patch in plan.Patches)
                if (patch.FirstFace == int.MaxValue)
                    EmitDissolveFaces(patch, map, faces, ref nextFace);
            var result = new MeshMutationResult(CompactRemovalStream(source.Vertices, removal, token), CompactRemovalStream(source.Normals, removal, token), CompactRemovalStream(source.Tangents, removal, token), CompactRemovalStream(source.UV0, removal, token), CompactRemovalStream(source.VertexColors, removal, token), CompactRemovalStream(source.TexCoord1, removal, token), CompactRemovalStream(source.TexCoord3, removal, token), triangles, faces, memberships);
            SetRemovalVertexMaps(result, source, map, token);
            return result;
        }

        private static void EmitDissolveFaces(Patch patch, int[] map, StructuralFace[] faces, ref int next)
        {
            if (patch.IsQuad)
            {
                faces[next] = new StructuralFace(next, map[patch.Boundary[0]], map[patch.Boundary[1]], map[patch.Boundary[2]], map[patch.Boundary[3]]);
                next++;
            }
            else
                for (int i = 0; i < patch.Triangles.Length; i += 3)
                {
                    faces[next] = new StructuralFace(next, map[patch.Triangles[i]], map[patch.Triangles[i + 1]], map[patch.Triangles[i + 2]]);
                    next++;
                }
        }
    }
}