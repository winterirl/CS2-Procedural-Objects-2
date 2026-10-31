using System;
using System.IO;

namespace PO2.Persistence.Format
{
    // A completed encoder owns this array. Capacity is not part of the payload.
    // No pooling: callers may keep a payload until its ordered write completes.
    internal readonly struct EncodedBuffer
    {
        internal ArraySegment<byte> Segment { get; }
        internal byte[] Array => Segment.Array;
        internal int Offset => Segment.Offset;
        internal int Length => Segment.Count;
        internal int Capacity => Array?.Length ?? 0;

        internal EncodedBuffer(byte[] array) : this(new ArraySegment<byte>(array)) { }
        internal EncodedBuffer(ArraySegment<byte> segment)
        {
            if (segment.Array == null) throw new ArgumentNullException(nameof(segment));
            Segment = segment;
        }
        internal static EncodedBuffer FromStream(MemoryStream stream) =>
            new EncodedBuffer(new ArraySegment<byte>(stream.GetBuffer(), 0, checked((int)stream.Length)));
        internal void WriteTo(Stream stream) => stream.Write(Array, Offset, Length);
        internal byte[] ToArray()
        {
            var result = new byte[Length];
            Buffer.BlockCopy(Array, Offset, result, 0, Length);
            return result;
        }
        public static implicit operator EncodedBuffer(byte[] bytes) => new EncodedBuffer(bytes);
    }
}
