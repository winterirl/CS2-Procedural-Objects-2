using Game;
using Game.Input;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Tools.Gizmos;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        public bool CanSetMeshPivot
        {
            get
            {
                if (!TryGetMeshEditObject(out ProceduralObject editedObject) || editedObject.IsLocked || m_MeshEditController == null || (m_MeshEditController.SelectionMode != MeshSelectionMode.Vertex && m_MeshEditController.SelectionMode != MeshSelectionMode.Edge))
                {
                    return false;
                }

                if (m_MeshEditController.VertexScope == MeshEditVertexScope.CurrentPart)
                {
                    int partIndex = m_ProceduralObjectSystem.SelectedPartIndex;
                    if (partIndex < 0 || partIndex >= editedObject.Parts.Count)
                    {
                        return false;
                    }
                }

                return !m_IsGizmoDragging && !m_IsVertexSlideDragging && !m_BoxSelection.IsGestureActive && !m_MeshEditController.IsAlignmentTargeting && !m_MeshEditController.IsVertexSlideTargeting && !IsMeshTopologyInteractionActive && !m_ProceduralObjectSystem.ActionHistory.IsBusy;
            }
        }

        public bool MeshPivotUsesSelection
        {
            get { return TryGetMeshEditObject(out ProceduralObject editedObject) && m_MeshEditController != null && m_MeshEditController.HasPartialPivotSelection(editedObject); }
        }

        public bool TryGetDisplayedPivotWorld(out Vector3 worldPivot)
        {
            worldPivot = Vector3.zero;
            if (AxisPickingActive) return false;
            ProceduralObject selected = m_ProceduralObjectSystem?.SelectedObject;
            if (selected == null)
            {
                return false;
            }

            bool usePartPivot = m_Mode == ObjectToolMode.MeshEdit ? m_MeshEditController != null && m_MeshEditController.VertexScope == MeshEditVertexScope.CurrentPart : m_TransformTarget == TransformTarget.ActivePart;

            if (!usePartPivot)
            {
                worldPivot = selected.WorldPivot;
                return IsFinite(worldPivot);
            }

            return TryGetPartGizmoPivot(selected, m_ProceduralObjectSystem.SelectedPartIndex, out worldPivot);
        }

        public void SetMeshPivot()
        {
            if (!CanSetMeshPivot || !TryGetMeshEditObject(out ProceduralObject editedObject) || !m_MeshEditController.TryGetPivotLocalPosition(editedObject, out Vector3 localPivot, out bool usesSelection))
            {
                return;
            }

            bool objectPivot = m_MeshEditController.VertexScope == MeshEditVertexScope.AllParts;
            LocalizedText description = usesSelection ? LocalizedText.For("PO2.History.Action.Pivot.Set") : LocalizedText.For("PO2.History.Action.Pivot.Center");
            bool changed;

            if (objectPivot)
            {
                using (HistoryEdit history = m_ProceduralObjectSystem.BeginObjectHistory(editedObject, ObjectValueKind.Pivot, description))
                {
                    if (history == null)
                    {
                        return;
                    }

                    changed = editedObject.SetLocalPivot(localPivot);
                    if (!history.Complete())
                    {
                        return;
                    }
                }
            }
            else
            {
                int partIndex = m_ProceduralObjectSystem.SelectedPartIndex;
                if (partIndex < 0 || partIndex >= editedObject.Parts.Count)
                {
                    return;
                }

                MeshPart part = editedObject.Parts[partIndex];
                using (HistoryEdit history = m_ProceduralObjectSystem.BeginPartPivotHistory(editedObject, partIndex, description))
                {
                    if (history == null)
                    {
                        return;
                    }

                    changed = part.SetLocalPivot(localPivot);
                    if (!history.Complete())
                    {
                        return;
                    }
                }
            }

            ClearGizmoHover();
            if (!changed)
            {
                return;
            }
        }
    }
}