using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PO2;
using PO2.Persistence;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

internal static partial class PersistenceTests {
    private static int s_Checks;
    private static readonly Guid Save = new Guid("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid Session = new Guid("11223344-5566-7788-99aa-bbccddeeff00");
    private static readonly SnapshotMetadata Metadata = new SnapshotMetadata(
        "City \u96ea", "Manual \"one\"\nSave", "Map \\ One", new DateTime(2026, 10, 11, 4, 30, 0, DateTimeKind.Utc),
        SnapshotSaveKind.Manual, new SnapshotCityDate(2038, 210, 23, 59));

    public static void Main(string[] args) {
        LegacyFixtures();
        LodProfiles();
        MetadataRoundTrips();
        CaptureAndDeferredValues();
        MetadataFailures();
        ContainerFailures();
        FileStorage();
        SaveDiscovery();
        SaveManagement();
        SaveActivity();
        UIBindings();
        EncodingProfiles();
        Console.WriteLine($"PASS: {s_Checks} persistence metadata, compatibility, and file-store checks.");
    }

    private static void Check(bool condition, string message) {
        s_Checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Equal(byte[] actual, byte[] expected, string message) =>
        Check(actual.SequenceEqual(expected), message);

    private static void Throws<T>(Action action, string message) where T : Exception {
        try { action(); }
        catch (TargetInvocationException exception) when (exception.InnerException is T) { Check(true, message); return; }
        catch (T) { Check(true, message); return; }
        throw new Exception("Expected " + typeof(T).Name + ": " + message);
    }

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    private static byte[] Legacy(int schema) => Fixture("v1-object-" + schema + ".po2");
    private static SnapshotReadResult Read(byte[] bytes) {
        Check(SnapshotReader.TryRead(bytes, out var result, out string error), "Read: " + error);
        return result;
    }
    private static PersistenceObjectData[] Objects() => Read(Legacy(7)).Objects.ToArray();
    private static byte[] Write(SnapshotMetadata metadata = null) =>
        SnapshotWriter.Write(Save, Session, metadata ?? Metadata, Objects());
    private static int DirectoryOffset(byte[] bytes) => checked((int)BitConverter.ToUInt64(bytes, 56));
    private static int FirstBlock(byte[] bytes) => checked((int)BitConverter.ToUInt64(bytes, DirectoryOffset(bytes) + 20));
    private static byte[] Slice(byte[] bytes, int start, int count) {
        byte[] result = new byte[count];
        Buffer.BlockCopy(bytes, start, result, 0, count);
        return result;
    }
    private static void Set32(byte[] bytes, int offset, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    private static void Set64(byte[] bytes, int offset, ulong value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    private static void HeaderCrc(byte[] bytes) => Set32(bytes, 76, Crc32.Compute(Slice(bytes, 0, 76)));
    private static void MetadataCrc(byte[] bytes) => Set32(bytes, 92, Crc32.Compute(Slice(bytes, 96, DirectoryOffset(bytes) - 96)));
    private static void DirectoryCrc(byte[] bytes) {
        Set32(bytes, 72, Crc32.Compute(Slice(bytes, DirectoryOffset(bytes), checked((int)BitConverter.ToUInt64(bytes, 64)))));
        HeaderCrc(bytes);
    }

    private static void LegacyFixtures() {
        var empty = Read(Fixture("v1-empty.po2"));
        Check(empty.Objects.Count == 0 && empty.ContainerVersion == 1, "Legacy 80-byte empty snapshot");
        for (int schema = 2; schema <= 7; schema++) {
            byte[] bytes = Legacy(schema);
            var result = Read(bytes);
            Check(result.ContainerVersion == 1 && result.MetadataStatus == SnapshotMetadataStatus.Missing && result.Metadata == null,
                "Legacy metadata missing, schema " + schema);
            Check(result.SaveGuid == Save && result.SessionGuid == Session, "Legacy identities");
            Check(result.SkippedObjects.Count == 0 && result.Objects.Count == 1, "Legacy object count");
            var obj = result.Objects[0];
            Check(obj.Name == "Legacy fixture" && obj.Parts.Length == 2, "Legacy object and parts");
            Check(obj.Parts[0].Vertices.Length == 4 && obj.Parts[0].Triangles.Length == 6, "Legacy geometry");
            Check(obj.ReceivesDecals == (schema >= 4), "Decal schema compatibility");
            Check(obj.LocalPivot.x == (schema >= 5 ? 2.5f : 0), "Pivot schema compatibility");
            Check((obj.Parts[0].TexCoord1 != null) == (schema >= 7), "UV1 schema compatibility");
            Check(obj.Parts[0].SurfaceAreas[1].Material.Metallic == 0.73f, "Legacy material values");
            Check(obj.Parts[0].SurfaceAreas[1].HasOriginalState, "Legacy original appearance");
        }
        byte[] oldBytes = Legacy(7);
        byte[] schema8 = ObjectBlockWriter.WriteForSchema(Objects()[0], 8);
        Check(ObjectBlockReader.Read(schema8, 8).Parts[0].SurfaceAreas[0].MappingSeed == null, "Schema 8 reads without a mapping seed");
        Equal(Slice(oldBytes, FirstBlock(oldBytes), oldBytes.Length - FirstBlock(oldBytes)),
            ObjectBlockWriter.WriteForSchema(Objects()[0], 7), "Schema 7 object bytes unchanged from frozen baseline");
        int headerSize = SnapshotFormat.kObjectBlockHeaderSize;
        Equal(Slice(oldBytes, FirstBlock(oldBytes) + headerSize, oldBytes.Length - FirstBlock(oldBytes) - headerSize),
            Slice(schema8, headerSize, schema8.Length - headerSize),
            "Schema 8 retains the existing object body encoding");
    }

    private static void MetadataRoundTrips() {
        var values = new[] {
            Metadata,
            new SnapshotMetadata(null, null, null, null, SnapshotSaveKind.Unknown, null),
            new SnapshotMetadata("Autosave", "\ud83c\udfd9", "", DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
                SnapshotSaveKind.AutoSave, new SnapshotCityDate(9999, 365, 0, 0)),
            new SnapshotMetadata(new string('x', 16384), new string('\u96ea', 5461), "M", null,
                SnapshotSaveKind.Manual, new SnapshotCityDate(1, 0, 0, 0)),
        };
        foreach (var metadata in values) {
            byte[] bytes = Write(metadata);
            var read = Read(bytes);
            Check(read.ContainerVersion == 2 && read.MetadataStatus == SnapshotMetadataStatus.Available, "New container metadata");
            Check(metadata.Equals(read.Metadata), "All metadata fields round-trip exactly");
            Check(read.Objects.Count == 1 && read.SkippedObjects.Count == 0, "New container geometry valid");
            using (var stream = new MemoryStream(bytes)) {
                Check(SnapshotReader.TryReadHeader(stream, out var header, out _), "Header discovery succeeds");
                Check(metadata.Equals(header.Metadata), "Header discovery carries metadata");
            }
        }
        byte[] empty = SnapshotWriter.Write(Save, Session, Metadata, Array.Empty<PersistenceObjectData>());
        Check(empty.Length == DirectoryOffset(empty), "New empty snapshot has metadata but no object data");
        Check(Read(empty).Objects.Count == 0, "New empty snapshot reads");
        byte[] valid = Write();
        using (var stream = new PayloadGuardStream(valid, FirstBlock(valid))) {
            Check(SnapshotReader.TryReadHeader(stream, out _, out _), "Discovery never reads geometry");
        }
        using (var stream = new ShortReadStream(valid)) {
            Check(SnapshotReader.TryRead(stream, out var read, out _) && read.Metadata.Equals(Metadata), "Short stream reads handled");
        }
    }

    private static void CaptureAndDeferredValues() {
        var local = new DateTime(2026, 10, 10, 21, 30, 0, DateTimeKind.Local);
        var cityDate = new SnapshotCityDate(2038, 210, 23, 59);
        string city = "Before";
        var captured = SnapshotMetadata.Capture(city, "Save 1", "Map 1", local, true, cityDate, out string warning);
        city = "After";
        Check(captured.CityName == "Before" && captured.SaveName == "Save 1", "Captured values do not follow later game changes");
        Check(captured.SaveKind == SnapshotSaveKind.AutoSave && warning.Length == 0, "Capture autosave");
        Check(captured.SavedAtUtc.Value.Kind == DateTimeKind.Utc && captured.SavedAtUtc.Value == local.ToUniversalTime(), "Local time converted to UTC");
        var unspecified = SnapshotMetadata.Capture(city, "", "", DateTime.SpecifyKind(local, DateTimeKind.Unspecified), false,
            cityDate, out _);
        Check(unspecified.SavedAtUtc == captured.SavedAtUtc, "CS2 unspecified wall time is local");
        var bad = SnapshotMetadata.Capture(new string('x', 16385), "\ud800", null, DateTime.MinValue, false,
            new SnapshotCityDate(), out warning);
        Check(bad.CityName == "" && bad.SaveName == "" && bad.MapName == "", "Invalid labels omitted rather than blocking save");
        Check(!bad.SavedAtUtc.HasValue && !bad.CityDate.HasValue && warning.Length > 0, "Missing dates not fabricated");
        Check(Read(Write(bad)).Metadata.Equals(bad), "Captured incomplete metadata remains writable");
    }

    private static void MetadataFailures() {
        byte[] original = Write();
        var corruptions = new Action<byte[]>[] {
            b => b[80] ^= 1, // magic
            b => Set32(b, 88, uint.MaxValue), // payload length
            b => b[92] ^= 1, // checksum
            b => b[86] = 1, // flags
            b => { Set32(b, 96, 4); MetadataCrc(b); }, // presence flags
            b => { Set64(b, 100, ulong.MaxValue); MetadataCrc(b); }, // timestamp
            b => { b[108] = 99; MetadataCrc(b); }, // save kind
            b => { b[109] = 1; MetadataCrc(b); }, // reserved
            b => { Set32(b, 116, 366); MetadataCrc(b); }, // raw city day index
            b => { Set32(b, 120, 24); MetadataCrc(b); }, // city hour
            b => { Set32(b, 128, uint.MaxValue); MetadataCrc(b); }, // city name byte count
            b => { b[132] = 0xff; MetadataCrc(b); }, // UTF-8
            b => { Set32(b, 96, 0); MetadataCrc(b); }, // absent fields with nonzero values
        };
        foreach (var corrupt in corruptions) {
            byte[] bytes = (byte[])original.Clone();
            corrupt(bytes);
            var result = Read(bytes);
            Check(result.MetadataStatus == SnapshotMetadataStatus.Invalid && result.Metadata == null && result.MetadataWarning.Length > 0,
                "Metadata corruption isolated");
            Check(result.SaveGuid == Save && result.SessionGuid == Session && result.Objects.Count == 1 && result.SkippedObjects.Count == 0,
                "Metadata corruption leaves identities and geometry intact");
            VerifyMustFail(bytes, Metadata, "Corrupt metadata cannot pass publication");
        }
        byte[] unsupported = (byte[])original.Clone();
        unsupported[84] = 2;
        Check(Read(unsupported).MetadataStatus == SnapshotMetadataStatus.Unsupported, "Unknown metadata schema is skippable");
        VerifyMustFail(unsupported, Metadata, "Unsupported metadata cannot pass publication");
        Throws<InvalidDataException>(() => Write(new SnapshotMetadata(new string('x', 16385), "", "", null,
            SnapshotSaveKind.Manual, null)), "Oversized label rejected by writer");
        Throws<ArgumentException>(() => Write(new SnapshotMetadata("\ud800", "", "", null,
            SnapshotSaveKind.Manual, null)), "Invalid UTF-16 writer input");
        Throws<InvalidDataException>(() => Write(new SnapshotMetadata("", "", "", DateTime.Now,
            SnapshotSaveKind.Manual, null)), "Writer requires UTC");
        Throws<InvalidDataException>(() => Write(new SnapshotMetadata("", "", "", null,
            (SnapshotSaveKind)99, null)), "Writer rejects unknown save kind");
        Throws<InvalidDataException>(() => Write(new SnapshotMetadata("", "", "", null,
            SnapshotSaveKind.Manual, new SnapshotCityDate(0, 0, 0, 0))), "Writer rejects invalid city date");
        VerifyMustFail(original, new SnapshotMetadata("Changed", "", "", null, SnapshotSaveKind.Unknown, null),
            "Valid but wrong metadata fails verification");
        VerifyMustFail(Legacy(7), Metadata, "Legacy file cannot pass new-write verification");
    }

    private static void VerifyMustFail(byte[] bytes, SnapshotMetadata metadata, string message) {
        // Call the actual publication gate, not a parallel validation implementation.
        var method = typeof(SnapshotFileStore).GetMethod("ValidateSnapshotStream", BindingFlags.NonPublic | BindingFlags.Static);
        using (var stream = new MemoryStream(bytes)) {
            Throws<InvalidDataException>(() => method.Invoke(null, new object[] { stream, Save, Session, new[] { Objects()[0].PersistentId }, metadata, "test" }), message);
        }
    }

    private static void ContainerFailures() {
        byte[] original = Write();
        var corruptions = new Action<byte[]>[] {
            b => b[16] ^= 1, // identity without header CRC update
            b => { b[8] = 99; HeaderCrc(b); },
            b => { b[10] = 99; HeaderCrc(b); },
            b => { b[12] = 81; HeaderCrc(b); },
            b => { b[14] = 1; HeaderCrc(b); },
            b => { Set64(b, 56, ulong.MaxValue); HeaderCrc(b); },
            b => { Set64(b, 56, 80); HeaderCrc(b); },
            b => { Set64(b, 56, 80 + 16 + 65537); HeaderCrc(b); },
            b => { Set64(b, 64, ulong.MaxValue); HeaderCrc(b); },
            b => b[DirectoryOffset(b)] ^= 1,
            b => { Set64(b, DirectoryOffset(b) + 20, 80); DirectoryCrc(b); }, // block overlaps metadata
        };
        foreach (var corrupt in corruptions) {
            byte[] bytes = (byte[])original.Clone();
            corrupt(bytes);
            Check(!SnapshotReader.TryRead(bytes, out _, out _), "Core corruption rejects container");
        }
        foreach (int length in new[] { 0, 79, 80, 95, DirectoryOffset(original) - 1, FirstBlock(original) - 1, original.Length - 1 }) {
            Check(!SnapshotReader.TryRead(Slice(original, 0, length), out _, out _), "Truncation rejected: " + length);
        }
        byte[] badObject = (byte[])original.Clone();
        badObject[FirstBlock(badObject) + 20] ^= 1;
        var partial = Read(badObject);
        Check(partial.MetadataStatus == SnapshotMetadataStatus.Available && partial.SkippedObjects.Count == 1 && partial.Objects.Count == 0,
            "Object corruption retains existing skip behavior");
        byte[] legacyWrongOffset = Legacy(7);
        Set64(legacyWrongOffset, 56, 81);
        HeaderCrc(legacyWrongOffset);
        Check(!SnapshotReader.TryRead(legacyWrongOffset, out _, out _), "Legacy directory must remain at 80");
    }

    private static void FileStorage() {
        string folder = Path.Combine(Path.GetTempPath(), "po2-persistence-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            var store = new SnapshotFileStore(folder);
            string key = SaveIdentity.ToFileKey(Save);
            string final = Path.Combine(folder, key + ".po2");
            var objects = Objects();
            var write = store.WriteTransactional(key, Save, Session, Metadata, objects);
            Check(write.ObjectCount == 1 && write.Bytes == new FileInfo(final).Length, "Actual disk write");
            var load = store.ReadExact(key, Save, Session);
            Check(load.IsValid && load.Result.Metadata.Equals(Metadata), "Actual disk read");
            Check(!store.ReadExact(key, Guid.NewGuid(), Session).IsValid, "Exact identity mismatch rejected");
            Check(!store.ReadExact(key, Save, Guid.NewGuid()).IsValid, "Session mismatch rejected");
            byte[] previous = File.ReadAllBytes(final);
            Throws<ArgumentNullException>(() => store.WriteTransactional(key, Save, Session, null, objects), "Metadata encoding failure");
            Equal(File.ReadAllBytes(final), previous, "Encoding failure leaves previous final intact");
            var later = new SnapshotMetadata("Later", "Second save", "", null, SnapshotSaveKind.AutoSave, null);
            store.WriteTransactional(key, Save, Session, later, objects);
            Check(store.ReadExact(key, Save, Session).Result.Metadata.Equals(later), "Replacement publishes new metadata");
            Check(!File.Exists(final + ".tmp"), "No temp after successful publication");
            File.WriteAllBytes(final, previous);
            Directory.CreateDirectory(final + ".tmp");
            Throws<IOException>(() => store.WriteTransactional(key, Save, Session, later, objects), "Blocked temp write fails");
            Equal(File.ReadAllBytes(final), previous, "Temp failure leaves previous final intact");
            Directory.Delete(final + ".tmp");
            Guid secondGuid = new Guid("01112233-4455-6677-8899-aabbccddeeff");
            string second = SaveIdentity.ToFileKey(secondGuid);
            File.WriteAllBytes(Path.Combine(folder, second + ".po2"), WithIdentity(Legacy(2), secondGuid, Session));
            var candidates = store.DiscoverSaves().ForRecovery(key, Session);
            Check(candidates.Saves.Count == 1 && candidates.Saves[0].ObjectCount == 1, "Legacy discovery alongside current container: " + candidates.Saves.Count + " | " + string.Join("; ", candidates.RejectedFiles.Select(e => e.Message)));
            Guid thirdGuid = new Guid("02112233-4455-6677-8899-aabbccddeeff");
            string third = SaveIdentity.ToFileKey(thirdGuid);
            File.WriteAllBytes(Path.Combine(folder, third + ".po2"), WithIdentity(Write(), thirdGuid, Session));
            Check(store.DiscoverSaves().ForRecovery(key, Session).Saves.Count == 2, "Mixed container discovery");
            // The raw 128 bits must still be independent of Guid string formatting.
            var hash = new Colossal.Hash128(0x12345678, 0x90abcdef, 0x10203040, 0x50607080);
            byte[] expected = BitConverter.GetBytes(0x12345678u).Concat(BitConverter.GetBytes(0x90abcdefu))
                .Concat(BitConverter.GetBytes(0x10203040u)).Concat(BitConverter.GetBytes(0x50607080u)).ToArray();
            Equal(SaveIdentity.ToSnapshotGuid(hash).ToByteArray(), expected, "Save identity byte order unchanged");
        }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class PayloadGuardStream : MemoryStream {
        private readonly long m_Start;
        internal PayloadGuardStream(byte[] bytes, long start) : base(bytes, false) { m_Start = start; }
        public override int Read(byte[] buffer, int offset, int count) {
            if (Position + count > m_Start) throw new Exception("Discovery attempted a geometry read.");
            return base.Read(buffer, offset, count);
        }
    }
    private sealed class ShortReadStream : MemoryStream {
        internal ShortReadStream(byte[] bytes) : base(bytes, false) { }
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 3));
    }
}
