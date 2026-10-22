using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.Geometry.Transforms;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;

internal static class StorageTests {
    private static int s_Checks;
    private static void Check(bool value, string message) {
        s_Checks++;
        if (!value) throw new Exception(message);
    }
    private sealed class BufferAction : IHistoryAction, IHistoryPayloadOwner {
        internal readonly HistoryResidentPayload Buffer;
        internal TaskCompletionSource<HistoryResult> Gate;
        internal int Replays, Disposals;
        internal int CoolCalls;
        internal long ExtraReportedBytes;
        internal BufferAction() {
            var bytes = new byte[128 * 1024];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 17 + 3);
            Buffer = new HistoryResidentPayload(bytes, bytes.Length);
        }
        public LocalizedText Description => LocalizedText.Diagnostic("Storage test");
        public bool HasChanges => true;
        public HistoryFootprint Footprint => Buffer.Footprint.Add(new HistoryFootprint(64 + ExtraReportedBytes, ExtraReportedBytes, 0, 64));
        public IEnumerable<HistoryResidentPayload> Payloads { get { yield return Buffer; } }
        public void CoolManagedPayload() { CoolCalls++; }
        public Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken token) => Gate?.Task ?? Task.FromResult(HistoryResult.Ok);
        public HistoryResult Replay(HistoryDirection direction, HistoryContext context) {
            var bytes = Buffer.Bytes;
            for (int i = 0; i < bytes.Length; i++) if (bytes[i] != (byte)(i * 17 + 3)) throw new Exception("Changed payload reached replay");
            Replays++;
            return HistoryResult.Ok;
        }
        public void Dispose() { Buffer.Dispose(); Interlocked.Increment(ref Disposals); }
    }
    private static string Root() => Path.Combine(Path.GetTempPath(), "PO2-storage-test-" + Guid.NewGuid().ToString("N"));
    private static void ResidentPolicy() {
        using (var history = new ActionHistory(null)) {
            var action = new BufferAction { ExtraReportedBytes = 3L * 1024 * 1024 * 1024 };
            using (var transaction = history.TryBegin()) Check(transaction.TryCommit(action), "Oversized reported payload commits");
            history.Poll();
            Replay(history, HistoryDirection.Undo);
            Replay(history, HistoryDirection.Redo);
            Check(action.CoolCalls == 0 && !action.Buffer.IsCold && action.Buffer.Bytes != null,
                "Commit, poll and replay never cool or spill history automatically");
            Check(action.Replays == 2 && history.Cursor == 1, "Resident payload remains reversible above former ceilings");
        }
    }
    private static string[] Files(string root) => Directory.Exists(root) ? Directory.GetFiles(root, "*.payload", SearchOption.AllDirectories) : Array.Empty<string>();
    private static void Record(ActionHistory history, BufferAction action, HistoryFileStore store) {
        using (var transaction = history.TryBegin()) Check(transaction != null && transaction.TryCommit(action), "Storage record");
        action.Buffer.Spill(store);
    }
    private static void Wait(Func<bool> complete, Action poll) {
        var timer = Stopwatch.StartNew();
        while (!complete() && timer.ElapsedMilliseconds < 10000) { poll(); Thread.Sleep(1); }
        Check(complete(), "Preparation/cleanup timed out");
    }
    private static void Replay(ActionHistory history, HistoryDirection direction) {
        Check(history.Request(direction), "Storage replay request");
        Wait(() => !history.IsBusy, history.Poll);
    }
    private static void Timeline() {
        string root = Root();
        try {
            using (var store = new HistoryFileStore(root))
            using (var history = new ActionHistory(null)) {
                for (int i = 0; i < 20; i++) Record(history, new BufferAction(), store);
                Check(history.Count == 20 && Files(root).Length == 20, "Cold storage preserves configured depth");
                Check(history.Footprint.EncodedBytes == 0 && history.Footprint.ColdBytes == 20L * 128 * 1024, "Cold accounting");
                Check(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length == 0, "Atomic publication leaves no temporary files");
                for (int i = 0; i < 20; i++) Replay(history, HistoryDirection.Undo);
                Check(history.Cursor == 0 && history.CanRedo, "Cold undo to start");
                for (int i = 0; i < 20; i++) Replay(history, HistoryDirection.Redo);
                Check(history.Cursor == 20 && history.Footprint.EncodedBytes == 20L * 128 * 1024, "Cold replay remains resident");
                Replay(history, HistoryDirection.Undo);
                Replay(history, HistoryDirection.Undo);
                Record(history, new BufferAction(), store);
                Check(history.Count == 19 && !history.CanRedo && Files(root).Length == 19, "Branch truncation releases cold files");
                history.SetCapacity(3);
                Check(history.Count == 3 && Files(root).Length == 3, "Capacity reduction releases cold files");
                history.Clear();
                Check(Files(root).Length == 0, "Clear releases cold files");
            }
            Check(Directory.GetDirectories(root).Length == 0, "Dispose removes session directory");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void DamagedFile(int kind) {
        string root = Root();
        try {
            using (var store = new HistoryFileStore(root))
            using (var history = new ActionHistory(null)) {
                var action = new BufferAction(); Record(history, action, store);
                string path = Files(root)[0];
                if (kind == 0) File.Delete(path);
                else {
                    var bytes = File.ReadAllBytes(path);
                    bytes[kind == 1 ? bytes.Length - 1 : 4] ^= 1;
                    File.WriteAllBytes(path, bytes);
                }
                Replay(history, HistoryDirection.Undo);
                Check(action.Replays == 0 && history.Count == 0 && history.LastFailure.Length > 0, "Damaged file rejected before model replay");
                Check(action.Disposals == 1 && Files(root).Length == 0, "Damaged timeline disposed");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void WriteFailure() {
        string root = Root(); File.WriteAllText(root, "Directory creation must fail");
        try {
            using (var store = new HistoryFileStore(root))
            using (var history = new ActionHistory(null)) {
                var action = new BufferAction();
                using (var transaction = history.TryBegin()) Check(transaction.TryCommit(action), "Resident commit");
                bool failed = false;
                try { action.Buffer.Spill(store); } catch (IOException) { failed = true; }
                Check(failed, "Injected disk write fails");
                Check(!action.Buffer.IsCold && history.Count == 1, "Failed write retains resident history");
                Replay(history, HistoryDirection.Undo);
                Check(action.Replays == 1 && history.CanRedo, "Resident fallback remains reversible");
            }
        }
        finally { File.Delete(root); }
    }
    private static void CancelPending() {
        string root = Root();
        try {
            using var store = new HistoryFileStore(root);
            var history = new ActionHistory(null);
            var action = new BufferAction { Gate = new TaskCompletionSource<HistoryResult>(TaskCreationOptions.RunContinuationsAsynchronously) };
            Record(history, action, store);
            Check(history.Request(HistoryDirection.Undo) && history.IsReplayPending, "Pending preparation blocks interaction");
            Check(!history.CanUndo && history.TryBegin() == null, "Pending preparation blocks capture");
            history.Clear(); history.Dispose();
            Check(action.Disposals == 0 && Files(root).Length == 1, "Pending action retains buffers until preparation settles");
            store.Dispose();
            action.Gate.SetResult(HistoryResult.Ok);
            Wait(() => Volatile.Read(ref action.Disposals) == 1, () => { });
            Check(action.Replays == 0 && Directory.GetDirectories(root).Length == 0, "Cancelled preparation never replays and cleans session");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void ProductionPayloads() {
        string root = Root();
        try {
            var part = TopologyTests.Part(8);
            var obj = new ProceduralObject(101, Guid.NewGuid(), part);
            var key = new HistoryObjectKey(obj.Id, obj.PersistentId);
            var partId = part.PartHistoryId;
            var system = new ProceduralObjectSystem(); system.Objects.Add(obj.Id, obj);
            var context = new HistoryContext(system);
            byte[] before = TopologyState.Capture(part).Signature();
            using (var store = new HistoryFileStore(root))
            using (var history = new ActionHistory(context)) {
                Check(PartGeometrySnapshot.TryCapture(part, false, out var snapshot, out _), "Cold transform capture");
                var transform = new PartTransformAction(obj, 0, snapshot, LocalizedText.Diagnostic("Transform"));
                using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Transform"), new List<HistoryCaptureAction> { transform }, null)) {
                    var matrix = Matrix4x4.identity; matrix.m03 = 1.2345f; matrix.m00 = 1.73f;
                    Check(PartGeometryTransform.TryApplyFromSnapshot(part, snapshot, matrix, out _), "Cold transform preview");
                    // Force the swappable fallback so replacement buffers also spill.
                    transform.SetRecipe(Matrix4x4.identity);
                    Check(edit.Complete(), "Cold transform commit");
                }
                Spill(transform, store);
                Check(!transform.UsesCorrection && transform.Footprint.ColdBytes > 0, "Transform fallback spilled");
                var replacement = TopologyTests.Part(10); var mesh = replacement.Mesh;
                var mutation = new MeshMutationResult(mesh.Vertices, mesh.Normals, mesh.Tangents, mesh.UV0, mesh.VertexColors, mesh.TexCoord3,
                    mesh.Triangles, Array.Empty<StructuralFace>(), Array.Empty<int>());
                mutation.SetHistoryVertexMap(8, null);

                var topology = new TopologyAction(obj, 0, mutation, LocalizedText.Diagnostic("Topology"), context);
                using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Topology"), new List<HistoryCaptureAction> { topology }, null)) {
                    part.ApplyHistoryTopology(replacement); Check(edit.Complete(), "Cold topology commit");
                }
                byte[] after = TopologyState.Capture(part).Signature();
                Spill(topology, store);
                Check(topology.Footprint.ColdBytes > 0, "Topology programs spilled");
                var delete = new LifetimeAction(obj, true, LocalizedText.Diagnostic("Delete"));
                using (var edit = new HistoryEdit(context, history.TryBegin(), LocalizedText.Diagnostic("Delete"), new List<HistoryCaptureAction> { delete }, null)) {
                    Check(context.TryRemove(key, out _, out _), "Cold initial deletion"); Check(edit.Complete(), "Resident delete commit");
                }
                Spill(delete, store);
                Check(delete.Footprint.ColdBytes > 0, "Detached graph encoded and spilled");
                for (int cycle = 0; cycle < 5; cycle++) {
                    Replay(history, HistoryDirection.Undo);
                    Check(context.TryResolve(key, out var restored) && !ReferenceEquals(restored, obj) && restored.Parts[0].PartHistoryId == partId, "Disk reconstruction preserves identity");
                    Check(TopologyState.Equal(after, TopologyState.Capture(restored.Parts[0]).Signature()), "Disk object exact state");
                    Replay(history, HistoryDirection.Undo); Replay(history, HistoryDirection.Undo);
                    Check(TopologyState.Equal(before, TopologyState.Capture(restored.Parts[0]).Signature()), "Older cold topology and geometry resolve reconstructed object");
                    Replay(history, HistoryDirection.Redo); Replay(history, HistoryDirection.Redo);
                    Check(TopologyState.Equal(after, TopologyState.Capture(restored.Parts[0]).Signature()), "Cold geometry and topology redo exact state");
                    Replay(history, HistoryDirection.Redo);
                    Spill(delete, store);
                    Spill(topology, store);
                    Spill(transform, store);
                    Check(!system.Objects.ContainsKey(obj.Id) && history.Count == 3, "Cold deletion redo retains timeline");
                }
            }
            Check(Directory.GetDirectories(root).Length == 0, "Production buffers cleaned");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Spill(IHistoryAction action, HistoryFileStore store) {
        (action as IHistoryPayloadOwner)?.CoolManagedPayload();
        if (action is LifetimeAction lifetime) {
            lifetime.PollBackgroundWork(null);
            // This helper only spills storage; it does not prepare a renderer replay.
            var storage = (LifetimeStorage)typeof(LifetimeAction).GetField("m_Payload",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(lifetime);
            storage.Drain.GetAwaiter().GetResult();
            storage.PollBackgroundWork(null);
        }
        foreach (var payload in HistoryPayloads.Get(action)) payload.Spill(store);
    }
    internal static int Run() {
        ResidentPolicy();
        Timeline(); ProductionPayloads(); DamagedFile(0); DamagedFile(1); DamagedFile(2); WriteFailure(); CancelPending();
        return s_Checks;
    }
}
