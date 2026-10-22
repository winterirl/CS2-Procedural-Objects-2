using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PO2.Geometry.Topology;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

namespace PO2.History
{
    // Mirror retains its stream lengths. One byte-exact XOR record works in
    // both directions; compression is optional and starts after publication.
    internal sealed class MirrorDelta : IDisposable
    {
        internal sealed class Reader
        {
            internal readonly byte[] Bytes;
            internal readonly bool Compressed;

            internal Reader(byte[] bytes, bool compressed)
            {
                Bytes = bytes;
                Compressed = compressed;
            }
        }

        private sealed class CompressionResult
        {
            internal HistoryResidentPayload Payload;
            internal bool Compressed;
        }

        private static readonly SemaphoreSlim s_CompressionGate = new SemaphoreSlim(1, 1);
        private readonly int[] m_Shape;
        private readonly ulong m_Before, m_After;
        internal readonly int RawBytes;
        private byte[] m_Raw;
        private HistoryResidentPayload m_Payload;
        private bool m_Compressed, m_Disposed, m_CompressionStarted;
        private CancellationTokenSource m_CompressionCancellation;
        private Task<CompressionResult> m_Compression;
        private string m_CompressionFailure;
        internal Task CompressionDrain { get; private set; } = Task.CompletedTask;
        internal bool CompressionPending => m_Compression != null;
        internal long StoredBytes => m_Payload?.LongLength ?? RawBytes;

        internal IEnumerable<HistoryResidentPayload> Payloads
        {
            get
            {
                if (m_Payload != null) yield return m_Payload;
            }
        }

        internal HistoryFootprint Footprint
        {
            get
            {
                if (m_Disposed) return default;
                var payload = m_Payload?.Footprint ?? default;
                long working = 0;
                if (m_Compression != null)
                {
                    // Include queued/completed storage as well as the raw record
                    // until the owner has adopted the worker's result.
                    if (m_Compression.Status == TaskStatus.RanToCompletion)
                        working = m_Compression.Result.Payload.LongLength;
                    else if (!m_Compression.IsCompleted) working = 2L * RawBytes;
                }

                return new HistoryFootprint(256 + (m_Raw?.LongLength ?? 0) + working, payload.EncodedBytes, payload.ColdBytes, RawBytes);
            }
        }

        internal MirrorDelta(PersistencePartData before, PersistencePartData after, CancellationToken token)
        {
            // TransformCapture deliberately shares these detached, unchanged
            // arrays. Refuse an unexpected edit instead of silently omitting it.
            if (!ReferenceEquals(before.UV0, after.UV0) || !ReferenceEquals(before.VertexColors, after.VertexColors) || !ReferenceEquals(before.TexCoord1, after.TexCoord1) || !ReferenceEquals(before.TexCoord3, after.TexCoord3) || !ReferenceEquals(before.RenderToLogical, after.RenderToLogical) || !ReferenceEquals(before.TriangleSurfaceAreaIds, after.TriangleSurfaceAreaIds))
                throw new InvalidOperationException("Mirror changed an appearance or vertex mapping stream.");
            var beforeMetadata = Metadata(before);
            var afterMetadata = Metadata(after);
            m_Shape = Shape(before, beforeMetadata.Length);
            CheckShape(after, afterMetadata.Length);
            RawBytes = checked(12 * m_Shape[0] + 12 * Math.Max(0, m_Shape[1]) + 16 * Math.Max(0, m_Shape[2]) + 4 * m_Shape[3] + 24 * m_Shape[4] + m_Shape[5]);
            m_Raw = new byte[RawBytes];
            ulong a = HashUnchanged(before, token), b = a;
            int offset = 0;
            MirrorHistoryKernels.Create(before.Vertices, after.Vertices, m_Raw, ref offset, ref a, ref b, token);
            MirrorHistoryKernels.Create(before.Normals, after.Normals, m_Raw, ref offset, ref a, ref b, token);
            MirrorHistoryKernels.Create(before.Tangents, after.Tangents, m_Raw, ref offset, ref a, ref b, token);
            MirrorHistoryKernels.Create(before.Triangles, after.Triangles, m_Raw, ref offset, ref a, ref b, token);
            MirrorHistoryKernels.Create(Faces(before, token), Faces(after, token), m_Raw, ref offset, ref a, ref b, token);
            MirrorHistoryKernels.Create(beforeMetadata, afterMetadata, m_Raw, ref offset, ref a, ref b, token);
            if (offset != RawBytes) throw new InvalidOperationException("Mirror delta size does not match its streams.");
            m_Before = a;
            m_After = b;
        }

