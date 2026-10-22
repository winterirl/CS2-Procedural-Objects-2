using System;
using System.Collections.Generic;
using System.Linq;
using PO2;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Materials;
using PO2.Rendering;
using PO2.Rendering.Objects;

internal static partial class Program {
    private static void RendererUpdateTests() {
        RendererDirtyAndBounds();
        RendererStreams();
        RendererChanges(false);
        RendererChanges(true);
        RendererMixedMapping();
        RendererCorrection();
        RendererHistory();
    }

    private static void RendererDirtyAndBounds() {
        var random = new Random(612);
        foreach (int size in new[] { 0, 1, 31, 32, 33, 255, 256, 257, 4097 }) {
            var dirty = new RenderDirtyIndices(size);
            var expected = new SortedSet<int>();
            var plan = new RenderUploadPlan();
            for (int frame = 0; frame < 40; frame++) {
                for (int i = 0; i < size / 9; i++) { int value = random.Next(size); dirty.Add(value); dirty.Add(value); expected.Add(value); }
                var actual = new List<int>(); foreach (int value in dirty) actual.Add(value);
                Check(actual.SequenceEqual(expected) && dirty.Count == expected.Count, "Dirty set preserves exact sorted union");
                plan.Build(dirty, 12);
                foreach (int index in expected) Check(plan.Ranges.Take(plan.RangeCount).Any(r => index >= r.Start && index < r.Start + r.Count), "Upload ranges cover dirty vertex");
                Check(plan.RangeCount <= RenderUploadPlan.kMaximumRanges, "Upload call count bounded");
                Check(!plan.Dense || (plan.RangeCount == 1 && plan.UploadedVertices == size), "Dense plan covers exactly the stream");
                dirty.Clear(); expected.Clear();
                Check(dirty.Count == 0 && !dirty.GetEnumerator().MoveNext(), "Cleared words contain no stale bits");
            }
            var points = Enumerable.Range(0, size).Select(i => new Vector3(i - 300, i % 17 - 100, -i)).ToArray();
            var bounds = new RenderBounds(points);
            for (int frame = 0; frame < 40; frame++) {
                if (size != 0) {
                    int index = random.Next(size); points[index] = new Vector3(random.Next(-1000, 1000), -frame, frame);
                    dirty.Add(index);
                }
                bounds.Update(points, dirty);
                if (size != 0) {
                    var min = points[0]; var max = min;
                    foreach (var point in points) { min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
                    Check(bounds.Value.min.Equals(min) && bounds.Value.max.Equals(max), "Cached bounds equal complete scan after expansion/shrink");
                }
                else Check(bounds.Value.size.Equals(Vector3.zero), "Empty mesh bounds");
                dirty.Clear();
            }
        }
        var sparse = new RenderDirtyIndices(20000); sparse.Add(100); sparse.Add(101);
        var ranges = new RenderUploadPlan(); ranges.Build(sparse, 12);
        Check(!ranges.Dense && ranges.RangeCount == 1 && ranges.UploadedVertices == 2, "Clustered edit stays sparse");
        sparse.Clear(); for (int i = 0; i < 34; i++) sparse.Add(i * 100);
        ranges.Build(sparse, 12); Check(ranges.Dense, "Fragmentation chooses one whole stream");
        sparse.Clear(); sparse.Add(0); sparse.Add(22); ranges.Build(sparse, 12);
        Check(ranges.RangeCount == 1, "Coalesce a 252-byte gap");
        sparse.Clear(); sparse.Add(0); sparse.Add(23); ranges.Build(sparse, 12);
        Check(ranges.RangeCount == 2, "Keep a 264-byte gap separate");
        var half = new RenderDirtyIndices(100); for (int i = 0; i < 50; i++) half.Add(i);
        ranges.Build(half, 12); Check(ranges.Dense, "Exactly half of a stream uses one dense upload");
        var large = new Vector3[20000]; var cache = new RenderBounds(large);
        sparse.Clear(); sparse.Add(1030); large[1030] = new Vector3(20, 30, 40); cache.Update(large, sparse);
        Check(cache.ScannedVertices == 256, "One changed bounds block avoids a full scan");
        large[1030] = Vector3.zero; cache.Update(large, sparse);
        Check(cache.Value.size.Equals(Vector3.zero), "Returning to the start shrinks bounds immediately");
    }

    private static void RendererStreams() {
        foreach (int count in new[] { 0, 3, 65536 }) {
            var positions = Enumerable.Range(0, count).Select(i => new Vector3(i, -i, 3)).ToArray();
            var triangles = count == 0 ? Array.Empty<int[]>() : new[] { new[] { 0, 1, count - 1 } };
            var mesh = ProceduralObjectRenderer.TestWriteMesh(positions, triangles);
            Check(mesh.vertices.SequenceEqual(positions), "Ordinary full creation retains vertex bytes");
            Check(mesh.indexFormat == (count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16), "Ordinary full creation chooses correct index width");
            Check(mesh.subMeshCount == triangles.Length && mesh.TestIndices.SequenceEqual(triangles.SelectMany(t => t)), "Ordinary full creation retains submesh indices");
        }
        foreach (bool normals in new[] { false, true }) foreach (bool tangents in new[] { false, true })
        foreach (int dimension in new[] { 2, 4 }) foreach (bool extras in new[] { false, true }) {
            var mesh = new Mesh();
            var p = new[] { new Vector3(2, 3, 4), new Vector3(-4, 5, 6) };
            var n = normals ? new[] { Vector3.up, Vector3.right } : null;
            var t = tangents ? new[] { new Vector4(1, 2, 3, -1), new Vector4(5, 6, 7, 1) } : null;
            var uv = new[] { new Vector4(3, 4, 7, 8), new Vector4(1, 2, 5, 6) };
            var tc1 = extras ? new[] { new Vector4(2, 17, 89, -0.75f), new Vector4(0, 999, 8, -1) } : null;
            var tc3 = extras ? new[] { new Vector4(4, 3, 2, 1), new Vector4(9, 8, 7, 6) } : null;
            var colors = extras ? new[] { new Color32(1, 2, 3, 4), new Color32(5, 6, 7, 8) } : null;
            RenderVertexStreams.WriteAll(mesh, p, n, t, uv, colors, tc1, tc3, dimension, tangents, tangents);
            var layout = RenderVertexStreams.Describe(p, n, t, uv, colors, tc1, tc3, dimension, tangents);
            Check(layout.Matches(mesh, p.Length), "Explicit layout signature matches optional streams");
            Check(mesh.vertices.SequenceEqual(p), "Position stream bytes");
            Check(!normals || mesh.normals.SequenceEqual(n), "Normal stream bytes");
            Check(!tangents || mesh.tangents.SequenceEqual(t), "Tangent stream and handedness bytes");
            Check(mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0) == dimension, "Preserve UV0 dimension");
            if (extras) {
                Check(mesh.Read<Vector4>(VertexAttribute.TexCoord1).SequenceEqual(tc1), "Preserve window UV1 payload");
                Check(mesh.Read<Vector4>(VertexAttribute.TexCoord3).SequenceEqual(tc3), "Preserve UV3 payload");
                Check(mesh.Read<Color32>(VertexAttribute.Color).SequenceEqual(colors), "Preserve byte colors");
            }
            var aux = (byte[])mesh.Streams[layout.Auxiliary].Clone();
            p[1] = new Vector3(123, 45, 6);
            RenderVertexStreams.Submit(mesh, p, 1, 1, 1, 0, 12, RenderVertexStreams.Role.Position);
            Check(mesh.vertices.SequenceEqual(p) && mesh.Streams[layout.Auxiliary].SequenceEqual(aux), "Position range leaves material bytes untouched");
        }
    }

