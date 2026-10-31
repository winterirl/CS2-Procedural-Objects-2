using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using PO2.Persistence;

internal static partial class LoadingTests
{
    private static void DetachedBatchChecks()
    {
        Throws<ArgumentOutOfRangeException>(() => LoadingBatch.RunDetached(-1, _ => { }));
        Throws<ArgumentNullException>(() => LoadingBatch.RunDetached(0, null));
        LoadingBatch.RunDetached(0, _ => throw new Exception("empty batch ran"));
        int caller = Thread.CurrentThread.ManagedThreadId;
        LoadingBatch.RunDetached(5, _ => Check(Thread.CurrentThread.ManagedThreadId == caller, "detached serial caller"), 1);

        foreach (int workers in new[] { 1, 2, 8, 16, 40 })
        {
            int count = workers == 40 ? 7 : 64;
            int active = 0, peak = 0;
            var hits = new int[count];
            var outputs = new int[count];
            int expectedWorkers = Math.Min(count, workers);
            using (var arrived = new CountdownEvent(expectedWorkers))
            {
                LoadingBatch.RunDetached(count, i =>
                {
                    int running = Interlocked.Increment(ref active);
                    RaisePeak(ref peak, running);
                    try
                    {
                        if (i < expectedWorkers)
                        {
                            arrived.Signal();
                            if (!arrived.Wait(15000)) throw new Exception("detached workers did not overlap");
                        }
                        Interlocked.Increment(ref hits[i]);
                        outputs[i] = i + 100;
                    }
                    finally { Interlocked.Decrement(ref active); }
                }, workers);
            }
            Check(active == 0 && peak == expectedWorkers, "detached active count");
            Check(hits.All(x => x == 1), "detached each index once");
            Same(outputs, Enumerable.Range(100, count), "detached result slots");
        }

        using (var beyondWindow = new ManualResetEventSlim())
        {
            LoadingBatch.RunDetached(64, i =>
            {
                if (i == 0 && !beyondWindow.Wait(15000)) throw new Exception("detached head blocked later admission");
                if (i == 40) beyondWindow.Set();
            }, 2);
            Check(beyondWindow.IsSet, "detached completion passes old pending window");
        }

        int autoStarts = 0;
        LoadingBatch.RunDetachedCore(100, _ => { }, 0, action =>
        {
            autoStarts++;
            return Task.Run(action);
        });
        Check(autoStarts == (LoadingBatch.WorkerCount == 1 ? 0 : Math.Min(100, LoadingBatch.WorkerCount)), "detached CPU default");
        DetachedFailureDrain(false);
        DetachedFailureDrain(true);
        using (var together = new CountdownEvent(2))
        {
            var first = new InvalidOperationException("first");
            var second = new InvalidOperationException("second");
            var failure = FailureOf(() => LoadingBatch.RunDetached(20, i =>
            {
                together.Signal();
                if (!together.Wait(15000)) throw new Exception("concurrent failures did not meet");
                ThrowDetachedFailure(i == 0 ? first : second);
            }, 2));
            Check(ReferenceEquals(failure, first) || ReferenceEquals(failure, second), "detached simultaneous failure retained");
        }

        var batches = Enumerable.Range(0, 4).Select(batch => Task.Run(() =>
        {
            var slots = new int[100];
            LoadingBatch.RunDetached(slots.Length, i => slots[i] = batch * 100 + i, 4);
            return slots;
        })).ToArray();
        Task.WaitAll(batches);
        for (int i = 0; i < batches.Length; i++) Same(batches[i].Result, Enumerable.Range(i * 100, 100), "independent detached batches");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowDetachedFailure(Exception exception) { throw exception; }

    private static void DetachedFailureDrain(bool launchFailure)
    {
        using (var held = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        using (var secondStarted = new ManualResetEventSlim())
        {
            var primary = new InvalidOperationException(launchFailure ? "launch" : "build");
            int starts = 0, claims = 0, active = 0;
            Task second = null;
            var caller = Task.Factory.StartNew(() => FailureOf(() => LoadingBatch.RunDetachedCore(30, i =>
            {
                Interlocked.Increment(ref claims);
                Interlocked.Increment(ref active);
                try
                {
                    if (i == 0)
                    {
                        held.Set();
                        if (!release.Wait(15000)) throw new Exception("held worker was not released");
                    }
                    else ThrowDetachedFailure(primary);
                }
                finally { Interlocked.Decrement(ref active); }
            }, 2, action =>
            {
                if (++starts == 1)
                {
                    var task = Task.Run(action);
                    if (!held.Wait(15000)) throw new Exception("first worker did not claim index zero");
                    return task;
                }
                if (launchFailure)
                {
                    secondStarted.Set();
                    ThrowDetachedFailure(primary);
                }
                second = Task.Run(action);
                secondStarted.Set();
                return second;
            })), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            try
            {
                Check(secondStarted.Wait(15000), "second worker launch reached");
                if (!launchFailure)
                {
                    Check(second.Wait(15000), "failing worker finished");
                    Check(Volatile.Read(ref claims) == 2, "failure stopped subsequent claims");
                }
                Check(!caller.IsCompleted && Volatile.Read(ref active) == 1, "failure waits for held worker");
            }
            finally { release.Set(); }
            Check(caller.Wait(15000), "detached caller drained");
            Check(ReferenceEquals(caller.Result, primary), "detached original exception retained");
            Check(caller.Result.StackTrace.Contains(nameof(ThrowDetachedFailure)), "detached failure stack retained");
            Check(Volatile.Read(ref active) == 0, "no detached work survives failure");
        }
    }
}
