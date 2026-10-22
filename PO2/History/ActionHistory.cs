using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PO2.Localization;

namespace PO2.History
{
    internal sealed class ActionHistory : IDisposable
    {
        private readonly List<IHistoryAction> m_Actions = new List<IHistoryAction>(HistoryLimits.kMaximumCapacity + 1);
        private readonly HistoryContext m_Context;
        private readonly Action<string> m_Log;
        private readonly int m_ThreadId = Thread.CurrentThread.ManagedThreadId;
        private Task<HistoryResult> m_Preparation;
        private CancellationTokenSource m_Cancellation;
        private IHistoryAction m_PendingAction;
        private HistoryDirection m_Direction;
        private HistoryTransaction m_Transaction;
        private bool m_Disposed;
        private bool m_Clearing;
        private bool m_EditingTimeline;
        internal bool IsReplayPending => IsReplaying || m_Preparation != null;

        internal int Count => m_Actions.Count;
        internal int Cursor { get; private set; }
        internal int Capacity { get; private set; } = HistoryLimits.kDefaultCapacity;
        internal bool IsReplaying { get; private set; }
        internal bool IsCapturing => m_Transaction != null;
        internal bool IsBusy => IsReplaying || m_Preparation != null || m_Transaction != null || m_Clearing || m_EditingTimeline;
        internal bool CanUndo => !m_Disposed && !IsBusy && Cursor > 0;
        internal bool CanRedo => !m_Disposed && !IsBusy && Cursor < Count;
        internal LocalizedText UndoDescription => Cursor > 0 ? m_Actions[Cursor - 1].Description : LocalizedText.Empty;
        internal LocalizedText RedoDescription => Cursor < Count ? m_Actions[Cursor].Description : LocalizedText.Empty;
        internal string LastFailure { get; private set; } = string.Empty;

        internal ActionHistory(HistoryContext context, Action<string> log = null)
        {
            m_Context = context;
            m_Log = log;
        }

        internal HistoryFootprint Footprint
        {
            get
            {
                var result = new HistoryFootprint();
                foreach (var action in m_Actions) result = result.Add(action.Footprint);
                return result;
            }
        }

        internal bool SetCapacity(int capacity)
        {
            CheckThread();
            if (m_Disposed || IsBusy) return false;
            m_EditingTimeline = true;
            try
            {
                Capacity = HistoryLimits.ClampCapacity(capacity);
                Trim();
            }
            finally
            {
                m_EditingTimeline = false;
            }

            return true;
        }

        // Hold the timeline still while the caller captures and applies an edit.
        internal HistoryTransaction TryBegin()
        {
            CheckThread();
            if (m_Disposed || IsBusy) return null;
            m_Transaction = new HistoryTransaction(this);
            return m_Transaction;
        }

        internal bool Commit(HistoryTransaction transaction, IHistoryAction action)
        {
            CheckThread();
            if (m_Disposed || m_Transaction != transaction || action == null || !action.HasChanges) return false;
            foreach (var existing in m_Actions)
                if (existing == action)
                    return false;
            // Capture all fallible metadata before disposing the redo branch.
            if (action.Description.IsEmpty) return false;
            m_EditingTimeline = true;
            try
            {
                RemoveRange(Cursor, Count - Cursor);
                m_Actions.Add(action);
                Cursor++;
                LastFailure = string.Empty;
                Trim();
                m_Transaction = null;
            }
            finally
            {
                m_EditingTimeline = false;
            }

            return true;
        }

        internal void Cancel(HistoryTransaction transaction)
        {
            CheckThread();
            if (m_Transaction == transaction) m_Transaction = null;
        }

        internal bool Request(HistoryDirection direction)
        {
            CheckThread();
            if (direction != HistoryDirection.Undo && direction != HistoryDirection.Redo) return false;
            if (direction == HistoryDirection.Undo ? !CanUndo : !CanRedo) return false;
            m_Direction = direction;
            m_PendingAction = m_Actions[direction == HistoryDirection.Undo ? Cursor - 1 : Cursor];
            m_Cancellation = new CancellationTokenSource();
            // Guard even synchronous Prepare code against reentry.
            IsReplaying = true;
            try
            {
                m_Preparation = HistoryPayloads.Prepare(m_PendingAction, direction, m_Cancellation.Token);
                if (m_Preparation == null) throw new InvalidOperationException("Missing history preparation task.");
            }
            catch (Exception exception)
            {
                m_Preparation = Task.FromResult(new HistoryResult(false, exception.Message, true));
            }
            finally
            {
                IsReplaying = false;
            }

            Poll();
            return true;
        }

        // A slider change waits for active capture/replay to finish before trimming.
        internal void Poll(int capacity)
        {
            Poll();
            if (Capacity != HistoryLimits.ClampCapacity(capacity)) SetCapacity(capacity);
        }

