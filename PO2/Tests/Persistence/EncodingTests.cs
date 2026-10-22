using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PO2;
using UnityEngine;
using PO2.Geometry.Topology;
using PO2.Persistence;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

internal static partial class PersistenceTests {
    private static void EncodingProfiles() {
        // Exercise exact float bits, including values where float equality is insufficient.
        uint[] edgeBits = { 0, 0x80000000, 1, 0x80000001, 0x7f7fffff, 0xff7fffff,
            0x7f800000, 0xff800000, 0x7fc01234, 0xffc04321, 0x3f800000 };
        foreach (int count in new[] { 0, 1, 15, 16, 21, 22, 64, 513 }) {
            var vectors = new Vector4[count];
            for (int i = 0; i < count; i++) vectors[i] = new Vector4(
                Bits(edgeBits[i % edgeBits.Length]), Bits(edgeBits[(i + 1) % edgeBits.Length]),
                Bits(edgeBits[(i + 2) % edgeBits.Length]), Bits(edgeBits[(i + 3) % edgeBits.Length]));
            float[] flattened = vectors.SelectMany(v => new[] { v.x, v.y, v.z, v.w }).ToArray();
            byte[] raw = SnapshotBinary.EncodeFloat32(flattened);
            foreach (SaveCompression mode in Enum.GetValues(typeof(SaveCompression))) {
                var context = new EncodingContext(mode, new EncodingMetrics());
                var encoded = ObjectBlockCodec.EncodeVector4(SnapshotStreamSemantic.UV0, vectors, context);
                var decoded = ObjectBlockCodec.DecodeVector4(encoded);
                Equal(SnapshotBinary.EncodeFloat32(decoded.SelectMany(v => new[] { v.x, v.y, v.z, v.w }).ToArray()),
                    raw, "Exact Vector4 bits " + mode);
                var vector3 = vectors.Select(v => new Vector3(v.x, v.y, v.z)).ToArray();
                var decoded3 = ObjectBlockCodec.DecodeVector3(ObjectBlockCodec.EncodeVector3(
                    SnapshotStreamSemantic.Positions, vector3, context));
                Equal(SnapshotBinary.EncodeFloat32(decoded3.SelectMany(v => new[] { v.x, v.y, v.z }).ToArray()),
                    SnapshotBinary.EncodeFloat32(vector3.SelectMany(v => new[] { v.x, v.y, v.z }).ToArray()),
                    "Exact Vector3 bits " + mode);
                if (mode == SaveCompression.Compact) {
                    byte[] expected = raw;
                    if (raw.Length >= ObjectBlockCodec.kFloatCandidateThresholdBytes) {
                        byte[] deflate = SnapshotBinary.Deflate(raw);
                        if (deflate.Length < expected.Length) expected = deflate;
                        byte[] shuffle = SnapshotBinary.Deflate(SnapshotBinary.ShuffleFloatBytes(raw));
                        if (shuffle.Length < expected.Length) expected = shuffle;
                    }
                    Equal(encoded.Payload, expected, "Compact retains baseline candidate selection");
                }
            }
            // The decoder must also preserve bits from raw and ordinary Deflate files.
            foreach (var codec in new[] { SnapshotCodec.Raw, SnapshotCodec.Deflate, SnapshotCodec.ByteShuffleDeflate }) {
                byte[] payload = codec == SnapshotCodec.Raw ? raw : SnapshotBinary.Deflate(
                    codec == SnapshotCodec.ByteShuffleDeflate ? SnapshotBinary.ShuffleFloatBytes(raw) : raw);
                var encoded = new EncodedStream(SnapshotStreamSemantic.UV0, codec, count, raw.Length, payload);
                var decoded = ObjectBlockCodec.DecodeVector4(encoded);
                Equal(SnapshotBinary.EncodeFloat32(decoded.SelectMany(v => new[] { v.x, v.y, v.z, v.w }).ToArray()), raw, "Legacy float codec bits");
            }
        }

        var random = new Random(925);
        foreach (int maximum in new[] { 0, 255, 256, 65535, 65536, int.MaxValue }) {
            foreach (int count in new[] { 0, 1, 63, 256, 4096 }) {
                var values = Enumerable.Range(0, count).Select(i => i % 9 == 0 ? maximum : random.Next(Math.Max(1, maximum))).ToArray();
                foreach (SaveCompression mode in Enum.GetValues(typeof(SaveCompression))) {
                    var context = new EncodingContext(mode);
                    Check(values.SequenceEqual(ObjectBlockCodec.DecodeInteger(ObjectBlockCodec.EncodeMembership(values, maximum, context))), "Membership exact " + mode);
                    Check(values.SequenceEqual(ObjectBlockCodec.DecodeInteger(ObjectBlockCodec.EncodeStructuralFaceIds(values, context))), "Index exact " + mode);
                    Throws<InvalidDataException>(() => ObjectBlockCodec.EncodeStructuralFaceIds(new[] { -1 }, context), "Negative index rejected");
                    Throws<InvalidDataException>(() => ObjectBlockCodec.EncodeMembership(new[] { 256 }, 255, context), "Fixed range retained");
                }
            }
        }

        var objects = EncodingFixture.Build(6, 80);
        byte[] canonical = SnapshotWriter.Write(Save, Session, Metadata, objects);
        foreach (SaveCompression mode in Enum.GetValues(typeof(SaveCompression))) {
            byte[] serial = SnapshotWriter.Write(Save, Session, Metadata, objects, new SnapshotEncodingOptions(mode, 1));
            var metrics = new EncodingMetrics();
            byte[] parallel = SnapshotWriter.Write(Save, Session, Metadata, objects, new SnapshotEncodingOptions(mode, 4), metrics);
            Equal(parallel, serial, "Parallel part order and bytes " + mode);
            Check(metrics.PartCount == 6 && metrics.PeakWorkers == 4 && metrics.ParallelBatches == 2, "Bounded part batches");
            byte[] constrained = SnapshotWriter.Write(Save, Session, Metadata, objects, new SnapshotEncodingOptions(mode, 4, 1));
            Equal(constrained, serial, "Oversized part falls back to serial");
            var decoded = Read(parallel);
            Equal(SnapshotWriter.Write(Save, Session, Metadata, decoded.Objects), canonical, "All saved values match canonical round-trip " + mode);
            for (int i = 0; i < 6; i++) Check(decoded.Objects[0].Parts[i].Name == "Encoding part " + i, "Part order preserved");
            byte[] damaged = (byte[])parallel.Clone(); damaged[damaged.Length - 1] ^= 1;
            Check(Read(damaged).SkippedObjects.Count == 1, "Corrupted encoded block rejected " + mode);
        }
        EncodingWorkerFailure(objects);
        Throws<ArgumentOutOfRangeException>(() => new SnapshotEncodingOptions((SaveCompression)99), "Unknown profile rejected");
    }

