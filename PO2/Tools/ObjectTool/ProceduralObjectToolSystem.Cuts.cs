using Game;
using Game.Input;
using Game.Simulation;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private InputBarrier m_CutZoomBarrier;

        public int MeshCutCount => m_LoopCut.IsActive ? m_LoopCut.CutCount : m_SurfaceCut.IsActive ? m_SurfaceCut.CutCount : 0;

        private void RefreshMeshCutInput()
        {
            bool active = (m_LoopCut.IsActive || m_SurfaceCut.IsActive) && !m_ManagementWorkspaceActive;
            if (active && m_CutZoomBarrier == null)
            {
                m_CutZoomBarrier = InputManager.instance.FindAction("Camera", "Zoom")?.CreateBarrier("PO2 cut count", InputManager.DeviceType.Mouse);
            }

            if (m_CutZoomBarrier != null) m_CutZoomBarrier.blocked = active;
        }

        private void DisposeMeshCutInput()
        {
            m_CutZoomBarrier?.Dispose();
            m_CutZoomBarrier = null;
        }

        private void UpdateMeshCutCount(ProceduralObject editedObject)
        {
            if (Mouse.current == null || InputManager.instance.hasInputFieldFocus) return;
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (scroll == 0f) return;

            // Wheel delta units differ between devices. One input frame adjusts
            // one cut, including on wheels which report a delta of 120.
            int delta = scroll > 0f ? 1 : -1;
            if (m_LoopCut.IsActive)
            {
                if (!m_LoopCut.HasCandidate) return;
                if (!IsLoopCutSourceCurrent(editedObject))
                {
                    CancelMeshLoopCut();
                    return;
                }

                if (!m_LoopCut.ChangeCutCount(delta)) return;
                if (!CutPreview.Fill(editedObject, m_LoopCut.PartIndex, m_LoopCut.StructuralEdgeId, m_LoopCut.Loop, m_LoopCut.CutCount, m_LoopCut.CurrentT, m_LoopCut.PreviewLines))
                {
                    CancelMeshLoopCut();
                }
            }
            else if (m_SurfaceCut.IsActive)
            {
                if (!m_SurfaceCut.HasCandidate) return;
                if (!IsSurfaceCutSourceCurrent(editedObject))
                {
                    CancelMeshSurfaceCut();
                    return;
                }

                if (!m_SurfaceCut.ChangeCutCount(delta)) return;
                if (!CutPreview.Fill(editedObject, m_SurfaceCut.PartIndex, m_SurfaceCut.StructuralEdgeId, m_SurfaceCut.Loop, m_SurfaceCut.CutCount, m_SurfaceCut.CurrentT, m_SurfaceCut.PreviewLines))
                {
                    CancelMeshSurfaceCut();
                }
            }
        }

        private readonly LoopCutState m_LoopCut = new LoopCutState();
        private readonly SurfaceCutState m_SurfaceCut = new SurfaceCutState();

        public bool IsMeshEditLoopCutTargeting =>
            m_LoopCut.IsTargeting;

        public bool IsMeshEditLoopCutPositioning =>
            m_LoopCut.IsPositioning;

        public bool IsMeshEditSurfaceCutTargeting =>
            m_SurfaceCut.IsTargeting;

        public bool IsMeshEditSurfaceCutPositioning =>
            m_SurfaceCut.IsPositioning;

        private bool IsMeshCutActive =>
            m_LoopCut.IsActive || m_SurfaceCut.IsActive;

        private bool IsMeshTopologyInteractionActive =>
            IsMeshCutActive || m_FaceOperation.IsActive;

        public bool TryGetMeshEditLoopCutPreview(out LoopCutPreviewLine[] previewLines, out int previewLineCount, out uint previewVersion)
        {
            if (m_FaceOperation.IsPositioning)
            {
                previewLines = m_FaceOperation.PreviewLines;
                previewLineCount = m_FaceOperation.PreviewLineCount;
                previewVersion = m_FaceOperation.PreviewVersion ^ 0x40000000u;

                return m_Mode == ObjectToolMode.MeshEdit && previewLineCount > 0;
            }

            if (m_SurfaceCut.IsActive)
            {
                previewLines = m_SurfaceCut.PreviewLines;
                previewLineCount = m_SurfaceCut.PreviewLineCount;
                // Both tools share one renderer-facing version field.
                // Reserve the high bit so switching tools cannot accidentally reuse a version.
                previewVersion = m_SurfaceCut.PreviewVersion ^ 0x80000000u;

                return m_Mode == ObjectToolMode.MeshEdit && previewLineCount > 0;
            }

            previewLines = m_LoopCut.PreviewLines;
            previewLineCount = m_LoopCut.PreviewLineCount;
            previewVersion = m_LoopCut.PreviewVersion;

            return m_Mode == ObjectToolMode.MeshEdit && m_LoopCut.IsActive && previewLineCount > 0;
        }

        public void BeginMeshLoopCut()
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null || m_IsGizmoDragging || m_IsVertexSlideDragging || m_MeshEditController.IsAlignmentTargeting || m_MeshEditController.IsVertexSlideTargeting || IsMeshTopologyInteractionActive || editedObject.Parts.Count == 0)
            {
                return;
            }

            MeshCutScopeAvailability availability = GetMeshCutScopeAvailability(MeshCutKind.Loop);
            if (!availability.IsAvailable)
            {
                return;
            }

            CancelBoxSelection();
            ClearGizmoHover();
            m_MeshEditController.ClearHover();
            m_MeshEditController.SetVertexHoverSuppressed(true);
            m_LoopCut.Begin(editedObject.Id);
            RefreshMeshCutInput();
        }

        public void BeginMeshSurfaceCut()
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null || m_IsGizmoDragging || m_IsVertexSlideDragging || m_MeshEditController.IsAlignmentTargeting || m_MeshEditController.IsVertexSlideTargeting || IsMeshTopologyInteractionActive || editedObject.Parts.Count == 0)
            {
                return;
            }

            MeshCutScopeAvailability availability = GetMeshCutScopeAvailability(MeshCutKind.Surface);
            if (!availability.IsAvailable)
            {
                return;
            }

            CancelBoxSelection();
            ClearGizmoHover();
            m_MeshEditController.ClearHover();
            m_MeshEditController.SetVertexHoverSuppressed(true);
            m_SurfaceCut.Begin(editedObject.Id);
            RefreshMeshCutInput();
        }

        public void CancelMeshLoopCut()
        {
            if (!m_LoopCut.IsActive)
            {
                return;
            }

            m_LoopCut.Reset();
            RefreshMeshCutInput();
            ClearGizmoHover();

            if (m_MeshEditController != null)
            {
                m_MeshEditController.SetVertexHoverSuppressed(false);
                m_MeshEditController.ClearHover();
            }
        }

        public void CancelMeshSurfaceCut()
        {
            if (!m_SurfaceCut.IsActive)
            {
                return;
            }

            m_SurfaceCut.Reset();
            RefreshMeshCutInput();
            ClearGizmoHover();

            if (m_MeshEditController != null)
            {
                m_MeshEditController.SetVertexHoverSuppressed(false);
                m_MeshEditController.ClearHover();
            }
        }

        private bool TryLockLoopCutCandidate(ProceduralObject editedObject, Camera camera, Vector2 mousePosition)
        {
            if (!m_LoopCut.IsTargeting || !m_LoopCut.HasCandidate || editedObject.Id != m_LoopCut.ObjectId || !TryGetLoopCutTargetWorldEdge(editedObject, m_LoopCut.PartIndex, m_LoopCut.StructuralEdgeId, out Vector3 worldStart, out Vector3 worldEnd) || !TryGetLoopCutAxisParameter(camera, mousePosition, worldStart, worldEnd, out float startAxisParameter))
            {
                return false;
            }

            // Locking ends hover-based targeting.
            // From here on, pointer movement controls a parameter along this captured world-space edge.
            return m_LoopCut.LockCandidate(worldStart, worldEnd, startAxisParameter);
        }

        private bool TryLockSurfaceCutCandidate(ProceduralObject editedObject, Camera camera, Vector2 mousePosition)
        {
            if (!m_SurfaceCut.IsTargeting || !m_SurfaceCut.HasCandidate || editedObject.Id != m_SurfaceCut.ObjectId || !TryGetLoopCutTargetWorldEdge(editedObject, m_SurfaceCut.PartIndex, m_SurfaceCut.StructuralEdgeId, out Vector3 worldStart, out Vector3 worldEnd) || !TryGetLoopCutAxisParameter(camera, mousePosition, worldStart, worldEnd, out float startAxisParameter))
            {
                return false;
            }

            // Surface Cut shares the positioning gesture with Loop Cut, but its
            // topology mutation stops at the selected strip rather than looping.
            return m_SurfaceCut.LockCandidate(worldStart, worldEnd, startAxisParameter);
        }

        private void UpdateMeshLoopCutInput(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool primaryPressed, bool shiftPressed, bool controlPressed)
        {
            m_MeshEditController.UpdateFrame(editedObject, camera, mousePosition, updateHover: false);

            m_MeshEditController.SetVertexHoverSuppressed(true);
            ClearGizmoHover();

            if (m_LoopCut.IsTargeting)
            {
                UpdateLoopCutTargeting(editedObject, camera, mousePosition);
                UpdateMeshCutCount(editedObject);

                if (primaryPressed && TryLockLoopCutCandidate(editedObject, camera, mousePosition))
                {
                }
            }
            else if (m_LoopCut.IsPositioning)
            {
                UpdateMeshCutCount(editedObject);
                UpdateLoopCutPositioning(editedObject, camera, mousePosition, shiftPressed, controlPressed);

                if (primaryPressed && m_LoopCut.IsPositioning)
                {
                    CommitLoopCut(editedObject);
                }
            }
        }

        private void UpdateMeshSurfaceCutInput(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool primaryPressed, bool shiftPressed, bool controlPressed)
        {
            m_MeshEditController.UpdateFrame(editedObject, camera, mousePosition, updateHover: false);

            m_MeshEditController.SetVertexHoverSuppressed(true);
            ClearGizmoHover();

            if (m_SurfaceCut.IsTargeting)
            {
                UpdateSurfaceCutTargeting(editedObject, camera, mousePosition);
                UpdateMeshCutCount(editedObject);

                if (primaryPressed && TryLockSurfaceCutCandidate(editedObject, camera, mousePosition))
                {
                }
            }
            else if (m_SurfaceCut.IsPositioning)
            {
                UpdateMeshCutCount(editedObject);
                UpdateSurfaceCutPositioning(editedObject, camera, mousePosition, shiftPressed, controlPressed);

                if (primaryPressed && m_SurfaceCut.IsPositioning)
                {
                    CommitSurfaceCut(editedObject);
                }
            }
        }
    }
}