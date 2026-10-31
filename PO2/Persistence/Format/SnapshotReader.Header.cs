using System;
using System.IO;
using PO2.Persistence.Model;

namespace PO2.Persistence.Format
{
    internal static partial class SnapshotReader
    {
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
            if (SnapshotFormat.HasMetadata(containerVersion))
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
                if (!SnapshotFormat.IsSupportedContainerVersion(containerVersion))
                {
                    throw new InvalidDataException($"Unsupported PO2 container version {containerVersion}; this build supports versions {SnapshotFormat.kLegacyContainerVersion} through {SnapshotFormat.kPreparedContainerVersion}.");
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
