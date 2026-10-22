using Game;
using Game.Input;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.Localization;
using PO2.Tools.Gizmos;
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        // MeshEditController owns selection and projected marker data.
        // The tool keeps the pointer gesture because it has to coordinate raw input first.
        private MeshEditController m_MeshEditController;

        // Keep session preferences here because the controller resets between edit targets.
        private MeshSelectionMode m_MeshEditSelectionModePreference = MeshSelectionMode.Vertex;
        private MeshEditVisibilityMode m_MeshEditVisibilityPreference = MeshEditVisibilityMode.XRay;
        private MeshEditVertexScope m_MeshEditVertexScopePreference = MeshEditVertexScope.CurrentPart;

        private GizmoMode m_PreMeshEditGizmoMode = GizmoMode.Translate;

        public int MeshEditSelectedVertexCount =>
            m_MeshEditController != null ? m_MeshEditController.SelectionCount : 0;

        public int MeshEditEffectiveEdgeVertexCount =>
            m_MeshEditController != null ? m_MeshEditController.EffectiveEdgeVertexCount : 0;

        public int MeshEditSelectionVersion =>
            m_MeshEditController != null ? m_MeshEditController.SelectionVersion : 0;

        public int MeshEditSelectedSurfaceCount =>
            GetSelectedSurfaceUnitCount();

        public MeshSelectionMode MeshEditSelectionMode =>
            m_MeshEditController != null && m_MeshEditController.IsActive ? m_MeshEditController.SelectionMode : m_MeshEditSelectionModePreference;

        public MeshEditVisibilityMode MeshEditVisibility =>
            m_MeshEditController != null && m_MeshEditController.IsActive ? m_MeshEditController.VisibilityMode : m_MeshEditVisibilityPreference;

        public MeshEditVertexScope MeshEditVertexScopeMode =>
            m_MeshEditController != null && m_MeshEditController.IsActive ? m_MeshEditController.VertexScope : m_MeshEditVertexScopePreference;

        public bool TryGetMeshEditObject(out ProceduralObject proceduralObject)
        {
            if (m_Mode != ObjectToolMode.MeshEdit)
            {
                proceduralObject = null;
                return false;
            }

            return TryGetActiveEditorObject(out proceduralObject);
        }

        public bool TryGetTexturingObject(out ProceduralObject proceduralObject)
        {
            if (m_Mode != ObjectToolMode.Texturing)
            {
                proceduralObject = null;
                return false;
            }

            return TryGetActiveEditorObject(out proceduralObject);
        }

        public bool TryGetActiveEditorObject(out ProceduralObject proceduralObject)
        {
            proceduralObject = null;

            if (!IsObjectEditorMode || m_MeshEditController == null || !m_MeshEditController.IsActive || !m_MeshEditController.ObjectId.HasValue)
            {
                return false;
            }

            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || selected.Id != m_MeshEditController.ObjectId.Value)
            {
                return false;
            }

            proceduralObject = selected;
            return true;
        }

        public bool TryGetMeshEditFrameData(out VertexFrameData[] frameData, out int frameDataCount, out uint projectionVersion, out uint visualVersion)
        {
            frameData = null;
            frameDataCount = 0;
            projectionVersion = 0;
            visualVersion = 0;

            if ((!IsObjectEditorMode && !AxisPickingActive) || m_MeshEditController == null || !m_MeshEditController.IsActive)
            {
                return false;
            }

            // These arrays are owned and reused by the controller.
            // Consumers use the version numbers to decide whether their GPU buffers need updates.
            frameData = m_MeshEditController.FrameData;
            frameDataCount = m_MeshEditController.FrameDataCount;
            projectionVersion = m_MeshEditController.ProjectionVersion;
            visualVersion = m_MeshEditController.VisualVersion;
            return true;
        }

        public bool TryGetMeshEditEdgeFrameData(out EdgeFrameData[] frameData, out int frameDataCount, out uint edgeWorldVersion)
        {
            frameData = null;
            frameDataCount = 0;
            edgeWorldVersion = 0;

            if ((!IsObjectEditorMode && !AxisPickingActive) || m_MeshEditController == null || !m_MeshEditController.IsActive || m_MeshEditController.SelectionMode != MeshSelectionMode.Edge)
            {
                return false;
            }

            frameData = m_MeshEditController.EdgeFrameData;
            frameDataCount = m_MeshEditController.EdgeFrameDataCount;
            edgeWorldVersion = m_MeshEditController.EdgeWorldVersion;
            return true;
        }

        public StructuralEdgeRef[] GetMeshEditEdgeSelectionSnapshot()
        {
            if ((!IsObjectEditorMode && !AxisPickingActive) || m_MeshEditController == null || !m_MeshEditController.IsActive || m_MeshEditController.SelectionMode != MeshSelectionMode.Edge)
            {
                return System.Array.Empty<StructuralEdgeRef>();
            }

            return m_MeshEditController.GetEdgeSelectionSnapshot();
        }

        public bool TryGetMeshEditHoveredEdge(out StructuralEdgeRef edge)
        {
            edge = default;

            return (IsObjectEditorMode || AxisPickingActive) && m_MeshEditController != null && m_MeshEditController.IsActive && m_MeshEditController.TryGetHoveredEdge(out edge);
        }

        public bool IsMeshEditVertexSelected(VertexRef vertex)
        {
            return m_MeshEditController != null && m_MeshEditController.IsSelected(vertex);
        }

        public bool IsMeshEditVertexHovered(VertexRef vertex)
        {
            return m_MeshEditController != null && m_MeshEditController.IsHovered(vertex);
        }

        public bool IsMeshEditStructuralEdgeSelected(StructuralEdgeRef edge)
        {
            return m_MeshEditController != null && m_MeshEditController.IsEdgeSelected(edge);
        }

        public bool IsMeshEditStructuralEdgeHovered(StructuralEdgeRef edge)
        {
            return m_MeshEditController != null && m_MeshEditController.IsEdgeHovered(edge);
        }

        public void SetMeshVertexSelectionMode()
        {
            SetMeshSelectionMode(MeshSelectionMode.Vertex);
        }

        public void SetMeshEdgeSelectionMode()
        {
            SetMeshSelectionMode(MeshSelectionMode.Edge);
        }

        public void SetMeshVertexScopeCurrentPart()
        {
            SetMeshVertexScope(MeshEditVertexScope.CurrentPart);
        }

        public void SetMeshVertexScopeAllParts()
        {
            SetMeshVertexScope(MeshEditVertexScope.AllParts);
        }

        private void SetMeshVertexScope(MeshEditVertexScope scope)
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null || IsMeshTopologyInteractionActive || m_IsVertexSlideDragging || m_IsGizmoDragging)
            {
                return;
            }

            bool changed = m_MeshEditController.SetVertexScope(editedObject, scope, m_ProceduralObjectSystem.SelectedPartIndex);

            if (changed || m_MeshEditController.VertexScope == scope)
            {
                m_MeshEditVertexScopePreference = scope;
            }

            if (changed)
            {
                CancelBoxSelection();
                ClearGizmoHover();
            }
        }

        public void SetMeshVisibilityXRay()
        {
            SetMeshVisibilityMode(MeshEditVisibilityMode.XRay);
        }

        public void SetMeshVisibilitySolid()
        {
            SetMeshVisibilityMode(MeshEditVisibilityMode.Solid);
        }

        private void SetMeshVisibilityMode(MeshEditVisibilityMode mode)
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null || IsMeshTopologyInteractionActive || m_IsVertexSlideDragging || m_IsGizmoDragging)
            {
                return;
            }

            bool changed = m_MeshEditController.SetVisibilityMode(editedObject, mode);

            if (changed || m_MeshEditController.VisibilityMode == mode)
            {
                m_MeshEditVisibilityPreference = mode;
            }

            if (changed)
            {
                CancelBoxSelection();
                ClearGizmoHover();
            }
        }

        private void SetMeshSelectionMode(MeshSelectionMode mode)
        {
            if (mode == MeshSelectionMode.SurfaceArea || !TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null || IsMeshTopologyInteractionActive || m_IsVertexSlideDragging || m_IsGizmoDragging)
            {
                return;
            }

            bool changed = m_MeshEditController.SetSelectionMode(editedObject, mode);

            if (changed || m_MeshEditController.SelectionMode == mode)
            {
                m_MeshEditSelectionModePreference = mode;
            }

            if (changed)
            {
                CancelBoxSelection();
                ClearGizmoHover();
            }
        }

        private void RestoreMeshEditSessionModes(ProceduralObject editedObject)
        {
            if (editedObject == null || m_MeshEditController == null || !m_MeshEditController.IsActive)
            {
                return;
            }

            if (m_MeshEditController.SelectionMode != m_MeshEditSelectionModePreference)
            {
                m_MeshEditController.SetSelectionMode(editedObject, m_MeshEditSelectionModePreference);
            }

            if (m_MeshEditController.VisibilityMode != m_MeshEditVisibilityPreference)
            {
                m_MeshEditController.SetVisibilityMode(editedObject, m_MeshEditVisibilityPreference);
            }

            if (m_MeshEditController.VertexScope != m_MeshEditVertexScopePreference)
            {
                m_MeshEditController.SetVertexScope(editedObject, m_MeshEditVertexScopePreference, m_ProceduralObjectSystem.SelectedPartIndex);
            }
        }

        public void SelectAllMeshElements()
        {
            if (IsMeshTopologyInteractionActive || !TryGetActiveEditorObject(out ProceduralObject editedObject))
            {
                return;
            }

            bool changed = m_MeshEditController.SelectionMode == MeshSelectionMode.SurfaceArea ? m_MeshEditController.SelectAllSurfaces(editedObject, m_ProceduralObjectSystem.SelectedPartIndex) : m_MeshEditController.SelectAll(editedObject);

            if (changed)
            {
                ClearGizmoHover();
            }
        }

        public void InvertMeshSelection()
        {
            if (IsMeshTopologyInteractionActive || !TryGetActiveEditorObject(out ProceduralObject editedObject))
            {
                return;
            }

            bool changed = m_MeshEditController.SelectionMode == MeshSelectionMode.SurfaceArea ? m_MeshEditController.InvertSurfaceSelection(editedObject, m_ProceduralObjectSystem.SelectedPartIndex) : m_MeshEditController.InvertSelection(editedObject);

            if (changed)
            {
                ClearGizmoHover();
            }
        }

        public void OnActiveMeshPartChanged()
        {
            if (ArrayActive) CancelArray();
            m_MirrorStatus = LocalizedText.Empty;
            CancelGizmoDrag(restoreTransform: true);
            CancelMeshFaceOperation();
            ClearGizmoHover();

            if (!TryGetActiveEditorObject(out ProceduralObject editedObject) || m_MeshEditController == null)
            {
                return;
            }

            CancelBoxSelection();
            m_MeshEditController.OnActivePartChanged(editedObject, m_ProceduralObjectSystem.SelectedPartIndex);
        }

        public void SelectConnectedMeshElements()
        {
            if (!IsMeshTopologyInteractionActive && TryGetMeshEditObject(out ProceduralObject editedObject) && m_MeshEditController.SelectConnected(editedObject))
            {
                ClearGizmoHover();
            }
        }

        public void EnterMeshEdit()
        {
            if (m_Mode == ObjectToolMode.MeshEdit)
            {
                return;
            }

            if (m_Mode == ObjectToolMode.Texturing)
            {
                LeaveMeshEdit(commitActiveDrag: true);
            }

            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null)
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            if (!m_ProceduralObjectSystem.IsToolActive)
            {
                m_ProceduralObjectSystem.ActivateTool();
            }

            if (!m_ProceduralObjectSystem.IsToolActive)
            {
                Mod.log.Warn("[PO Mesh Edit] Cannot enter mesh edit because " + "the Procedural Objects tool is not active.");
                return;
            }

            CancelGizmoDrag(restoreTransform: true);
            CancelBoxSelection();
            ClearCombineParent();
            ClearGizmoHover();

            // Remember the object gizmo mode separately.
            // Mesh Edit can change it, but leaving should return the panel to the user's prior tool.
            m_PreMeshEditGizmoMode = m_GizmoMode;

            if (!m_MeshEditController.Enter(selected, m_ProceduralObjectSystem.SelectedPartIndex))
            {
                return;
            }

            RestoreMeshEditSessionModes(selected);
            m_Mode = ObjectToolMode.MeshEdit;
        }

        public void EnterTexturing()
        {
            if (m_Mode == ObjectToolMode.Texturing)
            {
                return;
            }

            if (m_Mode == ObjectToolMode.MeshEdit)
            {
                LeaveMeshEdit(commitActiveDrag: true);
            }

            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null)
            {
                return;
            }

            if (!m_ProceduralObjectSystem.IsToolActive)
            {
                m_ProceduralObjectSystem.ActivateTool();
            }

            if (!m_ProceduralObjectSystem.IsToolActive)
            {
                Mod.log.Warn("[PO Texturing] Cannot enter Texturing because " + "the Procedural Objects tool is not active.");
                return;
            }

            CancelGizmoDrag(restoreTransform: true);
            CancelBoxSelection();
            ClearCombineParent();
            ClearGizmoHover();
            m_LoopCut.Reset();
            m_SurfaceCut.Reset();
            RefreshMeshCutInput();
            m_FaceOperation.Reset();

            m_PreMeshEditGizmoMode = m_GizmoMode;

            if (!m_MeshEditController.Enter(selected, m_ProceduralObjectSystem.SelectedPartIndex))
            {
                return;
            }

            m_MeshEditController.SetSelectionMode(selected, MeshSelectionMode.SurfaceArea);
            m_Mode = ObjectToolMode.Texturing;
        }

        public void ExitMeshEdit()
        {
            LeaveMeshEdit(commitActiveDrag: true);
        }

        public void ExitTexturing()
        {
            LeaveMeshEdit(commitActiveDrag: true);
        }

        private void OnTexturingObjectLocked()
        {
            if (m_Mode != ObjectToolMode.Texturing || m_MeshEditController == null)
            {
                return;
            }

            m_MeshEditController.ClearSelection();
            m_MeshEditController.ClearHover();
            ClearGizmoHover();
        }

        public void PrepareSelectedObjectLock()
        {
            CancelAxisPicking();
            if (m_Mode == ObjectToolMode.MeshEdit)
            {
                LeaveMeshEdit(commitActiveDrag: false);
                return;
            }

            if (m_Mode == ObjectToolMode.Texturing)
            {
                OnTexturingObjectLocked();
                return;
            }

            if (m_Mode == ObjectToolMode.Transform && m_IsGizmoDragging)
            {
                CancelGizmoDrag(restoreTransform: true);
                ClearGizmoHover();
            }
        }

        public void PrepareRuntimeSelectionInvalidation()
        {
            CancelNestedInteractionForWorkspaceChange(preserveWorkspace: false);

            if (IsObjectEditorMode)
            {
                LeaveMeshEdit(commitActiveDrag: false);
            }

            ClearPrimitivePartPlacementParent();
            ClearCombineParent();
            ClearGizmoHover();
            m_Mode = ObjectToolMode.Select;
            m_WorkspaceReturnTarget = Workspace.Select;
            m_ManagementReturnWorkspace = Workspace.Select;
            m_ManagementPriorReturnTarget = Workspace.Select;
        }

        private void LeaveMeshEdit(bool commitActiveDrag)
        {
            ResetMeshTranslationPreparation();
            // Normal UI exit accepts the live drag.
            // Tool shutdown/cancellation restores its captured snapshot before thorwing controller state away.
            if (m_MeshEditController == null || !m_MeshEditController.IsActive)
            {
                if (m_IsVertexSlideDragging)
                {
                    if (commitActiveDrag)
                    {
                        CompleteVertexSlideDrag();
                    }
                    else
                    {
                        CancelVertexSlideDrag(restore: true);
                    }
                }

                if (m_IsGizmoDragging)
                {
                    if (commitActiveDrag) CompleteGizmoDrag();
                    else CancelGizmoDrag(restoreTransform: true);
                }

                ClearGizmoHover();

                if (IsObjectEditorMode)
                {
                    m_Mode = m_ProceduralObjectSystem.SelectedObject != null ? ObjectToolMode.Transform : ObjectToolMode.Select;
                }

                return;
            }

            if (m_IsVertexSlideDragging)
            {
                if (commitActiveDrag)
                {
                    CompleteVertexSlideDrag();
                }
                else
                {
                    CancelVertexSlideDrag(restore: true);
                }
            }

            if (m_IsGizmoDragging)
            {
                if (commitActiveDrag)
                {
                    CompleteGizmoDrag();
                }
                else
                {
                    CancelGizmoDrag(restoreTransform: true);
                }
            }

            CancelBoxSelection();
            m_LoopCut.Reset();
            m_SurfaceCut.Reset();
            RefreshMeshCutInput();
            m_FaceOperation.Reset();
            ClearGizmoHover();
            m_MeshEditController.Exit();
            m_GizmoMode = m_PreMeshEditGizmoMode;

            m_Mode = m_ProceduralObjectSystem.SelectedObject != null ? ObjectToolMode.Transform : ObjectToolMode.Select;
        }
    }
}