using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using PO2.Core.Lod;

namespace PO2.Rendering.Lod
{
    internal sealed class LodCancellationSignal
    {
        private int m_Cancelled;

        internal bool IsCancellationRequested =>
            Volatile.Read(ref m_Cancelled) != 0;

        internal void Cancel()
        {
            Interlocked.Exchange(ref m_Cancelled, 1);
        }
    }

    internal readonly struct LodGenerationCompletion
    {
        internal LodGenerationCompletion(LodGenerationKey key, LodGenerationAttempt attempt, double workerMilliseconds)
        {
            Key = key;
            Attempt = attempt ?? throw new ArgumentNullException(nameof(attempt));
            WorkerMilliseconds = workerMilliseconds;
        }

        internal LodGenerationKey Key { get; }
        internal LodGenerationAttempt Attempt { get; }
        internal double WorkerMilliseconds { get; }
    }

    internal sealed class LodGenerationWorker : IDisposable
    {
        private readonly ILodGenerationEngine m_Engine;
        private readonly int m_MaximumOutstandingJobs;
        private readonly object m_Gate = new object();
        private readonly List<WorkItem> m_Requests = new List<WorkItem>();
        private readonly List<LodGenerationCompletion> m_Completions = new List<LodGenerationCompletion>();
        private readonly AutoResetEvent m_WorkAvailable = new AutoResetEvent(false);
        private readonly Thread m_Thread;
        private bool m_StopRequested;
        private int m_OutstandingJobs;
        private WorkItem m_Active;

        internal LodGenerationWorker(ILodGenerationEngine engine, int maximumOutstandingJobs)
        {
            m_Engine = engine ?? throw new ArgumentNullException(nameof(engine));
            if (maximumOutstandingJobs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumOutstandingJobs));
            }

            m_MaximumOutstandingJobs = maximumOutstandingJobs;
            m_Thread = new Thread(Run)
            {
                IsBackground = true, Name = "PO2 LOD generation", Priority = ThreadPriority.BelowNormal,
            };
            m_Thread.Start();
        }

        internal int OutstandingJobs
        {
            get
            {
                lock (m_Gate)
                {
                    return m_OutstandingJobs;
                }
            }
        }

        internal bool HasCapacity
        {
            get
            {
                lock (m_Gate)
                {
                    return !m_StopRequested && m_OutstandingJobs < m_MaximumOutstandingJobs;
                }
            }
        }

        internal bool TrySubmit(LodCapturedSource source, out LodCancellationSignal cancellation)
        {
            return TrySubmit(source, null, out cancellation);
        }

        internal bool TrySubmit(LodCapturedSource source, LodProfile profile, out LodCancellationSignal cancellation)
        {
            lock (m_Gate)
            {
                cancellation = null;
                if (source == null || m_StopRequested || m_OutstandingJobs >= m_MaximumOutstandingJobs)
                {
                    return false;
                }

                cancellation = new LodCancellationSignal();
                m_Requests.Add(new WorkItem(source, profile, cancellation));
                ++m_OutstandingJobs;
                m_WorkAvailable.Set();
                return true;
            }
        }

        // Only queued work is removed. An active native call keeps its buffers until it returns.
        internal List<LodGenerationKey> DiscardQueued(bool includeBackground = false)
        {
            var removed = new List<LodGenerationKey>();
            lock (m_Gate)
            {
                for (int i = m_Requests.Count - 1; i >= 0; --i)
                {
                    WorkItem item = m_Requests[i];
                    if (!item.Cancellation.IsCancellationRequested && !(includeBackground && !item.Source.Key.IsPreview))
                    {
                        continue;
                    }

                    item.Cancellation.Cancel();
                    removed.Add(item.Source.Key);
                    m_Requests.RemoveAt(i);
                    --m_OutstandingJobs;
                }
            }

            return removed;
        }

        internal bool TryDequeueCompletion(out LodGenerationCompletion completion)
        {
            lock (m_Gate)
            {
                completion = default;
                if (m_Completions.Count == 0)
                {
                    return false;
                }

                completion = m_Completions[0];
                m_Completions.RemoveAt(0);
                --m_OutstandingJobs;
                return true;
            }
        }

        public void Dispose()
        {
            lock (m_Gate)
            {
                if (m_StopRequested)
                {
                    return;
                }

                m_StopRequested = true;
                m_Active?.Cancellation.Cancel();
                foreach (WorkItem item in m_Requests)
                {
                    item.Cancellation.Cancel();
                }

                m_OutstandingJobs -= m_Requests.Count + m_Completions.Count;
                m_Requests.Clear();
                m_Completions.Clear();
                m_WorkAvailable.Set();
            }
        }

        private void Run()
        {
            try
            {
                while (true)
                {
                    WorkItem item = null;
                    lock (m_Gate)
                    {
                        if (m_StopRequested)
                        {
                            return;
                        }

                        if (m_Requests.Count != 0)
                        {
                            int index = m_Requests.FindIndex(x => x.Source.Key.IsPreview);
                            if (index < 0)
                            {
                                index = 0;
                            }

                            item = m_Requests[index];
                            m_Requests.RemoveAt(index);
                            m_Active = item;
                        }
                    }

                    if (item == null)
                    {
                        m_WorkAvailable.WaitOne();
                    }
                    else
                    {
                        Process(item);
                    }
                }
            }
            finally
            {
                m_WorkAvailable.Dispose();
            }
        }

        private void Process(WorkItem item)
        {
            long start = Stopwatch.GetTimestamp();
            LodGenerationAttempt attempt;
            try
            {
                if (item.Cancellation.IsCancellationRequested)
                {
                    attempt = LodGenerationAttempt.Cancelled();
                }
                else
                {
                    LodSourceSnapshot snapshot = item.Source.Materialize();
                    attempt = m_Engine.Generate(new LodGenerationRequest(snapshot, item.Profile), () => item.Cancellation.IsCancellationRequested);
                }
            }
            catch (OperationCanceledException)
            {
                attempt = LodGenerationAttempt.Cancelled();
            }
            catch (Exception exception)
            {
                attempt = LodGenerationAttempt.Failed(exception.GetType().Name + ": " + exception.Message);
            }

            lock (m_Gate)
            {
                m_Active = null;
                if (m_StopRequested)
                {
                    --m_OutstandingJobs;
                    return;
                }

                if (item.Cancellation.IsCancellationRequested)
                {
                    attempt = LodGenerationAttempt.Cancelled();
                }

                m_Completions.Add(new LodGenerationCompletion(item.Source.Key, attempt, (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency));
            }
        }

        private sealed class WorkItem
        {
            internal WorkItem(LodCapturedSource source, LodProfile profile, LodCancellationSignal cancellation)
            {
                Source = source;
                Profile = profile;
                Cancellation = cancellation;
            }

            internal LodCapturedSource Source { get; }
            internal LodProfile Profile { get; }
            internal LodCancellationSignal Cancellation { get; }
        }
    }
}