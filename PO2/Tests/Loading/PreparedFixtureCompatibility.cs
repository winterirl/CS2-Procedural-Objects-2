using System;
using System.IO;
using System.Linq;
using PO2.Core;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Persistence.PreparedTopology;
using Unity.Collections;
using UnityEngine;

internal static partial class LoadingTests
{
    private static CitySaveCapture PreparedContractCapture()
    {
        var fixture = StableFixture(42, SnapshotFormat.kObjectSchemaVersion);
        var data = PersistenceObjectData.FromDecoded(fixture.PersistentId, "prepared-contract", false, false,
            fixture.Transform, fixture.CustomOrientations, new[] { PreparedGrid(8, false), PreparedGrid(65, false) }, Vector3.zero);
        return new CitySaveCapture(new[] { data.Restore(1) }, null);
    }

    private static void PreparedFixtureCompatibility()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PreparedTopology", "container3.bin"));
        var read = SnapshotReader.Read(new SmallReadStream(bytes), true, FixtureSave, FixtureSession);
        Check(read.ContainerVersion == 3 && read.Metadata.SaveKind == SnapshotSaveKind.AutoSave, "frozen prepared container and metadata");
        var expected = PreparedContractCapture();
        var metadata = new SnapshotMetadata("contract-city", "contract-auto", "contract-map", null, SnapshotSaveKind.AutoSave, null);
        // Keep the frozen records for verification, and check the test-only writer
        // against their decoded tables without depending on identical Deflate bytes.
        using (var candidates = SnapshotReader.Read(new MemoryStream(bytes)).TakeObjectsForRestore())
        {
            for (int j = 0; j < expected.Parts[0].Length; j++)
            {
                var capture = expected.Parts[0][j];
                capture.Candidate = candidates.TakePrepared(0, j);
                var encoded = FullTopologyTestWriter.Encode(capture);
                var source = expected.Objects[0].Parts[j];
                var frozen = PreparedTopologyValidator.Validate(capture.Candidate, source).Data;
                var generated = PreparedTopologyValidator.Validate(encoded, source).Data;
                Check(encoded.Version == 1 && encoded.Revision == 1, "test writer retains the full format");
                for (int id = 1; id <= 22; id++)
                {
                    if (id == 17) Check(generated.Forward.SequenceEqual(frozen.Forward), "full test writer matches frozen directions");
                    else Same(generated[id], frozen[id], "full test writer matches frozen section " + id);
                }
            }
        }
        SnapshotReader.Verify(new SmallReadStream(bytes), FixtureSave, FixtureSession, metadata,
            expected.Objects.Select(o => o.PersistentId).ToArray(), prepared: expected);
        int allocations = NativeAllocationChecks.Created;
        var restored = SnapshotModelRestorer.Restore(read.TakeObjectsForRestore());
        Check(NativeAllocationChecks.Created == allocations, "frozen fixture uses prepared path");
        Check(ObjectBlockWriter.Write(PersistenceObjectData.Capture(restored.Single())).SequenceEqual(ObjectBlockWriter.Write(expected.Objects.Single())), "frozen prepared geometry and metadata match");
    }

}
