using System;
using System.Collections.Generic;
using System.IO;
using PO2.Persistence.Model;
using PO2.Persistence.PreparedTopology;

namespace PO2.Persistence.Format
{
    // Reads container v1/v2/v3 snapshots, with optional descriptive metadata.
    // Container corruption rejects the save; a damaged object block can be skipped.
    internal static partial class SnapshotReader
    {
        internal static bool TryRead(byte[] bytes, out SnapshotReadResult result, out string error)
        {
            if (bytes == null)
            {
                result = null;
                error = "Snapshot bytes are null.";
                return false;
            }

            using (MemoryStream stream = new MemoryStream(bytes, writable: false))
            {
                return TryRead(stream, out result, out error);
            }
        }

        internal static bool TryRead(Stream stream, out SnapshotReadResult result, out string error, bool parallel = false, Guid? expectedSaveGuid = null, Guid? expectedSessionGuid = null)
        {
            try
            {
                result = Read(stream, parallel, expectedSaveGuid, expectedSessionGuid);
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is EndOfStreamException || exception is OverflowException || exception is ArgumentException)
            {
                result = null;
                error = exception.Message;
                return false;
            }
        }

        internal static SnapshotReadResult Read(Stream stream, bool parallel = false, Guid? expectedSaveGuid = null, Guid? expectedSessionGuid = null)
        {
            var snapshot = PrepareSnapshot(stream, expectedSaveGuid, expectedSessionGuid);
            var objects = new List<PersistenceObjectData>(snapshot.Entries.Length);
            var skipped = new List<SnapshotObjectError>();
            WalkObjects(stream, snapshot, (i, obj) => objects.Add(obj), error => skipped.Add(error), parallel);
            var prepared = ReadPreparedParts(stream, snapshot, objects);
            return new SnapshotReadResult(snapshot.Header, objects, skipped, prepared);
        }

        private static EncodedPartTopology[][] ReadPreparedParts(Stream stream, PreparedSnapshot snapshot, IReadOnlyList<PersistenceObjectData> objects)
        {
            if (snapshot.Header.ContainerVersion != SnapshotFormat.kPreparedContainerVersion) return null;
            var index = PreparedTopologyExtension.ReadIndex(stream, snapshot.RequiredEnd, snapshot.Entries, false);
            var lookup = new Dictionary<Guid, int>(objects.Count);
            var result = new EncodedPartTopology[objects.Count][];
            for (int i = 0; i < objects.Count; i++)
            {
                lookup.Add(objects[i].PersistentId, i);
                result[i] = new EncodedPartTopology[objects[i].Parts.Length];
            }
            foreach (var entry in index)
            {
                if (!lookup.TryGetValue(entry.Object, out int i) || entry.Part >= result[i].Length) continue;
                result[i][entry.Part] = PreparedTopologyExtension.ReadPart(stream, entry, objects[i].Parts[entry.Part], false);
            }
            return result;
        }

        private sealed class PreparedSnapshot
        {
            internal SnapshotHeaderInfo Header;
            internal SnapshotDirectoryEntry[] Entries;
            internal long RequiredEnd;
        }

        private sealed class StoredObjectInput
        {
            internal byte[] Bytes;
            internal string Error;
            internal long Declared;
        }

        private static PreparedSnapshot PrepareSnapshot(Stream stream, Guid? expectedSaveGuid, Guid? expectedSessionGuid)
        {
            SnapshotEnvelope envelope = ReadEnvelope(stream);
            SnapshotHeaderInfo header = envelope.Header;
            if (expectedSaveGuid.HasValue && header.SaveGuid != expectedSaveGuid.Value)
                throw new InvalidDataException("Embedded exact save GUID does not match the loaded vanilla save.");
            if (expectedSessionGuid.HasValue && header.SessionGuid != expectedSessionGuid.Value)
                throw new InvalidDataException("Embedded session GUID does not match the loaded vanilla session.");
            var entries = ParseDirectory(envelope.DirectoryBytes, header.ObjectCount, envelope.DirectoryEnd, stream.Length);
            ValidateNonOverlappingRanges(entries, envelope.DirectoryEnd);
            long requiredEnd = envelope.DirectoryEnd;
            foreach (var entry in entries) requiredEnd = Math.Max(requiredEnd, checked(entry.BlockOffset + entry.StoredLength));
            return new PreparedSnapshot { Header = header, Entries = entries, RequiredEnd = requiredEnd };
        }

