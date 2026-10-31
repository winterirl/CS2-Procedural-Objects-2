using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PO2.Persistence;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

internal static partial class Program
{
    private static readonly DateTime s_Time = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly GameSaveCatalogSnapshot s_Complete = new GameSaveCatalogSnapshot(Array.Empty<GameSaveCatalogEntry>(), true);
    private static int s_Id;

    private static void Main()
    {
        Action[] tests = { RetainsNewestPerSession, ProtectsUncertainFiles, CatalogPresenceAndConflicts, HeaderNeverReadsGeometry,
            DiscoveryRejectsBadEnvelopes, DeletionRevalidatesFiles, ManualPlanDoesNotExpand, ManualPlanRechecksProtection,
            PagingPreservesFiltersAndTotals, ForegroundSuspendsWithoutJoining, WorkerCancellationAndFaults,
            TemporaryFilesRespectCutoffAndProtection, LargeFilesNeedOnlyHeaders, CoordinatorTests.CatalogCaptureAvoidsLazyLoads, CoordinatorTests.CatalogChangeRejectsStaleWork,
            CoordinatorTests.SettingsAndRecoverySuspendCleanup, CoordinatorTests.StartupRetainsThreeAndRefreshesPages, CoordinatorTests.RecoveryQueueSurvivesManagerRefresh };
        foreach (Action test in tests)
        {
            test();
            Console.WriteLine("PASS " + test.Method.Name);
        }
        Console.WriteLine(tests.Length + " maintenance tests passed.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static Guid NextId() => new Guid(++s_Id, 0, 0, new byte[8]);
    private static HashSet<string> Keys(params string[] keys) => new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
    private static SnapshotMetadata Metadata(SnapshotSaveKind kind, int age = 0) => new SnapshotMetadata("City", "Save", "Map", s_Time.AddMinutes(-age), kind, null);
    private static SaveRecord Record(Guid session, int age, SnapshotSaveKind kind = SnapshotSaveKind.AutoSave,
        SnapshotMetadataStatus metadataStatus = SnapshotMetadataStatus.Available, GameSaveCatalogSnapshot catalog = null)
    {
        Guid id = NextId();
        var header = new SnapshotHeaderInfo(id, session, 0, 80, 0, 2, Metadata(kind, age), metadataStatus, "");
        return new SaveRecord(SaveIdentity.ToFileKey(id), "", header, 100, s_Time.AddMinutes(-age), catalog ?? s_Complete);
    }
    private static SaveDiscoveryResult Discovery(params SaveRecord[] records) => new SaveDiscoveryResult(records, Array.Empty<SaveDiscoveryError>());
    private static List<SaveRecord> Select(SaveDiscoveryResult discovery, int keep, ISet<string> keys = null, Guid session = default, bool complete = true)
        => AutosaveRetentionPolicy.Select(discovery, complete, keep, keys ?? Keys(), session);

    private static void RetainsNewestPerSession()
    {
        Guid a = NextId(), b = NextId();
        var records = Enumerable.Range(0, 7).Select(i => Record(a, i)).Concat(Enumerable.Range(0, 5).Select(i => Record(b, i))).ToArray();
        var targets = Select(Discovery(records), 3);
        Check(targets.Count == 6 && targets.All(s => s.DisplayTimeUtc <= s_Time.AddMinutes(-3)), "Keep three per session, not three globally.");
        Check(Select(Discovery(records), 0).Count == records.Length, "Zero retention should remove eligible orphans.");
        Check(Select(Discovery(records), 20).Count == 0, "Retention larger than a group should keep it.");
        targets = Select(Discovery(records.Reverse().ToArray()), 3, Keys(records[6].FileKey.ToUpperInvariant()));
        Check(targets.Count == 5 && !targets.Contains(records[6]), "An older protected save is kept in addition to the retained three.");
        var ties = Enumerable.Range(0, 6).Select(i => Record(a, 0)).ToArray();
        var forward = Select(Discovery(ties), 3).Select(s => s.FileKey);
        var reverse = Select(Discovery(ties.Reverse().ToArray()), 3).Select(s => s.FileKey);
        Check(forward.SequenceEqual(reverse), "Timestamp ties must produce stable retention.");
    }

    private static void ProtectsUncertainFiles()
    {
        Guid session = NextId();
        var safe = Record(session, 4);
        var unknown = Record(session, 4, catalog: GameSaveCatalogSnapshot.s_Unavailable);
        var records = new[] { safe, unknown, Record(Guid.Empty, 4), Record(session, 4, SnapshotSaveKind.Manual),
            Record(session, 4, SnapshotSaveKind.Unknown), Record(session, 4, metadataStatus: SnapshotMetadataStatus.Missing),
            Record(session, 4, metadataStatus: SnapshotMetadataStatus.Invalid), Record(session, 4, metadataStatus: SnapshotMetadataStatus.Unsupported) };
        Check(Select(Discovery(records), 0).SequenceEqual(new[] { safe }), "Only validated embedded autosave metadata may qualify.");
        Check(Select(Discovery(records), 0, complete: false).Count == 0, "Incomplete catalogs must never authorize automatic deletion.");
        Check(Select(new SaveDiscoveryResult(records, Array.Empty<SaveDiscoveryError>(), false), 0).Count == 0, "Failed scans must never authorize automatic deletion.");
        Check(Select(Discovery(records), 0, session: session).Count == 0, "An active recovery session must be preserved.");
    }

    private static void CatalogPresenceAndConflicts()
    {
        Guid id = NextId(), session = NextId();
        string key = SaveIdentity.ToFileKey(id);
        var entry = new GameSaveCatalogEntry(key, id, Guid.Empty, null, presenceOnly: true);
        var catalog = new GameSaveCatalogSnapshot(new[] { entry }, true);
        Check(catalog.Resolve(key, id, session, out _) == GameSaveStatus.Matched, "Presence alone protects a linked save without lazy metadata loading.");
        Check(catalog.Resolve(key, NextId(), session, out _) == GameSaveStatus.Unknown, "A filename collision is not a verified match.");
        catalog = new GameSaveCatalogSnapshot(new[] { entry, new GameSaveCatalogEntry(key, NextId(), session, null) }, true);
        Check(catalog.Resolve(key, id, session, out _) == GameSaveStatus.Unknown, "Conflicting catalog entries must remain unknown.");
        catalog = new GameSaveCatalogSnapshot(new[] { new GameSaveCatalogEntry(key, id, Guid.Empty, null) }, true);
        Check(catalog.Resolve(key, id, session, out _) == GameSaveStatus.Unknown, "Legacy full entries with missing session identity must remain unknown.");
    }

    private static byte[] Envelope(Guid id, Guid session, SnapshotSaveKind kind = SnapshotSaveKind.AutoSave, int age = 0, bool legacy = false, bool withObject = false)
    {
        byte[] metadata = legacy ? Array.Empty<byte>() : SnapshotMetadataCodec.Write(Metadata(kind, age));
        byte[] directory = Array.Empty<byte>();
        if (withObject)
        {
            using var entry = new MemoryStream();
            SnapshotBinary.WriteGuid(entry, NextId());
            SnapshotBinary.WriteUInt16(entry, SnapshotFormat.kObjectSchemaVersion);
            SnapshotBinary.WriteUInt16(entry, 0);
            SnapshotBinary.WriteUInt64(entry, (ulong)(80 + metadata.Length + SnapshotFormat.kDirectoryEntrySize));
            SnapshotBinary.WriteUInt64(entry, 1024);
            SnapshotBinary.WriteUInt64(entry, 1024);
            SnapshotBinary.WriteUInt32(entry, 0);
            SnapshotBinary.WriteUInt32(entry, 0);
            directory = entry.ToArray();
        }
        using var stream = new MemoryStream();
        stream.Write(SnapshotFormat.s_Magic);
        SnapshotBinary.WriteUInt16(stream, legacy ? SnapshotFormat.kLegacyContainerVersion : SnapshotFormat.kContainerVersion);
        SnapshotBinary.WriteUInt16(stream, SnapshotFormat.kSnapshotSchemaVersion);
        SnapshotBinary.WriteUInt16(stream, SnapshotFormat.kSnapshotHeaderSize);
        SnapshotBinary.WriteUInt16(stream, 0);
        SnapshotBinary.WriteGuid(stream, id);
        SnapshotBinary.WriteGuid(stream, session);
        SnapshotBinary.WriteUInt32(stream, withObject ? 1u : 0u);
        SnapshotBinary.WriteUInt16(stream, SnapshotFormat.kDirectoryEntrySize);
        SnapshotBinary.WriteUInt16(stream, 0);
        SnapshotBinary.WriteUInt64(stream, (ulong)(80 + metadata.Length));
        SnapshotBinary.WriteUInt64(stream, (ulong)directory.Length);
        SnapshotBinary.WriteUInt32(stream, Crc32.Compute(directory));
        SnapshotBinary.WriteUInt32(stream, Crc32.Compute(stream.ToArray()));
        stream.Write(metadata);
        stream.Write(directory);
        return stream.ToArray();
    }

    private sealed class HeaderOnlyStream : MemoryStream
    {
        internal HeaderOnlyStream(byte[] bytes) : base(bytes) { }
        public override long Length => 64L * 1024 * 1024 * 1024;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position + count > base.Length) throw new InvalidOperationException("Geometry was read during discovery.");
            return base.Read(buffer, offset, count);
        }
    }

