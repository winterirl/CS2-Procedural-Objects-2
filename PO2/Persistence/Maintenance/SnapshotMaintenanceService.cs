using System;
using System.Threading;
using System.Threading.Tasks;

namespace PO2.Persistence
{
    internal sealed class MaintenanceProgress
    {
        private int m_Checked;
        private int m_Deleted;
        private long m_DeletedBytes;
        internal int Checked => Volatile.Read(ref m_Checked);
        internal int Deleted => Volatile.Read(ref m_Deleted);
        internal long DeletedBytes => Interlocked.Read(ref m_DeletedBytes);
        internal void CheckedFile() { Interlocked.Increment(ref m_Checked); }
        internal void DeletedFile(long bytes)
        {
            Interlocked.Add(ref m_DeletedBytes, bytes);
            Interlocked.Increment(ref m_Deleted);
        }
    }

    internal sealed class MaintenanceWork
    {
        internal readonly CancellationToken Token;
        internal readonly MaintenanceProgress Progress;
        private int m_Items;

        internal MaintenanceWork(CancellationToken token, MaintenanceProgress progress)
        {
            Token = token;
            Progress = progress;
        }

        internal void Checkpoint()
        {
            Token.ThrowIfCancellationRequested();
            // Give other disk users a turn during a large backlog scan.
            if ((++m_Items & 31) == 0 && Token.WaitHandle.WaitOne(5)) Token.ThrowIfCancellationRequested();
        }
    }

    internal sealed class SnapshotMaintenanceService : IDisposable
    {
        private Task<object> m_Task;
        private CancellationTokenSource m_Cancellation;
        internal MaintenanceProgress Progress { get; private set; } = new MaintenanceProgress();
        internal bool HasWork => m_Task != null;

        internal void Start(Func<MaintenanceWork, object> operation)
        {
            if (HasWork) throw new InvalidOperationException("Snapshot maintenance is already running.");
            m_Cancellation = new CancellationTokenSource();
            var work = new MaintenanceWork(m_Cancellation.Token, new MaintenanceProgress());
            Progress = work.Progress;
            m_Task = Task.Run(() => operation(work), work.Token);
        }

        internal void Cancel() { m_Cancellation?.Cancel(); }

        internal bool TryTake(out object result, out Exception error)
        {
            result = null;
            error = null;
            if (m_Task == null || !m_Task.IsCompleted) return false;
            try { result = m_Task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { error = exception; }
            m_Task = null;
            m_Cancellation.Dispose();
            m_Cancellation = null;
            return true;
        }

        public void Dispose()
        {
            Cancel();
            // The worker only owns detached data. Shutdown never joins disk work.
            if (m_Task != null)
            {
                CancellationTokenSource cancellation = m_Cancellation;
                m_Task.ContinueWith(task =>
                {
                    if (task.IsFaulted) _ = task.Exception;
                    cancellation.Dispose();
                }, TaskScheduler.Default);
                m_Task = null;
                m_Cancellation = null;
            }
        }
    }
}