        private static long WalkObjects(Stream stream, PreparedSnapshot snapshot,
            Action<int, PersistenceObjectData> accepted, Action<SnapshotObjectError> rejected, bool parallel, Action<int, PersistenceObjectData> verifyInputs = null)
        {
            var entries = snapshot.Entries;
            long totalDecodedBytes = 0, pendingDeclared = 0;
            int pendingCount = 0;
            OrderedWorkQueue.Run(entries.Length, i =>
            {
                var entry = entries[i];
                var input = new StoredObjectInput { Error = ValidateEntry(entry) };
                if (input.Error == null)
                {
                    try
                    {
                        stream.Position = entry.BlockOffset;
                        input.Bytes = SnapshotBinary.ReadExactBytes(stream, checked((int)entry.StoredLength));
                        input.Declared = entry.DecodedLength;
                    }
                    catch (Exception exception) when (IsObjectError(exception)) { input.Error = exception.Message; }
                }
                pendingDeclared = checked(pendingDeclared + input.Declared);
                pendingCount++;
                return input;
            }, (i, input) =>
            {
                var decoded = input.Error != null ? new DecodedObject { Error = input.Error } : DecodeObject(entries[i], input.Bytes);
                if (decoded.Error == null) verifyInputs?.Invoke(i, decoded.Object);
                return decoded;
            },
            (i, decoded) =>
            {
                var entry = entries[i];
                if (decoded.Error == null)
                {
                    totalDecodedBytes = checked(totalDecodedBytes + entry.DecodedLength);
                    if (totalDecodedBytes > SnapshotFormat.kMaxDecodedSnapshotBytes)
                        decoded.Error = "Decoded snapshot footprint exceeds safety ceiling.";
                }
                if (decoded.Error == null) accepted(i, decoded.Object);
                else rejected(new SnapshotObjectError(entry.PersistentId, i, decoded.Error));
            }, input =>
            {
                pendingDeclared -= input.Declared;
                pendingCount--;
                input.Bytes = null;
            }, decoded => decoded.Object = null,
            parallel ? LoadingBatch.WorkerCount : 1,
            i => pendingCount == 0 || ValidateEntry(entries[i]) != null ||
                entries[i].DecodedLength <= SnapshotFormat.kMaxDecodedSnapshotBytes - totalDecodedBytes - pendingDeclared);
            return totalDecodedBytes;
        }

        private sealed class DecodedObject
        {
            internal PersistenceObjectData Object;
            internal string Error;
        }

        private static bool IsObjectError(Exception exception) => exception is InvalidDataException ||
            exception is EndOfStreamException || exception is OverflowException || exception is ArgumentException;

        private static string ValidateEntry(SnapshotDirectoryEntry entry)
        {
            if (!SnapshotFormat.IsSupportedObjectSchemaVersion(entry.ObjectSchemaVersion))
                return $"Unsupported object schema version {entry.ObjectSchemaVersion}.";
            if (entry.Flags != 0 || entry.Reserved != 0) return "Object directory entry contains unknown flags/reserved data.";
            if (entry.StoredLength <= 0 || entry.StoredLength > SnapshotFormat.kMaxStoredObjectBytes || entry.StoredLength > int.MaxValue)
                return $"Stored object length {entry.StoredLength} is outside the supported range.";
            if (entry.DecodedLength < 0 || entry.DecodedLength > SnapshotFormat.kMaxDecodedObjectBytes)
                return $"Decoded object length {entry.DecodedLength} exceeds the safety ceiling.";
            return null;
        }

