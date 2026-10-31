using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using PO2.Persistence.Model;

namespace PO2.Persistence.Format
{
    internal static class SnapshotEncodingPipeline
    {
        private enum Kind { Begin, Part, End }
        private readonly struct Job
        {
            internal readonly int Object, Part;
            internal readonly Kind Kind;
            internal Job(int obj, int part, Kind kind) { Object = obj; Part = part; Kind = kind; }
        }

        private sealed class Result : IDisposable
        {
            internal EncodedBuffer Buffer;
            internal PreparedPart Part;
            internal readonly EncodingMetrics Metrics = new EncodingMetrics();
            public void Dispose() { Part?.Dispose(); Part = null; Buffer = default; }
        }

        internal static SnapshotDirectoryEntry[] Write(Stream output, IReadOnlyList<PersistenceObjectData> objects,
            long[] footprints, SnapshotEncodingOptions options, EncodingMetrics metrics)
        {
            options = options ?? new SnapshotEncodingOptions(SaveCompression.Compact, 1);
            var jobs = new List<Job>();
            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                jobs.Add(new Job(i, -1, Kind.Begin));
                for (int part = 0; part < obj.Parts.Length; part++)
                    jobs.Add(new Job(i, part, Kind.Part));
                jobs.Add(new Job(i, -1, Kind.End));
            }

            var entries = new SnapshotDirectoryEntry[objects.Count];
            ObjectBlockAssembly assembly = null;
            int active = 0, peak = 0, current = -1, parts = 0;
            try
            {
                OrderedWorkQueue.Run(jobs.Count, i => jobs[i], (i, job) =>
                {
                    int running = Interlocked.Increment(ref active), previous;
                    do { previous = peak; if (previous >= running) break; }
                    while (Interlocked.CompareExchange(ref peak, running, previous) != previous);
                    var result = new Result();
                    try
                    {
                        var obj = objects[job.Object];
                        var context = new EncodingContext(options.Compression, result.Metrics);
                        if (job.Kind == Kind.Begin) result.Buffer = ObjectBlockWriter.EncodePrefix(obj, SnapshotFormat.kObjectSchemaVersion, context);
                        else if (job.Kind == Kind.End) result.Buffer = ObjectBlockWriter.EncodeSuffix(obj, SnapshotFormat.kObjectSchemaVersion, context);
                        else
                        {
                            long start = Stopwatch.GetTimestamp();
                            result.Part = ObjectBlockWriter.PreparePart(obj.Parts[job.Part], context);
                            result.Metrics.PartTicks = Stopwatch.GetTimestamp() - start;
                            result.Metrics.PartCount = 1;
                        }
                        return result;
                    }
                    catch { result.Dispose(); throw; }
                    finally { Interlocked.Decrement(ref active); }
                }, (i, result) =>
                {
                    var job = jobs[i];
                    var obj = objects[job.Object];
                    metrics?.Add(result.Metrics);
                    if (job.Kind == Kind.Begin)
                    {
                        if (assembly != null) throw new InvalidOperationException("Unfinished object block.");
                        assembly = new ObjectBlockAssembly(SnapshotFormat.kObjectSchemaVersion);
                        current = job.Object;
                        parts = 0;
                        assembly.Append(result.Buffer);
                    }
                    else
                    {
                        if (assembly == null || current != job.Object) throw new InvalidOperationException("Object encoding order changed.");
                        if (job.Kind == Kind.Part)
                        {
                            if (job.Part != parts++) throw new InvalidOperationException("Part encoding order changed.");
                            assembly.Append(result.Part);
                        }
                        else
                        {
                            if (parts != obj.Parts.Length) throw new InvalidOperationException("Incomplete object block.");
                            assembly.Append(result.Buffer);
                            var bytes = assembly.Finish(out uint checksum);
                            long offset = output.Position;
                            bytes.WriteTo(output);
                            entries[job.Object] = new SnapshotDirectoryEntry
                            {
                                PersistentId = obj.PersistentId, ObjectSchemaVersion = SnapshotFormat.kObjectSchemaVersion,
                                BlockOffset = offset, StoredLength = bytes.Length, DecodedLength = footprints[job.Object],
                                BlockChecksum = checksum
                            };
                            assembly.Dispose();
                            assembly = null;
                        }
                    }
                }, null, result => result.Dispose(), options.MaxWorkers);
                if (metrics != null) metrics.PeakWorkers = Math.Max(metrics.PeakWorkers, peak);
                return entries;
            }
            finally { assembly?.Dispose(); }
        }
    }
}
