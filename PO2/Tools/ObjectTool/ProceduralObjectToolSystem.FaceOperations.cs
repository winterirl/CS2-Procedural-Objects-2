using Game;
using Game.Input;
using Game.Simulation;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private readonly FaceOperationState m_FaceOperation = new FaceOperationState();
        private bool m_ContinueKeyWasPressed;

        public bool CanBeginMeshFaceExtrude
        {
            get
            {
                if (MeshEditRequiresBake) return false;
                if (!TryGetMeshEditObject(out ProceduralObject editedObject) || editedObject.IsLocked || m_MeshEditController == null || m_IsGizmoDragging || m_IsVertexSlideDragging || m_BoxSelection.IsGestureActive || m_MeshEditController.IsAlignmentTargeting || m_MeshEditController.IsVertexSlideTargeting || IsMeshTopologyInteractionActive)
                {
                    return false;
                }

                int partIndex = m_ProceduralObjectSystem.SelectedPartIndex;
                if (partIndex < 0 || partIndex >= editedObject.Parts.Count)
                {
                    return false;
                }

                return editedObject.CanEditGeometry(partIndex, out _) && editedObject.Parts[partIndex].TopologyEditCapabilities.CanEditStructuralFaces;
            }
        }

        public bool CanBeginMeshFaceInset =>
            CanBeginMeshFaceExtrude;

        public bool IsMeshEditFaceSelectionActive =>
            m_Mode == ObjectToolMode.MeshEdit && m_FaceOperation.IsActive;

        public bool IsMeshEditFaceExtrudeSelecting =>
            m_Mode == ObjectToolMode.MeshEdit && m_FaceOperation.Kind == FaceOperationKind.Extrude && m_FaceOperation.IsSelecting;

        public bool IsMeshEditFaceExtrudePositioning =>
            m_Mode == ObjectToolMode.MeshEdit && m_FaceOperation.Kind == FaceOperationKind.Extrude && m_FaceOperation.IsPositioning;

        public bool IsMeshEditFaceInsetSelecting =>
            m_Mode == ObjectToolMode.MeshEdit && m_FaceOperation.Kind == FaceOperationKind.Inset && m_FaceOperation.IsSelecting;

        public bool IsMeshEditFaceInsetPositioning =>
            m_Mode == ObjectToolMode.MeshEdit && m_FaceOperation.Kind == FaceOperationKind.Inset && m_FaceOperation.IsPositioning;

        public bool CanContinueMeshFaceExtrude =>
            CanContinueMeshFaceOperation(FaceOperationKind.Extrude);

        public bool CanContinueMeshFaceInset =>
            CanContinueMeshFaceOperation(FaceOperationKind.Inset);

        public bool CanCommitMeshFaceExtrude =>
            CanCommitMeshFaceOperation(FaceOperationKind.Extrude);

        public bool CanCommitMeshFaceInset =>
            CanCommitMeshFaceOperation(FaceOperationKind.Inset);

        public float MeshFaceExtrudeDistance =>
            IsMeshEditFaceExtrudePositioning ? m_FaceOperation.ExtrusionDistance : 0f;

        public float MeshFaceInsetAmount =>
            IsMeshEditFaceInsetPositioning ? m_FaceOperation.InsetAmount : 0f;

        private bool CanContinueMeshFaceOperation(FaceOperationKind kind)
        {
            if (m_FaceOperation.Kind != kind || !TryGetMeshEditObject(out ProceduralObject editedObject) || !m_FaceOperation.IsSourceCurrent(editedObject, m_ProceduralObjectSystem.SelectedPartIndex))
            {
                return false;
            }

            return m_FaceOperation.CanContinue(editedObject);
        }

        private bool CanCommitMeshFaceOperation(FaceOperationKind kind)
        {
            if (m_Mode != ObjectToolMode.MeshEdit || m_FaceOperation.Kind != kind || !m_FaceOperation.IsPositioning || !TryGetMeshEditObject(out ProceduralObject editedObject) || !m_FaceOperation.IsSourceCurrent(editedObject, m_ProceduralObjectSystem.SelectedPartIndex))
            {
                return false;
            }

            return m_FaceOperation.CanCommit;
        }

        public int MeshEditSelectedFaceCount =>
            IsMeshEditFaceSelectionActive ? m_FaceOperation.SelectedFaceCount : 0;

        internal uint MeshEditFaceSelectionVisualVersion =>
            m_FaceOperation.VisualVersion;

        internal bool TryGetMeshEditFaceOperationRenderState(out ProceduralObject proceduralObject, out int partIndex, out bool[] selectedFaces, out int hoveredFaceId, out uint visualVersion)
        {
            proceduralObject = null;
            partIndex = -1;
            selectedFaces = System.Array.Empty<bool>();
            hoveredFaceId = -1;
            visualVersion = m_FaceOperation.VisualVersion;

            if (!m_FaceOperation.IsSelecting || !TryGetMeshEditObject(out proceduralObject))
            {
                return false;
            }

            partIndex = m_FaceOperation.PartIndex;
            selectedFaces = m_FaceOperation.SelectedFaces;
            hoveredFaceId = m_FaceOperation.HoveredFaceId;
            return partIndex >= 0 && partIndex < proceduralObject.Parts.Count;
        }

        internal StructuralFaceRef[] GetMeshEditFaceSelectionSnapshot()
        {
            return IsMeshEditFaceSelectionActive ? m_FaceOperation.GetSelectionSnapshot() : System.Array.Empty<StructuralFaceRef>();
        }

        public void BeginMeshFaceExtrude()
        {
            BeginMeshFaceOperation(FaceOperationKind.Extrude);
        }

        public void BeginMeshFaceInset()
        {
            BeginMeshFaceOperation(FaceOperationKind.Inset);
        }

        private void BeginMeshFaceOperation(FaceOperationKind kind)
        {
            bool canBegin = kind == FaceOperationKind.Extrude ? CanBeginMeshFaceExtrude : kind == FaceOperationKind.Inset && CanBeginMeshFaceInset;
            if (!canBegin || !TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null)
            {
                return;
            }

            int partIndex = m_ProceduralObjectSystem.SelectedPartIndex;
            if (partIndex < 0 || partIndex >= editedObject.Parts.Count || !editedObject.Parts[partIndex].TopologyEditCapabilities.CanEditStructuralFaces)
            {
                return;
            }

            CancelBoxSelection();
            ClearGizmoHover();
            m_MeshEditController.ClearHover();
            m_MeshEditController.SetVertexHoverSuppressed(true);

            if (!m_FaceOperation.Begin(kind, editedObject, partIndex))
            {
                m_MeshEditController.SetVertexHoverSuppressed(false);
                Mod.log.Warn($"[PO Mesh Edit] Could not start {GetFaceOperationName(kind)} face selection because the active part does not have a complete structural-face map.");
                return;
            }
        }

        public void ContinueMeshFaceExtrude()
        {
            ContinueMeshFaceOperation(FaceOperationKind.Extrude);
        }

        public void ContinueMeshFaceInset()
        {
            ContinueMeshFaceOperation(FaceOperationKind.Inset);
        }

        private void ContinueActiveMeshFaceOperation()
        {
            ContinueMeshFaceOperation(m_FaceOperation.Kind);
        }

        private void ContinueMeshFaceOperation(FaceOperationKind kind)
        {
            if (m_FaceOperation.Kind != kind || !TryGetMeshEditObject(out ProceduralObject editedObject) || !m_FaceOperation.IsSourceCurrent(editedObject, m_ProceduralObjectSystem.SelectedPartIndex))
            {
                return;
            }

            bool primaryIsPressed = Mouse.current != null && Mouse.current.leftButton.isPressed;
            if (!m_FaceOperation.TryContinue(editedObject, primaryIsPressed))
            {
                return;
            }
        }

        public void BackMeshFaceExtrude()
        {
            BackMeshFaceOperation(FaceOperationKind.Extrude);
        }

        public void BackMeshFaceInset()
        {
            BackMeshFaceOperation(FaceOperationKind.Inset);
        }

        private void BackActiveMeshFaceOperation()
        {
            BackMeshFaceOperation(m_FaceOperation.Kind);
        }

        private void BackMeshFaceOperation(FaceOperationKind kind)
        {
            if (m_FaceOperation.Kind != kind)
            {
                return;
            }

            if (!m_FaceOperation.BackToSelection())
            {
                return;
            }
        }

        public void SetMeshFaceExtrudeDistance(float distance)
        {
            m_FaceOperation.SetExtrusionDistance(distance);
        }

        public void SetMeshFaceInsetAmount(float amount)
        {
            m_FaceOperation.SetInsetAmount(amount);
        }

        public void ConfirmMeshFaceExtrude()
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject))
            {
                return;
            }

            CommitMeshFaceExtrude(editedObject);
        }

        public void ConfirmMeshFaceInset()
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject))
            {
                return;
            }

            CommitMeshFaceInset(editedObject);
        }

        public void CancelMeshFaceOperation()
        {
            if (!m_FaceOperation.IsActive)
            {
                return;
            }

            m_FaceOperation.Reset();
            ClearGizmoHover();

            if (m_MeshEditController != null)
            {
                m_MeshEditController.SetVertexHoverSuppressed(false);
                m_MeshEditController.ClearHover();
            }
        }

        private static string GetFaceOperationName(FaceOperationKind kind)
        {
            return kind == FaceOperationKind.Inset ? "Inset" : "Extrude";
        }

        private void UpdateMeshFaceOperationInput(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool primaryPressed, bool shiftPressed, bool controlPressed, bool continuePressed)
        {
            int activePartIndex = m_ProceduralObjectSystem.SelectedPartIndex;
            if (!m_FaceOperation.IsSourceCurrent(editedObject, activePartIndex) || activePartIndex < 0 || activePartIndex >= editedObject.Parts.Count || !editedObject.Parts[activePartIndex].TopologyEditCapabilities.CanEditStructuralFaces)
            {
                Mod.log.Warn($"[PO Mesh Edit] {GetFaceOperationName(m_FaceOperation.Kind)} face selection was cancelled because its source part changed.");
                CancelMeshFaceOperation();
                return;
            }

            m_MeshEditController.SetVertexHoverSuppressed(true);
            m_MeshEditController.ClearHover();
            ClearGizmoHover();

            Ray faceMouseRay = camera.ScreenPointToRay(mousePosition);
            if (m_FaceOperation.IsSelecting)
            {
                m_FaceOperation.UpdateHover(editedObject, faceMouseRay);

                if (continuePressed && m_FaceOperation.CanContinue(editedObject))
                {
                    ContinueActiveMeshFaceOperation();
                    return;
                }

                if (primaryPressed)
                {
                    m_FaceOperation.ApplySelectionClick(shiftPressed, controlPressed);
                }
            }
            else if (m_FaceOperation.IsPositioning)
            {
                m_MeshEditController.UpdateFrame(editedObject, camera, mousePosition, updateHover: false);

                bool primaryIsPressed = Mouse.current != null && Mouse.current.leftButton.isPressed;
                bool releasedPrimaryLatch = m_FaceOperation.ReleasePrimaryLatchIfReady(primaryIsPressed);

                if (!releasedPrimaryLatch)
                {
                    m_FaceOperation.UpdatePosition(camera, faceMouseRay, shiftPressed, m_SnapSettings.IsIncrementSnappingActive(controlPressed), m_SnapSettings.MoveIncrement, m_SnapSettings.PrecisionMoveIncrement);
                }

                if (primaryPressed && m_FaceOperation.CanAcceptPrimaryCommit && m_FaceOperation.CanCommit)
                {
                    CommitActiveMeshFaceOperation(editedObject);
                }
            }
        }
    }
}