using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Persistence;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Persistence.PreparedTopology;
using Unity.Collections;
using UnityEngine;

internal static partial class LoadingTests
{
    private static PersistencePartData PreparedFixture(int vertices, int[] triangles, StructuralFace[] faces,
        int[] mapping = null, StructuralTopologySource? source = StructuralTopologySource.Authored)
    {
        mapping = mapping ?? Enumerable.Range(0, vertices).ToArray();
        var area = Fixture(SnapshotFormat.kObjectSchemaVersion).Parts[0].SurfaceAreas[0];
        var areas = new[] { PersistenceSurfaceAreaData.FromDecoded(0, PO2.Materials.VertexAttributeFlags.None, area.NativeWindowKind, area.Material, area.ColorMask, area.OriginalMaterial, area.OriginalColorMask) };
        return PersistencePartData.FromDecoded("prepared", new Vector3[vertices], null, null, null, null, null,
            triangles, mapping, mapping.Length == 0 ? 0 : mapping.Max() + 1, faces, source,
            areas, new int[triangles.Length / 3], 1, Vector3.zero);
    }

    private static void PreparedTopologyChecks()
    {
        var samples = new List<PersistencePartData>
        {
            PreparedFixture(0, Array.Empty<int>(), Array.Empty<StructuralFace>()),
            PreparedFixture(4, Array.Empty<int>(), Array.Empty<StructuralFace>()),
            PreparedFixture(4, new[] { 0, 1, 2, 0, 2, 3 }, new[] { new StructuralFace(0, 0, 1, 2, 3) }),
            PreparedFixture(4, new[] { 0, 1, 2, 0, 2, 3 }, new[] { new StructuralFace(0, 0, 1, 2, 3) }, source: StructuralTopologySource.ConvertedInferred),
            PreparedFixture(5, new[] { 0, 1, 2, 0, 2, 3, 4, 4, 1, 0, 0, 0 }, Array.Empty<StructuralFace>(), new[] { 0, 1, 2, 0, 3 }),
            PreparedFixture(4, new[] { 0, 1, 2, 0, 2, 3, 0, 1, 2, 0, 2, 3 }, new[] { new StructuralFace(0, 0, 1, 2, 3), new StructuralFace(1, 0, 1, 2, 3) }),
            PreparedFixture(4, new[] { 2, 3, 0, 1, 2, 0, 1, 2, 0 }, new[] { new StructuralFace(0, 0, 1, 2, 3) }),
            PreparedFixture(6, new[] { 0, 1, 2, 3, 4, 5 }, new[] { new StructuralFace(0, 0, 1, 2), new StructuralFace(1, 3, 4, 5) }, new[] { 0, 1, 2, 0, 1, 2 })
        };
        var random = new Random(17231);
        for (int trial = 0; trial < 40; trial++)
        {
            var faces = new List<StructuralFace>();
            var triangles = new List<int[]>();
            for (int f = 0; f < 10 + trial; f++)
            {
                int a = random.Next(20), b, c;
                do { b = random.Next(20); } while (a == b);
                do { c = random.Next(20); } while (a == c || b == c);
                faces.Add(new StructuralFace(f, a, b, c));
                triangles.Add(new[] { b, c, a });
            }
            triangles = triangles.OrderBy(_ => random.Next()).ToList();
            samples.Add(PreparedFixture(20, triangles.SelectMany(t => t).ToArray(), faces.ToArray()));
        }
        foreach (var source in samples)
        {
            var ordinary = source.Restore();
            var encoded = PreparedTopologyCodec.Encode(ordinary.CapturePersistenceTopology());
            byte[] bytes;
            using (var stream = new MemoryStream()) { encoded.WriteTo(stream); bytes = stream.ToArray(); }
            Check(Crc32.Compute(bytes) == encoded.Checksum(), "incremental record CRC");
            using (var stream = new SmallReadStream(bytes)) encoded = PreparedTopologyCodec.Read(stream, bytes.Length, source);
            var validated = PreparedTopologyValidator.Validate(encoded, source);
            Check(validated.CanonicalMaps, "ordinary maps are canonical");
            int allocations = NativeAllocationChecks.Created;
            var restored = source.RestoreOwned(validated);
            Check(NativeAllocationChecks.Created == allocations, "prepared restore bypasses native reconstruction");
            CompareLogicalTopology(restored.Topology, ordinary.Topology);
            CompareStructuralOutputs(restored.StructuralTopology, ReferenceStructuralTopology.Build(ordinary.Topology, source.Vertices.Length, source.StructuralFaces), source.LogicalVertexCount);
            var a = restored.CapturePersistenceTopology(); var b = ordinary.CapturePersistenceTopology();
            Same(a.TriangleFaces, b.TriangleFaces, "prepared reverse maps");
            Check(a.FaceTriangles.SequenceEqual(b.FaceTriangles), "prepared forward maps");
            Throws<InvalidOperationException>(() => PersistencePartData.Capture(ordinary).RestoreOwned(validated));
            Check(!ReferenceEquals(source.Restore().Mesh.Vertices, source.Vertices), "reusable restore stays independent");
        }
        PreparedSemanticChecks(samples[2]);
        PreparedDuplicateChecks();
        PreparedContainerChecks();
    }

