using Game;
using Game.Input;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.History;
using PO2.Tools.Gizmos;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private GizmoMode m_GizmoMode = GizmoMode.Translate;

        private TransformTarget m_TransformTarget = TransformTarget.Object;

        private GizmoHandle m_HoveredGizmoHandle = GizmoHandle.None;
        public GizmoMode GizmoMode => m_GizmoMode;

        public TransformTarget TransformTarget =>
            m_TransformTarget;

        public GizmoHandle HoveredGizmoHandle => m_HoveredGizmoHandle;

        public bool TryGetGizmoContext(Camera camera, out GizmoContext context)
        {
            context = default;

            if (camera == null || AxisPickingActive || !m_ProceduralObjectSystem.IsToolActive || DeformationCommitBusy)
            {
                return false;
            }

            if (ArrayActive) return TryGetArrayGizmoContext(camera, out context);

            switch (m_Mode)
            {
                case ObjectToolMode.Transform:
                    return TryGetObjectGizmoContext(camera, out context);

                case ObjectToolMode.MeshEdit:
                    return TryGetMeshEditGizmoContext(camera, out context);

                default:
                    return false;
            }
        }

        private bool TryGetObjectGizmoContext(Camera camera, out GizmoContext context)
        {
            context = default;

            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || selected.IsLocked || MirrorMode)
            {
                return false;
            }

            if (m_GizmoMode != GizmoMode.Translate && m_GizmoMode != GizmoMode.Rotate && m_GizmoMode != GizmoMode.Scale)
            {
                return false;
            }

            GizmoTargetKind targetKind = m_TransformTarget == TransformTarget.ActivePart ? GizmoTargetKind.Part : GizmoTargetKind.Object;

            int partIndex = targetKind == GizmoTargetKind.Part ? m_ProceduralObjectSystem.SelectedPartIndex : -1;

            bool activeTargetDrag = m_IsGizmoDragging && m_ActiveGizmoTargetKind == targetKind && GetActiveDragObjectId() == selected.Id;

            if (m_DeformMode) return TryGetCageGizmoContext(camera, selected, out context);
            if (targetKind == GizmoTargetKind.Part && !selected.CanEditGeometry(partIndex, out _)) return false;

            Vector3 pivot;
            Bounds? selectedBounds = null;

            if (targetKind == GizmoTargetKind.Part)
            {
                if (activeTargetDrag)
                {
                    pivot = m_PartTransformDrag.CurrentPivotWorld;
                }
                else if (!TryGetPartGizmoPivot(selected, partIndex, out pivot))
                {
                    return false;
                }
            }
            else
            {
                pivot = selected.WorldPivot;

                if (selected.TryGetLocalBounds(out Bounds localBounds))
                {
                    selectedBounds = localBounds;
                }
            }

            float gizmoSize;
            float activeScaleHandleLength = 0f;
            TransformFrame frame = ResolveGizmoFrame(selected, m_GizmoMode, targetKind);

            if (activeTargetDrag)
            {
                if (GizmoMath.IsTranslationHandle(m_ActiveGizmoHandle))
                {
                    gizmoSize = m_TranslateDrag.GizmoSize;
                    frame = m_TranslateDrag.Frame;
                }
                else if (GizmoMath.IsRotationHandle(m_ActiveGizmoHandle))
                {
                    gizmoSize = m_RotateDrag.GizmoSize;
                    frame = m_RotateDrag.Frame;
                }
                else if (GizmoMath.IsScaleHandle(m_ActiveGizmoHandle))
                {
                    gizmoSize = m_ScaleDrag.GizmoSize;
                    frame = m_ScaleDrag.Frame;
                    activeScaleHandleLength = m_ScaleDrag.ActiveHandleLength;
                }
                else
                {
                    gizmoSize = GetTransformGizmoSize(camera, targetKind, pivot, selectedBounds, selected.Transform.Scale);
                }
            }
            else
            {
                gizmoSize = GetTransformGizmoSize(camera, targetKind, pivot, selectedBounds, selected.Transform.Scale);
            }

            context = new GizmoContext(targetKind, selected.Id, partIndex, m_GizmoMode, pivot, frame, gizmoSize, activeScaleHandleLength);

            return true;
        }

        private bool TryGetMeshEditGizmoContext(Camera camera, out GizmoContext context)
        {
            context = default;
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || editedObject.IsLocked || m_MeshEditController == null || IsMeshTopologyInteractionActive || MarkerPreparationPending || m_BoxSelection.IsGestureActive || m_MeshEditController.IsAlignmentTargeting || m_MeshEditController.IsVertexSlideTargeting || m_IsVertexSlideDragging || (!m_IsGizmoDragging && m_ProceduralObjectSystem.ActionHistory.IsBusy) || !TryGetMeshTransformPivot(editedObject, out Vector3 pivot))
            {
                return false;
            }

            bool dragging = m_IsGizmoDragging && m_ActiveGizmoTargetKind == GizmoTargetKind.VertexSelection && m_VertexTransformDrag.ObjectId == editedObject.Id;
            if (dragging && m_MeshTransformMode != GizmoMode.Translate)
            {
                pivot = m_VertexTransformDrag.StartPivot;
            }

            float gizmoSize = dragging ? m_VertexTransformDrag.GizmoSize : GizmoMath.GetScreenSpaceGizmoSize(camera, pivot);
            TransformFrame frame = dragging ? m_VertexTransformDrag.Frame : ResolveActiveTransformFrame(editedObject);
            float scaleHandleLength = dragging && m_MeshTransformMode == GizmoMode.Scale ? m_ScaleDrag.ActiveHandleLength : 0f;

            context = new GizmoContext(GizmoTargetKind.VertexSelection, editedObject.Id, -1, m_MeshTransformMode, pivot, frame, gizmoSize, scaleHandleLength);
            return true;
        }

        private TransformFrame ResolveGizmoFrame(ProceduralObject proceduralObject, GizmoMode mode, GizmoTargetKind targetKind)
        {
            return mode == GizmoMode.Scale && targetKind == GizmoTargetKind.Object ? ResolveScaleFrame(proceduralObject) : ResolveActiveTransformFrame(proceduralObject);
        }

        private static float GetTransformGizmoSize(Camera camera, GizmoTargetKind targetKind, Vector3 pivot, Bounds? objectBounds, Vector3 objectScale)
        {
            return targetKind == GizmoTargetKind.Part ? GizmoMath.GetScreenSpaceGizmoSize(camera, pivot) : GizmoMath.GetGizmoSize(camera, pivot, objectBounds, objectScale);
        }

        private bool TryGetPartGizmoPivot(ProceduralObject proceduralObject, int partIndex, out Vector3 worldPivot)
        {
            worldPivot = Vector3.zero;

            if (proceduralObject == null || partIndex < 0 || partIndex >= proceduralObject.Parts.Count)
            {
                return false;
            }

            MeshPart part = proceduralObject.Parts[partIndex];
            worldPivot = proceduralObject.Transform.TransformPoint(part.LocalPivot);
            return IsFinite(worldPivot);
        }

        public void SetGizmoMode(GizmoMode mode)
        {
            if (IsObjectEditorMode || DeformationCommitBusy)
            {
                return;
            }

            if (mode != GizmoMode.Translate && mode != GizmoMode.Rotate && mode != GizmoMode.Scale)
            {
                Mod.log.Warn($"[PO Gizmo] Gizmo mode {mode} is not implemented yet.");
                return;
            }

            bool wasAlternateMode = m_DeformMode || m_MirrorMode;
            m_DeformMode = false;
            m_MirrorMode = false;
            CancelBoxSelection();
            if (m_GizmoMode == mode && !wasAlternateMode)
            {
                return;
            }

            CancelGizmoDrag(restoreTransform: true);
            ClearGizmoHover();
            m_GizmoMode = mode;

            if (m_ProceduralObjectSystem.SelectedObject != null && !IsPrimitivePlacementMode && !IsObjectEditorMode)
            {
                m_Mode = ObjectToolMode.Transform;
            }
        }

        public void SetTranslateMode()
        {
            SetGizmoMode(GizmoMode.Translate);
        }

        public void SetRotateMode()
        {
            SetGizmoMode(GizmoMode.Rotate);
        }

        public void SetScaleMode()
        {
            SetGizmoMode(GizmoMode.Scale);
        }

        public void SetObjectTransformTarget()
        {
            SetTransformTarget(TransformTarget.Object);
        }

        public void SetActivePartTransformTarget()
        {
            SetTransformTarget(TransformTarget.ActivePart);
        }

        private void SetTransformTarget(TransformTarget target)
        {
            if (DeformationCommitBusy) return;
            if (target != TransformTarget.Object && target != TransformTarget.ActivePart)
            {
                Mod.log.Warn($"[PO Gizmo] Transform target {target} is not supported.");
                return;
            }

            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (target == TransformTarget.ActivePart && (selected == null || m_ProceduralObjectSystem.SelectedPartIndex < 0))
            {
                return;
            }

            if (m_TransformTarget == target)
            {
                return;
            }

            CancelGizmoDrag(restoreTransform: true);
            ClearGizmoHover();
            m_TransformTarget = target;

            if (selected != null && !IsObjectEditorMode && !IsPrimitivePlacementMode)
            {
                m_Mode = ObjectToolMode.Transform;
            }
        }

        private void UpdateGizmoHover(Ray mouseRay, in GizmoContext context)
        {
            ClearGizmoHover();

            switch (context.Mode)
            {
                case GizmoMode.Translate:
                    GizmoMath.TryPickTranslationHandle(mouseRay, context.Pivot, context.Frame, context.Size, out m_HoveredGizmoHandle, out _);
                    break;

                case GizmoMode.Rotate:
                    GizmoMath.TryPickRotationHandle(mouseRay, context.Pivot, context.Frame, GizmoMath.GetRotationRadius(context.Size), out m_HoveredGizmoHandle, out _);
                    break;

                case GizmoMode.Scale:
                    GizmoMath.TryPickScaleHandle(mouseRay, context.Pivot, context.Frame, context.Size, out m_HoveredGizmoHandle, out _);
                    break;
            }
        }

        private void ClearGizmoHover()
        {
            m_HoveredGizmoHandle = GizmoHandle.None;
        }
    }
}