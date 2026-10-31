using System;
using System.Collections.Generic;
using System.IO;
using PO2.Persistence.Model;
using PO2.Persistence.PreparedTopology;

namespace PO2.Persistence.Format
{
    internal static partial class SnapshotReader
    {
        // Use the same decoder as loading, but never keep a second decoded city.
        // A damaged old object may be skipped on load; a new save must retain all of them.
        internal static SnapshotVerificationResult Verify(Stream stream, Guid saveGuid, Guid sessionGuid,
            SnapshotMetadata metadata, IReadOnlyList<Guid> objectIds, bool parallel = true, CitySaveCapture prepared = null)
        {
            if (objectIds == null) throw new ArgumentNullException(nameof(objectIds));
            var snapshot = PrepareSnapshot(stream, saveGuid, sessionGuid);
            var header = snapshot.Header;
            if (header.ContainerVersion != (prepared == null ? SnapshotFormat.kContainerVersion : SnapshotFormat.kPreparedContainerVersion) ||
                header.MetadataStatus != SnapshotMetadataStatus.Available || metadata == null || !metadata.Equals(header.Metadata))
                throw new InvalidDataException("Temporary snapshot metadata did not round-trip.");
            if (header.ObjectCount != objectIds.Count)
                throw new InvalidDataException("Temporary snapshot object count mismatch.");
            for (int i = 0; i < objectIds.Count; i++)
                if (snapshot.Entries[i].PersistentId != objectIds[i])
                    throw new InvalidDataException("Temporary snapshot object identity/order mismatch.");
            int count = 0;
            long bytes = WalkObjects(stream, snapshot, (i, obj) =>
            {
                if (obj.PersistentId != objectIds[i]) throw new InvalidDataException("Verified object identity mismatch.");
                count++;
            }, error => throw new InvalidDataException($"Temporary snapshot object {error.DirectoryIndex} failed verification: {error.Message}"), parallel, prepared == null ? (Action<int, PersistenceObjectData>)null : prepared.VerifyInputs);
            if (count != objectIds.Count) throw new InvalidDataException("Temporary snapshot did not verify every object.");
            if (prepared != null)
            {
                var index = PreparedTopologyExtension.ReadIndex(stream, snapshot.RequiredEnd, snapshot.Entries, true);
                var sources = new List<PersistencePartData>();
                var captures = new List<PartTopologyCapture>();
                int at = 0;
                for (int i = 0; i < prepared.Objects.Count; i++)
                {
                    for (int j = 0; j < prepared.Objects[i].Parts.Length; j++)
                    {
                        if (prepared.Parts[i][j].Candidate == null) continue;
                        captures.Add(prepared.Parts[i][j]);
                        if (at >= index.Length || index[at].Object != objectIds[i] || index[at].Part != j)
                            throw new InvalidDataException("Temporary snapshot prepared part binding mismatch.");
                        sources.Add(prepared.Objects[i].Parts[j]);
                        at++;
                    }
                }
                if (at != index.Length) throw new InvalidDataException("Temporary snapshot prepared part count mismatch.");
                OrderedWorkQueue.Run(index.Length, i => PreparedTopologyExtension.ReadPart(stream, index[i], sources[i], true),
                    (i, encoded) =>
                    {
                        if (encoded.Version != captures[i].Candidate.Version || encoded.Revision != captures[i].Candidate.Revision)
                            throw new InvalidDataException("Temporary snapshot prepared layout changed.");
                        var validated = PreparedTopologyValidator.Validate(encoded, sources[i]);
                        captures[i].Reusable = validated.CanonicalMaps;
                        if (validated.CanonicalMaps) PO2.Geometry.MeshPart.ValidatePreparedSurfaceAreas(sources[i], validated.Data);
                        else
                        {
                            PO2.Geometry.Topology.StructuralFaceTriangleResolver.Build(sources[i].StructuralFaces, sources[i].Triangles, out var forward, out _);
                            PO2.Geometry.MeshPart.ValidateResolvedSurfaceAreas(sources[i], forward);
                        }
                        return true;
                    }, (i, valid) => { }, null, null, parallel ? LoadingBatch.WorkerCount : 1);
            }
            return new SnapshotVerificationResult(header, count, bytes);
        }
    }

    internal sealed class SnapshotVerificationResult
    {
        internal SnapshotHeaderInfo Header { get; }
        internal int ObjectCount { get; }
        internal long DecodedBytes { get; }
        internal SnapshotVerificationResult(SnapshotHeaderInfo header, int count, long bytes)
        {
            Header = header;
            ObjectCount = count;
            DecodedBytes = bytes;
        }
    }
}
