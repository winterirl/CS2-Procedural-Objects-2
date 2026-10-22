using Game;
using Game.Input;
using Game.Simulation;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Tools.Gizmos;
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private readonly VertexSlideState m_VertexSlide = new VertexSlideState();
        private GizmoDragModifierState m_VertexSlideModifierState;
        private bool m_IsVertexSlideDragging;

        public bool IsMeshEditVertexSlideTargeting =>
            m_MeshEditController != null && m_MeshEditController.IsVertexSlideTargeting;

        public bool IsMeshEditVertexSlideDragging =>
            m_IsVertexSlideDragging;

        public bool IsMeshEditVertexSlideTargetCandidate(VertexRef vertex)
        {
            return TryGetMeshEditObject(out ProceduralObject editedObject) && m_MeshEditController != null && m_MeshEditController.IsVertexSlideTargetCandidate(editedObject, vertex);
        }

        public bool TryGetMeshEditVertexSlideCandidateEdge(out Vector3 worldStart, out Vector3 worldEnd)
        {
            worldStart = Vector3.zero;
            worldEnd = Vector3.zero;

            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null || !m_MeshEditController.TryGetHoveredSlideTarget(editedObject, out VertexRef source, out VertexRef target))
            {
                return false;
            }

            MeshPart part = editedObject.Parts[source.PartIndex];

            worldStart = editedObject.Transform.TransformPoint(part.Topology.GetVertex(source.VertexId).GetPosition(part.Mesh));
            worldEnd = editedObject.Transform.TransformPoint(part.Topology.GetVertex(target.VertexId).GetPosition(part.Mesh));
            return true;
        }

        public void BeginMeshVertexSlide()
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null || m_MeshEditController.SelectionCount != 1 || m_IsVertexSlideDragging || IsMeshTopologyInteractionActive)
            {
                return;
            }

            CancelBoxSelection();
            ClearGizmoHover();

            if (m_MeshEditController.BeginVertexSlide(editedObject))
            {
            }
        }

        public void CancelMeshVertexSlide()
        {
            if (m_IsVertexSlideDragging)
            {
                CancelVertexSlideDrag(restore: true);
                return;
            }

            if (m_MeshEditController != null && m_MeshEditController.CancelVertexSlide())
            {
                ClearGizmoHover();
            }
        }

        private void BeginVertexSlideDrag(ProceduralObject editedObject, VertexRef target, Ray mouseRay, bool precision)
        {
            if (m_MeshEditController == null || !m_MeshEditController.CaptureVertexSlide(editedObject, target, m_VertexSlide))
            {
                return;
            }

            float startAxisParameter = 0f;

            GizmoMath.TryGetClosestRayLineParameter(mouseRay, m_VertexSlide.WorldStartPosition, m_VertexSlide.WorldAxis, out startAxisParameter);

            m_VertexSlide.StartAxisParameter = startAxisParameter;
            m_VertexSlideModifierState.Begin(precision, false);
            m_IsVertexSlideDragging = true;
        }

        private void UpdateVertexSlideDrag(Ray mouseRay, bool precision)
        {
            if (!m_IsVertexSlideDragging || !TryGetMeshEditObject(out ProceduralObject editedObject) || editedObject.Id != m_VertexSlide.ObjectId || m_MeshEditController == null || m_VertexSlide.WorldEdgeLength <= 0.000001f)
            {
                CancelVertexSlideDrag(restore: false);
                return;
            }

            if (!GizmoMath.TryGetClosestRayLineParameter(mouseRay, m_VertexSlide.WorldStartPosition, m_VertexSlide.WorldAxis, out float currentAxisParameter))
            {
                return;
            }

            float rawDistance = currentAxisParameter - m_VertexSlide.StartAxisParameter;

            if (!m_VertexSlideModifierState.ResolveLinear(rawDistance, precision, false, 0f, 0f, out float resolvedDistance))
            {
                return;
            }

            float t = resolvedDistance / m_VertexSlide.WorldEdgeLength;

            m_MeshEditController.ApplyVertexSlide(editedObject, m_VertexSlide, t);
            if (m_VertexSlide.History?.PreviewFailed == true)
            {
                Mod.log.Warn("[PO Mesh Edit] Slide cancelled because its normal update was invalid.");
                CancelVertexSlideDrag(restore: true);
            }
        }

        private void CompleteVertexSlideDrag()
        {
            if (!m_IsVertexSlideDragging)
            {
                return;
            }

            if (m_VertexSlide.History != null && !m_VertexSlide.History.Complete())
            {
                Mod.log.Error("[PO History] Vertex slide was restored because it could not be recorded.");
            }

            m_IsVertexSlideDragging = false;
            m_VertexSlideModifierState.Reset();
            m_VertexSlide.Reset();

            if (m_MeshEditController != null)
            {
                m_MeshEditController.SetVertexHoverSuppressed(false);
            }
        }

        private void CancelVertexSlideDrag(bool restore)
        {
            if (!m_IsVertexSlideDragging)
            {
                return;
            }

            int objectId = m_VertexSlide.ObjectId;

            if (restore && m_MeshEditController != null && m_ProceduralObjectSystem.TryGetObject(objectId, out ProceduralObject editedObject))
            {
                m_MeshEditController.RestoreVertexSlide(editedObject, m_VertexSlide);
            }

            m_IsVertexSlideDragging = false;
            m_VertexSlideModifierState.Reset();
            m_VertexSlide.Reset();

            if (m_MeshEditController != null)
            {
                m_MeshEditController.SetVertexHoverSuppressed(false);
            }
        }

        private void UpdateMeshVertexSlideDragInput(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool primaryReleased, bool shiftPressed)
        {
            Ray activeMouseRay = camera.ScreenPointToRay(mousePosition);

            UpdateVertexSlideDrag(activeMouseRay, shiftPressed);

            m_MeshEditController.UpdateFrame(editedObject, camera, mousePosition, updateHover: false);

            m_MeshEditController.SetVertexHoverSuppressed(true);
            ClearGizmoHover();

            if (primaryReleased)
            {
                CompleteVertexSlideDrag();
            }
        }

        private void UpdateMeshVertexSlideTargetInput(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool primaryPressed, bool primaryReleased, bool shiftPressed, bool controlPressed, bool altPressed)
        {
            ClearGizmoHover();
            m_MeshEditController.SetVertexHoverSuppressed(false);

            if (m_MeshEditController.UsesGpuMarkers)
            {
                HandleGpuPointer(editedObject, camera, mousePosition, primaryPressed, primaryReleased, shiftPressed, controlPressed, altPressed, GpuPointerKind.Slide);
                return;
            }

            if (primaryPressed && m_MeshEditController.TryGetHoveredSlideTarget(editedObject, out VertexRef _, out VertexRef target))
            {
                Ray slideMouseRay = camera.ScreenPointToRay(mousePosition);
                BeginVertexSlideDrag(editedObject, target, slideMouseRay, shiftPressed);
            }
        }
    }
}