using System;
using System.IO;
using UnityEngine;

namespace PO2.Persistence.Format
{
    // Descriptor and stored payload for one mesh stream.
    // DecodedByteLength is kept even for compressed codecs so readers can bound allocation first.
    internal sealed class EncodedStream
    {
        public SnapshotStreamSemantic Semantic { get; }
        public SnapshotCodec Codec { get; }
        public int ElementCount { get; }
        public int DecodedByteLength { get; }
        public EncodedBuffer Payload { get; }

        internal EncodedStream(SnapshotStreamSemantic semantic, SnapshotCodec codec, int elementCount, int decodedByteLength, EncodedBuffer payload)
        {
            Semantic = semantic;
            Codec = codec;
            ElementCount = elementCount;
            DecodedByteLength = decodedByteLength;
            Payload = payload;
        }
    }

    // Decoder views are bounded slices of the checksum-validated object block.
    // They never escape decoding into a runtime model.
    internal sealed class EncodedStreamView
    {
        internal readonly SnapshotStreamSemantic Semantic;
        internal readonly SnapshotCodec Codec;
        internal readonly int ElementCount, DecodedByteLength;
        internal readonly ArraySegment<byte> Payload;

        internal EncodedStreamView(SnapshotStreamSemantic semantic, SnapshotCodec codec, int count, int decodedLength, ArraySegment<byte> payload)
        {
            Semantic = semantic;
            Codec = codec;
            ElementCount = count;
            DecodedByteLength = decodedLength;
            Payload = payload;
        }

        public static implicit operator EncodedStreamView(EncodedStream stream) =>
            new EncodedStreamView(stream.Semantic, stream.Codec, stream.ElementCount, stream.DecodedByteLength, stream.Payload.Segment);
    }

    internal static class ObjectBlockCodec
    {
        // Trying multiple compression layouts costs more than it saves on tiny streams,
        // so small payloads stay in their simple raw/fixed form.
        internal const int kFloatCandidateThresholdBytes = 256;

        internal static EncodedStream EncodeVector3(SnapshotStreamSemantic semantic, Vector3[] values, EncodingContext context = null)
        {
            if (values == null) return null;
            int length = checked(values.Length * 12);
            if (context != null && context.Compression == SaveCompression.Fast && length >= 16 * 1024)
                return EncodeSampledFloatStream(semantic, values.Length, length, FloatStreamPacking.Sample(values),
                    shuffled => FloatStreamPacking.Pack(values, shuffled), context);
            return EncodeFloatStream(semantic, FloatStreamPacking.Pack(values, false), values.Length, context);
        }

        internal static EncodedStream EncodeVector4(SnapshotStreamSemantic semantic, Vector4[] values, EncodingContext context = null)
        {
            if (values == null) return null;
            int length = checked(values.Length * 16);
            if (context != null && context.Compression == SaveCompression.Fast && length >= 16 * 1024)
                return EncodeSampledFloatStream(semantic, values.Length, length, FloatStreamPacking.Sample(values),
                    shuffled => FloatStreamPacking.Pack(values, shuffled), context);
            return EncodeFloatStream(semantic, FloatStreamPacking.Pack(values, false), values.Length, context);
        }

        private static EncodedStream EncodeSampledFloatStream(SnapshotStreamSemantic semantic, int count, int length,
            byte[] sample, Func<bool, byte[]> pack, EncodingContext context)
        {
            int ordinary = Deflate(sample, context).Length;
            int shuffledLength = Deflate(SnapshotBinary.ShuffleFloatBytes(sample), context).Length;
            bool shuffled = shuffledLength < ordinary;
            byte[] packed = pack(shuffled);
            EncodedBuffer compressed = Deflate(packed, context);
            if (compressed.Length < length)
                return new EncodedStream(semantic, shuffled ? SnapshotCodec.ByteShuffleDeflate : SnapshotCodec.Deflate, count, length, compressed);
            return new EncodedStream(semantic, SnapshotCodec.Raw, count, length, shuffled ? pack(false) : packed);
        }

