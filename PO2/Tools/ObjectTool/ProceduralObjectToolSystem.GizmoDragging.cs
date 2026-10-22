using Game;
using Game.Input;
using Game.Simulation;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Tools.Gizmos;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private GizmoHandle m_ActiveGizmoHandle = GizmoHandle.None;

        // Only one of these drag records is active at a time.
        // The target kind and handle select the matching record
        // without allocating a shared hierarchy.
        private bool m_IsGizmoDragging;
        private GizmoTargetKind m_ActiveGizmoTargetKind;
        private HistoryEdit m_ObjectHistoryDrag;
        private readonly PartTransformDragState m_PartTransformDrag = new PartTransformDragState();
        private GizmoDragModifierState m_DragModifierState;
        private GizmoDragModifierState m_SecondaryDragModifierState;
        public GizmoHandle ActiveGizmoHandle => m_ActiveGizmoHandle;
        public bool IsGizmoDragging => m_IsGizmoDragging;

        private void BeginGizmoDrag(Camera camera, Ray mouseRay, in GizmoContext context, GizmoHandle handle)
        {
            if (context.TargetKind == GizmoTargetKind.VertexSelection)
            {
                if (GizmoMath.IsTranslationHandle(handle))
                {
                    BeginVertexMoveDrag(camera, mouseRay, context, handle);
                }
                else if (GizmoMath.IsRotationHandle(handle))
                {
                    BeginRotateDrag(mouseRay, context, handle);
                }
                else if (GizmoMath.IsScaleHandle(handle))
                {
                    BeginScaleDrag(camera, mouseRay, context, handle);
                }

                return;
            }

            if (context.TargetKind != GizmoTargetKind.Object && context.TargetKind != GizmoTargetKind.Part && context.TargetKind != GizmoTargetKind.CageCorners)
            {
                return;
            }

            if (GizmoMath.IsTranslationHandle(handle))
            {
                BeginTranslateDrag(camera, mouseRay, context, handle);
            }
            else if (GizmoMath.IsRotationHandle(handle))
            {
                BeginRotateDrag(mouseRay, context, handle);
            }
            else if (GizmoMath.IsScaleHandle(handle))
            {
                BeginScaleDrag(camera, mouseRay, context, handle);
            }
        }

        private bool TryBeginPartTransform(ProceduralObject selected, in GizmoContext context, PartTransformKind transformKind)
        {
            if (!m_PartTransformDrag.TryBegin(selected, context.PartIndex, context.Pivot, transformKind, m_ProceduralObjectSystem, out string failureReason))
            {
                Mod.log.Warn($"[PO Part] Could not start the transform: " + failureReason);
                m_PartTransformDrag.Reset();
                return false;
            }

            return true;
        }

        private void StartGizmoDrag(GizmoHandle handle, GizmoTargetKind targetKind)
        {
            if (targetKind == GizmoTargetKind.Object)
            {
                var selected = m_ProceduralObjectSystem.SelectedObject;
                if (selected == null) return;
                LocalizedText description = GizmoMath.IsTranslationHandle(handle) ? LocalizedText.For("PO2.History.Action.Object.Move") : GizmoMath.IsRotationHandle(handle) ? LocalizedText.For("PO2.History.Action.Object.Rotate") : LocalizedText.For("PO2.History.Action.Object.Scale");
                m_ObjectHistoryDrag = m_ProceduralObjectSystem.BeginObjectHistory(selected, ObjectValueKind.Transform, description);
                if (m_ObjectHistoryDrag == null) return;
            }

            Keyboard keyboard = Keyboard.current;

            bool precision = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

            bool controlPressed = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);

            bool snapEnabled = m_SnapSettings.IsIncrementSnappingActive(controlPressed);

            // Modifier state remembers when precision/snap changes mid-drag and
            // rebases the input, preventing the object from jumping at that moment.
            m_DragModifierState.Begin(precision, snapEnabled);

            m_SecondaryDragModifierState.Begin(precision, snapEnabled);

            m_IsGizmoDragging = true;
            m_ActiveGizmoTargetKind = targetKind;
            m_ActiveGizmoHandle = handle;
            m_HoveredGizmoHandle = handle;
        }

        private void UpdateActiveGizmoDrag(Ray mouseRay, Vector2 mousePosition, bool precision, bool controlPressed)
        {
            bool snappingActive = m_SnapSettings.IsSnappingActive(controlPressed);

            bool incrementSnapEnabled = m_SnapSettings.IsIncrementSnappingActive(controlPressed);

            bool targetSnapEnabled = snappingActive && (m_SnapSettings.POVerticesEnabled || m_SnapSettings.POEdgesEnabled || m_SnapSettings.GameSurfacesEnabled);

            // Part-aware source and sibling filtering arrive in C6.
            // Until then part movement uses precision and increment snapping only.
            if (m_ActiveGizmoTargetKind == GizmoTargetKind.Part)
            {
                targetSnapEnabled = false;
            }

            if (m_ActiveGizmoTargetKind == GizmoTargetKind.VertexSelection)
            {
                if (GizmoMath.IsTranslationHandle(m_ActiveGizmoHandle))
                {
                    UpdateVertexMoveDrag(mouseRay, mousePosition, precision, incrementSnapEnabled, targetSnapEnabled);
                }
                else if (GizmoMath.IsRotationHandle(m_ActiveGizmoHandle))
                {
                    UpdateRotateDrag(mouseRay, precision, incrementSnapEnabled);
                }
                else if (GizmoMath.IsScaleHandle(m_ActiveGizmoHandle))
                {
                    UpdateScaleDrag(mouseRay, precision, incrementSnapEnabled);
                }
                else
                {
                    CancelGizmoDrag(restoreTransform: true);
                }

                return;
            }

            if (GizmoMath.IsTranslationHandle(m_ActiveGizmoHandle))
            {
                UpdateTranslateDrag(mouseRay, mousePosition, precision, incrementSnapEnabled, targetSnapEnabled);
            }
            else if (GizmoMath.IsRotationHandle(m_ActiveGizmoHandle))
            {
                UpdateRotateDrag(mouseRay, precision, incrementSnapEnabled);
            }
            else if (GizmoMath.IsScaleHandle(m_ActiveGizmoHandle))
            {
                UpdateScaleDrag(mouseRay, precision, incrementSnapEnabled);
            }
        }

        private bool TryApplyPartTransformPreview(ProceduralObject selected, Matrix4x4 worldFromStart)
        {
            if (m_PartTransformDrag.TryApplyWorldTransform(selected, worldFromStart, out bool _, out string failureReason))
            {
                return true;
            }

            Mod.log.Error($"[PO Part] Transform preview failed for object " + $"{m_PartTransformDrag.ObjectId} part " + $"{m_PartTransformDrag.PartIndex}: {failureReason}");
            CancelGizmoDrag(restoreTransform: true);
            return false;
        }

        private static Matrix4x4 CreateWorldScaleTransform(Vector3 pivot, TransformFrame frame, Vector3 scaleFactors)
        {
            return SelectionTransformMath.CreateWorldScale(pivot, frame, scaleFactors);
        }

        private void CompleteGizmoDrag()
        {
            if (!m_IsGizmoDragging)
            {
                return;
            }

            int objectId = GetActiveDragObjectId();

            GizmoHandle completedHandle = m_ActiveGizmoHandle;

            GizmoTargetKind completedTarget = m_ActiveGizmoTargetKind;

            int partIndex = m_PartTransformDrag.PartIndex;
            bool deformationCompletionPending = false;
            if (completedTarget == GizmoTargetKind.CageCorners)
            {
                var selected = m_ProceduralObjectSystem.SelectedObject;
                if (!m_CageDrag.TryBeginCompletion(selected, out deformationCompletionPending, out LocalizedText reason))
                    Mod.log.Error("[PO Deformation] " + reason);
            }
            else if (completedTarget == GizmoTargetKind.Part)
            {
                ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;
                string failureReason = "The transformed object is no longer selected.";

                if (selected == null || selected.Id != objectId || !m_PartTransformDrag.TryComplete(selected, out failureReason))
                {
                    Mod.log.Error($"[PO Part] Could not commit {completedHandle} " + $"for object {objectId} part {partIndex}: " + failureReason);
                }
            }

            // The object/mesh already contains the last live update.
            // Completion only releases the captured rollback state.
            if (completedTarget == GizmoTargetKind.Object && m_ObjectHistoryDrag != null && !m_ObjectHistoryDrag.Complete())
            {
                Mod.log.Error("[PO History] Object drag could not be recorded and was restored.");
            }

            if (completedTarget == GizmoTargetKind.VertexSelection)
            {
                ProceduralObject snapObject;
                bool snapValid, committed;
                snapValid = m_ProceduralObjectSystem.TryGetObject(objectId, out snapObject) && m_MeshSnapPreparation.ValidateActive(snapObject);
                committed = m_VertexTransformDrag.History != null && m_VertexTransformDrag.History.Complete();
                if (!committed) Mod.log.Error("[PO History] Vertex drag was restored because it could not be recorded.");
                FinishMeshSnapTranslation(snapObject, snapValid && committed, m_VertexTransformDrag.LastAppliedLocalDelta);
            }

            ClearActiveDragState(preserveCageDeformation: deformationCompletionPending);
        }

        private void CancelGizmoDrag(bool restoreTransform)
        {
            if (!m_IsGizmoDragging)
            {
                m_ActiveGizmoHandle = GizmoHandle.None;
                return;
            }

            int objectId = GetActiveDragObjectId();

            GizmoHandle cancelledHandle = m_ActiveGizmoHandle;

            GizmoTargetKind cancelledTarget = m_ActiveGizmoTargetKind;

            if (cancelledTarget == GizmoTargetKind.CageCorners)
            {
                ClearActiveDragState();
                return;
            }

            int partIndex = m_PartTransformDrag.PartIndex;

            if (restoreTransform)
            {
                // Every drag captures its starting state.
                // Thus Escape or tool shutdown restores exactly instead of applying an inverse.
                if (cancelledTarget == GizmoTargetKind.VertexSelection)
                {
                    ProceduralObject editedObject = null;
                    bool canRestore, snapValid;
                    canRestore = m_MeshEditController != null && m_ProceduralObjectSystem.TryGetObject(objectId, out editedObject);
                    snapValid = canRestore && m_MeshSnapPreparation.ValidateActive(editedObject);
                    if (canRestore)
                    {
                        bool restored;
                        restored = !m_VertexTransformDrag.HasAppliedDelta || m_MeshEditController.RestoreVertexMove(editedObject, m_VertexTransformDrag);
                        FinishMeshSnapTranslation(editedObject, snapValid && restored, Vector3.zero);
                    }
                }
                else if (cancelledTarget == GizmoTargetKind.Part)
                {
                    ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;
                    string failureReason = "The transformed object is no longer selected.";

                    if (selected == null || selected.Id != objectId || !m_PartTransformDrag.TryCancel(selected, out failureReason))
                    {
                        Mod.log.Error($"[PO Part] Could not restore {cancelledHandle} " + $"for object {objectId} part {partIndex}: " + failureReason);
                    }
                }
                else
                {
                    ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

                    if (selected != null && selected.Id == objectId)
                    {
                        if (GizmoMath.IsTranslationHandle(cancelledHandle))
                        {
                            selected.SetTransform(m_TranslateDrag.StartTransform);
                        }
                        else if (GizmoMath.IsRotationHandle(cancelledHandle))
                        {
                            selected.SetTransform(m_RotateDrag.StartTransform);
                        }
                        else if (GizmoMath.IsScaleHandle(cancelledHandle))
                        {
                            selected.SetTransform(m_ScaleDrag.StartTransform);
                        }
                    }
                }
            }

            ClearActiveDragState();
        }

        private int GetActiveDragObjectId()
        {
            if (m_ActiveGizmoTargetKind == GizmoTargetKind.VertexSelection)
            {
                return m_VertexTransformDrag.ObjectId;
            }

            if (m_ActiveGizmoTargetKind == GizmoTargetKind.CageCorners) return m_CageDrag.ObjectId;

            if (m_ActiveGizmoTargetKind == GizmoTargetKind.Part)
            {
                return m_PartTransformDrag.ObjectId;
            }

            if (GizmoMath.IsRotationHandle(m_ActiveGizmoHandle))
            {
                return m_RotateDrag.ObjectId;
            }

            if (GizmoMath.IsScaleHandle(m_ActiveGizmoHandle))
            {
                return m_ScaleDrag.ObjectId;
            }

            return m_TranslateDrag.ObjectId;
        }

        private void ClearActiveDragState(bool preserveCageDeformation = false)
        {
            if (m_MeshSnapPreparation.ActiveGesture) m_MeshSnapPreparation.Invalidate();
            m_MeshSnapAttached = m_MeshSnapRequested = false;
            m_MeshSnapInput.Reset();
            m_ObjectHistoryDrag?.Dispose();
            m_ObjectHistoryDrag = null;
            m_IsGizmoDragging = false;
            m_ActiveGizmoTargetKind = GizmoTargetKind.Object;
            m_ActiveGizmoHandle = GizmoHandle.None;

            m_TranslateDrag = default;
            m_RotateDrag = default;
            m_ScaleDrag = default;
            m_PartTransformDrag.Reset();
            if (!preserveCageDeformation)
            {
                ResetCageDrag();
            }

            m_DragModifierState.Reset();
            m_SecondaryDragModifierState.Reset();
            m_TranslationSnapState.Reset();
            ClearMeshTranslationSnapTargetSession();
            m_TranslationSceneSnapWasEnabled = false;
            m_ObjectTranslationSnapCaptureReady = false;
            m_ObjectTranslationSnapTargetCache.Clear();
            m_VertexTransformDrag.Reset();

            if (m_MeshEditController != null)
            {
                m_MeshEditController.SetVertexHoverSuppressed(false);
            }
        }

        private void UpdateMeshGizmoDragInput(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool primaryReleased, bool shiftPressed, bool controlPressed)
        {
            Ray activeMouseRay = camera.ScreenPointToRay(mousePosition);

            UpdateActiveGizmoDrag(activeMouseRay, mousePosition, shiftPressed, controlPressed);

            // Keep projected markers and the selected-vertex centroid
            // current while the active drag owns the pointer.
            // Hover work remains suppressed until the drag ends.
            m_MeshEditController.UpdateFrame(editedObject, camera, mousePosition, updateHover: false);

            m_MeshEditController.SetVertexHoverSuppressed(true);

            if (primaryReleased)
            {
                CompleteGizmoDrag();
            }
        }
    }
}