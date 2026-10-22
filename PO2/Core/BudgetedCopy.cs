using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PO2.Core
{
    // Allocate detached buffers on a worker, then read live data only on its
    // owner thread. A revision check surrounds every bounded copy slice.
    internal sealed class BudgetedCopy
    {
        private interface IStream
        {
            void Allocate();
            bool Copy(ref int bytes);
        }

        internal sealed class Stream<T> : IStream
        {
            private readonly T[] m_Source;
            private readonly Func<int, T> m_Read;
            private readonly int m_Count, m_Stride;
            private int m_Cursor;
            internal T[] Data { get; private set; }

            internal Stream(T[] source, int count, int stride, Func<int, T> read)
            {
                m_Source = source;
                m_Count = count;
                m_Stride = stride;
                m_Read = read;
            }

            void IStream.Allocate()
            {
                Data = new T[m_Count];
            }

            bool IStream.Copy(ref int bytes)
            {
                int count = Math.Min(m_Count - m_Cursor, bytes / m_Stride);
                if (m_Source != null) Array.Copy(m_Source, m_Cursor, Data, m_Cursor, count);
                else
                    for (int i = m_Cursor; i < m_Cursor + count; i++)
                        Data[i] = m_Read(i);
                m_Cursor += count;
                bytes -= count * m_Stride;
                return m_Cursor == m_Count;
            }
        }

        private readonly List<IStream> m_Streams = new List<IStream>();
        private readonly int m_Thread = Thread.CurrentThread.ManagedThreadId;
        private Task m_Allocation;
        private int m_Cursor;
        internal long Bytes { get; private set; }
        internal long CopiedBytes { get; private set; }
        internal bool IsComplete => m_Allocation != null && m_Allocation.Status == TaskStatus.RanToCompletion && m_Cursor == m_Streams.Count;

        internal Stream<T> Add<T>(T[] source, int stride) => source == null ? null : Add(source, source.Length, stride, null);
        internal Stream<T> Add<T>(int count, int stride, Func<int, T> read) => Add(null, count, stride, read);

        private Stream<T> Add<T>(T[] source, int count, int stride, Func<int, T> read)
        {
            if (m_Allocation != null || count < 0 || stride <= 0) throw new InvalidOperationException("Invalid capture layout.");
            var stream = new Stream<T>(source, count, stride, read);
            Bytes = checked(Bytes + (long)count * stride);
            m_Streams.Add(stream);
            return stream;
        }

        internal void Start(CancellationToken token)
        {
            if (m_Allocation != null) throw new InvalidOperationException("Capture already started.");
            m_Allocation = Task.Run(() =>
            {
                foreach (var stream in m_Streams)
                {
                    token.ThrowIfCancellationRequested();
                    stream.Allocate();
                }
            }, token);
        }

        internal bool Poll(Func<bool> matches, CancellationToken token, int maximumBytes = 1024 * 1024, long deadlineTimestamp = 0)
        {
            if (Thread.CurrentThread.ManagedThreadId != m_Thread) throw new InvalidOperationException("Capture must run on its owner thread.");
            token.ThrowIfCancellationRequested();
            if (!matches()) throw new InvalidOperationException("The mesh changed during capture.");
            if (m_Allocation == null || !m_Allocation.IsCompleted) return false;
            m_Allocation.GetAwaiter().GetResult();
            var timer = Stopwatch.StartNew();
            int remaining = Math.Max(256, maximumBytes);
            do
            {
                if (m_Cursor == m_Streams.Count) break;
                int chunk = Math.Min(remaining, 16 * 1024), left = chunk;
                bool finished = m_Streams[m_Cursor].Copy(ref left);
                if (finished) m_Cursor++;
                if (!finished && left == chunk) break;
                remaining -= chunk - left;
                CopiedBytes += chunk - left;
            } while (remaining >= 256 && timer.Elapsed.TotalMilliseconds < 1 && (deadlineTimestamp == 0 || Stopwatch.GetTimestamp() < deadlineTimestamp));

            token.ThrowIfCancellationRequested();
            if (!matches()) throw new InvalidOperationException("The mesh changed during capture.");
            return IsComplete;
        }

        internal Task Allocation => m_Allocation ?? Task.CompletedTask;
    }
}