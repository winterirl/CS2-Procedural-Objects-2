using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;

internal static partial class LifetimeTests {
    private static LifetimeStorage Storage(LifetimeAction action) => (LifetimeStorage)typeof(LifetimeAction)
        .GetField("m_Payload", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(action);

    private static void BackgroundStorage() {
        BackgroundRoundTrip();
        EarlyUndoAndFailure();
        BackgroundClearAndTrim(false);
        BackgroundClearAndTrim(true);
        QueuedCancellation();
        LargeDeleteStorage();
        CancelEarlyUndo();
    }

    private static void CancelEarlyUndo() {
        var system = new ProceduralObjectSystem(); var context = new HistoryContext(system);
        var obj = Object(560); system.Objects.Add(obj.Id, obj);
        byte[] expected = LifetimeModelCodec.Encode(obj);
        using (var history = new ActionHistory(context))
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim()) {
            var action = DeleteForTest(obj, context, history);
            var storage = Storage(action);
            action.CoolManagedPayload();
            storage.PollBackgroundWork(null, payload => {
                entered.Set();
                if (!release.Wait(10000)) throw new TimeoutException();
                payload.Cool();
            });
            try {
                Check(entered.Wait(10000), "Encoder paused before undo");
                Check(history.Request(HistoryDirection.Undo) && history.CancelReplay(), "Cancel undo waiting for encoding");
                Check(!system.Objects.ContainsKey(obj.Id) && history.Cursor == 1, "Early cancellation leaves object deleted");
            }
            finally { release.Set(); }
            FinishReplay(history);
            Check(history.Count == 1 && history.Cursor == 1, "Cancelled decode retains history entry");
            Check(history.Request(HistoryDirection.Undo), "Retry cancelled decode"); FinishReplay(history);
            Equal(expected, LifetimeModelCodec.Encode(system.Objects[obj.Id]), "Decode retry preserves object data");
        }
    }

    private static void FinishReplay(ActionHistory history) {
        var timeout = Stopwatch.StartNew();
        while (history.IsReplayPending && timeout.ElapsedMilliseconds < 10000) {
            history.Poll();
            Thread.Sleep(1);
        }
        Check(!history.IsReplayPending && history.LastFailure.Length == 0, "Background lifetime replay completed");
    }

    private static LifetimeAction DeleteForTest(ProceduralObject obj, HistoryContext context, ActionHistory history) {
        var action = new LifetimeAction(obj, true, LocalizedText.Diagnostic("Delete"));
        using (var edit = new HistoryEdit(context, history.TryBegin(), action.Description,
            new List<HistoryCaptureAction> { action }, null, true)) {
            Check(context.TryRemove(Key(obj), out _, out _), "Detach before background encoding");
            Check(edit.Complete(), "Commit deletion before background encoding");
        }
        return action;
    }

