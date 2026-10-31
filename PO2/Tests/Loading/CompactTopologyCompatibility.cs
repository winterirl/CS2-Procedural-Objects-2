using System;
using System.IO;
using System.Linq;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Persistence.PreparedTopology;
using Unity.Collections;

internal static partial class LoadingTests
{
    private static void WriteCompactFixture(string path)
    {
        var capture = PreparedContractCapture();
        var metadata = new SnapshotMetadata("contract-city", "contract-auto", "contract-map", null, SnapshotSaveKind.AutoSave, null);
        byte[] bytes = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, capture.Objects, prepared: capture);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllBytes(path, bytes);
        Console.WriteLine("Wrote compact fixture: " + bytes.Length + " bytes.");
    }

    private static void CompactReaderCompatibility(string path, bool expectFallback)
    {
        var expected = PreparedContractCapture();
        var read = SnapshotReader.Read(new SmallReadStream(File.ReadAllBytes(path)), true, FixtureSave, FixtureSession);
        int allocations = NativeAllocationChecks.Created;
        var restored = SnapshotModelRestorer.Restore(read.TakeObjectsForRestore());
        Check((NativeAllocationChecks.Created != allocations) == expectFallback, "reader uses expected prepared/fallback path");
        Check(ObjectBlockWriter.Write(PersistenceObjectData.Capture(restored.Single())).SequenceEqual(ObjectBlockWriter.Write(expected.Objects.Single())), "reader required object parity");
        Console.WriteLine("PASS compact reader compatibility; ordinary reconstruction=" + expectFallback);
    }

    private static void CompactFixtureChecks()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CompactTopology", "container3.bin");
        CompactReaderCompatibility(path, false);
        var bytes = File.ReadAllBytes(path);
        var expected = PreparedContractCapture();
        var metadata = new SnapshotMetadata("contract-city", "contract-auto", "contract-map", null, SnapshotSaveKind.AutoSave, null);
        using (var owned = SnapshotReader.Read(new MemoryStream(bytes)).TakeObjectsForRestore())
            for (int j = 0; j < expected.Parts[0].Length; j++) expected.Parts[0][j].Candidate = owned.TakePrepared(0, j);
        SnapshotReader.Verify(new SmallReadStream(bytes), FixtureSave, FixtureSession, metadata, expected.Objects.Select(o => o.PersistentId).ToArray(), prepared: expected);

        // Replace the optional extension with one full and one compact record.
        int footerAt = bytes.Length - PreparedTopologyExtension.FooterSize;
        long requiredEnd = (long)BitConverter.ToUInt64(bytes, footerAt + 16);
        var full = FullTopologyTestWriter.Encode(expected.Objects[0].Parts[0].Restore().CapturePersistenceTopology());
        var compact = expected.Parts[0][1].Candidate;
        byte[] mixed;
        using (var output = new MemoryStream())
        {
            output.Write(bytes, 0, (int)requiredEnd);
            long first = output.Position; full.WriteTo(output);
            long second = output.Position; compact.WriteTo(output);
            long directory = output.Position;
            using (var table = new MemoryStream())
            {
                int part = 0;
                foreach (var record in new[] { full, compact })
                {
                    SnapshotBinary.WriteGuid(table, expected.Objects[0].PersistentId);
                    SnapshotBinary.WriteUInt32(table, (uint)part);
                    SnapshotBinary.WriteUInt16(table, record.Version); SnapshotBinary.WriteUInt16(table, 0);
                    SnapshotBinary.WriteUInt64(table, (ulong)(part++ == 0 ? first : second));
                    SnapshotBinary.WriteUInt64(table, (ulong)record.Length); SnapshotBinary.WriteUInt64(table, (ulong)record.DecodedLength);
                    SnapshotBinary.WriteUInt32(table, record.Checksum()); SnapshotBinary.WriteUInt32(table, 0);
                    SnapshotBinary.WriteUInt32(table, record.Revision); SnapshotBinary.WriteUInt32(table, 0);
                }
                byte[] directoryBytes = table.ToArray();
                output.Write(directoryBytes, 0, directoryBytes.Length);
                var footer = bytes.Skip(footerAt).ToArray();
                Array.Copy(BitConverter.GetBytes((ulong)directory), 0, footer, 24, 8);
                Array.Copy(BitConverter.GetBytes(Crc32.Compute(directoryBytes)), 0, footer, 48, 4);
                Array.Copy(BitConverter.GetBytes(Crc32.Compute(footer, 0, 60)), 0, footer, 60, 4);
                output.Write(footer, 0, footer.Length);
            }
            mixed = output.ToArray();
        }
        int before = NativeAllocationChecks.Created;
        var result = SnapshotModelRestorer.Restore(SnapshotReader.Read(new SmallReadStream(mixed)).TakeObjectsForRestore());
        Check(NativeAllocationChecks.Created == before && result.Count == 1, "mixed versions both use prepared restore");
        Check(ObjectBlockWriter.Write(PersistenceObjectData.Capture(result[0])).SequenceEqual(ObjectBlockWriter.Write(expected.Objects[0])), "mixed version geometry parity");

        // A directory version cannot relabel a different payload version.
        int mixedDirectory = (int)BitConverter.ToUInt64(mixed, mixed.Length - 40);
        mixed[mixedDirectory + 20] = 2;
        Array.Copy(BitConverter.GetBytes(Crc32.Compute(mixed, mixedDirectory, 128)), 0, mixed, mixed.Length - 16, 4);
        Array.Copy(BitConverter.GetBytes(Crc32.Compute(mixed, mixed.Length - 64, 60)), 0, mixed, mixed.Length - 4, 4);
        before = NativeAllocationChecks.Created;
        result = SnapshotModelRestorer.Restore(SnapshotReader.Read(new SmallReadStream(mixed)).TakeObjectsForRestore());
        Check(NativeAllocationChecks.Created > before && result.Count == 1, "mismatched version falls back for the affected part");
    }
}
