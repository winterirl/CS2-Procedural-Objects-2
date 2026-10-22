using System;
using System.Diagnostics;
using System.Threading;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Picking;
using PO2.Rendering.Objects;
using PO2.Tools.MeshEdit;
using PO2.Tools.ObjectTool;

internal static partial class Program {
    private static MeshPart Part(Vector3[] vertices, int[] indices) {
        var part = new MeshPart { Mesh = new TestMesh { Vertices = vertices, Triangles = indices } };
        part.StructuralTopology.FaceCount = indices.Length / 6 + 1;
        for (int i = 0; i < indices.Length / 3; i++) part.TriangleSurfaceAreaIds.Add(0);
        return part;
    }
    private static MeshPart Square(float z, float x = 0, float size = 1) {
        return Part(new[] { new Vector3(x, 0, z), new Vector3(x + size, 0, z), new Vector3(x + size, size, z), new Vector3(x, size, z) }, new[] { 0, 1, 2, 0, 2, 3 });
    }
    private static Ray CenterRay => new Ray(new Vector3(0.25f, 0.3f, 0), new Vector3(0, 0, 1));
    private static Bounds WideBounds => new Bounds(new Vector3(0, 0, 10), new Vector3(100, 100, 20));
    private static ObjectPickStatus Resolve(ProceduralObjectRenderer renderer, Ray ray, out int id, out float distance) {
        Stopwatch timer = Stopwatch.StartNew();
        ObjectPickStatus result;
        do {
            result = renderer.FindClosestMesh(ray, out id, out distance);
            if (result != ObjectPickStatus.Pending) return result;
            if (timer.Elapsed.TotalSeconds > 10) throw new Exception("Picker remained pending");
            Thread.Sleep(1);
        } while (true);
    }
    private static void Expect(ProceduralObjectRenderer renderer, int expected, string name, Ray? ray = null) {
        var status = Resolve(renderer, ray ?? CenterRay, out int id, out float _);
        Check(id == expected && status == (expected < 0 ? ObjectPickStatus.Miss : ObjectPickStatus.Hit), name);
    }
    private static void GeometryCases() {
        Random random = new Random(214);
        foreach (int count in new[] { 8, 64, 65, 700 }) {
            Vector3[] vertices = new Vector3[count * 3];
            int[] indices = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) {
                vertices[i] = new Vector3((float)random.NextDouble() * 20 - 10, (float)random.NextDouble() * 20 - 10, (float)random.NextDouble() * 20 + 1);
                indices[i] = i;
            }
            var cache = new TrianglePickCache(vertices, indices);
            for (int i = 0; i < 400; i++) {
                Vector3 origin = new Vector3((float)random.NextDouble() * 30 - 15, (float)random.NextDouble() * 30 - 15, 0);
                Vector3 direction = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, 1).normalized;
                double expected = Oracle(vertices, indices, origin, direction);
                bool hit = cache.TryPick(origin, direction, out int _, out float distance);
                Check(hit == !double.IsPositiveInfinity(expected), "oracle hit/miss");
                if (hit) Check(Math.Abs(distance - expected) < 0.001, "oracle distance");
            }
        }
        Grid(12, out Vector3[] grid, out int[] tris);
        var source = Part(grid, tris);
        Check(StructuralFacePickCache.TryCreate(source, out var face), "face wrapper create");
        for (int y = 0; y < 12; y++) {
            for (int x = 0; x < 12; x++) {
                Check(face.TryPick(new Vector3(x + 0.2f, y + 0.7f, 0), new Vector3(0, 0, 1), out int faceId) && faceId == y * 12 + x, "face ID mapping");
            }
        }
        source.StructuralTopology = null;
        Check(!StructuralFacePickCache.TryCreate(source, out face), "structural wrapper restriction");
        var square = Square(10);
        var picker = new TrianglePickCache(square.Mesh.Vertices, square.Mesh.Triangles);
        foreach (Vector3 origin in new[] { new Vector3(0, 0, 0), new Vector3(1, 1, 0), new Vector3(0.5f, 0.5f, 0) }) {
            Check(picker.TryPick(origin, new Vector3(0, 0, 1), out int _, out float _), "edge/vertex hit");
        }
        Check(picker.TryPick(new Vector3(0.5f, 0.5f, 20), new Vector3(0, 0, -1), out int _, out float _), "backface");
        Check(!picker.TryPick(CenterRay.origin, new Vector3(1, 0, 0), out int _, out float _), "parallel miss");
        Check(!picker.TryPick(CenterRay.origin, CenterRay.direction, out int _, out float _, 9), "maximum distance");
        Check(!picker.TryPick(new Vector3(float.NaN, 0, 0), CenterRay.direction, out int _, out float _), "invalid ray");
        Check(TrianglePickCache.TryIntersectBounds(new Vector3(0, 0, 0), CenterRay.direction, new Vector3(-1, -1, -1), Vector3.one, 100, out float entry) && entry == 0, "inside bounds entry");
        Check(TrianglePickCache.TryIntersectBounds(new Vector3(0, 0, 0), CenterRay.direction, new Vector3(-1, -1, 0), new Vector3(1, 1, 0), 100, out _) == false, "zero-distance surface excluded");
        var empty = new TrianglePickCache(Array.Empty<Vector3>(), Array.Empty<int>());
        Check(!empty.TryPick(CenterRay.origin, CenterRay.direction, out int _, out float _), "empty mesh");
        var degenerate = new TrianglePickCache(new[] { Vector3.zero }, new[] { 0, 0, 0 });
        Check(!degenerate.TryPick(CenterRay.origin, CenterRay.direction, out int _, out float _), "degenerate triangle");
    }
    // Independent plane intersection and barycentric oracle, using doubles.
    private static double Oracle(Vector3[] vertices, int[] indices, Vector3 origin, Vector3 direction) {
        double best = double.PositiveInfinity;
        for (int i = 0; i < indices.Length; i += 3) {
            var a = D(vertices[indices[i]]); var b = D(vertices[indices[i + 1]]); var c = D(vertices[indices[i + 2]]);
            var ab = Sub(b, a); var ac = Sub(c, a); var n = Cross(ab, ac);
            double denominator = Dot(n, D(direction));
            if (Math.Abs(denominator) < 1e-12) continue;
            double t = Dot(n, Sub(a, D(origin))) / denominator;
            if (t <= 1e-6 || t >= best) continue;
            var q = Sub(Add(D(origin), Mul(D(direction), t)), a);
            double aa = Dot(ab, ab), cc = Dot(ac, ac), bc = Dot(ab, ac);
            double det = aa * cc - bc * bc;
            if (Math.Abs(det) < 1e-16) continue;
            double u = (cc * Dot(q, ab) - bc * Dot(q, ac)) / det;
            double v = (aa * Dot(q, ac) - bc * Dot(q, ab)) / det;
            if (u >= 0 && v >= 0 && u + v <= 1) best = t;
        }
        return best;
    }
    private static double[] D(Vector3 v) => new[] { (double)v.x, v.y, v.z };
    private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    private static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
    private static double[] Mul(double[] a, double b) => new[] { a[0] * b, a[1] * b, a[2] * b };
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

    private static void IntegrationCases() {
        var renderer = new ProceduralObjectRenderer();
        renderer.Add(1, Square(5, 20), WideBounds);
        renderer.Add(2, Square(10), new Bounds(new Vector3(0.5f, 0.5f, 10), new Vector3(1, 1, 0)));
        Expect(renderer, 2, "small visible PO beats big bounds");
        Expect(renderer, 1, "bounds fallback", new Ray(new Vector3(3, 3, -1), CenterRay.direction));
        Expect(renderer, -1, "complete miss", new Ray(new Vector3(300, 300, -1), CenterRay.direction));
        renderer.Add(3, Square(3), WideBounds, true);
        Expect(renderer, 3, "isolated actual surface wins");
        renderer.Add(0, Square(3), WideBounds);
        Expect(renderer, 0, "deterministic tie");
        renderer.Remove(0); renderer.Remove(3);
        renderer.Add(1, Square(2), WideBounds);
        Expect(renderer, 1, "second part participates");
        renderer.Remove(1);
        var part = Square(10);
        renderer.Replace(2, part);
        Expect(renderer, 2, "replacement identity");
        object oldCache = renderer.Cache(2);
        Expect(renderer, 2, "unchanged geometry reuses cache");
        Check(ReferenceEquals(oldCache, renderer.Cache(2)), "cache identity");
        for (int i = 0; i < part.Mesh.Vertices.Length; i++) part.Mesh.Vertices[i].z = 12;
        part.PositionVersion++;
        renderer.Touch(2, WideBounds);
        Check(Resolve(renderer, CenterRay, out _, out float d) == ObjectPickStatus.Hit && Math.Abs(d - 12) < 0.001, "position refit distance");
        part.Mesh.Triangles = new[] { 0, 0, 0, 0, 0, 0 };
        part.TopologyVersion++;
        Check(Resolve(renderer, CenterRay, out _, out d) == ObjectPickStatus.Hit && d == 0, "topology invalidation falls back");
        renderer.Replace(2, Square(7));
        Check(Resolve(renderer, CenterRay, out _, out d) == ObjectPickStatus.Hit && Math.Abs(d - 7) < 0.001, "undo-style replaced object with reset versions");
        renderer.Dirty(2, true);
        Check(renderer.FindClosestMesh(CenterRay, out _, out _) == ObjectPickStatus.Pending, "wait for renderer sync");
        renderer.Dirty(2, false);
        renderer.Remove(2);
        Check(renderer.RetainedBytes == 0, "delete releases cache");
        foreach (var scale in new[] { new Vector3(2, 0.5f, 4), new Vector3(-2, 3, -4), Vector3.one }) {
            var transform = new ObjectTransform(new Vector3(4, 8, 12), new Quaternion { value = System.Numerics.Quaternion.CreateFromYawPitchRoll(0.7f, 0.2f, -0.3f) }, scale);
            var origin = transform.TransformPoint(CenterRay.origin);
            var direction = transform.TransformVector(CenterRay.direction).normalized;
            renderer = new ProceduralObjectRenderer();
            renderer.Add(1, Square(10), new Bounds(Vector3.zero, new Vector3(500, 500, 500)), false, transform);
            Check(Resolve(renderer, new Ray(origin, direction), out _, out d) == ObjectPickStatus.Hit && Math.Abs(d - 10 * Math.Abs(scale.z)) < 0.001, "world-distance under transformed ray");
        }
        var excluded = Square(1);
        for (int i = 0; i < excluded.TriangleSurfaceAreaIds.Count; i++) excluded.TriangleSurfaceAreaIds[i] = 999;
        renderer = new ProceduralObjectRenderer();
        renderer.Add(1, excluded, WideBounds);
        renderer.Add(2, Square(8), WideBounds);
        Expect(renderer, 2, "non-rendered membership excluded");
        for (int i = 0; i < excluded.TriangleSurfaceAreaIds.Count; i++) excluded.TriangleSurfaceAreaIds[i] = 0;
        excluded.SurfaceAreaMembershipVersion++;
        Expect(renderer, 1, "membership change invalidates");
        excluded.FullGeometryVersion++;
        Expect(renderer, 1, "broad geometry invalidates");
        var tool = new ProceduralObjectToolSystem();
        tool.System.Renderer.Add(1, Square(2), WideBounds);
        tool.Pick(CenterRay, true);
        for (int i = 0; i < 5 && !tool.System.SelectedObjectId.HasValue; i++) tool.Pick(CenterRay, false);
        Check(tool.System.SelectedObjectId == 1, "tool click selects");
        Check(tool.System.QueryCount <= 2, "one lookup per update, allowing cold budget");
        Grid(180, out var largeVertices, out var largeIndices);
        tool = new ProceduralObjectToolSystem();
        tool.System.Renderer.Add(1, Part(largeVertices, largeIndices), WideBounds);
        tool.System.Renderer.Add(2, Square(2, 20), WideBounds);
        tool.Pick(CenterRay, true);
        Check(!tool.System.SelectedObjectId.HasValue, "cold click waits, no fallback");
        Stopwatch wait = Stopwatch.StartNew();
        while (!tool.System.SelectedObjectId.HasValue && wait.Elapsed.TotalSeconds < 10) {
            tool.Pick(new Ray(new Vector3(300, 300, 0), CenterRay.direction), false);
            Thread.Sleep(1);
        }
        Check(tool.System.SelectedObjectId == 1, "pending click retains original ray");
        var pending = new PendingObjectPick();
        pending.Set(CenterRay, 1, 2, 3); pending.Validate(1, 2, 3); Check(pending.HasClick, "pending stable");
        pending.Validate(1, 2, 4); Check(!pending.HasClick, "scene change cancels");
        pending.Set(CenterRay, 1, 2, 3); pending.Validate(2, 2, 3); Check(!pending.HasClick, "mode change cancels");
        pending.Set(CenterRay, 1, 2, 3); pending.Validate(1, 3, 3); Check(!pending.HasClick, "selection change cancels");
    }

    private static void WorkerCases() {
        var worker = new ObjectPickWorker();
        var state = new ObjectPickCacheState();
        var square = Square(2);
        var cache = new TrianglePickCache(square.Mesh.Vertices, square.Mesh.Triangles);
        var gate = new System.Threading.ManualResetEventSlim(false);
        Check(worker.TryStart(state, () => { gate.Wait(); return cache; }), "worker starts");
        Check(!worker.TryStart(state, () => cache), "bounded worker");
        state.Release(); gate.Set();
        Stopwatch wait = Stopwatch.StartNew();
        while (worker.IsBusy && wait.Elapsed.TotalSeconds < 5) { worker.Poll(); Thread.Sleep(1); }
        Check(state.Cache == null && !worker.IsBusy, "stale completion discarded");
        Check(worker.TryStart(state, () => throw new ArgumentException("Expected test failure")), "worker failure starts");
        Exception error = null;
        while (worker.IsBusy) { error = worker.Poll() ?? error; Thread.Sleep(1); }
        Check(error != null && state.Failed, "worker failure observed");
        state.Release();
        worker.TryStart(state, () => cache); worker.Detach();
        while (worker.IsBusy) { worker.Poll(); Thread.Sleep(1); }
        Check(state.Cache == null, "detached worker cannot publish");
        gate.Dispose();
        Grid(200, out var vertices, out var indices);
        var part = Part(vertices, indices);
        state = new ObjectPickCacheState();
        state.Observe(part, 0, 0, 0, 0);
        var capture = new ObjectPickCapture(state, vertices, indices, part.TriangleSurfaceAreaIds,
            new System.Collections.Generic.HashSet<int> { 0 });
        Check(!capture.Step(Stopwatch.GetTimestamp() - 1), "capture respects the update deadline");
        state.Observe(part, 1, 0, 0, 0);
        bool rejected = false;
        try { capture.Step(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "changed capture cannot mix geometry versions");
        capture = new ObjectPickCapture(state, vertices, indices, part.TriangleSurfaceAreaIds,
            new System.Collections.Generic.HashSet<int> { 0 });
        while (!capture.Step()) { }
        var build = capture.CreateBuild();
        vertices[0].z = 999;
        var detached = build();
        Check(detached.TryPick(CenterRay.origin, CenterRay.direction, out _, out float detachedDistance) && Math.Abs(detachedDistance - 10) < 0.001, "worker capture is detached from live vertices");
    }

}