        private static EncodedBuffer Deflate(EncodedBuffer bytes, EncodingContext context) =>
            context == null ? SnapshotBinary.DeflateBuffer(bytes) : context.Deflate(bytes);

        internal static EncodedStream EncodeColors(Color32[] values, EncodingContext context = null)
        {
            if (values == null) return null;
            int length = checked(values.Length * 4);
            bool uniform = values.Length > 0;
            Color32 first = values.Length > 0 ? values[0] : default;
            for (int i = 1; uniform && i < values.Length; i++)
            {
                var value = values[i];
                uniform = value.r == first.r && value.g == first.g && value.b == first.b && value.a == first.a;
            }
            if (uniform)
                return new EncodedStream(SnapshotStreamSemantic.VertexColors, SnapshotCodec.UniformColor32, values.Length, length, new[] { first.r, first.g, first.b, first.a });
            var raw = new byte[length];
            int cursor = 0;
            foreach (var value in values)
            {
                raw[cursor++] = value.r; raw[cursor++] = value.g;
                raw[cursor++] = value.b; raw[cursor++] = value.a;
            }

            EncodedBuffer deflated = raw.Length >= kFloatCandidateThresholdBytes ? Deflate(raw, context) : default;
            if (deflated.Array != null && deflated.Length < raw.Length)
            {
                return new EncodedStream(SnapshotStreamSemantic.VertexColors, SnapshotCodec.Deflate, values.Length, raw.Length, deflated);
            }

            return new EncodedStream(SnapshotStreamSemantic.VertexColors, SnapshotCodec.Raw, values.Length, raw.Length, raw);
        }

        internal static EncodedStream EncodeTriangles(int[] values, int renderVertexCount, EncodingContext context = null)
        {
            return EncodeAdaptiveInteger(SnapshotStreamSemantic.Triangles, values, Math.Max(0, renderVertexCount - 1), allowRle: false, context: context);
        }

        internal static EncodedStream EncodeRenderToLogical(int[] values, int logicalVertexCount, EncodingContext context = null)
        {
            return EncodeAdaptiveInteger(SnapshotStreamSemantic.RenderToLogical, values, Math.Max(0, logicalVertexCount - 1), allowRle: false, context: context);
        }

        internal static EncodedStream EncodeMembership(int[] values, int maxAreaId, EncodingContext context = null)
        {
            return EncodeAdaptiveInteger(SnapshotStreamSemantic.TriangleSurfaceAreaIds, values, Math.Max(0, maxAreaId), allowRle: true, context: context);
        }

        internal static EncodedStream EncodeStructuralFaceIds(int[] values, EncodingContext context = null)
        {
            int max = FindMaximum(values);
            return EncodeAdaptiveInteger(SnapshotStreamSemantic.StructuralFaceIds, values, max, allowRle: false, context: context);
        }

        internal static EncodedStream EncodeStructuralCorners(int[] values, int renderVertexCount, EncodingContext context = null)
        {
            return EncodeAdaptiveInteger(SnapshotStreamSemantic.StructuralFaceCorners, values, Math.Max(0, renderVertexCount - 1), allowRle: false, context: context);
        }

        internal static EncodedStream EncodeStructuralCornerCounts(byte[] values, EncodingContext context = null)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            byte[] raw = (byte[])values.Clone();
            EncodedBuffer deflated = raw.Length >= kFloatCandidateThresholdBytes ? Deflate(raw, context) : default;
            if (deflated.Array != null && deflated.Length < raw.Length)
            {
                return new EncodedStream(SnapshotStreamSemantic.StructuralFaceCornerCounts, SnapshotCodec.Deflate, values.Length, raw.Length, deflated);
            }

            return new EncodedStream(SnapshotStreamSemantic.StructuralFaceCornerCounts, SnapshotCodec.Raw, values.Length, raw.Length, raw);
        }

