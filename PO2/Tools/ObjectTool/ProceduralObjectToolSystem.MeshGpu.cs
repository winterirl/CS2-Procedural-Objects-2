using Game.Input;
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
        internal MeshEditController MeshController => m_MeshEditController;
        internal Camera MeshEditCamera => m_CameraUpdateSystem?.activeCamera ?? Camera.main;

        private enum GpuPointerKind
        {
            Vertex,
            Edge,
            Alignment,
            Slide,
            Axis
        }

        private MeshEditGpuRequest m_GpuPointer;
        private GpuPointerKind m_GpuPointerKind;
        private bool m_GpuShift, m_GpuControl, m_GpuAlt, m_GpuReleased, m_GpuAxisConfirm;
        private Vector2 m_GpuDown, m_GpuEnd;
        private Ray m_GpuDownRay;
        private VertexAlignmentAxis m_GpuAlignment;
        internal bool GpuHoverSuppressed => m_HoveredGizmoHandle != GizmoHandle.None || m_BoxSelection.IsGestureActive || m_IsGizmoDragging || m_IsVertexSlideDragging || IsMeshCutActive || m_FaceOperation.IsActive || InputManager.instance.hasInputFieldFocus || (m_ToolRaycastSystem.raycastFlags & Game.Common.RaycastFlags.UIDisable) != 0;
        private bool GpuInteractionPending => m_GpuPointer != null || m_MeshEditController.GpuSelectionPending;

        private void CancelGpuInteraction()
        {
            if (m_GpuPointer != null) m_GpuPointer.Invalid = true;
            m_GpuPointer = null;
            m_GpuAxisConfirm = false;
            m_MeshEditController?.CancelGpuBox();
            m_MeshEditController?.MeshGpu?.CancelAction();
        }

        private void PollGpuCancellation()
        {
            if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) || m_ProceduralObjectSystem.HistoryBusy) CancelGpuInteraction();
            var gpu = m_MeshEditController?.MeshGpu;
            if (m_GpuPointer != null && (gpu == null || gpu.Obsolete(m_GpuPointer))) CancelGpuInteraction();
        }

        private void HandleGpuPointer(ProceduralObject obj, Camera camera, Vector2 mouse, bool pressed, bool released, bool shift, bool control, bool alt, GpuPointerKind kind)
        {
            var gpu = m_MeshEditController.MeshGpu;
            if (!gpu.Ready)
            {
                CancelGpuInteraction();
                return;
            }

            if (pressed)
            {
                CancelGpuInteraction();
                m_GpuPointer = gpu.Request(mouse, default, false);
                m_GpuPointerKind = kind;
                m_GpuDown = m_GpuEnd = mouse;
                m_GpuDownRay = camera.ScreenPointToRay(mouse);
                m_GpuShift = shift;
                m_GpuControl = control;
                m_GpuAlt = alt;
                m_GpuReleased = released;
                m_GpuAlignment = m_MeshEditController.AlignmentAxis;
            }

            var request = m_GpuPointer;
            if (request == null) return;
            if (gpu.Obsolete(request) || kind != m_GpuPointerKind || m_GpuAlignment != m_MeshEditController.AlignmentAxis)
            {
                CancelGpuInteraction();
                return;
            }

            if (!m_GpuReleased) m_GpuEnd = mouse;
            m_GpuReleased |= released || Mouse.current == null || !Mouse.current.leftButton.isPressed;
            if (!request.Complete) return;
            if (!gpu.Valid(request))
            {
                CancelGpuInteraction();
                return;
            }

            int slot = (int)request.Data[1] - 1;
            m_GpuPointer = null;
            gpu.CancelAction();
            m_MeshEditController.SetGpuHoveredSlot(slot);
            if (kind == GpuPointerKind.Axis)
            {
                if (slot >= 0) m_MeshEditController.ReplaceEdgeSelection(obj, new[] { request.Geometry.Edges[slot] });
                else m_MeshEditController.ClearSelection();
                return;
            }

            if (kind == GpuPointerKind.Slide)
            {
                if (!m_GpuReleased && m_MeshEditController.TryGetHoveredSlideTarget(obj, out _, out var target))
                    BeginVertexSlideDrag(obj, target, m_GpuDownRay, m_GpuShift);
                return;
            }

            if (kind == GpuPointerKind.Alignment)
            {
                if (slot >= 0) m_MeshEditController.ApplyAlignmentToHoveredVertex(obj);
                return;
            }

            if (slot < 0)
            {
                var mode = m_GpuControl ? BoxSelectionMode.Remove : m_GpuShift ? BoxSelectionMode.Add : BoxSelectionMode.Replace;
                m_BoxSelection.Begin(m_GpuDown, mode);
                m_BoxSelection.Update(m_GpuEnd);
                UpdateBoxSelectionVisualRect(camera);
                if (m_GpuReleased)
                {
                    if (m_BoxSelection.IsActive) m_MeshEditController.ApplyBoxSelection(obj, m_BoxSelection.GetScreenRect(), mode);
                    else if (mode == BoxSelectionMode.Replace)
                    {
                        if (kind == GpuPointerKind.Edge) m_MeshEditController.ApplyEdgeSelectionClick(obj, false, false);
                        else m_MeshEditController.ApplySelectionClick(obj, false, false);
                    }

                    CancelBoxSelection();
                }

                return;
            }

            if (kind == GpuPointerKind.Vertex)
            {
                m_MeshEditController.ApplySelectionClick(obj, m_GpuShift, m_GpuControl);
                return;
            }

            var edge = request.Geometry.Edges[slot];
            if (m_GpuAlt)
            {
                var part = obj.Parts[edge.PartIndex];
                if (TopologyEditor.TryBuildStructuralEdgeLoop(part.StructuralTopology, edge.EdgeId, out int[] ids, out _))
                {
                    var refs = new StructuralEdgeRef[ids.Length];
                    for (int i = 0; i < ids.Length; i++) refs[i] = new StructuralEdgeRef(edge.PartIndex, ids[i]);
                    m_MeshEditController.ApplyEdgeSelectionSet(obj, refs, m_GpuShift, m_GpuControl);
                }
            }
            else m_MeshEditController.ApplyEdgeSelectionClick(obj, m_GpuShift, m_GpuControl);
        }
    }
}