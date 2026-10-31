using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PO2.Geometry.Topology;
using PO2.Persistence;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

internal static partial class LoadingTests
{
    private static void SchedulingIntegrationChecks()
    {
        ConcurrentTopology();
        ReaderAdmissionChecks();
    }

    private static void ConcurrentTopology()
    {
        var random = new Random(319);
        var meshes = Enumerable.Range(0, 32).Select(i => Mesh(80,
            Enumerable.Range(0, 600).Select(_ => random.Next(80)).ToArray())).ToArray();
        var maps = meshes.Select(_ => Enumerable.Range(0, 80).Select(i => i % 40).ToArray()).ToArray();
        var expected = meshes.Select((mesh, i) => ReferenceMeshTopology.Restore(mesh, maps[i], 40)).ToArray();
        LoadingBatch.Run(meshes.Length, i => MeshTopology.Restore(meshes[i], maps[i], 40, true), (i, actual) =>
        {
            var reference = expected[i];
            Check(actual.LogicalVertexCount == reference.LogicalVertexCount && actual.LogicalEdgeCount == reference.LogicalEdgeCount, "concurrent topology counts");
            for (int vertex = 0; vertex < actual.LogicalVertexCount; vertex++)
            {
                var a = actual.GetVertex(vertex);
                var b = reference.GetVertex(vertex);
                Same(a.RenderVertexIndices, b.RenderVertexIndices, "concurrent render membership");
                Same(a.TriangleIndices, b.TriangleIndices, "concurrent triangle membership");
                Same(a.AdjacentVertexIds, b.AdjacentVertexIds, "concurrent vertex adjacency");
            }
            for (int edge = 0; edge < actual.LogicalEdgeCount; edge++)
            {
                var a = actual.GetEdge(edge);
                var b = reference.GetEdge(edge);
                Check(a.Id == b.Id && a.VertexA == b.VertexA && a.VertexB == b.VertexB, "concurrent edge identity");
                Same(a.TriangleIndices, b.TriangleIndices, "concurrent edge adjacency");
            }
        }, workers: 16);
    }

    private static void ReaderAdmissionChecks()
    {
        var objects = Enumerable.Range(201, 7).Select(i => StableFixture(i, SnapshotFormat.kObjectSchemaVersion)).ToArray();
        var metadata = new SnapshotMetadata("city", "save", "map", null, SnapshotSaveKind.Manual, null);
        var bytes = SnapshotWriter.Write(FixtureSave, FixtureSession, metadata, objects, new SnapshotEncodingOptions(SaveCompression.Fast, 16));
        int directory = checked((int)BitConverter.ToUInt64(bytes, 56));
        var starts = new Dictionary<long, int>();
        var declared = new long[objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            int entry = directory + i * SnapshotFormat.kDirectoryEntrySize;
            starts.Add(checked((long)BitConverter.ToUInt64(bytes, entry + 20)), i);
            declared[i] = i < 6 ? SnapshotFormat.kMaxDecodedObjectBytes : checked((long)BitConverter.ToUInt64(bytes, entry + 36));
            Array.Copy(BitConverter.GetBytes(checked((ulong)declared[i])), 0, bytes, entry + 36, 8);
        }
        FixChecksums(bytes);

        // Large declared sizes exercise the actual reader's admission guard.
        // The blocks themselves stay tiny and fail the footprint comparison.
        long pending = 0, maximumPending = 0, acceptedBytes = 0;
        int next = 0, accepted = 0, rejected = 0;
        using (var stream = new AdmissionReadStream(bytes, starts, i =>
        {
            pending += declared[i];
            maximumPending = Math.Max(maximumPending, pending);
            Check(pending + acceptedBytes <= SnapshotFormat.kMaxDecodedSnapshotBytes, "reader lookahead respects decoded ceiling");
        }))
        {
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var prepared = typeof(SnapshotReader).GetMethod("PrepareSnapshot", flags).Invoke(null,
                new object[] { stream, FixtureSave, FixtureSession });
            Action<int, PersistenceObjectData> accept = (i, value) =>
            {
                Check(i == next++ && value.PersistentId == objects[i].PersistentId, "accepted reader order");
                pending -= declared[i];
                acceptedBytes += declared[i];
                accepted++;
            };
            Action<SnapshotObjectError> reject = error =>
            {
                Check(error.DirectoryIndex == next++, "rejected reader order");
                pending -= declared[error.DirectoryIndex];
                rejected++;
            };
            var total = (long)typeof(SnapshotReader).GetMethod("WalkObjects", flags).Invoke(null,
                new object[] { stream, prepared, accept, reject, true, null });
            Check(accepted == 1 && rejected == 6 && pending == 0 && total == acceptedBytes, "reader drains guarded lookahead");
            if (LoadingBatch.WorkerCount >= 4)
                Check(maximumPending == SnapshotFormat.kMaxDecodedSnapshotBytes, "reader exercises full decoded allowance");
        }
        foreach (bool parallel in new[] { false, true })
        {
            var result = SnapshotReader.Read(new MemoryStream(bytes), parallel);
            Check(result.ObjectCount == 1 && result.Objects[0].PersistentId == objects[6].PersistentId, "guarded load retains valid object");
            Same(result.SkippedObjects.Select(e => e.DirectoryIndex), Enumerable.Range(0, 6), "guarded load errors stay ordered");
            Throws<InvalidDataException>(() => SnapshotReader.Verify(new MemoryStream(bytes), FixtureSave, FixtureSession,
                metadata, objects.Select(o => o.PersistentId).ToArray(), parallel));
        }
    }

    private sealed class AdmissionReadStream : MemoryStream
    {
        private readonly Dictionary<long, int> m_Starts;
        private readonly Action<int> m_OnRead;
        private readonly HashSet<int> m_Read = new HashSet<int>();

        internal AdmissionReadStream(byte[] bytes, Dictionary<long, int> starts, Action<int> onRead) : base(bytes, false)
        {
            m_Starts = starts;
            m_OnRead = onRead;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count != 0 && m_Starts.TryGetValue(Position, out int index) && m_Read.Add(index)) m_OnRead(index);
            return base.Read(buffer, offset, count);
        }
    }
}
