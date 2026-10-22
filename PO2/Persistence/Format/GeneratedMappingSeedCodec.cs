using System.IO;
using System.Text;
using UnityEngine;
using PO2.Materials;

namespace PO2.Persistence.Format
{
    internal static class GeneratedMappingSeedCodec
    {
        internal static void Write(Stream stream, GeneratedMappingSeed seed)
        {
            stream.WriteByte(seed == null ? (byte)0 : (byte)1);
            if (seed == null) return;
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(seed.Charts.Length);
                writer.Write(seed.ReferenceMetric.Length);
                foreach (int value in seed.Corners) writer.Write(value);
                foreach (int value in seed.Charts) writer.Write(value);
                foreach (var value in seed.Coordinates)
                {
                    writer.Write(value.x);
                    writer.Write(value.y);
                }

                foreach (var value in seed.ReferenceMetric)
                {
                    writer.Write(value.x);
                    writer.Write(value.y);
                }
            }
        }

        internal static GeneratedMappingSeed Read(Stream stream)
        {
            int present = stream.ReadByte();
            if (present == 0) return null;
            if (present != 1) throw new InvalidDataException("Invalid mapping seed flag.");
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                int triangles = reader.ReadInt32(), count = reader.ReadInt32();
                // Validate the complete payload before allocating from file-provided counts.
                if (triangles <= 0 || triangles > SnapshotFormat.kMaxTrianglesPerPart || count <= 0 || count > triangles || 40L * triangles + 8L * count > stream.Length - stream.Position)
                    throw new InvalidDataException("Invalid mapping seed size.");
                var corners = new int[triangles * 3];
                var charts = new int[triangles];
                var coordinates = new Vector2[corners.Length];
                var metric = new Vector2[count];
                for (int i = 0; i < corners.Length; i++) corners[i] = reader.ReadInt32();
                for (int i = 0; i < charts.Length; i++) charts[i] = reader.ReadInt32();
                for (int i = 0; i < coordinates.Length; i++) coordinates[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                for (int i = 0; i < metric.Length; i++) metric[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                return new GeneratedMappingSeed(corners, charts, coordinates, metric);
            }
        }
    }
}