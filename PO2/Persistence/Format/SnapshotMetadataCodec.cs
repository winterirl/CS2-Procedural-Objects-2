using System;
using System.IO;
using PO2.Persistence.Model;

namespace PO2.Persistence.Format
{
    internal static class SnapshotMetadataCodec
    {
        private const uint kHasTimestamp = 1;
        private const uint kHasCityDate = 2;
        private const uint kMagic = 0x4d324f50; // PO2M, little-endian.

        internal static byte[] Write(SnapshotMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (metadata.SavedAtUtc.HasValue && metadata.SavedAtUtc.Value.Kind != DateTimeKind.Utc)
            {
                throw new InvalidDataException("PO2 metadata timestamp must be UTC.");
            }

            ValidateSaveKind(metadata.SaveKind);
            if (metadata.CityDate.HasValue && !metadata.CityDate.Value.IsValid)
            {
                throw new InvalidDataException("Invalid PO2 metadata city date.");
            }

            byte[] payload;
            using (var stream = new MemoryStream())
            {
                SnapshotBinary.WriteUInt32(stream, (metadata.SavedAtUtc.HasValue ? kHasTimestamp : 0) | (metadata.CityDate.HasValue ? kHasCityDate : 0));
                SnapshotBinary.WriteUInt64(stream, metadata.SavedAtUtc.HasValue ? checked((ulong)metadata.SavedAtUtc.Value.Ticks) : 0);
                stream.WriteByte((byte)metadata.SaveKind);
                stream.WriteByte(0);
                SnapshotBinary.WriteUInt16(stream, 0);
                SnapshotCityDate date = metadata.CityDate.GetValueOrDefault();
                WriteInt32(stream, date.Year);
                WriteInt32(stream, date.Month);
                WriteInt32(stream, date.Hour);
                WriteInt32(stream, date.Minute);
                WriteString(stream, metadata.CityName);
                WriteString(stream, metadata.SaveName);
                WriteString(stream, metadata.MapName);
                payload = stream.ToArray();
            }

            if (payload.Length > SnapshotFormat.kMaxMetadataPayloadBytes)
            {
                throw new InvalidDataException("PO2 metadata exceeds the payload limit.");
            }

            using (var stream = new MemoryStream())
            {
                SnapshotBinary.WriteUInt32(stream, kMagic);
                SnapshotBinary.WriteUInt16(stream, SnapshotFormat.kMetadataSchemaVersion);
                SnapshotBinary.WriteUInt16(stream, 0);
                SnapshotBinary.WriteUInt32(stream, checked((uint)payload.Length));
                SnapshotBinary.WriteUInt32(stream, Crc32.Compute(payload));
                stream.Write(payload, 0, payload.Length);
                return stream.ToArray();
            }
        }

        // The container has already bounded this section. Only descriptive data
        // errors are softened here; stream I/O and container errors stay outside.
        internal static SnapshotMetadataStatus Read(byte[] section, out SnapshotMetadata metadata, out string warning)
        {
            metadata = null;
            warning = string.Empty;
            try
            {
                if (section == null || section.Length < SnapshotFormat.kMetadataPrefixSize || section.Length > SnapshotFormat.kMetadataPrefixSize + SnapshotFormat.kMaxMetadataPayloadBytes)
                {
                    throw new InvalidDataException("Invalid PO2 metadata section length.");
                }

                using (var stream = new MemoryStream(section, false))
                {
                    if (SnapshotBinary.ReadUInt32(stream) != kMagic)
                    {
                        throw new InvalidDataException("Invalid PO2 metadata magic.");
                    }

                    ushort version = SnapshotBinary.ReadUInt16(stream);
                    ushort flags = SnapshotBinary.ReadUInt16(stream);
                    uint length = SnapshotBinary.ReadUInt32(stream);
                    uint checksum = SnapshotBinary.ReadUInt32(stream);
                    if (length != stream.Length - stream.Position)
                    {
                        throw new InvalidDataException("PO2 metadata length does not match the container boundary.");
                    }

                    byte[] payload = SnapshotBinary.ReadExactBytes(stream, checked((int)length));
                    if (Crc32.Compute(payload) != checksum)
                    {
                        throw new InvalidDataException("PO2 metadata checksum mismatch.");
                    }

                    if (version != SnapshotFormat.kMetadataSchemaVersion)
                    {
                        warning = $"Unsupported PO2 metadata schema {version}.";
                        return SnapshotMetadataStatus.Unsupported;
                    }

                    if (flags != 0) throw new InvalidDataException("Unknown PO2 metadata flags.");
                    metadata = ReadPayload(payload);
                    return SnapshotMetadataStatus.Available;
                }
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is EndOfStreamException || exception is OverflowException || exception is ArgumentException)
            {
                warning = exception.Message;
                return SnapshotMetadataStatus.Invalid;
            }
        }

