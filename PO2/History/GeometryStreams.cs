using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Transforms;

namespace PO2.History
{
    internal sealed class GeometryStreams
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] internal float Float;
            [FieldOffset(0)] internal int Bits;
        }

        internal readonly Vector3[] Positions;
        internal readonly Vector3[] Normals;
        internal readonly Vector4[] Tangents;

        internal GeometryStreams(Vector3[] positions, Vector3[] normals, Vector4[] tangents)
        {
            Positions = positions;
            Normals = normals;
            Tangents = tangents;
        }

        internal static GeometryStreams Capture(MeshPart part, bool positionsOnly) => new GeometryStreams((Vector3[])part.Mesh.Vertices.Clone(), !positionsOnly && part.Mesh.Normals != null ? (Vector3[])part.Mesh.Normals.Clone() : null, !positionsOnly && part.Mesh.Tangents != null ? (Vector4[])part.Mesh.Tangents.Clone() : null);
        internal long RawBytes => 12L * (Positions.Length + (Normals?.Length ?? 0)) + 16L * (Tangents?.Length ?? 0);

        internal GeometryStreams Transform(Matrix4x4 matrix, bool positionsOnly)
        {
            var result = new GeometryStreams(new Vector3[Positions.Length], Normals == null ? null : new Vector3[Normals.Length], Tangents == null ? null : new Vector4[Tangents.Length]);
            if (!PartGeometryTransform.TryHistoryTransform(Positions, Normals, Tangents, result.Positions, result.Normals, result.Tangents, matrix, positionsOnly, out string reason)) throw new InvalidOperationException(reason);
            return result;
        }

        internal void Publish(MeshPart part)
        {
            Array.Copy(Positions, part.Mesh.Vertices, Positions.Length);
            if (Normals != null) Array.Copy(Normals, part.Mesh.Normals, Normals.Length);
            if (Tangents != null) Array.Copy(Tangents, part.Mesh.Tangents, Tangents.Length);
        }

        internal ulong Hash()
        {
            var hash = HistoryHash.Start;
            foreach (var value in Positions) hash.Add(value);
            if (Normals != null)
                foreach (var value in Normals)
                    hash.Add(value);
            if (Tangents != null)
                foreach (var value in Tangents)
                {
                    hash.Add(value.x);
                    hash.Add(value.y);
                    hash.Add(value.z);
                    hash.Add(value.w);
                }

            return hash.Value;
        }

        internal byte[] CreateRawXor(GeometryStreams final, GeometryStreams live, out ulong finalHash, out bool changed)
        {
            return CreateRawXor(final, live, out _, out finalHash, out changed);
        }

        internal byte[] CreateRawXor(GeometryStreams final, out ulong startHash, out ulong finalHash, out bool changed)
        {
            return CreateRawXor(final, live: null, out startHash, out finalHash, out changed);
        }

        internal byte[] CreateRawXor(GeometryStreams final, GeometryStreams live, out ulong startHash, out ulong finalHash, out bool changed)
        {
            if (!HasSameShape(final) || (live != null && !HasSameShape(live)) || RawBytes > int.MaxValue)
            {
                throw new InvalidOperationException("Geometry delta stream layouts differ.");
            }

            byte[] delta = new byte[checked((int)RawBytes)];
            int offset = 0;
            changed = false;
            var start = HistoryHash.Start;
            var finalValue = HistoryHash.Start;

            WriteVector3Delta(Positions, final.Positions, live?.Positions, delta, ref offset, ref start, ref finalValue, ref changed);

            if (Normals != null)
            {
                WriteVector3Delta(Normals, final.Normals, live?.Normals, delta, ref offset, ref start, ref finalValue, ref changed);
            }

            if (Tangents != null)
            {
                WriteVector4Delta(Tangents, final.Tangents, live?.Tangents, delta, ref offset, ref start, ref finalValue, ref changed);
            }

            if (offset != delta.Length)
            {
                throw new InvalidOperationException("Geometry delta length did not match its layout.");
            }

            startHash = start.Value;
            finalHash = finalValue.Value;
            return delta;
        }

        internal GeometryStreams ApplyRawXor(byte[] delta, out ulong currentHash, out ulong resultHash)
        {
            if (delta == null || delta.LongLength != RawBytes)
            {
                throw new InvalidDataException("Geometry delta length does not match its layout.");
            }

            var result = new GeometryStreams(new Vector3[Positions.Length], Normals == null ? null : new Vector3[Normals.Length], Tangents == null ? null : new Vector4[Tangents.Length]);
            int offset = 0;
            var current = HistoryHash.Start;
            var restored = HistoryHash.Start;

            ReadVector3Delta(Positions, result.Positions, delta, ref offset, ref current, ref restored);

            if (Normals != null)
            {
                ReadVector3Delta(Normals, result.Normals, delta, ref offset, ref current, ref restored);
            }

            if (Tangents != null)
            {
                ReadVector4Delta(Tangents, result.Tangents, delta, ref offset, ref current, ref restored);
            }

            currentHash = current.Value;
            resultHash = restored.Value;
            return result;
        }

        internal static byte[] CompressRawXor(byte[] raw)
        {
            if (raw == null || raw.Length % 4 != 0)
            {
                throw new InvalidDataException("Geometry delta is not a complete float stream.");
            }

            byte[] shuffled = new byte[raw.Length];
            int words = raw.Length / 4;

            for (int lane = 0; lane < 4; lane++)
            {
                for (int i = 0; i < words; i++)
                {
                    shuffled[lane * words + i] = raw[i * 4 + lane];
                }
            }

            using (var output = new MemoryStream())
            {
                using (var zip = new DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    zip.Write(shuffled, 0, shuffled.Length);
                }

                return output.ToArray();
            }
        }

        internal static byte[] DecompressRawXor(byte[] encoded, int rawLength)
        {
            if (encoded == null || rawLength < 0 || rawLength % 4 != 0)
            {
                throw new InvalidDataException("Geometry delta metadata is invalid.");
            }

            byte[] shuffled = new byte[rawLength];
            using (var input = new MemoryStream(encoded, false))
            using (var zip = new DeflateStream(input, CompressionMode.Decompress))
            {
                int offset = 0;
                while (offset < shuffled.Length)
                {
                    int count = zip.Read(shuffled, offset, shuffled.Length - offset);
                    if (count == 0)
                    {
                        throw new InvalidDataException("Truncated geometry delta payload.");
                    }

                    offset += count;
                }

                if (zip.ReadByte() != -1)
                {
                    throw new InvalidDataException("Oversized geometry delta payload.");
                }
            }

            byte[] raw = new byte[rawLength];
            int words = rawLength / 4;

            for (int lane = 0; lane < 4; lane++)
            {
                for (int i = 0; i < words; i++)
                {
                    raw[i * 4 + lane] = shuffled[lane * words + i];
                }
            }

            return raw;
        }

        private bool HasSameShape(GeometryStreams other)
        {
            return other != null && Positions.Length == other.Positions.Length && SameLength(Normals, other.Normals) && SameLength(Tangents, other.Tangents);
        }

        private static bool SameLength<T>(T[] left, T[] right)
        {
            if (left == null || right == null)
            {
                return left == null && right == null;
            }

            return left.Length == right.Length;
        }

        private static void WriteVector3Delta(Vector3[] start, Vector3[] final, Vector3[] live, byte[] delta, ref int offset, ref HistoryHash startHash, ref HistoryHash finalHash, ref bool changed)
        {
            for (int i = 0; i < start.Length; i++)
            {
                WriteFloatDelta(start[i].x, final[i].x, live != null ? live[i].x : final[i].x, delta, ref offset, ref startHash, ref finalHash, ref changed);
                WriteFloatDelta(start[i].y, final[i].y, live != null ? live[i].y : final[i].y, delta, ref offset, ref startHash, ref finalHash, ref changed);
                WriteFloatDelta(start[i].z, final[i].z, live != null ? live[i].z : final[i].z, delta, ref offset, ref startHash, ref finalHash, ref changed);
            }
        }

        private static void WriteVector4Delta(Vector4[] start, Vector4[] final, Vector4[] live, byte[] delta, ref int offset, ref HistoryHash startHash, ref HistoryHash finalHash, ref bool changed)
        {
            for (int i = 0; i < start.Length; i++)
            {
                WriteFloatDelta(start[i].x, final[i].x, live != null ? live[i].x : final[i].x, delta, ref offset, ref startHash, ref finalHash, ref changed);
                WriteFloatDelta(start[i].y, final[i].y, live != null ? live[i].y : final[i].y, delta, ref offset, ref startHash, ref finalHash, ref changed);
                WriteFloatDelta(start[i].z, final[i].z, live != null ? live[i].z : final[i].z, delta, ref offset, ref startHash, ref finalHash, ref changed);
                WriteFloatDelta(start[i].w, final[i].w, live != null ? live[i].w : final[i].w, delta, ref offset, ref startHash, ref finalHash, ref changed);
            }
        }

        private static void WriteFloatDelta(float start, float final, float live, byte[] delta, ref int offset, ref HistoryHash startHash, ref HistoryHash finalHash, ref bool changed)
        {
            int finalBits = ToBits(final);
            if (ToBits(live) != finalBits)
            {
                throw new InvalidOperationException("Live geometry does not match the completed preview.");
            }

            int value = ToBits(start) ^ finalBits;
            delta[offset++] = unchecked((byte)value);
            delta[offset++] = unchecked((byte)(value >> 8));
            delta[offset++] = unchecked((byte)(value >> 16));
            delta[offset++] = unchecked((byte)(value >> 24));
            changed |= value != 0;
            startHash.Add(start);
            finalHash.Add(final);
        }

        private static void ReadVector3Delta(Vector3[] source, Vector3[] destination, byte[] delta, ref int offset, ref HistoryHash sourceHash, ref HistoryHash destinationHash)
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = new Vector3(ReadFloatDelta(source[i].x, delta, ref offset, ref sourceHash, ref destinationHash), ReadFloatDelta(source[i].y, delta, ref offset, ref sourceHash, ref destinationHash), ReadFloatDelta(source[i].z, delta, ref offset, ref sourceHash, ref destinationHash));
            }
        }

        private static void ReadVector4Delta(Vector4[] source, Vector4[] destination, byte[] delta, ref int offset, ref HistoryHash sourceHash, ref HistoryHash destinationHash)
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = new Vector4(ReadFloatDelta(source[i].x, delta, ref offset, ref sourceHash, ref destinationHash), ReadFloatDelta(source[i].y, delta, ref offset, ref sourceHash, ref destinationHash), ReadFloatDelta(source[i].z, delta, ref offset, ref sourceHash, ref destinationHash), ReadFloatDelta(source[i].w, delta, ref offset, ref sourceHash, ref destinationHash));
            }
        }

        private static float ReadFloatDelta(float source, byte[] delta, ref int offset, ref HistoryHash sourceHash, ref HistoryHash destinationHash)
        {
            int value = delta[offset] | delta[offset + 1] << 8 | delta[offset + 2] << 16 | delta[offset + 3] << 24;
            offset += 4;

            int resultBits = ToBits(source) ^ value;
            float result = FromBits(resultBits);
            sourceHash.Add(source);
            destinationHash.Add(result);
            return result;
        }

        private static int ToBits(float value)
        {
            return new FloatBits { Float = value }.Bits;
        }

        private static float FromBits(int value)
        {
            return new FloatBits { Bits = value }.Float;
        }

        private byte[] Bytes()
        {
            using (var stream = new MemoryStream(checked((int)RawBytes)))
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    foreach (var value in Positions)
                    {
                        writer.Write(value.x);
                        writer.Write(value.y);
                        writer.Write(value.z);
                    }

                    if (Normals != null)
                        foreach (var value in Normals)
                        {
                            writer.Write(value.x);
                            writer.Write(value.y);
                            writer.Write(value.z);
                        }

                    if (Tangents != null)
                        foreach (var value in Tangents)
                        {
                            writer.Write(value.x);
                            writer.Write(value.y);
                            writer.Write(value.z);
                            writer.Write(value.w);
                        }
                }

                return stream.ToArray();
            }
        }

        // Byte planes put the mostly-zero high bits together. XOR against the
        // inverse result preserves even the low float bits without float math.
        internal byte[] Encode(GeometryStreams predicted = null)
        {
            byte[] bytes = Bytes();
            if (predicted != null)
            {
                byte[] other = predicted.Bytes();
                if (other.Length != bytes.Length) throw new InvalidOperationException("Correction stream lengths differ.");
                for (int i = 0; i < bytes.Length; i++) bytes[i] ^= other[i];
            }

            var shuffled = new byte[bytes.Length];
            int count = bytes.Length / 4;
            for (int lane = 0; lane < 4; lane++)
            for (int i = 0; i < count; i++)
                shuffled[lane * count + i] = bytes[i * 4 + lane];
            using (var output = new MemoryStream())
            {
                using (var zip = new DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true)) zip.Write(shuffled, 0, shuffled.Length);
                return output.ToArray();
            }
        }

        internal GeometryStreams Decode(byte[] encoded, bool correction)
        {
            var shuffled = new byte[checked((int)RawBytes)];
            using (var input = new MemoryStream(encoded, false))
            using (var zip = new DeflateStream(input, CompressionMode.Decompress))
            {
                int offset = 0;
                while (offset < shuffled.Length)
                {
                    int count = zip.Read(shuffled, offset, shuffled.Length - offset);
                    if (count == 0) throw new InvalidDataException("Truncated geometry payload.");
                    offset += count;
                }

                if (zip.ReadByte() != -1) throw new InvalidDataException("Oversized geometry payload.");
            }

            byte[] bytes = correction ? Bytes() : new byte[shuffled.Length];
            int words = bytes.Length / 4;
            for (int lane = 0; lane < 4; lane++)
            for (int i = 0; i < words; i++)
                bytes[i * 4 + lane] ^= shuffled[lane * words + i];
            var result = new GeometryStreams(new Vector3[Positions.Length], Normals == null ? null : new Vector3[Normals.Length], Tangents == null ? null : new Vector4[Tangents.Length]);
            using (var input = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(input))
            {
                for (int i = 0; i < result.Positions.Length; i++) result.Positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                if (result.Normals != null)
                    for (int i = 0; i < result.Normals.Length; i++)
                        result.Normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                if (result.Tangents != null)
                    for (int i = 0; i < result.Tangents.Length; i++)
                        result.Tangents[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            return result;
        }
    }
}