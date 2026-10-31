using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PO2.Core;
using PO2.Geometry.Topology;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Persistence.PreparedTopology;
using Unity.Collections;

internal static partial class LoadingTests
{
    private static void CompactTopologyChecks()
    {
        var cases = new List<PersistencePartData>
        {
            PreparedGrid(24, false), PreparedGrid(24, true), CompactShuffledFixture(PreparedGrid(24, false)),
            PreparedFixture(0, Array.Empty<int>(), Array.Empty<StructuralFace>()),
            PreparedFixture(4, Array.Empty<int>(), Array.Empty<StructuralFace>()),
            PreparedFixture(5, new[] { 0, 1, 2, 0, 2, 3, 4, 4, 1, 0, 0, 0 }, Array.Empty<StructuralFace>(), new[] { 0, 1, 2, 0, 3 }),
            PreparedFixture(4, new[] { 0, 1, 2, 0, 2, 3 }, new[] { new StructuralFace(0, 0, 1, 2, 3) }, new[] { 0, 1, 0, 2 }),
            PreparedFixture(3, new[] { 0, 1, 2, 1, 2, 0, 0, 1, 2 }, new[] { new StructuralFace(0, 0, 1, 2), new StructuralFace(1, 0, 1, 2) })
        };
        var random = new Random(172971);
        for (int trial = 0; trial < 100; trial++)
        {
            int l = 10 + trial;
            int[] mapping = Enumerable.Range(0, l).Concat(Enumerable.Range(0, l).Select(_ => random.Next(l))).ToArray();
            var faces = new List<StructuralFace>();
            var triangles = new List<int[]>();
            for (int f = 0; f < 50 + trial; f++)
            {
                int a = random.Next(mapping.Length), b, c;
                do { b = random.Next(mapping.Length); } while (mapping[a] == mapping[b]);
                do { c = random.Next(mapping.Length); } while (mapping[c] == mapping[a] || mapping[c] == mapping[b]);
                faces.Add(new StructuralFace(f, a, b, c));
                triangles.Add(new[] { b, c, a });
            }
            triangles.Add(new[] { 0, 0, 0 });
            triangles = triangles.OrderBy(_ => random.Next()).ToList();
            cases.Add(PreparedFixture(mapping.Length, triangles.SelectMany(t => t).ToArray(), faces.ToArray(), mapping));
        }
        foreach (var source in cases)
        {
            var part = source.Restore();
            var capture = part.CapturePersistenceTopology();
            var full = FullTopologyTestWriter.Encode(capture);
            var compact = PreparedTopologyCodec.Encode(capture);
            capture.Candidate = full;
            var projected = PreparedTopologyCodec.PrepareForWrite(capture);
            Check(projected.Version == 2 && compact.Version == 2 && full.Version == 1, "separate record versions");
            Check(projected.DecodedLength == full.DecodedLength && compact.DecodedLength == full.DecodedLength, "decoded footprint retains all tables");
            for (int i = 0; i < 22; i++)
            {
                bool derived = PreparedTopologyLayout.IsDerived(2, i + 1);
                Check(projected.Sections[i].Bytes.SequenceEqual(compact.Sections[i].Bytes), "projection matches fresh encoding");
                Check(derived ? projected.Sections[i].Bytes.Length == 0 : ReferenceEquals(full.Sections[i], projected.Sections[i]), "upgrade shares retained bytes");
            }
            capture.Candidate = projected;
            Check(ReferenceEquals(projected, PreparedTopologyCodec.PrepareForWrite(capture)), "current candidate reuse");
            var expected = PreparedTopologyValidator.Validate(full, source);
            foreach (var encoded in new[] { compact, projected })
            {
                byte[] bytes = CompactBytes(encoded);
                var read = PreparedTopologyCodec.Read(new SmallReadStream(bytes), bytes.Length, source, Crc32.Compute(bytes));
                var validated = PreparedTopologyValidator.Validate(read, source);
                for (int id = 1; id <= 22; id++)
                {
                    if (id == 17) Check(validated.Data.Forward.SequenceEqual(expected.Data.Forward), "compact directions");
                    else Same(validated.Data[id], expected.Data[id], "compact section " + id);
                }
                // Run the original full semantic checker independently over the expansion.
                Check(PreparedTopologyValidator.Validate(MutatePrepared(encoded, source, _ => { }), source).CanonicalMaps == expected.CanonicalMaps, "full validation of compact expansion");
                int allocations = NativeAllocationChecks.Created;
                var restored = source.RestoreOwned(validated);
                Check(NativeAllocationChecks.Created == allocations, "compact avoids native topology builders");
                CompareLogicalTopology(restored.Topology, part.Topology);
                var a = restored.CapturePersistenceTopology();
                Check(a.FaceTriangles.SequenceEqual(capture.FaceTriangles), "compact forward maps");
                Same(a.TriangleFaces, capture.TriangleFaces, "compact reverse maps");
            }
        }
        CompactCorruptionChecks();
        CompactReuseChecks();
    }

    private static byte[] CompactBytes(EncodedPartTopology encoded)
    {
        using (var stream = new MemoryStream()) { encoded.WriteTo(stream); return stream.ToArray(); }
    }