    private static EncodedPartTopology MutatePrepared(EncodedPartTopology encoded, PersistencePartData source, Action<PackedTopologyData> mutate)
    {
        var data = PreparedTopologyCodec.Decode(encoded, source);
        mutate(data);
        byte[] header = (byte[])encoded.Header.Clone();
        header[8] = 1; header[9] = 0; // This helper deliberately emits all 22 raw sections.
        var sections = new EncodedTopologySection[22];
        using (var descriptors = new MemoryStream(header, true))
        {
            descriptors.Position = PreparedTopologyCodec.HeaderSize;
            long offset = header.Length;
            for (int i = 0; i < 22; i++)
            {
                byte[] raw = i == 16 ? data.Forward : data[i + 1].SelectMany(BitConverter.GetBytes).ToArray();
                int count = i == 16 ? raw.Length : raw.Length / 4;
                sections[i] = new EncodedTopologySection(count, 0, raw);
                SnapshotBinary.WriteUInt16(descriptors, (ushort)(i + 1)); descriptors.WriteByte(0); descriptors.WriteByte(0);
                SnapshotBinary.WriteUInt32(descriptors, (uint)count); SnapshotBinary.WriteUInt64(descriptors, (ulong)offset);
                SnapshotBinary.WriteUInt32(descriptors, (uint)raw.Length); SnapshotBinary.WriteUInt32(descriptors, (uint)raw.Length);
                SnapshotBinary.WriteUInt32(descriptors, Crc32.Compute(raw)); SnapshotBinary.WriteUInt32(descriptors, 0);
                offset += raw.Length;
            }
            descriptors.Position = 44;
            SnapshotBinary.WriteUInt32(descriptors, Crc32.Compute(header, 48, header.Length - 48));
        }
        return new EncodedPartTopology(header, sections);
    }

    private static void PreparedSemanticChecks(PersistencePartData source)
    {
        var part = source.Restore();
        var encoded = PreparedTopologyCodec.Encode(part.CapturePersistenceTopology());
        for (int id = 1; id <= 22; id++)
        {
            int section = id;
            var bad = MutatePrepared(encoded, source, data =>
            {
                if (section == 17) data.Forward[0] ^= 1;
                else data[section][0] = int.MaxValue;
            });
            Throws<InvalidDataException>(() => PreparedTopologyValidator.Validate(bad, source));
        }
        // Self-consistent counts/CRCs cannot establish a relationship to geometry.
        var wrongOrder = MutatePrepared(encoded, source, d => { var v = d[2][0]; d[2][0] = d[2][1]; d[2][1] = v; });
        Throws<InvalidDataException>(() => PreparedTopologyValidator.Validate(wrongOrder, source));
        var badMembership = PersistencePartData.Capture(part);
        badMembership.TriangleSurfaceAreaIds[1] = 7;
        var validated = PreparedTopologyValidator.Validate(encoded, badMembership);
        Throws<InvalidOperationException>(() => badMembership.RestoreOwned(validated));
        Throws<InvalidOperationException>(() => MeshPart.ValidatePreparedSurfaceAreas(badMembership, validated.Data));
        var legacy = PreparedFixture(4, source.Triangles, source.StructuralFaces, source: null);
        Throws<InvalidDataException>(() => PreparedTopologyValidator.Validate(encoded, legacy));
        var unknown = PreparedFixture(4, source.Triangles, source.StructuralFaces, source: (StructuralTopologySource)255);
        Throws<InvalidDataException>(() => PreparedTopologyValidator.Validate(encoded, unknown));
        foreach (int offset in new[] { 0, 8, 12, 16, 40, 44, 48, 50, 52, 56, 64, 68, 72, 76 })
        {
            byte[] bytes;
            using (var stream = new MemoryStream()) { encoded.WriteTo(stream); bytes = stream.ToArray(); }
            bytes[offset] ^= 127;
            Throws<InvalidDataException>(() => PreparedTopologyCodec.Read(new MemoryStream(bytes), bytes.Length, source));
        }
    }