        private static EncodedStream EncodeFloatStream(SnapshotStreamSemantic semantic, byte[] raw, int elementCount, EncodingContext context)
        {
            SnapshotCodec bestCodec = SnapshotCodec.Raw;
            EncodedBuffer best = raw;

            // Compact and small Fast streams retain both original candidates.
            // Large Fast streams use EncodeSampledFloatStream before packing.
            // This actually doesn't make much of a difference but every bit helps I guess.
            if (raw.Length >= kFloatCandidateThresholdBytes)
            {
                EncodedBuffer deflated = Deflate(raw, context);
                if (deflated.Length < best.Length)
                {
                    best = deflated;
                    bestCodec = SnapshotCodec.Deflate;
                }
                EncodedBuffer shuffled = Deflate(SnapshotBinary.ShuffleFloatBytes(raw), context);
                if (shuffled.Length < best.Length)
                {
                    best = shuffled;
                    bestCodec = SnapshotCodec.ByteShuffleDeflate;
                }
            }
            return new EncodedStream(semantic, bestCodec, elementCount, raw.Length, best);
        }

        private static EncodedStream EncodeAdaptiveInteger(SnapshotStreamSemantic semantic, int[] values, int maximumValue, bool allowRle, EncodingContext context)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            SnapshotCodec fixedCodec = SnapshotBinary.SelectUnsignedFixedCodec(maximumValue);
            if (context != null && context.Compression == SaveCompression.Fast)
            {
                return EncodeFastInteger(semantic, values, fixedCodec, allowRle, context);
            }

            EncodedBuffer best = SnapshotBinary.EncodeUnsignedFixed(values, fixedCodec);
            SnapshotCodec bestCodec = fixedCodec;

            // Indices usually move by small deltas, while Surface Area membership often has long repeated runs.
            // Try both shapes and keep the smallest.
            byte[] delta = SnapshotBinary.EncodeDeltaZigZagVarInt(values);
            if (delta.Length < best.Length)
            {
                best = delta;
                bestCodec = SnapshotCodec.DeltaZigZagVarInt;
            }

            if (delta.Length >= kFloatCandidateThresholdBytes)
            {
                EncodedBuffer compressedDelta = Deflate(delta, context);
                if (compressedDelta.Length < best.Length)
                {
                    best = compressedDelta;
                    bestCodec = SnapshotCodec.DeltaZigZagVarIntDeflate;
                }
            }

            if (allowRle)
            {
                byte[] rle = SnapshotBinary.EncodeRleVarUInt(values);
                if (rle.Length < best.Length)
                {
                    best = rle;
                    bestCodec = SnapshotCodec.RleVarUInt;
                }

                if (rle.Length >= kFloatCandidateThresholdBytes)
                {
                    EncodedBuffer compressedRle = Deflate(rle, context);
                    if (compressedRle.Length < best.Length)
                    {
                        best = compressedRle;
                        bestCodec = SnapshotCodec.RleVarUIntDeflate;
                    }
                }
            }

