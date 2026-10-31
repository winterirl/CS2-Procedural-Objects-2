using System;
using System.Collections.Generic;
using System.IO;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

namespace PO2.Persistence.PreparedTopology
{
    internal static class PreparedTopologyExtension
    {
        internal const int FooterSize = 64, EntrySize = 64;
        private static readonly byte[] s_Magic = { 80, 79, 50, 84, 79, 80, 0, 0 };
        internal sealed class Entry
        {
            internal Guid Object;
            internal int Part;
            internal ushort Version;
            internal uint Revision;
            internal long Offset, Length, DecodedLength;
            internal uint Checksum;
        }

        internal static void Write(Stream output, CitySaveCapture capture, int workers)
        {
            long requiredEnd = output.Position;
            var jobs = new List<KeyValuePair<int, int>>();
            for (int i = 0; i < capture.Parts.Length; i++)
                for (int j = 0; j < capture.Parts[i].Length; j++) jobs.Add(new KeyValuePair<int, int>(i, j));
            var entries = new List<Entry>(jobs.Count);
            OrderedWorkQueue.Run(jobs.Count, i => jobs[i], (i, job) =>
            {
                var part = capture.Parts[job.Key][job.Value];
                return PreparedTopologyCodec.PrepareForWrite(part);
            }, (i, encoded) =>
            {
                var job = jobs[i];
                if (encoded == null) return;
                entries.Add(new Entry { Object = capture.Objects[job.Key].PersistentId, Part = job.Value,
                    Version = encoded.Version, Revision = encoded.Revision,
                    Offset = output.Position, Length = encoded.Length, DecodedLength = encoded.DecodedLength, Checksum = encoded.Checksum() });
                encoded.WriteTo(output);
                capture.Parts[job.Key][job.Value].Candidate = encoded;
            }, null, null, workers);
            long directoryOffset = output.Position;
            byte[] directory = new byte[checked(entries.Count * EntrySize)];
            using (var stream = new MemoryStream(directory, true))
            {
                foreach (var e in entries)
                {
                    SnapshotBinary.WriteGuid(stream, e.Object);
                    SnapshotBinary.WriteUInt32(stream, (uint)e.Part);
                    SnapshotBinary.WriteUInt16(stream, e.Version);
                    SnapshotBinary.WriteUInt16(stream, 0);
                    SnapshotBinary.WriteUInt64(stream, (ulong)e.Offset);
                    SnapshotBinary.WriteUInt64(stream, (ulong)e.Length);
                    SnapshotBinary.WriteUInt64(stream, (ulong)e.DecodedLength);
                    SnapshotBinary.WriteUInt32(stream, e.Checksum);
                    SnapshotBinary.WriteUInt32(stream, 0);
                    SnapshotBinary.WriteUInt32(stream, e.Revision);
                    SnapshotBinary.WriteUInt32(stream, 0);
                }
            }
            output.Write(directory, 0, directory.Length);
            byte[] footer = new byte[FooterSize];
            using (var stream = new MemoryStream(footer, true))
            {
                stream.Write(s_Magic, 0, s_Magic.Length);
                SnapshotBinary.WriteUInt16(stream, 1);
                SnapshotBinary.WriteUInt16(stream, FooterSize);
                SnapshotBinary.WriteUInt16(stream, EntrySize);
                SnapshotBinary.WriteUInt16(stream, 0);
                SnapshotBinary.WriteUInt64(stream, (ulong)requiredEnd);
                SnapshotBinary.WriteUInt64(stream, (ulong)directoryOffset);
                SnapshotBinary.WriteUInt64(stream, (ulong)directory.Length);
                SnapshotBinary.WriteUInt32(stream, (uint)entries.Count);
                SnapshotBinary.WriteUInt32(stream, 0);
                SnapshotBinary.WriteUInt32(stream, Crc32.Compute(directory));
                SnapshotBinary.WriteUInt32(stream, 0);
                SnapshotBinary.WriteUInt32(stream, 0);
                SnapshotBinary.WriteUInt32(stream, Crc32.Compute(footer, 0, FooterSize - 4));
            }
            output.Write(footer, 0, footer.Length);
        }

        internal static Entry[] ReadIndex(Stream input, long requiredEnd, SnapshotDirectoryEntry[] required, bool strict)
        {
            try { return ReadIndexCore(input, requiredEnd, required); }
            catch (Exception ex) when (!strict && IsOptionalError(ex)) { return Array.Empty<Entry>(); }
        }

