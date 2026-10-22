using System;
using System.Collections.Generic;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.History;
using PO2.Tools.ObjectTool;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        internal ActionHistory ActionHistory { get; private set; }
        private HistoryContext m_HistoryContext;

        internal bool HistoryBusy => ActionHistory != null && ActionHistory.IsReplayPending;
        internal bool CanReplayHistory => ActionHistory != null && IsToolActive && m_ProceduralObjectToolSystem != null && m_ProceduralObjectToolSystem.CanReplayHistory && !HistoryBusy;
        internal bool CanUndoHistory => CanReplayHistory && ActionHistory.CanUndo;
        internal bool CanRedoHistory => CanReplayHistory && ActionHistory.CanRedo;
        internal bool CancelHistoryReplay() => ActionHistory?.CancelReplay() ?? false;

        internal void UndoHistory()
        {
            if (!CanUndoHistory) return;
            if (m_ProceduralObjectToolSystem.ArrayActive) m_ProceduralObjectToolSystem.CancelArray();
            EndLodPreview();
            ActionHistory.Request(HistoryDirection.Undo);
        }

        internal void RedoHistory()
        {
            if (!CanRedoHistory) return;
            if (m_ProceduralObjectToolSystem.ArrayActive) m_ProceduralObjectToolSystem.CancelArray();
            EndLodPreview();
            ActionHistory.Request(HistoryDirection.Redo);
        }

        private void InitializeActionHistory()
        {
            m_HistoryContext = new HistoryContext(this, obj =>
            {
                m_Renderer?.InvalidateGeometryChanges(obj);
                World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>().NotifyHistoryGeometryChanged(obj);
            }, obj => World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>().CaptureTopologyHistorySelection(obj), (obj, selection) => World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>().RestoreTopologyHistorySelection(obj, selection), (obj, selection) => World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>().AdoptPreparedTopologySelection(obj, selection), (obj, change) =>
            {
                NotifyRendererGeometryChange(obj, change);
                World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>().NotifyHistoryGeometryChanged(obj, change);
            });
            ActionHistory = new ActionHistory(m_HistoryContext, message => Mod.log.Warn(message));
            ActionHistory.SetCapacity(Mod.Settings?.UndoHistorySteps ?? HistoryLimits.kDefaultCapacity);
        }

        internal IHistoryObjectRenderer PrepareHistoryObject(ProceduralObject obj) => m_Renderer.PrepareHistoryObject(obj);

        internal bool TryHistoryRegister(HistoryObjectKey key, ProceduralObject staging, out string reason, IHistoryObjectRenderer renderer = null)
        {
            reason = string.Empty;
            if (!(ActionHistory.IsReplaying || ActionHistory.IsCapturing) || !key.IsValid || staging == null || staging.Id != key.RuntimeId || staging.PersistentId != key.PersistentId || staging.Id == int.MaxValue || m_Objects.ContainsKey(key.RuntimeId))
            {
                reason = "History object registration does not match the expected identity or replay state.";
                return false;
            }

            var incoming = new HashSet<Guid>();
            foreach (var part in staging.Parts)
            {
                if (part.PartHistoryId == Guid.Empty || !incoming.Add(part.PartHistoryId))
                {
                    reason = "History object contains missing or repeated part identities.";
                    return false;
                }
            }

            foreach (var live in m_Objects.Values)
            {
                if (live.PersistentId == key.PersistentId)
                {
                    reason = "Persistent object identity is already registered.";
                    return false;
                }

                foreach (var part in live.Parts)
                {
                    if (incoming.Contains(part.PartHistoryId))
                    {
                        reason = "History part identity is already owned by a live object.";
                        return false;
                    }
                }
            }

            staging.MarkDirty(ProceduralObjectDirtyFlags.All);
            return TryRegisterNewObject(staging, false, null, null, out reason, renderer);
        }

        internal bool TryHistoryRemove(HistoryObjectKey key, out ProceduralObject detached, out string reason)
        {
            detached = null;
            reason = string.Empty;
            if (!(ActionHistory.IsReplaying || ActionHistory.IsCapturing) || !m_HistoryContext.TryResolve(key, out _))
            {
                reason = "History object removal does not match the expected identity or replay state.";
                return false;
            }

            try
            {
                return TryRemoveObject(key.RuntimeId, true, out detached);
            }
            catch (Exception exception)
            {
                if (m_HistoryContext.TryResolve(key, out var live)) live.MarkDirty(ProceduralObjectDirtyFlags.All);
                detached = null;
                reason = "Could not detach history object: " + exception.Message;
                return false;
            }
        }

        internal HistoryFocusState CaptureHistoryFocus()
        {
            if (!TryGetSelected(out var obj)) return default;
            var key = new HistoryObjectKey(obj.Id, obj.PersistentId);
            HistoryPartKey? part = null;
            if (m_SelectedPartIndex >= 0 && m_SelectedPartIndex < obj.Parts.Count)
            {
                part = new HistoryPartKey(key, obj.Parts[m_SelectedPartIndex].PartHistoryId, m_SelectedPartIndex);
            }

            return new HistoryFocusState(key, part, m_SelectedSurfaceAreaId);
        }

        internal bool TryHistoryInsertPart(HistoryPartKey key, int index, MeshPart part, out string reason)
        {
            reason = string.Empty;
            if (!(ActionHistory.IsReplaying || ActionHistory.IsCapturing) || !key.IsValid || !m_HistoryContext.TryResolve(key.Owner, out var owner))
            {
                reason = "Part insertion requires a matching owner during history replay.";
                return false;
            }

            foreach (var live in m_Objects.Values)
            {
                foreach (var existing in live.Parts)
                {
                    if (existing.PartHistoryId == key.PartId)
                    {
                        reason = "History part is already owned by a live object.";
                        return false;
                    }
                }
            }

            return owner.TryHistoryInsertPart(key.PartId, index, part, out reason);
        }

        internal bool TryHistoryRemovePart(HistoryPartKey key, int index, out MeshPart removed, out string reason)
        {
            removed = null;
            reason = string.Empty;
            if (!(ActionHistory.IsReplaying || ActionHistory.IsCapturing) || !m_HistoryContext.TryResolve(key.Owner, out var owner))
            {
                reason = "Part removal requires a matching owner during history replay.";
                return false;
            }

            return owner.TryHistoryRemovePart(key.PartId, index, out removed, out reason);
        }

        internal void RestoreHistoryFocus(HistoryFocusState focus)
        {
            if (!(ActionHistory.IsReplaying || ActionHistory.IsCapturing)) throw new InvalidOperationException("Focus replay requires an active history transaction.");
            ProceduralObject obj = null;
            if (focus.Object.HasValue) m_HistoryContext.TryResolve(focus.Object.Value, out obj);
            int index = 0;
            if (obj != null && focus.Part.HasValue && focus.Part.Value.Owner.Equals(focus.Object.Value) && m_HistoryContext.TryResolve(focus.Part.Value, out _, out int resolvedIndex)) index = resolvedIndex;
            int areaId = MeshPart.kDefaultSurfaceAreaId;
            if (obj != null)
            {
                var part = obj.Parts[index];
                areaId = GetInitialSurfaceAreaId(part);
                foreach (var area in part.SurfaceAreas)
                    if (area.Id == focus.SurfaceAreaId)
                    {
                        areaId = area.Id;
                        break;
                    }
            }

            m_SelectedObjectId = obj == null ? (int?)null : obj.Id;
            m_SelectedPartIndex = index;
            m_SelectedSurfaceAreaId = areaId;
            NotifyLodSelectionChanged();
            // The focus fields commit before notifying tools. A notification
            // failure must not turn a completed model replay into a failed edit.
            try
            {
                ClearHoveredObject();
                m_ProceduralObjectToolSystem?.OnActiveMeshPartChanged();
            }
            catch (Exception exception)
            {
                try
                {
                    Mod.log.Error("[PO History] Focus notification failed: " + exception.Message);
                }
                catch
                {
                }
            }
        }
    }
}