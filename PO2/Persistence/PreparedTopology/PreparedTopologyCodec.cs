using System;
using System.IO;
using System.IO.Compression;
using PO2.Geometry.Topology;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

namespace PO2.Persistence.PreparedTopology
{
    internal static class PreparedTopologyCodec
    {
        internal const int SectionCount = 22, HeaderSize = 48, DescriptorSize = 32;
        // Codec 2 shuffles each 4096-int block into four byte planes before Deflate.
        // The block size is part of the wire contract, independent of stream read sizes.
        internal const int PackingBlockBytes = 16384;
        internal const int PrefixSize = HeaderSize + SectionCount * DescriptorSize;
        internal const ushort Version = PreparedTopologyLayout.CompactVersion;
        internal const uint Revision = PreparedTopologyLayout.Revision;
        private static readonly byte[] s_Magic = { 80, 79, 50, 80, 65, 82, 84, 0 };

        internal static EncodedPartTopology EncodeOptional(PartTopologyCapture capture)
        {
            if (capture.FaceTriangles == null || capture.TriangleFaces == null ||
                capture.FaceTriangles.Length != capture.Structural.FaceCount) return null;
            try { return Encode(capture); }
            // An optional CLR array limit must not become a new geometry-save limit.
            catch (OverflowException) { return null; }
        }

        internal static EncodedPartTopology PrepareForWrite(PartTopologyCapture capture)
        {
            var candidate = capture.Candidate;
            if (candidate == null || !PreparedTopologyLayout.Supported(candidate.Version, candidate.Revision))
                return EncodeOptional(capture);
            if (candidate.Version == Version) return candidate;

            // These sections already passed validation when the old save loaded.
            // Keep their compressed bytes; only the omitted tables and framing change.
            var sections = new EncodedTopologySection[SectionCount];
            for (int i = 0; i < SectionCount; i++)
            {
                var old = candidate.Sections[i];
                sections[i] = PreparedTopologyLayout.IsDerived(Version, i + 1)
                    ? new EncodedTopologySection(old.Count, PreparedTopologyLayout.Derived, Array.Empty<byte>()) : old;
            }
            return BuildRecord(capture, sections);
        }

        internal static EncodedPartTopology Encode(PartTopologyCapture capture)
        {
            var sections = new EncodedTopologySection[SectionCount];
            var buffer = new byte[PackingBlockBytes];
            var shuffled = new byte[buffer.Length];
            for (int id = 1; id <= SectionCount; id++)
            {
                int count = Count(capture, id);
                int bytes = checked(count * (id == 17 ? 1 : 4));
                if (PreparedTopologyLayout.IsDerived(Version, id))
                {
                    sections[id - 1] = new EncodedTopologySection(count, PreparedTopologyLayout.Derived, Array.Empty<byte>());
                    continue;
                }
                byte[] stored;
                byte codec = 0;
                if (bytes > 256)
                {
                    using (var output = new MemoryStream())
                    {
                        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                            WriteStoredSection(deflate, capture, id, buffer, shuffled);
                        stored = output.ToArray();
                    }
                    if (stored.Length < bytes) codec = 2;
                    else stored = Raw(capture, id, bytes, buffer);
                }
                else stored = Raw(capture, id, bytes, buffer);
                sections[id - 1] = new EncodedTopologySection(count, codec, stored);
            }
            return BuildRecord(capture, sections);
        }