    private static float Bits(uint value) => BitConverter.Int32BitsToSingle(unchecked((int)value));

    private static void EncodingWorkerFailure(PersistenceObjectData[] objects) {
        string root = Path.Combine(Path.GetTempPath(), "po2-encode-" + Guid.NewGuid().ToString("N"));
        try {
            var store = new SnapshotFileStore(root);
            string key = "00112233445566778899aabbccddeeff00";
            var options = new SnapshotEncodingOptions(SaveCompression.Fast, 4);
            var result = store.WriteTransactional(key, Save, Session, Metadata, objects, options);
            byte[] previous = File.ReadAllBytes(result.FinalPath);
            objects[0].Parts[1].Triangles[0] = int.MaxValue;
            Throws<InvalidDataException>(() => store.WriteTransactional(key, Save, Session, Metadata, objects, options), "Worker failure aborts save");
            Equal(File.ReadAllBytes(result.FinalPath), previous, "Worker failure preserves previous final");
            Check(!File.Exists(result.FinalPath + ".tmp"), "Worker failure before writing leaves no temp");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

}

internal static class EncodingFixture {
    internal static PersistenceObjectData[] Build(int partCount, int side) {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1-object-7.po2"));
        if (!SnapshotReader.TryRead(bytes, out var read, out var error)) throw new Exception(error);
        var template = read.Objects[0];
        var areas = template.Parts[0].SurfaceAreas;
        var parts = new PersistencePartData[partCount];
        for (int part = 0; part < partCount; part++) {
            int count = side * side;
            var positions = new Vector3[count]; var normals = new Vector3[count];
            var tangents = new Vector4[count]; var uv = new Vector4[count];
            var uv1 = new Vector4[count]; var uv3 = new Vector4[count];
            var colors = new Color32[count]; var logical = new int[count];
            for (int i = 0; i < count; i++) {
                float x = i % side * .71f, z = i / side * .83f;
                positions[i] = new Vector3(x + part * 150, (float)Math.Sin(x * .07) * (float)Math.Cos(z * .03), z);
                normals[i] = new Vector3((float)Math.Sin(x) * .1f, .99f, (float)Math.Cos(z) * .1f);
                tangents[i] = new Vector4(.99f, normals[i].x, normals[i].z, i % 2 == 0 ? 1 : -1);
                uv[i] = new Vector4(x * .2f, z * .2f, 0, 0);
                uv1[i] = new Vector4(x / side, z / side, part * .03f, 1);
                uv3[i] = new Vector4(i % 7 * .1f, i % 11 * .2f, 0, 1);
                colors[i] = new Color32((byte)(i % 255), 128, 255, 255); logical[i] = i;
            }
            int cells = (side - 1) * (side - 1);
            var triangles = new int[cells * 6]; var faces = new StructuralFace[cells];
            var memberships = new int[cells * 2];
            for (int y = 0, face = 0; y < side - 1; y++) for (int x = 0; x < side - 1; x++, face++) {
                int a = y * side + x, b = a + 1, c = a + side + 1, d = a + side;
                faces[face] = new StructuralFace(face, a, b, c, d);
                int t = face * 6; triangles[t] = a; triangles[t + 1] = b; triangles[t + 2] = c;
                triangles[t + 3] = a; triangles[t + 4] = c; triangles[t + 5] = d;
                memberships[face * 2] = memberships[face * 2 + 1] = areas[(face / 40) % areas.Length].Id;
            }
            parts[part] = PersistencePartData.FromDecoded("Encoding part " + part, positions, normals, tangents,
                uv, colors, uv1, uv3, triangles, logical, count, faces,
                template.Parts[0].StructuralTopologySource, areas, memberships, template.Parts[0].NextSurfaceAreaId, template.Parts[0].LocalPivot);
        }
        return new[] { PersistenceObjectData.FromDecoded(template.PersistentId, template.Name, template.IsLocked,
            template.ReceivesDecals, template.Transform, template.CustomOrientations, parts, template.LocalPivot) };
    }
}
