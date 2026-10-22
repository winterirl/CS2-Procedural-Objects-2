using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace PO2.Persistence.Format
{
    // Exact little-endian primitives used by the production snapshot format.
    // These methods contain no runtime/game-state dependencies.
    internal static class SnapshotBinary
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] public float Float;
            [FieldOffset(0)] public uint UInt;
        }

        internal static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        internal static ushort ReadUInt16(Stream stream)
        {
            int b0 = ReadByteRequired(stream);
            int b1 = ReadByteRequired(stream);
            return (ushort)(b0 | (b1 << 8));
        }

        internal static void WriteUInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        internal static uint ReadUInt32(Stream stream)
        {
            uint b0 = (uint)ReadByteRequired(stream);
            uint b1 = (uint)ReadByteRequired(stream);
            uint b2 = (uint)ReadByteRequired(stream);
            uint b3 = (uint)ReadByteRequired(stream);
            return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
        }

        internal static void WriteUInt64(Stream stream, ulong value)
        {
            WriteUInt32(stream, (uint)value);
            WriteUInt32(stream, (uint)(value >> 32));
        }

        internal static ulong ReadUInt64(Stream stream)
        {
            ulong low = ReadUInt32(stream);
            ulong high = ReadUInt32(stream);
            return low | (high << 32);
        }

        internal static void WriteSingle(Stream stream, float value)
        {
            // Preserve the exact bits.
            // Persistence never rounds mesh or transform values to make the sidecar smaller.
            FloatBits bits = new FloatBits { Float = value };
            WriteUInt32(stream, bits.UInt);
        }

        internal static float ReadSingle(Stream stream)
        {
            FloatBits bits = new FloatBits { UInt = ReadUInt32(stream) };
            return bits.Float;
        }

        internal static void WriteGuid(Stream stream, Guid value)
        {
            byte[] bytes = value.ToByteArray();
            stream.Write(bytes, 0, bytes.Length);
        }

        internal static Guid ReadGuid(Stream stream)
        {
            return new Guid(ReadExactBytes(stream, SnapshotFormat.kGuidByteCount));
        }

        internal static void WriteHash128(Stream stream, Colossal.Hash128 value)
        {
            WriteUInt32(stream, value.value.x);
            WriteUInt32(stream, value.value.y);
            WriteUInt32(stream, value.value.z);
            WriteUInt32(stream, value.value.w);
        }

        internal static Colossal.Hash128 ReadHash128(Stream stream)
        {
            return new Colossal.Hash128(ReadUInt32(stream), ReadUInt32(stream), ReadUInt32(stream), ReadUInt32(stream));
        }

        internal static byte[] ReadExactBytes(Stream stream, int length)
        {
            if (length < 0)
            {
                throw new InvalidDataException("Negative binary read length.");
            }

            byte[] result = new byte[length];
            ReadExactly(stream, result, 0, length);
            return result;
        }

        internal static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            // Stream.Read is allowed to return fewer bytes than requested even before EOF
            // Keep reading until the declared field is complete.
            int remaining = count;
            while (remaining > 0)
            {
                int read = stream.Read(buffer, offset, remaining);
                if (read <= 0)
                {
                    throw new EndOfStreamException($"Snapshot ended with {remaining} byte(s) still required.");
                }

                offset += read;
                remaining -= read;
            }
        }

        internal static void WriteVarUInt(Stream stream, uint value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        internal static uint ReadVarUInt(Stream stream)
        {
            uint value = 0;
            int shift = 0;
            for (int i = 0; i < 5; i++)
            {
                int current = ReadByteRequired(stream);
                value |= (uint)(current & 0x7f) << shift;
                if ((current & 0x80) == 0)
                {
                    return value;
                }

                shift += 7;
            }

            throw new InvalidDataException("VarUInt exceeds 32-bit encoding length.");
        }

        internal static uint ZigZagEncode(int value)
        {
            return unchecked((uint)((value << 1) ^ (value >> 31)));
        }

        internal static int ZigZagDecode(uint value)
        {
            return unchecked((int)((value >> 1) ^ (uint)-(int)(value & 1)));
        }

        internal static void WriteFloat32(byte[] bytes, int offset, float value)
        {
            FloatBits bits = new FloatBits { Float = value };
            bytes[offset] = (byte)bits.UInt;
            bytes[offset + 1] = (byte)(bits.UInt >> 8);
            bytes[offset + 2] = (byte)(bits.UInt >> 16);
            bytes[offset + 3] = (byte)(bits.UInt >> 24);
        }

        internal static float ReadFloat32(byte[] bytes, int index, bool shuffled)
        {
            int stride = shuffled ? bytes.Length / 4 : 1;
            int offset = shuffled ? index : index * 4;
            uint bits = (uint)(bytes[offset] | (bytes[offset + stride] << 8) | (bytes[offset + stride * 2] << 16) | (bytes[offset + stride * 3] << 24));
            return new FloatBits { UInt = bits }.Float;
        }

        internal static byte[] EncodeFloat32(float[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length == 0) return Array.Empty<byte>();

            byte[] result = new byte[checked(values.Length * 4)];
            for (int i = 0; i < values.Length; i++)
            {
                FloatBits bits = new FloatBits { Float = values[i] };
                int offset = i * 4;
                result[offset] = (byte)bits.UInt;
                result[offset + 1] = (byte)(bits.UInt >> 8);
                result[offset + 2] = (byte)(bits.UInt >> 16);
                result[offset + 3] = (byte)(bits.UInt >> 24);
            }

            return result;
        }

        internal static float[] DecodeFloat32(byte[] bytes)
        {
            ValidateFloatByteStream(bytes, "decode");
            if (bytes.Length == 0) return Array.Empty<float>();

            float[] result = new float[bytes.Length / 4];
            for (int i = 0; i < result.Length; i++)
            {
                int offset = i * 4;
                uint bits = (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
                result[i] = new FloatBits { UInt = bits }.Float;
            }

            return result;
        }

        internal static byte[] EncodeUnsignedFixed(int[] values, SnapshotCodec codec)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            int width = GetUnsignedFixedWidth(codec);
            byte[] result = new byte[checked(values.Length * width)];

            int offset = 0;
            for (int i = 0; i < values.Length; i++)
            {
                int value = values[i];
                if (value < 0)
                {
                    throw new InvalidDataException("Unsigned fixed stream contains a negative value.");
                }

                switch (width)
                {
                    case 1:
                        if (value > byte.MaxValue)
                        {
                            throw new InvalidDataException("Value exceeds UnsignedFixed8 range.");
                        }

                        result[offset++] = (byte)value;
                        break;
                    case 2:
                        if (value > ushort.MaxValue)
                        {
                            throw new InvalidDataException("Value exceeds UnsignedFixed16 range.");
                        }

                        result[offset++] = (byte)value;
                        result[offset++] = (byte)(value >> 8);
                        break;
                    default:
                        uint raw = checked((uint)value);
                        result[offset++] = (byte)raw;
                        result[offset++] = (byte)(raw >> 8);
                        result[offset++] = (byte)(raw >> 16);
                        result[offset++] = (byte)(raw >> 24);
                        break;
                }
            }

            return result;
        }

        internal static int[] DecodeUnsignedFixed(byte[] bytes, int count, SnapshotCodec codec)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (count < 0) throw new InvalidDataException("Negative unsigned fixed element count.");
            int width = GetUnsignedFixedWidth(codec);
            int expectedLength = checked(count * width);
            if (bytes.Length != expectedLength)
            {
                throw new InvalidDataException($"Unsigned fixed stream length {bytes.Length} does not match expected {expectedLength}.");
            }

            int[] result = new int[count];
            int offset = 0;
            for (int i = 0; i < count; i++)
            {
                switch (width)
                {
                    case 1:
                        result[i] = bytes[offset++];
                        break;
                    case 2:
                        result[i] = bytes[offset] | (bytes[offset + 1] << 8);
                        offset += 2;
                        break;
                    default:
                        uint raw = (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
                        if (raw > int.MaxValue)
                        {
                            throw new InvalidDataException("UnsignedFixed32 value exceeds Int32 range.");
                        }

                        result[i] = (int)raw;
                        offset += 4;
                        break;
                }
            }

            return result;
        }

        internal static SnapshotCodec SelectUnsignedFixedCodec(int maximumValue)
        {
            if (maximumValue < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumValue));
            }

            if (maximumValue <= byte.MaxValue) return SnapshotCodec.UnsignedFixed8;
            if (maximumValue <= ushort.MaxValue) return SnapshotCodec.UnsignedFixed16;
            return SnapshotCodec.UnsignedFixed32;
        }

        internal static byte[] EncodeDeltaZigZagVarInt(int[] values)
        {
            if (values == null || values.Length == 0) return Array.Empty<byte>();

            // ZigZag keeps small negative deltas small too, which is useful when triangle indices step backward within a face.
            using (MemoryStream stream = new MemoryStream(Math.Max(16, values.Length)))
            {
                int previous = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    int delta = unchecked(values[i] - previous);
                    WriteVarUInt(stream, ZigZagEncode(delta));
                    previous = values[i];
                }

                return stream.ToArray();
            }
        }

        internal static int[] DecodeDeltaZigZagVarInt(byte[] bytes, int count)
        {
            if (count < 0) throw new InvalidDataException("Negative decoded integer count.");
            if (count == 0)
            {
                if (bytes != null && bytes.Length != 0)
                {
                    throw new InvalidDataException("Empty delta stream has trailing bytes.");
                }

                return Array.Empty<int>();
            }

            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            int[] result = new int[count];
            using (MemoryStream stream = new MemoryStream(bytes, false))
            {
                int previous = 0;
                for (int i = 0; i < count; i++)
                {
                    int delta = ZigZagDecode(ReadVarUInt(stream));
                    int current = checked(previous + delta);
                    result[i] = current;
                    previous = current;
                }

                if (stream.Position != stream.Length)
                {
                    throw new InvalidDataException("Delta stream has trailing bytes.");
                }
            }

            return result;
        }

        internal static byte[] EncodeRleVarUInt(int[] values)
        {
            if (values == null || values.Length == 0) return Array.Empty<byte>();

            using (MemoryStream stream = new MemoryStream())
            {
                int start = 0;
                while (start < values.Length)
                {
                    int value = values[start];
                    if (value < 0)
                    {
                        throw new InvalidDataException("RLE values must be non-negative.");
                    }

                    int end = start + 1;
                    while (end < values.Length && values[end] == value) end++;
                    WriteVarUInt(stream, checked((uint)value));
                    WriteVarUInt(stream, checked((uint)(end - start)));
                    start = end;
                }

                return stream.ToArray();
            }
        }

        internal static int[] DecodeRleVarUInt(byte[] bytes, int expectedCount)
        {
            if (expectedCount < 0) throw new InvalidDataException("Negative RLE decoded count.");
            if (expectedCount == 0)
            {
                if (bytes != null && bytes.Length != 0)
                {
                    throw new InvalidDataException("Empty RLE stream has trailing bytes.");
                }

                return Array.Empty<int>();
            }

            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            int[] result = new int[expectedCount];
            int destination = 0;
            using (MemoryStream stream = new MemoryStream(bytes, false))
            {
                while (destination < expectedCount)
                {
                    uint value = ReadVarUInt(stream);
                    uint runLength = ReadVarUInt(stream);
                    if (runLength == 0)
                    {
                        throw new InvalidDataException("RLE run length cannot be zero.");
                    }

                    if (value > int.MaxValue)
                    {
                        throw new InvalidDataException("RLE value exceeds Int32 range.");
                    }

                    if (runLength > (uint)(expectedCount - destination))
                    {
                        throw new InvalidDataException("RLE run exceeds expected decoded count.");
                    }

                    int runEnd = checked(destination + (int)runLength);
                    for (; destination < runEnd; destination++)
                    {
                        result[destination] = (int)value;
                    }
                }

                if (stream.Position != stream.Length)
                {
                    throw new InvalidDataException("RLE stream has trailing bytes.");
                }
            }

            return result;
        }

        internal static byte[] Deflate(byte[] raw)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (raw.Length == 0) return Array.Empty<byte>();

            using (MemoryStream output = new MemoryStream())
            {
                using (DeflateStream deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                {
                    deflate.Write(raw, 0, raw.Length);
                }

                return output.ToArray();
            }
        }

        internal static byte[] Inflate(byte[] compressed, int expectedLength)
        {
            if (compressed == null) throw new ArgumentNullException(nameof(compressed));
            if (expectedLength < 0)
            {
                throw new InvalidDataException("Negative expected inflate length.");
            }

            if (expectedLength == 0)
            {
                if (compressed.Length != 0)
                {
                    throw new InvalidDataException("Non-empty compressed stream for zero decoded bytes.");
                }

                return Array.Empty<byte>();
            }

            byte[] result = new byte[expectedLength];
            using (MemoryStream input = new MemoryStream(compressed, false))
            using (DeflateStream deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                ReadExactly(deflate, result, 0, result.Length);
                if (deflate.ReadByte() != -1)
                {
                    throw new InvalidDataException("Deflate stream decoded beyond declared length.");
                }
            }

            return result;
        }

        internal static byte[] ShuffleFloatBytes(byte[] raw)
        {
            ValidateFloatByteStream(raw, "shuffle");
            if (raw.Length == 0) return Array.Empty<byte>();

            // Grouping matching byte positions exposes repeated exponent/sign
            // bytes to Deflate while remaining exactly reversible.
            int count = raw.Length / SnapshotFormat.kFloatByteCount;
            byte[] shuffled = new byte[raw.Length];
            for (int i = 0; i < count; i++)
            {
                int source = i * 4;
                shuffled[i] = raw[source];
                shuffled[count + i] = raw[source + 1];
                shuffled[count * 2 + i] = raw[source + 2];
                shuffled[count * 3 + i] = raw[source + 3];
            }

            return shuffled;
        }

        internal static byte[] UnshuffleFloatBytes(byte[] shuffled)
        {
            ValidateFloatByteStream(shuffled, "unshuffle");
            if (shuffled.Length == 0) return Array.Empty<byte>();

            int count = shuffled.Length / SnapshotFormat.kFloatByteCount;
            byte[] raw = new byte[shuffled.Length];
            for (int i = 0; i < count; i++)
            {
                int destination = i * 4;
                raw[destination] = shuffled[i];
                raw[destination + 1] = shuffled[count + i];
                raw[destination + 2] = shuffled[count * 2 + i];
                raw[destination + 3] = shuffled[count * 3 + i];
            }

            return raw;
        }

        private static int GetUnsignedFixedWidth(SnapshotCodec codec)
        {
            switch (codec)
            {
                case SnapshotCodec.UnsignedFixed8:
                    return 1;
                case SnapshotCodec.UnsignedFixed16:
                    return 2;
                case SnapshotCodec.UnsignedFixed32:
                    return 4;
                default:
                    throw new ArgumentException($"Codec {codec} is not an unsigned fixed-width codec.", nameof(codec));
            }
        }

        private static int ReadByteRequired(Stream stream)
        {
            int value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException("Unexpected end of snapshot stream.");
            return value;
        }

        private static void ValidateFloatByteStream(byte[] bytes, string operation)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if ((bytes.Length & 3) != 0)
            {
                throw new InvalidDataException($"Float byte stream length is not divisible by four for {operation}.");
            }
        }
    }
}