        private static EncodedPartTopology BuildRecord(PartTopologyCapture capture, EncodedTopologySection[] sections)
        {
            byte[] prefix = new byte[PrefixSize];
            using (var stream = new MemoryStream(prefix, true))
            {
                stream.Write(s_Magic, 0, 8);
                SnapshotBinary.WriteUInt16(stream, Version);
                SnapshotBinary.WriteUInt16(stream, 0);
                SnapshotBinary.WriteUInt32(stream, Revision);
                SnapshotBinary.WriteUInt32(stream, (uint)capture.Logical.RenderVertexCount);
                SnapshotBinary.WriteUInt32(stream, (uint)capture.Logical.LogicalVertexCount);
                SnapshotBinary.WriteUInt32(stream, (uint)capture.TriangleFaces.Length);
                SnapshotBinary.WriteUInt32(stream, (uint)capture.Structural.FaceCount);
                SnapshotBinary.WriteUInt32(stream, (uint)capture.Logical.LogicalEdgeCount);
                SnapshotBinary.WriteUInt32(stream, (uint)capture.Structural.StructuralEdgeCount);
                SnapshotBinary.WriteUInt16(stream, SectionCount);
                SnapshotBinary.WriteUInt16(stream, DescriptorSize);
                SnapshotBinary.WriteUInt32(stream, 0);
                long offset = PrefixSize;
                for (int i = 0; i < SectionCount; i++)
                {
                    var section = sections[i];
                    SnapshotBinary.WriteUInt16(stream, (ushort)(i + 1));
                    stream.WriteByte(section.Codec);
                    stream.WriteByte(0);
                    SnapshotBinary.WriteUInt32(stream, (uint)section.Count);
                    SnapshotBinary.WriteUInt64(stream, (ulong)offset);
                    SnapshotBinary.WriteUInt32(stream, (uint)section.Bytes.Length);
                    SnapshotBinary.WriteUInt32(stream, checked((uint)section.Count * (i == 16 ? 1u : 4u)));
                    SnapshotBinary.WriteUInt32(stream, Crc32.Compute(section.Bytes));
                    SnapshotBinary.WriteUInt32(stream, 0);
                    offset = checked(offset + section.Bytes.Length);
                }
                stream.Position = 44;
                SnapshotBinary.WriteUInt32(stream, Crc32.Compute(prefix, HeaderSize, PrefixSize - HeaderSize));
            }
            return new EncodedPartTopology(prefix, sections);
        }

        private static byte[] Raw(PartTopologyCapture capture, int id, int length, byte[] buffer)
        {
            if (length == 0) return Array.Empty<byte>();
            var bytes = new byte[length];
            using (var stream = new MemoryStream(bytes, true)) WriteStoredSection(stream, capture, id, buffer);
            return bytes;
        }

