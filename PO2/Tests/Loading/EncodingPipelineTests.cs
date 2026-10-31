using System;
using System.IO;
using System.Linq;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

internal static partial class LoadingTests
{
    private static void EncodingPipelineChecks()
    {
        var source = StableFixture(101, SnapshotFormat.kObjectSchemaVersion);
        var empty = PersistenceObjectData.FromDecoded(source.PersistentId, source.Name, source.IsLocked, source.ReceivesDecals,
            source.Transform, source.CustomOrientations, Array.Empty<PersistencePartData>(), source.LocalPivot, source.LodProfile);
        var metadata = new SnapshotMetadata("city", "save", "map", null, SnapshotSaveKind.Manual, null);
        foreach (var objects in new[] { Array.Empty<PersistenceObjectData>(), new[] { empty },
            new[] { empty, StableFixture(102, SnapshotFormat.kObjectSchemaVersion, true), StableFixture(103, SnapshotFormat.kObjectSchemaVersion) } })
        foreach (int workers in new[] { 1, 4, 8, 16, 32 })
        {
            var metrics = new EncodingMetrics();
            var bytes = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, objects,
                new SnapshotEncodingOptions(SaveCompression.Fast, workers), metrics);
            var read = SnapshotReader.Read(new MemoryStream(bytes), true);
            Check(read.ObjectCount == objects.Length && read.SkippedObjects.Count == 0, "empty/mixed object pipeline");
            Check(metrics.PartCount == objects.Sum(o => o.Parts.Length) && metrics.PeakWorkers <= workers, "encoding accounting and bounds");
            Check(SnapshotReader.Verify(new MemoryStream(bytes), FixtureSave, FixtureSession, metadata, objects.Select(o => o.PersistentId).ToArray()).ObjectCount == objects.Length, "empty/mixed verification");
        }
        Throws<InvalidDataException>(() => SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, new[] { source, source }));
        var part = StableFixture(105, SnapshotFormat.kObjectSchemaVersion, true).Parts[0];
        using (var prepared = ObjectBlockWriter.PreparePart(part, new EncodingContext(SaveCompression.Compact)))
        using (var output = new MemoryStream())
        {
            prepared.WriteTo(output);
            Check(output.Length == prepared.Length, "prepared part exact byte length");
        }
    }
}