            return new EncodedStream(semantic, bestCodec, values.Length, checked(values.Length * 4), best);
        }

        private static EncodedStream EncodeFastInteger(SnapshotStreamSemantic semantic, int[] values, SnapshotCodec fixedCodec, bool allowRle, EncodingContext context)
        {
            int width = fixedCodec == SnapshotCodec.UnsignedFixed8 ? 1 : fixedCodec == SnapshotCodec.UnsignedFixed16 ? 2 : 4;
            int fixedLength = checked(values.Length * width);
            int maximum = FindMaximum(values);
            if ((width == 1 && maximum > byte.MaxValue) || (width == 2 && maximum > ushort.MaxValue))
                throw new InvalidDataException("Value exceeds unsigned fixed range.");
            EncodedBuffer stored;
            SnapshotCodec codec;
            if (fixedLength < kFloatCandidateThresholdBytes)
            {
                stored = SnapshotBinary.EncodeUnsignedFixed(values, fixedCodec);
                codec = fixedCodec;
            }
            else
            {
                // Membership benefits from runs; connectivity benefits from small deltas.
                // Fast makes one compression attempt and retains a lossless raw fallback.
                stored = allowRle ? SnapshotBinary.EncodeRleVarUInt(values) : SnapshotBinary.EncodeDeltaZigZagVarInt(values);
                codec = allowRle ? SnapshotCodec.RleVarUInt : SnapshotCodec.DeltaZigZagVarInt;
                if (stored.Length >= kFloatCandidateThresholdBytes)
                {
                    EncodedBuffer compressed = Deflate(stored, context);
                    if (compressed.Length < stored.Length)
                    {
                        stored = compressed;
                        codec = allowRle ? SnapshotCodec.RleVarUIntDeflate : SnapshotCodec.DeltaZigZagVarIntDeflate;
                    }
                }

                if (stored.Length >= fixedLength)
                {
                    stored = SnapshotBinary.EncodeUnsignedFixed(values, fixedCodec);
                    codec = fixedCodec;
                }
            }

            return new EncodedStream(semantic, codec, values.Length, checked(values.Length * 4), stored);
        }

        internal static Vector3[] DecodeVector3(EncodedStreamView stream)
        {
            var bytes = DecodeFloatBytes(stream, 3, out bool shuffled);
            return FloatStreamPacking.Read3(bytes, stream.ElementCount, shuffled);
        }

        internal static Vector4[] DecodeVector4(EncodedStreamView stream)
        {
            var bytes = DecodeFloatBytes(stream, 4, out bool shuffled);
            return FloatStreamPacking.Read4(bytes, stream.ElementCount, shuffled);
        }

        internal static Color32[] DecodeColors(EncodedStreamView stream)
        {
            if (stream.Codec == SnapshotCodec.UniformColor32)
            {
                if (stream.Payload.Count != 4)
                {
                    throw new InvalidDataException("Uniform Color32 payload must contain exactly four bytes.");
                }

                Color32[] result = new Color32[stream.ElementCount];
                Color32 color = new Color32(stream.Payload.Array[stream.Payload.Offset + (0)], stream.Payload.Array[stream.Payload.Offset + (1)], stream.Payload.Array[stream.Payload.Offset + (2)], stream.Payload.Array[stream.Payload.Offset + (3)]);
                for (int i = 0; i < result.Length; i++) result[i] = color;
                return result;
            }

            ArraySegment<byte> raw = DecodeRawOrDeflate(stream);
            if (raw.Count != checked(stream.ElementCount * 4))
            {
                throw new InvalidDataException("Color32 decoded length mismatch.");
            }

            Color32[] colors = new Color32[stream.ElementCount];
            int cursor = 0;
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32(raw.Array[raw.Offset + (cursor++)], raw.Array[raw.Offset + (cursor++)], raw.Array[raw.Offset + (cursor++)], raw.Array[raw.Offset + (cursor++)]);
            }

            return colors;
        }

        internal static int[] DecodeInteger(EncodedStreamView stream)
        {
            int expectedDecodedBytes = checked(stream.ElementCount * 4);
            if (stream.DecodedByteLength != expectedDecodedBytes)
            {
                throw new InvalidDataException($"Integer stream decoded length {stream.DecodedByteLength} does not match expected {expectedDecodedBytes}.");
            }

            switch (stream.Codec)
            {
                case SnapshotCodec.UnsignedFixed8:
                case SnapshotCodec.UnsignedFixed16:
                case SnapshotCodec.UnsignedFixed32:
                    return SnapshotBinary.DecodeUnsignedFixed(stream.Payload, stream.ElementCount, stream.Codec);
                case SnapshotCodec.DeltaZigZagVarInt:
                    return SnapshotBinary.DecodeDeltaZigZagVarInt(stream.Payload, stream.ElementCount);
                case SnapshotCodec.DeltaZigZagVarIntDeflate:
                {
                    byte[] decoded = InflateUnknownLength(stream.Payload, checked(Math.Max(1, stream.ElementCount) * 5));
                    return SnapshotBinary.DecodeDeltaZigZagVarInt(decoded, stream.ElementCount);
                }
                case SnapshotCodec.RleVarUInt:
                    return SnapshotBinary.DecodeRleVarUInt(stream.Payload, stream.ElementCount);
                case SnapshotCodec.RleVarUIntDeflate:
                {
                    byte[] decoded = InflateUnknownLength(stream.Payload, checked(Math.Max(1, stream.ElementCount) * 10));
                    return SnapshotBinary.DecodeRleVarUInt(decoded, stream.ElementCount);
                }
                default:
                    throw new InvalidDataException($"Codec {stream.Codec} is not valid for an integer stream.");
            }
        }

        internal static byte[] DecodeByteStream(EncodedStreamView stream)
        {
            var raw = DecodeRawOrDeflate(stream);
            var result = new byte[raw.Count];
            Array.Copy(raw.Array, raw.Offset, result, 0, raw.Count);
            return result;
        }

        private static ArraySegment<byte> DecodeFloatBytes(EncodedStreamView stream, int components, out bool shuffled)
        {
            shuffled = stream.Codec == SnapshotCodec.ByteShuffleDeflate;
            int expected = checked(stream.ElementCount * components * 4);
            if (stream.DecodedByteLength != expected)
            {
                throw new InvalidDataException($"Float stream decoded length {stream.DecodedByteLength} does not match expected {expected}.");
            }

            ArraySegment<byte> raw;
            switch (stream.Codec)
            {
                case SnapshotCodec.Raw:
                    raw = stream.Payload;
                    break;
                case SnapshotCodec.Deflate:
                    raw = new ArraySegment<byte>(SnapshotBinary.Inflate(stream.Payload, expected));
                    break;
                case SnapshotCodec.ByteShuffleDeflate:
                    raw = new ArraySegment<byte>(SnapshotBinary.Inflate(stream.Payload, expected));
                    break;
                default:
                    throw new InvalidDataException($"Codec {stream.Codec} is not valid for a float stream.");
            }

            if (raw.Count != expected) throw new InvalidDataException("Float stream length mismatch.");
            return raw;
        }

        private static ArraySegment<byte> DecodeRawOrDeflate(EncodedStreamView stream)
        {
            switch (stream.Codec)
            {
                case SnapshotCodec.Raw:
                    if (stream.Payload.Count != stream.DecodedByteLength)
                    {
                        throw new InvalidDataException("Raw stream length mismatch.");
                    }

                    return stream.Payload;
                case SnapshotCodec.Deflate:
                    return new ArraySegment<byte>(SnapshotBinary.Inflate(stream.Payload, stream.DecodedByteLength));
                default:
                    throw new InvalidDataException($"Codec {stream.Codec} is not valid for a raw byte stream.");
            }
        }

        private static byte[] InflateUnknownLength(ArraySegment<byte> compressed, int maximumDecodedBytes)
        {
            // Delta/RLE payload length is variable but it is still bounded from
            // the declared element count before any decompression can grow freely.
            using (MemoryStream input = new MemoryStream(compressed.Array, compressed.Offset, compressed.Count, writable: false))
            using (System.IO.Compression.DeflateStream deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int read;
                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > maximumDecodedBytes)
                    {
                        throw new InvalidDataException("Inflated integer stream exceeds declared decoded ceiling.");
                    }

                    output.Write(buffer, 0, read);
                }

                return output.ToArray();
            }
        }

        private static int FindMaximum(int[] values)
        {
            int max = 0;
            if (values == null) return max;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < 0) throw new InvalidDataException("Unsigned stream contains a negative value.");
                if (values[i] > max) max = values[i];
            }

            return max;
        }
    }
}