        private static int Count(PartTopologyCapture c, int id)
        {
            int l = c.Logical.LogicalVertexCount, e = c.Logical.LogicalEdgeCount, f = c.Structural.FaceCount, s = c.Structural.StructuralEdgeCount;
            switch (id)
            {
                case 1: case 3: case 5: return checked(l + 1);
                case 2: return c.Logical.RenderVertexCount;
                case 4: return Sum(l, i => c.Logical.GetVertex(i).TriangleIndices);
                case 6: return checked(2 * e);
                case 7: case 8: return e;
                case 9: return checked(e + 1);
                case 10: return Sum(e, i => c.Logical.GetEdge(i).TriangleIndices);
                case 11: case 12: return s;
                case 13: return f == 0 ? 0 : checked(s + 1);
                case 14: return Sum(s, i => c.Structural.GetEdge(i).AdjacentFaceIds);
                case 15: return c.Structural.PersistenceBoundaryOffsets.Length;
                case 16: return c.Structural.PersistenceBoundaryEdges.Length;
                case 17: return c.Structural.PersistenceBoundaryForward.Length;
                case 18: return c.Structural.PersistenceIncidentOffsets.Length;
                case 19: return c.Structural.PersistenceIncidentEdges.Length;
                case 20: case 21: return c.FaceTriangles.Length;
                case 22: return c.TriangleFaces.Length;
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        private static int Sum(int count, Func<int, IndexSpan> get)
        {
            int total = 0;
            for (int i = 0; i < count; i++) total = checked(total + get(i).Count);
            return total;
        }

        private static void WriteStoredSection(Stream stream, PartTopologyCapture c, int id, byte[] buffer, byte[] shuffled = null)
        {
            var writer = new PackedWriter(stream, buffer, shuffled);
            int e = c.Logical.LogicalEdgeCount, f = c.Structural.FaceCount;
            switch (id)
            {
                case 7: for (int i = 0; i < e; i++) writer.Int(c.Logical.GetEdge(i).VertexA); break;
                case 8: for (int i = 0; i < e; i++) writer.Int(c.Logical.GetEdge(i).VertexB); break;
                case 9: writer.Spans(e, i => c.Logical.GetEdge(i).TriangleIndices, true); break;
                case 10: writer.Spans(e, i => c.Logical.GetEdge(i).TriangleIndices, false); break;
                case 16: writer.Array(c.Structural.PersistenceBoundaryEdges); break;
                case 20: for (int i = 0; i < f; i++) writer.Int(c.FaceTriangles[i].TriangleA); break;
                case 21: for (int i = 0; i < f; i++) writer.Int(c.FaceTriangles[i].TriangleB); break;
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
            writer.Flush();
        }

        private sealed class PackedWriter
        {
            private readonly Stream m_Stream;
            private readonly byte[] m_Buffer, m_Shuffled;
            private int m_Count;
            internal PackedWriter(Stream stream, byte[] buffer, byte[] shuffled) { m_Stream = stream; m_Buffer = buffer; m_Shuffled = shuffled; }
            internal void Int(int value)
            {
                if (m_Count == m_Buffer.Length) Flush();
                m_Buffer[m_Count++] = (byte)value;
                m_Buffer[m_Count++] = (byte)(value >> 8);
                m_Buffer[m_Count++] = (byte)(value >> 16);
                m_Buffer[m_Count++] = (byte)(value >> 24);
            }
            internal void Array(int[] values) { foreach (int value in values) Int(value); }
            internal void Spans(int count, Func<int, IndexSpan> get, bool offsets)
            {
                int offset = 0;
                if (offsets) Int(0);
                for (int i = 0; i < count; i++)
                {
                    var span = get(i);
                    if (offsets) { offset = checked(offset + span.Count); Int(offset); }
                    else foreach (int value in span) Int(value);
                }
            }
            internal void Flush()
            {
                if (m_Shuffled == null) m_Stream.Write(m_Buffer, 0, m_Count);
                else
                {
                    int count = m_Count / 4;
                    for (int plane = 0; plane < 4; plane++)
                        for (int i = 0; i < count; i++) m_Shuffled[plane * count + i] = m_Buffer[4 * i + plane];
                    m_Stream.Write(m_Shuffled, 0, m_Count);
                }
                m_Count = 0;
            }
        }

        internal static EncodedPartTopology Read(Stream input, long length, PersistencePartData source, uint? expectedChecksum = null)
        {
            Require(length >= PrefixSize, "Prepared topology is truncated.");
            byte[] prefix = SnapshotBinary.ReadExactBytes(input, PrefixSize);
            var counts = ReadCounts(prefix, source);
            ushort version = (ushort)(prefix[8] | prefix[9] << 8);
            var sections = new EncodedTopologySection[SectionCount];
            using (var directory = new MemoryStream(prefix, false))
            {
                directory.Position = HeaderSize;
                long offset = PrefixSize;
                for (int i = 0; i < SectionCount; i++)
                {
                    Require(SnapshotBinary.ReadUInt16(directory) == i + 1, "Prepared section order changed.");
                    int codec = directory.ReadByte(), flags = directory.ReadByte();
                    uint count = SnapshotBinary.ReadUInt32(directory);
                    ulong start = SnapshotBinary.ReadUInt64(directory);
                    uint stored = SnapshotBinary.ReadUInt32(directory), decoded = SnapshotBinary.ReadUInt32(directory);
                    uint checksum = SnapshotBinary.ReadUInt32(directory); // Payload checked on its worker.
                    uint reserved = SnapshotBinary.ReadUInt32(directory);
                    Require(PreparedTopologyLayout.ValidCodec(version, i + 1, codec) && flags == 0 && reserved == 0, "Unsupported prepared section.");
                    Require(codec != PreparedTopologyLayout.Derived || (stored == 0 && checksum == 0), "Derived section has stored data.");
                    Require(count <= int.MaxValue && stored <= int.MaxValue && decoded <= int.MaxValue, "Prepared section cannot fit an array.");
                    ValidateSectionCount(i + 1, count, counts, source);
                    Require(decoded == count * (i == 16 ? 1L : 4L), "Prepared section byte count mismatch.");
                    Require(start == (ulong)offset && stored <= length - offset, "Prepared section lies outside its record.");
                    Require(stored <= decoded, "Prepared stored length exceeds its source-derived bound.");
                    Require(codec != 0 || stored == decoded, "Raw prepared length mismatch.");
                    offset = checked(offset + stored);
                    byte[] bytes = SnapshotBinary.ReadExactBytes(input, (int)stored);
                    sections[i] = new EncodedTopologySection((int)count, (byte)codec, bytes);
                }
                Require(offset == length, "Prepared record has trailing bytes.");
            }
            return new EncodedPartTopology(prefix, sections, expectedChecksum);
        }

        internal static PackedTopologyData Decode(EncodedPartTopology encoded, PersistencePartData source)
        {
            var data = DecodeStored(encoded, source);
            if (encoded.Version == PreparedTopologyLayout.CompactVersion)
                PreparedTopologyExpansion.ExpandAndValidate(data, source, encoded.Sections);
            return data;
        }

        internal static PackedTopologyData DecodeStored(EncodedPartTopology encoded, PersistencePartData source)
        {
            var data = ReadCounts(encoded.Header, source);
            Require(encoded.Sections.Length == SectionCount, "Prepared section count mismatch.");
            // File I/O stays ordered on the caller. Bulk checksum work belongs
            // to the same detached worker that validates and installs this part.
            using (var descriptors = new MemoryStream(encoded.Header, false))
            {
                descriptors.Position = HeaderSize;
                long offset = PrefixSize;
                for (int i = 0; i < SectionCount; i++)
                {
                    var section = encoded.Sections[i];
                    Require(SnapshotBinary.ReadUInt16(descriptors) == i + 1 && descriptors.ReadByte() == section.Codec && descriptors.ReadByte() == 0, "Prepared descriptor identity mismatch.");
                    Require(SnapshotBinary.ReadUInt32(descriptors) == section.Count && SnapshotBinary.ReadUInt64(descriptors) == (ulong)offset, "Prepared descriptor count/offset mismatch.");
                    Require(SnapshotBinary.ReadUInt32(descriptors) == section.Bytes.Length && SnapshotBinary.ReadUInt32(descriptors) == (long)section.Count * (i == 16 ? 1 : 4), "Prepared descriptor length mismatch.");
                    Require(SnapshotBinary.ReadUInt32(descriptors) == Crc32.Compute(section.Bytes) && SnapshotBinary.ReadUInt32(descriptors) == 0, "Prepared section checksum mismatch.");
                    offset = checked(offset + section.Bytes.Length);
                }
                Require(offset == encoded.Length, "Prepared record length mismatch.");
            }
            if (encoded.ExpectedChecksum.HasValue) Require(encoded.Checksum() == encoded.ExpectedChecksum.Value, "Prepared payload checksum mismatch.");
            var buffer = new byte[PackingBlockBytes];
            for (int i = 0; i < SectionCount; i++)
            {
                var section = encoded.Sections[i];
                ValidateSectionCount(i + 1, section.Count, data, source);
                Require(PreparedTopologyLayout.ValidCodec(encoded.Version, i + 1, section.Codec), "Unsupported prepared section codec.");
                if (section.Codec == PreparedTopologyLayout.Derived)
                {
                    Require(section.Bytes.Length == 0, "Derived section has stored data.");
                    continue;
                }
                using (var raw = new MemoryStream(section.Bytes, false))
                using (Stream decoded = section.Codec == 0 ? (Stream)raw : new DeflateStream(raw, CompressionMode.Decompress, true))
                {
                    int width = i == 16 ? 1 : 4;
                    if (width == 1) data.Forward = new byte[section.Count];
                    else data[i + 1] = new int[section.Count];
                    int at = 0;
                    while (at < section.Count)
                    {
                        int count = Math.Min(buffer.Length / width, section.Count - at), bytes = count * width, got = 0;
                        while (got < bytes)
                        {
                            int read = decoded.Read(buffer, got, bytes - got);
                            Require(read > 0, "Prepared section ended early.");
                            got += read;
                        }
                        if (width == 1) Buffer.BlockCopy(buffer, 0, data.Forward, at, count);
                        else for (int j = 0; j < count; j++)
                        {
                            int p = j * 4;
                            data[i + 1][at + j] = section.Codec == 2
                                ? buffer[j] | buffer[count + j] << 8 | buffer[2 * count + j] << 16 | buffer[3 * count + j] << 24
                                : buffer[p] | buffer[p + 1] << 8 | buffer[p + 2] << 16 | buffer[p + 3] << 24;
                        }
                        at += count;
                    }
                    Require(decoded.ReadByte() == -1, "Prepared section expands beyond its declared count.");
                }
            }
            return data;
        }

        private static PackedTopologyData ReadCounts(byte[] prefix, PersistencePartData source)
        {
            Require(prefix.Length == PrefixSize && source.StructuralTopologySource.HasValue, "Prepared topology needs explicit source data.");
            Require((byte)source.StructuralTopologySource.Value <= (byte)StructuralTopologySource.ConvertedInferred, "Invalid topology provenance.");
            for (int i = 0; i < 8; i++) Require(prefix[i] == s_Magic[i], "Invalid prepared magic.");
            using (var stream = new MemoryStream(prefix, false))
            {
                stream.Position = 8;
                ushort version = SnapshotBinary.ReadUInt16(stream);
                Require(SnapshotBinary.ReadUInt16(stream) == 0 && PreparedTopologyLayout.Supported(version, SnapshotBinary.ReadUInt32(stream)), "Unsupported prepared topology revision.");
                var d = new PackedTopologyData
                {
                    RenderCount = checked((int)SnapshotBinary.ReadUInt32(stream)), LogicalCount = checked((int)SnapshotBinary.ReadUInt32(stream)),
                    TriangleCount = checked((int)SnapshotBinary.ReadUInt32(stream)), FaceCount = checked((int)SnapshotBinary.ReadUInt32(stream)),
                    EdgeCount = checked((int)SnapshotBinary.ReadUInt32(stream)), StructuralEdgeCount = checked((int)SnapshotBinary.ReadUInt32(stream))
                };
                Require(d.RenderCount == source.Vertices.Length && d.LogicalCount == source.LogicalVertexCount && d.TriangleCount == source.Triangles.Length / 3 && d.FaceCount == source.StructuralFaces.Length, "Prepared topology belongs to different geometry.");
                Require(d.LogicalCount <= d.RenderCount, "Prepared logical groups exceed their render members.");
                Require(d.EdgeCount <= 3L * d.TriangleCount && d.StructuralEdgeCount <= (d.BoundaryCount = BoundaryCount(source)), "Prepared edge count exceeds geometry.");
                Require(SnapshotBinary.ReadUInt16(stream) == SectionCount && SnapshotBinary.ReadUInt16(stream) == DescriptorSize, "Unsupported prepared descriptor table.");
                Require(SnapshotBinary.ReadUInt32(stream) == Crc32.Compute(prefix, HeaderSize, PrefixSize - HeaderSize), "Prepared descriptor checksum mismatch.");
                return d;
            }
        }

        private static long BoundaryCount(PersistencePartData source)
        {
            long count = 0;
            foreach (var face in source.StructuralFaces) count += face.CornerCount;
            return count;
        }

        private static void ValidateSectionCount(int id, long count, PackedTopologyData d, PersistencePartData source)
        {
            long exact = -1, max = -1, boundary = d.BoundaryCount;
            switch (id)
            {
                case 1: case 3: case 5: exact = d.LogicalCount + 1L; break;
                case 2: exact = d.RenderCount; break;
                case 4: case 10: max = 3L * d.TriangleCount; break;
                case 6: exact = 2L * d.EdgeCount; break;
                case 7: case 8: exact = d.EdgeCount; break;
                case 9: exact = d.EdgeCount + 1L; break;
                case 11: case 12: exact = d.StructuralEdgeCount; break;
                case 13: exact = d.FaceCount == 0 ? 0 : d.StructuralEdgeCount + 1L; break;
                case 14: max = boundary; break;
                case 15: exact = d.FaceCount == 0 ? 0 : d.FaceCount + 1L; break;
                case 16: case 17: exact = boundary; break;
                case 18: exact = d.FaceCount == 0 ? 0 : d.LogicalCount + 1L; break;
                case 19: exact = 2L * d.StructuralEdgeCount; break;
                case 20: case 21: exact = d.FaceCount; break;
                case 22: exact = d.TriangleCount; break;
            }
            Require(count >= 0 && (exact < 0 ? count <= max : count == exact), "Prepared section count does not match geometry.");
        }

        internal static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }

    }
}