        private static DecodedObject DecodeObject(SnapshotDirectoryEntry entry, byte[] block)
        {
            try
            {
                uint actual = Crc32.Compute(block);
                if (actual != entry.BlockChecksum) throw new InvalidDataException($"Directory block checksum mismatch: expected {entry.BlockChecksum:x8}, got {actual:x8}.");
                var decoded = ObjectBlockReader.Read(block, entry.ObjectSchemaVersion);
                if (decoded.PersistentId != entry.PersistentId) throw new InvalidDataException("Object block persistent ID does not match directory ID.");
                long footprint = SnapshotObjectMetrics.ComputeDecodedFootprint(decoded, entry.ObjectSchemaVersion);
                if (footprint != entry.DecodedLength) throw new InvalidDataException($"Decoded footprint {footprint} does not match directory value {entry.DecodedLength}.");
                return new DecodedObject { Object = decoded };
            }
            catch (Exception exception) when (IsObjectError(exception)) { return new DecodedObject { Error = exception.Message }; }
        }

        private static SnapshotDirectoryEntry[] ParseDirectory(byte[] directoryBytes, int objectCount, long directoryEnd, long fileLength)
        {
            SnapshotDirectoryEntry[] entries = new SnapshotDirectoryEntry[objectCount];
            HashSet<Guid> ids = new HashSet<Guid>();

            using (MemoryStream directory = new MemoryStream(directoryBytes, writable: false))
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    Guid id = SnapshotBinary.ReadGuid(directory);
                    if (id == Guid.Empty)
                    {
                        throw new InvalidDataException($"Directory object {i} has an empty persistent ID.");
                    }

                    if (!ids.Add(id))
                    {
                        throw new InvalidDataException($"Directory contains duplicate persistent ID {id}.");
                    }

                    ushort objectSchemaVersion = SnapshotBinary.ReadUInt16(directory);
                    ushort flags = SnapshotBinary.ReadUInt16(directory);
                    ulong blockOffsetRaw = SnapshotBinary.ReadUInt64(directory);
                    ulong storedLengthRaw = SnapshotBinary.ReadUInt64(directory);
                    ulong decodedLengthRaw = SnapshotBinary.ReadUInt64(directory);
                    uint blockChecksum = SnapshotBinary.ReadUInt32(directory);
                    uint reserved = SnapshotBinary.ReadUInt32(directory);

                    if (blockOffsetRaw > long.MaxValue || storedLengthRaw > long.MaxValue || decodedLengthRaw > long.MaxValue)
                    {
                        throw new InvalidDataException($"Directory object {i} contains values outside Int64 range.");
                    }

                    long blockOffset = checked((long)blockOffsetRaw);
                    long storedLength = checked((long)storedLengthRaw);
                    long decodedLength = checked((long)decodedLengthRaw);
                    long blockEnd = checked(blockOffset + storedLength);
                    if (blockOffset < directoryEnd || blockEnd > fileLength)
                    {
                        throw new InvalidDataException($"Directory object {i} block range lies outside snapshot data bounds.");
                    }

                    entries[i] = new SnapshotDirectoryEntry
                    {
                        PersistentId = id,
                        ObjectSchemaVersion = objectSchemaVersion,
                        Flags = flags,
                        BlockOffset = blockOffset,
                        StoredLength = storedLength,
                        DecodedLength = decodedLength,
                        BlockChecksum = blockChecksum,
                        Reserved = reserved,
                    };
                }

                if (directory.Position != directory.Length)
                {
                    throw new InvalidDataException("PO2 snapshot directory contains trailing bytes.");
                }
            }

            return entries;
        }

        private static void ValidateNonOverlappingRanges(SnapshotDirectoryEntry[] entries, long directoryEnd)
        {
            // Directory order is object order, not necessarily file order.
            // Sort a copy so overlap checks do not change the order returned to runtime.
            SnapshotDirectoryEntry[] sorted = (SnapshotDirectoryEntry[])entries.Clone();
            Array.Sort(sorted, (left, right) => left.BlockOffset.CompareTo(right.BlockOffset));

            long previousEnd = directoryEnd;
            for (int i = 0; i < sorted.Length; i++)
            {
                SnapshotDirectoryEntry entry = sorted[i];
                if (entry.BlockOffset < previousEnd)
                {
                    throw new InvalidDataException("PO2 snapshot object block ranges overlap.");
                }

                previousEnd = checked(entry.BlockOffset + entry.StoredLength);
            }
        }

    }
}