        private static Entry[] ReadIndexCore(Stream input, long requiredEnd, SnapshotDirectoryEntry[] required)
        {
            Require(input.Length - requiredEnd >= FooterSize);
            input.Position = input.Length - FooterSize;
            byte[] footer = SnapshotBinary.ReadExactBytes(input, FooterSize);
            for (int i = 0; i < 8; i++) Require(footer[i] == s_Magic[i]);
            long directoryOffset, directoryLength;
            int count;
            uint directoryChecksum;
            using (var stream = new MemoryStream(footer, false))
            {
                stream.Position = 8;
                Require(SnapshotBinary.ReadUInt16(stream) == 1 && SnapshotBinary.ReadUInt16(stream) == FooterSize && SnapshotBinary.ReadUInt16(stream) == EntrySize && SnapshotBinary.ReadUInt16(stream) == 0);
                Require(SnapshotBinary.ReadUInt64(stream) == (ulong)requiredEnd);
                directoryOffset = checked((long)SnapshotBinary.ReadUInt64(stream));
                directoryLength = checked((long)SnapshotBinary.ReadUInt64(stream));
                count = checked((int)SnapshotBinary.ReadUInt32(stream));
                Require(SnapshotBinary.ReadUInt32(stream) == 0);
                directoryChecksum = SnapshotBinary.ReadUInt32(stream);
                Require(SnapshotBinary.ReadUInt32(stream) == 0 && SnapshotBinary.ReadUInt32(stream) == 0);
                Require(SnapshotBinary.ReadUInt32(stream) == Crc32.Compute(footer, 0, FooterSize - 4));
            }
            Require(count <= (long)required.Length * PO2.Core.ProceduralObject.kMaxPartCount);
            Require(directoryLength == (long)count * EntrySize && directoryLength <= int.MaxValue);
            Require(directoryOffset >= requiredEnd && directoryOffset <= input.Length - FooterSize && directoryLength == input.Length - FooterSize - directoryOffset);
            input.Position = directoryOffset;
            byte[] bytes = SnapshotBinary.ReadExactBytes(input, (int)directoryLength);
            Require(Crc32.Compute(bytes) == directoryChecksum);
            var order = new Dictionary<Guid, int>(required.Length);
            for (int i = 0; i < required.Length; i++) order.Add(required[i].PersistentId, i);
            var entries = new Entry[count];
            using (var stream = new MemoryStream(bytes, false))
            {
                long next = requiredEnd;
                int previousObject = -1, previousPart = -1;
                for (int i = 0; i < count; i++)
                {
                    var e = new Entry { Object = SnapshotBinary.ReadGuid(stream), Part = checked((int)SnapshotBinary.ReadUInt32(stream)), Version = SnapshotBinary.ReadUInt16(stream) };
                    Require(SnapshotBinary.ReadUInt16(stream) == 0);
                    e.Offset = checked((long)SnapshotBinary.ReadUInt64(stream));
                    e.Length = checked((long)SnapshotBinary.ReadUInt64(stream));
                    e.DecodedLength = checked((long)SnapshotBinary.ReadUInt64(stream));
                    e.Checksum = SnapshotBinary.ReadUInt32(stream);
                    Require(SnapshotBinary.ReadUInt32(stream) == 0);
                    e.Revision = SnapshotBinary.ReadUInt32(stream);
                    Require(SnapshotBinary.ReadUInt32(stream) == 0);
                    Require(order.TryGetValue(e.Object, out int ordinal) && (ordinal > previousObject || ordinal == previousObject && e.Part > previousPart));
                    Require(e.Part < PO2.Core.ProceduralObject.kMaxPartCount && e.Offset == next && e.Length >= PreparedTopologyCodec.PrefixSize && e.Length <= directoryOffset - next);
                    previousObject = ordinal; previousPart = e.Part;
                    next = checked(next + e.Length);
                    entries[i] = e;
                }
                Require(next == directoryOffset);
            }
            return entries;
        }

        internal static EncodedPartTopology ReadPart(Stream input, Entry entry, PersistencePartData source, bool strict)
        {
            try
            {
                Require(PreparedTopologyLayout.Supported(entry.Version, entry.Revision));
                input.Position = entry.Offset;
                var encoded = PreparedTopologyCodec.Read(input, entry.Length, source, entry.Checksum);
                Require(encoded.Version == entry.Version && encoded.Revision == entry.Revision);
                Require(encoded.DecodedLength == entry.DecodedLength);
                return encoded;
            }
            catch (Exception ex) when (!strict && IsOptionalError(ex)) { return null; }
        }

        internal static bool IsOptionalError(Exception ex) => ex is InvalidDataException || ex is EndOfStreamException || ex is OverflowException || ex is ArgumentException;
        private static void Require(bool condition) => PreparedTopologyCodec.Require(condition, "Invalid prepared topology extension.");
    }
}