    private static void HeaderNeverReadsGeometry()
    {
        Guid id = NextId(), session = NextId();
        foreach (ushort version in new ushort[] { 2, 3 })
        {
            byte[] bytes = Envelope(id, session, withObject: true);
            bytes[8] = (byte)version;
            Array.Copy(BitConverter.GetBytes(Crc32.Compute(bytes, 0, 76)), 0, bytes, 76, 4);
            using var stream = new HeaderOnlyStream(bytes);
            Check(SnapshotReader.TryReadHeader(stream, out var header, out _) && header.SaveGuid == id && header.SessionGuid == session && header.ObjectCount == 1 && header.ContainerVersion == version && header.MetadataStatus == SnapshotMetadataStatus.Available,
                "A large snapshot should need only its envelope for discovery, including container 3.");
        }
    }

    private sealed class Folder : IDisposable
    {
        internal readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PO2-Maintenance-" + Guid.NewGuid().ToString("N"));
        internal readonly SnapshotFileStore Store;
        internal Folder() { Directory.CreateDirectory(Path); Store = new SnapshotFileStore(Path); }
        internal string Write(Guid session, int age = 0, SnapshotSaveKind kind = SnapshotSaveKind.AutoSave, bool legacy = false)
        {
            Guid id = NextId();
            string file = System.IO.Path.Combine(Path, SaveIdentity.ToFileKey(id) + ".po2");
            File.WriteAllBytes(file, Envelope(id, session, kind, age, legacy));
            return file;
        }
        public void Dispose() { Directory.Delete(Path, true); }
    }

