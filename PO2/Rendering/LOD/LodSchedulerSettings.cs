using System;

namespace PO2.Rendering.Lod
{
    internal sealed class LodSchedulerSettings
    {
        internal static LodSchedulerSettings CreateRuntimeDefaults()
        {
            return new LodSchedulerSettings(maximumOutstandingJobs: 2, maximumCaptureBytesPerUpdate: 1024 * 1024, maximumCompletionsPerUpdate: 2);
        }

        internal LodSchedulerSettings(int maximumOutstandingJobs, int maximumCaptureBytesPerUpdate, int maximumCompletionsPerUpdate)
        {
            if (maximumOutstandingJobs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumOutstandingJobs));
            }

            // Vector4 is the largest captured element. Requiring one complete
            // element guarantees every capture can make forward progress.
            if (maximumCaptureBytesPerUpdate < 16)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCaptureBytesPerUpdate));
            }

            if (maximumCompletionsPerUpdate < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCompletionsPerUpdate));
            }

            MaximumOutstandingJobs = maximumOutstandingJobs;
            MaximumCaptureBytesPerUpdate = maximumCaptureBytesPerUpdate;
            MaximumCompletionsPerUpdate = maximumCompletionsPerUpdate;
        }

        internal int MaximumOutstandingJobs { get; }
        internal int MaximumCaptureBytesPerUpdate { get; }
        internal int MaximumCompletionsPerUpdate { get; }
    }
}