    private static void CompactCorruptionChecks()
    {
        var source = PreparedGrid(8, false);
        var capture = source.Restore().CapturePersistenceTopology();
        var compact = PreparedTopologyCodec.Encode(capture);
        foreach (int id in new[] { 7, 8, 9, 10, 16, 20, 21 })
        foreach (int badValue in new[] { -1, int.MaxValue })
        {
            capture.Candidate = MutatePrepared(compact, source, d => d[id][0] = badValue);
            var bad = PreparedTopologyCodec.PrepareForWrite(capture);
            Throws<InvalidDataException>(() => PreparedTopologyValidator.Validate(bad, source));
        }
        foreach (Action<PackedTopologyData> mutate in new Action<PackedTopologyData>[]
        {
            d => d[7][0] = d[8][0],
            d => { d[7][1] = d[7][0]; d[8][1] = d[8][0]; },
            d => d[10][d[9][1]] = d.TriangleCount,
            d => d[16][1] = d[16][0],
            d => d[20][1] = d[20][0],
            d => d[21][0] = d[20][0],
            d => d[4] = new int[d[4].Length - 1],
            d => d[14] = new int[d[14].Length - 1]
        })
        {
            capture.Candidate = MutatePrepared(compact, source, mutate);
            var bad = PreparedTopologyCodec.PrepareForWrite(capture);
            Throws<InvalidDataException>(() => PreparedTopologyValidator.Validate(bad, source));
        }
        void ValidateBytes(byte[] bytes)
        {
            var encoded = PreparedTopologyCodec.Read(new SmallReadStream(bytes), bytes.Length, source);
            PreparedTopologyValidator.Validate(encoded, source);
        }
        void FixTable(byte[] bytes) => Array.Copy(BitConverter.GetBytes(Crc32.Compute(bytes, 48, 704)), 0, bytes, 44, 4);
        for (int id = 1; id <= 22; id++)
        {
            byte[] bytes = CompactBytes(compact);
            int descriptor = 48 + 32 * (id - 1);
            bytes[descriptor + 2] = PreparedTopologyLayout.IsDerived(2, id) ? (byte)0 : (byte)3;
            FixTable(bytes);
            Throws<InvalidDataException>(() => ValidateBytes(bytes));
            bytes = CompactBytes(compact);
            bytes[descriptor + 4] ^= 1;
            FixTable(bytes);
            Throws<InvalidDataException>(() => ValidateBytes(bytes));
        }
        foreach (int offset in new[] { 8, 12, 48 + 3, 48 + 8, 48 + 16, 48 + 20, 48 + 24, 48 + 28, 752 })
        {
            byte[] bytes = CompactBytes(compact);
            bytes[offset] ^= 127;
            if (offset >= 48 && offset < 752) FixTable(bytes);
            Throws<InvalidDataException>(() => ValidateBytes(bytes));
        }
        byte[] valid = CompactBytes(compact);
        foreach (int length in new[] { 0, 751, valid.Length - 1 })
            Check(FailureOf(() => ValidateBytes(valid.Take(length).ToArray())) != null, "compact truncation rejected");

        var duplicate = PreparedFixture(3, new[] { 0, 1, 2, 0, 1, 2 }, new[] { new StructuralFace(0, 0, 1, 2), new StructuralFace(1, 0, 1, 2) });
        var duplicateCapture = duplicate.Restore().CapturePersistenceTopology();
        duplicateCapture.Candidate = MutatePrepared(PreparedTopologyCodec.Encode(duplicateCapture), duplicate,
            d => { d[20][0] = 1; d[20][1] = 0; d[22][0] = 1; d[22][1] = 0; });
        var noncanonical = PreparedTopologyValidator.Validate(PreparedTopologyCodec.PrepareForWrite(duplicateCapture), duplicate);
        Check(!noncanonical.CanonicalMaps, "compact preserves noncanonical detection");
        Same(duplicate.RestoreOwned(noncanonical).CapturePersistenceTopology().TriangleFaces, new[] { 0, 1 }, "compact duplicate fallback order");
    }

    private static void CompactReuseChecks()
    {
        var live = new[] { StableFixture(271, SnapshotFormat.kObjectSchemaVersion).Restore(1) };
        var session = new TopologyEncodingSession();
        foreach (var part in live[0].Parts)
        {
            var capture = part.CapturePersistenceTopology();
            session.Store(capture, FullTopologyTestWriter.Encode(capture));
        }
        var city = new CitySaveCapture(live, session);
        Check(city.Parts[0].All(p => p.Candidate.Version == 1), "old candidates captured without main-thread conversion");
        var metadata = new SnapshotMetadata("city", "save", "map", null, SnapshotSaveKind.AutoSave, null);
        byte[] bytes = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, city.Objects, prepared: city);
        SnapshotReader.Verify(new MemoryStream(bytes), FixtureSave, FixtureSession, metadata, city.Objects.Select(o => o.PersistentId).ToArray(), prepared: city);
        Check(city.Parts[0].All(p => p.Candidate.Version == 2), "writer projects old candidates");
        session.Promote(city);
        var diskSession = new TopologyEncodingSession();
        var restored = PO2.Persistence.Model.SnapshotModelRestorer.Restore(SnapshotReader.Read(new MemoryStream(bytes)).TakeObjectsForRestore(diskSession));
        diskSession.CommitLoaded(restored);
        var captured = new CitySaveCapture(restored, diskSession);
        Check(captured.Parts[0].All(p => p.Candidate != null && p.Candidate.Version == 2), "restart-loaded compact reuse");
        byte[] again = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, captured.Objects, prepared: captured);
        Check(bytes.SequenceEqual(again), "restart roundtrip bytes");
        var part0 = restored[0].Parts[0];
        part0.Mesh.Vertices[0] = part0.Mesh.Vertices[0] + new UnityEngine.Vector3(1, 0, 0);
        Check(diskSession.Find(part0.CapturePersistenceTopology()) != null, "position changes retain compact adjacency reuse");
        part0.TopologyVersion++;
        Check(diskSession.Find(part0.CapturePersistenceTopology()) == null, "topology changes invalidate compact reuse");
    }
}
