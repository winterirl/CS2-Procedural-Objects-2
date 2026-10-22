using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Core.Deformation;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Geometry.Transforms;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Materials;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Rendering.Objects;
using PO2.Tools.ObjectTool;

internal static partial class Program {
    private static MeshPart MirrorFixture() {
        // A UV seam, a folded quad with nonconsecutive triangles, and a separate
        // material area exercise the data that a negative scale alone would miss.
        var vertices = new[] {
            new Vector3(1, 0, 0), new Vector3(4, 0, 0),
            new Vector3(4, 2, 0), new Vector3(1, 2, 1),
            new Vector3(1, 0, 0), new Vector3(-1, 0, 2), new Vector3(1, 2, 1)
        };
        var normals = Enumerable.Repeat(Vector3.forward, 7).ToArray();
        var tangents = Enumerable.Repeat(new Vector4(1, 0, 0, -1), 7).ToArray();
        var uv = Enumerable.Range(0, 7).Select(i => new Vector4(i, i * .2f, 3, 4)).ToArray();
        var uv1 = Enumerable.Range(0, 7).Select(i => new Vector4(2, i, 5, 9)).ToArray();
        var colors = Enumerable.Range(0, 7).Select(i => new Color32((byte)i, 90, 120, 255)).ToArray();
        var mesh = new MeshData(vertices, normals, tangents, uv, colors, uv1,
            (Vector4[])uv1.Clone(), new[] { 0, 1, 2, 4, 5, 6, 0, 2, 3 });
        var original = MaterialData.CreateDefault();
        var area = new SurfaceArea(1, original.Clone(),
            originalState: new SurfaceAreaOriginalState(original, new ColorMaskData()));
        return MeshPart.Restore("Mirror fixture", mesh, new[] { 0, 1, 2, 3, 0, 4, 3 }, 5,
            new[] { new StructuralFace(0, 0, 1, 2, 3), new StructuralFace(1, 4, 5, 6) },
            new List<SurfaceArea> { new SurfaceArea(0, original), area },
            new[] { 0, 1, 0 }, 2, PO2.Geometry.Topology.StructuralTopologySource.Authored, new Vector3(2, 1, .5f));
    }

    private static void MirrorNear(Vector3 actual, Vector3 expected, string label) {
        Check((actual - expected).sqrMagnitude < 0.000002f, label);
    }

    private static void MirrorSame(TopologyState expected, MeshPart actual, string label) {
        Check(TopologyState.Equal(expected.Signature(), TopologyState.Capture(actual).Signature()), label);
    }

    private static void MirrorTests() {
        PreparationLifetimeTest();
        MirrorSizeTests();
        MirrorTopologyReuseTests();
        MirrorGeometryTests();
        MirrorKernelEdgeTests();
        MirrorCommandTests();
        MirrorGuardAndRollbackTests();
        MirrorAsyncFailureTests();
        MirrorRendererTests();
        MirrorHistoryStorageTests();
    }

