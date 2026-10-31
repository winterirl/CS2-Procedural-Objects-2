using System;
using System.IO;
using System.IO.Compression;
using PO2.Geometry.Topology;
using PO2.Persistence.Format;
using PO2.Persistence.PreparedTopology;

// Keep the old full writer here so compatibility tests can still make version 1
// records without leaving an old-format write path in the shipped mod.
internal static class FullTopologyTestWriter
{
    private const int SectionCount = 22, HeaderSize = 48, DescriptorSize = 32;
    private const int PackingBlockBytes = 16384;
    private const int PrefixSize = HeaderSize + SectionCount * DescriptorSize;
    private const ushort Version = 1;
    private const uint Revision = 1;
    private static readonly byte[] s_Magic = { 80, 79, 50, 80, 65, 82, 84, 0 };

    internal static EncodedPartTopology Encode(PartTopologyCapture capture)
    {
        var sections = new EncodedTopologySection[SectionCount];
        var buffer = new byte[PackingBlockBytes];
        var shuffled = new byte[buffer.Length];
        for (int id = 1; id <= SectionCount; id++)
        {
            int count = Count(capture, id);
            int bytes = checked(count * (id == 17 ? 1 : 4));
            byte[] stored;
            byte codec = 0;
            if (bytes > 256)
            {
                using (var output = new MemoryStream())
                {
                    using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                        WriteSection(deflate, capture, id, buffer, id == 17 ? null : shuffled);
                    stored = output.ToArray();
                }
                if (stored.Length < bytes) codec = id == 17 ? (byte)1 : (byte)2;
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
        using (var stream = new MemoryStream(bytes, true)) WriteSection(stream, capture, id, buffer);
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

    private static void WriteSection(Stream stream, PartTopologyCapture c, int id, byte[] buffer, byte[] shuffled = null)
    {
        var writer = new PackedWriter(stream, buffer, shuffled);
        int l = c.Logical.LogicalVertexCount, e = c.Logical.LogicalEdgeCount, f = c.Structural.FaceCount, s = c.Structural.StructuralEdgeCount;
        switch (id)
        {
            case 1: writer.Spans(l, i => c.Logical.GetVertex(i).RenderVertexIndices, true); break;
            case 2: writer.Spans(l, i => c.Logical.GetVertex(i).RenderVertexIndices, false); break;
            case 3: writer.Spans(l, i => c.Logical.GetVertex(i).TriangleIndices, true); break;
            case 4: writer.Spans(l, i => c.Logical.GetVertex(i).TriangleIndices, false); break;
            case 5: writer.Spans(l, i => c.Logical.GetVertex(i).AdjacentVertexIds, true); break;
            case 6: writer.Spans(l, i => c.Logical.GetVertex(i).AdjacentVertexIds, false); break;
            case 7: for (int i = 0; i < e; i++) writer.Int(c.Logical.GetEdge(i).VertexA); break;
            case 8: for (int i = 0; i < e; i++) writer.Int(c.Logical.GetEdge(i).VertexB); break;
            case 9: writer.Spans(e, i => c.Logical.GetEdge(i).TriangleIndices, true); break;
            case 10: writer.Spans(e, i => c.Logical.GetEdge(i).TriangleIndices, false); break;
            case 11: for (int i = 0; i < s; i++) writer.Int(c.Structural.GetEdge(i).LogicalVertexA); break;
            case 12: for (int i = 0; i < s; i++) writer.Int(c.Structural.GetEdge(i).LogicalVertexB); break;
            case 13: if (f != 0) writer.Spans(s, i => c.Structural.GetEdge(i).AdjacentFaceIds, true); break;
            case 14: writer.Spans(s, i => c.Structural.GetEdge(i).AdjacentFaceIds, false); break;
            case 15: writer.Array(c.Structural.PersistenceBoundaryOffsets); break;
            case 16: writer.Array(c.Structural.PersistenceBoundaryEdges); break;
            case 17: var bytes = c.Structural.PersistenceBoundaryForward; stream.Write(bytes, 0, bytes.Length); break;
            case 18: writer.Array(c.Structural.PersistenceIncidentOffsets); break;
            case 19: writer.Array(c.Structural.PersistenceIncidentEdges); break;
            case 20: for (int i = 0; i < f; i++) writer.Int(c.FaceTriangles[i].TriangleA); break;
            case 21: for (int i = 0; i < f; i++) writer.Int(c.FaceTriangles[i].TriangleB); break;
            case 22: writer.Array(c.TriangleFaces); break;
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
}
