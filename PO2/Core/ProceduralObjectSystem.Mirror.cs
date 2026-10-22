using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using PO2.Core.Operations;
using PO2.Geometry.Transforms;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Persistence.Model;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        private TopologyBatchPreparation m_MirrorPreparation;
        private CancellationTokenSource m_MirrorCancellation;
        private HistoryTransaction m_MirrorTransaction;
        private ProceduralObject m_MirrorObject;
        private ObjectTransform m_MirrorTransform;
        private Vector3 m_MirrorPivot;
        private MirrorHistoryAction.Part[] m_MirrorParts;
        private LocalizedText m_MirrorDescription;
        private Task m_MirrorDrain = Task.CompletedTask;
        internal bool MirrorEditBusy => m_MirrorPreparation != null || !m_MirrorDrain.IsCompleted;
        internal LocalizedText MirrorEditStatus => m_MirrorPreparation == null ? LocalizedText.For("PO2.Object.Mirror.Status.FinishingCancellation") : (m_MirrorCancellation.IsCancellationRequested ? LocalizedText.For("PO2.Object.Mirror.Status.Cancelling") : LocalizedText.For("PO2.Object.Mirror.Status.Progress", ("stage", m_MirrorPreparation.Stage)));

        internal bool TryMirrorSelected(int partIndex, Vector3 worldAxis, LocalizedText description, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            if (MirrorEditBusy || ActionHistory == null || ActionHistory.IsBusy || !TryGetSelected(out var obj)) return false;
            if (!MirrorGeometry.TryValidate(obj, partIndex, worldAxis, out reason)) return false;
            try
            {
                int first = partIndex < 0 ? 0 : partIndex;
                int count = partIndex < 0 ? obj.Parts.Count : 1;
                var parts = new MirrorHistoryAction.Part[count];
                var stamps = new TopologyTargetStamp[count];
                for (int i = 0; i < count; i++)
                {
                    var part = obj.Parts[first + i];
                    var size = TopologySize.ForPart(part);
                    parts[i] = new MirrorHistoryAction.Part
                    {
                        Size = size,
                        Key = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), part.PartHistoryId, first + i)
                    };
                    stamps[i] = new TopologyTargetStamp(obj, first + i);
                }

                m_MirrorTransaction = ActionHistory.TryBegin();
                if (m_MirrorTransaction == null)
                {
                    reason = LocalizedText.For("PO2.History.Status.OperationFailed");
                    return false;
                }

                m_MirrorCancellation = new CancellationTokenSource();
                m_MirrorObject = obj;
                m_MirrorTransform = obj.Transform;
                m_MirrorPivot = obj.LocalPivot;
                m_MirrorDescription = description;
                m_MirrorParts = parts;
                var pivot = partIndex < 0 ? obj.LocalPivot : obj.Parts[partIndex].LocalPivot;
                var reflection = MirrorGeometry.CreateLocalReflection(obj.Transform, pivot, worldAxis);
                var token = m_MirrorCancellation.Token;
                m_MirrorPreparation = new TopologyBatchPreparation(count, i =>
                {
                    var topology = obj.Parts[first + i].CaptureMirrorTopology();
                    return new TopologyPreparation(obj, first + i, stamps[i], null, this, token, (data, cancellation) => BuildMirror(data, topology, reflection, partIndex >= 0, cancellation), size: parts[i].Size);
                });
                return true;
            }
            catch (Exception exception)
            {
                reason = LocalizedText.FromException(exception);
                ResetMirrorEdit();
                return false;
            }
        }

        private static TopologyPreparedResult BuildMirror(PersistencePartData data, MirrorTopologySnapshot topology, Matrix4x4 reflection, bool preservePivot, CancellationToken token)
        {
            var result = new TopologyPreparedResult { RestorePivot = true, RenderCaptureMode = TopologyRenderCaptureMode.Mirror };
            try
            {
                token.ThrowIfCancellationRequested();
                topology.Prepare(data, token);
                var mirrored = MirrorGeometry.TransformCapture(data, reflection, preservePivot, token, topology.FaceTriangles);
                result.Part = topology.CreateCandidate(mirrored, token);
                token.ThrowIfCancellationRequested();
                result.MirrorDelta = new MirrorDelta(data, mirrored, token);
                token.ThrowIfCancellationRequested();
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        internal void CancelMirrorEdit() => m_MirrorCancellation?.Cancel();

        internal OperationOutcome PollMirrorEdit(out LocalizedText reason)
        {
            bool completed = false;
            reason = LocalizedText.Empty;
            if (m_MirrorPreparation == null) return OperationOutcome.Pending;
            MirrorHistoryAction action = null;
            try
            {
                if (!m_MirrorTransaction.IsValid || !TryGetSelected(out var selected) || !ReferenceEquals(selected, m_MirrorObject) || !m_MirrorObject.Transform.Equals(m_MirrorTransform) || !m_MirrorObject.LocalPivot.Equals(m_MirrorPivot)) CancelMirrorEdit();
                if (!m_MirrorPreparation.Poll()) return OperationOutcome.Pending;
                completed = true;
                for (int i = 0; i < m_MirrorParts.Length; i++)
                {
                    var result = m_MirrorPreparation.Parts[i].Result;
                    m_MirrorParts[i].Delta = result.MirrorDelta;
                    result.MirrorDelta = null;
                    m_MirrorParts[i].Proof = result.Render.ReplayProof;
                }

                action = new MirrorHistoryAction(m_MirrorParts, m_HistoryContext, m_MirrorDescription);
                var committed = action;
                if (!m_MirrorPreparation.Publish(() =>
                    {
                        if (!m_MirrorTransaction.TryCommit(action)) return false;
                        action = null;
                        return true;
                    })) throw new LocalizedTextException(LocalizedText.For("PO2.Object.Mirror.Error.HistoryRestored"));
                // The raw record is already valid for undo. Storage reduction
                // must never prevent a successfully committed edit from finishing.
                committed.StartCompression();
                return OperationOutcome.Succeeded;
            }
            catch (OperationCanceledException)
            {
                completed = true;
                reason = LocalizedText.For("PO2.Object.Mirror.Status.Cancelled");
                return OperationOutcome.Cancelled;
            }
            catch (Exception e)
            {
                completed = true;
                reason = LocalizedText.FromException(e);
                return OperationOutcome.Failed;
            }
            finally
            {
                action?.Dispose();
                if (completed) ResetMirrorEdit();
            }
        }

        internal void ResetMirrorEdit()
        {
            m_MirrorCancellation?.Cancel();
            if (m_MirrorPreparation != null)
            {
                m_MirrorPreparation.Dispose();
                m_MirrorDrain = m_MirrorPreparation.Drain;
                m_MirrorPreparation = null;
            }

            m_MirrorCancellation?.Dispose();
            m_MirrorCancellation = null;
            m_MirrorTransaction?.Dispose();
            m_MirrorTransaction = null;
            m_MirrorParts = null;
            m_MirrorObject = null;
        }
    }
}