    private static void MirrorGeometryTests() {
        var axes = new[] { Vector3.right, Vector3.up, Vector3.forward, new Vector3(1, 2, 3).normalized };
        var rotations = new[] { Quaternion.identity, Quaternion.AngleAxis(67, new Vector3(1, 2, 3).normalized) };
        foreach (var rotation in rotations) foreach (var axis in axes) {
            var source = MirrorFixture();
            var before = TopologyState.Capture(source);
            var obj = new ProceduralObject(9301, Guid.NewGuid(), source);
            obj.Transform = new ObjectTransform(new Vector3(40000, 600, -40000), rotation, new Vector3(2, .6f, 3));
            obj.SetLocalPivot(new Vector3(.5f, -.75f, 1));
            Check(MirrorGeometry.TryPrepare(obj, -1, axis, out var prepared, out var reason), "Prepare mirror: " + reason);
            MirrorSame(before, source, "Preparation leaves the source untouched");
            var matrix = MirrorGeometry.CreateLocalReflection(obj.Transform, obj.LocalPivot, axis);
            var capture = PersistencePartData.Capture(source);
            var mirrored = MirrorGeometry.TransformCapture(capture, matrix, false, CancellationToken.None);
            var target = mirrored.RestoreOwned();
            MirrorCompareGeometry(prepared[0], target);
            using (var delta = new MirrorDelta(capture, mirrored, CancellationToken.None)) {
                var reader = delta.Prepare(CancellationToken.None).GetAwaiter().GetResult();
                var restoredData = delta.Apply(mirrored, HistoryDirection.Undo, reader, CancellationToken.None);
                MirrorSame(before, restoredData.RestoreOwned(), "Mirror delta restores exact source bytes");
            }
            Check(!ReferenceEquals(source.Mesh.UV0, target.Mesh.UV0), "Owned candidate stays detached from live streams");
            Check(ReferenceEquals(capture.UV0, target.Mesh.UV0), "Owned candidate reuses detached unchanged streams");
            var restored = mirrored.Restore();
            Check(!ReferenceEquals(restored.Mesh.Vertices, mirrored.Vertices) && !ReferenceEquals(restored.Mesh.UV0, mirrored.UV0),
                "Ordinary persistence restore retains independent ownership");
            var basis = Matrix4x4.TRS(Vector3.zero, rotation, obj.Transform.Scale);
            for (int i = 0; i < source.Mesh.Vertices.Length; i++) {
                var relative = basis.MultiplyVector(source.Mesh.Vertices[i] - obj.LocalPivot);
                var expected = relative - axis * (2f * Vector3.Dot(relative, axis));
                MirrorNear(basis.MultiplyVector(target.Mesh.Vertices[i] - obj.LocalPivot), expected, "World-space reflection");
                var normal = basis.inverse.transpose.MultiplyVector(source.Mesh.Normals[i]).normalized;
                var reflectedNormal = normal - axis * (2f * Vector3.Dot(normal, axis));
                MirrorNear(basis.inverse.transpose.MultiplyVector(target.Mesh.Normals[i]).normalized, reflectedNormal, "Reflected normal");
                Check(target.Mesh.Tangents[i].w == -source.Mesh.Tangents[i].w, "Tangent handedness reversed");
                Check(Math.Abs(Vector3.Dot(target.Mesh.Normals[i], new Vector3(target.Mesh.Tangents[i].x,
                    target.Mesh.Tangents[i].y, target.Mesh.Tangents[i].z))) < 0.0001f, "Tangent stays perpendicular");
                Check(target.Topology.GetLogicalVertexIdForRenderVertex(i) == source.Topology.GetLogicalVertexIdForRenderVertex(i), "Seam groups preserved");
            }
            for (int i = 0; i < target.StructuralTopology.FaceCount; i++) {
                Check(target.TryGetStructuralFaceTriangles(i, out var faces) && faces.Count == (i == 0 ? 2 : 1), "Structural face remains editable");
            }
            // Compare geometric normals per face, including the swapped quad halves.
            for (int tri = 0; tri < 3; tri++) {
                int sourceTri = tri == 0 ? 2 : tri == 2 ? 0 : 1;
                Vector3 FaceNormal(MeshData m, int t) => Vector3.Cross(
                    m.Vertices[m.Triangles[t * 3 + 1]] - m.Vertices[m.Triangles[t * 3]],
                    m.Vertices[m.Triangles[t * 3 + 2]] - m.Vertices[m.Triangles[t * 3]]).normalized;
                MirrorNear(FaceNormal(target.Mesh, tri), matrix.inverse.transpose.MultiplyVector(FaceNormal(source.Mesh, sourceTri)).normalized, "Outward winding");
            }
            Check(target.TriangleSurfaceAreaIds.SequenceEqual(source.TriangleSurfaceAreaIds), "Material membership preserved");
            Check(target.SurfaceAreas[1].HasOriginalState, "Original material retained for Reset");
            Check(target.Mesh.UV0.SequenceEqual(source.Mesh.UV0) && target.Mesh.TexCoord1.SequenceEqual(source.Mesh.TexCoord1) &&
                target.Mesh.TexCoord3.SequenceEqual(source.Mesh.TexCoord3) && target.Mesh.VertexColors.SequenceEqual(source.Mesh.VertexColors), "All appearance channels preserved");
            MirrorNear(target.LocalPivot, matrix.MultiplyPoint3x4(source.LocalPivot), "Child pivot follows object mirror");
            source.ApplyHistoryTopology(target); source.SetLocalPivot(target.LocalPivot);
            Check(MirrorGeometry.TryPrepare(obj, -1, axis, out var twice, out reason), "Second reflection");
            var original = before.Restore();
            for (int i = 0; i < source.Mesh.Vertices.Length; i++) MirrorNear(twice[0].Mesh.Vertices[i], original.Mesh.Vertices[i], "Two mirrors return to starting shape");
            Check(twice[0].Mesh.Triangles.SequenceEqual(original.Mesh.Triangles), "Two mirrors restore winding and triangle order");
            Check(MirrorGeometry.TryPrepare(obj, 0, axis, out var partOnly, out reason), "Part reflection");
            Check(partOnly[0].LocalPivot.Equals(source.LocalPivot), "Part mirror keeps its pivot exact");
        }
    }

