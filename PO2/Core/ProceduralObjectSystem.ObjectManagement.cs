using System;
using System.Collections.Generic;
using Game;
using Game.Tools;
using UnityEngine;
using PO2.Geometry;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Tools.ObjectTool;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        public bool CanDuplicateSelected =>
            TryGetSelected(out ProceduralObject selected) && !selected.IsLocked && (m_ProceduralObjectToolSystem == null || m_ProceduralObjectToolSystem.Mode != ObjectToolMode.MeshEdit && m_ProceduralObjectToolSystem.Mode != ObjectToolMode.CombineSource);

        public void RenameSelected(string name)
        {
            if (!TryGetSelected(out ProceduralObject selected))
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            string requestedName = name == null ? string.Empty : name.Trim();
            if (string.IsNullOrEmpty(requestedName))
            {
                Mod.log.Warn("[PO] Object name cannot be blank.");
                return;
            }

            string uniqueName = GetUniqueObjectName(requestedName, selected.Id);

            using (var history = BeginObjectHistory(selected, ObjectValueKind.Name, LocalizedText.For("PO2.History.Action.Object.Rename")))
            {
                if (history == null) return;
                if (!selected.Rename(uniqueName)) return;
                if (!history.Complete()) return;
            }
        }

        public void ToggleSelectedLocked()
        {
            if (!TryGetSelected(out ProceduralObject selected))
            {
                return;
            }

            if (m_ProceduralObjectToolSystem != null && m_ProceduralObjectToolSystem.Mode == ObjectToolMode.CombineSource)
            {
                return;
            }

            bool lockObject = !selected.IsLocked;
            if (lockObject && m_ProceduralObjectToolSystem != null)
            {
                m_ProceduralObjectToolSystem.PrepareSelectedObjectLock();
            }

            using (var history = BeginObjectHistory(selected, ObjectValueKind.Locked, LocalizedText.For("PO2.History.Action.Object.SetLock")))
            {
                if (history == null) return;
                if (!selected.SetLocked(lockObject)) return;
                if (!history.Complete()) return;
            }
        }

        private bool TryRemoveObject(int objectId, bool clearSelection, out ProceduralObject removed)
        {
            if (!m_Objects.TryGetValue(objectId, out removed))
            {
                return false;
            }

            if (!clearSelection && m_SelectedObjectId == objectId)
            {
                removed = null;
                return false;
            }

            if (m_HoveredObjectId == objectId)
            {
                ClearHoveredObject();
            }

            m_Renderer.Destroy(objectId);
            m_Objects.Remove(objectId);
            UntrackObjectName(removed);
            NotifyLodObjectRemoved(objectId);

            if (m_SelectedObjectId == objectId)
            {
                m_SelectedObjectId = null;
                m_SelectedPartIndex = 0;
                m_SelectedSurfaceAreaId = MeshPart.kDefaultSurfaceAreaId;
                NotifyLodSelectionChanged();
            }

            return true;
        }
    }
}