        private static SnapshotMetadata ReadPayload(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes, false))
            {
                uint present = SnapshotBinary.ReadUInt32(stream);
                if ((present & ~(kHasTimestamp | kHasCityDate)) != 0)
                {
                    throw new InvalidDataException("Unknown PO2 metadata presence flags.");
                }

                ulong ticks = SnapshotBinary.ReadUInt64(stream);
                DateTime? savedAt = null;
                if ((present & kHasTimestamp) != 0)
                {
                    if (ticks > (ulong)DateTime.MaxValue.Ticks)
                    {
                        throw new InvalidDataException("Invalid PO2 metadata timestamp.");
                    }

                    savedAt = new DateTime((long)ticks, DateTimeKind.Utc);
                }
                else if (ticks != 0) throw new InvalidDataException("Absent timestamp has nonzero data.");

                byte[] kindAndReserved = SnapshotBinary.ReadExactBytes(stream, 4);
                var kind = (SnapshotSaveKind)kindAndReserved[0];
                ValidateSaveKind(kind);
                if (kindAndReserved[1] != 0 || kindAndReserved[2] != 0 || kindAndReserved[3] != 0)
                {
                    throw new InvalidDataException("Nonzero PO2 metadata reserved bytes.");
                }

                var rawDate = new SnapshotCityDate(ReadInt32(stream), ReadInt32(stream), ReadInt32(stream), ReadInt32(stream));
                SnapshotCityDate? cityDate = null;
                if ((present & kHasCityDate) != 0)
                {
                    if (!rawDate.IsValid) throw new InvalidDataException("Invalid PO2 metadata city date.");
                    cityDate = rawDate;
                }
                else if (!rawDate.Equals(default(SnapshotCityDate)))
                {
                    throw new InvalidDataException("Absent city date has nonzero data.");
                }

                string city = ReadString(stream);
                string save = ReadString(stream);
                string map = ReadString(stream);
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing PO2 metadata bytes.");
                return new SnapshotMetadata(city, save, map, savedAt, kind, cityDate);
            }
        }

        private static void ValidateSaveKind(SnapshotSaveKind kind)
        {
            if (kind != SnapshotSaveKind.Unknown && kind != SnapshotSaveKind.Manual && kind != SnapshotSaveKind.AutoSave) throw new InvalidDataException("Unknown PO2 save kind.");
        }

        private static void WriteString(Stream stream, string value)
        {
            int length = SnapshotMetadata.s_Utf8.GetByteCount(value);
            if (length > SnapshotFormat.kMaxMetadataStringBytes)
            {
                throw new InvalidDataException("PO2 metadata string exceeds the size limit.");
            }

            byte[] bytes = SnapshotMetadata.s_Utf8.GetBytes(value);
            SnapshotBinary.WriteUInt32(stream, checked((uint)bytes.Length));
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ReadString(Stream stream)
        {
            uint length = SnapshotBinary.ReadUInt32(stream);
            if (length > SnapshotFormat.kMaxMetadataStringBytes || length > stream.Length - stream.Position)
            {
                throw new InvalidDataException("Invalid PO2 metadata string length.");
            }

            return SnapshotMetadata.s_Utf8.GetString(SnapshotBinary.ReadExactBytes(stream, (int)length));
        }

        private static void WriteInt32(Stream stream, int value) =>
            SnapshotBinary.WriteUInt32(stream, unchecked((uint)value));

        private static int ReadInt32(Stream stream) =>
            unchecked((int)SnapshotBinary.ReadUInt32(stream));
    }
}