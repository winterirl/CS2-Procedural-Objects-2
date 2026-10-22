using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PO2;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;

internal static partial class Program {
    private static int s_Checks;
    private static void Check(bool condition, string message) {
        s_Checks++;
        if (!condition) throw new Exception(message);
    }

    private sealed class ValueAction : IHistoryAction {
        public LocalizedText Description => LocalizedText.Diagnostic("Test value");
        public bool HasChanges { get; set; } = true;
        internal long ReportedBytes = 16;
        public HistoryFootprint Footprint => new HistoryFootprint(ReportedBytes, ReportedBytes, 0, ReportedBytes);
        internal int Replays;
        internal int Disposals;
        internal bool Fail;
        internal bool Fatal;
        internal Action DuringReplay;
        internal TaskCompletionSource<HistoryResult> Pending;
        internal CancellationToken PreparationToken;
        public Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken token) {
            PreparationToken = token;
            return Pending == null ? Task.FromResult(HistoryResult.Ok) : Pending.Task;
        }
        public HistoryResult Replay(HistoryDirection direction, HistoryContext context) {
            DuringReplay?.Invoke();
            if (Fail) return new HistoryResult(false, "Injected failure", Fatal);
            Replays += direction == HistoryDirection.Undo ? -1 : 1;
            return HistoryResult.Ok;
        }
        public void Dispose() { Interlocked.Increment(ref Disposals); }
    }

    private static void Record(ActionHistory history, IHistoryAction action) {
        using (var tx = history.TryBegin()) {
            Check(tx != null && tx.TryCommit(action), "Record failed");
        }
    }

    private sealed class PartAction : IHistoryAction {
        private readonly HistoryPartKey m_Key;
        internal int Replays;
        internal PartAction(HistoryPartKey key) { m_Key = key; }
        public LocalizedText Description => LocalizedText.Diagnostic("Resolve reconstructed part");
        public bool HasChanges => true;
        public HistoryFootprint Footprint => default;
        public Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken token) => Task.FromResult(HistoryResult.Ok);
        public HistoryResult Replay(HistoryDirection direction, HistoryContext context) {
            if (!context.TryResolve(m_Key, out _, out _)) return new HistoryResult(false, "Missing reconstructed part", true);
            Replays++;
            return HistoryResult.Ok;
        }
        public void Dispose() { }
    }

    private static void Timeline() {
        using (var history = new ActionHistory(null)) {
            var first = new ValueAction();
            Record(history, first);
            for (int i = 0; i < 20; i++) Record(history, new ValueAction());
            Check(history.Count == 20 && history.Cursor == 20 && first.Disposals == 1, "Capacity eviction");
            for (int i = 0; i < 10; i++) history.Request(HistoryDirection.Undo);
            Check(history.Cursor == 10 && history.CanRedo, "Undo cursor");
            Record(history, new ValueAction());
            Check(history.Count == 11 && history.Cursor == 11 && !history.CanRedo, "Branch truncation");
            history.SetCapacity(-5);
            Check(history.Capacity == 1 && history.Count == 1 && history.Cursor == 1, "Clamp minimum");
            history.Request(HistoryDirection.Undo);
            Check(history.Cursor == 0 && !history.CanUndo, "Undo end");
            history.SetCapacity(99);
            Check(history.Capacity == 40 && history.Count == 1, "Clamp maximum");
            history.Request(HistoryDirection.Redo);
            Check(history.Cursor == 1, "Redo cursor");
        }
        using (var history = new ActionHistory(null)) {
            for (int i = 0; i < 8; i++) Record(history, new ValueAction());
            for (int i = 0; i < 6; i++) history.Request(HistoryDirection.Undo);
            history.SetCapacity(3);
            Check(history.Count == 3 && history.Cursor == 0 && history.CanRedo, "Mixed trim preserves next redo");
        }
    }

    private static void DepthPolicy() {
        using (var history = new ActionHistory(null)) {
            Check(history.Capacity == 20, "Default depth is 20");
            history.SetCapacity(40);
            var oldest = new ValueAction { ReportedBytes = 3L * 1024 * 1024 * 1024 };
            Record(history, oldest);
            for (int i = 1; i < 40; i++) Record(history, new ValueAction());
            Check(history.Count == 40 && oldest.Disposals == 0, "Large entries remain resident at configured depth");
            Record(history, new ValueAction());
            Check(history.Count == 40 && history.Cursor == 40 && oldest.Disposals == 1, "Forty-first action evicts only the oldest");
            for (int i = 0; i < 40; i++) Check(history.Request(HistoryDirection.Undo), "Full-depth undo");
            Check(!history.CanUndo && history.CanRedo, "Forty steps can all be undone");
            for (int i = 0; i < 40; i++) Check(history.Request(HistoryDirection.Redo), "Full-depth redo");
        }
        using (var history = new ActionHistory(null)) {
            var first = new ValueAction { ReportedBytes = 3L * 1024 * 1024 * 1024 };
            var second = new ValueAction { ReportedBytes = first.ReportedBytes };
            Record(history, first); Record(history, second);
            Check(history.Footprint.EncodedBytes > 1024L * 1024 * 1024, "Aggregate byte estimates do not reject edits");
            history.Request(HistoryDirection.Undo);
            Record(history, new ValueAction());
            Check(second.Disposals == 1 && first.Disposals == 0 && history.Count == 2 && !history.CanRedo,
                "Large redo entries are released when branching");
            using (var transaction = history.TryBegin()) {
                history.Poll(1);
                Check(history.Count == 2 && first.Disposals == 0 && history.Capacity == 20,
                    "Slider reduction waits for active capture");
                Check(transaction.TryCommit(new ValueAction()), "Capture completes while resize is pending");
            }
            history.Poll(1);
            Check(history.Capacity == 1 && history.Count == 1 && history.Cursor == 1 && first.Disposals == 1,
                "Next lifecycle poll applies deferred depth");
        }
        using (var history = new ActionHistory(null)) {
            var first = new ValueAction();
            var pending = new ValueAction { Pending = new TaskCompletionSource<HistoryResult>() };
            Record(history, first); Record(history, pending);
            history.Request(HistoryDirection.Undo);
            history.Poll(1);
            Check(history.IsBusy && history.Count == 2 && first.Disposals == 0 && pending.Disposals == 0,
                "Slider reduction waits for pending replay");
            pending.Pending.SetResult(HistoryResult.Ok);
            Check(SpinWait.SpinUntil(() => { history.Poll(1); return !history.IsBusy; }, 2000), "Pending replay settles");
            Check(history.Count == 1 && history.Cursor == 0 && pending.Disposals == 0 && first.Disposals == 1,
                "Deferred trim preserves next redo after replay");
            Check(history.Request(HistoryDirection.Redo) && history.Cursor == 1 && pending.Replays == 0,
                "Retained redo remains continuous");
        }
    }

    private static void FailuresAndPreparation() {
        using (var history = new ActionHistory(null)) {
            var action = new ValueAction { Fail = true };
            Record(history, action);
            history.Request(HistoryDirection.Undo);
            Check(history.Count == 1 && history.Cursor == 1 && history.LastFailure.Length > 0, "Failure preserves cursor");
            action.Fail = false;
            action.DuringReplay = () => {
                Check(history.IsReplaying && !history.CanUndo && history.TryBegin() == null, "Replay reentry blocked");
            };
            history.Request(HistoryDirection.Undo);
            Check(history.Cursor == 0, "Successful retry");
            action.Fail = true;
            action.Fatal = true;
            history.Request(HistoryDirection.Redo);
            Check(history.Count == 0 && action.Disposals == 1, "Untrusted timeline cleared");
            using (var tx = history.TryBegin()) {
                var noOp = new ValueAction { HasChanges = false };
                Check(!tx.TryCommit(noOp) && history.Count == 0, "No-op rejected");
                noOp.Dispose();
            }
            var pending = new ValueAction { Pending = new TaskCompletionSource<HistoryResult>() };
            Record(history, pending);
            history.Request(HistoryDirection.Undo);
            Check(history.IsBusy && history.Cursor == 1, "Preparation waits without mutation");
            history.Clear();
            Record(history, new ValueAction());
            pending.Pending.SetResult(HistoryResult.Ok);
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref pending.Disposals) == 1, 2000), "Deferred payload disposal");
            history.Poll();
            Check(pending.Replays == 0 && history.Cursor == 1, "Stale preparation cannot replay into new timeline");
        }
    }

    private static void Compound() {
        using (var history = new ActionHistory(null)) {
            var a = new ValueAction { Fail = true };
            var b = new ValueAction();
            Record(history, new CompoundHistoryAction(LocalizedText.Diagnostic("Test compound"), new IHistoryAction[] { a, b }));
            history.Request(HistoryDirection.Undo);
            Check(history.Cursor == 1 && b.Replays == 0, "Compound rollback restores completed child");
            a.Fail = false;
            history.Request(HistoryDirection.Undo);
            history.Request(HistoryDirection.Redo);
            Check(history.Cursor == 1 && a.Replays == 0 && b.Replays == 0, "Compound cycle");
            history.Clear();
            Check(a.Disposals == 1 && b.Disposals == 1, "Compound disposal");
        }
    }

    private static void Identities() {
        var system = new ProceduralObjectSystem();
        var context = new HistoryContext(system);
        var obj = new ProceduralObject(1, Guid.NewGuid(), new MeshPart(), new MeshPart());
        var manifest = HistoryIdentityManifest.Capture(obj);
        var partKey = new HistoryPartKey(manifest.ObjectKey, obj.Parts[1].PartHistoryId, 0);
        var otherHint = new HistoryPartKey(manifest.ObjectKey, obj.Parts[1].PartHistoryId, 500);
        Check(partKey.Equals(otherHint) && partKey.GetHashCode() == otherHint.GetHashCode(), "Index is not identity");
        system.Objects.Add(1, obj);
        using var history = new ActionHistory(context);
        var partAction = new PartAction(partKey);
        Record(history, partAction);
        Check(context.TryResolve(partKey, out var before, out int index) && index == 1, "Stale hint resolution");
        using (var stream = new MemoryStream()) {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) manifest.Write(writer);
            stream.Position = 0;
            HistoryIdentityManifest decoded;
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true)) decoded = HistoryIdentityManifest.Read(reader);
            Check(manifest.Matches(decoded), "Identity codec round trip");
            var replacement = new ProceduralObject(1, obj.PersistentId, new MeshPart(), new MeshPart());
            Check(decoded.TryRestoreStaging(replacement, out _), "Staging identity restoration");
            system.Objects[1] = replacement;
            Check(context.TryResolve(partKey, out var after, out index) && after != before, "New managed instance resolves through old key");
            history.Request(HistoryDirection.Undo);
            history.Request(HistoryDirection.Redo);
            Check(partAction.Replays == 2 && history.Cursor == 1, "Recorded action survives managed graph replacement");
            Check(!context.TryResolve(new HistoryObjectKey(1, Guid.NewGuid()), out _), "Runtime ID collision rejected");
            Check(replacement.TryHistoryRemovePart(partKey.PartId, 1, out var detached, out _), "Exact removal");
            Check(replacement.TryHistoryInsertPart(partKey.PartId, 1, detached, out _), "Exact reinsertion");
            Check(!replacement.TryHistoryInsertPart(partKey.PartId, 0, detached, out _), "Duplicate insertion rejected");
            replacement.Parts[0].RestoreHistoryIdentity(partKey.PartId);
            Check(!context.TryResolve(partKey, out _, out _), "Repeated part identities rejected");
        }
    }

    private static void Main(string[] args) {
        Timeline();
        DepthPolicy();
        FailuresAndPreparation();
        ReplayCancellation();
        HistoryInput();
        Compound();
        Identities();
        int valueChecks = ValueTests.Run();
        int geometryChecks = GeometryTests.Run();
        int lifetimeChecks = LifetimeTests.Run();
        int topologyChecks = TopologyTests.Run();
        int storageChecks = StorageTests.Run();
        int gizmoChecks = GizmoTests.Run();
        int cageChecks = CageTests.Run();
        Console.WriteLine($"PASS: {cageChecks} deformation cage checks.");
        int pivotChecks = PivotTests.Run();
        int selectionChecks = SelectionTransformTests.Run();
        Console.WriteLine($"PASS: {s_Checks} foundation checks, {valueChecks} value/appearance/Surface Area checks, {geometryChecks} geometry checks, {lifetimeChecks} lifetime checks, {topologyChecks} topology checks, {storageChecks} storage checks, {gizmoChecks} gizmo checks, {pivotChecks} pivot checks, {selectionChecks} selection transform checks.");
    }
}