    private static void MirrorCompareGeometry(MeshPart expected, MeshPart actual) {
        Check(actual.Mesh.Vertices.Length == expected.Mesh.Vertices.Length, "Kernel vertex count matches reference");
        for (int i = 0; i < actual.Mesh.Vertices.Length; i++) {
            MirrorNear(actual.Mesh.Vertices[i], expected.Mesh.Vertices[i], "Kernel position matches reference");
            if (actual.Mesh.Normals != null) MirrorNear(actual.Mesh.Normals[i], expected.Mesh.Normals[i], "Kernel normal matches reference");
            if (actual.Mesh.Tangents != null) {
                var a = actual.Mesh.Tangents[i]; var e = expected.Mesh.Tangents[i];
                MirrorNear(new Vector3(a.x, a.y, a.z), new Vector3(e.x, e.y, e.z), "Kernel tangent matches reference");
                Check(a.w == e.w, "Kernel handedness matches reference");
            }
        }
        Check(actual.Mesh.Triangles.SequenceEqual(expected.Mesh.Triangles), "Kernel winding and quad triangle order match reference");
        Check(TopologyState.Capture(actual).Streams[8].SequenceEqual(TopologyState.Capture(expected).Streams[8]),
            "Kernel structural faces match reference");
    }

    private static void MirrorKernelEdgeTests() {
        var matrix = MirrorGeometry.CreateLocalReflection(ObjectTransform.IdentityAt(Vector3.zero), Vector3.one, Vector3.right);
        foreach (bool streams in new[] { false, true }) {
            var source = Single(3, streams: streams);
            if (streams) {
                // Zero directions and a tangent parallel to its normal use the
                // same fallback as the shared transform implementation.
                source.Mesh.Normals[0] = Vector3.zero;
                source.Mesh.Normals[1] = Vector3.up;
                source.Mesh.Normals[2] = new Vector3(1e-8f, 0, 0);
                source.Mesh.Tangents[0] = new Vector4(0, 0, 0, 1);
                source.Mesh.Tangents[1] = new Vector4(0, 1, 0, -1);
                source.Mesh.Tangents[2] = new Vector4(1e-8f, 0, 0, 0);
            }
            Check(PartGeometryTransform.TryCloneTransformed(source, matrix, source.Name, out var expected, out _), "Reference edge transform");
            var capture = PersistencePartData.Capture(source);
            var result = MirrorGeometry.TransformCapture(capture, matrix, true, CancellationToken.None).RestoreOwned();
            MirrorCompareGeometry(expected, result);
            Check(result.LocalPivot.Equals(source.LocalPivot), "Kernel preserves the active part pivot exactly");
            Check((result.Mesh.Normals == null) == !streams && (result.Mesh.Tangents == null) == !streams, "Kernel preserves absent streams");
            using (var cancel = new CancellationTokenSource()) {
                cancel.Cancel(); bool rejected = false;
                try { MirrorGeometry.TransformCapture(capture, matrix, true, cancel.Token); }
                catch (OperationCanceledException) { rejected = true; }
                Check(rejected, "Kernel respects cancellation before work");
            }
        }
        foreach (int stream in new[] { 0, 1, 2, 3 }) {
            var capture = PersistencePartData.Capture(MirrorFixture());
            if (stream == 0) capture.Vertices[0] = new Vector3(float.NaN, 0, 0);
            if (stream == 1) capture.Normals[0] = new Vector3(0, float.PositiveInfinity, 0);
            if (stream == 2) capture.Tangents[0] = new Vector4(float.NaN, 0, 0, 1);
            if (stream == 3) capture.Tangents[0] = new Vector4(1, 0, 0, float.PositiveInfinity);
            bool rejected = false;
            try { MirrorGeometry.TransformCapture(capture, matrix, true, CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Kernel rejects nonfinite geometry stream " + stream);
        }
        // Cross both kernel batch boundaries, including a short final batch.
        var positions = new Vector3[12291];
        var faces = new StructuralFace[positions.Length / 3];
        for (int i = 0; i < faces.Length; i++) {
            int at = i * 3;
            positions[at] = new Vector3(i * 2, 0, 0);
            positions[at + 1] = new Vector3(i * 2 + 1, 0, 0);
            positions[at + 2] = new Vector3(i * 2, 1, 0);
            faces[i] = new StructuralFace(i, at, at + 1, at + 2);
        }
        var large = Make(positions, faces);
        Check(PartGeometryTransform.TryCloneTransformed(large, matrix, large.Name, out var reference, out _), "Reference multi-batch transform");
        var transformed = MirrorGeometry.TransformCapture(PersistencePartData.Capture(large), matrix, false, CancellationToken.None);
        MirrorCompareGeometry(reference, transformed.RestoreOwned());
    }

    private static bool FinishMirror(ProceduralObjectSystem system, out LocalizedText reason) {
        bool completed = false, success = false; LocalizedText status = LocalizedText.Empty;
        WaitDelete(() => {
            if (!completed) {
                var outcome = system.PollMirrorEdit(out status);
                completed = outcome != OperationOutcome.Pending;
                success = outcome == OperationOutcome.Succeeded;
            }
            return completed && !system.MirrorEditBusy;
        }, "Mirror preparation finishes");
        reason = status; return success;
    }

    private static void MirrorCommandTests() {
        var a = MirrorFixture(); var b = MirrorFixture();
        b.SetLocalPivot(new Vector3(7, 2, -1));
        var obj = new ProceduralObject(9302, Guid.NewGuid(), a, b);
        obj.Transform = new ObjectTransform(Vector3.zero, Quaternion.identity, Vector3.one);
        obj.SetLocalPivot(new Vector3(-2, 3, 1));
        var system = new ProceduralObjectSystem { MirrorTestSelected = obj };
        system.Objects.Add(obj.Id, obj);
        var context = new HistoryContext(system);
        system.m_HistoryContext = context;
        using (var history = new ActionHistory(context, null)) {
            system.ActionHistory = history;
            var before = obj.Parts.Select(TopologyState.Capture).ToArray();
            var ids = obj.Parts.Select(p => p.PartHistoryId).ToArray();
            Check(system.TryMirrorSelected(-1, Vector3.right, LocalizedText.Diagnostic("Mirror Object X"), out var reason), "Multipart command: " + reason);
            Check(history.Count == 0 && history.IsBusy, "Mirror reserves history without synchronous publication");
            for (int i = 0; i < 2; i++) MirrorSame(before[i], obj.Parts[i], "Initial mirror command leaves live geometry untouched");
            Check(FinishMirror(system, out reason), "Multipart mirror publishes: " + reason);
            Check(history.Count == 1, "Multipart mirror creates one history entry");
            var after = obj.Parts.Select(TopologyState.Capture).ToArray();
            for (int pass = 0; pass < 8; pass++) {
                Check(history.Request(HistoryDirection.Undo), "Mirror undo starts"); WaitDelete(() => { history.Poll(); return !history.IsBusy; }, "Mirror history publishes asynchronously");
                for (int i = 0; i < 2; i++) MirrorSame(before[i], obj.Parts[i], "Undo restores exact geometry, appearance, and pivot");
                Check(history.Request(HistoryDirection.Redo), "Mirror redo starts"); WaitDelete(() => { history.Poll(); return !history.IsBusy; }, "Mirror history publishes asynchronously");
                for (int i = 0; i < 2; i++) MirrorSame(after[i], obj.Parts[i], "Redo restores exact mirrored state");
            }
            Check(obj.Parts.Select(p => p.PartHistoryId).SequenceEqual(ids), "History identities preserved");
            Check(obj.LocalPivot.Equals(new Vector3(-2, 3, 1)), "Object pivot remains unchanged");
            Check(system.TryMirrorSelected(1, Vector3.up, LocalizedText.Diagnostic("Mirror Part Y"), out reason), "Active part command");
            Check(FinishMirror(system, out reason), "Active part publishes: " + reason);
            MirrorSame(after[0], a, "Active part leaves sibling untouched");
            Check(history.Count == 2, "Active part adds one action");
            Check(history.Request(HistoryDirection.Undo), "Part undo starts"); WaitDelete(() => { history.Poll(); return !history.IsBusy; }, "Mirror history publishes asynchronously");
            MirrorSame(after[1], b, "Part undo restores pivot and geometry");
            var data = PersistenceObjectData.Capture(obj);
            var saved = ObjectBlockReader.Read(ObjectBlockWriter.Write(data), SnapshotFormat.kObjectSchemaVersion).Restore(obj.Id);
            for (int i = 0; i < 2; i++) MirrorSame(after[i], saved.Parts[i], "Saved mirrored part round-trip");

            obj.SetLocked(true);
            Check(!system.TryMirrorSelected(-1, Vector3.right, LocalizedText.Diagnostic("Locked"), out reason), "Locked object rejects mirror");
            obj.SetLocked(false);
            Check(!system.TryMirrorSelected(2, Vector3.right, LocalizedText.Diagnostic("Invalid part"), out reason), "Invalid part rejected");
            Check(!system.TryMirrorSelected(-1, Vector3.zero, LocalizedText.Diagnostic("Invalid axis"), out reason), "Zero axis rejected");
            Check(!system.TryMirrorSelected(-1, new Vector3(float.NaN, 0, 0), LocalizedText.Diagnostic("Invalid axis"), out reason), "Nonfinite axis rejected");
            // Failure on a later part must not change an earlier one.
            b.Mesh.Vertices[0] = new Vector3(float.NaN, 0, 0);
            Check(system.TryMirrorSelected(-1, Vector3.right, LocalizedText.Diagnostic("Invalid part geometry"), out reason), "Later-part validation is asynchronous");
            Check(!FinishMirror(system, out reason), "Invalid later part rejected before publication");
            MirrorSame(after[0], a, "Failed preparation leaves earlier part untouched");
            Check(history.Count == 2, "Rejected actions do not enter history");
        }
    }

    private static void MirrorGuardAndRollbackTests() {
        var bare = Single(3, streams: false);
        var obj = new ProceduralObject(9303, Guid.NewGuid(), MirrorFixture(), bare);
        obj.Transform = new ObjectTransform(Vector3.zero, Quaternion.identity, Vector3.one);
        Check(MirrorGeometry.TryPrepare(obj, 1, Vector3.forward, out var prepared, out var reason), "Mirror with missing optional streams");
        Check(prepared[0].Mesh.Normals == null && prepared[0].Mesh.Tangents == null, "Absent streams remain absent");
        var part = obj.Parts[0];
        Check(PartGeometrySnapshot.TryCapture(part, false, out var snapshot, out reason), "Capture deformation baseline");
        var cage = DeformationCage.Capture(obj, 0, Matrix4x4.identity, new[] { snapshot });
        obj.SetDeformation(0, cage);
        Check(!MirrorGeometry.TryPrepare(obj, -1, Vector3.right, out _, out reason), "Object mirror rejects unapplied part deformation");
        Check(!MirrorGeometry.TryPrepare(obj, 0, Vector3.right, out _, out reason), "Part mirror rejects its unapplied deformation");
        Check(MirrorGeometry.TryPrepare(obj, 1, Vector3.right, out _, out reason), "Unrelated part deformation does not block target");
        obj.SetDeformation(0, null);

        var system = new ProceduralObjectSystem { MirrorTestSelected = obj };
        system.Objects.Add(obj.Id, obj);
        var context = new HistoryContext(system);
        system.m_HistoryContext = context;
        using (var history = new ActionHistory(context, null)) {
            system.ActionHistory = history;
            var before = TopologyState.Capture(part);

            using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Cancelled Mirror"),
                new List<HistoryCaptureAction> { new TopologyAction(obj, 0,
                    Enumerable.Range(0, part.Mesh.Vertices.Length).ToArray(), LocalizedText.Diagnostic("Cancelled Mirror"), context, true) }, null)) {
                Check(!system.TryMirrorSelected(-1, Vector3.right, LocalizedText.Diagnostic("Busy"), out _), "Another history transaction blocks mirror");
                Check(MirrorGeometry.TryPrepare(obj, -1, Vector3.right, out prepared, out reason), "Prepare cancelled mirror");
                part.ApplyHistoryTopology(prepared[0]);
                part.SetLocalPivot(prepared[0].LocalPivot);
                // Dispose before completion simulates a failure midway through a multipart edit.
            }
            MirrorSame(before, part, "Cancelled mirror restores exact geometry and pivot");
            Check(history.Count == 0, "Cancelled mirror leaves no history entry");
        }
    }
    private static void MirrorAsyncFailureTests() {
        foreach (string failure in new[] { "cancel", "late cancel", "later publication", "selection", "transform", "history clear", "lock" }) {
            var obj = new ProceduralObject(9310, Guid.NewGuid(), MirrorFixture(), MirrorFixture());
            obj.Transform = ObjectTransform.IdentityAt(Vector3.zero);
            var system = new ProceduralObjectSystem { MirrorTestSelected = obj };
            system.Objects.Add(obj.Id, obj); system.m_HistoryContext = new HistoryContext(system);
            using (system.ActionHistory = new ActionHistory(system.m_HistoryContext)) {
                var before = obj.Parts.Select(TopologyState.Capture).ToArray();
                Check(system.TryMirrorSelected(-1, Vector3.forward, LocalizedText.Diagnostic("Mirror Object Z"), out var reason), "Begin failure case " + failure);
                Check(!system.TryMirrorSelected(-1, Vector3.right, LocalizedText.Diagnostic("Overlap"), out reason), "Reject overlapping mirror");
                // Let part one finish staging before the second part fails.
                if (failure == "later publication") system.RejectTopologyPublicationAt = 2;
                if (failure == "late cancel") {
                    WaitDelete(() => {
                        system.PollMirrorEdit(out _);
                        return system.TestMirrorFirstPartPrepared;
                    }, "First part finishes before cancellation");
                }
                if (failure == "cancel" || failure == "late cancel") system.CancelMirrorEdit();
                if (failure == "selection") system.MirrorTestSelected = null;
                if (failure == "transform") obj.Transform.Position = Vector3.one;
                if (failure == "history clear") system.ActionHistory.Clear();
                if (failure == "lock") obj.SetLocked(true);
                Check(!FinishMirror(system, out reason), "Failure leaves command uncommitted: " + failure);
                for (int i = 0; i < 2; i++) MirrorSame(before[i], obj.Parts[i], "Atomic mirror rollback: " + failure);
                Check(system.ActionHistory.Count == 0 && !system.ActionHistory.IsBusy, "Failure releases history reservation");
                if (failure == "later publication") Check(system.TopologyAdoptions == 2 && system.TopologyRollbacks == 2, "Later renderer failure rolls back both parts");
            }
        }
        var target = new ProceduralObject(9311, Guid.NewGuid(), MirrorFixture());
        target.Transform = ObjectTransform.IdentityAt(Vector3.zero);
        var owner = new ProceduralObjectSystem { MirrorTestSelected = target };
        owner.Objects.Add(target.Id, target); owner.m_HistoryContext = new HistoryContext(owner);
        using (owner.ActionHistory = new ActionHistory(owner.m_HistoryContext)) {
            var tool = new ProceduralObjectToolSystem { m_ProceduralObjectSystem = owner };
            tool.EnterMirrorForTest(); Check(tool.CanMirrorSelected, "Mirror tool starts idle");
            tool.MirrorSelected(0);
            Check(tool.TopologyEditBusy && tool.MeshMutationBusy && !tool.CanMirrorSelected, "Async mirror blocks conflicting tool mutations");
            owner.CancelMirrorEdit();
            WaitDelete(() => { tool.TickVertexDelete(); return !tool.TopologyEditBusy; }, "Tool polls mirror cancellation");
            Check(tool.CanMirrorSelected, "Tool resumes after cancellation");
            tool.MirrorSelected(1); tool.StopVertexDelete();
            WaitDelete(() => !owner.MirrorEditBusy, "Tool shutdown drains detached work");
            Check(owner.ActionHistory.Count == 0 && !owner.ActionHistory.IsBusy, "Tool shutdown releases mirror reservation");
        }
    }

