using System;
using System.IO;
using System.Linq;
using PO2.Core.Deformation;
using PO2.Persistence.Format;
using UnityEngine;

internal static partial class LoadingTests
{
    private static void CageCodecChecks()
    {
        foreach (bool large in new[] { false, true })
        foreach (int scope in new[] { -1, 0 })
        foreach (var mode in new[] { SaveCompression.Compact, SaveCompression.Fast })
        {
            var obj = StableFixture(23, SnapshotFormat.kObjectSchemaVersion, large);
            var reference = Matrix4x4.identity;
            reference.m00 = 600; reference.m11 = 8; reference.m22 = 2;
            var baselines = Enumerable.Range(0, scope < 0 ? obj.Parts.Length : 1).Select(i =>
                new DeformationBaseline(i, Guid.Empty, (Vector3[])obj.Parts[i].Vertices.Clone(),
                    (Vector3[])obj.Parts[i].Normals.Clone(), (Vector4[])obj.Parts[i].Tangents.Clone())).ToArray();
            var corners = DeformationCage.ReferenceCorners(reference);
            // A real nonidentity deformation, with an affine reference containing
            // every baseline vertex. The actual production recipe validates it.
            for (int i = 0; i < corners.Length; i++) corners[i] += new Vector3(2, 3, 4);
            var cage = new DeformationCage(new Guid("84f9cb5b-0259-4113-aabd-b82864b12356"), scope, reference, corners, baselines);
            Check(DeformationRecipe.TryCreate(reference, corners, out var recipe, out _), "valid cage recipe");
            foreach (var baseline in baselines)
            for (int i = 0; i < baseline.Positions.Length; i++) obj.Parts[baseline.PartIndex].Vertices[i] = recipe.Position(baseline.Positions[i]);
            obj.Deformations = new[] { cage };
            DeformationCodec.Validate(obj.Deformations, obj.Parts);
            byte[] Encode(bool previous)
            {
                using (var output = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, true))
                    {
                        if (previous) ReferenceDeformationCodec.Write(writer, cage, new EncodingContext(mode));
                        else DeformationCodec.Write(writer, cage, new EncodingContext(mode));
                    }
                    return output.ToArray();
                }
            }
            Check(Encode(false).SequenceEqual(Encode(true)), "cage bytes match frozen encoder");
            var metadata = new PO2.Persistence.Model.SnapshotMetadata("city", "cage", "map", null, PO2.Persistence.Model.SnapshotSaveKind.Manual, null);
            var bytes = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, new[] { obj }, new SnapshotEncodingOptions(mode, 4));
            var restored = SnapshotReader.Read(new MemoryStream(bytes), true).Objects.Single();
            Check(restored.Deformations.Single().Id == cage.Id, "cage identity round trip");
            var loaded = restored.Deformations[0];
            Check(loaded.PartIndex == scope && loaded.Parts.Length == baselines.Length, "cage scope round trip");
            foreach (var baseline in loaded.Parts)
            {
                Check(FloatStreamPacking.Pack(baseline.Positions, false).SequenceEqual(FloatStreamPacking.Pack(baselines[baseline.PartIndex].Positions, false)), "baseline positions exact");
                Check(!ReferenceEquals(restored.Parts[baseline.PartIndex].Vertices, baseline.Positions), "live/baseline independent");
            }
            Check(SnapshotReader.Verify(new MemoryStream(bytes), FixtureSave, FixtureSession, metadata, new[] { obj.PersistentId }).ObjectCount == 1, "cage verified through production decoder");
            obj.Parts[0].Vertices[0] = new Vector3(9999, 0, 0);
            Throws<InvalidDataException>(() => SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, new[] { obj }, new SnapshotEncodingOptions(mode, 4)));
        }
    }
}
