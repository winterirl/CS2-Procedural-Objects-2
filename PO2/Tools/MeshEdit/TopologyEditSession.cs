using System;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core;
using PO2.Core.Operations;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Persistence.Model;

namespace PO2.Tools.MeshEdit
{
    internal sealed class TopologyEditSession
    {
        private TopologyPreparation m_Preparation;
        private CancellationTokenSource m_Cancellation;
        private HistoryTransaction m_Transaction;
        private ProceduralObject m_Object;
        private int m_PartIndex;
        private TopologyTargetStamp m_Stamp;
        private TopologySize m_Size;
        internal Task Drain { get; private set; } = Task.CompletedTask;
        internal bool IsBusy => m_Preparation != null;
        internal LocalizedText Description { get; private set; }
        internal LocalizedText Status => m_Cancellation != null && m_Cancellation.IsCancellationRequested ? LocalizedText.For("PO2.Edit.Topology.Status.Cancelling") : LocalizedText.For("PO2.Edit.Topology.Status.Progress", ("stage", m_Preparation?.Stage ?? LocalizedText.For("PO2.Edit.Topology.Stage.Preparing")));

        internal bool TryBegin(ProceduralObject obj, int partIndex, PersistencePartData source, TopologyTargetStamp stamp, LocalizedText description, TopologySize size, Func<HistoryPartKey, CancellationToken, TopologyPreparedResult> build, ProceduralObjectSystem system, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            if (IsBusy || !Drain.IsCompleted || obj == null || obj.IsLocked || source == null || stamp == null || !stamp.Matches(obj, partIndex) || !obj.CanEditGeometry(partIndex, out reason)) return false;
            var transaction = system.ActionHistory.TryBegin();
            if (transaction == null)
            {
                reason = LocalizedText.For("PO2.History.Status.OperationFailed");
                return false;
            }

            m_Transaction = transaction;
            m_Object = obj;
            m_PartIndex = partIndex;
            m_Stamp = stamp;
            m_Size = size;
            Description = description;
            var key = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), obj.Parts[partIndex].PartHistoryId, partIndex);
            m_Cancellation = new CancellationTokenSource();
            try
            {
                m_Preparation = new TopologyPreparation(obj, partIndex, stamp, source, system, m_Cancellation.Token, (data, token) =>
                {
                    TopologyPreparedResult result = null;
                    try
                    {
                        result = build(key, token);
                        size.ValidateCandidate(result.Part);
                        result.PreparedSelection = new TopologyPreparedSelection(result.AfterSelection, key, partIndex, result.Part, token);
                        return result;
                    }
                    catch
                    {
                        result?.Dispose();
                        throw;
                    }
                }, size: size);
                return true;
            }
            catch (Exception e)
            {
                reason = LocalizedText.FromException(e);
                Reset();
                return false;
            }
        }

        internal void Cancel()
        {
            m_Cancellation?.Cancel();
        }

        internal OperationOutcome PollFinish(ProceduralObject obj, ProceduralObjectSystem system, out LocalizedText reason)
        {
            bool completed = false;
            reason = LocalizedText.Empty;
            if (!IsBusy) return OperationOutcome.Pending;
            TopologyAction action = null;
            try
            {
                if (!m_Transaction.IsValid || !ReferenceEquals(obj, m_Object) || obj.IsLocked || !m_Stamp.Matches(obj, m_PartIndex) || !obj.CanEditGeometry(m_PartIndex, out reason)) Cancel();
                if (!m_Preparation.Poll()) return OperationOutcome.Pending;
                completed = true;
                var result = m_Preparation.Result;
                action = system.CreatePreparedTopologyHistory(obj, m_PartIndex, result.Delta, result.BeforeSelection, result.AfterSelection, Description, result.Render.ReplayProof, m_Size);
                result.Delta = null;
                if (!m_Preparation.Publish(() =>
                    {
                        bool recorded = m_Transaction.TryCommit(action);
                        if (recorded) action = null;
                        return recorded;
                    }))
                {
                    reason = LocalizedText.For("PO2.Edit.Topology.Error.HistoryRestored");
                    return OperationOutcome.Failed;
                }

                system.RestorePreparedTopologySelection(obj, result.PreparedSelection);
                return OperationOutcome.Succeeded;
            }
            catch (OperationCanceledException)
            {
                completed = true;
                reason = LocalizedText.For("PO2.Edit.Topology.Status.Cancelled");
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
                if (completed) Reset();
            }
        }

        internal void Reset()
        {
            m_Cancellation?.Cancel();
            if (m_Preparation != null)
            {
                m_Preparation.Dispose();
                Drain = m_Preparation.Drain;
                m_Preparation = null;
            }

            m_Cancellation?.Dispose();
            m_Cancellation = null;
            m_Transaction?.Dispose();
            m_Transaction = null;
            m_Object = null;
            m_Stamp = null;
            m_Size = null;
        }
    }
}