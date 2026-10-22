using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

namespace PO2.History
{
    // Raw canonical streams are temporary capture/replay work, never retained
    // by a sealed action. Keeping streams separate makes vertex compaction cheap.
    internal sealed class TopologyState
    {
        internal static readonly int[] s_Widths = { 12, 12, 16, 16, 4, 16, 4, 4, 24, 4, 1, 16 };
        internal readonly byte[][] Streams;

        internal TopologyState(byte[][] streams)
        {
            Streams = streams;
        }

        internal long Bytes
        {
            get
            {
                long size = 0;
                foreach (var stream in Streams) size += stream?.LongLength ?? 0;
                return size;
            }
        }

        private static byte[] Write(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) write(writer);
                return stream.ToArray();
            }
        }

        private static byte[] Pack<T>(T[] values, Action<BinaryWriter, T> write) => values == null
            ? null
            : Write(w =>
            {
                foreach (var value in values) write(w, value);
            });

        private static void V3(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        private static void V4(BinaryWriter w, Vector4 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
            w.Write(v.w);
        }

        internal static TopologyState Capture(MeshPart part)
        {
            return Capture(PersistencePartData.Capture(part));
        }

        internal static TopologyState Capture(PersistencePartData data)
        {
            return new TopologyState(new[]
            {
                Pack(data.Vertices, V3), Pack(data.Normals, V3), Pack(data.Tangents, V4), Pack(data.UV0, V4),
                Pack(data.VertexColors, (w, v) =>
                {
                    w.Write(v.r);
                    w.Write(v.g);
                    w.Write(v.b);
                    w.Write(v.a);
                }),
                Pack(data.TexCoord3, V4), Pack(data.Triangles, (w, v) => w.Write(v)),
                Pack(data.RenderToLogical, (w, v) => w.Write(v)),
                Pack(data.StructuralFaces, (w, v) =>
                {
                    w.Write(v.Id);
                    w.Write(v.CornerCount);
                    for (int i = 0; i < 4; i++) w.Write(i < v.CornerCount ? v.GetRenderVertexIndex(i) : -1);
                }),
                Pack(data.TriangleSurfaceAreaIds, (w, v) => w.Write(v)),
                Write(w =>
                {
                    w.Write(data.LogicalVertexCount);
                    ObjectBlockWriter.WritePartMetadata(w.BaseStream, data);
                }),
                Pack(data.TexCoord1, V4)
            });
        }

        private T[] Read<T>(int index, Func<BinaryReader, T> read)
        {
            var bytes = Streams[index];
            if (bytes == null) return null;
            if (bytes.Length % s_Widths[index] != 0) throw new InvalidDataException("Invalid topology stream size.");
            var values = new T[bytes.Length / s_Widths[index]];
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
                for (int i = 0; i < values.Length; i++)
                    values[i] = read(reader);
            return values;
        }

        private static Vector3 V3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        private static Vector4 V4(BinaryReader r) => new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        internal MeshPart Restore()
        {
            int logicalCount;
            ObjectBlockReader.PartMetadata metadata;
            using (var stream = new MemoryStream(Streams[10], false))
            using (var reader = new BinaryReader(stream))
            {
                logicalCount = reader.ReadInt32();
                metadata = ObjectBlockReader.ReadPartMetadata(stream, SnapshotFormat.kObjectSchemaVersion);
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing topology metadata.");
            }

            var faces = Read(8, r =>
            {
                int id = r.ReadInt32(), count = r.ReadInt32();
                int a = r.ReadInt32(), b = r.ReadInt32(), c = r.ReadInt32(), d = r.ReadInt32();
                if (count == 3 && d == -1) return new StructuralFace(id, a, b, c);
                if (count == 4) return new StructuralFace(id, a, b, c, d);
                throw new InvalidDataException("Invalid history face.");
            });
            return PersistencePartData.FromDecoded(metadata.Name, Read(0, V3), Read(1, V3), Read(2, V4), Read(3, V4), Read(4, r => new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte())), Read(11, V4), Read(5, V4), Read(6, r => r.ReadInt32()), Read(7, r => r.ReadInt32()), logicalCount, faces, metadata.StructuralTopologySource, metadata.SurfaceAreas, Read(9, r => r.ReadInt32()), metadata.NextSurfaceAreaId, metadata.LocalPivot).Restore();
        }

        internal byte[] Signature()
        {
            using (var sha = SHA256.Create())
            {
                foreach (var stream in Streams)
                {
                    var length = BitConverter.GetBytes(stream == null ? -1 : stream.Length);
                    sha.TransformBlock(length, 0, length.Length, length, 0);
                    if (stream != null && stream.Length != 0) sha.TransformBlock(stream, 0, stream.Length, null, 0);
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return sha.Hash;
            }
        }

        internal static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}