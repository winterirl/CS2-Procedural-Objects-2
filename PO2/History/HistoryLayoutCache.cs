using System;
using System.Collections.Generic;
using System.Threading;
using PO2.Geometry;

namespace PO2.History
{
    // A matching version is not enough: callers can still edit triangle arrays.
    // Keep a small private copy and check every index before reusing its hash.
    internal sealed class HistoryLayoutCache
    {
        internal const long kEntryLimit = 4L * 1024 * 1024;
        internal const long kByteLimit = 16L * 1024 * 1024;
        internal const int kEntryCountLimit = 8;
        private const long kEntryOverhead = 256;

        private sealed class Entry
        {
            internal WeakReference<MeshData> Source;
            internal int Vertices, Normals, Tangents;
            internal int[] Triangles;
            internal ulong Hash;
            internal long Bytes;
        }

        private readonly List<Entry> m_Entries = new List<Entry>(kEntryCountLimit);
        private readonly long m_ByteLimit, m_EntryLimit;
        private readonly int m_EntryCountLimit;
        private readonly int m_Thread = Thread.CurrentThread.ManagedThreadId;
        internal long ReservedBytes { get; private set; }
        internal long PeakBytes { get; private set; }
        internal int Count => m_Entries.Count;

        internal HistoryLayoutCache(long byteLimit = kByteLimit, long entryLimit = kEntryLimit, int entryCountLimit = kEntryCountLimit)
        {
            if (byteLimit < 0 || entryLimit < 0 || entryCountLimit < 0 || entryCountLimit > kEntryCountLimit)
                throw new ArgumentOutOfRangeException(nameof(byteLimit));
            m_ByteLimit = Math.Min(byteLimit, kByteLimit);
            m_EntryLimit = Math.Min(entryLimit, kEntryLimit);
            m_EntryCountLimit = entryCountLimit;
        }

        internal ulong Layout(MeshData mesh)
        {
            CheckThread();
            if (mesh == null || mesh.Vertices == null || mesh.Triangles == null)
                throw new ArgumentException("Missing vertex history layout.");
            int vertices = mesh.Vertices.Length, normals = mesh.Normals?.Length ?? -1, tangents = mesh.Tangents?.Length ?? -1;
            for (int i = m_Entries.Count - 1; i >= 0; i--)
            {
                var entry = m_Entries[i];
                if (!entry.Source.TryGetTarget(out var source))
                {
                    Remove(i);
                    continue;
                }

                if (!ReferenceEquals(mesh, source)) continue;
                bool shape = entry.Vertices == vertices && entry.Normals == normals && entry.Tangents == tangents && entry.Triangles.Length == mesh.Triangles.Length;
                if (shape && HistoryBoundaryKernels.EqualTriangles(mesh.Triangles, entry.Triangles))
                {
                    m_Entries.RemoveAt(i);
                    m_Entries.Add(entry);

                    return entry.Hash;
                }

                Remove(i);
            }

            long bytes = kEntryOverhead + 4L * mesh.Triangles.Length;
            if (bytes > m_EntryLimit || bytes > m_ByteLimit || m_EntryCountLimit == 0)
            {
                return VertexHistoryKernels.Layout(mesh);
            }

            while (m_Entries.Count >= m_EntryCountLimit || bytes > m_ByteLimit - ReservedBytes)
            {
                Remove(0);
            }

            ReservedBytes += bytes;
            PeakBytes = Math.Max(PeakBytes, ReservedBytes);
            Entry added;
            try
            {
                added = new Entry
                {
                    Source = new WeakReference<MeshData>(mesh), Vertices = vertices,
                    Normals = normals, Tangents = tangents, Triangles = new int[mesh.Triangles.Length], Bytes = bytes
                };
            }
            catch (OutOfMemoryException)
            {
                Release(bytes);
                return VertexHistoryKernels.Layout(mesh);
            }

            try
            {
                added.Hash = VertexHistoryKernels.Layout(mesh, added.Triangles);
                m_Entries.Add(added);
                return added.Hash;
            }
            catch
            {
                Release(bytes);
                throw;
            }
        }

        internal void Clear()
        {
            CheckThread();
            while (m_Entries.Count > 0) Remove(m_Entries.Count - 1);
        }

        private void Remove(int index)
        {
            long bytes = m_Entries[index].Bytes;
            m_Entries.RemoveAt(index);
            Release(bytes);
        }

        private void Release(long bytes)
        {
            ReservedBytes -= bytes;
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != m_Thread)
                throw new InvalidOperationException("History layout validation must stay on the game thread.");
        }
    }
}