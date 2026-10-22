using System;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    internal sealed class TopologyAction : HistoryCaptureAction, IHistoryPayloadOwner, IHistoryBackgroundWork, IHistoryOwnerCleanup
    {
        private readonly HistoryPartKey m_Key;
        private TopologyState m_Before;
        private ReversibleTopologyDelta m_Delta;
        private int[] m_Map;
        private TopologySelection m_BeforeSelection, m_AfterSelection;
        private readonly bool m_RestorePivot;
        private HistoryContext m_PreparedContext;
        private TopologyPreparation m_Preparation;
        private TaskCompletionSource<HistoryResult> m_Completion;
        private Task m_PayloadPreparation;
        private CancellationToken m_PreparationToken;
        private ProceduralObject m_ReplayObject;
        private int m_ReplayIndex;
        private bool m_PreparationEnding;
        private TopologyReplayProof m_ReplayProof;
        private TopologySize m_Size;
        private TopologyTargetStamp m_ReplayStamp;
        private HistoryDirection m_PreparedDirection;

        internal TopologyAction(ProceduralObject obj, int index, ReversibleTopologyDelta delta, TopologySelection beforeSelection, TopologySelection afterSelection, HistoryContext context, LocalizedText description, TopologyReplayProof proof = null, TopologySize size = null) : base(description)
        {
            if (delta == null) throw new InvalidOperationException("Invalid prepared topology history.");
            m_Key = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), obj.Parts[index].PartHistoryId, index);
            m_Delta = delta;
            m_ReplayProof = proof;
            m_Size = size;
            m_BeforeSelection = beforeSelection;
            m_AfterSelection = afterSelection;
            m_PreparedContext = context;
            HasChanges = true;
        }

        internal TopologyAction(ProceduralObject obj, int index, MeshMutationResult mutation, LocalizedText description, HistoryContext context) : this(obj, index, mutation.HistoryOldToNew, description, context, false)
        {
        }

        internal TopologyAction(ProceduralObject obj, int index, int[] oldToNew, LocalizedText description, HistoryContext context, bool restorePivot) : base(description)
        {
            m_Key = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), obj.Parts[index].PartHistoryId, index);
            if (oldToNew == null) throw new InvalidOperationException("The topology editor did not provide vertex provenance.");
            m_Map = oldToNew;
            m_RestorePivot = restorePivot;
            m_Before = TopologyState.Capture(obj.Parts[index]);
            m_BeforeSelection = context.CaptureTopologySelection(obj);
        }

        public System.Collections.Generic.IEnumerable<HistoryResidentPayload> Payloads => m_Delta?.Payloads ?? System.Array.Empty<HistoryResidentPayload>();

        public void CoolManagedPayload()
        {
        }

        public override HistoryFootprint Footprint => new HistoryFootprint(256 + (m_ReplayProof?.Bytes ?? 0) + (m_Before?.Bytes ?? 0) + (m_BeforeSelection?.Bytes ?? 0) + (m_AfterSelection?.Bytes ?? 0), m_Delta?.Footprint.EncodedBytes ?? 0, m_Delta?.Footprint.ColdBytes ?? 0, m_Delta?.EncodedBytes ?? m_Before?.Bytes ?? 0);

        private MeshPart Resolve(HistoryContext context, out ProceduralObject obj)
        {
            if (!context.TryResolve(m_Key.Owner, out obj) || !context.TryResolve(m_Key, out var part, out _))
                throw new InvalidOperationException("Missing topology history target.");
            return part;
        }

        internal override void Seal(HistoryContext context)
        {
            if (m_PreparedContext != null) return;
            var part = Resolve(context, out var obj);
            var after = TopologyState.Capture(part);
            HasChanges = !TopologyState.Equal(m_Before.Signature(), after.Signature());
            if (!HasChanges) return;
            var delta = new ReversibleTopologyDelta(m_Before, after, m_Map);
            m_AfterSelection = context.CaptureTopologySelection(obj);
            m_Delta = delta;
            m_Before = null;
            m_Map = null;
        }

        internal override void RestoreCapture(HistoryContext context)
        {
            var part = Resolve(context, out var obj);
            var current = TopologyState.Capture(part);
            var before = m_Before ?? m_Delta.Apply(current, HistoryDirection.Undo);
            if (!TopologyState.Equal(current.Signature(), before.Signature()))
            {
                var staging = before.Restore();
                part.ApplyHistoryTopology(staging);
                if (m_RestorePivot) part.SetLocalPivot(staging.LocalPivot);
                obj.MarkDirty(ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
            }

            context.RestoreTopologySelection(obj, m_BeforeSelection);
        }

        public override Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken cancellation)
        {
            if (m_PreparedContext == null) return Task.FromResult(HistoryResult.Ok);
            Resolve(m_PreparedContext, out m_ReplayObject);
            if (!m_PreparedContext.TryResolve(m_Key, out _, out m_ReplayIndex))
                return Task.FromResult(new HistoryResult(false, "Missing topology target.", true));
            m_ReplayStamp = new TopologyTargetStamp(m_ReplayObject, m_ReplayIndex);
            m_PreparedDirection = direction;
            m_PreparationToken = cancellation;
            m_PreparationEnding = false;
            m_Completion = new TaskCompletionSource<HistoryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_PayloadPreparation = PreparePayloads(cancellation);
            return m_Completion.Task;
        }

        private async Task PreparePayloads(CancellationToken token)
        {
            foreach (var payload in m_Delta.Payloads) await payload.Prepare(token).ConfigureAwait(false);
        }

        public void PollBackgroundWork(Action<string> log)
        {
            if (m_PreparationEnding || m_Completion == null || m_Completion.Task.IsCompleted) return;
            try
            {
                m_PreparationToken.ThrowIfCancellationRequested();
                if (!m_PayloadPreparation.IsCompleted) return;
                m_PayloadPreparation.GetAwaiter().GetResult();
                if (m_Preparation == null)
                {
                    var delta = m_Delta;
                    var direction = m_PreparedDirection;
                    var selection = direction == HistoryDirection.Undo ? m_BeforeSelection : m_AfterSelection;
                    var key = m_Key;
                    int partIndex = m_ReplayIndex;
                    m_Preparation = m_PreparedContext.CreateTopologyPreparation(m_ReplayObject, m_ReplayIndex, m_ReplayStamp, m_PreparationToken, (data, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        var state = delta.Apply(TopologyState.Capture(data), direction);
                        token.ThrowIfCancellationRequested();
                        var staging = state.Restore();
                        token.ThrowIfCancellationRequested();
                        return new TopologyPreparedResult
                        {
                            Part = staging,
                            PreparedSelection = new TopologyPreparedSelection(selection, key, partIndex, staging, token)
                        };
                    }, direction == HistoryDirection.Undo ? m_ReplayProof : null, m_Size);
                }

                if (m_Preparation.Poll()) m_Completion.TrySetResult(HistoryResult.Ok);
            }
            catch (Exception e)
            {
                var completion = m_Completion;
                var drain = ReleasePreparation();
                drain.ContinueWith(_ => completion.TrySetResult(new HistoryResult(false, e.Message, true)), TaskScheduler.Default);
            }
        }

        // Unload releases Unity staging on the main thread. Worker buffers are released
        // only after the detached build stops reading the delta.
        public void CancelPreparation()
        {
            var completion = m_Completion;
            var drain = ReleasePreparation();
            if (completion != null) drain.ContinueWith(_ => completion.TrySetCanceled(), TaskScheduler.Default);
        }

        private Task ReleasePreparation()
        {
            m_PreparationEnding = true;
            var preparation = m_Preparation;
            m_Preparation = null;
            preparation?.Dispose();
            return Task.WhenAll(preparation?.Drain ?? Task.CompletedTask, m_PayloadPreparation ?? Task.CompletedTask);
        }

        public override HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            var part = Resolve(context, out var obj);
            MeshPart staging;
            if (m_PreparedContext != null)
            {
                if (m_Preparation == null || !m_Preparation.IsReady || m_PreparedDirection != direction || !context.TryResolve(m_Key, out _, out int index) || !m_ReplayStamp.Matches(obj, index))
                    return new HistoryResult(false, "The topology target changed while preparing history.", true);
                try
                {
                    if (!m_Preparation.Publish(() => true)) return new HistoryResult(false, "Topology replay could not be published.", true);
                    context.RestorePreparedTopologySelection(obj, m_Preparation.Result.PreparedSelection);
                    return HistoryResult.Ok;
                }
                finally
                {
                    ReleasePreparation();
                    m_Completion = null;
                    m_ReplayObject = null;
                    m_ReplayStamp = null;
                }
            }

            var restored = m_Delta.Apply(TopologyState.Capture(part), direction);
            staging = restored.Restore();
            // All decoding, signatures, adjacency building, and validation have
            // finished before the live part receives any replacement fields.
            part.ApplyHistoryTopology(staging);
            if (m_RestorePivot) part.SetLocalPivot(staging.LocalPivot);
            obj.MarkDirty(ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
            context.RestoreTopologySelection(obj, direction == HistoryDirection.Undo ? m_BeforeSelection : m_AfterSelection);
            return HistoryResult.Ok;
        }

        public override void Dispose()
        {
            if (m_Preparation != null) ReleasePreparation();
            m_Completion = null;
            m_PayloadPreparation = null;
            m_ReplayObject = null;
            m_ReplayStamp = null;
            m_PreparedContext = null;
            m_ReplayProof = null;
            m_Size = null;
            m_Before = null;
            m_Delta?.Dispose();
            m_Delta = null;
            m_Map = null;
            m_BeforeSelection = null;
            m_AfterSelection = null;
        }
    }
}