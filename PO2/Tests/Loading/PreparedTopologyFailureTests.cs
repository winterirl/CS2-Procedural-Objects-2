using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using PO2.Core;
using PO2.Geometry.Topology;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Persistence.PreparedTopology;
using UnityEngine;

internal static partial class LoadingTests
{
    private static void PreparedAdditionalChecks()
    {
        // More than one shuffle block, including a partial last block.
        foreach (int side in new[] { 36, 65 })
        {
            var source = PreparedGrid(side, false);
            var ordinary = source.Restore();
            var encoded = PreparedTopologyCodec.Encode(ordinary.CapturePersistenceTopology());
            Check(encoded.Sections.Any(s => s.Codec == 2), "integer sections use bounded shuffled compression");
            byte[] bytes;
            using (var output = new MemoryStream()) { encoded.WriteTo(output); bytes = output.ToArray(); }
            using (var input = new SmallReadStream(bytes)) encoded = PreparedTopologyCodec.Read(input, bytes.Length, source);
            var prepared = PreparedTopologyValidator.Validate(encoded, source);
            CompareLogicalTopology(source.RestoreOwned(prepared).Topology, ordinary.Topology);
        }

        // An authored quad can repeat a logical boundary without repeating render corners.
        var repeated = PreparedFixture(4, new[] { 0, 1, 2, 0, 2, 3 }, new[] { new StructuralFace(0, 0, 1, 2, 3) }, new[] { 0, 1, 0, 2 });
        var original = repeated.Restore();
        var packed = PreparedTopologyCodec.Encode(original.CapturePersistenceTopology());
        var duplicateBoundary = repeated.RestoreOwned(PreparedTopologyValidator.Validate(packed, repeated));
        for (int i = 0; i < 4; i++)
        {
            Check(original.StructuralTopology.GetBoundaryEdgeId(0, i) == duplicateBoundary.StructuralTopology.GetBoundaryEdgeId(0, i), "repeated boundary ID");
            Check(original.StructuralTopology.IsBoundaryForward(0, i) == duplicateBoundary.StructuralTopology.IsBoundaryForward(0, i), "repeated boundary direction");
        }
        var legacy = PreparedFixture(4, new[] { 0, 1, 2, 0, 2, 3 }, Array.Empty<StructuralFace>(), source: null);
        legacy.Vertices[1] = new Vector3(1, 0, 0); legacy.Vertices[2] = new Vector3(1, 0, 1); legacy.Vertices[3] = new Vector3(0, 0, 1);
        var upgraded = legacy.Restore();
        Check(upgraded.StructuralTopology.FaceCount > 0 && upgraded.StructuralTopologySource != StructuralTopologySource.Authored, "legacy inference remains real and active");
        var upgradedSource = PersistencePartData.Capture(upgraded);
        Check(PreparedTopologyValidator.Validate(PreparedTopologyCodec.Encode(upgraded.CapturePersistenceTopology()), upgradedSource).CanonicalMaps, "upgraded legacy geometry can be prepared on next save");

        var live = new[] { StableFixture(1, SnapshotFormat.kObjectSchemaVersion).Restore(1) };
        var capture = new CitySaveCapture(live, null);
        var metadata = new SnapshotMetadata("city", "save", "map", null, SnapshotSaveKind.Manual, null);
        byte[] good = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, capture.Objects, prepared: capture);
        int footer = good.Length - 64;
        int index = (int)BitConverter.ToUInt64(good, footer + 24);
        var ids = capture.Objects.Select(o => o.PersistentId).ToArray();
        void FixOptional(byte[] data)
        {
            Array.Copy(BitConverter.GetBytes(Crc32.Compute(data, index, footer - index)), 0, data, footer + 48, 4);
            Array.Copy(BitConverter.GetBytes(Crc32.Compute(data, footer, 60)), 0, data, footer + 60, 4);
        }
        foreach (int scenario in Enumerable.Range(0, 9))
        {
            var bad = (byte[])good.Clone();
            if (scenario == 0) Array.Copy(bad, index, bad, index + 64, 20); // duplicate key
            if (scenario == 1) Array.Copy(BitConverter.GetBytes(ulong.MaxValue), 0, bad, index + 24, 8);
            if (scenario == 2) Array.Copy(BitConverter.GetBytes(uint.MaxValue), 0, bad, index + 16, 4);
            if (scenario == 3) Array.Copy(bad, index + 24, bad, index + 64 + 24, 8); // overlap
            if (scenario == 4) bad[index + 20] = 99; // unknown part version, local fallback
            if (scenario == 5) bad[index + 56] = 99; // unknown interpretation, local fallback
            if (scenario == 6) Array.Copy(Guid.NewGuid().ToByteArray(), 0, bad, index, 16);
            if (scenario == 7) Array.Copy(BitConverter.GetBytes(ulong.MaxValue), 0, bad, index + 32, 8);
            if (scenario == 8) Array.Copy(BitConverter.GetBytes(uint.MaxValue), 0, bad, footer + 40, 4);
            FixOptional(bad);
            var read = SnapshotReader.Read(new MemoryStream(bad));
            Check(SnapshotModelRestorer.Restore(read.TakeObjectsForRestore()).Count == 1, "invalid self-checksummed optional index remains recoverable");
            Check(FailureOf(() => SnapshotReader.Verify(new MemoryStream(bad), FixtureSave, FixtureSession, metadata, ids, prepared: capture)) != null, "new invalid extension cannot pass strict verification");
        }
        // A valid city save may omit a representation the encoder cannot supply.
        capture.Parts[0][0] = new PartTopologyCapture(live[0].Parts[0], null, null);
        byte[] omitted = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, capture.Objects, prepared: capture);
        SnapshotReader.Verify(new MemoryStream(omitted), FixtureSave, FixtureSession, metadata, ids, prepared: capture);
        Check(SnapshotModelRestorer.Restore(SnapshotReader.Read(new MemoryStream(omitted)).TakeObjectsForRestore()).Count == 1, "omitted optional record uses ordinary restore");
        Check(capture.Parts[0][0].Candidate == null, "no incompatible representation retained");

        // A correct adjacency record with a noncanonical duplicate assignment
        // remains usable, but must not be promoted as reusable encoded topology.
        var duplicateSource = PreparedFixture(3, new[] { 0, 1, 2, 0, 1, 2 },
            new[] { new StructuralFace(0, 0, 1, 2), new StructuralFace(1, 0, 1, 2) });
        var duplicateObject = PersistenceObjectData.FromDecoded(live[0].PersistentId, "duplicates", false, false,
            live[0].Transform, Array.Empty<PersistenceCustomOrientationData>(), new[] { duplicateSource }).Restore(1);
        var duplicateCapture = new CitySaveCapture(new[] { duplicateObject }, null);
        var canonical = PreparedTopologyCodec.Encode(duplicateCapture.Parts[0][0]);
        duplicateCapture.Parts[0][0].Candidate = MutatePrepared(canonical, duplicateSource, d =>
        { d[20][0] = 1; d[20][1] = 0; d[22][0] = 1; d[22][1] = 0; });
        byte[] duplicateBytes = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, duplicateCapture.Objects, prepared: duplicateCapture);
        SnapshotReader.Verify(new MemoryStream(duplicateBytes), FixtureSave, FixtureSession, metadata, ids, prepared: duplicateCapture);
        var noReuse = new TopologyEncodingSession(); noReuse.Promote(duplicateCapture);
        Check(noReuse.Find(duplicateObject.Parts[0].CapturePersistenceTopology()) == null, "map-fallback record not promoted after save");
        var duplicateRestored = SnapshotModelRestorer.Restore(SnapshotReader.Read(new MemoryStream(duplicateBytes)).TakeObjectsForRestore(noReuse));
        noReuse.CommitLoaded(duplicateRestored);
        Check(noReuse.Find(duplicateRestored[0].Parts[0].CapturePersistenceTopology()) == null, "map-fallback record not promoted after load");
        Same(duplicateRestored[0].Parts[0].CapturePersistenceTopology().TriangleFaces, new[] { 0, 1 }, "canonical map restored from saved noncanonical record");

        var session = new TopologyEncodingSession();
        var staging = SnapshotModelRestorer.Restore(SnapshotReader.Read(new MemoryStream(good)).TakeObjectsForRestore(session));
        session.CommitLoaded(Array.Empty<ProceduralObject>());
        Check(session.Find(staging[0].Parts[0].CapturePersistenceTopology()) == null, "failed publication cannot seed reuse");
        var captured = new CitySaveCapture(live, null);
        SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, captured.Objects, prepared: captured);
        session.Promote(captured);
        var replacement = captured.Objects[0].Restore(4);
        replacement.Parts[0].PartHistoryId = live[0].Parts[0].PartHistoryId;
        Check(session.Find(replacement.Parts[0].CapturePersistenceTopology()) == null, "history identity alone cannot select stale bytes");
        var weak = PreparedWeakEntry(session);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!weak.IsAlive, "encoded cache does not own live parts");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PreparedWeakEntry(TopologyEncodingSession session)
    {
        var part = StableFixture(7, SnapshotFormat.kObjectSchemaVersion).Restore(7).Parts[0];
        var capture = part.CapturePersistenceTopology();
        session.Store(capture, PreparedTopologyCodec.Encode(capture));
        return new WeakReference(part);
    }
}
