using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PO2.Persistence.Format
{
    internal static unsafe class FloatStreamPacking
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct Bits
        {
            [FieldOffset(0)] internal float Float;
            [FieldOffset(0)] internal uint UInt;
        }

        private static readonly bool s_Vector3Layout = Layout(typeof(Vector3), sizeof(Vector3), 12, false);
        private static readonly bool s_Vector4Layout = Layout(typeof(Vector4), sizeof(Vector4), 16, true);

        private static bool Layout(Type type, int size, int expected, bool four)
        {
            try
            {
                return BitConverter.IsLittleEndian && size == expected &&
                    Marshal.OffsetOf(type, "x").ToInt32() == 0 && Marshal.OffsetOf(type, "y").ToInt32() == 4 &&
                    Marshal.OffsetOf(type, "z").ToInt32() == 8 && (!four || Marshal.OffsetOf(type, "w").ToInt32() == 12);
            }
            catch (ArgumentException) { return false; }
        }

        internal static byte[] Pack(Vector3[] values, bool shuffled, bool allowBulk = true)
        {
            var bytes = new byte[checked(values.Length * 12)];
            if (bytes.Length == 0) return bytes;
            if (allowBulk && s_Vector3Layout)
            {
                fixed (Vector3* source = values)
                fixed (byte* target = bytes)
                {
                    if (!shuffled) Buffer.MemoryCopy(source, target, bytes.Length, bytes.Length);
                    else PackPlanes((uint*)source, bytes, values.Length * 3);
                }
            }
            else
            {
                int stride = shuffled ? values.Length * 3 : 0;
                for (int i = 0; i < values.Length; i++)
                {
                    Write(bytes, i * 3, stride, values[i].x);
                    Write(bytes, i * 3 + 1, stride, values[i].y);
                    Write(bytes, i * 3 + 2, stride, values[i].z);
                }
            }
            return bytes;
        }

        internal static byte[] Pack(Vector4[] values, bool shuffled, bool allowBulk = true)
        {
            var bytes = new byte[checked(values.Length * 16)];
            if (bytes.Length == 0) return bytes;
            if (allowBulk && s_Vector4Layout)
            {
                fixed (Vector4* source = values)
                fixed (byte* target = bytes)
                {
                    if (!shuffled) Buffer.MemoryCopy(source, target, bytes.Length, bytes.Length);
                    else PackPlanes((uint*)source, bytes, values.Length * 4);
                }
            }
            else
            {
                int stride = shuffled ? values.Length * 4 : 0;
                for (int i = 0; i < values.Length; i++)
                {
                    Write(bytes, i * 4, stride, values[i].x);
                    Write(bytes, i * 4 + 1, stride, values[i].y);
                    Write(bytes, i * 4 + 2, stride, values[i].z);
                    Write(bytes, i * 4 + 3, stride, values[i].w);
                }
            }
            return bytes;
        }

        private static void PackPlanes(uint* source, byte[] bytes, int count)
        {
            int plane2 = count * 2, plane3 = count * 3;
            for (int i = 0; i < count; i++)
            {
                uint value = source[i];
                bytes[i] = (byte)value; bytes[count + i] = (byte)(value >> 8);
                bytes[plane2 + i] = (byte)(value >> 16); bytes[plane3 + i] = (byte)(value >> 24);
            }
        }

        private static void Write(byte[] bytes, int word, int stride, float value)
        {
            uint bits = new Bits { Float = value }.UInt;
            int offset = stride == 0 ? word * 4 : word;
            int step = stride == 0 ? 1 : stride;
            bytes[offset] = (byte)bits; bytes[offset + step] = (byte)(bits >> 8);
            bytes[offset + step * 2] = (byte)(bits >> 16); bytes[offset + step * 3] = (byte)(bits >> 24);
        }

        internal static Vector3[] Read3(ArraySegment<byte> bytes, int count, bool shuffled, bool allowBulk = true)
        {
            Validate(bytes, count, 12);
            var values = new Vector3[count];
            if (count == 0) return values;
            if (allowBulk && s_Vector3Layout)
            {
                fixed (Vector3* target = values) CopyToVectors(bytes, (uint*)target, shuffled);
            }
            else for (int i = 0; i < count; i++) values[i] = new Vector3(
                SnapshotBinary.ReadFloat32(bytes, i * 3, shuffled), SnapshotBinary.ReadFloat32(bytes, i * 3 + 1, shuffled), SnapshotBinary.ReadFloat32(bytes, i * 3 + 2, shuffled));
            return values;
        }

        internal static Vector4[] Read4(ArraySegment<byte> bytes, int count, bool shuffled, bool allowBulk = true)
        {
            Validate(bytes, count, 16);
            var values = new Vector4[count];
            if (count == 0) return values;
            if (allowBulk && s_Vector4Layout)
            {
                fixed (Vector4* target = values) CopyToVectors(bytes, (uint*)target, shuffled);
            }
            else for (int i = 0; i < count; i++) values[i] = new Vector4(
                SnapshotBinary.ReadFloat32(bytes, i * 4, shuffled), SnapshotBinary.ReadFloat32(bytes, i * 4 + 1, shuffled),
                SnapshotBinary.ReadFloat32(bytes, i * 4 + 2, shuffled), SnapshotBinary.ReadFloat32(bytes, i * 4 + 3, shuffled));
            return values;
        }

        private static void Validate(ArraySegment<byte> bytes, int count, int stride)
        {
            if (bytes.Array == null || count < 0 || bytes.Count != checked(count * stride))
                throw new InvalidDataException("Invalid vector byte layout.");
        }

        private static void CopyToVectors(ArraySegment<byte> bytes, uint* target, bool shuffled)
        {
            fixed (byte* source = bytes.Array)
            {
                byte* start = source + bytes.Offset;
                if (!shuffled) Buffer.MemoryCopy(start, target, bytes.Count, bytes.Count);
                else
                {
                    int words = bytes.Count / 4;
                    byte* second = start + words;
                    byte* third = second + words;
                    byte* fourth = third + words;
                    for (int i = 0; i < words; i++)
                        target[i] = (uint)(start[i] | second[i] << 8 | third[i] << 16 | fourth[i] << 24);
                }
            }
        }

        // Reproduce the existing raw-byte sample offsets, including offsets
        // inside a vector. The sample is small; full arrays are packed once.
        internal static byte[] Sample(Vector3[] values) => Sample(checked(values.Length * 12), word =>
        {
            var value = values[word / 3];
            return word % 3 == 0 ? value.x : word % 3 == 1 ? value.y : value.z;
        });
        internal static byte[] Sample(Vector4[] values) => Sample(checked(values.Length * 16), word =>
        {
            var value = values[word / 4];
            switch (word % 4) { case 0: return value.x; case 1: return value.y; case 2: return value.z; default: return value.w; }
        });

        private static byte[] Sample(int length, Func<int, float> component)
        {
            const int chunk = 1024;
            if (length < 16 * 1024) throw new ArgumentOutOfRangeException(nameof(length));
            var result = new byte[chunk * 3];
            int[] starts = { 0, ((length - chunk) / 2) & ~3, length - chunk };
            for (int part = 0; part < 3; part++)
                for (int offset = 0; offset < chunk; offset += 4)
                    SnapshotBinary.WriteFloat32(result, part * chunk + offset, component((starts[part] + offset) / 4));
            return result;
        }
    }
}
