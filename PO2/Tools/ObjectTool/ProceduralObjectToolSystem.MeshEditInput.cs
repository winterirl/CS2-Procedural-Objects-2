using Game;
using Game.Input;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.Tools.Gizmos;
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private JobHandle UpdateMeshEditMode(JobHandle inputDeps, bool primaryPressed, bool primaryReleased, bool shiftPressed, bool controlPressed, bool altPressed, bool continuePressed)
        {
            if (!TryGetActiveEditorObject(out ProceduralObject editedObject))
            {
                Mod.log.Warn("[PO Editor] Active target is no longer valid; " + "leaving the object editor.");

                LeaveMeshEdit(commitActiveDrag: false);
                return inputDeps;
            }

            Camera camera = MeshEditCamera;

            if (camera == null)
            {
                m_FaceOperation.ClearHover();
                ClearGizmoHover();
                return inputDeps;
            }

            Vector2 mousePosition = InputManager.instance.mousePosition;

            RefreshMeshTranslationPreparation(editedObject);

            // Active gestures take priority over hover and new selections.
            // Each branch owns the pointer until it commits, cancels, or releases.
            if (m_BoxSelection.IsGestureActive)
            {
                UpdateMeshBoxSelectionInput(editedObject, camera, mousePosition, primaryReleased, shiftPressed, controlPressed);
                return inputDeps;
            }

            if (m_IsVertexSlideDragging)
            {
                UpdateMeshVertexSlideDragInput(editedObject, camera, mousePosition, primaryReleased, shiftPressed);
                return inputDeps;
            }

            if (m_IsGizmoDragging && m_ActiveGizmoTargetKind == GizmoTargetKind.VertexSelection)
            {
                UpdateMeshGizmoDragInput(editedObject, camera, mousePosition, primaryReleased, shiftPressed, controlPressed);
                return inputDeps;
            }

            if (m_FaceOperation.IsActive)
            {
                UpdateMeshFaceOperationInput(editedObject, camera, mousePosition, primaryPressed, shiftPressed, controlPressed, continuePressed);
                return inputDeps;
            }

            if (m_LoopCut.IsActive)
            {
                UpdateMeshLoopCutInput(editedObject, camera, mousePosition, primaryPressed, shiftPressed, controlPressed);
                return inputDeps;
            }

            if (m_SurfaceCut.IsActive)
            {
                UpdateMeshSurfaceCutInput(editedObject, camera, mousePosition, primaryPressed, shiftPressed, controlPressed);
                return inputDeps;
            }

            m_MeshEditController.UpdateFrame(editedObject, camera, mousePosition);

            if (MarkerPreparationPending)
            {
                CancelGpuInteraction();
                ClearGizmoHover();
                return inputDeps;
            }

            if (m_MeshEditController.GpuSelectionPending)
            {
                if (primaryPressed) CancelGpuInteraction();
                else return inputDeps;
            }

            // Once no gesture is active, selection mode decides which picker and gizmo rules apply.
            // Surface and edge modes suppress vertex hover.
            if (m_MeshEditController.SelectionMode == MeshSelectionMode.SurfaceArea)
            {
                UpdateMeshSurfaceSelectionInput(editedObject, camera, mousePosition, primaryPressed, shiftPressed, controlPressed, altPressed);
                return inputDeps;
            }

            if (m_MeshEditController.SelectionMode == MeshSelectionMode.Edge)
            {
                UpdateMeshEdgeSelectionInput(editedObject, camera, mousePosition, primaryPressed, primaryReleased, shiftPressed, controlPressed, altPressed);
                return inputDeps;
            }

            if (m_MeshEditController.IsVertexSlideTargeting)
            {
                UpdateMeshVertexSlideTargetInput(editedObject, camera, mousePosition, primaryPressed, primaryReleased, shiftPressed, controlPressed, altPressed);
                return inputDeps;
            }

            if (m_MeshEditController.IsAlignmentTargeting)
            {
                UpdateMeshAlignmentInput(editedObject, camera, mousePosition, primaryPressed, primaryReleased, shiftPressed, controlPressed, altPressed);
                return inputDeps;
            }

            bool hasGizmoContext = TryGetGizmoContext(camera, out GizmoContext context);

            Ray mouseRay = default;

            if (hasGizmoContext)
            {
                mouseRay = camera.ScreenPointToRay(mousePosition);

                UpdateGizmoHover(mouseRay, context);

                m_MeshEditController.SetVertexHoverSuppressed(m_HoveredGizmoHandle != GizmoHandle.None);
            }
            else
            {
                ClearGizmoHover();
                m_MeshEditController.SetVertexHoverSuppressed(false);
            }

            if (m_MeshEditController.UsesGpuMarkers && (m_GpuPointer != null || !primaryPressed || m_HoveredGizmoHandle == GizmoHandle.None))
            {
                HandleGpuPointer(editedObject, camera, mousePosition, primaryPressed, primaryReleased, shiftPressed, controlPressed, altPressed, GpuPointerKind.Vertex);
                return inputDeps;
            }

            if (!primaryPressed)
            {
                return inputDeps;
            }

            if (hasGizmoContext && m_HoveredGizmoHandle != GizmoHandle.None)
            {
                BeginGizmoDrag(camera, mouseRay, context, m_HoveredGizmoHandle);

                return inputDeps;
            }

            if (!m_MeshEditController.HasHoveredVertex)
            {
                BoxSelectionMode mode = controlPressed ? BoxSelectionMode.Remove : shiftPressed ? BoxSelectionMode.Add : BoxSelectionMode.Replace;

                m_BoxSelection.Begin(mousePosition, mode);
                m_BoxSelectionNormalizedRect = Rect.zero;

                return inputDeps;
            }

            if (m_MeshEditController.ApplySelectionClick(editedObject, shiftPressed, controlPressed))
            {
                ClearGizmoHover();
            }

            return inputDeps;
        }
    }
}