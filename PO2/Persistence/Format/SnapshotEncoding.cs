using System;
using System.Diagnostics;

namespace PO2.Persistence.Format
{
    public enum SaveCompression
    {
        Compact = 0,
        Fast = 1,
    }

    // These are just savetime choices, not fields in the .po2 format 
    internal sealed class SnapshotEncodingOptions
    {
        internal SaveCompression Compression { get; }
        internal int MaxWorkers { get; }

        internal SnapshotEncodingOptions(SaveCompression compression, int maxWorkers = 0)
        {
            if (compression != SaveCompression.Compact && compression != SaveCompression.Fast)
                throw new ArgumentOutOfRangeException(nameof(compression));
            Compression = compression;
            MaxWorkers = maxWorkers > 0 ? maxWorkers : LoadingBatch.WorkerCount;
        }
    }

    // Each worker owns its counters. The writer combines them as ordered results are consumed.
    internal sealed class EncodingMetrics
    {
        internal long CompressionTicks, CompressionInputBytes, PartTicks;
        internal int CompressionCalls, PartCount, PeakWorkers;
        internal double CompressionMs => CompressionTicks * 1000d / Stopwatch.Frequency;
        internal double PartWorkMs => PartTicks * 1000d / Stopwatch.Frequency;

        internal void Add(EncodingMetrics other)
        {
            CompressionTicks += other.CompressionTicks;
            CompressionInputBytes += other.CompressionInputBytes;
            CompressionCalls += other.CompressionCalls;
            PartTicks += other.PartTicks;
            PartCount += other.PartCount;
        }
    }

    internal sealed class EncodingContext
    {
        internal readonly SaveCompression Compression;
        internal readonly EncodingMetrics Metrics;

        internal EncodingContext(SaveCompression compression, EncodingMetrics metrics = null)
        {
            Compression = compression;
            Metrics = metrics;
        }

        internal EncodedBuffer Deflate(EncodedBuffer bytes)
        {
            if (Metrics == null) return SnapshotBinary.DeflateBuffer(bytes);
            long start = Stopwatch.GetTimestamp();
            EncodedBuffer result = SnapshotBinary.DeflateBuffer(bytes);
            Metrics.CompressionTicks += Stopwatch.GetTimestamp() - start;
            Metrics.CompressionInputBytes += bytes.Length;
            Metrics.CompressionCalls++;
            return result;
        }
    }

}
