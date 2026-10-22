using System;
using System.IO;
using System.Linq;
using System.Threading;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry.Transforms;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Persistence.Model;

internal static partial class Program {
    private static void MirrorHistoryStorageTests() {
        MirrorDeltaValidationTests();
        MirrorDeferredHistoryTest(0);
        MirrorDeferredHistoryTest(1);
        MirrorDeferredHistoryTest(2);
        MirrorCompressionFailureTest();
        MirrorUncompressedStorageTest();
        MirrorQueuedCompressionTest();
    }

    private static PersistencePartData MirrorHistoryAfter(PersistencePartData before) =>
        MirrorGeometry.TransformCapture(before, MirrorGeometry.CreateLocalReflection(
            ObjectTransform.IdentityAt(Vector3.zero), new Vector3(2, 3, 5), Vector3.right), false, CancellationToken.None);

    private static void MirrorDeltaValidationTests() {
        foreach (bool streams in new[] { false, true }) {
            var part = streams ? MirrorFixture() : Single(3, streams: false);
            part.Mesh.Vertices[0].x = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
            var before = PersistencePartData.Capture(part);
            var after = MirrorHistoryAfter(before);
            using (var delta = new MirrorDelta(before, after, CancellationToken.None)) {
                var reader = delta.Prepare(CancellationToken.None).GetAwaiter().GetResult();
                Check(!reader.Compressed && !delta.Payloads.Any(), "Raw mirror is usable without compression or payload hashing");
                var current = after;
                for (int i = 0; i < 8; i++) {
                    current = delta.Apply(current, HistoryDirection.Undo, reader, CancellationToken.None);
                    Check(TopologyState.Equal(TopologyState.Capture(before).Signature(), TopologyState.Capture(current).Signature()),
                        "XOR undo preserves all bits, including signed zero and optional streams");
                    current = delta.Apply(current, HistoryDirection.Redo, reader, CancellationToken.None);
                    Check(TopologyState.Equal(TopologyState.Capture(after).Signature(), TopologyState.Capture(current).Signature()), "XOR redo exact state");
                }
                bool rejected = false;
                reader.Bytes[0] ^= 1;
                try { delta.Apply(after, HistoryDirection.Undo, reader, CancellationToken.None); }
                catch (InvalidDataException) { rejected = true; }
                finally { reader.Bytes[0] ^= 1; }
                Check(rejected, "Corrupt raw delta cannot publish");
                rejected = false;
                int index = after.Triangles[0]; after.Triangles[0] = after.Triangles[1];
                try { delta.Apply(after, HistoryDirection.Undo, reader, CancellationToken.None); }
                catch (InvalidDataException) { rejected = true; }
                finally { after.Triangles[0] = index; }
                Check(rejected, "Changed triangle winding is rejected");
                if (streams) {
                    foreach (var values in new[] { after.UV0, after.TexCoord1, after.TexCoord3 }) {
                        var value = values[0]; values[0].w += 1;
                        rejected = false;
                        try { delta.Apply(after, HistoryDirection.Undo, reader, CancellationToken.None); }
                        catch (InvalidDataException) { rejected = true; }
                        finally { values[0] = value; }
                        Check(rejected, "Unchanged appearance streams remain validated");
                    }
                }
                using (var cancellation = new CancellationTokenSource()) {
                    cancellation.Cancel(); rejected = false;
                    try { delta.Apply(after, HistoryDirection.Undo, reader, cancellation.Token); }
                    catch (OperationCanceledException) { rejected = true; }
                    Check(rejected, "Mirror replay observes cancellation");
                }
            }
        }
    }

