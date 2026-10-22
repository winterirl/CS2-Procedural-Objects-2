using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PO2.Geometry.Picking
{
    // Keep a small number of detached builds running without waiting on the game thread.
    internal sealed class ObjectPickWorker
    {
        private readonly int m_Capacity;
        private readonly List<Build> m_Builds = new List<Build>();

        private sealed class Build
        {
            internal Task<TrianglePickCache> Task;
            internal ObjectPickCacheState Target;
            internal uint Revision;
        }

        internal ObjectPickWorker(int capacity = 0)
        {
            m_Capacity = capacity > 0 ? capacity : (Environment.ProcessorCount >= 4 ? 2 : 1);
        }

        internal bool IsBusy => m_Builds.Count != 0;
        internal bool HasCapacity => m_Builds.Count < m_Capacity;

        internal bool IsBuilding(ObjectPickCacheState target)
        {
            foreach (var build in m_Builds)
                if (ReferenceEquals(build.Target, target))
                    return true;
            return false;
        }

        internal bool TryStart(ObjectPickCacheState target, Func<TrianglePickCache> build)
        {
            if (!HasCapacity || IsBuilding(target)) return false;
            m_Builds.Add(new Build { Target = target, Revision = target.Revision, Task = Task.Run(build) });
            return true;
        }

        internal Exception Poll()
        {
            Exception firstError = null;
            for (int i = m_Builds.Count - 1; i >= 0; i--)
            {
                var build = m_Builds[i];
                if (!build.Task.IsCompleted) continue;
                Exception error = build.Task.IsFaulted ? build.Task.Exception.GetBaseException() : build.Task.IsCanceled ? new OperationCanceledException("Picking cache build cancelled.") : null;
                if (build.Target != null && build.Target.Revision == build.Revision)
                {
                    build.Target.Cache = error == null ? build.Task.Result : null;
                    build.Target.RefitSource = null;
                    build.Target.Failed = error != null;
                }

                if (error != null && firstError == null) firstError = error;
                m_Builds.RemoveAt(i);
            }

            return firstError;
        }

        internal void Detach()
        {
            foreach (var build in m_Builds)
            {
                build.Target = null;
                build.Task.ContinueWith(task =>
                {
                    var observed = task.Exception;
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }
}