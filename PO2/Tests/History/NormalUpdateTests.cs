using System;
using System.Collections.Generic;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;

internal static class NormalUpdateTests {
    private static int s_Checks;
    private static void Check(bool value, string reason) { s_Checks++; if (!value) throw new Exception(reason); }
    private static bool Same(Vector3 a, Vector3 b) => BitConverter.SingleToInt32Bits(a.x) == BitConverter.SingleToInt32Bits(b.x) &&
        BitConverter.SingleToInt32Bits(a.y) == BitConverter.SingleToInt32Bits(b.y) && BitConverter.SingleToInt32Bits(a.z) == BitConverter.SingleToInt32Bits(b.z);
    private static Vector3[] Values(Vector3[] source, int[] indices) {
        var values = new Vector3[indices.Length];
        for (int i = 0; i < indices.Length; i++) values[i] = source[indices[i]];
        return values;
    }
    // Deliberately rebuild support from source triangles. This does not use
    // the production plan, slot maps, masks or kernel math.
    internal static Vector3[] Reference(MeshData mesh, Vector3[] startPositions, Vector3[] startNormals, int[] selected) {
        var moved = new HashSet<int>();
        foreach (int i in selected) if (!Same(mesh.Vertices[i], startPositions[i])) moved.Add(i);
        var active = new HashSet<int>(moved);
        for (int i = 0; i < mesh.Triangles.Length; i += 3) {
            int a = mesh.Triangles[i], b = mesh.Triangles[i + 1], c = mesh.Triangles[i + 2];
            if (moved.Contains(a) || moved.Contains(b) || moved.Contains(c)) { active.Add(a); active.Add(b); active.Add(c); }
        }
        var sums = new Vector3[mesh.Vertices.Length];
        for (int i = 0; i < mesh.Triangles.Length; i += 3) {
            int a = mesh.Triangles[i], b = mesh.Triangles[i + 1], c = mesh.Triangles[i + 2];
            Vector3 normal = Vector3.Cross(mesh.Vertices[b] - mesh.Vertices[a], mesh.Vertices[c] - mesh.Vertices[a]);
            if (active.Contains(a)) sums[a] += normal;
            if (active.Contains(b)) sums[b] += normal;
            if (active.Contains(c)) sums[c] += normal;
        }
        var result = (Vector3[])startNormals.Clone();
        foreach (int i in active) result[i] = sums[i].sqrMagnitude > 0f ? sums[i].normalized : Vector3.up;
        return result;
    }