    private static void MirrorRendererTests() {
        foreach (var mode in new[] { TextureMappingMode.Original, TextureMappingMode.Repeat, TextureMappingMode.Stretch }) {
            var parts = new[] { MirrorFixture(), MirrorFixture() };
            foreach (var part in parts) foreach (var area in part.SurfaceAreas) part.SetSurfaceAreaMappingMode(area.Id, mode);
            parts[1].SetLocalPivot(new Vector3(3, 5, 9));
            parts[1].SetSurfaceAreaVanillaSpecialMaterial(1, VanillaSpecialMaterialKind.Glass);
            parts[1].SetSurfaceAreaMappingMode(1, TextureMappingMode.Original);
            var obj = new ProceduralObject(9312, Guid.NewGuid(), parts);
            obj.Transform = ObjectTransform.IdentityAt(Vector3.zero);
            obj.SetLocalPivot(new Vector3(-2, 3, 1));
            var before = parts.Select(TopologyState.Capture).ToArray();
            var renderer = new ProceduralObjectRenderer { TestAdditionalRenderer = InitialRenderer };
            renderer.TestInitialize(obj, InitialRenderer(parts[0], Vector3.one));
            var system = new ProceduralObjectSystem { MirrorTestSelected = obj, TestTopologyRenderer = renderer };
            system.Objects.Add(obj.Id, obj); system.m_HistoryContext = new HistoryContext(system);
            using (system.ActionHistory = new ActionHistory(system.m_HistoryContext)) {
                var old = new[] { renderer.TestMesh(obj, 0), renderer.TestMesh(obj, 1) };
                Check(system.TryMirrorSelected(-1, Vector3.right, LocalizedText.Diagnostic("Mirror Object X"), out var reason), "Staged renderer mirror begins");
                bool completed = false, success = false; int polls = 0;
                WaitDelete(() => {
                    if (completed) return true;
                    var outcome = system.PollMirrorEdit(out reason); polls++;
                    completed = outcome != OperationOutcome.Pending;
                    success = outcome == OperationOutcome.Succeeded;
                    if (!completed) for (int i = 0; i < 2; i++) {
                        Check(ReferenceEquals(old[i], renderer.TestMesh(obj, i)), "Old renderer remains visible until all parts are ready");
                        MirrorSame(before[i], parts[i], "Model unchanged while renderer uploads");
                    }
                    return completed;
                }, "Real mirror renderer publishes");
                Check(success, "Real mirror publication: " + reason);
                Check(polls > 1, "Mirror workers complete asynchronously");
                WaitDelete(() => !system.MirrorEditBusy, "Mirror buffers drain");
                var after = parts.Select(TopologyState.Capture).ToArray();
                for (int pass = 0; pass < 2; pass++) foreach (var direction in new[] { HistoryDirection.Undo, HistoryDirection.Redo }) {
                    Check(system.ActionHistory.Request(direction), "Staged mirror replay begins");
                    WaitDelete(() => { system.ActionHistory.Poll(); return !system.ActionHistory.IsBusy; }, "Staged mirror replay completes");
                    Check(system.ActionHistory.Count == 1, "Replay retained valid mirror history: " + system.ActionHistory.LastFailure);
                    for (int i = 0; i < 2; i++) {
                        MirrorSame(direction == HistoryDirection.Undo ? before[i] : after[i], parts[i], "Real renderer replay exact state");
                        Check(renderer.TestVersionMatches(obj, i), "Published renderer already matches model versions");
                        using (var expected = InitialRenderer(parts[i], Vector3.one)) {
                            Check(renderer.TestMesh(obj, i).vertices.SequenceEqual(expected.Upload.Positions), "Rendered mirrored positions match model");
                            Check(renderer.TestMesh(obj, i).TestIndices.SequenceEqual(expected.Upload.Indices), "Rendered mirrored winding matches model");
                        }
                    }
                }
                if (mode == TextureMappingMode.Original) {
                    Check(old.All(mesh => mesh.TestReadLeaseAcquisitions == 0), "Original mapping skips current tangent leases");
                }
                else {
                    var visible = renderer.TestMesh(obj);
                    // Simulate one indivisible Unity call consuming the frame budget.
                    // The scheduler must yield with the lease still held so clearing
                    // history can exercise cancellation at this ownership boundary.
                    visible.TestReadLeaseDelayMilliseconds = 5;
                    Check(system.ActionHistory.Request(HistoryDirection.Undo), "Start replay cancellation during renderer capture");
                    WaitDelete(() => {
                        system.ActionHistory.Poll();
                        return visible.ReadLeases > 0 || !system.ActionHistory.IsBusy;
                    }, "Mirror replay reaches native renderer capture");
                    Check(visible.ReadLeases > 0 && system.ActionHistory.IsBusy, "Replay owns a renderer read lease before cancellation");
                    system.ActionHistory.Clear();
                    visible.TestReadLeaseDelayMilliseconds = 0;
                    Check(visible.ReadLeases == 0 && ReferenceEquals(visible, renderer.TestMesh(obj)), "Clearing pending mirror replay releases native lease and keeps old renderer");
                    for (int i = 0; i < 2; i++) MirrorSame(after[i], parts[i], "Cancelled replay leaves every live part untouched");
                }
            }
            renderer.TestDispose(); Check(renderer.TestMaterialReferences == 0, "Mirror renderer releases staged and retired materials");
        }
    }

}
