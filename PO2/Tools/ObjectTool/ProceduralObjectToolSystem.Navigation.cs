using Game;
using Game.Input;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Interface;
using PO2.Tools.Gizmos;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private ObjectToolMode m_RuntimeMode = ObjectToolMode.Select;
        private Workspace m_Workspace = Workspace.Select;
        private Workspace m_WorkspaceReturnTarget = Workspace.Select;
        private Workspace m_ManagementReturnWorkspace = Workspace.Select;
        private Workspace m_ManagementPriorReturnTarget = Workspace.Select;
        private bool m_SuppressWorkspaceSync;
        private bool m_ManagementWorkspaceActive;

        private ObjectToolMode m_Mode
        {
            get => m_RuntimeMode;
            set
            {
                if (m_RuntimeMode != value)
                {
                    if (m_RuntimeMode == ObjectToolMode.Lod)
                    {
                        m_ProceduralObjectSystem.EndLodPreview();
                    }

                    m_PendingObjectPick.Clear();
                    if (m_RuntimeMode == ObjectToolMode.Delete || m_RuntimeMode == ObjectToolMode.Copy) m_ProceduralObjectSystem.ClearHoveredObject();
                }

                if (m_RuntimeMode == ObjectToolMode.ConvertAsset && value != ObjectToolMode.ConvertAsset)
                {
                    ClearAssetConversionHover();
                }

                m_RuntimeMode = value;
                if (!m_SuppressWorkspaceSync)
                {
                    m_Workspace = ResolveWorkspace(value);
                }
            }
        }

        private bool m_ShowObjectMarkers = true;

        // Most of these properties are read by ProceduralObjectUiSystem or the render systems.
        // Exposing snapshots here keeps those consumers out of the mutable controller/gesture state.
        public ObjectToolMode Mode => m_Mode;

        internal bool CanReplayHistory =>
            !AssetConversionBusy && !m_ProceduralObjectSystem.ClipboardBusy && !DeformationCommitBusy && !TopologyEditBusy && !ArrayBusy && (ActiveInteraction == NestedInteraction.None || ActiveInteraction == NestedInteraction.PartArray);

        public Workspace Workspace => m_Workspace;

        public Workspace WorkspaceReturnTarget =>
            m_ManagementWorkspaceActive ? m_ManagementReturnWorkspace : (m_Workspace == Workspace.Create || m_Workspace == Workspace.Convert) ? m_WorkspaceReturnTarget : m_Workspace;

        public NestedInteraction ActiveInteraction =>
            ResolveActiveInteraction();

        public bool ShowObjectMarkers => m_ShowObjectMarkers && m_Mode != ObjectToolMode.Lod;

        private bool IsObjectEditorMode =>
            m_Mode == ObjectToolMode.MeshEdit || m_Mode == ObjectToolMode.Texturing;

        public void EnterMaterialLibraryWorkspace() => EnterManagementWorkspace(Workspace.MaterialLibrary);

        public void EnterSaveManagerWorkspace() => EnterManagementWorkspace(Workspace.SaveManager);

        private void EnterManagementWorkspace(Workspace workspace)
        {
            Workspace origin = m_Workspace == Workspace.Convert ? m_WorkspaceReturnTarget : m_Workspace;
            CancelNestedInteractionForWorkspaceChange(preserveWorkspace: true);
            m_ManagementReturnWorkspace = origin;
            m_ManagementPriorReturnTarget = m_WorkspaceReturnTarget;
            m_Workspace = workspace;
            m_ManagementWorkspaceActive = true;
        }

        public void ExitMaterialLibraryWorkspace() => ExitManagementWorkspace();

        public void ExitSaveManagerWorkspace()
        {
            if (m_Workspace == Workspace.SaveManager) ExitManagementWorkspace();
            else if (m_Workspace != Workspace.MaterialLibrary) m_ManagementWorkspaceActive = false;
        }

        private void ExitManagementWorkspace()
        {
            m_ManagementWorkspaceActive = false;
            bool returnTargetValid = IsWorkspaceValid(m_ManagementReturnWorkspace);

            if (!returnTargetValid && IsObjectEditorMode)
            {
                LeaveMeshEdit(commitActiveDrag: false);
            }

            m_Workspace = returnTargetValid ? m_ManagementReturnWorkspace : ResolveSelectionWorkspace();
            m_WorkspaceReturnTarget = m_ManagementPriorReturnTarget;
        }

        private void CancelNestedInteractionForWorkspaceChange(bool preserveWorkspace)
        {
            m_ProceduralObjectSystem.EndLodPreview();
            m_ProceduralObjectSystem.CancelClipboardOperation();
            m_PendingObjectPick.Clear();
            m_ProceduralObjectSystem.ClearHoveredObject();
            CancelArray();
            ResetMeshTranslationPreparation();
            CancelGpuInteraction();
            CancelAxisPicking();
            CancelObjectListCameraPan();

            if (m_Mode == ObjectToolMode.ConvertAsset)
            {
                AbandonPendingAssetConversion();
            }

            if (m_IsGizmoDragging)
            {
                CancelGizmoDrag(restoreTransform: true);
            }

            CancelBoxSelection();

            if (m_IsVertexSlideDragging)
            {
                CancelVertexSlideDrag(restore: true);
            }

            m_LoopCut.Reset();
            m_SurfaceCut.Reset();
            RefreshMeshCutInput();
            m_FaceOperation.Reset();

            if (m_MeshEditController != null)
            {
                m_MeshEditController.CancelVertexSlide();
                m_MeshEditController.CancelAlignment();
                m_MeshEditController.ClearHover();
                m_MeshEditController.SetVertexHoverSuppressed(false);
            }

            if (m_Mode == ObjectToolMode.CombineSource)
            {
                ClearCombineParent();
            }

            if (IsPrimitivePlacementMode)
            {
                ClearPrimitivePartPlacementParent();
            }

            if (m_Mode == ObjectToolMode.CombineSource || IsPrimitivePlacementMode || m_Mode == ObjectToolMode.ConvertAsset)
            {
                m_SuppressWorkspaceSync = preserveWorkspace;
                try
                {
                    m_Mode = m_ProceduralObjectSystem.SelectedObject != null ? ObjectToolMode.Transform : ObjectToolMode.Select;
                }
                finally
                {
                    m_SuppressWorkspaceSync = false;
                }
            }

            ClearGizmoHover();
        }

        private Workspace ResolveSelectionWorkspace()
        {
            return m_ProceduralObjectSystem.SelectedObject != null ? Workspace.Transform : Workspace.Select;
        }

        private bool IsWorkspaceValid(Workspace workspace)
        {
            bool hasSelection = m_ProceduralObjectSystem.SelectedObject != null;

            switch (workspace)
            {
                case Workspace.Transform:
                case Workspace.Lod:
                    return hasSelection;

                case Workspace.MeshEdit:
                    return hasSelection && TryGetMeshEditObject(out ProceduralObject _);

                case Workspace.Texturing:
                    return hasSelection && TryGetTexturingObject(out ProceduralObject _);

                case Workspace.ObjectList:
                    return true;

                default:
                    return true;
            }
        }

        public void NavigateToSelectWorkspace()
        {
            CancelNestedInteractionForWorkspaceChange(preserveWorkspace: true);

            if (IsObjectEditorMode)
            {
                LeaveMeshEdit(commitActiveDrag: false);
            }

            m_ProceduralObjectSystem.Deselect();
            m_Mode = ObjectToolMode.Select;
            m_WorkspaceReturnTarget = Workspace.Select;
        }

        public void NavigateToCreateWorkspace()
        {
            Workspace origin = m_Workspace == Workspace.Delete || m_Workspace == Workspace.Copy || m_Workspace == Workspace.Paste ? Workspace.Select : m_Workspace == Workspace.MeshEdit || m_Workspace == Workspace.Texturing || m_Workspace == Workspace.Lod ? Workspace.Transform : m_Workspace;

            CancelNestedInteractionForWorkspaceChange(preserveWorkspace: true);

            if (IsObjectEditorMode)
            {
                LeaveMeshEdit(commitActiveDrag: false);
            }

            m_Mode = m_ProceduralObjectSystem.SelectedObject != null ? ObjectToolMode.Transform : ObjectToolMode.Select;
            m_WorkspaceReturnTarget = IsWorkspaceValid(origin) ? origin : ResolveSelectionWorkspace();
            m_Workspace = Workspace.Create;
        }

        public void NavigateToConvertWorkspace()
        {
            BeginAssetConversion();
        }

        public void NavigateToObjectListWorkspace()
        {
            CancelNestedInteractionForWorkspaceChange(preserveWorkspace: true);

            if (IsObjectEditorMode)
            {
                LeaveMeshEdit(commitActiveDrag: false);
            }

            m_SuppressWorkspaceSync = true;
            try
            {
                m_Mode = m_ProceduralObjectSystem.SelectedObject != null ? ObjectToolMode.Transform : ObjectToolMode.Select;
            }
            finally
            {
                m_SuppressWorkspaceSync = false;
            }

            m_Workspace = Workspace.ObjectList;
            ClearGizmoHover();
        }

        public void SelectObjectFromList(int objectId)
        {
            bool browsingSelect = m_Workspace == Workspace.Select;
            if ((!browsingSelect && m_Workspace != Workspace.ObjectList) || !m_ProceduralObjectSystem.SelectObject(objectId))
            {
                return;
            }

            CancelNestedInteractionForWorkspaceChange(preserveWorkspace: true);

            m_SuppressWorkspaceSync = true;
            try
            {
                m_Mode = ObjectToolMode.Transform;
            }
            finally
            {
                m_SuppressWorkspaceSync = false;
            }

            m_Workspace = browsingSelect ? Workspace.Transform : Workspace.ObjectList;
            ClearGizmoHover();
            BeginObjectListCameraPan(objectId);
        }

        public void NavigateToTransformWorkspace()
        {
            if (m_ProceduralObjectSystem.SelectedObject == null)
            {
                NavigateToSelectWorkspace();
                return;
            }

            CancelNestedInteractionForWorkspaceChange(preserveWorkspace: true);

            if (IsObjectEditorMode)
            {
                LeaveMeshEdit(commitActiveDrag: false);
            }

            m_Mode = ObjectToolMode.Transform;
        }

        public void NavigateToMeshEditWorkspace()
        {
            CancelNestedInteractionForWorkspaceChange(preserveWorkspace: true);

            if (m_Mode == ObjectToolMode.Texturing)
            {
                LeaveMeshEdit(commitActiveDrag: false);
            }

            EnterMeshEdit();
        }

        public void NavigateToTexturingWorkspace()
        {
            CancelNestedInteractionForWorkspaceChange(preserveWorkspace: true);

            if (m_Mode == ObjectToolMode.MeshEdit)
            {
                LeaveMeshEdit(commitActiveDrag: false);
            }

            EnterTexturing();
        }

        private static Workspace ResolveWorkspace(ObjectToolMode mode)
        {
            switch (mode)
            {
                case ObjectToolMode.PlacePrimitive:
                case ObjectToolMode.PlacePrimitivePart:
                    return Workspace.Create;

                case ObjectToolMode.ConvertAsset:
                    return Workspace.Convert;

                case ObjectToolMode.Transform:
                case ObjectToolMode.CombineSource:
                    return Workspace.Transform;

                case ObjectToolMode.MeshEdit:
                    return Workspace.MeshEdit;

                case ObjectToolMode.Texturing:
                    return Workspace.Texturing;

                case ObjectToolMode.Lod:
                    return Workspace.Lod;

                case ObjectToolMode.Delete:
                    return Workspace.Delete;

                case ObjectToolMode.Copy:
                    return Workspace.Copy;

                case ObjectToolMode.Paste:
                    return Workspace.Paste;

                default:
                    return Workspace.Select;
            }
        }

        private NestedInteraction ResolveActiveInteraction()
        {
            if (ArrayActive) return NestedInteraction.PartArray;
            if (AxisPickingActive) return NestedInteraction.OrientationTarget;
            if (m_IsGizmoDragging)
            {
                return NestedInteraction.GizmoDrag;
            }

            if (m_BoxSelection.IsGestureActive)
            {
                return NestedInteraction.BoxSelection;
            }

            if (m_IsVertexSlideDragging)
            {
                return NestedInteraction.VertexSlideDrag;
            }

            if (m_LoopCut.IsTargeting)
            {
                return NestedInteraction.LoopCutTarget;
            }

            if (m_LoopCut.IsPositioning)
            {
                return NestedInteraction.LoopCutPosition;
            }

            if (m_SurfaceCut.IsTargeting)
            {
                return NestedInteraction.SurfaceCutTarget;
            }

            if (m_SurfaceCut.IsPositioning)
            {
                return NestedInteraction.SurfaceCutPosition;
            }

            if (m_FaceOperation.IsActive)
            {
                return NestedInteraction.FaceOperationSelection;
            }

            if (m_MeshEditController != null)
            {
                if (m_MeshEditController.IsAlignmentTargeting)
                {
                    return NestedInteraction.VertexAlignmentTarget;
                }

                if (m_MeshEditController.IsVertexSlideTargeting)
                {
                    return NestedInteraction.VertexSlideTarget;
                }
            }

            switch (m_Mode)
            {
                case ObjectToolMode.PlacePrimitive:
                    return NestedInteraction.PrimitivePlacement;

                case ObjectToolMode.PlacePrimitivePart:
                    return NestedInteraction.PrimitivePartPlacement;

                case ObjectToolMode.ConvertAsset:
                    return NestedInteraction.AssetConversionTarget;

                case ObjectToolMode.CombineSource:
                    return NestedInteraction.CombineSourceTarget;

                default:
                    return NestedInteraction.None;
            }
        }

        public void ToggleObjectMarkers()
        {
            m_ShowObjectMarkers = !m_ShowObjectMarkers;
        }

        public void BackOutOneLevel()
        {
            if (m_ProceduralObjectSystem.ClipboardBusy)
            {
                m_ProceduralObjectSystem.CancelClipboardOperation();
                return;
            }

            if (ArrayActive || ArrayBusy)
            {
                BackOutArray();
                return;
            }

            if (AxisPickingActive)
            {
                CancelAxisPicking();
                return;
            }

            // Escape/right-click unwinds the innermost active interaction first.
            // Repeated presses eventually deselect the object and stop at Select.
            if (ProceduralObjectUiSystem.TryBackOutSaveManager()) return;

            if (ProceduralObjectUiSystem.TryBackOutMaterialManagementView())
            {
                return;
            }

            if (ProceduralObjectUiSystem.TryBackOutRecoveryResult())
            {
                return;
            }

            if (m_Mode == ObjectToolMode.Lod)
            {
                BackOutLodWorkspace();
                return;
            }

            if (m_Mode == ObjectToolMode.Delete || IsClipboardMode)
            {
                NavigateToSelectWorkspace();
                return;
            }

            if (m_IsGizmoDragging)
            {
                CancelGizmoDrag(restoreTransform: true);
                return;
            }

            if (m_BoxSelection.IsGestureActive)
            {
                CancelBoxSelection();
                return;
            }

            if (DeformMode && m_CageSelection != 0)
            {
                ClearDeformationSelection();
                return;
            }

            if (m_Mode == ObjectToolMode.ConvertAsset)
            {
                Workspace returnTarget = m_WorkspaceReturnTarget;
                AbandonPendingAssetConversion();
                m_SuppressWorkspaceSync = true;
                try
                {
                    m_Mode = m_ProceduralObjectSystem.SelectedObject != null ? ObjectToolMode.Transform : ObjectToolMode.Select;
                }
                finally
                {
                    m_SuppressWorkspaceSync = false;
                }

                m_Workspace = IsWorkspaceValid(returnTarget) ? returnTarget : ResolveSelectionWorkspace();
                ClearGizmoHover();
                return;
            }

            if (m_Mode == ObjectToolMode.CombineSource)
            {
                ClearCombineParent();
                m_Mode = m_ProceduralObjectSystem.SelectedObject != null ? ObjectToolMode.Transform : ObjectToolMode.Select;
                ClearGizmoHover();
                return;
            }

            if (IsPrimitivePlacementMode)
            {
                ClearPrimitivePartPlacementParent();
                m_SuppressWorkspaceSync = true;
                try
                {
                    m_Mode = m_ProceduralObjectSystem.SelectedObject != null ? ObjectToolMode.Transform : ObjectToolMode.Select;
                }
                finally
                {
                    m_SuppressWorkspaceSync = false;
                }

                ClearGizmoHover();
                return;
            }

            if (m_Workspace == Workspace.Create)
            {
                if (m_WorkspaceReturnTarget == Workspace.Transform && m_ProceduralObjectSystem.SelectedObject != null)
                {
                    NavigateToTransformWorkspace();
                }
                else
                {
                    NavigateToSelectWorkspace();
                }

                return;
            }

            if (m_Mode == ObjectToolMode.MeshEdit)
            {
                if (m_FaceOperation.IsActive)
                {
                    if (m_FaceOperation.IsPositioning)
                    {
                        BackActiveMeshFaceOperation();
                    }
                    else
                    {
                        CancelMeshFaceOperation();
                    }

                    return;
                }

                if (m_LoopCut.IsActive)
                {
                    CancelMeshLoopCut();
                    return;
                }

                if (m_SurfaceCut.IsActive)
                {
                    CancelMeshSurfaceCut();
                    return;
                }

                if (m_IsVertexSlideDragging)
                {
                    CancelVertexSlideDrag(restore: true);
                    return;
                }

                if (m_MeshEditController != null && m_MeshEditController.CancelVertexSlide())
                {
                    ClearGizmoHover();
                    return;
                }

                if (m_MeshEditController != null && m_MeshEditController.CancelAlignment())
                {
                    ClearGizmoHover();
                    return;
                }

                if (m_MeshEditController != null && m_MeshEditController.ClearSelection())
                {
                    ClearGizmoHover();
                    return;
                }

                ExitMeshEdit();
                return;
            }

            if (m_Mode == ObjectToolMode.Texturing)
            {
                if (m_MeshEditController != null && m_MeshEditController.ClearSelection())
                {
                    ClearGizmoHover();
                    return;
                }

                ExitTexturing();
                return;
            }

            if (m_Workspace == Workspace.ObjectList)
            {
                if (m_ProceduralObjectSystem.SelectedObject != null)
                {
                    NavigateToTransformWorkspace();
                }
                else
                {
                    NavigateToSelectWorkspace();
                }

                return;
            }

            if (m_ProceduralObjectSystem.SelectedObjectId.HasValue)
            {
                m_ProceduralObjectSystem.Deselect();
                ClearGizmoHover();
                m_Mode = ObjectToolMode.Select;
                return;
            }

            // Back stops here. Closing PO2 is an explicit action.
        }

        public void CloseTool()
        {
            m_ProceduralObjectSystem.EndLodPreview();
            m_ProceduralObjectSystem.CancelClipboardOperation();
            // Closing is stronger than backing out: every in-progress edit is
            // restored and the vanilla default tool becomes active again.
            CancelObjectListCameraPan();
            AbandonPendingAssetConversion();
            ClearAssetConversionHover();
            m_AssetConversionStatus.Clear();
            CancelBoxSelection();
            m_LoopCut.Reset();
            m_SurfaceCut.Reset();
            RefreshMeshCutInput();
            m_FaceOperation.Reset();

            if (m_IsVertexSlideDragging)
            {
                CancelVertexSlideDrag(restore: true);
            }

            if (m_IsGizmoDragging)
            {
                CancelGizmoDrag(restoreTransform: true);
            }

            if (m_MeshEditController != null && m_MeshEditController.IsActive)
            {
                LeaveMeshEdit(commitActiveDrag: false);
            }

            m_ProceduralObjectSystem.Deselect();
            ClearPrimitivePartPlacementParent();
            ClearCombineParent();
            ClearGizmoHover();
            m_ActiveGizmoHandle = GizmoHandle.None;
            m_Mode = ObjectToolMode.Select;
            m_ManagementWorkspaceActive = false;
            m_WorkspaceReturnTarget = Workspace.Select;
            m_ManagementReturnWorkspace = Workspace.Select;
            m_ManagementPriorReturnTarget = Workspace.Select;

            if (m_ToolSystem.activeTool == this)
            {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
            }
        }

        private void CaptureConvertReturnTarget()
        {
            if (m_Workspace == Workspace.Convert)
            {
                return;
            }

            Workspace origin = m_Workspace == Workspace.Delete || m_Workspace == Workspace.Copy || m_Workspace == Workspace.Paste ? Workspace.Select : m_Workspace == Workspace.MeshEdit || m_Workspace == Workspace.Texturing || m_Workspace == Workspace.Lod ? Workspace.Transform : m_Workspace;

            m_WorkspaceReturnTarget = IsWorkspaceValid(origin) ? origin : ResolveSelectionWorkspace();
        }

        private void CaptureCreateReturnTarget()
        {
            if (m_Workspace == Workspace.Create)
            {
                return;
            }

            Workspace origin = m_Workspace == Workspace.Delete || m_Workspace == Workspace.Copy || m_Workspace == Workspace.Paste ? Workspace.Select : m_Workspace == Workspace.MeshEdit || m_Workspace == Workspace.Texturing || m_Workspace == Workspace.Lod ? Workspace.Transform : m_Workspace;

            m_WorkspaceReturnTarget = IsWorkspaceValid(origin) ? origin : ResolveSelectionWorkspace();
        }

        private JobHandle UpdateObjectMode(JobHandle inputDeps, bool primaryPressed, bool primaryReleased, bool shiftPressed, bool controlPressed)
        {
            Camera camera = Camera.main;

            if (camera == null)
            {
                return inputDeps;
            }

            Vector2 mousePosition = InputManager.instance.mousePosition;

            Ray mouseRay = camera.ScreenPointToRay(mousePosition);

            if (m_Mode == ObjectToolMode.Transform && m_IsGizmoDragging)
            {
                UpdateActiveGizmoDrag(mouseRay, mousePosition, shiftPressed, controlPressed);

                if (primaryReleased)
                {
                    CompleteGizmoDrag();
                }

                return inputDeps;
            }

            if (m_Mode == ObjectToolMode.Transform && m_DeformMode)
            {
                UpdateCagePointer(camera, mouseRay, mousePosition, primaryPressed, primaryReleased, shiftPressed, controlPressed);
                return inputDeps;
            }

            if (m_Mode == ObjectToolMode.Transform)
            {
                m_ProceduralObjectSystem.ClearHoveredObject();

                if (TryGetGizmoContext(camera, out GizmoContext context))
                {
                    UpdateGizmoHover(mouseRay, context);
                }
                else
                {
                    ClearGizmoHover();
                }

                if (primaryPressed && m_HoveredGizmoHandle != GizmoHandle.None)
                {
                    BeginGizmoDrag(camera, mouseRay, context, m_HoveredGizmoHandle);
                }

                return inputDeps;
            }

            // Select mode exists only to choose an object to manipulate.
            // It never exposes transform gizmos or gizmo hover state.
            ClearGizmoHover();
            UpdateObjectSelection(mouseRay, primaryPressed);

            return inputDeps;
        }
    }
}