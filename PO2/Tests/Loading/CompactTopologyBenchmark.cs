using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PO2.Core;
using PO2.Geometry.Topology;
using PO2.Persistence;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Persistence.PreparedTopology;
using UnityEngine;

internal static partial class LoadingTests
{
    private static PersistencePartData CompactShuffledFixture(PersistencePartData source)
    {
        var random = new Random(91671);
        var permutation = Enumerable.Range(0, source.Vertices.Length).OrderBy(_ => random.Next()).ToArray();
        var triangles = Enumerable.Range(0, source.Triangles.Length / 3).OrderBy(_ => random.Next())
            .SelectMany(t => new[] { permutation[source.Triangles[t * 3]], permutation[source.Triangles[t * 3 + 1]], permutation[source.Triangles[t * 3 + 2]] }).ToArray();
        var faces = source.StructuralFaces.Select(f => new StructuralFace(f.Id, permutation[f.GetRenderVertexIndex(0)], permutation[f.GetRenderVertexIndex(1)], permutation[f.GetRenderVertexIndex(2)], permutation[f.GetRenderVertexIndex(3)])).ToArray();
        return PreparedFixture(source.Vertices.Length, triangles, faces);
    }

    private static void CompactTopologyBenchmark()
    {
        string chosen = Environment.GetEnvironmentVariable("PO2_COMPACT_CASE") ?? "quad";
        PersistencePartData source;
        int parts = 1;
        switch (chosen)
        {
            case "quad": source = PreparedGrid(320, false); break;
            case "split": source = PreparedGrid(240, true); break;
            case "million": source = PreparedGrid(720, false); break;
            case "shuffled": source = CompactShuffledFixture(PreparedGrid(240, false)); break;
            case "batch": source = PreparedGrid(80, false); parts = 64; break;
            case "triangles":
                var grid = PreparedGrid(240, false);
                source = PreparedFixture(grid.Vertices.Length, grid.Triangles, Enumerable.Range(0, grid.Triangles.Length / 3)
                    .Select(t => new StructuralFace(t, grid.Triangles[t * 3], grid.Triangles[t * 3 + 1], grid.Triangles[t * 3 + 2])).ToArray());
                break;
            case "sparse": source = PreparedFixture(180000, Enumerable.Range(0, 180000).ToArray(), Enumerable.Range(0, 60000).Select(i => new StructuralFace(i, i * 3, i * 3 + 1, i * 3 + 2)).ToArray()); break;
            case "duplicates": source = PreparedFixture(3, Enumerable.Range(0, 90000).SelectMany(_ => new[] { 0, 1, 2 }).ToArray(), Enumerable.Range(0, 70000).Select(i => new StructuralFace(i, 0, 1, 2)).ToArray()); break;
            default: throw new ArgumentException("Unknown compact benchmark case.");
        }
        var fixture = StableFixture(1, SnapshotFormat.kObjectSchemaVersion);
        var data = PersistenceObjectData.FromDecoded(fixture.PersistentId, fixture.Name, false, false, fixture.Transform,
            fixture.CustomOrientations, Enumerable.Repeat(source, parts).ToArray(), Vector3.zero);
        var live = new[] { data.Restore(1) };
        var metadata = new SnapshotMetadata("bench", "save", "map", null, SnapshotSaveKind.Manual, null);
        var options = new SnapshotEncodingOptions(SaveCompression.Fast, LoadingBatch.WorkerCount);
        string root = Environment.GetEnvironmentVariable("PO2_COMPACT_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "po2-compact-benchmark-" + Guid.NewGuid().ToString("N"));
        string oldRoot = Environment.GetEnvironmentVariable("PO2_COMPACT_OLD_INPUT");
        var store = new SnapshotFileStore(root);
        var oldStore = oldRoot == null ? null : new SnapshotFileStore(oldRoot);
        var fresh = new List<double>(); var reuse = new List<double>(); var load = new List<double>();
        var afterLoad = new List<double>(); var upgrade = new List<double>();
        long bytes = 0;
        double Time(Action action) { var watch = Stopwatch.StartNew(); action(); return watch.Elapsed.TotalMilliseconds; }
        long Save(string key, IReadOnlyList<ProceduralObject> objects, TopologyEncodingSession session)
        {
            var capture = new CitySaveCapture(objects, session);
            long length = store.WriteTransactional(key, FixtureSave, FixtureSession, metadata, capture.Objects, options, capture).Bytes;
            session.Promote(capture);
            return length;
        }
        List<ProceduralObject> Load(SnapshotFileStore input, string key, TopologyEncodingSession session)
        {
            var read = input.ReadExact(key, FixtureSave, FixtureSession);
            if (!read.IsValid) throw new InvalidDataException(read.Error);
            var objects = SnapshotModelRestorer.Restore(read.Result.TakeObjectsForRestore(session));
            session.CommitLoaded(objects);
            return objects;
        }
        int trials = int.TryParse(Environment.GetEnvironmentVariable("PO2_COMPACT_TRIALS"), out int requested) ? requested : 5;
        for (int trial = -1; trial < trials; trial++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var session = new TopologyEncodingSession();
            double a = Time(() => bytes = Save(chosen, live, session));
            double b = Time(() => Save(chosen + "-reuse", live, session));
            var loadedSession = new TopologyEncodingSession();
            List<ProceduralObject> loaded = null;
            double c = Time(() => loaded = Load(store, chosen, loadedSession));
            double d = Time(() => Save(chosen + "-loaded", loaded, loadedSession));
            double e = 0;
            if (oldStore != null)
            {
                var oldSession = new TopologyEncodingSession();
                var oldLive = Load(oldStore, chosen, oldSession);
                e = Time(() => Save(chosen + "-upgrade", oldLive, oldSession));
            }
            if (trial >= 0) { fresh.Add(a); reuse.Add(b); load.Add(c); afterLoad.Add(d); upgrade.Add(e); }
        }
        double Median(List<double> samples) => samples.OrderBy(x => x).ElementAt(samples.Count / 2);
        Console.WriteLine("case,workers,bytes,fresh_save_ms,reuse_save_ms,load_ms,loaded_save_ms,projected_upgrade_ms");
        Console.WriteLine($"{chosen},{LoadingBatch.WorkerCount},{bytes},{Median(fresh):F3},{Median(reuse):F3},{Median(load):F3},{Median(afterLoad):F3},{Median(upgrade):F3}");
        if (Environment.GetEnvironmentVariable("PO2_COMPACT_OUTPUT") == null) Directory.Delete(root, true);
    }
}