    private static void BackgroundRoundTrip() {
        var system = new ProceduralObjectSystem(); var context = new HistoryContext(system);
        var obj = Object(510); system.Objects.Add(obj.Id, obj); system.Renderers.Add(obj.Id);
        byte[] expected = LifetimeModelCodec.Encode(obj);
        using (var history = new ActionHistory(context))
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim()) {
            var action = DeleteForTest(obj, context, history);
            var storage = Storage(action);
            Check(!action.IsCold && storage.Drain.IsCompleted, "Deletion retains its model without starting encoding inline");
            action.CoolManagedPayload();
            int ownerThread = Thread.CurrentThread.ManagedThreadId, workerThread = ownerThread;
            storage.PollBackgroundWork(null, payload => {
                workerThread = Thread.CurrentThread.ManagedThreadId;
                entered.Set();
                if (!release.Wait(10000)) throw new TimeoutException();
                payload.Cool();
            });
            try {
                Check(entered.Wait(10000) && workerThread != ownerThread, "Encoding runs on a worker");
                Check(history.Request(HistoryDirection.Undo), "Undo accepted during background encoding");
                Check(history.IsReplayPending && !system.Objects.ContainsKey(obj.Id), "Undo waits without publishing a graph still being read");
                Check(history.TryBegin() == null, "History rejects edits during pending replay");
            }
            finally { release.Set(); }
            FinishReplay(history);
            Check(history.Cursor == 0 && system.Objects.ContainsKey(obj.Id), "Undo restores after worker completion");
            var restored = system.Objects[obj.Id];
            Check(HistoryIdentityManifest.Capture(obj).Matches(HistoryIdentityManifest.Capture(restored)), "Background encoding preserves part identities");
            Equal(expected, LifetimeModelCodec.Encode(restored), "Background object streams and appearance");
            Check(history.Request(HistoryDirection.Redo), "Redo detaches without inline encoding");
            FinishReplay(history);
            Check(!system.Objects.ContainsKey(obj.Id) && !action.IsCold, "Redo keeps detached data ready for background storage");
            action.CoolManagedPayload();
            history.Poll();
            Check(Storage(action).Drain.Wait(10000), "Focused wrapper starts background storage");
            history.Poll();
            Check(action.IsCold, "Focused wrapper adopts completed storage");
            Check(history.Request(HistoryDirection.Undo), "Undo encoded deletion");
            FinishReplay(history);
            Equal(expected, LifetimeModelCodec.Encode(system.Objects[obj.Id]), "Repeated deletion undo remains exact");
        }
    }

    private static void EarlyUndoAndFailure() {
        var obj = Object(511);
        using (var storage = new LifetimeStorage(new LifetimePayload(obj))) {
            storage.RequestCooling();
            Check(storage.Prepare(CancellationToken.None).IsCompleted, "Undo before encoding starts needs no worker");
            storage.PollBackgroundWork(null, _ => throw new Exception("Must not start after preparation"));
            Check(ReferenceEquals(storage.GetObject(), obj), "Early undo returns the original detached model");
        }
        using (var storage = new LifetimeStorage(new LifetimePayload(obj))) {
            storage.RequestCooling();
            storage.PollBackgroundWork(null, _ => throw new IOException("Injected encoder failure"));
            Check(storage.Prepare(CancellationToken.None).GetAwaiter().GetResult().Success, "Encoder failure does not lose undo");
            string failure = null;
            storage.PollBackgroundWork(value => failure = value);
            Check(!string.IsNullOrEmpty(failure), "Encoder failure is reported");
            Check(ReferenceEquals(storage.GetObject(), obj), "Failed encoding preserves the complete original model");
        }
    }

    private static void BackgroundClearAndTrim(bool trim) {
        var system = new ProceduralObjectSystem(); var context = new HistoryContext(system);
        var obj = Object(trim ? 513 : 512); system.Objects.Add(obj.Id, obj);
        using (var history = new ActionHistory(context))
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim()) {
            var first = DeleteForTest(obj, context, history);
            var storage = Storage(first);
            storage.RequestCooling();
            storage.PollBackgroundWork(null, payload => {
                entered.Set();
                if (!release.Wait(10000)) throw new TimeoutException();
                payload.Cool();
            });
            try {
                Check(entered.Wait(10000), "Worker active before clear or trim");
                if (trim) {
                    var second = Object(514); system.Objects.Add(second.Id, second);
                    DeleteForTest(second, context, history);
                    Check(history.SetCapacity(1) && history.Count == 1, "Trimming does not wait for encoding");
                }
                else {
                    Check(history.Request(HistoryDirection.Undo), "Pending undo before clear");
                    history.Clear();
                    Check(history.Count == 0 && !history.IsReplayPending, "Clear detaches pending replay without waiting");
                }
                Check(!system.Objects.ContainsKey(obj.Id), "Clear or trim cannot resurrect the object");
            }
            finally { release.Set(); }
            // Clear's replay continuation disposes the action after preparation exits.
            var deadline = Stopwatch.StartNew();
            while (storage.ManagedBytes != 0 && deadline.ElapsedMilliseconds < 10000) Thread.Sleep(1);
            Check(storage.ManagedBytes == 0, "Retired lifetime storage releases the graph");
            Check(storage.Drain.Wait(10000), "Retired encoder drains safely");
            history.Poll();
            Check(!system.Objects.ContainsKey(obj.Id), "Finished worker never replays after clear or trim");
        }
    }

    private static void QueuedCancellation() {
        using (var first = new LifetimeStorage(new LifetimePayload(Object(515))))
        using (var queued = new LifetimeStorage(new LifetimePayload(Object(516))))
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim()) {
            first.RequestCooling(); queued.RequestCooling();
            first.PollBackgroundWork(null, payload => {
                entered.Set();
                if (!release.Wait(10000)) throw new TimeoutException();
                payload.Cool();
            });
            int calls = 0;
            try {
                Check(entered.Wait(10000), "First encoder occupies the worker slot");
                queued.PollBackgroundWork(null, payload => { Interlocked.Increment(ref calls); payload.Cool(); });
                queued.Dispose();
                Check(queued.Drain.Wait(10000) && calls == 0, "Queued encoding cancels without consuming another worker");
            }
            finally { release.Set(); }
            Check(first.Drain.Wait(10000), "Active encoder finishes after queued cancellation");
        }
    }

    private static void LargeDeleteStorage() {
        const int vertices = 150000;
        var part = TopologyTests.Part(vertices);
        var triangles = new int[vertices];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = i;
        part.Mesh.Triangles = triangles;
        part.ApplyHistorySurfaceAreas(new[] { part.SurfaceAreas[0] }, new int[vertices / 3], 1);
        var obj = new ProceduralObject(517, Guid.NewGuid(), part);
        var system = new ProceduralObjectSystem(); var context = new HistoryContext(system);
        system.Objects.Add(obj.Id, obj);
        using (var history = new ActionHistory(context)) {
            var action = DeleteForTest(obj, context, history);
            Check(!action.IsCold, "Large deletion does not serialize during its commit");
            history.Poll();
            Check(Storage(action).Drain.Wait(30000), "Large background encoding completes");
            history.Poll();
            Check(action.IsCold, "Large deletion automatically cools after commit");

        }
    }
}
