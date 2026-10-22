using System;
using System.Collections.Generic;
using System.IO;
using PO2.Persistence.Model;

namespace PO2.Persistence.Format
{
    // Reads container v1/v2 snapshots, with optional descriptive metadata.
    // Container corruption rejects the save; a damaged object block can be skipped.
    internal static class SnapshotReader
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

        internal static bool TryRead(Stream stream, out SnapshotReadResult result, out string error)
        {
            try
            {
                result = Read(stream);
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

        internal static bool TryReadHeader(Stream stream, out SnapshotHeaderInfo header, out string error)
        {
            try
            {
                // Recovery discovery stops after the checksummed header/directory.
                // Geometry is decoded only if the candidate is actually applied.
                SnapshotEnvelope envelope = ReadEnvelope(stream);
                header = envelope.Header;
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is EndOfStreamException || exception is OverflowException || exception is ArgumentException)
            {
                header = null;
                error = exception.Message;
                return false;
            }
        }

        internal static SnapshotReadResult Read(Stream stream)
        {
            SnapshotEnvelope envelope = ReadEnvelope(stream);
            SnapshotHeaderInfo header = envelope.Header;

            SnapshotDirectoryEntry[] entries = ParseDirectory(envelope.DirectoryBytes, header.ObjectCount, envelope.DirectoryEnd, stream.Length);
            ValidateNonOverlappingRanges(entries, envelope.DirectoryEnd);

            List<PersistenceObjectData> objects = new List<PersistenceObjectData>(entries.Length);
            List<SnapshotObjectError> skipped = new List<SnapshotObjectError>();
            long totalDecodedBytes = 0;

            // The envelope and directory are all-or-nothing.
            // Once their ranges are trusted a bad object block can be contained to that one PO.
            for (int i = 0; i < entries.Length; i++)
            {
                SnapshotDirectoryEntry entry = entries[i];
                if (!SnapshotFormat.IsSupportedObjectSchemaVersion(entry.ObjectSchemaVersion))
                {
                    skipped.Add(new SnapshotObjectError(entry.PersistentId, i, $"Unsupported object schema version {entry.ObjectSchemaVersion}; this build supports versions {SnapshotFormat.kLegacyObjectSchemaVersion} through {SnapshotFormat.kObjectSchemaVersion}."));
                    continue;
                }

                if (entry.Flags != 0 || entry.Reserved != 0)
                {
                    skipped.Add(new SnapshotObjectError(entry.PersistentId, i, "Object directory entry contains unknown flags/reserved data."));
                    continue;
                }

                if (entry.StoredLength <= 0 || entry.StoredLength > SnapshotFormat.kMaxStoredObjectBytes || entry.StoredLength > int.MaxValue)
                {
                    skipped.Add(new SnapshotObjectError(entry.PersistentId, i, $"Stored object length {entry.StoredLength} is outside the supported range."));
                    continue;
                }

                if (entry.DecodedLength < 0 || entry.DecodedLength > SnapshotFormat.kMaxDecodedObjectBytes)
                {
                    skipped.Add(new SnapshotObjectError(entry.PersistentId, i, $"Decoded object length {entry.DecodedLength} exceeds the safety ceiling."));
                    continue;
                }

                try
                {
                    stream.Position = entry.BlockOffset;
                    byte[] block = SnapshotBinary.ReadExactBytes(stream, checked((int)entry.StoredLength));
                    uint actualBlockChecksum = Crc32.Compute(block);
                    if (actualBlockChecksum != entry.BlockChecksum)
                    {
                        throw new InvalidDataException($"Directory block checksum mismatch: expected {entry.BlockChecksum:x8}, got {actualBlockChecksum:x8}.");
                    }

                    PersistenceObjectData decoded = ObjectBlockReader.Read(block, entry.ObjectSchemaVersion);
                    if (decoded.PersistentId != entry.PersistentId)
                    {
                        throw new InvalidDataException($"Object block persistent ID {decoded.PersistentId} does not match directory ID {entry.PersistentId}.");
                    }

                    // The directory's decoded size is checked against the object we actually got.
                    // It is not trusted as an allocation hint alone.
                    long decodedFootprint = SnapshotObjectMetrics.ComputeDecodedFootprint(decoded, entry.ObjectSchemaVersion);
                    if (decodedFootprint != entry.DecodedLength)
                    {
                        throw new InvalidDataException($"Decoded footprint {decodedFootprint} does not match directory value {entry.DecodedLength}.");
                    }

                    totalDecodedBytes = checked(totalDecodedBytes + decodedFootprint);
                    if (totalDecodedBytes > SnapshotFormat.kMaxDecodedSnapshotBytes)
                    {
                        throw new InvalidDataException("Decoded snapshot footprint exceeds safety ceiling.");
                    }

                    objects.Add(decoded);
                }
                catch (Exception exception) when (exception is InvalidDataException || exception is EndOfStreamException || exception is OverflowException || exception is ArgumentException)
                {
                    skipped.Add(new SnapshotObjectError(entry.PersistentId, i, exception.Message));
                }
            }

            return new SnapshotReadResult(header, objects, skipped);
        }

        private static SnapshotEnvelope ReadEnvelope(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek)
            {
                throw new ArgumentException("PO2 snapshot input stream must be readable and seekable.", nameof(stream));
            }

            if (stream.Length < SnapshotFormat.kSnapshotHeaderSize)
            {
                throw new InvalidDataException("PO2 snapshot is shorter than the fixed header.");
            }

            stream.Position = 0;
            // Check the header bytes before using any embedded count or offset.
            byte[] headerBytes = SnapshotBinary.ReadExactBytes(stream, SnapshotFormat.kSnapshotHeaderSize);
            ValidateHeaderChecksum(headerBytes);

            ushort containerVersion;
            Guid saveGuid;
            Guid sessionGuid;
            int objectCount;
            long directoryOffset;
            long directoryLength;
            uint expectedDirectoryChecksum;
            ParseHeader(headerBytes, out containerVersion, out saveGuid, out sessionGuid, out objectCount, out directoryOffset, out directoryLength, out expectedDirectoryChecksum);

            long directoryEnd = checked(directoryOffset + directoryLength);
            if (directoryOffset < SnapshotFormat.kSnapshotHeaderSize || directoryEnd > stream.Length)
            {
                throw new InvalidDataException("PO2 snapshot directory lies outside file bounds.");
            }

            if (directoryLength > int.MaxValue)
            {
                throw new InvalidDataException("PO2 snapshot directory is too large to decode.");
            }

            stream.Position = directoryOffset;
            byte[] directoryBytes = SnapshotBinary.ReadExactBytes(stream, checked((int)directoryLength));
            uint actualDirectoryChecksum = Crc32.Compute(directoryBytes);
            if (actualDirectoryChecksum != expectedDirectoryChecksum)
            {
                throw new InvalidDataException($"PO2 snapshot directory checksum mismatch: expected {expectedDirectoryChecksum:x8}, got {actualDirectoryChecksum:x8}.");
            }

            SnapshotMetadata metadata = null;
            SnapshotMetadataStatus metadataStatus = SnapshotMetadataStatus.Missing;
            string metadataWarning = string.Empty;
            if (containerVersion == SnapshotFormat.kContainerVersion)
            {
                // The checksummed header, not the metadata payload, sets the boundary.
                stream.Position = SnapshotFormat.kSnapshotHeaderSize;
                byte[] section = SnapshotBinary.ReadExactBytes(stream, checked((int)(directoryOffset - SnapshotFormat.kSnapshotHeaderSize)));
                metadataStatus = SnapshotMetadataCodec.Read(section, out metadata, out metadataWarning);
            }

            return new SnapshotEnvelope(new SnapshotHeaderInfo(saveGuid, sessionGuid, objectCount, directoryOffset, directoryLength, containerVersion, metadata, metadataStatus, metadataWarning, actualDirectoryChecksum), directoryBytes, directoryEnd);
        }

        private static void ValidateHeaderChecksum(byte[] headerBytes)
        {
            if (headerBytes.Length != SnapshotFormat.kSnapshotHeaderSize)
            {
                throw new InvalidDataException("Invalid PO2 snapshot header byte count.");
            }

            byte[] checksumSource = new byte[headerBytes.Length - 4];
            Buffer.BlockCopy(headerBytes, 0, checksumSource, 0, checksumSource.Length);
            uint expected = (uint)(headerBytes[76] | (headerBytes[77] << 8) | (headerBytes[78] << 16) | (headerBytes[79] << 24));
            uint actual = Crc32.Compute(checksumSource);
            if (actual != expected)
            {
                throw new InvalidDataException($"PO2 snapshot header checksum mismatch: expected {expected:x8}, got {actual:x8}.");
            }
        }

        private static void ParseHeader(byte[] headerBytes, out ushort containerVersion, out Guid saveGuid, out Guid sessionGuid, out int objectCount, out long directoryOffset, out long directoryLength, out uint directoryChecksum)
        {
            using (MemoryStream header = new MemoryStream(headerBytes, writable: false))
            {
                byte[] magic = SnapshotBinary.ReadExactBytes(header, SnapshotFormat.s_Magic.Length);
                for (int i = 0; i < SnapshotFormat.s_Magic.Length; i++)
                {
                    if (magic[i] != SnapshotFormat.s_Magic[i])
                    {
                        throw new InvalidDataException("Invalid PO2 snapshot magic.");
                    }
                }

                containerVersion = SnapshotBinary.ReadUInt16(header);
                if (containerVersion != SnapshotFormat.kLegacyContainerVersion && containerVersion != SnapshotFormat.kContainerVersion)
                {
                    throw new InvalidDataException($"Unsupported PO2 container version {containerVersion}; this build supports versions {SnapshotFormat.kLegacyContainerVersion} and {SnapshotFormat.kContainerVersion}.");
                }

                ushort snapshotSchemaVersion = SnapshotBinary.ReadUInt16(header);
                if (snapshotSchemaVersion != SnapshotFormat.kSnapshotSchemaVersion)
                {
                    throw new InvalidDataException($"Unsupported PO2 snapshot schema version {snapshotSchemaVersion}; this build supports version {SnapshotFormat.kSnapshotSchemaVersion}.");
                }

                ushort headerSize = SnapshotBinary.ReadUInt16(header);
                if (headerSize != SnapshotFormat.kSnapshotHeaderSize)
                {
                    throw new InvalidDataException($"Unsupported PO2 header size {headerSize}.");
                }

                ushort flags = SnapshotBinary.ReadUInt16(header);
                if (flags != 0)
                {
                    throw new InvalidDataException("PO2 snapshot contains unknown container flags.");
                }

                saveGuid = SnapshotBinary.ReadGuid(header);
                sessionGuid = SnapshotBinary.ReadGuid(header);
                uint objectCountRaw = SnapshotBinary.ReadUInt32(header);
                if (objectCountRaw > SnapshotFormat.kMaxObjectsPerSnapshot || objectCountRaw > int.MaxValue)
                {
                    throw new InvalidDataException($"PO2 snapshot object count {objectCountRaw} exceeds safety ceiling.");
                }

                objectCount = checked((int)objectCountRaw);

                ushort entrySize = SnapshotBinary.ReadUInt16(header);
                if (entrySize != SnapshotFormat.kDirectoryEntrySize)
                {
                    throw new InvalidDataException($"Unsupported PO2 directory entry size {entrySize}.");
                }

                ushort reserved = SnapshotBinary.ReadUInt16(header);
                if (reserved != 0)
                {
                    throw new InvalidDataException("PO2 snapshot header contains nonzero reserved data.");
                }

                ulong directoryOffsetRaw = SnapshotBinary.ReadUInt64(header);
                ulong directoryLengthRaw = SnapshotBinary.ReadUInt64(header);
                if (directoryOffsetRaw > long.MaxValue || directoryLengthRaw > long.MaxValue)
                {
                    throw new InvalidDataException("PO2 snapshot directory values exceed Int64 range.");
                }

                directoryOffset = checked((long)directoryOffsetRaw);
                directoryLength = checked((long)directoryLengthRaw);
                long expectedLength = checked((long)objectCount * SnapshotFormat.kDirectoryEntrySize);
                if (directoryLength != expectedLength)
                {
                    throw new InvalidDataException($"PO2 snapshot directory length {directoryLength} does not match object count {objectCount}.");
                }

                if (containerVersion == SnapshotFormat.kLegacyContainerVersion)
                {
                    if (directoryOffset != SnapshotFormat.kSnapshotHeaderSize)
                    {
                        throw new InvalidDataException("Legacy PO2 directory offset must be 80.");
                    }
                }
                else
                {
                    long metadataLength = directoryOffset - SnapshotFormat.kSnapshotHeaderSize;
                    if (metadataLength < SnapshotFormat.kMetadataPrefixSize || metadataLength > SnapshotFormat.kMetadataPrefixSize + SnapshotFormat.kMaxMetadataPayloadBytes)
                    {
                        throw new InvalidDataException("PO2 metadata boundary is outside the supported range.");
                    }
                }

                directoryChecksum = SnapshotBinary.ReadUInt32(header);
                SnapshotBinary.ReadUInt32(header); // Header checksum already validated.
                if (header.Position != header.Length)
                {
                    throw new InvalidDataException("PO2 snapshot header contains trailing bytes.");
                }
            }
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

        private sealed class SnapshotEnvelope
        {
            internal SnapshotEnvelope(SnapshotHeaderInfo header, byte[] directoryBytes, long directoryEnd)
            {
                Header = header;
                DirectoryBytes = directoryBytes;
                DirectoryEnd = directoryEnd;
            }

            internal SnapshotHeaderInfo Header { get; }
            internal byte[] DirectoryBytes { get; }
            internal long DirectoryEnd { get; }
        }
    }
}