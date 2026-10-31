using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace PO2.Persistence
{
    // Inputs and consumers belong to the caller. Only detached preparation runs
    // on workers. Finished results still occupy a slot until consumed/released.
    internal static class OrderedWorkQueue
    {
        private sealed class Slot<TInput, TResult>
        {
            internal int Index;
            internal TInput Input;
            internal TResult Result;
            internal bool HasInput, HasResult;
            internal Task<TResult> Task;

            internal void Join()
            {
                if (Task == null) return;
                var task = Task;
                Task = null;
                Result = task.GetAwaiter().GetResult();
                HasResult = true;
            }

            internal void Release(Action<TInput> input, Action<TResult> result)
            {
                Exception failure = null;
                try { Join(); }
                catch (Exception exception) { failure = exception; }
                try
                {
                    if (HasResult)
                    {
                        HasResult = false;
                        try { result?.Invoke(Result); }
                        finally { Result = default(TResult); }
                    }
                }
                catch (Exception exception) { if (failure == null) failure = exception; }
                try
                {
                    if (HasInput)
                    {
                        HasInput = false;
                        try { input?.Invoke(Input); }
                        finally { Input = default(TInput); }
                    }
                }
                catch (Exception exception) { if (failure == null) failure = exception; }
                if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        internal static void Run<TInput, TResult>(int count,
            Func<int, TInput> prepare, Func<int, TInput, TResult> build,
            Action<int, TResult> consume, Action<TInput> releaseInput,
            Action<TResult> releaseResult, int workers,
            Func<int, bool> canAdmit = null)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (workers < 1 || workers == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(workers));
            if (prepare == null || build == null || consume == null) throw new ArgumentNullException();
            var pending = new List<Slot<TInput, TResult>>(Math.Min(count, workers + 1));
            int next = 0;
            Exception failure = null;

            void Fill()
            {
                while (next < count && pending.Count < workers + 1)
                {
                    int running = 0;
                    foreach (var item in pending)
                    {
                        if (item.Task == null) continue;
                        if (item.Task.IsFaulted || item.Task.IsCanceled) item.Join();
                        if (item.Task != null && !item.Task.IsCompleted) running++;
                    }
                    if (running >= workers) break;
                    if (canAdmit != null && !canAdmit(next))
                    {
                        if (pending.Count == 0) throw new InvalidOperationException("Work admission cannot make progress.");
                        break;
                    }
                    var slot = new Slot<TInput, TResult> { Index = next++ };
                    pending.Add(slot);
                    slot.Input = prepare(slot.Index);
                    slot.HasInput = true;
                    if (workers == 1)
                    {
                        slot.Result = build(slot.Index, slot.Input);
                        slot.HasResult = true;
                        break;
                    }
                    slot.Task = Task.Run(() => build(slot.Index, slot.Input));
                }
            }

            try
            {
                while (next < count || pending.Count != 0)
                {
                    Fill();
                    var head = pending[0];
                    head.Join();
                    // Keep the head in its slot while the next build overlaps its
                    // consumer (which may be uploading a mesh on the main thread).
                    if (workers > 1) Fill();
                    consume(head.Index, head.Result);
                    head.Release(releaseInput, releaseResult);
                    pending.RemoveAt(0);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                foreach (var slot in pending)
                {
                    try { slot.Release(releaseInput, releaseResult); }
                    catch (Exception exception) { if (failure == null) failure = exception; }
                }
                pending.Clear();
            }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
