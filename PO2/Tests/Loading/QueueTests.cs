using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PO2.Persistence;
using PO2.Persistence.Format;

internal static partial class LoadingTests
{
    private static void QueueChecks()
    {
        Check(LoadingBatch.WorkerCount == Math.Max(1, Environment.ProcessorCount), "CPU-based loading workers");
        foreach (int requested in new[] { -1, 0, 1, 4, 8, 16, 32 })
        {
            var options = new SnapshotEncodingOptions(SaveCompression.Fast, requested);
            Check(options.MaxWorkers == (requested > 0 ? requested : LoadingBatch.WorkerCount), "encoding worker choice " + requested);
        }
        QueueParallelism();
        QueueWindow();
        QueueFailures();
        QueueAdmission();

        int caller = Thread.CurrentThread.ManagedThreadId;
        foreach (int count in new[] { 0, 1, 3 })
        foreach (int workers in new[] { 1, 16 })
        {
            int prepared = 0, consumed = 0, released = 0;
            OrderedWorkQueue.Run(count, i => { prepared++; return i; }, (i, input) =>
            {
                if (workers == 1 && Thread.CurrentThread.ManagedThreadId != caller)
                    throw new Exception("serial build left caller");
                return input;
            }, (i, result) => Check(i == consumed++ && result == i, "small queue order"),
                input => released++, null, workers);
            Check(prepared == count && consumed == count && released == count, "empty/small queue lifetime");
        }
        foreach (int workers in new[] { 0, -1, int.MaxValue })
            Throws<ArgumentOutOfRangeException>(() => OrderedWorkQueue.Run(1, i => i, (i, x) => x, (i, x) => { }, null, null, workers));
        Throws<ArgumentOutOfRangeException>(() => OrderedWorkQueue.Run(-1, i => i, (i, x) => x, (i, x) => { }, null, null, 1));
    }

    private static void RaisePeak(ref int peak, int value)
    {
        int old;
        do { old = Volatile.Read(ref peak); if (old >= value) return; }
        while (Interlocked.CompareExchange(ref peak, value, old) != old);
    }

    private static void QueueParallelism()
    {
        const int workers = 8;
        int active = 0, peak = 0, consumed = 0, released = 0;
        using (var arrived = new CountdownEvent(workers))
        {
            LoadingBatch.Run(40, i =>
            {
                int running = Interlocked.Increment(ref active);
                RaisePeak(ref peak, running);
                try
                {
                    if (running > workers) throw new Exception("too many active builds");
                    // All eight builds must be alive together. A four-worker
                    // clamp cannot pass just because the jobs finish quickly.
                    if (i < workers)
                    {
                        arrived.Signal();
                        if (!arrived.Wait(15000)) throw new Exception("eight workers did not overlap");
                    }
                    return i;
                }
                finally { Interlocked.Decrement(ref active); }
            }, (i, result) => Check(i == consumed++ && result == i, "wide queue ordered results"),
                result => released++, workers);
        }
        Check(peak == workers && active == 0 && consumed == 40 && released == 40, "wide queue drains");
    }

    private static void QueueWindow()
    {
        const int workers = 8;
        int caller = Thread.CurrentThread.ManagedThreadId;
        int held = 0, maximum = 0, prepared = 0;
        using (var tails = new CountdownEvent(workers - 1))
        using (var refilled = new ManualResetEventSlim())
        {
            var order = new List<int>();
            OrderedWorkQueue.Run(40, i =>
            {
                Check(Thread.CurrentThread.ManagedThreadId == caller, "prepare on caller");
                prepared++;
                maximum = Math.Max(maximum, ++held);
                Check(held <= workers + 1, "pending window includes completed results");
                return i;
            }, (i, input) =>
            {
                // The first result cannot finish until later builds have run.
                if (i == 0 && !tails.Wait(15000)) throw new Exception("head wait deadlock");
                if (i > 0 && i < workers) tails.Signal();
                if (i == workers) refilled.Set();
                return input;
            }, (i, result) =>
            {
                Check(Thread.CurrentThread.ManagedThreadId == caller, "consume on caller");
                if (i == 0)
                {
                    Check(refilled.Wait(15000), "refill overlaps head consumption");
                    Check(prepared == workers + 1 && held == workers + 1, "consumer wait does not admit whole input");
                }
                order.Add(result);
            }, input =>
            {
                Check(Thread.CurrentThread.ManagedThreadId == caller, "input release on caller");
                held--;
            }, result => Check(Thread.CurrentThread.ManagedThreadId == caller, "result release on caller"), workers);
            Same(order, Enumerable.Range(0, 40), "out-of-order completion preserves order");
        }
        Check(maximum == workers + 1 && held == 0, "bounded pending window released");
    }

    private static void QueueFailures()
    {
        foreach (int workers in new[] { 1, 8, 16 })
        for (int failureStage = 0; failureStage < 5; failureStage++)
        {
            const int count = 40;
            var prepared = new bool[count];
            var built = new bool[count];
            var releasedInputs = new int[count];
            var releasedResults = new int[count];
            Throws<InvalidOperationException>(() => OrderedWorkQueue.Run(count, i =>
            {
                if (failureStage == 0 && i == 3) throw new InvalidOperationException("prepare");
                prepared[i] = true;
                return i;
            }, (i, input) =>
            {
                if (failureStage == 1 && i == 3) throw new InvalidOperationException("build");
                built[i] = true;
                return input;
            }, (i, result) =>
            {
                if (failureStage == 2 && i == 2) throw new InvalidOperationException("consume");
            }, input =>
            {
                releasedInputs[input]++;
                if (failureStage == 3 && input == 2) throw new InvalidOperationException("release input");
            }, result =>
            {
                releasedResults[result]++;
                if (failureStage == 4 && result == 2) throw new InvalidOperationException("release result");
            }, workers));
            for (int i = 0; i < count; i++)
            {
                Check(releasedInputs[i] == (prepared[i] ? 1 : 0), "input released once after failure");
                Check(releasedResults[i] == (built[i] ? 1 : 0), "result released once after failure");
            }
        }
        var primary = new InvalidOperationException("primary");
        try
        {
            OrderedWorkQueue.Run(20, i => i, (i, input) => input,
                (i, result) => throw primary,
                input => throw new Exception("secondary input"), result => throw new Exception("secondary result"), 16);
            throw new Exception("missing queue failure");
        }
        catch (InvalidOperationException exception) { Check(ReferenceEquals(exception, primary), "cleanup preserves primary failure"); }
    }

    private static void QueueAdmission()
    {
        int held = 0, maximum = 0, consumed = 0, blocked = 0;
        OrderedWorkQueue.Run(20, i => { maximum = Math.Max(maximum, ++held); return i; },
            (i, input) => input, (i, result) => Check(i == consumed++ && result == i, "admission preserves order"),
            input => held--, null, 8, i =>
            {
                if (held < 2) return true;
                blocked++;
                return false;
            });
        Check(blocked > 0 && maximum == 2 && held == 0 && consumed == 20, "admission resumes after release");
        Throws<InvalidOperationException>(() => OrderedWorkQueue.Run(1, i => i, (i, x) => x,
            (i, x) => { }, null, null, 1, i => false));
    }
}
