using System;
using System.Collections.Generic;
using System.Diagnostics;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Tools.Gizmos;
using PO2.Tools.MeshEdit;

internal static class SelectionTransformTests {
    private static int s_Checks;
    private static readonly TransformFrame s_Global = new TransformFrame(
        new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1));

    private static void Check(bool value, string reason) {
        s_Checks++;
        if (!value) throw new Exception(reason);
    }

    private static void Near(Vector3 actual, Vector3 expected, string reason) {
        Check((actual - expected).sqrMagnitude < 0.000001f, reason);
    }

    internal static int Run() {
        FramedMath();
        CaptureAndNeutral();
        CaptureHistoryIndices();
        LargeCoordinatesKeepPivot();
        ExactHistory();
        NormalMaskReuse();
        NonFiniteNormalsRollback();
        return s_Checks;
    }

    private static void FramedMath() {
        Vector3 pivot = new Vector3(2, 3, 4);
        Matrix4x4 rotation = SelectionTransformMath.CreateWorldRotation(
            pivot, Vector3.up, 90f, out bool neutral);
        Check(!neutral, "Quarter turn is not neutral");
        Near(rotation.MultiplyPoint3x4(new Vector3(3, 3, 4)), new Vector3(2, 3, 3), "Rotation about offset pivot");
        Matrix4x4 scaling = SelectionTransformMath.CreateWorldScale(pivot, s_Global, new Vector3(2, 3, 4));
        Near(scaling.MultiplyPoint3x4(new Vector3(3, 4, 5)), new Vector3(4, 6, 8), "Independent axis scale");
        TransformFrame custom = new TransformFrame(
            new Vector3(0, 0, -1), Vector3.up, Vector3.right);
        Matrix4x4 customScale = SelectionTransformMath.CreateWorldScale(pivot, custom, new Vector3(2, 1, 1));
        Near(customScale.MultiplyPoint3x4(new Vector3(3, 4, 5)), new Vector3(3, 4, 6), "Custom X scales world Z");

        foreach (float sign in new[] { 1f, -1f }) {
            Matrix4x4 root = Matrix4x4.TRS(new Vector3(7, -2, 5),
                Quaternion.AngleAxis(35, new Vector3(1, 2, 3)), new Vector3(sign * 2, 3, 0.7f));
            Check(SelectionTransformMath.TryGetLocalTransform(root, rotation, false, out Matrix4x4 local), "Root conversion accepted");
            Vector3 start = new Vector3(0.5f, 0.25f, 1);
            Near(root.MultiplyPoint3x4(local.MultiplyPoint3x4(start)),
                rotation.MultiplyPoint3x4(root.MultiplyPoint3x4(start)), "Rotation with nonuniform/mirrored root");
            Check(SelectionTransformMath.TryGetLocalTransform(root, customScale, false, out local), "Scale root conversion accepted");
            Near(root.MultiplyPoint3x4(local.MultiplyPoint3x4(start)),
                customScale.MultiplyPoint3x4(root.MultiplyPoint3x4(start)), "Custom scale with nonuniform/mirrored root");
        }

        Check(!SelectionTransformMath.TryGetLocalTransform(Matrix4x4.Scale(new Vector3(1, 0, 1)), rotation, false, out _), "Singular root rejected");
        Matrix4x4 invalid = Matrix4x4.identity;
        invalid.m00 = float.NaN;
        Check(!SelectionTransformMath.TryGetLocalTransform(Matrix4x4.identity, invalid, false, out _), "Non-finite matrix rejected");
    }

    private static void CaptureHistoryIndices() {
        var capture = new VertexTransformDragState();
        Check(capture.CreateHistoryIndices().Count == 0, "Empty capture has no history indices");
        var expected = new Dictionary<int, HashSet<int>>();
        var random = new Random(5301);
        for (int i = 0; i < 10000; i++) {
            int part = new[] { 0, 7, 4095 }[i % 3];
            int index = random.Next(2000) * 3;
            capture.AddRenderVertex(part, index, new Vector3(i, i, i));
            if (!expected.TryGetValue(part, out var set)) expected.Add(part, set = new HashSet<int>());
            set.Add(index);
        }
        var actual = capture.CreateHistoryIndices();
        Check(actual.Count == expected.Count && capture.RenderVertexCount == 10000, "History preparation preserves capture counts");
        foreach (var pair in expected) {
            var sorted = new int[pair.Value.Count]; pair.Value.CopyTo(sorted); Array.Sort(sorted);
            Check(actual[pair.Key].Length == sorted.Length, "History indices are deduplicated");
            for (int i = 0; i < sorted.Length; i++) Check(actual[pair.Key][i] == sorted[i], "Sorted per-part history indices match the selection set");
        }
        var again = capture.CreateHistoryIndices();
        foreach (var pair in actual) Check(!ReferenceEquals(pair.Value, again[pair.Key]), "Each history capture owns fresh arrays");
        int firstIndex = capture.GetRenderVertex(0).RenderVertexIndex;
        Check(firstIndex == actual[0][Array.BinarySearch(actual[0], firstIndex)], "Index preparation leaves render entry order intact");
        capture.Reset();
        for (int i = 0; i < 20000; i++) capture.AddRenderVertex(7, i, Vector3.zero);
        foreach (var pair in actual) {
            Check(pair.Value.Length == again[pair.Key].Length, "Reset does not resize old history arrays");
            for (int i = 0; i < pair.Value.Length; i++) Check(pair.Value[i] == again[pair.Key][i], "Reset and buffer growth do not overwrite history indices");
        }
        capture.Reset();
    }

    private static VertexTransformDragState Capture(ProceduralObject obj, int[] indices, int version = 7) {
        var capture = new VertexTransformDragState();
        capture.BeginCapture(obj.Id, GizmoHandle.RotateY, s_Global, default,
            Vector3.zero, 0, default, Vector3.zero, 1, obj.Transform, indices.Length);
        for (int part = 0; part < obj.Parts.Count; part++) {
            foreach (int index in indices) capture.AddRenderVertex(part, index, obj.Parts[part].Mesh.Vertices[index]);
        }
        capture.CaptureTargets(obj, version);
        return capture;
    }

    private static MeshPart Part(int count = 6) {
        var part = new MeshPart();
        part.Mesh.Vertices = new Vector3[count];
        part.Mesh.Normals = new Vector3[count];
        part.Mesh.Tangents = new Vector4[count];
        part.Mesh.Triangles = new int[count];
        for (int i = 0; i < count; i++) {
            int corner = i % 3;
            part.Mesh.Vertices[i] = new Vector3(i / 3 * 2 + (corner == 1 ? 1 : 0), corner == 2 ? 1 : 0, 0);
            part.Mesh.Normals[i] = new Vector3(0.123f, 0.456f, 0.789f);
            part.Mesh.Tangents[i] = new Vector4(1, 0, 0, -1);
            part.Mesh.Triangles[i] = i;
        }
        return part;
    }

    private static void CaptureAndNeutral() {
        var part = Part();
        part.Mesh.Vertices[4] = part.Mesh.Vertices[1];
        var obj = new ProceduralObject(1, Guid.NewGuid(), part);
        var capture = Capture(obj, new[] { 0, 1, 4 });
        Check(capture.MatchesTargets(obj, 7) && !capture.MatchesTargets(obj, 8), "Selection version bound to capture");
        Matrix4x4 rotate = SelectionTransformMath.CreateWorldRotation(Vector3.zero, Vector3.up, 90, out _);
        Check(capture.TryPrepareTransform(rotate, false, out bool changed) && changed, "Capture rotation");
        Near(capture.GetTransformedPosition(0), Vector3.zero, "Pivot vertex stays fixed");
        Near(capture.GetTransformedPosition(1), new Vector3(0, 0, -1), "Captured endpoint rotates");
        Check(capture.GetTransformedPosition(1).Equals(capture.GetTransformedPosition(2)), "Seam copies remain together");
        Check(capture.TryPrepareTransform(rotate, false, out changed) && !changed, "Identical input skips publication");
        Matrix4x4 fullTurn = SelectionTransformMath.CreateWorldRotation(Vector3.zero, Vector3.up, -720, out bool neutral);
        Check(neutral && capture.TryPrepareTransform(fullTurn, neutral, out changed) && changed, "Full turn returns to capture");
        for (int i = 0; i < capture.RenderVertexCount; i++)
            Check(capture.GetTransformedPosition(i).Equals(capture.GetRenderVertex(i).StartLocalPosition), "Neutral exact positions");
        capture.Reset();

        part.Mesh.Vertices[4] = new Vector3(float.MaxValue, 0, 0);
        capture = Capture(obj, new[] { 1, 4 });
        Vector3 before = part.Mesh.Vertices[1];
        Check(!capture.TryPrepareTransform(Matrix4x4.Scale(new Vector3(2, 2, 2)), false, out _), "Overflow rejected before publication");
        Check(part.Mesh.Vertices[1].Equals(before), "Failed staging never writes mesh");
        var replacement = new ProceduralObject(obj.Id, obj.PersistentId, Part());
        Check(!capture.MatchesTargets(replacement, 7), "Replaced part invalidates capture");
        capture.Reset();
    }

    private static void LargeCoordinatesKeepPivot() {
        var part = Part();
        var obj = new ProceduralObject(1, Guid.NewGuid(), part);
        obj.Transform = new ObjectTransform(new Vector3(17000, 240, -23000),
            Quaternion.AngleAxis(37f, new Vector3(1, 2, 3)), new Vector3(-2, 0.7f, 3));
        part.Mesh.Vertices[0] = new Vector3(0.12345f, 6.789f, 2.3456f);
        var capture = Capture(obj, new[] { 0, 1 });
        capture.LocalPivot = part.Mesh.Vertices[0];
        Matrix4x4 root = Matrix4x4.TRS(obj.Transform.Position, obj.Transform.Rotation, obj.Transform.Scale);
        Matrix4x4 worldRotation = SelectionTransformMath.CreateWorldRotation(
            root.MultiplyPoint3x4(capture.LocalPivot), Vector3.up, 38f, out bool neutral);
        Check(SelectionTransformMath.TryGetLocalLinearTransform(root, worldRotation, neutral, out Matrix4x4 local), "Large-coordinate transform");
        Check(capture.TryPrepareTransform(local, neutral, out _), "Large-coordinate preview");
        Check(capture.GetTransformedPosition(0).Equals(part.Mesh.Vertices[0]), "Pivot vertex stays bit-exact far from map origin");
        Matrix4x4 linearRoot = Matrix4x4.TRS(Vector3.zero, obj.Transform.Rotation, obj.Transform.Scale);
        Vector3 expectedOffset = Matrix4x4.Rotate(Quaternion.AngleAxis(38f, Vector3.up))
            .MultiplyVector(linearRoot.MultiplyVector(part.Mesh.Vertices[1] - capture.LocalPivot));
        Near(linearRoot.MultiplyVector(capture.GetTransformedPosition(1) - capture.LocalPivot), expectedOffset,
            "Pivot-relative operation uses full nonuniform root conversion");
        capture.Reset();
    }

    private static void Publish(ProceduralObject obj, VertexTransformDragState capture) {
        for (int i = 0; i < capture.RenderVertexCount; i++) {
            var entry = capture.GetRenderVertex(i);
            obj.Parts[entry.PartIndex].Mesh.Vertices[entry.RenderVertexIndex] = capture.GetTransformedPosition(i);
        }
    }

    private static void ExactHistory() {
        var system = new ProceduralObjectSystem();
        var obj = new ProceduralObject(1, Guid.NewGuid(), Part(), Part());
        system.Objects.Add(obj.Id, obj);
        var context = new HistoryContext(system, _ => { });
        using (var history = new ActionHistory(context)) {
            var indices = new[] { 0, 1, 2 };
            ulong before0 = GeometryStreams.Capture(obj.Parts[0], false).Hash();
            ulong before1 = GeometryStreams.Capture(obj.Parts[1], false).Hash();
            var capture = Capture(obj, indices);
            var capturedIndices = capture.CreateHistoryIndices();
            var first = new VertexPatchAction(obj, 0, capturedIndices[0], LocalizedText.Diagnostic("Rotate Vertices"));
            var second = new VertexPatchAction(obj, 1, capturedIndices[1], LocalizedText.Diagnostic("Rotate Vertices"));
            using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Rotate Vertices"),
                    new List<HistoryCaptureAction> { first, second }, null)) {
                Matrix4x4 rotate = SelectionTransformMath.CreateWorldRotation(Vector3.zero, Vector3.up, 90, out _);
                Check(capture.TryPrepareTransform(rotate, false, out _), "Multi-part preview prepared");
                Publish(obj, capture);
                first.Recalculate(obj.Parts[0]); second.Recalculate(obj.Parts[1]);
                Check(edit.Complete(), "Multi-part rotation commit");
            }
            Check(history.Count == 1, "One history entry across parts");
            ulong after0 = GeometryStreams.Capture(obj.Parts[0], false).Hash();
            ulong after1 = GeometryStreams.Capture(obj.Parts[1], false).Hash();
            capture.Reset();
            for (int i = 0; i < 100; i++) capture.AddRenderVertex(0, i, Vector3.zero);
            for (int i = 0; i < 40; i++) {
                history.Request(HistoryDirection.Undo);
                Check(GeometryStreams.Capture(obj.Parts[0], false).Hash() == before0 &&
                    GeometryStreams.Capture(obj.Parts[1], false).Hash() == before1, "Exact multipart undo");
                history.Request(HistoryDirection.Redo);
                Check(GeometryStreams.Capture(obj.Parts[0], false).Hash() == after0 &&
                    GeometryStreams.Capture(obj.Parts[1], false).Hash() == after1, "Exact multipart redo");
            }
            capture.Reset();
            capture = Capture(obj, indices);
            capturedIndices = capture.CreateHistoryIndices();
            first = new VertexPatchAction(obj, 0, capturedIndices[0], LocalizedText.Diagnostic("Scale Vertices"));
            second = new VertexPatchAction(obj, 1, capturedIndices[1], LocalizedText.Diagnostic("Scale Vertices"));
            using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Scale Vertices"),
                    new List<HistoryCaptureAction> { first, second }, null)) {
                Check(capture.TryPrepareTransform(Matrix4x4.Scale(new Vector3(2, 0.5f, 1)), false, out _), "Scale preview prepared");
                Publish(obj, capture);
                first.Recalculate(obj.Parts[0]); second.Recalculate(obj.Parts[1]);
            }
            Check(history.Count == 1 && GeometryStreams.Capture(obj.Parts[0], false).Hash() == after0 &&
                GeometryStreams.Capture(obj.Parts[1], false).Hash() == after1, "Scale cancel exact across parts");
            capture.Reset();
            capture = Capture(obj, indices);
            capturedIndices = capture.CreateHistoryIndices();
            first = new VertexPatchAction(obj, 0, capturedIndices[0], LocalizedText.Diagnostic("Scale Vertices"));
            second = new VertexPatchAction(obj, 1, capturedIndices[1], LocalizedText.Diagnostic("Scale Vertices"));
            using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Scale Vertices"),
                    new List<HistoryCaptureAction> { first, second }, null)) {
                Check(capture.TryPrepareTransform(Matrix4x4.Scale(new Vector3(0.01f, 0.01f, 0.01f)), false, out _), "Minimum scale prepared");
                Publish(obj, capture);
                first.Recalculate(obj.Parts[0]); second.Recalculate(obj.Parts[1]);
                Check(edit.Complete(), "Scale commit");
            }
            Check(history.Count == 2, "Scale creates one more entry");
            ulong scaled = GeometryStreams.Capture(obj.Parts[0], false).Hash();
            history.Request(HistoryDirection.Undo);
            Check(GeometryStreams.Capture(obj.Parts[0], false).Hash() == after0, "Exact scale undo");
            history.Request(HistoryDirection.Redo);
            Check(GeometryStreams.Capture(obj.Parts[0], false).Hash() == scaled, "Exact scale redo");
            capture.Reset();
            capture = Capture(obj, indices);
            capturedIndices = capture.CreateHistoryIndices();
            first = new VertexPatchAction(obj, 0, capturedIndices[0], LocalizedText.Diagnostic("Rotate Vertices"));
            second = new VertexPatchAction(obj, 1, capturedIndices[1], LocalizedText.Diagnostic("Rotate Vertices"));
            using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Rotate Vertices"),
                    new List<HistoryCaptureAction> { first, second }, null)) {
                capture.TryPrepareTransform(Matrix4x4.Rotate(Quaternion.AngleAxis(37, Vector3.up)), false, out _);
                Publish(obj, capture);
                first.Recalculate(obj.Parts[0]); second.Recalculate(obj.Parts[1]);
                capture.TryPrepareTransform(Matrix4x4.identity, true, out _);
                Publish(obj, capture);
                first.Recalculate(obj.Parts[0]); second.Recalculate(obj.Parts[1]);
                Check(edit.Complete(), "Return-to-start commit");
            }
            Check(history.Count == 2 && GeometryStreams.Capture(obj.Parts[0], false).Hash() == scaled,
                "Return to start keeps exact normals and adds no history");
            capture.Reset();
        }
    }

    private static void NormalMaskReuse() {
        s_Checks += NormalUpdateTests.Run();
    }

    private static void NonFiniteNormalsRollback() {
        var system = new ProceduralObjectSystem();
        var part = Part();
        var obj = new ProceduralObject(1, Guid.NewGuid(), part);
        system.Objects.Add(obj.Id, obj);
        var context = new HistoryContext(system, _ => { });
        using (var history = new ActionHistory(context)) {
            ulong before = GeometryStreams.Capture(part, false).Hash();
            var action = new VertexPatchAction(obj, 0, new[] { 1, 2 }, LocalizedText.Diagnostic("Scale Vertices"));
            using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Scale Vertices"),
                    new List<HistoryCaptureAction> { action }, null)) {
                part.Mesh.Vertices[1] = new Vector3(1e25f, 0, 0);
                part.Mesh.Vertices[2] = new Vector3(0, 1e25f, 0);
                action.Recalculate(part);
                Check(!action.HasFiniteNormals(part), "Finite positions producing invalid normals are rejected");
            }
            Check(history.Count == 0 && GeometryStreams.Capture(part, false).Hash() == before,
                "Invalid normal preview rolls back exactly");
        }
    }

}
