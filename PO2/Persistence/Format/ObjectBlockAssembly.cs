using System;
using System.Collections.Generic;
using System.IO;

namespace PO2.Persistence.Format
{
    internal sealed class PreparedPart : IDisposable
    {
        private readonly int m_Vertices, m_Logical, m_Triangles, m_Faces, m_Areas;
        private List<EncodedStream> m_Streams;
        internal int Length { get; }
        internal long RetainedBytes { get; }

        internal PreparedPart(int vertices, int logical, int triangles, int faces, int areas, List<EncodedStream> streams)
        {
            m_Vertices = vertices; m_Logical = logical; m_Triangles = triangles; m_Faces = faces; m_Areas = areas;
            m_Streams = streams;
            int length = checked(24 + streams.Count * 20);
            long retained = length;
            foreach (var stream in streams)
            {
                length = checked(length + stream.Payload.Length);
                retained = checked(retained + stream.Payload.Capacity);
            }
            Length = length;
            RetainedBytes = retained;
        }

        internal void WriteTo(Stream stream)
        {
            if (m_Streams == null) throw new ObjectDisposedException(nameof(PreparedPart));
            SnapshotBinary.WriteUInt32(stream, checked((uint)m_Vertices));
            SnapshotBinary.WriteUInt32(stream, checked((uint)m_Logical));
            SnapshotBinary.WriteUInt32(stream, checked((uint)m_Triangles));
            SnapshotBinary.WriteUInt32(stream, checked((uint)m_Faces));
            SnapshotBinary.WriteUInt32(stream, checked((uint)m_Areas));
            SnapshotBinary.WriteUInt16(stream, checked((ushort)m_Streams.Count));
            SnapshotBinary.WriteUInt16(stream, 0);
            int offset = 0;
            foreach (var encoded in m_Streams)
            {
                stream.WriteByte((byte)encoded.Semantic);
                stream.WriteByte((byte)encoded.Codec);
                SnapshotBinary.WriteUInt16(stream, 0);
                SnapshotBinary.WriteUInt32(stream, checked((uint)encoded.ElementCount));
                SnapshotBinary.WriteUInt32(stream, checked((uint)encoded.DecodedByteLength));
                SnapshotBinary.WriteUInt32(stream, checked((uint)encoded.Payload.Length));
                SnapshotBinary.WriteUInt32(stream, checked((uint)offset));
                offset = checked(offset + encoded.Payload.Length);
            }
            foreach (var encoded in m_Streams) encoded.Payload.WriteTo(stream);
        }

        public void Dispose() { m_Streams?.Clear(); m_Streams = null; }
    }

    // Only the ordered writer owns this buffer. Keep one object, not the entire save.
    internal sealed class ObjectBlockAssembly : IDisposable
    {
        private readonly MemoryStream m_Output = new MemoryStream();
        private readonly ushort m_Schema;
        private bool m_Finished;

        internal ObjectBlockAssembly(ushort schema)
        {
            m_Schema = schema;
            m_Output.SetLength(SnapshotFormat.kObjectBlockHeaderSize);
            m_Output.Position = SnapshotFormat.kObjectBlockHeaderSize;
        }

        private void CheckAppend(int length)
        {
            if (m_Finished) throw new InvalidOperationException("Object block already finished.");
            long next = checked(m_Output.Length + length);
            if (length < 0 || next > SnapshotFormat.kMaxStoredObjectBytes || next > int.MaxValue)
                throw new InvalidDataException("Encoded object block exceeds safety ceiling.");
        }

        internal void Append(EncodedBuffer buffer)
        {
            CheckAppend(buffer.Length);
            buffer.WriteTo(m_Output);
        }

        internal void Append(PreparedPart part)
        {
            CheckAppend(part.Length);
            part.WriteTo(m_Output);
        }

        internal EncodedBuffer Finish(out uint checksum)
        {
            if (m_Finished) throw new InvalidOperationException("Object block already finished.");
            m_Finished = true;
            int length = checked((int)m_Output.Length);
            int bodyLength = length - SnapshotFormat.kObjectBlockHeaderSize;
            uint bodyCrc = Crc32.Compute(m_Output.GetBuffer(), SnapshotFormat.kObjectBlockHeaderSize, bodyLength);
            m_Output.Position = 0;
            m_Output.WriteByte((byte)'P'); m_Output.WriteByte((byte)'O'); m_Output.WriteByte((byte)'2'); m_Output.WriteByte((byte)'O');
            SnapshotBinary.WriteUInt16(m_Output, m_Schema);
            SnapshotBinary.WriteUInt16(m_Output, 0);
            SnapshotBinary.WriteUInt32(m_Output, checked((uint)bodyLength));
            SnapshotBinary.WriteUInt32(m_Output, bodyCrc);
            checksum = Crc32.Compute(m_Output.GetBuffer(), 0, length);
            return EncodedBuffer.FromStream(m_Output);
        }

        public void Dispose() => m_Output.Dispose();
    }
}
