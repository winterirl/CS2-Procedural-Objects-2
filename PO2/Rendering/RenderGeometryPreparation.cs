using System;
using System.Threading;
using UnityEngine;

namespace PO2.Rendering
{
    // Detached metadata built with the existing conversion/topology worker. It is
    // consumed once by a live renderer generation and is never history state.
    internal sealed class RenderGeometryPreparation : IDisposable
    {
        internal Reservation Budget;
        internal const long kPartLimit = 64L * 1024 * 1024;

        internal struct Copy
        {
            internal int Area, Vertex;
        }

        internal readonly int SourceCount;
        internal readonly int[][] Maps;
        internal readonly Vector3[] MainPositions;
        internal readonly Vector3[][] IsolatedPositions;
        internal readonly int[] Offsets;
        internal readonly Copy[] Copies;
        internal readonly RenderBounds MainBounds;
        internal readonly RenderBounds[] IsolatedBounds;
        internal readonly long Bytes;

        internal static long Estimate(int sourceCount, long copies, long rendered, int areas) =>
            checked(4096 + sourceCount * 9L + copies * 9L + rendered * 3L + areas * 8192L);

        internal static RenderGeometryPreparation TryBuild(int sourceCount, int[][] maps, Vector3[] main, Vector3[][] isolated, CancellationToken token = default(CancellationToken))
        {
            long copies = 0, rendered = main?.Length ?? 0;
            for (int i = 0; i < maps.Length; i++)
            {
                copies += maps[i].Length;
                rendered += isolated[i]?.Length ?? 0;
            }

            if (Estimate(sourceCount, copies, rendered, maps.Length) > kPartLimit) return null;
            var budget = Reservation.TryAcquire(Estimate(sourceCount, copies, rendered, maps.Length));
            if (budget == null) return null;
            try
            {
                var result = new RenderGeometryPreparation(sourceCount, maps, main, isolated, checked((int)copies), token) { Budget = budget };
                return result;
            }
            catch
            {
                budget.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Budget?.Dispose();
            Budget = null;
        }

        // Reserve peak construction memory until the renderer generation is released.
        // Abandoned worker results have a finalizer as a backup.
        internal sealed class Reservation : IDisposable
        {
            internal const long kTotalLimit = 128L * 1024 * 1024;
            private static long s_Reserved;
            private long m_Bytes;
            internal long Bytes => Interlocked.Read(ref m_Bytes);
            internal static long Reserved => Interlocked.Read(ref s_Reserved);

            private Reservation(long bytes)
            {
                m_Bytes = bytes;
            }

            internal static Reservation TryAcquire(long bytes)
            {
                if (bytes < 0 || bytes > kTotalLimit) return null;
                while (true)
                {
                    long current = Reserved;
                    if (bytes > kTotalLimit - current) return null;
                    if (Interlocked.CompareExchange(ref s_Reserved, current + bytes, current) == current)
                        return new Reservation(bytes);
                }
            }

            public void Dispose()
            {
                long bytes = Interlocked.Exchange(ref m_Bytes, 0);
                if (bytes != 0) Interlocked.Add(ref s_Reserved, -bytes);
                GC.SuppressFinalize(this);
            }

            ~Reservation()
            {
                Dispose();
            }
        }

        private RenderGeometryPreparation(int sourceCount, int[][] maps, Vector3[] main, Vector3[][] isolated, int copies, CancellationToken token)
        {
            SourceCount = sourceCount;
            Maps = maps;
            MainPositions = main;
            IsolatedPositions = isolated;
            Offsets = new int[checked(sourceCount + 1)];
            foreach (var map in maps)
            {
                for (int vertex = 0; vertex < map.Length; vertex++)
                {
                    if ((vertex & 4095) == 0) token.ThrowIfCancellationRequested();
                    int index = map[vertex];
                    if ((uint)index >= (uint)sourceCount) throw new InvalidOperationException("Invalid renderer source vertex.");
                    Offsets[index + 1]++;
                }
            }

            for (int i = 1; i < Offsets.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                Offsets[i] = checked(Offsets[i] + Offsets[i - 1]);
            }

            Copies = new Copy[copies];
            var next = (int[])Offsets.Clone();
            for (int area = 0; area < maps.Length; area++)
            {
                var map = maps[area];
                for (int vertex = 0; vertex < map.Length; vertex++)
                {
                    if ((vertex & 4095) == 0) token.ThrowIfCancellationRequested();
                    Copies[next[map[vertex]]++] = new Copy { Area = area, Vertex = vertex };
                }
            }

            MainBounds = main == null ? null : new RenderBounds(main, token);
            IsolatedBounds = new RenderBounds[maps.Length];
            Bytes = Offsets.Length * 4L + Copies.Length * 8L + (MainBounds?.Bytes ?? 0) + maps.Length * 24L + 256;
            for (int i = 0; i < maps.Length; i++)
            {
                if (isolated[i] == null) continue;
                IsolatedBounds[i] = new RenderBounds(isolated[i], token);
                Bytes += IsolatedBounds[i].Bytes;
            }
        }
    }
}