        private static int[] Shape(PersistencePartData data, int metadata) => new[]
        {
            data.Vertices.Length, data.Normals?.Length ?? -1, data.Tangents?.Length ?? -1,
            data.Triangles.Length, data.StructuralFaces.Length, metadata
        };

        private void CheckShape(PersistencePartData data, int metadata)
        {
            var shape = Shape(data, metadata);
            for (int i = 0; i < shape.Length; i++)
                if (shape[i] != m_Shape[i])
                    throw new InvalidOperationException("The mirror history stream layout changed.");
        }

        private static ulong HashUnchanged(PersistencePartData data, CancellationToken token)
        {
            ulong hash = MirrorHistoryKernels.Hash(data.UV0, MirrorHistoryKernels.kHashStart, token);
            hash = MirrorHistoryKernels.Hash(data.VertexColors, hash, token);
            hash = MirrorHistoryKernels.Hash(data.TexCoord1, hash, token);
            hash = MirrorHistoryKernels.Hash(data.TexCoord3, hash, token);
            hash = MirrorHistoryKernels.Hash(data.RenderToLogical, hash, token);
            return MirrorHistoryKernels.Hash(data.TriangleSurfaceAreaIds, hash, token);
        }

        private static int[] Faces(PersistencePartData data, CancellationToken token)
        {
            var values = new int[checked(data.StructuralFaces.Length * 6)];
            for (int i = 0; i < data.StructuralFaces.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                var face = data.StructuralFaces[i];
                int at = i * 6;
                values[at] = face.Id;
                values[at + 1] = face.CornerCount;
                for (int j = 0; j < 4; j++) values[at + 2 + j] = j < face.CornerCount ? face.GetRenderVertexIndex(j) : -1;
            }

            return values;
        }

        private static byte[] Metadata(PersistencePartData data)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(data.LogicalVertexCount);
                    ObjectBlockWriter.WritePartMetadata(stream, data);
                    // The existing deformation compressor shuffles four byte lanes.
                    while ((stream.Length & 3) != 0) writer.Write((byte)0);
                }

