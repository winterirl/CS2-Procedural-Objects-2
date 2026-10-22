using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    // Cage deformations retain one exact symmetric XOR delta. The raw delta is
    // ready for undo immediately, while compression runs away from mouse-up.
    internal sealed class PartDeformationDeltaAction : HistoryCaptureAction, IHistoryPayloadOwner, IHistoryBackgroundWork
    {
        private const int kMinimumDeferredCompressionBytes = 64 * 1024;
        private static readonly SemaphoreSlim s_CompressionGate = new SemaphoreSlim(1, 1);

        private sealed class CompressionResult
        {
            internal readonly HistoryResidentPayload Payload;

            internal CompressionResult(HistoryResidentPayload payload)
            {
                Payload = payload;
            }
        }

        private readonly HistoryPartKey m_Key;
        private readonly ulong m_Layout;
        private readonly long m_LogicalBytes;
        private MeshPart m_CapturePart;
        private GeometryStreams m_Start;
        private GeometryStreams m_Final;
        private byte[] m_RawDelta;
        private HistoryResidentPayload m_Payload;
        private Task<CompressionResult> m_CompressionTask;
        private string m_BackgroundMessage;
        private ulong m_Before;
        private ulong m_After;
        private bool m_BeforeReady;
        private bool m_PreparedDelta;
        private bool m_CompletionDeclared;
        private bool m_Disposed;

        internal PartDeformationDeltaAction(ProceduralObject obj, int index, PartGeometrySnapshot snapshot, LocalizedText description) : base(description)
        {
            if (obj == null || snapshot == null || snapshot.PositionsOnly)
            {
                throw new ArgumentException("A deformation action requires a full part snapshot.");
            }

            MeshPart part = obj.Parts[index];
            if (!ReferenceEquals(part, snapshot.Part))
            {
                throw new ArgumentException("The deformation snapshot belongs to another part.");
            }

            m_Key = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), part.PartHistoryId, index);
            m_Layout = GeometryHistory.Layout(part.Mesh);
            m_CapturePart = part;
            m_Start = new GeometryStreams(snapshot.Positions, snapshot.Normals, snapshot.Tangents);
            m_LogicalBytes = m_Start.RawBytes;
        }

        internal void SetFinalSnapshot(PartGeometrySnapshot snapshot)
        {
            if (m_Disposed || snapshot == null || snapshot.PositionsOnly || !ReferenceEquals(snapshot.Part, m_CapturePart))
            {
                throw new InvalidOperationException("The completed deformation snapshot is invalid.");
            }

            m_Final = new GeometryStreams(snapshot.WorkPositions, snapshot.WorkNormals, snapshot.WorkTangents);
            m_CompletionDeclared = true;
        }

        internal void SetPreparedRawDelta(byte[] rawDelta, ulong beforeHash, ulong afterHash, bool changed)
        {
            if (m_Disposed || m_Start == null || rawDelta == null || rawDelta.LongLength != m_LogicalBytes)
            {
                throw new InvalidOperationException("The prepared deformation delta is invalid.");
            }

            m_RawDelta = changed ? rawDelta : null;
            m_Before = beforeHash;
            m_After = afterHash;
            m_BeforeReady = true;
            m_PreparedDelta = true;
            m_Final = null;
            m_CompletionDeclared = true;
        }

        internal void SetNoChange()
        {
            if (m_Disposed || m_Start == null)
            {
                throw new InvalidOperationException("The deformation capture is no longer active.");
            }

            m_Final = null;
            m_CompletionDeclared = true;
        }

        public IEnumerable<HistoryResidentPayload> Payloads
        {
            get
            {
                TryAdoptCompression();
                if (m_Payload != null)
                {
                    yield return m_Payload;
                }
            }
        }

        public void CoolManagedPayload()
        {
            TryAdoptCompression();
        }

        public void PollBackgroundWork(Action<string> log)
        {
            TryAdoptCompression();
            if (string.IsNullOrEmpty(m_BackgroundMessage))
            {
                return;
            }

            string message = m_BackgroundMessage;
            m_BackgroundMessage = null;
            try
            {
                log?.Invoke(message);
            }
            catch
            {
            }
        }

        public override HistoryFootprint Footprint
        {
            get
            {
                TryAdoptCompression();
                long managed = 384 + (m_Start?.RawBytes ?? 0) + (m_RawDelta?.LongLength ?? 0);
                HistoryFootprint payload = m_Payload?.Footprint ?? default;
                return new HistoryFootprint(managed, payload.EncodedBytes, payload.ColdBytes, 384 + m_LogicalBytes);
            }
        }

        internal override void Seal(HistoryContext context)
        {
            if (!m_CompletionDeclared)
            {
                throw new InvalidOperationException("The deformation completion state was not declared.");
            }

            if (m_Final == null)
            {
                if (m_PreparedDelta)
                {
                    Resolve(context);
                }

                HasChanges = m_PreparedDelta && m_RawDelta != null;
                m_Start = null;
                m_CapturePart = null;

                if (HasChanges)
                {
                    if (m_RawDelta.Length >= kMinimumDeferredCompressionBytes)
                    {
                        ScheduleCompression(m_RawDelta);
                    }
                }

                return;
            }

            MeshPart part = Resolve(context);
            GeometryStreams current = Reference(part);
            byte[] raw = m_Start.CreateRawXor(m_Final, current, out m_Before, out m_After, out bool changed);
            m_BeforeReady = true;
            HasChanges = changed;
            m_Start = null;
            m_Final = null;
            m_CapturePart = null;

            if (!HasChanges)
            {
                return;
            }

            m_RawDelta = raw;

            if (raw.Length >= kMinimumDeferredCompressionBytes)
            {
                ScheduleCompression(raw);
            }
        }

        internal override void RestoreCapture(HistoryContext context)
        {
            MeshPart part = Resolve(context);

            if (m_Start != null)
            {
                if (!m_BeforeReady || Reference(part).Hash() != m_Before)
                {
                    Apply(context, part, m_Start);
                }

                return;
            }

            if (HasChanges)
            {
                HistoryResult result = Replay(HistoryDirection.Undo, context);
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.Reason);
                }
            }
        }

        public override Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken cancellation)
        {
            TryAdoptCompression();
            return Task.FromResult(HistoryResult.Ok);
        }

        public override HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            TryAdoptCompression();
            MeshPart part = Resolve(context);
            byte[] raw;

            try
            {
                raw = m_RawDelta ?? GeometryStreams.DecompressRawXor(m_Payload.Bytes, checked((int)m_LogicalBytes));
            }
            catch (Exception exception)
            {
                return new HistoryResult(false, "Could not read the deformation delta: " + exception.Message, true);
            }

            try
            {
                GeometryStreams current = Reference(part);
                GeometryStreams restored = current.ApplyRawXor(raw, out ulong currentHash, out ulong restoredHash);
                bool undo = direction == HistoryDirection.Undo;
                ulong expectedCurrent = undo ? m_After : m_Before;
                ulong expectedResult = undo ? m_Before : m_After;

                if (currentHash != expectedCurrent)
                {
                    return new HistoryResult(false, "Part deformation values no longer match.", true);
                }

                if (restoredHash != expectedResult)
                {
                    return new HistoryResult(false, "Part deformation delta failed verification.", true);
                }

                Apply(context, part, restored);
                return HistoryResult.Ok;
            }
            catch (Exception exception)
            {
                return new HistoryResult(false, "Could not replay the deformation delta: " + exception.Message, true);
            }
        }

        public override void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            m_Start = null;
            m_Final = null;
            m_CapturePart = null;
            m_RawDelta = null;
            m_Payload?.Dispose();
            m_Payload = null;

            Task<CompressionResult> task = m_CompressionTask;
            m_CompressionTask = null;
            if (task != null)
            {
                task.ContinueWith(completed =>
                {
                    if (completed.Status == TaskStatus.RanToCompletion)
                    {
                        completed.Result.Payload?.Dispose();
                    }
                    else
                    {
                        var ignored = completed.Exception;
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }

        private void ScheduleCompression(byte[] raw)
        {
            m_CompressionTask = Task.Run(async () =>
            {
                await s_CompressionGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    byte[] encoded = GeometryStreams.CompressRawXor(raw);

                    if (encoded.Length >= raw.Length)
                    {
                        return new CompressionResult(null);
                    }

                    return new CompressionResult(new HistoryResidentPayload(encoded, raw.LongLength));
                }
                finally
                {
                    s_CompressionGate.Release();
                }
            });
        }

        private void TryAdoptCompression()
        {
            Task<CompressionResult> task = m_CompressionTask;
            if (task == null || !task.IsCompleted || m_Disposed)
            {
                return;
            }

            m_CompressionTask = null;

            if (task.Status != TaskStatus.RanToCompletion)
            {
                var ignored = task.Exception;
                m_BackgroundMessage = "[PO History] Deformation compression failed; " + "the exact raw delta remains available.";
                return;
            }

            CompressionResult result = task.Result;
            if (result.Payload == null)
            {
                return;
            }

            m_Payload = result.Payload;
            m_RawDelta = null;
        }

        private MeshPart Resolve(HistoryContext context)
        {
            if (!context.TryResolve(m_Key, out MeshPart part, out _) || GeometryHistory.Layout(part.Mesh) != m_Layout)
            {
                throw new InvalidOperationException("Part deformation history target or layout changed.");
            }

            return part;
        }

        private static GeometryStreams Reference(MeshPart part)
        {
            return new GeometryStreams(part.Mesh.Vertices, part.Mesh.Normals, part.Mesh.Tangents);
        }

        private void Apply(HistoryContext context, MeshPart part, GeometryStreams streams)
        {
            if (!context.TryResolve(m_Key.Owner, out ProceduralObject obj))
            {
                throw new InvalidOperationException("Missing deformation history owner.");
            }

            streams.Publish(part);
            part.MarkPositionsAndDirectionsChanged();
            obj.MarkDirty(ProceduralObjectDirtyFlags.Geometry);
            context.GeometryChanged(obj);
        }
    }
}