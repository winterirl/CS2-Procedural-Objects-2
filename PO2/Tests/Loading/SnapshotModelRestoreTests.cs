using System;
using System.IO;
using System.Linq;
using PO2.Core.Deformation;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using UnityEngine;

internal static partial class LoadingTests
{
    private static void SnapshotModelRestoreChecks()
    {
        var fixtures = Enumerable.Range(1, 12).Select(i => StableFixture(i, SnapshotFormat.kObjectSchemaVersion)).ToArray();
        var metadata = new SnapshotMetadata("city", "restore", "map", null, SnapshotSaveKind.AutoSave, null);
        var bytes = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, fixtures);
        var read = SnapshotReader.Read(new MemoryStream(bytes), true);
        var decoded = read.Objects.ToArray();
        // Add a detached cage so the real DTO path must rebind its part history ID.
        var part = decoded[0].Parts[0];
        var baseline = new DeformationBaseline(0, Guid.Empty, (Vector3[])part.Vertices.Clone(),
            (Vector3[])part.Normals.Clone(), (Vector4[])part.Tangents.Clone());
        decoded[0].Deformations = new[] { new DeformationCage(Guid.NewGuid(), 0, Matrix4x4.identity,
            DeformationCage.ReferenceCorners(Matrix4x4.identity), new[] { baseline }) };
        var expected = decoded.Select(o => ObjectBlockWriter.Write(o)).ToArray();
        var owned = read.TakeObjectsForRestore();
        var restored = SnapshotModelRestorer.Restore(owned);
        Check(restored.Count == fixtures.Length, "owned staging object count");
        for (int i = 0; i < restored.Count; i++)
        {
            var obj = restored[i];
            Check(obj.Id == i + 1 && obj.PersistentId == fixtures[i].PersistentId, "owned staging saved order and fresh IDs");
            Throws<InvalidOperationException>(() => owned.Peek(i));
            for (int j = 0; j < obj.Parts.Count; j++)
            {
                Check(ReferenceEquals(obj.Parts[j].Mesh.Vertices, decoded[i].Parts[j].Vertices), "owned mesh transfer");
                Same(obj.Parts[j].TriangleSurfaceAreaIds, decoded[i].Parts[j].TriangleSurfaceAreaIds, "owned memberships");
            }
            Check(ObjectBlockWriter.Write(PersistenceObjectData.Capture(obj)).SequenceEqual(expected[i]), "owned staging capture preserves persisted values");
        }
        var cage = restored[0].Deformations[0];
        Check(ReferenceEquals(cage.Parts[0].Positions, baseline.Positions), "owned cage transfer");
        Check(!ReferenceEquals(cage.Parts[0].Positions, restored[0].Parts[0].Mesh.Vertices), "cage baseline separate from live mesh");
        Check(cage.Matches(restored[0]), "cage rebound to restored part");
        var copy = decoded[1].Restore(77);
        Check(!ReferenceEquals(copy.Parts[0].Mesh.Vertices, decoded[1].Parts[0].Vertices), "reusable DTO still clones");
        var roundTrip = SnapshotReader.Read(new MemoryStream(SnapshotWriter.Write(FixtureSave, FixtureSession, metadata,
            restored.Select(PersistenceObjectData.Capture).ToArray())));
        for (int i = 0; i < restored.Count; i++)
            Check(ObjectBlockWriter.Write(roundTrip.Objects[i]).SequenceEqual(expected[i]), "owned restore/save/read round trip");

        Check(SnapshotModelRestorer.Restore(new OwnedSnapshotObjects(Array.Empty<PersistenceObjectData>())).Count == 0, "empty owned staging");
        var duplicates = new[] { decoded[1], decoded[1], decoded[2] };
        Throws<InvalidOperationException>(() => SnapshotModelRestorer.Restore(new OwnedSnapshotObjects(duplicates)));
        Check(duplicates.All(x => x == null), "duplicate failure clears unconsumed snapshot references");
        var broken = SnapshotReader.Read(new MemoryStream(bytes)).Objects.ToArray();
        broken[1].Parts[0].RenderToLogical[0] = int.MaxValue;
        Throws<InvalidOperationException>(() => SnapshotModelRestorer.Restore(new OwnedSnapshotObjects(broken)));
        Check(broken.All(x => x == null), "part failure consumes snapshot references");
    }
}
