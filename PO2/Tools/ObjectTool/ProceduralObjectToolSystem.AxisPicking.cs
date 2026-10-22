using System;
using Game.Input;
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
        // Workspace, transform mode/pivot, orientation, selected part, and cage state
        // stay in place. Only the mesh controller is borrowed for edge picking.
        private sealed class AxisPickSession
        {
            internal ProceduralObject Object;
            internal Guid PersistentId;
            internal int ObjectId;
            internal int PartIndex;
            internal uint PartVersion;
            internal uint DeformationVersion;
            internal MeshPart[] Parts;
            internal uint[] GeometryVersions;
            internal bool RestoreEditor;
            internal TopologySelection Selection;
            internal MeshEditVertexScope Scope;
            internal MeshEditVisibilityMode Visibility;
        }

        private AxisPickSession m_AxisPickSession;
        public bool AxisPickingActive => m_AxisPickSession != null;

        public bool CanBeginAxisPicking
        {
            get
            {
                var obj = m_ProceduralObjectSystem.SelectedObject;
                return !AxisPickingActive && !m_ManagementWorkspaceActive && (m_Mode == ObjectToolMode.Transform || m_Mode == ObjectToolMode.MeshEdit) && obj != null && !obj.IsLocked && obj.Parts.Count > 0 && (m_Mode != ObjectToolMode.MeshEdit || TryGetMeshEditObject(out _)) && obj.CustomOrientations.Count < ProceduralObject.kMaxCustomOrientationCount && ActiveInteraction == NestedInteraction.None && !MeshMutationBusy && !m_ProceduralObjectSystem.HistoryBusy;
            }
        }

        public bool CanConfirmAxisPicking =>
            !MarkerPreparationPending && IsAxisPickTargetValid(m_AxisPickSession) && !m_AxisPickSession.Object.IsLocked && m_AxisPickSession.Object.CustomOrientations.Count < ProceduralObject.kMaxCustomOrientationCount && m_MeshEditController.TryGetSingleSelectedEdge(out var edge) && TryBuildCustomOrientationRotation(m_AxisPickSession.Object, edge, out _, out _);

        private bool IsAxisPickTargetValid(AxisPickSession session)
        {
            if (session == null) return false;
            var obj = m_ProceduralObjectSystem.SelectedObject;
            if (!ReferenceEquals(obj, session.Object) || obj.Id != session.ObjectId || obj.PersistentId != session.PersistentId || obj.PartVersion != session.PartVersion || obj.DeformationVersion != session.DeformationVersion || obj.Parts.Count != session.Parts.Length || m_ProceduralObjectSystem.SelectedPartIndex != session.PartIndex) return false;
            for (int i = 0; i < session.Parts.Length; i++)
            {
                if (!ReferenceEquals(obj.Parts[i], session.Parts[i]) || obj.Parts[i].GeometryVersion != session.GeometryVersions[i]) return false;
            }

            return true;
        }

        public void BeginAxisPicking()
        {
            if (!CanBeginAxisPicking) return;
            var obj = m_ProceduralObjectSystem.SelectedObject;
            bool editor = m_Mode == ObjectToolMode.MeshEdit;
            var session = new AxisPickSession
            {
                Object = obj, PersistentId = obj.PersistentId, ObjectId = obj.Id,
                PartIndex = m_ProceduralObjectSystem.SelectedPartIndex,
                PartVersion = obj.PartVersion, DeformationVersion = obj.DeformationVersion,
                Parts = new MeshPart[obj.Parts.Count], GeometryVersions = new uint[obj.Parts.Count],
                RestoreEditor = editor,
                Selection = editor ? m_MeshEditController.CaptureTopologyHistorySelection(obj) : null,
                Scope = editor ? m_MeshEditController.VertexScope : (m_TransformTarget == TransformTarget.Object ? MeshEditVertexScope.AllParts : MeshEditVertexScope.CurrentPart),
                Visibility = editor ? m_MeshEditController.VisibilityMode : m_MeshEditVisibilityPreference,
            };
            for (int i = 0; i < obj.Parts.Count; i++)
            {
                session.Parts[i] = obj.Parts[i];
                session.GeometryVersions[i] = obj.Parts[i].GeometryVersion;
            }

            if (!editor && !m_MeshEditController.Enter(obj, session.PartIndex)) return;
            m_AxisPickSession = session;
            m_MeshEditController.SetSelectionMode(obj, MeshSelectionMode.Edge);
            m_MeshEditController.SetVertexScope(obj, session.Scope, session.PartIndex);
            m_MeshEditController.SetVisibilityMode(obj, session.Visibility);
            m_MeshEditController.ClearSelection();
            m_MeshEditController.ClearHover();
            m_PendingObjectPick.Clear();
            ClearGizmoHover();
        }

        public void ConfirmAxisPicking()
        {
            if (MeshMutationBusy || m_ProceduralObjectSystem.HistoryBusy || !CanConfirmAxisPicking) return;
            var obj = m_AxisPickSession.Object;
            if (!m_MeshEditController.TryGetSingleSelectedEdge(out var edge) || !TryBuildCustomOrientationRotation(obj, edge, out var rotation, out _)) return;
            int index;
            using (var history = m_ProceduralObjectSystem.BeginObjectHistory(obj, ObjectValueKind.Orientations, LocalizedText.For("PO2.History.Action.CustomAxis.Create")))
            {
                if (history == null || !obj.TryAddCustomOrientation(obj.GetNextCustomOrientationName(), rotation, out index)) return;
                if (!history.Complete()) return;
            }

            CancelAxisPicking();
            SetCustomTransformOrientation(index);
        }

        public void CancelAxisPicking()
        {
            CancelGpuInteraction();
            var session = m_AxisPickSession;
            if (session == null) return;
            bool restore = IsAxisPickTargetValid(session);
            m_AxisPickSession = null;
            m_PendingObjectPick.Clear();
            ClearGizmoHover();
            if (!session.RestoreEditor)
            {
                m_MeshEditController.Exit();
                return;
            }

            if (restore)
            {
                m_MeshEditController.SetVertexScope(session.Object, session.Scope, session.PartIndex);
                m_MeshEditController.SetVisibilityMode(session.Object, session.Visibility);
                m_MeshEditController.RestoreTopologyHistorySelection(session.Object, session.Selection);
                m_MeshEditController.ClearHover();
            }
            else
            {
                // Never restore stale element IDs after external deletion or geometry changes.
                m_MeshEditController.ClearSelection();
                m_MeshEditController.ClearHover();
                if (ReferenceEquals(m_ProceduralObjectSystem.SelectedObject, session.Object) && session.Selection != null)
                {
                    m_MeshEditController.SetSelectionMode(session.Object, (MeshSelectionMode)session.Selection.Mode);
                }
                else
                {
                    m_MeshEditController.Exit();
                }
            }
        }

        private void UpdateAxisPicking(bool allowWorldInput, bool confirmPressed)
        {
            if (!IsAxisPickTargetValid(m_AxisPickSession) || m_AxisPickSession.Object.IsLocked)
            {
                CancelAxisPicking();
                return;
            }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (!InputManager.instance.hasInputFieldFocus && ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame)))
            {
                CancelAxisPicking();
                return;
            }

            var camera = MeshEditCamera;
            if (camera == null) return;
            var position = InputManager.instance.mousePosition;
            m_MeshEditController.SetVertexHoverSuppressed(!allowWorldInput);
            m_MeshEditController.UpdateFrame(m_AxisPickSession.Object, camera, position, updateHover: allowWorldInput);
            if (MarkerPreparationPending)
            {
                CancelGpuInteraction();
                return;
            }

            StructuralEdgeRef edge = default;
            bool hasEdge = allowWorldInput && m_MeshEditController.TryPickEdge(position, out edge);
            m_MeshEditController.SetHoveredEdge(hasEdge ? edge : (StructuralEdgeRef?)null);
            if (m_MeshEditController.UsesGpuMarkers)
            {
                HandleGpuPointer(m_AxisPickSession.Object, camera, position, allowWorldInput && mouse != null && mouse.leftButton.wasPressedThisFrame, mouse == null || mouse.leftButton.wasReleasedThisFrame, false, false, false, GpuPointerKind.Axis);
                if (confirmPressed && !InputManager.instance.hasInputFieldFocus) m_GpuAxisConfirm = true;
                if (m_GpuAxisConfirm && m_GpuPointer == null)
                {
                    m_GpuAxisConfirm = false;
                    ConfirmAxisPicking();
                }

                return;
            }

            if (allowWorldInput && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                if (hasEdge) m_MeshEditController.ReplaceEdgeSelection(m_AxisPickSession.Object, new[] { edge });
                else m_MeshEditController.ClearSelection();
            }

            if (confirmPressed && !InputManager.instance.hasInputFieldFocus) ConfirmAxisPicking();
        }

        // Render access is deliberately separate from the mutation/editor predicates.
        public bool TryGetMeshVisualizationObject(out ProceduralObject obj)
        {
            if (AxisPickingActive && IsAxisPickTargetValid(m_AxisPickSession))
            {
                obj = m_AxisPickSession.Object;
                return true;
            }

            return TryGetActiveEditorObject(out obj);
        }
    }
}