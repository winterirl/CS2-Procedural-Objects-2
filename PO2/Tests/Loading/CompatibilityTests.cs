using System;
using System.IO;
using System.Linq;
using PO2.Core;
using PO2.Geometry.Topology;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using UnityEngine;

internal static partial class LoadingTests
{
    private static readonly Guid FixtureSave = new Guid("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid FixtureSession = new Guid("10213243-5465-7687-98a9-bacbdcedfe0f");

    private static PersistenceObjectData StableFixture(int id, ushort schema, bool large = false)
    {
        var source = Fixture(schema);
        var parts = source.Parts;
        if (large)
        {
            var p = parts[0];
            const int count = 4099;
            var vertices = Enumerable.Range(0, count).Select(i => new Vector3(i * 0.125f, i % 7, (i % 13) * 0.1f)).ToArray();
            var triangles = Enumerable.Range(0, 4098).ToArray();
            var faces = Enumerable.Range(0, triangles.Length / 3).Select(i => new StructuralFace(i, i * 3, i * 3 + 1, i * 3 + 2)).ToArray();
            var uv = Enumerable.Range(0, count).Select(i => new Vector4(i * 0.01f, i % 3, 0.3f, -0.2f)).ToArray();
            parts = new[] { PersistencePartData.FromDecoded("large", vertices, Enumerable.Repeat(Vector3.up, count).ToArray(),
                Enumerable.Repeat(new Vector4(1, 0, 0, 1), count).ToArray(), uv,
                Enumerable.Range(0, count).Select(i => new Color32((byte)i, 2, 3, 4)).ToArray(),
                schema >= SnapshotFormat.kTexCoord1ObjectSchemaVersion ? uv : null, uv, triangles,
                Enumerable.Range(0, count).ToArray(), count, faces, StructuralTopologySource.Authored,
                p.SurfaceAreas, new int[faces.Length], 1, new Vector3(2, 3, 4)) };
        }
        return PersistenceObjectData.FromDecoded(new Guid(id, 1, 2, new byte[8]), source.Name, source.IsLocked, source.ReceivesDecals,
            source.Transform, source.CustomOrientations, parts, source.LocalPivot, source.LodProfile);
    }

    private static void Compatibility()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pass1");
        void Match(string name, byte[] bytes)
        {
            string path = Path.Combine(root, name);
            Check(bytes.SequenceEqual(File.ReadAllBytes(path)), "pass-1 bytes: " + name);
        }
        foreach (var mode in new[] { SaveCompression.Compact, SaveCompression.Fast })
        {
            for (ushort schema = 2; schema <= SnapshotFormat.kObjectSchemaVersion; schema++)
                Match($"schema-{schema}-{mode}.bin", ObjectBlockWriter.WriteForSchema(StableFixture(1, schema, true), schema, new SnapshotEncodingOptions(mode, 1)));
            var objects = Enumerable.Range(1, 7).Select(i => StableFixture(i, SnapshotFormat.kObjectSchemaVersion, i % 2 != 0)).ToArray();
            var metadata = new SnapshotMetadata("city", "autosave", "map", null, SnapshotSaveKind.AutoSave, null);
            byte[] serial = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, objects, new SnapshotEncodingOptions(mode, 1));
            Match($"container-{mode}.bin", serial);
            foreach (int workers in new[] { 1, 4, 8, 16, 32 })
                Check(serial.SequenceEqual(SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, objects, new SnapshotEncodingOptions(mode, workers))), "worker count byte equivalence");
        }
    }
}