    private static void DiscoveryRejectsBadEnvelopes()
    {
        using var folder = new Folder();
        Guid session = NextId();
        folder.Write(session);
        folder.Write(session, legacy: true);
        string badMetadata = folder.Write(session);
        byte[] bytes = File.ReadAllBytes(badMetadata);
        bytes[bytes.Length - 1] ^= 0xff;
        File.WriteAllBytes(badMetadata, bytes);
        string badHeader = folder.Write(session);
        bytes = File.ReadAllBytes(badHeader); bytes[20] ^= 0xff; File.WriteAllBytes(badHeader, bytes);
        string renamed = folder.Write(session);
        File.Move(renamed, System.IO.Path.Combine(folder.Path, SaveIdentity.ToFileKey(NextId()) + ".po2"));
        for (int i = 0; i < 50; i++) File.WriteAllText(System.IO.Path.Combine(folder.Path, "invalid-" + i + ".po2"), "bad");
        var result = folder.Store.DiscoverSaves(s_Complete);
        Check(result.ScanSucceeded && result.Saves.Count == 3 && result.RejectedFileCount == 52 && result.RejectedFiles.Count == 32,
            "Reject invalid files while bounding diagnostic detail.");
        Check(Select(result, 0).Count == 1, "Legacy and invalid metadata files must not become autosave candidates.");
    }

