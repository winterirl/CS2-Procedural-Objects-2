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
        public byte[] Payload { get; }

        internal EncodedStream(SnapshotStreamSemantic semantic, SnapshotCodec codec, int elementCount, int decodedByteLength, byte[] payload)
        {
            Semantic = semantic;
            Codec = codec;
            ElementCount = elementCount;
            DecodedByteLength = decodedByteLength;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }
    }

    internal static class ObjectBlockCodec
    {
        // Trying multiple compression layouts costs more than it saves on tiny streams,
        // so small payloads stay in their simple raw/fixed form.
        internal const int kFloatCandidateThresholdBytes = 256;

        internal static EncodedStream EncodeVector3(SnapshotStreamSemantic semantic, Vector3[] values, EncodingContext context = null)
        {
            if (values == null) return null;
            byte[] raw = new byte[checked(values.Length * 12)];
            for (int i = 0; i < values.Length; i++)
            {
                int offset = i * 12;
                SnapshotBinary.WriteFloat32(raw, offset, values[i].x);
                SnapshotBinary.WriteFloat32(raw, offset + 4, values[i].y);
                SnapshotBinary.WriteFloat32(raw, offset + 8, values[i].z);
            }

            return EncodeFloatStream(semantic, raw, values.Length, context);
        }

        internal static EncodedStream EncodeVector4(SnapshotStreamSemantic semantic, Vector4[] values, EncodingContext context = null)
        {
            if (values == null) return null;
            byte[] raw = new byte[checked(values.Length * 16)];
            for (int i = 0; i < values.Length; i++)
            {
                int offset = i * 16;
                SnapshotBinary.WriteFloat32(raw, offset, values[i].x);
                SnapshotBinary.WriteFloat32(raw, offset + 4, values[i].y);
                SnapshotBinary.WriteFloat32(raw, offset + 8, values[i].z);
                SnapshotBinary.WriteFloat32(raw, offset + 12, values[i].w);
            }

            return EncodeFloatStream(semantic, raw, values.Length, context);
        }

        private static byte[] Deflate(byte[] bytes, EncodingContext context) =>
            context == null ? SnapshotBinary.Deflate(bytes) : context.Deflate(bytes);

        internal static EncodedStream EncodeColors(Color32[] values, EncodingContext context = null)
        {
            if (values == null) return null;
            byte[] raw = new byte[checked(values.Length * 4)];
            bool uniform = values.Length > 0;
            Color32 first = values.Length > 0 ? values[0] : default;
            int cursor = 0;
            for (int i = 0; i < values.Length; i++)
            {
                Color32 value = values[i];
                raw[cursor++] = value.r;
                raw[cursor++] = value.g;
                raw[cursor++] = value.b;
                raw[cursor++] = value.a;
                if (uniform && (value.r != first.r || value.g != first.g || value.b != first.b || value.a != first.a))
                {
                    uniform = false;
                }
            }

            // Converted window meshes often repeat the same packed color across a whole region.
            // Four bytes are enough for that common case.
            if (uniform && values.Length > 0)
            {
                return new EncodedStream(SnapshotStreamSemantic.VertexColors, SnapshotCodec.UniformColor32, values.Length, raw.Length, new[] { first.r, first.g, first.b, first.a });
            }

            byte[] deflated = raw.Length >= kFloatCandidateThresholdBytes ? Deflate(raw, context) : null;
            if (deflated != null && deflated.Length < raw.Length)
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
            byte[] deflated = raw.Length >= kFloatCandidateThresholdBytes ? Deflate(raw, context) : null;
            if (deflated != null && deflated.Length < raw.Length)
            {
                return new EncodedStream(SnapshotStreamSemantic.StructuralFaceCornerCounts, SnapshotCodec.Deflate, values.Length, raw.Length, deflated);
            }

            return new EncodedStream(SnapshotStreamSemantic.StructuralFaceCornerCounts, SnapshotCodec.Raw, values.Length, raw.Length, raw);
        }

        private static EncodedStream EncodeFloatStream(SnapshotStreamSemantic semantic, byte[] raw, int elementCount, EncodingContext context)
        {
            SnapshotCodec bestCodec = SnapshotCodec.Raw;
            byte[] best = raw;

            // Compact keeps the old full-stream candidates. Fast samples large
            // streams first, then compresses the full stream in just one layout.
            if (raw.Length >= kFloatCandidateThresholdBytes)
            {
                bool sample = context != null && context.Compression == SaveCompression.Fast && raw.Length >= 16 * 1024;
                bool shuffled = sample && PreferShuffledSample(raw, context);
                if (!sample || !shuffled)
                {
                    byte[] deflated = Deflate(raw, context);
                    if (deflated.Length < best.Length)
                    {
                        best = deflated;
                        bestCodec = SnapshotCodec.Deflate;
                    }
                }

                if (!sample || shuffled)
                {
                    byte[] shuffledDeflated = Deflate(SnapshotBinary.ShuffleFloatBytes(raw), context);
                    if (shuffledDeflated.Length < best.Length)
                    {
                        best = shuffledDeflated;
                        bestCodec = SnapshotCodec.ByteShuffleDeflate;
                    }
                }
            }

            return new EncodedStream(semantic, bestCodec, elementCount, raw.Length, best);
        }

        private static bool PreferShuffledSample(byte[] raw, EncodingContext context)
        {
            const int chunk = 1024;
            var sample = new byte[chunk * 3];
            Buffer.BlockCopy(raw, 0, sample, 0, chunk);
            Buffer.BlockCopy(raw, ((raw.Length - chunk) / 2) & ~3, sample, chunk, chunk);
            Buffer.BlockCopy(raw, raw.Length - chunk, sample, chunk * 2, chunk);
            int ordinary = Deflate(sample, context).Length;
            int shuffled = Deflate(SnapshotBinary.ShuffleFloatBytes(sample), context).Length;
            return shuffled < ordinary;
        }

        private static EncodedStream EncodeAdaptiveInteger(SnapshotStreamSemantic semantic, int[] values, int maximumValue, bool allowRle, EncodingContext context)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            SnapshotCodec fixedCodec = SnapshotBinary.SelectUnsignedFixedCodec(maximumValue);
            if (context != null && context.Compression == SaveCompression.Fast)
            {
                return EncodeFastInteger(semantic, values, fixedCodec, allowRle, context);
            }

            byte[] best = SnapshotBinary.EncodeUnsignedFixed(values, fixedCodec);
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
                byte[] compressedDelta = Deflate(delta, context);
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
                    byte[] compressedRle = Deflate(rle, context);
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
            byte[] stored;
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
                    byte[] compressed = Deflate(stored, context);
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

        internal static Vector3[] DecodeVector3(EncodedStream stream)
        {
            byte[] bytes = DecodeFloatBytes(stream, 3, out bool shuffled);
            Vector3[] result = new Vector3[stream.ElementCount];
            for (int i = 0; i < result.Length; i++)
            {
                int offset = i * 3;
                result[i] = new Vector3(SnapshotBinary.ReadFloat32(bytes, offset, shuffled), SnapshotBinary.ReadFloat32(bytes, offset + 1, shuffled), SnapshotBinary.ReadFloat32(bytes, offset + 2, shuffled));
            }

            return result;
        }

        internal static Vector4[] DecodeVector4(EncodedStream stream)
        {
            byte[] bytes = DecodeFloatBytes(stream, 4, out bool shuffled);
            Vector4[] result = new Vector4[stream.ElementCount];
            for (int i = 0; i < result.Length; i++)
            {
                int offset = i * 4;
                result[i] = new Vector4(SnapshotBinary.ReadFloat32(bytes, offset, shuffled), SnapshotBinary.ReadFloat32(bytes, offset + 1, shuffled), SnapshotBinary.ReadFloat32(bytes, offset + 2, shuffled), SnapshotBinary.ReadFloat32(bytes, offset + 3, shuffled));
            }

            return result;
        }

        internal static Color32[] DecodeColors(EncodedStream stream)
        {
            if (stream.Codec == SnapshotCodec.UniformColor32)
            {
                if (stream.Payload.Length != 4)
                {
                    throw new InvalidDataException("Uniform Color32 payload must contain exactly four bytes.");
                }

                Color32[] result = new Color32[stream.ElementCount];
                Color32 color = new Color32(stream.Payload[0], stream.Payload[1], stream.Payload[2], stream.Payload[3]);
                for (int i = 0; i < result.Length; i++) result[i] = color;
                return result;
            }

            byte[] raw = DecodeRawOrDeflate(stream);
            if (raw.Length != checked(stream.ElementCount * 4))
            {
                throw new InvalidDataException("Color32 decoded length mismatch.");
            }

            Color32[] colors = new Color32[stream.ElementCount];
            int cursor = 0;
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32(raw[cursor++], raw[cursor++], raw[cursor++], raw[cursor++]);
            }

            return colors;
        }

        internal static int[] DecodeInteger(EncodedStream stream)
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

        internal static byte[] DecodeByteStream(EncodedStream stream)
        {
            return DecodeRawOrDeflate(stream);
        }

        private static byte[] DecodeFloatBytes(EncodedStream stream, int components, out bool shuffled)
        {
            shuffled = stream.Codec == SnapshotCodec.ByteShuffleDeflate;
            int expected = checked(stream.ElementCount * components * 4);
            if (stream.DecodedByteLength != expected)
            {
                throw new InvalidDataException($"Float stream decoded length {stream.DecodedByteLength} does not match expected {expected}.");
            }

            byte[] raw;
            switch (stream.Codec)
            {
                case SnapshotCodec.Raw:
                    raw = stream.Payload;
                    break;
                case SnapshotCodec.Deflate:
                    raw = SnapshotBinary.Inflate(stream.Payload, expected);
                    break;
                case SnapshotCodec.ByteShuffleDeflate:
                    raw = SnapshotBinary.Inflate(stream.Payload, expected);
                    break;
                default:
                    throw new InvalidDataException($"Codec {stream.Codec} is not valid for a float stream.");
            }

            if (raw.Length != expected) throw new InvalidDataException("Float stream length mismatch.");
            return raw;
        }

        private static byte[] DecodeRawOrDeflate(EncodedStream stream)
        {
            switch (stream.Codec)
            {
                case SnapshotCodec.Raw:
                    if (stream.Payload.Length != stream.DecodedByteLength)
                    {
                        throw new InvalidDataException("Raw stream length mismatch.");
                    }

                    return stream.Payload;
                case SnapshotCodec.Deflate:
                    return SnapshotBinary.Inflate(stream.Payload, stream.DecodedByteLength);
                default:
                    throw new InvalidDataException($"Codec {stream.Codec} is not valid for a raw byte stream.");
            }
        }

        private static byte[] InflateUnknownLength(byte[] compressed, int maximumDecodedBytes)
        {
            // Delta/RLE payload length is variable but it is still bounded from
            // the declared element count before any decompression can grow freely.
            using (MemoryStream input = new MemoryStream(compressed, writable: false))
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