        internal bool CancelReplay()
        {
            CheckThread();
            if (m_Disposed || IsReplaying || m_Preparation == null) return false;
            if (m_Cancellation.IsCancellationRequested) return true;
            try
            {
                m_Cancellation.Cancel();
            }
            catch (Exception exception)
            {
                SafeLog("History cancellation: " + exception.Message);
            }

            (m_PendingAction as IHistoryOwnerCleanup)?.CancelPreparation();
            // Keep edits blocked until workers stop reading the prepared data.
            // Poll will discard their result without moving the history cursor.
            return true;
        }

        // Called from the rendering-phase lifecycle, including while paused.
        internal void Poll()
        {
            CheckThread();
            if (m_Disposed) return;
            PollBackgroundWork();
            if (IsReplaying || m_Preparation == null || !m_Preparation.IsCompleted) return;
            HistoryResult result;
            bool cancelled = m_Cancellation.IsCancellationRequested;
            IsReplaying = true;
            try
            {
                if (cancelled)
                {
                    var ignored = m_Preparation.Exception;
                    result = HistoryResult.Ok;
                    LastFailure = string.Empty;
                }
                else
                {
                    result = m_Preparation.GetAwaiter().GetResult();
                    if (result.Success) result = m_PendingAction.Replay(m_Direction, m_Context);
                    if (result.Success)
                    {
                        Cursor += m_Direction == HistoryDirection.Undo ? -1 : 1;
                        LastFailure = string.Empty;
                    }
                }
            }
            catch (Exception exception)
            {
                result = new HistoryResult(false, exception.ToString(), true);
            }
            finally
            {
                IsReplaying = false;
                m_Preparation = null;
                m_PendingAction = null;
                m_Cancellation.Dispose();
                m_Cancellation = null;
            }

            if (!result.Success)
            {
                if (result.InvalidateHistory) Clear();
                LastFailure = result.Reason;
            }

            if (!result.Success) SafeLog($"[PO History] {m_Direction} failed: {result.Reason}");
        }

        internal void Clear()
        {
            CheckThread();
            if (m_Clearing) return;
            // Live replay is synchronous on this thread; a nested clear would
            // dispose the action underneath its own mutation.
            if (IsReplaying || m_EditingTimeline) throw new InvalidOperationException("Cannot clear during history mutation.");
            m_Clearing = true;
            try
            {
                m_Transaction?.Invalidate();
                m_Transaction = null;
                var pending = m_PendingAction;
                var task = m_Preparation;
                var cancellation = m_Cancellation;
                m_PendingAction = null;
                m_Preparation = null;
                m_Cancellation = null;
                if (pending != null)
                {
                    try
                    {
                        cancellation.Cancel();
                    }
                    catch (Exception exception)
                    {
                        SafeLog("History cancellation: " + exception.Message);
                    }

                    (pending as IHistoryOwnerCleanup)?.CancelPreparation();
                    // Do not dispose buffers while a decoder is still using them.
                    // This continuation has no model/context reference and never replays.
                    task.ContinueWith(completed =>
                    {
                        var ignored = completed.Exception;
                        try
                        {
                            pending.Dispose();
                        }
                        catch
                        {
                        }

                        cancellation.Dispose();
                    }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                }

                for (int i = 0; i < Count; i++)
                {
                    // The pending action is released by its continuation above.
                    if (m_Actions[i] != pending) SafeDispose(m_Actions[i]);
                }

                m_Actions.Clear();
                m_Context?.Layouts.Clear();
                Cursor = 0;
                LastFailure = string.Empty;
            }
            finally
            {
                m_Clearing = false;
            }
        }

        private void PollBackgroundWork()
        {
            foreach (var action in m_Actions)
            {
                (action as IHistoryBackgroundWork)?.PollBackgroundWork(SafeLog);
            }
        }

        private void Trim()
        {
            int removeApplied = Math.Min(Cursor, Math.Max(0, Count - Capacity));
            RemoveRange(0, removeApplied);
            Cursor -= removeApplied;
            if (Count > Capacity) RemoveRange(Capacity, Count - Capacity);
        }

        private void RemoveRange(int start, int count)
        {
            for (int i = start; i < start + count; i++) SafeDispose(m_Actions[i]);
            m_Actions.RemoveRange(start, count);
        }

        private void SafeDispose(IHistoryAction action)
        {
            try
            {
                action.Dispose();
            }
            catch (Exception exception)
            {
                SafeLog("History disposal: " + exception.Message);
            }
        }

        private void SafeLog(string message)
        {
            try
            {
                m_Log?.Invoke(message);
            }
            catch
            {
            }
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != m_ThreadId)
            {
                throw new InvalidOperationException("History mutation must stay on the game thread.");
            }
        }

        public void Dispose()
        {
            CheckThread();
            if (m_Disposed) return;
            Clear();
            m_Disposed = true;
        }
    }
}