                return stream.ToArray();
            }
        }

        internal async Task<Reader> Prepare(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (m_Disposed) throw new ObjectDisposedException(nameof(MirrorDelta));
            if (m_Raw != null) return new Reader(m_Raw, false);
            var payload = m_Payload;
            await payload.Prepare(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new Reader(payload.Bytes, m_Compressed);
        }

        internal PersistencePartData Apply(PersistencePartData current, HistoryDirection direction, Reader reader, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var metadata = Metadata(current);
            CheckShape(current, metadata.Length);
            var raw = reader.Compressed ? GeometryStreams.DecompressRawXor(reader.Bytes, RawBytes) : reader.Bytes;
            if (raw.Length != RawBytes) throw new InvalidDataException("Mirror delta has the wrong length.");
            ulong a = HashUnchanged(current, token), b = a;
            int offset = 0;
            var positions = MirrorHistoryKernels.Apply(current.Vertices, raw, ref offset, ref a, ref b, token);
            var normals = MirrorHistoryKernels.Apply(current.Normals, raw, ref offset, ref a, ref b, token);
            var tangents = MirrorHistoryKernels.Apply(current.Tangents, raw, ref offset, ref a, ref b, token);
            var triangles = MirrorHistoryKernels.Apply(current.Triangles, raw, ref offset, ref a, ref b, token);
            var faceValues = MirrorHistoryKernels.Apply(Faces(current, token), raw, ref offset, ref a, ref b, token);
            var restoredMetadata = MirrorHistoryKernels.Apply(metadata, raw, ref offset, ref a, ref b, token);
            bool undo = direction == HistoryDirection.Undo;
            if (a != (undo ? m_After : m_Before)) throw new InvalidDataException("The mirror history target no longer matches.");
            if (offset != RawBytes || b != (undo ? m_Before : m_After)) throw new InvalidDataException("The mirror delta failed verification.");
            var faces = new StructuralFace[m_Shape[4]];
            for (int i = 0; i < faces.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                int at = i * 6, id = faceValues[at], count = faceValues[at + 1];
                if (count == 3 && faceValues[at + 5] == -1)
                    faces[i] = new StructuralFace(id, faceValues[at + 2], faceValues[at + 3], faceValues[at + 4]);
                else if (count == 4)
                    faces[i] = new StructuralFace(id, faceValues[at + 2], faceValues[at + 3], faceValues[at + 4], faceValues[at + 5]);
                else throw new InvalidDataException("Invalid mirror history face.");
            }

            using (var stream = new MemoryStream(restoredMetadata, false))
            using (var input = new BinaryReader(stream))
            {
                int logical = input.ReadInt32();
                var part = ObjectBlockReader.ReadPartMetadata(stream, SnapshotFormat.kObjectSchemaVersion);
                if (stream.Length - stream.Position > 3) throw new InvalidDataException("Trailing mirror metadata.");
                while (stream.Position < stream.Length)
                    if (input.ReadByte() != 0)
                        throw new InvalidDataException("Invalid mirror metadata padding.");
                return PersistencePartData.FromDecoded(part.Name, positions, normals, tangents, current.UV0, current.VertexColors, current.TexCoord1, current.TexCoord3, triangles, current.RenderToLogical, logical, faces, part.StructuralTopologySource, part.SurfaceAreas, current.TriangleSurfaceAreaIds, part.NextSurfaceAreaId, part.LocalPivot);
            }
        }

        internal void StartCompression(Func<byte[], byte[]> encode = null)
        {
            if (m_Disposed || m_CompressionStarted || m_Raw == null || RawBytes < 65536) return;
            m_CompressionStarted = true;
            m_CompressionCancellation = new CancellationTokenSource();
            var token = m_CompressionCancellation.Token;
            var raw = m_Raw;
            var compress = encode ?? GeometryStreams.CompressRawXor;
            try
            {
                m_Compression = Task.Run(async () =>
                {
                    await s_CompressionGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var encoded = compress(raw);
                        token.ThrowIfCancellationRequested();
                        bool smaller = encoded.Length < raw.Length;
                        // Hashing and cold-store preparation also stay off the owner thread.
                        var payload = new HistoryResidentPayload(smaller ? encoded : raw, raw.Length);
                        if (token.IsCancellationRequested)
                        {
                            payload.Dispose();
                            token.ThrowIfCancellationRequested();
                        }

                        return new CompressionResult { Payload = payload, Compressed = smaller };
                    }
                    finally
                    {
                        s_CompressionGate.Release();
                    }
                }, token);
                CompressionDrain = m_Compression;
            }
            catch (Exception e)
            {
                m_CompressionFailure = e.Message;
                m_CompressionCancellation.Dispose();
                m_CompressionCancellation = null;
            }
        }

        // Only the owner adopts storage, and only when no replay is reading it.
        internal void PollCompression(Action<string> log)
        {
            if (m_Disposed) return;
            if (m_Compression != null && m_Compression.IsCompleted)
            {
                var task = m_Compression;
                m_Compression = null;
                CompressionDrain = Task.CompletedTask;
                m_CompressionCancellation.Dispose();
                m_CompressionCancellation = null;
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    var result = task.Result;
                    m_Payload = result.Payload;
                    m_Compressed = result.Compressed;
                    m_Raw = null;
                }
                else m_CompressionFailure = task.Exception?.GetBaseException().Message ?? "Compression cancelled.";
            }

            if (m_CompressionFailure != null && log != null)
            {
                log("[PO Mirror] History compression failed; the exact raw delta remains available: " + m_CompressionFailure);
                m_CompressionFailure = null;
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Raw = null;
            m_Payload?.Dispose();
            m_Payload = null;
            var cancellation = m_CompressionCancellation;
            var task = m_Compression;
            m_CompressionCancellation = null;
            m_Compression = null;
            cancellation?.Cancel();
            if (task == null)
            {
                cancellation?.Dispose();
                return;
            }

            CompressionDrain = task.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion) t.Result.Payload.Dispose();
                else
                {
                    var ignored = t.Exception;
                }

                cancellation.Dispose();
            }, TaskScheduler.Default);
        }
    }
}