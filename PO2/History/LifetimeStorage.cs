using System;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core;
using PO2.Geometry;

namespace PO2.History
{
    // Keep the detached model until its background encoding succeeds.
    internal sealed class LifetimeStorage : IDisposable
    {
        private static readonly SemaphoreSlim s_EncodingGate = new SemaphoreSlim(1, 1);
        private readonly object m_Gate = new object();
        private LifetimePayload m_Payload;
        private Task<LifetimePayload> m_Encoding;
        private Task<HistoryResult> m_Preparation;
        private CancellationTokenSource m_Cancellation;
        private bool m_Requested, m_Started, m_Disposed;
        private string m_Message;
        internal long EstimatedBytes { get; }
        internal Task Drain { get; private set; } = Task.CompletedTask;

        internal bool IsCold
        {
            get
            {
                lock (m_Gate) return m_Payload?.IsCold ?? false;
            }
        }

        internal long ManagedBytes
        {
            get
            {
                lock (m_Gate) return m_Payload?.ManagedBytes ?? 0;
            }
        }

        internal HistoryResidentPayload EncodedPayload
        {
            get
            {
                lock (m_Gate) return m_Payload?.EncodedPayload;
            }
        }

        internal LifetimeStorage(LifetimePayload payload)
        {
            m_Payload = payload;
            EstimatedBytes = payload.EstimatedBytes;
        }

        internal void RequestCoolingIfLarge()
        {
            lock (m_Gate)
            {
                if (!m_Disposed && m_Payload.ShouldCool) m_Requested = true;
            }
        }

        internal void RequestCooling()
        {
            lock (m_Gate)
            {
                if (!m_Disposed) m_Requested = true;
            }
        }

        internal void PollBackgroundWork(Action<string> log, Action<LifetimePayload> encode = null)
        {
            string message;
            lock (m_Gate)
            {
                if (m_Disposed) return;
                FinishEncoding();
                if (m_Requested && !m_Started && !m_Payload.IsCold)
                {
                    m_Started = true;
                    LifetimePayload snapshot = null;
                    try
                    {
                        // This only copies references and identity metadata. The model is
                        // detached, and replay waits before making it editable again.
                        snapshot = m_Payload.CopyForEncoding();
                        m_Cancellation = new CancellationTokenSource();
                        var token = m_Cancellation.Token;
                        var owned = snapshot;
                        m_Encoding = Task.Run(async () =>
                        {
                            try
                            {
                                await s_EncodingGate.WaitAsync(token).ConfigureAwait(false);
                                try
                                {
                                    token.ThrowIfCancellationRequested();
                                    if (encode == null) owned.Cool();
                                    else encode(owned);
                                    token.ThrowIfCancellationRequested();
                                    return owned;
                                }
                                finally
                                {
                                    s_EncodingGate.Release();
                                }
                            }
                            catch
                            {
                                owned.Dispose();
                                throw;
                            }
                        });
                        Drain = m_Encoding;
                    }
                    catch (Exception exception)
                    {
                        snapshot?.Dispose();
                        m_Cancellation?.Dispose();
                        m_Cancellation = null;
                        m_Message = FailureMessage(exception.Message);
                    }
                }

                message = log == null ? null : m_Message;
                if (log != null) m_Message = null;
            }

            if (message != null) log(message);
        }

        // Preparation can be called by a compound action's continuation.
        internal Task<HistoryResult> Prepare(CancellationToken token)
        {
            lock (m_Gate)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(LifetimeStorage));
                m_Started = true;
                if (m_Preparation != null && m_Preparation.IsCanceled) m_Preparation = null;
                if (m_Preparation == null) m_Preparation = PrepareCore(m_Encoding, token);
                Drain = m_Preparation;
                return m_Preparation;
            }
        }

        private async Task<HistoryResult> PrepareCore(Task<LifetimePayload> encoding, CancellationToken token)
        {
            if (encoding != null)
            {
                try
                {
                    await encoding.ConfigureAwait(false);
                }
                catch
                {
                    /* Encoding is optional; the detached model is still intact. */
                }
            }

            token.ThrowIfCancellationRequested();
            LifetimePayload payload;
            lock (m_Gate)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(LifetimeStorage));
                FinishEncoding();
                payload = m_Payload;
            }

            await payload.Prepare(token).ConfigureAwait(false);
            return HistoryResult.Ok;
        }

        private void FinishEncoding()
        {
            if (m_Encoding == null || !m_Encoding.IsCompleted) return;
            var task = m_Encoding;
            m_Encoding = null;
            m_Cancellation.Dispose();
            m_Cancellation = null;
            if (task.Status == TaskStatus.RanToCompletion)
            {
                var payload = task.Result;
                m_Payload.Dispose();
                m_Payload = payload;
            }
            else m_Message = FailureMessage(task.Exception?.GetBaseException().Message ?? "Encoding cancelled.");

            Drain = Task.CompletedTask;
        }

        private static string FailureMessage(string reason) =>
            "[PO History] Keeping the detached object after background encoding failed: " + reason;

        private void RequireReady()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(LifetimeStorage));
            FinishEncoding();
            if (m_Encoding != null) throw new InvalidOperationException("Lifetime history is still preparing.");
            if (m_Preparation != null)
            {
                if (!m_Preparation.IsCompleted) throw new InvalidOperationException("Lifetime history is still decoding.");
                m_Preparation.GetAwaiter().GetResult();
            }

            m_Started = true;
        }

        internal ProceduralObject GetObject()
        {
            lock (m_Gate)
            {
                RequireReady();
                return m_Payload.GetObject();
            }
        }

        internal MeshPart GetPart()
        {
            lock (m_Gate)
            {
                RequireReady();
                return m_Payload.GetPart();
            }
        }

        internal void Cool()
        {
            lock (m_Gate)
            {
                RequireReady();
                m_Payload.Cool();
            }
        }

        public void Dispose()
        {
            lock (m_Gate)
            {
                if (m_Disposed) return;
                m_Disposed = true;
                var payload = m_Payload;
                m_Payload = null;
                var task = m_Encoding;
                m_Encoding = null;
                var cancellation = m_Cancellation;
                m_Cancellation = null;
                cancellation?.Cancel();
                var work = Task.WhenAll((Task)task ?? Task.CompletedTask, m_Preparation ?? Task.CompletedTask);
                // Decoding may still read the encoded bytes after history is cleared.
                Action release = () =>
                {
                    var ignored = work.Exception;
                    payload.Dispose();
                    if (task?.Status == TaskStatus.RanToCompletion && !ReferenceEquals(task.Result, payload)) task.Result.Dispose();
                    cancellation?.Dispose();
                };
                if (work.IsCompleted)
                {
                    release();
                    Drain = Task.CompletedTask;
                }
                else Drain = work.ContinueWith(_ => release(), TaskScheduler.Default);
            }
        }
    }
}