    // 0 adopts compression, 1 clears history, 2 evicts at the configured depth.
    private static void MirrorDeferredHistoryTest(int cleanup) {
        var source = Grid(40);
        var before = PersistencePartData.Capture(source);
        var after = MirrorHistoryAfter(before);
        var delta = new MirrorDelta(before, after, CancellationToken.None);
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim()) {
            string root = Path.Combine(Path.GetTempPath(), "PO2-Mirror-Test-" + Guid.NewGuid().ToString("N"));
            var obj = new ProceduralObject(9390, Guid.NewGuid(), source);
            obj.Transform = ObjectTransform.IdentityAt(Vector3.zero);
            var system = new ProceduralObjectSystem { MirrorTestSelected = obj };
            system.Objects.Add(obj.Id, obj); system.m_HistoryContext = new HistoryContext(system);
            using (var store = new HistoryFileStore(root))
            using (var history = new ActionHistory(system.m_HistoryContext)) {
                system.ActionHistory = history;
                var size = TopologySize.ForPart(source);
                var action = new MirrorHistoryAction(new[] { new MirrorHistoryAction.Part {
                    Key = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), source.PartHistoryId, 0),
                    Size = size, Delta = delta
                } }, system.m_HistoryContext, LocalizedText.Diagnostic("Mirror storage test"));
                source.ApplyHistoryTopology(after.RestoreOwned()); source.SetLocalPivot(after.LocalPivot.Value);
                using (var transaction = history.TryBegin()) Check(transaction.TryCommit(action), "Raw mirror history commits");
                delta.StartCompression(raw => {
                    entered.Set();
                    if (!release.Wait(10000)) throw new Exception("Compression test gate timed out.");
                    return GeometryStreams.CompressRawXor(raw);
                });
                try {
                    WaitDelete(() => entered.IsSet, "Compression starts after history is available");
                    Check(!history.IsBusy && delta.CompressionPending, "Compression does not block history interaction");
                    foreach (var direction in new[] { HistoryDirection.Undo, HistoryDirection.Redo }) {
                        int cursor = history.Cursor;
                        var unchanged = TopologyState.Capture(source);
                        Check(history.Request(direction) && history.CancelReplay(), "Mirror replay can be cancelled");
                        WaitDelete(() => { history.Poll(); return !history.IsBusy; }, "Cancelled mirror workers drain");
                        Check(history.Count == 1 && history.Cursor == cursor && history.LastFailure.Length == 0, "Mirror cancellation preserves history");
                        MirrorSame(unchanged, source, "Cancelled mirror leaves geometry unchanged");
                        Check(history.Request(direction), "Replay begins while compressor is blocked");
                        WaitDelete(() => { history.Poll(); return !history.IsBusy; }, "Replay does not wait for compression");
                        Check(history.Count == 1, "Raw mirror replay succeeds: " + history.LastFailure);
                        MirrorSame(TopologyState.Capture(direction == HistoryDirection.Undo ? before : after), source, "Raw replay exact result");
                    }
                    if (cleanup != 0) {
                        if (cleanup == 1) history.Clear();
                        else {
                            history.SetCapacity(1);
                            var rename = new ObjectValueAction(obj, ObjectValueKind.Name, LocalizedText.Diagnostic("Rename"));
                            obj.Name = "Renamed after mirror";
                            rename.Seal(system.m_HistoryContext);
                            using (var transaction = history.TryBegin()) Check(transaction.TryCommit(rename), "Next edit evicts compressing mirror");
                        }
                        release.Set();
                        WaitDelete(() => delta.CompressionDrain.IsCompleted && delta.Footprint.ManagedBytes == 0,
                            "Disposed compression work drains");
                        Check(history.Count == (cleanup == 1 ? 0 : 1) && !delta.Payloads.Any() && delta.Footprint.ManagedBytes == 0,
                            "Clear and depth eviction release raw and late compression results");
                        MirrorSame(TopologyState.Capture(after), source, "History disposal leaves live geometry alone");
                    }
                    else {
                        release.Set();
                        WaitDelete(() => { history.Poll(); return delta.Payloads.Any(); }, "Owner adopts compressed mirror storage");
                        Check(delta.StoredBytes < delta.RawBytes, "One compressed delta shrinks the retained record");
                        var payload = delta.Payloads.Single(); payload.Spill(store);
                        Check(payload.IsCold, "Mirror payload can use cold storage");
                        Check(history.Request(HistoryDirection.Undo), "Cold mirror undo begins");
                        WaitDelete(() => { history.Poll(); return !history.IsBusy; }, "Cold mirror undo completes");
                        Check(history.Count == 1, "Cold mirror undo succeeds: " + history.LastFailure);
                        MirrorSame(TopologyState.Capture(before), source, "Cold mirror undo exact state");
                        Check(history.Request(HistoryDirection.Redo), "Cold mirror redo begins");
                        WaitDelete(() => { history.Poll(); return !history.IsBusy; }, "Cold mirror redo completes");
                        MirrorSame(TopologyState.Capture(after), source, "Cold mirror redo exact state");
                        Check(!payload.IsCold && Directory.GetFiles(root, "*.payload", SearchOption.AllDirectories).Length == 1,
                            "Replayed mirror stays resident without automatic spilling");
                        history.Clear();
                        WaitDelete(() => Directory.GetFiles(root, "*.payload", SearchOption.AllDirectories).Length == 0, "Clearing mirror removes its cold payload");
                    }
                }
                finally { release.Set(); }
            }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void MirrorCompressionFailureTest() {
        var before = PersistencePartData.Capture(Grid(40)); var after = MirrorHistoryAfter(before);
        using (var delta = new MirrorDelta(before, after, CancellationToken.None)) {
            delta.StartCompression(_ => throw new InvalidOperationException("Injected compressor failure"));
            WaitDelete(() => { delta.PollCompression(_ => { }); return !delta.CompressionPending; }, "Failed compressor settles");
            Check(!delta.Payloads.Any(), "Compression failure retains raw history");
            var restored = delta.Apply(after, HistoryDirection.Undo, delta.Prepare(CancellationToken.None).GetAwaiter().GetResult(), CancellationToken.None);
            MirrorSame(TopologyState.Capture(before), restored.RestoreOwned(), "Undo survives compression failure");
        }
    }

    private static void MirrorQueuedCompressionTest() {
        var before = PersistencePartData.Capture(Grid(40)); var after = MirrorHistoryAfter(before);
        using (var release = new ManualResetEventSlim())
        using (var entered = new ManualResetEventSlim())
        using (var first = new MirrorDelta(before, after, CancellationToken.None))
        using (var second = new MirrorDelta(before, after, CancellationToken.None)) {
            bool secondRan = false;
            first.StartCompression(raw => {
                entered.Set();
                if (!release.Wait(10000)) throw new Exception("Compression queue test timed out.");
                return GeometryStreams.CompressRawXor(raw);
            });
            try {
                WaitDelete(() => entered.IsSet, "First compressor holds shared gate");
                second.StartCompression(raw => { secondRan = true; return GeometryStreams.CompressRawXor(raw); });
                second.Dispose();
                WaitDelete(() => second.CompressionDrain.IsCompleted, "Disposed queued compressor does not wait for the active one");
                Check(!secondRan && second.Footprint.ManagedBytes == 0, "Queued disposed history skips compression and releases buffers");
            }
            finally {
                release.Set(); first.Dispose();
                WaitDelete(() => first.CompressionDrain.IsCompleted, "Active disposed compressor drains safely");
            }
        }
    }

    private static void MirrorUncompressedStorageTest() {
        var before = PersistencePartData.Capture(Grid(40)); var after = MirrorHistoryAfter(before);
        string root = Path.Combine(Path.GetTempPath(), "PO2-Mirror-Raw-" + Guid.NewGuid().ToString("N"));
        using (var store = new HistoryFileStore(root))
        using (var delta = new MirrorDelta(before, after, CancellationToken.None)) {
            delta.StartCompression(raw => new byte[raw.Length + 1]);
            WaitDelete(() => { delta.PollCompression(null); return !delta.CompressionPending; }, "Non-beneficial compression settles");
            Check(delta.StoredBytes == delta.RawBytes, "Larger compression result keeps the original delta");
            delta.Payloads.Single().Spill(store);
            var reader = delta.Prepare(CancellationToken.None).GetAwaiter().GetResult();
            Check(!reader.Compressed, "Cold raw payload retains its format");
            var restored = delta.Apply(after, HistoryDirection.Undo, reader, CancellationToken.None);
            MirrorSame(TopologyState.Capture(before), restored.RestoreOwned(), "Uncompressed cold history restores exact state");
        }
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
