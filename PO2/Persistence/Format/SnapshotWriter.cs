using System;
using System.Collections.Generic;
using System.IO;
using PO2.Persistence.Model;

namespace PO2.Persistence.Format
{
    // Container v2 writer. Object block encoding is shared with legacy snapshots.
    // The writer receives save identity explicitly and has no GameManager or filesystem lifecycle logic.
    internal static class SnapshotWriter
    {
        internal static byte[] Write(Guid exactSaveGuid, Guid sessionGuid, SnapshotMetadata metadata, IReadOnlyList<PersistenceObjectData> objects, SnapshotEncodingOptions options = null, EncodingMetrics metrics = null)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Write(stream, exactSaveGuid, sessionGuid, metadata, objects, options, metrics);
                return stream.ToArray();
            }
        }

        internal static void Write(Stream stream, Guid exactSaveGuid, Guid sessionGuid, SnapshotMetadata metadata, IReadOnlyList<PersistenceObjectData> objects, SnapshotEncodingOptions options = null, EncodingMetrics metrics = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            if (!stream.CanWrite || !stream.CanSeek)
            {
                throw new ArgumentException("PO2 snapshot output stream must be writable and seekable.", nameof(stream));
            }

            SnapshotValidation.ValidateCount(objects.Count, SnapshotFormat.kMaxObjectsPerSnapshot, "Object");

            // Persistent IDs are also directory keys.
            // Duplicates would make it impossible to tell which object block a later reference meant.
            HashSet<Guid> ids = new HashSet<Guid>();
            for (int i = 0; i < objects.Count; i++)
            {
                PersistenceObjectData obj = objects[i];
                if (obj == null)
                {
                    throw new InvalidDataException($"Snapshot object {i} is null.");
                }

                if (obj.PersistentId == Guid.Empty)
                {
                    throw new InvalidDataException($"Snapshot object {i} has an empty persistent ID.");
                }

                if (!ids.Add(obj.PersistentId))
                {
                    throw new InvalidDataException($"Snapshot contains duplicate persistent ID {obj.PersistentId}.");
                }
            }

            byte[] metadataBytes = SnapshotMetadataCodec.Write(metadata);
            long directoryLength = checked((long)objects.Count * SnapshotFormat.kDirectoryEntrySize);
            long directoryOffset = checked(SnapshotFormat.kSnapshotHeaderSize + metadataBytes.Length);
            long blockStart = checked(directoryOffset + directoryLength);

            // Reserve header, metadata and directory space first.
            // Object blocks can then be written once while their final offsets are collected.
            stream.SetLength(0);
            stream.Position = 0;
            WriteZeros(stream, blockStart);
            stream.Position = blockStart;

            SnapshotDirectoryEntry[] entries = new SnapshotDirectoryEntry[objects.Count];
            long decodedSnapshotBytes = 0;

            for (int i = 0; i < objects.Count; i++)
            {
                PersistenceObjectData obj = objects[i];
                byte[] block = ObjectBlockWriter.Write(obj, options, metrics);
                long decodedLength = SnapshotObjectMetrics.ComputeDecodedFootprint(obj, SnapshotFormat.kObjectSchemaVersion);
                decodedSnapshotBytes = checked(decodedSnapshotBytes + decodedLength);
                if (decodedSnapshotBytes > SnapshotFormat.kMaxDecodedSnapshotBytes)
                {
                    throw new InvalidDataException("Decoded snapshot footprint exceeds safety ceiling.");
                }

                long offset = stream.Position;
                stream.Write(block, 0, block.Length);
                entries[i] = new SnapshotDirectoryEntry
                {
                    PersistentId = obj.PersistentId,
                    ObjectSchemaVersion = SnapshotFormat.kObjectSchemaVersion,
                    Flags = 0,
                    BlockOffset = offset,
                    StoredLength = block.LongLength,
                    DecodedLength = decodedLength,
                    BlockChecksum = Crc32.Compute(block),
                    Reserved = 0,
                };
            }

            // The block offsets are known now, so fill the reserved front matter
            // without moving or rewriting the object data.
            long finalLength = stream.Position;
            byte[] directory = BuildDirectory(entries);
            uint directoryChecksum = Crc32.Compute(directory);
            byte[] header = BuildHeader(exactSaveGuid, sessionGuid, objects.Count, directoryOffset, directoryLength, directoryChecksum);

            stream.Position = 0;
            stream.Write(header, 0, header.Length);
            stream.Write(metadataBytes, 0, metadataBytes.Length);
            stream.Write(directory, 0, directory.Length);
            stream.SetLength(finalLength);
            stream.Position = finalLength;
        }

        private static byte[] BuildDirectory(SnapshotDirectoryEntry[] entries)
        {
            using (MemoryStream directory = new MemoryStream(checked(entries.Length * SnapshotFormat.kDirectoryEntrySize)))
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    SnapshotDirectoryEntry entry = entries[i];
                    SnapshotBinary.WriteGuid(directory, entry.PersistentId);
                    SnapshotBinary.WriteUInt16(directory, entry.ObjectSchemaVersion);
                    SnapshotBinary.WriteUInt16(directory, entry.Flags);
                    SnapshotBinary.WriteUInt64(directory, checked((ulong)entry.BlockOffset));
                    SnapshotBinary.WriteUInt64(directory, checked((ulong)entry.StoredLength));
                    SnapshotBinary.WriteUInt64(directory, checked((ulong)entry.DecodedLength));
                    SnapshotBinary.WriteUInt32(directory, entry.BlockChecksum);
                    SnapshotBinary.WriteUInt32(directory, entry.Reserved);
                }

                byte[] bytes = directory.ToArray();
                int expected = checked(entries.Length * SnapshotFormat.kDirectoryEntrySize);
                if (bytes.Length != expected)
                {
                    throw new InvalidDataException("PO2 snapshot directory size mismatch while writing.");
                }

                return bytes;
            }
        }

        private static byte[] BuildHeader(Guid exactSaveGuid, Guid sessionGuid, int objectCount, long directoryOffset, long directoryLength, uint directoryChecksum)
        {
            using (MemoryStream header = new MemoryStream(SnapshotFormat.kSnapshotHeaderSize))
            {
                header.Write(SnapshotFormat.s_Magic, 0, SnapshotFormat.s_Magic.Length);
                SnapshotBinary.WriteUInt16(header, SnapshotFormat.kContainerVersion);
                SnapshotBinary.WriteUInt16(header, SnapshotFormat.kSnapshotSchemaVersion);
                SnapshotBinary.WriteUInt16(header, SnapshotFormat.kSnapshotHeaderSize);
                SnapshotBinary.WriteUInt16(header, 0);
                SnapshotBinary.WriteGuid(header, exactSaveGuid);
                SnapshotBinary.WriteGuid(header, sessionGuid);
                SnapshotBinary.WriteUInt32(header, checked((uint)objectCount));
                SnapshotBinary.WriteUInt16(header, SnapshotFormat.kDirectoryEntrySize);
                SnapshotBinary.WriteUInt16(header, 0);
                SnapshotBinary.WriteUInt64(header, checked((ulong)directoryOffset));
                SnapshotBinary.WriteUInt64(header, checked((ulong)directoryLength));
                SnapshotBinary.WriteUInt32(header, directoryChecksum);

                byte[] withoutChecksum = header.ToArray();
                if (withoutChecksum.Length != SnapshotFormat.kSnapshotHeaderSize - 4)
                {
                    throw new InvalidDataException("PO2 snapshot header pre-checksum size mismatch.");
                }

                uint headerChecksum = Crc32.Compute(withoutChecksum);
                SnapshotBinary.WriteUInt32(header, headerChecksum);

                byte[] bytes = header.ToArray();
                if (bytes.Length != SnapshotFormat.kSnapshotHeaderSize)
                {
                    throw new InvalidDataException("PO2 snapshot header size mismatch while writing.");
                }

                return bytes;
            }
        }

        private static void WriteZeros(Stream stream, long count)
        {
            byte[] zeros = new byte[4096];
            long remaining = count;
            while (remaining > 0)
            {
                int write = (int)Math.Min(remaining, zeros.Length);
                stream.Write(zeros, 0, write);
                remaining -= write;
            }
        }
    }
}