using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using PO2.Persistence.Model;

namespace PO2.Persistence.Format
{
    public enum SaveCompression
    {
        Compact = 0,
        Fast = 1,
    }

    // These are save-time choices, not fields in the .po2 format.
    internal sealed class SnapshotEncodingOptions
    {
        internal const long kDefaultTemporaryBudget = 128L * 1024 * 1024;
        internal SaveCompression Compression { get; }
        internal int MaxWorkers { get; }
        internal long TemporaryBudget { get; }

        internal SnapshotEncodingOptions(SaveCompression compression, int maxWorkers = 0, long temporaryBudget = kDefaultTemporaryBudget)
        {
            if (compression != SaveCompression.Compact && compression != SaveCompression.Fast)
                throw new ArgumentOutOfRangeException(nameof(compression));
            if (temporaryBudget <= 0) throw new ArgumentOutOfRangeException(nameof(temporaryBudget));
            Compression = compression;
            MaxWorkers = Math.Max(1, Math.Min(4, maxWorkers <= 0 ? Environment.ProcessorCount - 1 : maxWorkers));
            TemporaryBudget = temporaryBudget;
        }
    }

    // Each worker owns its counters. The writer combines them after it joins the batch.
    internal sealed class EncodingMetrics
    {
        internal long CompressionTicks, CompressionInputBytes, PartTicks;
        internal int CompressionCalls, PartCount, ParallelBatches, PeakWorkers;
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

        internal byte[] Deflate(byte[] bytes)
        {
            if (Metrics == null) return SnapshotBinary.Deflate(bytes);
            long start = Stopwatch.GetTimestamp();
            byte[] result = SnapshotBinary.Deflate(bytes);
            Metrics.CompressionTicks += Stopwatch.GetTimestamp() - start;
            Metrics.CompressionInputBytes += bytes.Length;
            Metrics.CompressionCalls++;
            return result;
        }
    }

    internal static class PartEncoding
    {
        private const long kMinimumParallelPartBytes = 256 * 1024;

        private sealed class EncodedPart : IDisposable
        {
            internal readonly MemoryStream Bytes = new MemoryStream();
            internal readonly EncodingMetrics Metrics = new EncodingMetrics();
            public void Dispose() => Bytes.Dispose();
        }

        internal static void Write(Stream output, PersistencePartData[] parts, SnapshotEncodingOptions options, EncodingMetrics metrics)
        {
            var compression = options?.Compression ?? SaveCompression.Compact;
            var context = new EncodingContext(compression, metrics);
            int index = 0;
            while (index < parts.Length)
            {
                int count = 0;
                long reserved = 0;
                // Account for packing/compression scratch and completed part buffers.
                // This bounds extra parallel work, not the captured city or final snapshot.
                while (options != null && count < options.MaxWorkers && index + count < parts.Length)
                {
                    long raw = GeometryBytes(parts[index + count]);
                    long estimate = checked(raw * 6 + 64 * 1024);
                    if (raw < kMinimumParallelPartBytes || estimate > options.TemporaryBudget - reserved) break;
                    reserved += estimate;
                    count++;
                }

                if (count < 2)
                {
                    WriteOne(output, parts[index++], context);
                    if (metrics != null) metrics.PeakWorkers = Math.Max(metrics.PeakWorkers, 1);
                    continue;
                }

                var tasks = new Task[count];
                var results = new EncodedPart[count];
                try
                {
                    for (int slot = 0; slot < count; slot++)
                    {
                        int capturedSlot = slot;
                        var part = parts[index + slot];
                        tasks[slot] = Task.Run(() =>
                        {
                            var result = new EncodedPart();
                            results[capturedSlot] = result;
                            WriteOne(result.Bytes, part, new EncodingContext(compression, result.Metrics));
                        });
                    }

                    // Join every worker before returning or propagating a failure.
                    Task.WhenAll(tasks).GetAwaiter().GetResult();
                    for (int slot = 0; slot < count; slot++)
                    {
                        results[slot].Bytes.WriteTo(output);
                        metrics?.Add(results[slot].Metrics);
                    }

                    if (metrics != null)
                    {
                        metrics.ParallelBatches++;
                        metrics.PeakWorkers = Math.Max(metrics.PeakWorkers, count);
                    }
                }
                finally
                {
                    // Also drain already-started tasks if scheduling a later task fails.
                    foreach (var task in tasks)
                    {
                        if (task == null) continue;
                        try
                        {
                            task.GetAwaiter().GetResult();
                        }
                        catch
                        {
                        }
                    }

                    foreach (var result in results) result?.Dispose();
                }

                index += count;
            }
        }

        private static void WriteOne(Stream output, PersistencePartData part, EncodingContext context)
        {
            long start = Stopwatch.GetTimestamp();
            ObjectBlockWriter.WritePart(output, part, context);
            if (context.Metrics != null)
            {
                context.Metrics.PartTicks += Stopwatch.GetTimestamp() - start;
                context.Metrics.PartCount++;
            }
        }

        private static long GeometryBytes(PersistencePartData part)
        {
            if (part == null) throw new InvalidDataException("Persistence part is null.");
            return 12L * ((part.Vertices?.Length ?? 0) + (long)(part.Normals?.Length ?? 0)) + 16L * ((part.Tangents?.Length ?? 0) + (long)(part.UV0?.Length ?? 0) + (part.TexCoord1?.Length ?? 0) + (long)(part.TexCoord3?.Length ?? 0)) + 4L * ((part.VertexColors?.Length ?? 0) + (long)(part.Triangles?.Length ?? 0) + (part.RenderToLogical?.Length ?? 0) + (long)(part.TriangleSurfaceAreaIds?.Length ?? 0)) + 21L * (part.StructuralFaces?.Length ?? 0);
        }
    }
}