    internal static int Run() {
        s_Checks = 0;
        long live = LocalNormals.Plan.LiveBytes;
        MasksAndReference();
        NumericalEdges();
        InvalidAndLifetime();
        MultipartRollbackAndSeal();
        Randomized();
        Check(LocalNormals.Plan.LiveBytes == live, "Normal tests release every preview plan");
        return s_Checks;
    }
    private static MeshPart Part(int count) {
        var part = new MeshPart();
        part.Mesh.Vertices = new Vector3[count]; part.Mesh.Normals = new Vector3[count];
        part.Mesh.Triangles = new[] { 0, 1, 2, 1, 3, 2, 4, 5, 6, 4, 4, 5 };
        for (int i = 0; i < count; i++) {
            part.Mesh.Vertices[i] = new Vector3(i * .17f, (i % 3) * .33f, (i % 7) * .13f);
            part.Mesh.Normals[i] = new Vector3(.123f + i * .001f, .456f, .789f);
        }
        return part;
    }
    private static void MasksAndReference() {
        var part = Part(9); var mesh = part.Mesh;
        int[] selected = { 0, 4, 8 };
        var start = (Vector3[])mesh.Vertices.Clone(); var normals = (Vector3[])mesh.Normals.Clone();
        int[] affected = LocalNormals.FindAffected(mesh, selected);
        using (var plan = new LocalNormals.Plan(mesh, affected, selected)) {
            Vector3[] oldPositions = Values(start, selected), oldNormals = Values(normals, affected);
            for (int pass = 0; pass < 3; pass++) for (int mask = 0; mask < 8; mask++) {
                for (int i = 0; i < selected.Length; i++) mesh.Vertices[selected[i]] = start[selected[i]] + (((mask >> i) & 1) != 0 ? new Vector3(.5f + pass, 0, .7f) : Vector3.zero);
                Check(plan.Recalculate(mesh, oldPositions, oldNormals), "Normal mask update succeeds");
                var expected = Reference(mesh, start, normals, selected);
                for (int i = 0; i < expected.Length; i++) Check(Same(expected[i], mesh.Normals[i]), "Every mask matches independent normal reference exactly");
            }
            int builds = plan.MaskBuilds;
            mesh.Vertices[0] += Vector3.up;
            Check(plan.Recalculate(mesh, oldPositions, oldNormals), "Stable mask update succeeds");
            Check(plan.MaskBuilds == builds && plan.MaskReuses > 0, "Stable moved mask reuses neighborhood");
            var changed = Reference(mesh, start, normals, selected);
            for (int i = 0; i < changed.Length; i++) Check(Same(changed[i], mesh.Normals[i]), "Reused mask still recomputes sums");
            Array.Copy(start, mesh.Vertices, start.Length);
            Check(plan.Recalculate(mesh, oldPositions, oldNormals), "Return to start succeeds");
            Check(plan.EvaluatedTriangles == 0, "Empty active mask skips triangle evaluation");
            for (int i = 0; i < normals.Length; i++) Check(Same(normals[i], mesh.Normals[i]), "Return restores imported normal bits");

        }
    }
    private static void NumericalEdges() {
        foreach (float height in new[] { 0f, 1e-12f, .000009f, .00001f, .000011f, 1f, -1f }) {
            var part = Part(9); var mesh = part.Mesh;
            mesh.Vertices[0] = Vector3.zero; mesh.Vertices[1] = Vector3.right; mesh.Vertices[2] = new Vector3(0, height, 0);
            mesh.Triangles = new[] { 0, 1, 2, 4, 4, 5 };
            var start = (Vector3[])mesh.Vertices.Clone(); var normals = (Vector3[])mesh.Normals.Clone();
            int[] selected = { 0, 4, 8 }; var affected = LocalNormals.FindAffected(mesh, selected);
            using (var plan = new LocalNormals.Plan(mesh, affected, selected)) {
                // A signed-zero change activates the triangle without changing its area.
                mesh.Vertices[0].x = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
                mesh.Vertices[4] += Vector3.up; mesh.Vertices[8] += Vector3.up;
                Check(plan.Recalculate(mesh, Values(start, selected), Values(normals, affected)), "Numerical edge accepted");
                var expected = Reference(mesh, start, normals, selected);
                for (int i = 0; i < expected.Length; i++) Check(Same(expected[i], mesh.Normals[i]), "Zero/tiny/threshold/isolated normals match exactly");
            }
        }
    }
    private static void InvalidAndLifetime() {
        var part = Part(9); var obj = new ProceduralObject(1, Guid.NewGuid(), part);
        var original = (Vector3[])part.Mesh.Normals.Clone();
        using (var action = new VertexPatchAction(obj, 0, new[] { 1, 2 }, LocalizedText.Diagnostic("Invalid normals"))) {
            part.Mesh.Vertices[1] = new Vector3(1e25f, 0, 0); part.Mesh.Vertices[2] = new Vector3(0, 1e25f, 0);
            Check(!action.Recalculate(part), "Non-finite cross product rejected");
            Check(!action.HasFiniteNormals(part), "Failed update remains invalid until rollback");
            for (int i = 0; i < original.Length; i++) Check(Same(original[i], part.Mesh.Normals[i]), "Failure publishes no partial normals");
        }
        part = Part(9); obj = new ProceduralObject(1, Guid.NewGuid(), part);
        using (var action = new VertexPatchAction(obj, 0, new[] { 0 }, LocalizedText.Diagnostic("Stale array"))) {
            part.Mesh.Vertices = (Vector3[])part.Mesh.Vertices.Clone();
            Check(!action.Recalculate(part), "Replaced source array rejected");
        }
        part = Part(9); obj = new ProceduralObject(1, Guid.NewGuid(), part);
        using (var action = new VertexPatchAction(obj, 0, new[] { 0 }, LocalizedText.Diagnostic("Bad triangle"))) {
            part.Mesh.Vertices[0] += Vector3.up; part.Mesh.Triangles[0] = int.MaxValue;
            Check(!action.Recalculate(part), "Late corrupt triangle cannot read out of bounds");
        }
        part = Part(9); part.Mesh.Normals = null; obj = new ProceduralObject(1, Guid.NewGuid(), part);
        long live = LocalNormals.Plan.LiveBytes;
        using (var action = new VertexPatchAction(obj, 0, new[] { 0 }, LocalizedText.Diagnostic("No normals"))) {
            Check(LocalNormals.Plan.LiveBytes == live, "Missing normal stream does not allocate a plan");
            part.Mesh.Vertices[0] += Vector3.up;
            Check(action.Recalculate(part) && part.Mesh.Normals == null, "Missing normal stream remains missing");
        }
        bool rejected = false;
        try { new LocalNormals.Plan(part.Mesh, new int[0], new[] { 0, 0 }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Duplicate selected indices rejected at capture");
    }
    private static void Randomized() {
        var random = new Random(631);
        for (int fixture = 0; fixture < 24; fixture++) {
            var part = Part(60); var mesh = part.Mesh;
            var triangles = new int[180];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = random.Next(60);
            mesh.Triangles = triangles;
            var selected = new int[20]; for (int i = 0; i < selected.Length; i++) selected[i] = i * 3;
            var start = (Vector3[])mesh.Vertices.Clone(); var normals = (Vector3[])mesh.Normals.Clone();
            var affected = LocalNormals.FindAffected(mesh, selected);
            using (var plan = new LocalNormals.Plan(mesh, affected, selected)) {
                var oldPositions = Values(start, selected); var oldNormals = Values(normals, affected);
                for (int step = 0; step < 30; step++) {
                    foreach (int i in selected) mesh.Vertices[i] = start[i] + (random.Next(3) == 0 ? Vector3.zero : new Vector3((float)random.NextDouble(), -.3f, .8f));
                    Check(plan.Recalculate(mesh, oldPositions, oldNormals), "Random normal update succeeds");
                    var expected = Reference(mesh, start, normals, selected);
                    for (int i = 0; i < expected.Length; i++) Check(Same(expected[i], mesh.Normals[i]), "Random normals match independent reference exactly");
                }
            }
        }
    }

    private static void MultipartRollbackAndSeal() {
        long live = LocalNormals.Plan.LiveBytes;
        var first = Part(9); var second = Part(9);
        var system = new ProceduralObjectSystem();
        var obj = new ProceduralObject(7, Guid.NewGuid(), first, second);
        system.Objects.Add(obj.Id, obj);
        int notices = 0;
        var context = new HistoryContext(system, geometryPatch: (owner, change) => notices++);
        using (var history = new ActionHistory(context)) {
            var a = new VertexPatchAction(obj, 0, new[] { 1, 2 }, LocalizedText.Diagnostic("Multipart"));
            var b = new VertexPatchAction(obj, 1, new[] { 1, 2 }, LocalizedText.Diagnostic("Multipart"));
            ulong beforeA = GeometryStreams.Capture(first, false).Hash(), beforeB = GeometryStreams.Capture(second, false).Hash();
            using (var edit = new VertexHistoryEdit(new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Multipart"),
                new List<HistoryCaptureAction> { a, b }, null), new Dictionary<int, VertexPatchAction> { [0] = a, [1] = b })) {
                first.Mesh.Vertices[1] += Vector3.up;
                Check(edit.Recalculate(obj, 0), "First multipart normal update succeeds");
                first.MarkPositionsAndDirectionsChanged();
                second.Mesh.Vertices[1] = new Vector3(1e25f, 0, 0); second.Mesh.Vertices[2] = new Vector3(0, 1e25f, 0);
                Check(!edit.Recalculate(obj, 1) && edit.PreviewFailed, "Late multipart failure cancels the edit");
                Check(GeometryStreams.Capture(first, false).Hash() == beforeA && GeometryStreams.Capture(second, false).Hash() == beforeB,
                    "Late multipart failure restores both parts exactly");
                Check(notices == 2 && history.Count == 0, "Rollback publishes both restored footprints without a history entry");
                Check(LocalNormals.Plan.LiveBytes == live, "Rollback immediately releases both preview plans");
            }
            a = new VertexPatchAction(obj, 0, new[] { 0 }, LocalizedText.Diagnostic("Commit"));
            using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Commit"), new List<HistoryCaptureAction> { a }, null)) {
                first.Mesh.Vertices[0] += Vector3.up;
                Check(a.Recalculate(first) && edit.Complete(), "Normal update seals successfully");
            }
            Check(history.Count == 1 && LocalNormals.Plan.LiveBytes == live, "Retained history does not retain preview plans");
            history.Request(HistoryDirection.Undo);
            Check(GeometryStreams.Capture(first, false).Hash() == beforeA, "Sealed normal update undoes exactly");
        }
    }
}
