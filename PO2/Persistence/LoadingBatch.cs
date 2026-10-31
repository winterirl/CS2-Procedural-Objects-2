using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace PO2.Persistence
{
    internal static class LoadingBatch
    {
        internal static int WorkerCount => Math.Max(1, Environment.ProcessorCount);

        internal static void Run<T>(int count, Func<int, T> build,
            Action<int, T> consume, Action<T> release = null, int workers = 0)
        {
            OrderedWorkQueue.Run(count, i => i, (i, input) => build(input),
                consume, null, release, workers > 0 ? workers : WorkerCount);
        }

        // Each index owns its output slot. There is nothing to publish until
        // all parts finish, so a slow early part need not hold up later work.
        internal static void RunDetached(int count, Action<int> build, int workers = 0)
        {
            RunDetachedCore(count, build, workers, action => Task.Run(action));
        }

        // Keeping task creation here also lets the standalone tests exercise a
        // launch failure after another worker has already claimed some work.
        internal static void RunDetachedCore(int count, Action<int> build,
            int workers, Func<Action, Task> start)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (start == null) throw new ArgumentNullException(nameof(start));
            if (count == 0) return;

            int workerCount = Math.Min(count, workers > 0 ? workers : WorkerCount);
            if (workerCount == 1)
            {
                for (int i = 0; i < count; i++) build(i);
                return;
            }

            var gate = new object();
            int next = 0;
            ExceptionDispatchInfo failure = null;
            var tasks = new Task[workerCount];

            void RecordFailure(Exception exception)
            {
                lock (gate)
                {
                    if (failure == null) failure = ExceptionDispatchInfo.Capture(exception);
                }
            }

            void Worker()
            {
                while (true)
                {
                    int index;
                    lock (gate)
                    {
                        if (failure != null || next == count) return;
                        index = next++;
                    }
                    try { build(index); }
                    catch (Exception exception)
                    {
                        RecordFailure(exception);
                        return;
                    }
                }
            }

            try
            {
                for (int i = 0; i < tasks.Length; i++)
                {
                    lock (gate)
                    {
                        if (failure != null) break;
                    }
                    tasks[i] = start(Worker) ?? throw new InvalidOperationException("Detached worker did not start.");
                }
            }
            catch (Exception exception) { RecordFailure(exception); }
            finally
            {
                // Inputs and output slots must stay alive until every started
                // worker is done, even if a later task could not be launched.
                foreach (var task in tasks)
                {
                    if (task == null) continue;
                    try { task.GetAwaiter().GetResult(); }
                    catch (Exception exception) { RecordFailure(exception); }
                }
            }
            failure?.Throw();
        }
    }
}
