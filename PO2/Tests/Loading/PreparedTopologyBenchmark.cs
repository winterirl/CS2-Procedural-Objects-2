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
    private static PersistencePartData PreparedGrid(int side, bool split)
    {
        var faces = new List<StructuralFace>();
        var triangles = new List<int>();
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
        {
            int a = y * (side + 1) + x, b = a + 1, d = a + side + 1, c = d + 1;
            var face = new StructuralFace(faces.Count, a, b, c, d);
            faces.Add(face); AppendFaceTriangles(triangles, face);
        }
        int count = (side + 1) * (side + 1);
        int[] mapping = Enumerable.Range(0, count).ToArray();
        if (split)
        {
            count *= 2;
            mapping = mapping.Concat(mapping).ToArray();
            // Keep distinct editable IDs at coincident positions as well as welds.
            for (int i = 0; i < triangles.Count; i += 6) triangles[i] += count / 2;
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                faces[i] = new StructuralFace(i, f.GetRenderVertexIndex(0) + count / 2, f.GetRenderVertexIndex(1), f.GetRenderVertexIndex(2), f.GetRenderVertexIndex(3));
                triangles[i * 6 + 3] += count / 2;
            }
        }
        var source = PreparedFixture(count, triangles.ToArray(), faces.ToArray(), mapping, StructuralTopologySource.ConvertedInferred);
        for (int i = 0; i < count; i++) source.Vertices[i] = new Vector3(mapping[i] % (side + 1), 0, mapping[i] / (side + 1));
        return source;
    }

    private static void PreparedTopologyBenchmark()
    {
        var metadata = new SnapshotMetadata("bench", "save", "map", null, SnapshotSaveKind.Manual, null);
        var options = new SnapshotEncodingOptions(SaveCompression.Fast, LoadingBatch.WorkerCount);
        var cases = new List<(string Name, PersistencePartData Source, int Parts)>
        {
            ("shared", PreparedGrid(320, false), 1),
            ("split", PreparedGrid(240, true), 1),
            ("many-parts", PreparedGrid(80, false), 16),
            ("large-batch", PreparedGrid(80, false), 64),
            ("sparse", PreparedFixture(180000, Enumerable.Range(0, 180000).ToArray(), Enumerable.Range(0, 60000).Select(i => new StructuralFace(i, i * 3, i * 3 + 1, i * 3 + 2)).ToArray()), 1),
            ("duplicates", PreparedFixture(3, Enumerable.Range(0, 90000).SelectMany(_ => new[] { 0, 1, 2 }).ToArray(), Enumerable.Range(0, 70000).Select(i => new StructuralFace(i, 0, 1, 2)).ToArray()), 1)
        };
        string root = Path.Combine(Path.GetTempPath(), "po2-prepared-benchmark-" + Guid.NewGuid().ToString("N"));
        try
        {
            Console.WriteLine("Host only, Burst disabled. Includes capture, encoding, actual disk transaction, read, validation and model restore; excludes Unity renderer.");
            Console.WriteLine("case,parts,triangles,old_save_ms,first_new_save_ms,reused_save_ms,old_load_ms,new_load_ms,old_bytes,new_bytes");
            var store = new SnapshotFileStore(root);
            string selected = Environment.GetEnvironmentVariable("PO2_PREPARED_BENCHMARK_CASE");
            foreach (var item in cases.Where(c => selected == null || c.Name == selected))
            {
                var fixture = StableFixture(1, SnapshotFormat.kObjectSchemaVersion);
                var data = PersistenceObjectData.FromDecoded(fixture.PersistentId, fixture.Name, false, false, fixture.Transform,
                    fixture.CustomOrientations, Enumerable.Repeat(item.Source, item.Parts).ToArray(), Vector3.zero);
                var live = new[] { data.Restore(1) };
                var oldSave = new List<double>(); var newSave = new List<double>(); var reuseSave = new List<double>();
                var oldLoad = new List<double>(); var newLoad = new List<double>();
                long oldBytes = 0, newBytes = 0;
                for (int trial = -1; trial < 5; trial++)
                {
                    double Time(Action action) { var timer = Stopwatch.StartNew(); action(); return timer.Elapsed.TotalMilliseconds; }
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    double a = Time(() => { var captured = live.Select(PersistenceObjectData.Capture).ToArray(); oldBytes = store.WriteTransactional("old", FixtureSave, FixtureSession, metadata, captured, options).Bytes; });
                    var session = new TopologyEncodingSession();
                    double b = Time(() => { var captured = new CitySaveCapture(live, session); newBytes = store.WriteTransactional("new", FixtureSave, FixtureSession, metadata, captured.Objects, options, captured).Bytes; session.Promote(captured); });
                    double c = Time(() => { var captured = new CitySaveCapture(live, session); store.WriteTransactional("reuse", FixtureSave, FixtureSession, metadata, captured.Objects, options, captured); session.Promote(captured); });
                    void Read(string key) { var result = store.ReadExact(key, FixtureSave, FixtureSession); if (!result.IsValid) throw new Exception(result.Error); var objects = SnapshotModelRestorer.Restore(result.Result.TakeObjectsForRestore()); GC.KeepAlive(objects); }
                    double d, e;
                    if ((trial & 1) == 0) { d = Time(() => Read("old")); e = Time(() => Read("new")); }
                    else { e = Time(() => Read("new")); d = Time(() => Read("old")); }
                    if (trial >= 0) { oldSave.Add(a); newSave.Add(b); reuseSave.Add(c); oldLoad.Add(d); newLoad.Add(e); }
                }
                double Median(List<double> values) => values.OrderBy(x => x).ElementAt(values.Count / 2);
                Console.WriteLine($"{item.Name},{item.Parts},{item.Source.Triangles.Length / 3 * item.Parts},{Median(oldSave):F1},{Median(newSave):F1},{Median(reuseSave):F1},{Median(oldLoad):F1},{Median(newLoad):F1},{oldBytes},{newBytes}");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