    private static void DeletionRevalidatesFiles()
    {
        using var folder = new Folder();
        Guid session = NextId();
        string path = folder.Write(session);
        var record = folder.Store.DiscoverSaves(s_Complete).Saves[0];
        File.SetLastWriteTimeUtc(path, record.LastWriteTimeUtc.AddMinutes(1));
        Check(!folder.Store.TryDeleteUnchanged(record, out _) && File.Exists(path), "Changed timestamps must reject deletion.");
        File.SetLastWriteTimeUtc(path, record.LastWriteTimeUtc);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[16] ^= 0x01;
        uint crc = Crc32.Compute(bytes, 0, 76);
        using (var stream = new MemoryStream(bytes)) { stream.Position = 76; SnapshotBinary.WriteUInt32(stream, crc); }
        File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, record.LastWriteTimeUtc);
        Check(!folder.Store.TryDeleteUnchanged(record, out _) && File.Exists(path), "Even a same-length, same-timestamp replacement must revalidate identity.");
        var outside = new SaveRecord(record.FileKey, System.IO.Path.Combine(folder.Path, "other", record.FileKey + ".po2"), record.Header, record.FileBytes, record.LastWriteTimeUtc, s_Complete);
        Check(!folder.Store.TryDeleteUnchanged(outside, out _), "Deletion must remain within the root and expected filename.");
        File.WriteAllBytes(path, Envelope(record.SnapshotSaveGuid, session));
        record = folder.Store.DiscoverSaves(s_Complete).Saves[0];
        Check(folder.Store.TryDeleteUnchanged(record, out _) && !File.Exists(path), "An unchanged candidate should be deleted.");
        Check(!folder.Store.TryDeleteUnchanged(record, out _), "A missing candidate is skipped.");
    }

    private static MaintenanceWork Work(CancellationToken token = default) => new MaintenanceWork(token, new MaintenanceProgress());
    private static SnapshotAccessGate OpenGate()
    {
        var gate = new SnapshotAccessGate();
        Check(gate.TryResume(gate.Revision), "Fresh gate should resume.");
        return gate;
    }

    private static void ManualPlanDoesNotExpand()
    {
        using var folder = new Folder();
        Guid session = NextId();
        string first = folder.Write(session, 1);
        var manager = new SaveManager(folder.Store);
        manager.Refresh(s_Complete);
        manager.Prepare("orphaned", "", "", Keys());
        string token = manager.Pending.Token;
        string later = folder.Write(session);
        var gate = OpenGate();
        var result = manager.Confirm(token, s_Complete, Keys(), Work(), gate, gate.Revision);
        Check(result.Deleted == 1 && !File.Exists(first) && File.Exists(later), "Confirmation may delete only the files in the reviewed plan.");
    }

    private static void ManualPlanRechecksProtection()
    {
        using var folder = new Folder();
        Guid session = NextId();
        string path = folder.Write(session);
        var manager = new SaveManager(folder.Store);
        manager.Refresh(s_Complete);
        manager.Prepare("orphaned", "", "", Keys());
        var plan = manager.Pending;
        var gate = OpenGate();
        var result = manager.Confirm("stale-token", s_Complete, Keys(), Work(), gate, gate.Revision);
        Check(result.FailureCount == 1 && manager.Pending == plan && File.Exists(path), "A stale token must not dismiss a newer plan.");
        result = manager.Confirm(plan.Token, s_Complete, Keys(plan.Saves[0].FileKey), Work(), gate, gate.Revision);
        Check(result.Deleted == 0 && result.FailureCount == 1 && File.Exists(path), "Protection is captured again at confirmation.");
        manager.Refresh(s_Complete); manager.Prepare("orphaned", "", "", Keys());
        plan = manager.Pending;
        var entry = new GameSaveCatalogEntry(plan.Saves[0].FileKey, plan.Saves[0].SnapshotSaveGuid, session, null);
        result = manager.Confirm(plan.Token, new GameSaveCatalogSnapshot(new[] { entry }, true), Keys(), Work(), gate, gate.Revision);
        Check(result.Deleted == 0 && File.Exists(path), "A newly linked save no longer meets an orphan deletion plan.");
    }

    private static void PagingPreservesFiltersAndTotals()
    {
        Guid a = NextId(), b = NextId();
        var records = Enumerable.Range(0, 123).Select(i => Record(i % 2 == 0 ? a : b, i)).ToArray();
        var discovery = Discovery(records);
        var page = SaveManagerPage.Create(discovery, "", "all", false, 1, null);
        Check(page.Saves.SequenceEqual(records.Skip(50).Take(50)) && page.MatchCount == 123 && page.TotalBytes == 12300 && page.PageBytes == 5000,
            "Pages must return only their range with correct totals.");
        page = SaveManagerPage.Create(discovery, a.ToString("D"), "Orphaned", true, 99, null);
        Check(page.Index == 1 && page.Saves.Length == 12 && page.MatchCount == 62 && page.Saves.Last() == records[0], "Filtering, reverse order and page clamping must agree.");
        page = SaveManagerPage.Create(discovery, "", "Matched", false, 3, null);
        Check(page.Index == 0 && page.Saves.Length == 0 && page.Lineages.Sum(g => g.Count) == 123, "An empty page must preserve overall lineage counts.");
    }

    private static void ForegroundSuspendsWithoutJoining()
    {
        var gate = OpenGate();
        long revision = gate.Revision;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task worker = Task.Run(() =>
        {
            using var lease = gate.TryEnter(revision);
            Check(lease != null, "Worker must enter before foreground suspension.");
            entered.Set();
            release.Wait();
        });
        Check(entered.Wait(TimeSpan.FromSeconds(10)), "Worker failed to start.");
        try
        {
            gate.Suspend();
            Check(gate.IsActive && gate.TryEnter(revision) == null && !gate.TryResume(gate.Revision), "Suspension must reject new work while leaving an active lease to finish.");
        }
        finally { release.Set(); }
        worker.GetAwaiter().GetResult();
        Check(!gate.IsActive && !gate.TryResume(revision) && gate.TryResume(gate.Revision), "Only the current revision may resume after disk work exits.");
    }

    private static void WorkerCancellationAndFaults()
    {
        using var service = new SnapshotMaintenanceService();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        service.Start(work => { entered.Set(); release.Wait(); work.Checkpoint(); return 1; });
        Check(entered.Wait(TimeSpan.FromSeconds(10)), "Worker failed to start.");
        Check(!service.TryTake(out _, out _), "Polling unfinished work must not join it.");
        bool refused = false;
        try { service.Start(work => 2); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "Only one maintenance worker may run.");
        service.Cancel(); release.Set();
        object result = null; Exception error = null;
        Check(SpinWait.SpinUntil(() => service.TryTake(out result, out error), TimeSpan.FromSeconds(10)), "Cancelled worker failed to finish.");
        Check(result == null && error == null && !service.HasWork, "Cancellation should discard the result without surfacing a failure.");
        service.Start(work => throw new IOException("fixture"));
        Check(SpinWait.SpinUntil(() => service.TryTake(out result, out error), TimeSpan.FromSeconds(10)) && error is IOException, "Worker faults must be reported through polling.");
    }

    private static void TemporaryFilesRespectCutoffAndProtection()
    {
        using var folder = new Folder();
        string old = System.IO.Path.Combine(folder.Path, SaveIdentity.ToFileKey(NextId()) + ".po2.tmp");
        string fresh = System.IO.Path.Combine(folder.Path, SaveIdentity.ToFileKey(NextId()) + ".po2.tmp");
        string key = SaveIdentity.ToFileKey(NextId());
        string protectedPath = System.IO.Path.Combine(folder.Path, key + ".po2.tmp");
        string foreign = System.IO.Path.Combine(folder.Path, "other.po2.tmp");
        foreach (string path in new[] { old, fresh, protectedPath, foreign }) { File.WriteAllText(path, "temp"); File.SetLastWriteTimeUtc(path, s_Time.AddDays(-1)); }
        File.SetLastWriteTimeUtc(fresh, s_Time);
        var gate = OpenGate();
        folder.Store.CleanupStaleTemps(s_Time, Keys(key), Work(), gate, gate.Revision);
        Check(!File.Exists(old) && File.Exists(fresh) && File.Exists(protectedPath) && File.Exists(foreign), "Only pre-start, unprotected PO2 temps may be removed.");
    }

    private static void LargeFilesNeedOnlyHeaders()
    {
        using var folder = new Folder();
        Guid session = NextId();
        for (int i = 0; i < 8; i++)
        {
            string path = folder.Write(session, i);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
            stream.SetLength(1024L * 1024 * 1024);
        }
        var discovery = folder.Store.DiscoverSaves(s_Complete);
        Check(discovery.Saves.Count == 8 && discovery.Saves.Sum(s => s.FileBytes) == 8L * 1024 * 1024 * 1024, "Large sparse files should be indexed by their headers.");
        var targets = Select(discovery, 3);
        foreach (var target in targets) Check(folder.Store.TryDeleteUnchanged(target, out _), "Unchanged large file should delete without reading its body.");
        Check(folder.Store.DiscoverSaves(s_Complete).Saves.Count == 3, "Retention should leave three large files.");
    }
}
