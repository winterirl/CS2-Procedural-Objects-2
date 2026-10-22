using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core;
using PO2.Core.Operations;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    // A whole-object mirror is one action with one atomic publication, including
    // replay. Child actions cannot publish independently during undo/redo.
    internal sealed class MirrorHistoryAction : IHistoryAction, IHistoryPayloadOwner, IHistoryBackgroundWork, IHistoryOwnerCleanup
    {
        internal sealed class Part
        {
            internal HistoryPartKey Key;
            internal TopologySize Size;
            internal MirrorDelta Delta;
            internal TopologyReplayProof Proof;
        }

        private readonly Part[] m_Parts;
        private readonly HistoryContext m_Context;
        private TopologyBatchPreparation m_Preparation;
        private TaskCompletionSource<HistoryResult> m_Completion;
        private Task<MirrorDelta.Reader[]> m_PayloadPreparation;
        private Task m_Drain = Task.CompletedTask;
        private CancellationToken m_Token;
        private HistoryDirection m_Direction;
        private ProceduralObject m_Object;
        private bool m_Ending, m_Disposed;
        public LocalizedText Description { get; }
        public bool HasChanges => true;

        public IEnumerable<HistoryResidentPayload> Payloads
        {
            get
            {
                foreach (var part in m_Parts)
                foreach (var payload in part.Delta.Payloads)
                    yield return payload;
            }
        }

        public void CoolManagedPayload() => PollCompression(null);

        internal void StartCompression()
        {
            foreach (var part in m_Parts)
            {
                try
                {
                    part.Delta.StartCompression();
                }
                catch (Exception e)
                {
                    try
                    {
                        Mod.log.Warn("[PO Mirror] Keeping raw history after compression setup failed: " + e.Message);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void PollCompression(Action<string> log)
        {
            if (m_Disposed || m_Preparation != null || !m_Drain.IsCompleted) return;
            foreach (var part in m_Parts) part.Delta.PollCompression(log);
        }

        public HistoryFootprint Footprint
        {
            get
            {
                var size = new HistoryFootprint(256, 0, 0, 0);
                foreach (var part in m_Parts) size = size.Add(part.Delta.Footprint).Add(new HistoryFootprint(128 + (part.Proof?.Bytes ?? 0), 0, 0, 0));
                return size;
            }
        }

        internal MirrorHistoryAction(Part[] parts, HistoryContext context, LocalizedText description)
        {
            m_Parts = parts;
            m_Context = context;
            Description = description;
        }

        public Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken cancellation)
        {
            PollCompression(null);
            m_Direction = direction;
            m_Token = cancellation;
            m_Ending = false;
            if (!m_Context.TryResolve(m_Parts[0].Key.Owner, out m_Object) || m_Object.IsLocked)
                return Task.FromResult(new HistoryResult(false, "The mirror target is unavailable or locked.", true));
            var indices = new int[m_Parts.Length];
            var stamps = new TopologyTargetStamp[m_Parts.Length];
            for (int i = 0; i < m_Parts.Length; i++)
            {
                if (!m_Context.TryResolve(m_Parts[i].Key, out _, out indices[i]) || !m_Object.CanEditGeometry(indices[i], out _))
                    return Task.FromResult(new HistoryResult(false, "The mirror part is unavailable.", true));
                stamps[i] = new TopologyTargetStamp(m_Object, indices[i]);
            }

            m_Completion = new TaskCompletionSource<HistoryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_PayloadPreparation = PreparePayloads(cancellation);
            var readers = m_PayloadPreparation;
            m_Preparation = new TopologyBatchPreparation(m_Parts.Length, i =>
            {
                var part = m_Parts[i];
                var reader = readers.GetAwaiter().GetResult()[i];
                var topology = m_Object.Parts[indices[i]].CaptureMirrorTopology();
                return m_Context.CreateTopologyPreparation(m_Object, indices[i], stamps[i], cancellation, (data, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var state = part.Delta.Apply(data, direction, reader, token);
                    token.ThrowIfCancellationRequested();
                    topology.Prepare(data, token);
                    var candidate = topology.CreateCandidate(state, token);
                    token.ThrowIfCancellationRequested();
                    return new TopologyPreparedResult { Part = candidate, RestorePivot = true, RenderCaptureMode = TopologyRenderCaptureMode.Mirror };
                }, direction == HistoryDirection.Undo ? part.Proof : null, part.Size);
            });
            return m_Completion.Task;
        }

        private async Task<MirrorDelta.Reader[]> PreparePayloads(CancellationToken token)
        {
            var readers = new MirrorDelta.Reader[m_Parts.Length];
            for (int i = 0; i < readers.Length; i++) readers[i] = await m_Parts[i].Delta.Prepare(token).ConfigureAwait(false);
            return readers;
        }

        public void PollBackgroundWork(Action<string> log)
        {
            PollCompression(log);
            if (m_Ending || m_Completion == null || m_Completion.Task.IsCompleted) return;
            try
            {
                m_Token.ThrowIfCancellationRequested();
                if (!m_PayloadPreparation.IsCompleted) return;
                m_PayloadPreparation.GetAwaiter().GetResult();
                if (m_Preparation.Poll()) m_Completion.TrySetResult(HistoryResult.Ok);
            }
            catch (Exception e)
            {
                var completion = m_Completion;
                ReleasePreparation().ContinueWith(_ => completion.TrySetResult(new HistoryResult(false, e.Message, true)), TaskScheduler.Default);
            }
        }

        public HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            if (direction != m_Direction || m_Preparation == null || !m_Preparation.IsReady)
                return new HistoryResult(false, "The mirror is not ready to publish.", true);
            try
            {
                m_Preparation.Publish(() => true);
                // Mirror starts outside mesh editing. Re-entering mesh mode after
                // replay must discard selection IDs tied to the previous winding.
                context.RestoreTopologySelection(m_Object, null);
                return HistoryResult.Ok;
            }
            finally
            {
                ReleasePreparation();
                m_Completion = null;
            }
        }

        private Task ReleasePreparation()
        {
            m_Ending = true;
            var preparation = m_Preparation;
            m_Preparation = null;
            preparation?.Dispose();
            var readers = m_PayloadPreparation;
            m_PayloadPreparation = null;
            m_Drain = Task.WhenAll(m_Drain, preparation?.Drain ?? Task.CompletedTask, (Task)readers ?? Task.CompletedTask).ContinueWith(t =>
            {
                var ignored = t.Exception;
                // Drop the read leases only after all detached workers settle.
                if (readers?.Status == TaskStatus.RanToCompletion) Array.Clear(readers.Result, 0, readers.Result.Length);
            }, TaskScheduler.Default);
            return m_Drain;
        }

        public void CancelPreparation()
        {
            var completion = m_Completion;
            var drain = ReleasePreparation();
            if (completion != null) drain.ContinueWith(_ => completion.TrySetCanceled(), TaskScheduler.Default);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            var drain = ReleasePreparation();
            // History cleanup may retire an in-flight action. Its worker must
            // finish reading the delta before the delta's storage is released.
            if (drain.IsCompleted) DisposeDeltas();
            else drain.ContinueWith(_ => DisposeDeltas(), TaskScheduler.Default);
            m_Completion = null;
            m_Object = null;
        }

        private void DisposeDeltas()
        {
            foreach (var part in m_Parts) part.Delta.Dispose();
        }
    }
}