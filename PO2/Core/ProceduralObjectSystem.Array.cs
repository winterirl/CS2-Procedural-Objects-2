using System;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Rendering.Objects;
using PO2.Tools.Arrays;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        private PartArrayPreparation m_ArrayPreparation;
        private HistoryTransaction m_ArrayTransaction;
        private Task m_ArrayDrain = Task.CompletedTask;
        private HistoryPartKey m_ArraySource;
        private HistoryFocusState m_ArrayFocus;
        private LocalizedText m_ArrayResult = LocalizedText.Empty;
        internal bool ArrayEditBusy => m_ArrayPreparation != null || !m_ArrayDrain.IsCompleted;
        internal LocalizedText ArrayEditStatus => m_ArrayPreparation?.Status ?? (!m_ArrayDrain.IsCompleted ? LocalizedText.For("PO2.PartArray.Status.Cancelling") : m_ArrayResult);
        internal ProceduralObjectRenderer.ArrayPreview CreateArrayPreview(ProceduralObject obj, int index) => m_Renderer.CreateArrayPreview(obj, index);

        internal ITopologyPublication StageArrayPart(ProceduralObject obj, int index, MeshPart part, ITopologyRenderData data, int finalCount) =>
            m_Renderer.StagePartAppend(obj, index, part, data, finalCount);

        internal bool TryBeginArray(ProceduralObject obj, int sourceIndex, PartArrayLayout layout, out LocalizedText reason) =>
            TryBeginArray(obj, sourceIndex, layout, null, out reason);

        internal bool TryBeginArray(ProceduralObject obj, int sourceIndex, PartArrayPlacements placements, out LocalizedText reason) =>
            TryBeginArray(obj, sourceIndex, default, placements, out reason);

        private bool TryBeginArray(ProceduralObject obj, int sourceIndex, PartArrayLayout layout, PartArrayPlacements placements, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            int count = placements?.Count ?? layout.Count;
            if (ArrayEditBusy || ActionHistory == null || ActionHistory.IsBusy || count <= 0 || obj == null || obj.IsLocked || sourceIndex < 0 || sourceIndex >= obj.Parts.Count || !obj.CanEditGeometry(-1, out reason)) return false;
            if (count > ProceduralObject.kMaxPartCount - obj.Parts.Count)
            {
                reason = LocalizedText.For("PO2.PartArray.Error.PlacementUnsupported");
                return false;
            }

            try
            {
                ArrayKernels.Initialize();
                m_ArrayResult = LocalizedText.Empty;
                m_ArrayFocus = CaptureHistoryFocus();
                m_ArraySource = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), obj.Parts[sourceIndex].PartHistoryId, sourceIndex);
                m_ArrayTransaction = ActionHistory.TryBegin();
                if (m_ArrayTransaction == null)
                {
                    reason = LocalizedText.For("PO2.History.Status.OperationFailed");
                    return false;
                }

                m_ArrayPreparation = new PartArrayPreparation(this, obj, sourceIndex, layout, CancellationToken.None, placements: placements);
                return true;
            }
            catch (Exception e)
            {
                reason = LocalizedText.FromException(e);
                ResetArrayEdit();
                return false;
            }
        }

        private void PollArrayEdit()
        {
            if (m_ArrayPreparation == null) return;
            bool finished = false;
            PartArrayAction action = null;
            try
            {
                if (!m_ArrayTransaction.IsValid) throw new OperationCanceledException();
                if (!m_ArrayPreparation.Poll()) return;
                finished = true;
                action = new PartArrayAction(m_ArraySource, m_ArrayPreparation.FirstIndex, m_ArrayPreparation.Parts, m_HistoryContext, m_ArrayFocus, m_ArrayPreparation.BakeTransform, m_ArrayPreparation.SourceBefore, m_ArrayPreparation.CopyTransforms);
                if (!m_ArrayPreparation.Publish(() => m_ArrayTransaction.TryCommit(action), out var reason))
                    throw new LocalizedTextException(reason);
                action = null;
                m_ArrayResult = LocalizedText.Empty;
                // Publication and history are already committed.
                // A UI refresh failure shouldnt report the geometry operation as failed or roll it back.
                try
                {
                    m_ProceduralObjectToolSystem?.ArrayApplied();
                }
                catch (Exception e)
                {
                    Mod.log.Warn("[PO Array] Could not refresh the tool after apply: " + e);
                }
            }
            catch (OperationCanceledException)
            {
                finished = true;
                m_ArrayResult = LocalizedText.For("PO2.PartArray.Status.Cancelled");
            }
            catch (Exception e)
            {
                finished = true;
                m_ArrayResult = LocalizedText.FromException(e);
                Mod.log.Warn("[PO Array] " + e);
            }
            finally
            {
                action?.Dispose();
                if (finished) ResetArrayEdit();
            }
        }

        internal void ResetArrayEdit()
        {
            if (m_ArrayPreparation != null)
            {
                m_ArrayPreparation.Dispose();
                m_ArrayDrain = m_ArrayPreparation.Drain;
                m_ArrayPreparation = null;
            }

            m_ArrayTransaction?.Dispose();
            m_ArrayTransaction = null;
        }

        internal PartArrayPreparation PrepareArrayReplay(ProceduralObject obj, int sourceIndex, MeshPart[] parts, CancellationToken token, UnityEngine.Matrix4x4? bakeTransform = null, MeshPart sourceReplacement = null, UnityEngine.Matrix4x4[] copyTransforms = null) =>
            new PartArrayPreparation(this, obj, sourceIndex, default, token, parts, bakeTransform, sourceReplacement, copyTransforms: copyTransforms);

        internal bool ValidateArrayIdentities(MeshPart[] parts, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            var ids = new System.Collections.Generic.HashSet<Guid>();
            foreach (var part in parts)
                if (part == null || part.PartHistoryId == Guid.Empty || !ids.Add(part.PartHistoryId))
                {
                    reason = LocalizedText.For("PO2.PartArray.Error.InvalidIdentity");
                    return false;
                }

            foreach (var obj in m_Objects.Values)
            foreach (var part in obj.Parts)
                if (ids.Contains(part.PartHistoryId))
                {
                    reason = LocalizedText.For("PO2.PartArray.Error.PartAlreadyOwned");
                    return false;
                }

            return true;
        }

        internal bool TryDetachArray(HistoryObjectKey key, int first, Guid[] ids, out MeshPart[] parts, out IArrayRemoval removal, out LocalizedText reason)
        {
            parts = null;
            removal = null;
            reason = LocalizedText.Empty;
            if (!ActionHistory.IsReplaying || !m_HistoryContext.TryResolve(key, out var obj))
            {
                reason = LocalizedText.For("PO2.PartArray.Error.ObjectMissing");
                return false;
            }

            var staged = m_Renderer.StagePartRemoval(obj, first, ids);
            try
            {
                if (!obj.TryDetachHistoryParts(ids, first, out parts, out reason)) return false;
                try
                {
                    staged.Adopt();
                }
                catch
                {
                    if (!obj.TryAppendHistoryParts(parts, first, out _)) throw new LocalizedTextException(LocalizedText.For("PO2.PartArray.Error.DetachedRestore"));
                    staged.Restore();
                    parts = null;
                    throw;
                }

                removal = staged;
                staged = null;
                return true;
            }
            finally
            {
                staged?.Dispose();
            }
        }
    }
}