    private static MeshPart RendererFixture(bool isolated) {
        var grid = Grid(64).Mesh;
        var tangents = Enumerable.Repeat(new Vector4(1, 0, 0, -1), grid.Vertices.Length).ToArray();
        var uvs = grid.Vertices.Select(p => new Vector4(p.x, p.z, 0, 0)).ToArray();
        var part = new MeshPart("Renderer", new MeshData(grid.Vertices, Enumerable.Repeat(Vector3.forward, grid.Vertices.Length).ToArray(), tangents, uvs, grid.Triangles), MaterialData.CreateDefault());
        part.SetSurfaceAreaMappingMode(0, TextureMappingMode.Original);
        var material = isolated ? MaterialData.Restore(MaterialSourceKind.VanillaSpecial, null, default,
            VanillaSpecialMaterialKind.Glass, new TextureMappingData(), 0, 0, 0) : MaterialData.CreateDefault();
        int area = part.CreateSurfaceArea(material);
        part.SetSurfaceAreaMappingMode(area, TextureMappingMode.Original);
        part.AssignTrianglesToSurfaceArea(Enumerable.Range(0, 100).Where(i => (i & 1) != 0).ToArray(), area);
        return part;
    }

    private static void RendererChanges(bool isolated) {
        var part = RendererFixture(isolated);
        var obj = new ProceduralObject(612, Guid.NewGuid(), part); obj.Transform.Scale = Vector3.one;
        var renderer = new ProceduralObjectRenderer(); renderer.TestInitialize(obj, InitialRenderer(part, Vector3.one));
        var meshes = renderer.TestMeshes(obj);
        var original = (Vector3[])part.Mesh.Vertices.Clone();
        var tangentBytes = meshes.Select(m => (byte[])m.Streams[m.GetVertexAttributeStream(VertexAttribute.Tangent)].Clone()).ToArray();
        foreach (var mesh in meshes) { mesh.Writes.Clear(); mesh.VertexWrites.Clear(); }
        int[] positions = { 1, 2, 2, 3 }, normals = { 4, 5, 5 };
        uint before = part.GeometryVersion;
        foreach (int i in positions) part.Mesh.Vertices[i] += new Vector3(0, 0, 2);
        foreach (int i in normals) part.Mesh.Normals[i] = Vector3.right;
        part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, before, positions, normals));
        // Borrowed lists can be reused as soon as the callback returns.
        positions[0] = 4000; normals[0] = 4000;
        before = part.GeometryVersion; part.Mesh.Vertices[6] += new Vector3(0, 0, 8); part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, before, new[] { 6 }, Array.Empty<int>()));
        Check(renderer.TestRefresh(obj), "Accumulated notices use precise path");
        Check(renderer.TestCopiesMatch(obj), "Every source copy and normal-only neighbor matches");
        for (int i = 0; i < meshes.Length; i++) {
            var mesh = meshes[i];
            int normalStream = mesh.GetVertexAttributeStream(VertexAttribute.Normal);
            Check(mesh.Writes.All(w => w.Stream == 0 || w.Stream == normalStream), "Unchanged material/tangent streams are not uploaded");
            Check(mesh.Streams[mesh.GetVertexAttributeStream(VertexAttribute.Tangent)].SequenceEqual(tangentBytes[i]), "Source tangent bytes stay intact");
            Check(mesh.Writes.Where(w => w.Stream == 0).Sum(w => w.Bytes) < mesh.vertexCount * 12, "Clustered position uploads stay proportional");
        }
        before = part.GeometryVersion;
        Array.Copy(original, part.Mesh.Vertices, original.Length); part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, before, new[] { 1, 2, 3, 6 }, Array.Empty<int>()));
        Check(renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "Restore to the start publishes all changed copies");
        before = part.GeometryVersion; part.Mesh.Vertices[20] += Vector3.up; part.MarkPositionsAndDirectionsChanged();
        // Deliberately miss one version; the next notice must not conceal it.
        uint missed = part.GeometryVersion; part.Mesh.Vertices[21] += Vector3.up; part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, missed, new[] { 21 }, Array.Empty<int>()));
        Check(!renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "Version gap repairs through full refresh");
        before = part.GeometryVersion; part.Mesh.Vertices[30] += Vector3.up; part.MarkPositionsAndDirectionsChanged();
        var notice = new GeometryChange(part, 0, before, new[] { 30 }, Array.Empty<int>());
        renderer.NoticeGeometryChange(obj, notice); renderer.NoticeGeometryChange(obj, notice);
        Check(!renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "Duplicate notice is conservative");
        before = part.GeometryVersion; part.Mesh.Vertices[31] += Vector3.up; part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, before, new[] { 31 }, Array.Empty<int>()));
        meshes[0].FailNextUpload = true;
        Check(!renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "Failed upload is repaired without a false completed version");
        before = part.GeometryVersion;
        part.Mesh.ReplaceDirectionStreams((Vector3[])part.Mesh.Normals.Clone(), (Vector4[])part.Mesh.Tangents.Clone());
        part.Mesh.Normals[33] = Vector3.up; part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, before, Array.Empty<int>(), new[] { 33 }));
        Check(!renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "Stream identity replacement forces full repair and rebases metadata");
        before = part.GeometryVersion; part.Mesh.Vertices[34] += Vector3.up; part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, before, new[] { -1 }, Array.Empty<int>()));
        Check(!renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "Malformed indices invalidate the entire notice");
        var foreign = new ProceduralObject(obj.Id, Guid.NewGuid(), part);
        before = part.GeometryVersion; part.Mesh.Vertices[32] += Vector3.up; part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(foreign, new GeometryChange(part, 0, before, new[] { 32 }, Array.Empty<int>()));
        Check(!renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "Reused numeric object ID cannot supply notices");
        Check(renderer.TestUpdateBytes > 0, "Update metadata is owned by the live renderer");
        renderer.TestDispose(); Check(renderer.TestUpdateBytes == 0, "Teardown retires all update metadata");
    }

    private static void RendererHistory() {
        var part = RendererFixture(true);
        var obj = new ProceduralObject(614, Guid.NewGuid(), part); obj.Transform.Scale = Vector3.one;
        var system = new ProceduralObjectSystem(); system.Objects.Add(obj.Id, obj);
        var renderer = new ProceduralObjectRenderer();
        var prepared = InitialRenderer(part, Vector3.one);
        Check(prepared.GeometryUpdates != null, "Topology worker builds reverse mapping and bounds");
        renderer.TestInitialize(obj, prepared);
        Check(prepared.GeometryUpdates == null, "Prepared metadata transfers to one live owner");
        var context = new HistoryContext(system, renderer.InvalidateGeometryChanges, geometryPatch: renderer.NoticeGeometryChange);
        var start = TopologyState.Capture(part);
        using (var action = new VertexPatchAction(obj, 0, new[] { 0, 1, 67 }, LocalizedText.Diagnostic("Renderer test"))) {
            uint before = part.GeometryVersion;
            part.Mesh.Vertices[0] += Vector3.forward;
            part.Mesh.Vertices[1] += Vector3.forward;
            action.Recalculate(part); part.MarkPositionsAndDirectionsChanged();
            renderer.NoticeGeometryChange(obj, action.Change(part, 0, before));
            Check(renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "Live patch sends its complete normal neighborhood");
            action.Seal(context);
            var final = TopologyState.Capture(part);
            for (int i = 0; i < 3; i++) {
                action.Replay(HistoryDirection.Undo, context);
                Check(renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "History undo reaches renderer without an active tool");
                Same(start, TopologyState.Capture(part), "Undo source values stay exact");
                action.Replay(HistoryDirection.Redo, context);
                Check(renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "History redo reaches renderer without an active tool");
                Same(final, TopologyState.Capture(part), "Redo source values stay exact");
            }
        }
        using (var cancel = new VertexPatchAction(obj, 0, new[] { 0 }, LocalizedText.Diagnostic("Cancel"))) {
            var beforeCancel = TopologyState.Capture(part);
            uint before = part.GeometryVersion;
            part.Mesh.Vertices[0] += Vector3.forward; cancel.Recalculate(part); part.MarkPositionsAndDirectionsChanged();
            renderer.NoticeGeometryChange(obj, cancel.Change(part, 0, before));
            Check(renderer.TestRefresh(obj), "Cancellation preview uses precise streams");
            cancel.RestoreCapture(context);
            Check(renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "Cancellation restores every rendered copy");
            Same(beforeCancel, TopologyState.Capture(part), "Cancellation source values stay exact");
        }
        var meshes = renderer.TestMeshes(obj);
        foreach (var mesh in meshes) mesh.Writes.Clear();
        uint version = part.GeometryVersion;
        part.Mesh.Normals[0] = Vector3.right; part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, version, Array.Empty<int>(), new[] { 0 }));
        Check(renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "Normal-only change reaches all destinations");
        Check(meshes.All(m => m.Writes.All(w => w.Stream == m.GetVertexAttributeStream(VertexAttribute.Normal))), "Normal-only change does not submit positions");
        int[] indices = { 0 };
        version = part.GeometryVersion; part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, version, indices, Array.Empty<int>()));
        for (int i = 0; i < 100; i++) {
            version = part.GeometryVersion; part.MarkPositionsAndDirectionsChanged();
            renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, version, indices, Array.Empty<int>()));
        }
        Check(renderer.TestRefresh(obj) && renderer.TestCopiesMatch(obj), "Many notices before one synchronization stay exact");
        renderer.TestDispose();
        Check(RenderGeometryPreparation.TryBuild(100000000, Array.Empty<int[]>(), null, Array.Empty<Vector3[]>()) == null,
            "Oversized metadata declines before allocating source-sized arrays");
        long reserved = RenderGeometryPreparation.Reservation.Reserved;
        using (var held = RenderGeometryPreparation.Reservation.TryAcquire(RenderGeometryPreparation.Reservation.kTotalLimit - reserved)) {
            Check(held != null && RenderGeometryPreparation.TryBuild(1, new[] { new[] { 0 } }, new Vector3[1], new Vector3[1][]) == null,
                "Global metadata admission includes outstanding generations and worker reservations");
        }
        Check(RenderGeometryPreparation.Reservation.Reserved == reserved, "Metadata reservation releases deterministically");
    }

    private static void RendererMixedMapping() {
        var part = RendererFixture(true);
        int area = part.SurfaceAreas.Last().Id;
        part.SetSurfaceAreaMappingMode(area, TextureMappingMode.Repeat);
        var obj = new ProceduralObject(613, Guid.NewGuid(), part); obj.Transform.Scale = Vector3.one;
        var renderer = new ProceduralObjectRenderer(); renderer.TestInitialize(obj, InitialRenderer(part, Vector3.one));
        var main = renderer.TestMesh(obj); main.Writes.Clear();
        uint before = part.GeometryVersion; part.Mesh.Vertices[4000] += Vector3.up; part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, before, new[] { 4000 }, Array.Empty<int>()));
        Check(renderer.TestRefresh(obj), "Isolated generated area does not disable original main mesh updates");
        Check(main.Writes.Where(w => w.Stream == 0).Sum(w => w.Bytes) == 12, "Unrelated generated area adds no full main position upload");
        Check(renderer.TestCopiesMatch(obj), "Mixed mapping retains source-mapped copies");
        var isolated = renderer.TestMeshes(obj).Last();
        isolated.Writes.Clear(); main.Writes.Clear();
        before = part.GeometryVersion; part.Mesh.Vertices[0] += new Vector3(0.125f, 0, 0); part.MarkPositionsAndDirectionsChanged();
        renderer.NoticeGeometryChange(obj, new GeometryChange(part, 0, before, new[] { 0 }, Array.Empty<int>()));
        Check(renderer.TestRefresh(obj), "Affected generated isolated area refreshes safely");
        Check(main.Writes.Where(w => w.Stream == 0).Sum(w => w.Bytes) == 12, "Generated isolated refresh keeps shared main copy sparse");
        Check(isolated.Writes.Any(w => w.Stream == isolated.GetVertexAttributeStream(VertexAttribute.TexCoord0)), "Affected generated area refreshes mapping payload");
        Check(renderer.TestCopiesMatch(obj), "Mixed mapping source copies stay coherent");
        renderer.TestDispose();
    }
}
