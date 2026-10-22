using System;
using System.Collections.Generic;
using System.IO;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Core.Deformation;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Tools.Gizmos;

internal static class CageTests {
    private static int s_Checks;
    private static void Check(bool value, string reason) { s_Checks++; if (!value) throw new Exception(reason); }
    private static void Near(Vector3 a, Vector3 b, string reason, float epsilon = 0.00001f) =>
        Check((a - b).sqrMagnitude < epsilon * epsilon, reason);

    private static ProceduralObject Object(int id = 1) {
        var part = new MeshPart();
        part.Mesh.Vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) };
        part.Mesh.Normals = new[] { new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 1f) };
        part.Mesh.Tangents = new[] { new Vector4(1,0,0,-1), new Vector4(1,0,0,-1), new Vector4(1,0,0,-1), new Vector4(1,0,0,-1) };
        return new ProceduralObject(id, Guid.NewGuid(), part) { Transform = ObjectTransform.IdentityAt(Vector3.zero) };
    }

    internal static int Run() {
        MathAndDirections();
        LargeStretchValidity();
        PersistenceAndHistory();
        SessionLifecycle();
        MeshBakeScopes();
        BoundsAndDisjointParts();
        return s_Checks;
    }

    private static void MathAndDirections() {
        var corners = DeformationCage.ReferenceCorners(Matrix4x4.identity);
        Check(DeformationRecipe.TryCreate(Matrix4x4.identity, corners, out var recipe, out _), "Identity cage");
        Vector3 point = new Vector3(0.2f, 0.3f, 0.4f);
        Check(recipe.Position(point).Equals(point), "Identity preserves position bits");
        corners[7] += new Vector3(0.2f, -0.1f, 0.15f);
        Check(DeformationRecipe.TryCreate(Matrix4x4.identity, corners, out recipe, out _), "Single corner cage");
        Near(recipe.Position(point), point + new Vector3(0.2f, -0.1f, 0.15f) * (point.x * point.y * point.z), "Independent corner weight");
        Near(recipe.Position(Vector3.zero), Vector3.zero, "Opposite corner remains fixed");
        var j = recipe.Jacobian(point);
        foreach (var axis in new[] { Vector3.right, Vector3.up, new Vector3(0f, 0f, 1f) }) {
            Vector3 numerical = (recipe.Position(point + axis * 0.001f) - recipe.Position(point - axis * 0.001f)) / 0.002f;
            Near(j.MultiplyVector(axis), numerical, "Analytic derivative", 0.0001f);
        }
        var obj = Object();
        Check(PartGeometrySnapshot.TryCapture(obj.Parts[0], false, out var snapshot, out _), "Cage snapshot");
        var cage = DeformationCage.Capture(obj, -1, Matrix4x4.identity, new[] { snapshot });
        Check(recipe.Positions(cage.Parts[0], snapshot.WorkPositions, out _), "Cage position preview");
        Check(recipe.Directions(cage.Parts[0], snapshot.WorkNormals, snapshot.WorkTangents, out _), "Cage directions");
        for (int i = 0; i < 4; i++) {
            Vector4 t = snapshot.WorkTangents[i];
            Check(Math.Abs(Vector3.Dot(snapshot.WorkNormals[i], new Vector3(t.x,t.y,t.z))) < 0.00001, "Tangent orthogonal");
            Check(t.w == -1, "Tangent handedness retained");
        }
        for (int axis = 0; axis < 3; axis++) {
            var scale = Vector3.one;
            if (axis == 0) scale.x = -1; else if (axis == 1) scale.y = -1; else scale.z = -1;
            var invalid = DeformationCage.ReferenceCorners(Matrix4x4.Scale(scale));
            Check(!DeformationRecipe.TryCreate(Matrix4x4.identity, invalid, out _, out _), "Reflected cage rejected");
        }
        var collapsed = DeformationCage.ReferenceCorners(Matrix4x4.Scale(new Vector3(1,0,1)));
        Check(!DeformationRecipe.TryCreate(Matrix4x4.identity, collapsed, out _, out _), "Collapsed cage rejected");
        corners = DeformationCage.ReferenceCorners(Matrix4x4.identity);
        corners[7] = new Vector3(-1,-1,-1);
        Check(!DeformationRecipe.TryCreate(Matrix4x4.identity, corners, out _, out _), "Folded cage rejected");
        var affine = Matrix4x4.identity; affine.m01 = 2f; affine.m22 = 0.2f;
        Check(DeformationRecipe.TryCreate(Matrix4x4.identity, DeformationCage.ReferenceCorners(affine), out recipe, out _), "Large affine shear allowed");
        Near(recipe.Position(point), affine.MultiplyPoint3x4(point), "Affine equivalence");
        var rng = new Random(21);
        for (int trial = 0; trial < 40; trial++) {
            corners = DeformationCage.ReferenceCorners(Matrix4x4.identity);
            for (int i = 0; i < 8; i++) corners[i] += new Vector3((float)rng.NextDouble()-.5f,(float)rng.NextDouble()-.5f,(float)rng.NextDouble()-.5f) * .2f;
            Check(DeformationRecipe.TryCreate(Matrix4x4.identity, corners, out recipe, out _), "Small corner changes accepted");
            for (int x = 0; x <= 4; x++) for (int y = 0; y <= 4; y++) for (int z = 0; z <= 4; z++) {
                Check(recipe.Jacobian(new Vector3(x/4f,y/4f,z/4f)).determinant > 0, "Accepted cage interior orientation");
            }
        }
    }

    private static void LargeStretchValidity() {
        foreach (float distance in new[] { 2f, 10f, 100f, 10000f }) {
            foreach (var direction in new[] { Vector3.right, Vector3.up, new Vector3(0,0,1), Vector3.one }) {
                var corners = DeformationCage.ReferenceCorners(Matrix4x4.identity);
                corners[7] += direction * distance;
                Check(DeformationRecipe.TryCreate(Matrix4x4.identity, corners, out var recipe, out _), "Long outward corner pull accepted");
                Near(recipe.Position(Vector3.one), corners[7], "Far corner reaches requested position", .01f);
                for (int x = 0; x <= 4; x++) for (int y = 0; y <= 4; y++) for (int z = 0; z <= 4; z++) {
                    Check(recipe.Jacobian(new Vector3(x/4f,y/4f,z/4f)).determinant > 0, "Long pull remains uninverted");
                }
            }
        }
        var crossing = DeformationCage.ReferenceCorners(Matrix4x4.identity);
        crossing[7].x = -.1f;
        Check(!DeformationRecipe.TryCreate(Matrix4x4.identity, crossing, out _, out _), "Corner crossing adjacent side rejected");
        crossing[7].x = 0f;
        Check(!DeformationRecipe.TryCreate(Matrix4x4.identity, crossing, out _, out _), "Collapsed corner rejected");
        crossing[7].x = .001f;
        Check(DeformationRecipe.TryCreate(Matrix4x4.identity, crossing, out _, out _), "Close but uncrossed corner accepted");
        var tiny = Matrix4x4.Scale(Vector3.one * .0002f);
        Check(DeformationRecipe.TryCreate(Matrix4x4.identity, DeformationCage.ReferenceCorners(tiny), out _, out _), "Small positive-volume cage remains valid");
        var shear = Matrix4x4.identity; shear.m01 = 10000f;
        Check(DeformationRecipe.TryCreate(Matrix4x4.identity, DeformationCage.ReferenceCorners(shear), out _, out _), "Long affine shear accepted");
        // All 27 coarse sample points are positive, but the cage folds between them.
        var interiorFold = new[] {
            new Vector3(1.1495709f,-1.3941627f,0.6006555f),
            new Vector3(1.8729300f,0.6207107f,1.2328627f),
            new Vector3(0.3559349f,0.7832957f,0.1727039f),
            new Vector3(2.0027417f,-0.3976448f,-0.4492847f),
            new Vector3(-0.0590838f,0.7315833f,1.8183787f),
            new Vector3(-0.3764349f,0.0624729f,1.6489833f),
            new Vector3(0.8248256f,1.5705664f,2.3485054f),
            new Vector3(1.6802062f,1.1349643f,1.4939712f),
        };
        Check(!DeformationRecipe.TryCreate(Matrix4x4.identity, interiorFold, out _, out _), "Fold between sample points rejected");
        var refined = new[] {
            new Vector3(0.0146205f,-0.3454468f,0.0159068f),
            new Vector3(0.3543741f,-0.2516883f,0.6640699f),
            new Vector3(-0.1864493f,1.1923313f,-0.2153483f),
            new Vector3(1.4279418f,-0.2762703f,-0.2692285f),
            new Vector3(0.2868823f,-0.0961543f,1.6688817f),
            new Vector3(1.2615473f,0.1124045f,1.3631832f),
            new Vector3(-0.1290650f,1.3380515f,1.6893976f),
            new Vector3(0.7197254f,1.1829912f,1.0163896f),
        };
        Check(DeformationRecipe.TryCreate(Matrix4x4.identity, refined, out _, out _), "Valid cage accepted after refining mixed bounds");
        var reference = Matrix4x4.TRS(new Vector3(3,-4,2), Quaternion.AngleAxis(32, Vector3.up), new Vector3(2,3,.5f));
        var rotated = DeformationCage.ReferenceCorners(reference);
        rotated[7] += reference.MultiplyVector(new Vector3(100,0,0));
        Check(DeformationRecipe.TryCreate(reference, rotated, out _, out _), "Long stretch in rotated reference accepted");
        var rng = new Random(613);
        int accepted = 0;
        for (int trial = 0; trial < 200; trial++) {
            var corners = DeformationCage.ReferenceCorners(Matrix4x4.identity);
            for (int i = 0; i < 8; i++) corners[i] += new Vector3((float)rng.NextDouble()-.5f, (float)rng.NextDouble()-.5f, (float)rng.NextDouble()-.5f) * 1.5f;
            if (!DeformationRecipe.TryCreate(Matrix4x4.identity, corners, out var recipe, out _)) continue;
            accepted++;
            for (int x = 0; x <= 8; x++) for (int y = 0; y <= 8; y++) for (int z = 0; z <= 8; z++) {
                Check(recipe.Jacobian(new Vector3(x/8f,y/8f,z/8f)).determinant > 0, "Accepted irregular cage interior orientation");
            }
        }
        Check(accepted > 10, "Irregular valid cages remain usable");
    }

    private static void PersistenceAndHistory() {
        var obj = Object();
        var system = new ProceduralObjectSystem(); system.Objects.Add(obj.Id, obj);
        var context = new HistoryContext(system);
        using (var history = new ActionHistory(context)) {
            Check(PartGeometrySnapshot.TryCapture(obj.Parts[0], false, out var snapshot, out _), "History snapshot");
            var rest = DeformationCage.Capture(obj, -1, Matrix4x4.identity, new[] { snapshot });
            var corners = (Vector3[])rest.Corners.Clone();
            corners[3] += new Vector3(0.1f,0,0.2f); corners[7] += new Vector3(0.1f,0,0.2f);
            var cage = rest.WithCorners(corners);
            Check(DeformationRecipe.TryCreate(cage.LocalFromReference, corners, out var recipe, out _), "History recipe");
            var geometry = new PartDeformationDeltaAction(obj, 0, snapshot, LocalizedText.Diagnostic("Deform"));
            var state = new DeformationAction(obj, -1, rest, true, LocalizedText.Diagnostic("Deform"));
            using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Deform"),
                new List<HistoryCaptureAction> { geometry, state }, null)) {
                Check(recipe.Positions(rest.Parts[0], snapshot.WorkPositions, out _), "First preview");
                var result = CageDeformationCommitWorker.RunCage(new[] {
                    new CageDeformationPartWorkItem(0, snapshot, rest.Parts[0]) }, recipe, null, cage);
                Check(result.Success, "Worker completed");
                var delta = result.Parts[0];
                geometry.SetPreparedRawDelta(delta.RawDelta, delta.BeforeHash, delta.AfterHash, delta.Changed);
                state.SetPreparedPayload(result.CagePayload);
                Check(snapshot.TryAdoptWorkStreams(obj.Parts[0], out _), "Adopt preview");
                obj.SetDeformation(-1, cage);
                Check(edit.Complete(), "Compound cage history recorded");
            }
            ulong final = GeometryStreams.Capture(obj.Parts[0], false).Hash();
            for (int i = 0; i < 100; i++) {
                history.Request(HistoryDirection.Undo);
                Check(!obj.HasDeformation && obj.Parts[0].Mesh.Vertices[3].Equals(snapshot.Positions[3]), "Exact create undo");
                history.Request(HistoryDirection.Redo);
                Check(obj.HasDeformation && GeometryStreams.Capture(obj.Parts[0], false).Hash() == final, "Exact create redo");
            }
            Check(!obj.CanEditGeometry(0, out _) && !obj.CanEditDeformationScope(0, out _), "Overlap and mesh edit guards");
            var dto = PersistenceObjectData.Capture(obj);
            byte[] bytes = ObjectBlockWriter.Write(dto);
            foreach (SaveCompression mode in Enum.GetValues(typeof(SaveCompression))) {
                byte[] profiled = ObjectBlockWriter.Write(dto, new SnapshotEncodingOptions(mode, 4));
                var decoded = ObjectBlockReader.Read(profiled, SnapshotFormat.kObjectSchemaVersion);
                Check(System.Linq.Enumerable.SequenceEqual(ObjectBlockWriter.Write(decoded), bytes),
                    "Compression profile preserves editable cage baseline " + mode);
            }
            var restored = ObjectBlockReader.Read(bytes, SnapshotFormat.kObjectSchemaVersion).Restore(9);
            Check(restored.HasDeformation && restored.GetDeformation(-1).Id == cage.Id, "Persistent baseline identity");
            Check(restored.GetDeformation(-1).Matches(restored), "Restored target identities rebound");
            Check(restored.GetDeformation(-1).Parts[0].Positions[3].Equals(snapshot.Positions[3]), "Original baseline persisted");
            bool rejected = false;
            try { ObjectBlockWriter.WriteForSchema(dto, SnapshotFormat.kPivotObjectSchemaVersion); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Old writer cannot silently bake cage");
            var clone = Object(10);
            obj.CopyDeformationsTo(clone);
            Check(clone.GetDeformation(-1).Id != cage.Id && !ReferenceEquals(clone.GetDeformation(-1).Parts[0].Positions, cage.Parts[0].Positions), "Clone baseline independent");
            var bake = new DeformationAction(obj, -1, obj.GetDeformation(-1), true, LocalizedText.Diagnostic("Bake Deformation"));
            using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Bake Deformation"),
                new List<HistoryCaptureAction> { bake }, null)) {
                bake.SetPreparedPayload(DeformationCodec.Encode(obj.GetDeformation(-1)));
                obj.SetDeformation(-1, null);
                Check(edit.Complete(), "Bake records metadata-only action");
            }
            Check(obj.CanEditGeometry(0, out _) && GeometryStreams.Capture(obj.Parts[0], false).Hash() == final, "Bake retains visible geometry");
            history.Request(HistoryDirection.Undo);
            Check(obj.HasDeformation && obj.GetDeformation(-1).Id == cage.Id, "Bake undo restores editable cage");
        }
    }
    private static void Finish(CageDeformationDragState session, ProceduralObject obj) {
        Check(session.TryBeginCompletion(obj, out bool pending, out LocalizedText reason), "Begin completion: " + reason);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        bool completed = !pending;
        while (!completed && timer.ElapsedMilliseconds < 10000) {
            Check(session.TryFinishCompletion(obj, out completed, out reason), "Finish completion: " + reason);
            if (!completed) System.Threading.Thread.Sleep(1);
        }
        Check(completed, "Completion timeout");
        session.Reset();
    }

    private static void Replay(ActionHistory history, HistoryDirection direction) {
        Check(history.Request(direction), "Cage replay requested");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (history.IsBusy && timer.ElapsedMilliseconds < 10000) {
            history.Poll();
            System.Threading.Thread.Sleep(1);
        }
        Check(!history.IsBusy && string.IsNullOrEmpty(history.LastFailure), "Cage replay finished");
    }

    private static void SessionLifecycle() {
        var obj = Object();
        var system = new ProceduralObjectSystem(); system.Objects.Add(obj.Id, obj);
        string directory = Path.Combine(Path.GetTempPath(), "po2-cage-" + Guid.NewGuid().ToString("N"));
        try {
            using (var history = new ActionHistory(new HistoryContext(system))) {
                system.ActionHistory = history;
                var session = new CageDeformationDragState();
                ulong original = GeometryStreams.Capture(obj.Parts[0], false).Hash();
                Check(session.TryBegin(obj, -1, Matrix4x4.identity, system, LocalizedText.Diagnostic("Deform"), false, false, out _), "Session capture");
                Check(session.TryMove(obj, 0xCC, new Vector3(.2f,0,.1f), out _), "Session preview");
                session.Reset();
                Check(!obj.HasDeformation && GeometryStreams.Capture(obj.Parts[0], false).Hash() == original && history.Count == 0, "Cancel first preview exactly");
                Check(session.TryBegin(obj, -1, Matrix4x4.identity, system, LocalizedText.Diagnostic("Deform"), false, false, out _), "Session recapture");
                Check(session.TryMove(obj, 0xCC, new Vector3(.2f,0,.1f), out _), "Session first edit");
                Finish(session, obj);
                var saved = obj.GetDeformation(-1);
                Check(saved != null && history.Count == 1, "First cage retained");
                ulong first = GeometryStreams.Capture(obj.Parts[0], false).Hash();
                Check(session.TryBegin(obj, -1, Matrix4x4.identity, system, LocalizedText.Diagnostic("Deform"), false, false, out _), "Second session");
                Check(session.TryMove(obj, 0xAA, new Vector3(0,.15f,0), out _), "Second preview");
                var invalid = (Vector3[])session.Cage.Corners.Clone(); invalid[7] = new Vector3(-20,-20,-20);
                ulong validPreview = GeometryStreams.Capture(obj.Parts[0], false).Hash();
                Check(!session.TryApply(obj, invalid, out _), "Fold rejected during drag");
                Check(GeometryStreams.Capture(obj.Parts[0], false).Hash() == validPreview, "Invalid candidate preserves last preview");
                Finish(session, obj);
                var second = obj.GetDeformation(-1);
                Check(second.Id == saved.Id && ReferenceEquals(saved.Parts[0].Positions, second.Parts[0].Positions), "Repeated edit shares original immutable baseline");
                Check(DeformationRecipe.TryCreate(second.LocalFromReference, second.Corners, out var recipe, out _), "Repeated recipe");
                for (int i = 0; i < obj.Parts[0].Mesh.Vertices.Length; i++)
                    Near(obj.Parts[0].Mesh.Vertices[i], recipe.Position(saved.Parts[0].Positions[i]), "Repeated edit evaluates from original baseline");
                ulong final = GeometryStreams.Capture(obj.Parts[0], false).Hash();
                Replay(history, HistoryDirection.Undo);
                Check(GeometryStreams.Capture(obj.Parts[0], false).Hash() == first && obj.GetDeformation(-1).SameCorners(saved.Corners), "Resident ordinary undo");
                Replay(history, HistoryDirection.Redo);
                Check(GeometryStreams.Capture(obj.Parts[0], false).Hash() == final, "Resident ordinary redo");
                Check(session.TryBegin(obj, -1, Matrix4x4.identity, system, LocalizedText.Diagnostic("Deform"), false, false, out _), "No-op capture");
                Finish(session, obj);
                Check(history.Count == 2, "No-op creates no history entry");
                Check(session.TryBegin(obj, -1, Matrix4x4.identity, system, LocalizedText.Diagnostic("Reset Deformation"), true, false, out _), "Reset capture");
                Check(session.TryApply(obj, DeformationCage.ReferenceCorners(saved.LocalFromReference), out _), "Reset reference");
                Finish(session, obj);
                Check(!obj.HasDeformation && GeometryStreams.Capture(obj.Parts[0], false).Hash() == original, "Reset restores exact rest positions and directions");
                Replay(history, HistoryDirection.Undo);
                Check(obj.HasDeformation && GeometryStreams.Capture(obj.Parts[0], false).Hash() == final, "Resident reset undo restores shape and cage");
                Check(session.TryBegin(obj, -1, Matrix4x4.identity, system, LocalizedText.Diagnostic("Bake Deformation"), true, true, out _), "Bake capture");
                Finish(session, obj);
                Check(!obj.HasDeformation && GeometryStreams.Capture(obj.Parts[0], false).Hash() == final, "Bake preserves all visible streams");
                Replay(history, HistoryDirection.Undo);
                Check(obj.GetDeformation(-1).SameCorners(second.Corners), "Resident bake undo restores original corners");
                Check(session.TryBegin(obj, -1, Matrix4x4.identity, system, LocalizedText.Diagnostic("Deform"), false, false, out _), "Abandon capture");
                Check(session.TryMove(obj, 0xCC, new Vector3(.01f,0,0), out _), "Abandon preview");
                Check(session.TryBeginCompletion(obj, out _, out _), "Abandon worker");
                session.Reset();
                Check(obj.GetDeformation(-1).SameCorners(second.Corners) && GeometryStreams.Capture(obj.Parts[0], false).Hash() == final, "Abandoned worker cannot publish");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void Reject(Action action, string reason) {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, reason);
    }

    private static void MeshBakeScopes() {
        var obj = new ProceduralObject(40, Guid.NewGuid(), Object(41).Parts[0], Object(42).Parts[0]) {
            Transform = ObjectTransform.IdentityAt(Vector3.zero)
        };
        var system = new ProceduralObjectSystem();
        system.Objects.Add(obj.Id, obj);
        using var history = new ActionHistory(new HistoryContext(system));
        system.ActionHistory = history;
        for (int i = 0; i < obj.Parts.Count; i++) {
            Check(PartGeometrySnapshot.TryCapture(obj.Parts[i], false, out var snapshot, out _), "Mesh bake part capture");
            obj.SetDeformation(i, DeformationCage.Capture(obj, i, Matrix4x4.identity, new[] { snapshot }));
        }
        Check(obj.GetGeometryDeformation(-1) != null && !obj.CanEditGeometry(-1, out _), "All Parts blocks while any cage remains");
        var session = new CageDeformationDragState();
        try {
            for (int i = 0; i < obj.Parts.Count; i++) {
                var cage = obj.GetGeometryDeformation(i);
                Check(cage != null && cage.PartIndex == i, "Mesh bake finds cage from mesh scope");
                ulong before = GeometryStreams.Capture(obj.Parts[i], false).Hash();
                Check(session.TryBegin(obj, cage.PartIndex, cage.LocalFromReference, system, LocalizedText.Diagnostic("Bake Deformation"), true, true, out _), "Mesh bake starts existing worker");
                Finish(session, obj);
                Check(obj.CanEditGeometry(i, out _) && obj.GetGeometryDeformation(i) == null, "Baked part unlocks");
                Check(GeometryStreams.Capture(obj.Parts[i], false).Hash() == before, "Mesh bake leaves visible streams unchanged");
                if (i == 0) Check(!obj.CanEditGeometry(-1, out _) && !obj.CanEditGeometry(1, out _), "Other part stays blocked until baked");
            }
        }
        finally { session.Reset(); }
        Check(obj.CanEditGeometry(-1, out _), "All Parts unlocks after final bake");
        Replay(history, HistoryDirection.Undo);
        Check(!obj.CanEditGeometry(-1, out _) && !obj.CanEditGeometry(1, out _) && obj.CanEditGeometry(0, out _), "Undo bake restores only its part lock");
        Replay(history, HistoryDirection.Redo);
        Check(obj.CanEditGeometry(-1, out _), "Redo bake unlocks mesh again");
    }

    private static void BoundsAndDisjointParts() {
        var obj = Object();
        obj.Transform = new ObjectTransform(new Vector3(100000,0,-100000),
            Quaternion.AngleAxis(37f, Vector3.up), new Vector3(3,.5f,2));
        var frame = new TransformFrame(Vector3.right, Vector3.up, new Vector3(0,0,1));
        Check(CageBounds.TryFit(obj, -1, frame, out var reference), "Flat geometry with rotated/scaled root fits");
        foreach (var point in obj.Parts[0].Mesh.Vertices) {
            Vector3 q = reference.inverse.MultiplyPoint3x4(point);
            Check(q.x >= -.001f && q.x <= 1.001f && q.y >= -.001f && q.y <= 1.001f && q.z >= -.001f && q.z <= 1.001f, "Rest point in fitted cage");
        }
        var farReference = reference;
        obj.Transform.Position = Vector3.zero;
        Check(CageBounds.TryFit(obj, -1, frame, out reference) && SelectionTransformMath.SameMatrix(farReference, reference), "City position does not affect fitted reference");
        var dto = PersistenceObjectData.Capture(obj);
        for (ushort schema = 2; schema <= 5; schema++) {
            var old = ObjectBlockReader.Read(ObjectBlockWriter.WriteForSchema(dto, schema), schema).Restore(11);
            Check(!old.HasDeformation, "Old schema remains readable without a cage");
        }
        var partA = Object(2).Parts[0]; var partB = Object(3).Parts[0];
        obj = new ProceduralObject(10, Guid.NewGuid(), partA, partB) { Transform = ObjectTransform.IdentityAt(Vector3.zero) };
        Check(PartGeometrySnapshot.TryCapture(partA, false, out var a, out _), "Part A capture");
        Check(PartGeometrySnapshot.TryCapture(partB, false, out var b, out _), "Part B capture");
        obj.SetDeformation(0, DeformationCage.Capture(obj, 0, Matrix4x4.identity, new[] { a }));
        Check(obj.CanEditGeometry(1, out _) && obj.CanEditDeformationScope(1, out _), "Unaffected part remains editable");
        Check(!obj.CanEditDeformationScope(-1, out _), "Overlapping object cage blocked");
        obj.SetDeformation(1, DeformationCage.Capture(obj, 1, Matrix4x4.identity, new[] { b }));
        dto = PersistenceObjectData.Capture(obj);
        var restored = ObjectBlockReader.Read(ObjectBlockWriter.Write(dto), SnapshotFormat.kObjectSchemaVersion).Restore(12);
        Check(restored.Deformations.Count == 2 && restored.GetDeformation(0).Matches(restored) && restored.GetDeformation(1).Matches(restored), "Independent part cages persist");
        using (var payload = new LifetimePayload(obj)) {
            payload.Cool();
            var cold = payload.GetObject();
            Check(cold.GetDeformation(0).Id == obj.GetDeformation(0).Id && cold.GetDeformation(0).Matches(cold), "Lifetime reconstruction retains cage identity and target identities");
        }
        var good = dto.Deformations;
        dto.Deformations = new[] { good[0], good[0] };
        Reject(() => ObjectBlockWriter.Write(dto), "Repeated cage rejected");
        dto.Deformations = good;
        dto.Parts[0].Vertices[0] += new Vector3(1,0,0);
        Reject(() => ObjectBlockWriter.Write(dto), "Mismatched visible geometry rejected");
        byte[] encoded = DeformationCodec.Encode(good[0]);
        Array.Resize(ref encoded, encoded.Length + 1);
        Reject(() => DeformationCodec.Decode(encoded, obj), "Trailing baseline payload rejected");
        var invalid = (Vector3[])good[0].Corners.Clone(); invalid[0].x = float.NaN;
        Reject(() => DeformationCodec.Decode(DeformationCodec.Encode(good[0].WithCorners(invalid)), obj), "Nonfinite serialized cage rejected");
    }

}
