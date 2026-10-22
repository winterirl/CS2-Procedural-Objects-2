using System;
using Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Localization;
using PO2.Tools.Gizmos;
using PO2.Tools.Snapping;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private readonly MeshTranslationSnapPreparation m_MeshSnapPreparation = new MeshTranslationSnapPreparation();
        private readonly MeshTranslationSnapInputGate m_MeshSnapInput = new MeshTranslationSnapInputGate();
        private int m_MeshSnapCaptureFrame = -1;
        private bool m_MeshSnapAttached, m_MeshSnapRequested;
        public LocalizedText MeshTranslationPreparationStatus => !m_MeshSnapRequested ? LocalizedText.Empty : m_MeshSnapPreparation.HasFailed ? LocalizedText.For("PO2.Transform.Snapping.Status.Unavailable", ("reason", m_MeshSnapPreparation.Failure)) : !m_MeshSnapAttached ? LocalizedText.For("PO2.Transform.Snapping.Status.Preparing") : LocalizedText.Empty;

        private bool CanPrepareMeshTranslation(ProceduralObject obj) =>
            m_Mode == ObjectToolMode.MeshEdit && obj != null && !obj.IsLocked && m_MeshEditController != null && m_MeshEditController.IsActive && m_MeshEditController.SelectionMode != MeshSelectionMode.SurfaceArea && m_MeshEditController.EffectiveSelectionCount > 0 && !MarkerPreparationPending && !GpuInteractionPending && !TopologyEditBusy && !m_ProceduralObjectSystem.HistoryBusy && !AssetConversionBusy && !DeformationCommitBusy && !DeformationSnapshotBusy && !m_IsVertexSlideDragging && !m_BoxSelection.IsGestureActive && !IsMeshTopologyInteractionActive && !AxisPickingActive && !m_MeshEditController.IsAlignmentTargeting && !m_MeshEditController.IsVertexSlideTargeting && m_MeshEditController.CanEditSelectedGeometry(obj);

        private bool HasMeshSnapTargets => m_SnapSettings.POVerticesEnabled || m_SnapSettings.POEdgesEnabled || m_SnapSettings.GameSurfacesEnabled;

        private bool MeshSnapRequestedNow()
        {
            var keyboard = Keyboard.current;
            bool control = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            return HasMeshSnapTargets && m_SnapSettings.IsSnappingActive(control);
        }

        private void RefreshMeshTranslationPreparation(ProceduralObject obj, bool request = false)
        {
            if (!CanPrepareMeshTranslation(obj) || (!m_MeshSnapPreparation.ActiveGesture && m_IsGizmoDragging))
            {
                m_MeshSnapPreparation.Suspend();
                return;
            }

            if (!HasMeshSnapTargets)
            {
                m_MeshSnapPreparation.Suspend();
                return;
            }

            if (m_MeshSnapCaptureFrame == UnityEngine.Time.frameCount) return;
            m_MeshSnapCaptureFrame = UnityEngine.Time.frameCount;
            m_MeshEditController.PrepareTranslationSource(m_MeshSnapPreparation, obj, request, true);
        }

        private void BeginVertexMoveDrag(Camera camera, Ray mouseRay, in GizmoContext context, GizmoHandle handle)
        {
            if (m_IsGizmoDragging || !TryGetMeshEditObject(out var obj) || obj.Id != context.ObjectId || !CanPrepareMeshTranslation(obj)) return;
            m_MeshSnapAttached = false;
            m_MeshSnapRequested = MeshSnapRequestedNow();
            m_MeshSnapInput.Begin(InputManager.instance.mousePosition, m_MeshSnapRequested);
            try
            {
                ActivateVertexMove(camera, mouseRay, context, handle);
                if (!m_IsGizmoDragging) ClearActiveDragState();
            }
            catch (Exception error)
            {
                ClearActiveDragState();
                m_MeshSnapPreparation.Invalidate();
                Mod.log.Warn("[PO Mesh Snap] Could not start the mesh move: " + error.Message);
            }
        }

        private void CaptureMeshSnapStart(ProceduralObject obj)
        {
            m_VertexTransformDrag.SnapVertices = m_MeshEditController.BeginTranslationSource(m_MeshSnapPreparation, obj);
            m_VertexTransformDrag.SnapVertexCount = 0;
        }

        private void SealMeshSnapStart(ProceduralObject obj)
        {
            m_MeshSnapPreparation.SealGestureCapture(m_VertexTransformDrag.SnapVertexCount);
            m_VertexTransformDrag.SnapVertices = null;
            ClearMeshTranslationSnapTargetSession();
            EnsureMeshSnapTargetSession(obj, true);
            TryAttachMeshSnapSource(obj, true);
        }

        private void EnsureMeshSnapTargetSession(ProceduralObject obj, bool warm)
        {
            if (m_MeshTranslationSnapTargetSession == null && m_SnapIndexSystem != null && (m_SnapSettings.POVerticesEnabled || m_SnapSettings.POEdgesEnabled))
                m_SnapIndexSystem.TryCreateMeshTranslationTargetSession(obj, null, out m_MeshTranslationSnapTargetSession, m_MeshSnapPreparation.Ready?.Input.Layout.TargetFilter, warm ? -1 : m_MeshSnapInput.Sequence);
        }

        private void TryAttachMeshSnapSource(ProceduralObject obj, bool warm)
        {
            if (m_MeshSnapAttached || !m_MeshSnapPreparation.ValidateActive(obj) || m_MeshSnapPreparation.Ready == null) return;
            var prepared = m_MeshSnapPreparation.Ready;
            m_TranslationSnapState.Begin(new TranslationSnapSource(prepared), m_VertexTransformDrag.Constraint);
            m_MeshSnapAttached = true;
            m_MeshSnapInput.Adopt(warm);
            m_MeshTranslationSnapTargetSession?.SetFilter(prepared.Input.Layout.TargetFilter);
        }

        private bool UpdateMeshSnapReadiness(ProceduralObject obj, Vector2 mouse, bool requested)
        {
            m_MeshSnapRequested = requested;
            m_MeshSnapInput.Observe(mouse, requested);
            RefreshMeshTranslationPreparation(obj);
            EnsureMeshSnapTargetSession(obj, false);
            TryAttachMeshSnapSource(obj, false);
            m_MeshTranslationSnapTargetSession?.Refresh(m_MeshSnapInput.Sequence);
            if (m_TranslationSnapState.HasActiveSnap && m_TranslationSnapState.TargetKind != SnapTargetKind.GameSurface && (m_MeshTranslationSnapTargetSession == null || !m_MeshTranslationSnapTargetSession.IsTargetCurrent(m_TranslationSnapState.CurrentTarget)))
                m_TranslationSnapState.ClearActiveSnap();
            if (!m_MeshSnapPreparation.ValidateActive(obj))
            {
                m_MeshSnapAttached = false;
                m_TranslationSnapState.Reset();
                ClearMeshTranslationSnapTargetSession();
            }

            return m_MeshSnapAttached && m_MeshSnapInput.Armed;
        }

        // Finish the pending input before UI/history early returns. The worker
        // can never replay mouse-down or revive a released gesture.
        private bool PollMeshTranslationPreparation()
        {
            m_MeshSnapPreparation.Drain();
            if (!m_MeshSnapPreparation.ActiveGesture || !m_IsGizmoDragging) return false;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (!Application.isFocused || InputManager.instance.hasInputFieldFocus || mouse == null || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || mouse.rightButton.wasPressedThisFrame)
            {
                CancelGizmoDrag(restoreTransform: true);
                return true;
            }

            if (!mouse.leftButton.isPressed)
            {
                CompleteGizmoDrag();
                return true;
            }

            return false;
        }

        private void ResetMeshTranslationPreparation()
        {
            m_MeshSnapPreparation.Invalidate();
            m_MeshSnapInput.Reset();
            m_MeshSnapAttached = m_MeshSnapRequested = false;
            m_MeshSnapCaptureFrame = -1;
        }

        private void FinishMeshSnapTranslation(ProceduralObject obj, bool valid, Vector3 delta)
        {
            m_MeshSnapPreparation.EndGesture(obj, valid, delta);
            m_MeshSnapAttached = m_MeshSnapRequested = false;
            m_MeshSnapInput.Reset();
        }
    }
}