    private static void PreparedDuplicateChecks()
    {
        var source = PreparedFixture(3, new[] { 0, 1, 2, 1, 2, 0, 0, 1, 2 }, new[] { new StructuralFace(0, 0, 1, 2), new StructuralFace(1, 0, 1, 2) });
        var ordinary = source.Restore();
        var encoded = PreparedTopologyCodec.Encode(ordinary.CapturePersistenceTopology());
        foreach (bool gap in new[] { false, true })
        {
            var changed = MutatePrepared(encoded, source, d =>
            {
                d[20][0] = gap ? 0 : 1; d[20][1] = gap ? 2 : 0;
                d[22][0] = gap ? 0 : 1; d[22][1] = gap ? -1 : 0; d[22][2] = gap ? 1 : -1;
            });
            var validated = PreparedTopologyValidator.Validate(changed, source);
            Check(!validated.CanonicalMaps, "noncanonical duplicate assignment detected");
            int allocations = NativeAllocationChecks.Created;
            var restored = source.RestoreOwned(validated);
            Check(NativeAllocationChecks.Created == allocations, "noncanonical map retains adjacency");
            Same(restored.CapturePersistenceTopology().TriangleFaces, ordinary.CapturePersistenceTopology().TriangleFaces, "duplicate map rebuilt canonically");
        }
    }

    private static void PreparedContainerChecks()
    {
        var live = Enumerable.Range(1, 4).Select(i => StableFixture(i, SnapshotFormat.kObjectSchemaVersion).Restore(i)).ToArray();
        var session = new TopologyEncodingSession();
        var capture = new CitySaveCapture(live, session);
        var metadata = new SnapshotMetadata("city", "auto", "map", null, SnapshotSaveKind.AutoSave, null);
        var options = new SnapshotEncodingOptions(SaveCompression.Fast, 4);
        byte[] bytes = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, capture.Objects, options, prepared: capture);
        var ids = capture.Objects.Select(o => o.PersistentId).ToArray();
        using (var stream = new SmallReadStream(bytes)) SnapshotReader.Verify(stream, FixtureSave, FixtureSession, metadata, ids, prepared: capture);
        Check(BitConverter.ToUInt16(bytes, 8) == 3, "city saves use container 3");
        var read = SnapshotReader.Read(new MemoryStream(bytes), true);
        Check(read.Metadata.SaveKind == SnapshotSaveKind.AutoSave && read.ObjectCount == 4, "container 3 metadata and autosave");
        int allocations = NativeAllocationChecks.Created;
        var restored = SnapshotModelRestorer.Restore(read.TakeObjectsForRestore(session));
        Check(allocations == NativeAllocationChecks.Created, "whole city uses prepared restoration");
        Check(session.Find(restored[0].Parts[0].CapturePersistenceTopology()) == null, "load candidates stay unpublished");
        session.CommitLoaded(restored);
        var loadedCapture = new CitySaveCapture(restored, session);
        Check(loadedCapture.Parts.All(p => p.All(c => c.Candidate != null)), "load seeds encoded reuse");
        var unchanged = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, loadedCapture.Objects, options, prepared: loadedCapture);
        Check(bytes.SequenceEqual(unchanged), "loaded topology can be copied without regeneration");
        var reused = loadedCapture.Parts[0][0].Candidate;
        restored[0].Parts[0].Mesh.Vertices[0] = new Vector3(9, 8, 7);
        Check(ReferenceEquals(new CitySaveCapture(restored, session).Parts[0][0].Candidate, reused), "position edit reuses topology");
        restored[0].Parts[0].TopologyVersion++;
        Check(new CitySaveCapture(restored, session).Parts[0][0].Candidate == null, "topology version invalidates reuse");
        session.Retain(Array.Empty<ProceduralObject>());
        Check(new CitySaveCapture(restored, session).Parts.All(p => p.All(c => c.Candidate == null)), "removed parts pruned");
        session.Promote(capture);
        Check(ReferenceEquals(new CitySaveCapture(live, session).Parts[0][0].Candidate, capture.Parts[0][0].Candidate), "committed save can seed reuse");
        session.Clear();
        Check(new CitySaveCapture(live, session).Parts[0][0].Candidate == null, "session reset clears reuse");

        int footer = bytes.Length - 64;
        int directory = checked((int)BitConverter.ToUInt64(bytes, footer + 24));
        int payload = checked((int)BitConverter.ToUInt64(bytes, directory + 24));
        var variants = new List<byte[]>();
        foreach (int offset in new[] { footer, footer + 8, footer + 16, footer + 24, footer + 40, footer + 60, directory })
        {
            var bad = (byte[])bytes.Clone(); bad[offset] ^= 1; variants.Add(bad);
        }
        variants.Add(bytes.Take(bytes.Length - 20).ToArray());
        foreach (var bad in variants)
        {
            var result = SnapshotReader.Read(new MemoryStream(bad), true);
            var objects = SnapshotModelRestorer.Restore(result.TakeObjectsForRestore());
            Check(objects.Count == 4, "bad optional index falls back without losing objects");
            Throws<InvalidDataException>(() => SnapshotReader.Verify(new MemoryStream(bad), FixtureSave, FixtureSession, metadata, ids, prepared: capture));
        }
        var badPart = (byte[])bytes.Clone(); badPart[payload + 752] ^= 1;
        var partialSession = new TopologyEncodingSession();
        var partial = SnapshotModelRestorer.Restore(SnapshotReader.Read(new MemoryStream(badPart)).TakeObjectsForRestore(partialSession));
        partialSession.CommitLoaded(partial);
        var partialCapture = new CitySaveCapture(partial, partialSession);
        Check(partial.Count == 4 && partialCapture.Parts[0][0].Candidate == null && partialCapture.Parts[0][1].Candidate != null, "one damaged part has local fallback");
        var badObject = (byte[])bytes.Clone();
        int primaryDirectory = (int)BitConverter.ToUInt64(bytes, 56);
        int objectOffset = (int)BitConverter.ToUInt64(bytes, primaryDirectory + 20);
        badObject[objectOffset + 16] ^= 1;
        var skippedSession = new TopologyEncodingSession();
        var skipped = SnapshotModelRestorer.Restore(SnapshotReader.Read(new MemoryStream(badObject)).TakeObjectsForRestore(skippedSession));
        skippedSession.CommitLoaded(skipped);
        var skippedCapture = new CitySaveCapture(skipped, skippedSession);
        Check(skipped[0].PersistentId == ids[1] && skippedCapture.Parts.All(p => p.All(c => c.Candidate != null)), "optional bindings survive skipped primary object");
        var empty = new CitySaveCapture(Array.Empty<ProceduralObject>(), null);
        byte[] emptyBytes = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, empty.Objects, options, prepared: empty);
        SnapshotReader.Verify(new MemoryStream(emptyBytes), FixtureSave, FixtureSession, metadata, Array.Empty<Guid>(), prepared: empty);
        Check(SnapshotReader.Read(new MemoryStream(emptyBytes)).ObjectCount == 0, "empty city extension");
        PreparedTransactionChecks(capture, metadata, options, bytes);
    }

    private static void PreparedTransactionChecks(CitySaveCapture capture, SnapshotMetadata metadata, SnapshotEncodingOptions options, byte[] expected)
    {
        string root = Path.Combine(Path.GetTempPath(), "po2-prepared-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SnapshotFileStore(root);
            var write = store.WriteTransactional("save", FixtureSave, FixtureSession, metadata, capture.Objects, options, capture);
            Check(write.ContainerVersion == 3 && expected.SequenceEqual(File.ReadAllBytes(write.FinalPath)), "transaction commits container 3");
            foreach (string stage in new[] { "write", "flush", "durable", "reopen", "read", "verify", "replace" })
            {
                var failing = new SnapshotFileStore(root, new FailingFileIO(stage));
                Check(FailureOf(() => failing.WriteTransactional("save", FixtureSave, FixtureSession, metadata, capture.Objects, options, capture)) != null, "prepared transaction failure: " + stage);
                Check(expected.SequenceEqual(File.ReadAllBytes(write.FinalPath)), "prepared failure retains previous final");
            }
            var old = capture.Parts[0][0].Candidate;
            // Corrupt a retained assignment; the compact writer intentionally drops reverse render tables.
            capture.Parts[0][0].Candidate = MutatePrepared(old, capture.Objects[0].Parts[0], d => d[7][0] = int.MaxValue);
            Throws<InvalidDataException>(() => store.WriteTransactional("save", FixtureSave, FixtureSession, metadata, capture.Objects, options, capture));
            Check(expected.SequenceEqual(File.ReadAllBytes(write.FinalPath)), "bad cached topology cannot commit");
            capture.Parts[0][0].Candidate = old;
            capture.Objects[0].Parts[0].Triangles[0] = 3;
            Throws<InvalidDataException>(() => store.WriteTransactional("save", FixtureSave, FixtureSession, metadata, capture.Objects, options, capture));
            Check(expected.SequenceEqual(File.ReadAllBytes(write.FinalPath)), "stale topology cannot commit");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
