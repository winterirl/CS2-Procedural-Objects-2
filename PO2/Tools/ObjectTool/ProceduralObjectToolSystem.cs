using System.Diagnostics;
using Game;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Conversion;
using PO2.Core;
using PO2.Localization;
using PO2.Tools.Gizmos;
using PO2.Tools.MeshEdit;
using PO2.Tools.Snapping;

// This file is just abso-fucking-lutely insane.
// I shouldn't have let it become so big.

namespace PO2.Tools.ObjectTool
{
    // Routes UI commands and world input to selection, placement, editing, and gizmos.
    // Objects and mesh-edit systems apply the geometry changes.
    public partial class ProceduralObjectToolSystem : ToolBaseSystem
    {
        private ProceduralObjectSystem m_ProceduralObjectSystem;
        private TerrainSystem m_TerrainSystem;
        private SnapIndexSystem m_SnapIndexSystem;
        private CameraUpdateSystem m_CameraUpdateSystem;

        public override string toolID => "PO2.ProceduralObjects";

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ProceduralObjectSystem = World.GetOrCreateSystemManaged<ProceduralObjectSystem>();

            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();

            m_SnapIndexSystem = World.GetOrCreateSystemManaged<SnapIndexSystem>();

            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            m_AssetMeshExtractor = new AssetMeshExtractor(EntityManager, prefabSystem);

            m_MeshEditController = new MeshEditController { HistorySystem = m_ProceduralObjectSystem };

            InitializePrimitiveCreationParameters();
        }

        protected override void OnDestroy()
        {
            CancelArray();
            m_MeshSnapPreparation.Dispose();
            CancelAxisPicking();
            ResetTopologyEdit();
            CancelObjectListCameraPan();
            AbandonPendingAssetConversion();
            ClearAssetConversionHover();
            m_AssetConversionTask = null;
            m_AssetConversionWorkItem = null;
            ClearActiveDragState();
            ClearPrimitivePartPlacementParent();
            m_CombineParentId = -1;
            m_CombineParent = null;
            m_MeshEditController?.Dispose();
            m_SnapIndexSystem = null;
            m_CameraUpdateSystem = null;
            DisposeMeshCutInput();
            base.OnDestroy();
        }

        public override PrefabBase GetPrefab()
        {
            return null;
        }

        public override bool TrySetPrefab(PrefabBase prefab)
        {
            return false;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();

            if (ArrayActive)
            {
                // Match move-tool surface targets. Terrain hits would win over increments on every frame.
                m_ToolRaycastSystem.typeMask = TypeMask.StaticObjects | TypeMask.MovingObjects | TypeMask.Net;
                m_ToolRaycastSystem.netLayerMask = Game.Net.Layer.All;
                m_ToolRaycastSystem.raycastFlags |= RaycastFlags.Outside | RaycastFlags.SubBuildings;
                return;
            }

            // Conversion and active surface snapping target game entities.
            // PO picking and gizmo picking keep their own mesh/math paths.
            if (m_Mode == ObjectToolMode.ConvertAsset)
            {
                m_ToolRaycastSystem.typeMask = TypeMask.StaticObjects | TypeMask.MovingObjects;
                m_ToolRaycastSystem.raycastFlags |= RaycastFlags.Outside | RaycastFlags.SubBuildings;
                return;
            }

            if (IsTranslationSceneSnappingRequested())
            {
                m_ToolRaycastSystem.typeMask = TypeMask.StaticObjects | TypeMask.MovingObjects | TypeMask.Net;
                m_ToolRaycastSystem.netLayerMask = Game.Net.Layer.All;
                m_ToolRaycastSystem.raycastFlags |= RaycastFlags.Outside | RaycastFlags.SubBuildings;

                return;
            }

            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
            m_ToolRaycastSystem.raycastFlags |= RaycastFlags.Outside;
            m_ToolRaycastSystem.raycastFlags &= ~RaycastFlags.SubBuildings;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (ProcessHistoryInput() || ProcessClipboardInput())
            {
                m_PendingObjectPick.Clear();
                return inputDeps;
            }

            RefreshMeshCutInput();
            if (ArrayActive)
            {
                InitializeRaycast();
                var flags = m_ToolRaycastSystem.raycastFlags;
                if ((flags & RaycastFlags.DebugDisable) != 0)
                {
                    CancelArray();
                    return inputDeps;
                }

                UpdateArrayInput((flags & RaycastFlags.UIDisable) == 0);
                return inputDeps;
            }

            if (ArrayBusy) return inputDeps;
            if (PollMeshTranslationPreparation()) return inputDeps;
            PollGpuCancellation();
            if (TopologyEditBusy && ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)))
            {
                m_TopologyEdit.Cancel();
                m_ProceduralObjectSystem.CancelMirrorEdit();
            }

            if (ProcessPendingTopologyEdit())
            {
                m_PendingObjectPick.Clear();
                return inputDeps;
            }

            if (DeformationCommitBusy && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                ResetCageDrag();
                m_DeformationStatus = LocalizedText.For("PO2.Object.Deformation.Status.Cancelled");
                return inputDeps;
            }

            bool deformationFinished = ProcessPendingCageDeformation();
            if (DeformationCommitBusy || deformationFinished)
            {
                m_PendingObjectPick.Clear();
                return inputDeps;
            }

            m_PendingObjectPick.Validate((int)m_Mode, m_ProceduralObjectSystem.SelectedObjectId ?? -1, m_ProceduralObjectSystem.PickSceneVersion);
            ProcessPendingAssetConversion();
            SynchronizeCustomTransformOrientationTarget();
            InitializeRaycast();

            RaycastFlags raycastFlags = m_ToolRaycastSystem.raycastFlags;

            Keyboard keyboard = Keyboard.current;
            bool continueKeyIsPressed = keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed);
            bool continuePressed = keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || (continueKeyIsPressed && !m_ContinueKeyWasPressed));
            m_ContinueKeyWasPressed = continueKeyIsPressed;

            // DebugDisable means the game has suspended tool interaction entirely.
            // Unlike UIDisable, even projection caches should not be advanced.
            if ((raycastFlags & RaycastFlags.DebugDisable) != 0)
            {
                CancelGpuInteraction();
                m_PendingObjectPick.Clear();
                ClearAssetConversionHover();
                m_ProceduralObjectSystem.ClearHoveredObject();
                m_FaceOperation.ClearHover();

                if (!m_IsGizmoDragging)
                {
                    ClearGizmoHover();
                }

                if (IsObjectEditorMode && m_MeshEditController != null)
                {
                    m_MeshEditController.ClearHover();
                }

                return inputDeps;
            }

            if (AxisPickingActive)
            {
                UpdateAxisPicking((raycastFlags & RaycastFlags.UIDisable) == 0, continuePressed);
                return inputDeps;
            }

            UpdateObjectListCameraPan();

            // The object list owns selection through its buttons. World clicks
            // stay blocked so browsing the list cannot also select whatever is
            // behind the panel.
            if (m_Workspace == Workspace.ObjectList)
            {
                Mouse listMouse = Mouse.current;
                Keyboard listKeyboard = Keyboard.current;
                bool listCancelPressed = (listKeyboard != null && listKeyboard.escapeKey.wasPressedThisFrame) || (listMouse != null && listMouse.rightButton.wasPressedThisFrame);

                if (listCancelPressed)
                {
                    BackOutOneLevel();
                    return inputDeps;
                }

                m_ProceduralObjectSystem.ClearHoveredObject();
                m_FaceOperation.ClearHover();
                ClearGizmoHover();
                return inputDeps;
            }

            // Management panels preserve the underlying editor for return navigation.
            // Their clicks must not reach world editing.
            if (m_ManagementWorkspaceActive)
            {
                Mouse materialMouse = Mouse.current;
                Keyboard materialKeyboard = Keyboard.current;
                bool materialCancelPressed = (materialKeyboard != null && materialKeyboard.escapeKey.wasPressedThisFrame) || (materialMouse != null && materialMouse.rightButton.wasPressedThisFrame);

                if (materialCancelPressed)
                {
                    BackOutOneLevel();
                    return inputDeps;
                }

                m_ProceduralObjectSystem.ClearHoveredObject();
                ClearGizmoHover();

                if (m_MeshEditController != null && TryGetActiveEditorObject(out ProceduralObject editedObject))
                {
                    Camera camera = MeshEditCamera;
                    if (camera != null)
                    {
                        m_MeshEditController.SetVertexHoverSuppressed(true);
                        m_MeshEditController.UpdateFrame(editedObject, camera, InputManager.instance.mousePosition, updateHover: false);
                    }

                    m_MeshEditController.ClearHover();
                }

                return inputDeps;
            }

            if (m_Mode == ObjectToolMode.Lod)
            {
                UpdateLodWorkspace();
                return inputDeps;
            }

            if ((m_Mode == ObjectToolMode.Delete || IsClipboardMode) && !InputManager.instance.hasInputFieldFocus && ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)))
            {
                BackOutOneLevel();
                return inputDeps;
            }

            if ((raycastFlags & RaycastFlags.UIDisable) != 0)
            {
                if (m_GpuPointer != null) CancelGpuInteraction();
                m_PendingObjectPick.Clear();
                ClearAssetConversionHover();

                // Enter advances the modeling step, so it should not depend on
                // where the mouse happens to be.
                // Do not steal it from a text field if the user is typing in the UI though.
                if (continuePressed && !InputManager.instance.hasInputFieldFocus && CanContinueMeshFaceOperation(m_FaceOperation.Kind))
                {
                    ContinueActiveMeshFaceOperation();
                    return inputDeps;
                }

                m_ProceduralObjectSystem.ClearHoveredObject();
                m_FaceOperation.ClearHover();

                if (!m_IsGizmoDragging)
                {
                    ClearGizmoHover();
                }

                if (m_BoxSelection.IsGestureActive && (Mouse.current == null || !Mouse.current.leftButton.isPressed))
                {
                    CancelBoxSelection();
                }

                // Block world input over the UI, but keep marker caches updating so panel
                // commands remain visible without allowing clicks through the panel.
                if (IsObjectEditorMode && m_MeshEditController != null && TryGetActiveEditorObject(out ProceduralObject editedObject))
                {
                    Camera camera = MeshEditCamera;

                    if (camera != null && !m_FaceOperation.IsActive)
                    {
                        Vector2 mousePosition = InputManager.instance.mousePosition;

                        m_MeshEditController.SetVertexHoverSuppressed(true);
                        m_MeshEditController.UpdateFrame(editedObject, camera, mousePosition, updateHover: false);
                    }

                    m_MeshEditController.ClearHover();
                }

                return inputDeps;
            }

            Mouse mouse = Mouse.current;

            // Sample input once so early returns cannot give later handlers a different state.
            bool primaryPressed = mouse != null && mouse.leftButton.wasPressedThisFrame;

            bool primaryReleased = mouse != null && mouse.leftButton.wasReleasedThisFrame;

            bool cancelPressed = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame);

            bool shiftPressed = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

            bool controlPressed = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);

            bool altPressed = keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);

            if (cancelPressed)
            {
                m_PendingObjectPick.Clear();
                BackOutOneLevel();
                return inputDeps;
            }

            if (m_Mode != ObjectToolMode.Select && m_Mode != ObjectToolMode.CombineSource && m_Mode != ObjectToolMode.Delete && m_Mode != ObjectToolMode.Copy)
            {
                m_ProceduralObjectSystem.ClearHoveredObject();
            }

            // Run only the first matching mode handler each frame.
            if (IsClipboardMode)
            {
                UpdateClipboardMode(primaryPressed);
                return inputDeps;
            }

            if (m_Mode == ObjectToolMode.Delete)
            {
                UpdateDeleteMode(primaryPressed);
                return inputDeps;
            }

            if (m_Mode == ObjectToolMode.ConvertAsset)
            {
                return UpdateAssetConversionMode(inputDeps, primaryPressed);
            }

            if (IsPrimitivePlacementMode)
            {
                return UpdatePlacementMode(inputDeps, primaryPressed);
            }

            if (m_Mode == ObjectToolMode.CombineSource)
            {
                return UpdateCombineSourceMode(inputDeps, primaryPressed);
            }

            if (IsObjectEditorMode)
            {
                return UpdateMeshEditMode(inputDeps, primaryPressed, primaryReleased, shiftPressed, controlPressed, altPressed, continuePressed);
            }

            return UpdateObjectMode(inputDeps, primaryPressed, primaryReleased, shiftPressed, controlPressed);
        }

        protected override void OnStopRunning()
        {
            m_ProceduralObjectSystem.EndLodPreview();
            m_ProceduralObjectSystem.CancelClipboardOperation();
            m_ProceduralObjectSystem.CancelHistoryReplay();
            CancelArray();
            ResetMeshTranslationPreparation();
            CancelAxisPicking();
            ResetTopologyEdit();
            m_PendingObjectPick.Clear();
            ClearAssetConversionHover();
            CancelBoxSelection();
            m_LoopCut.Reset();
            m_SurfaceCut.Reset();
            RefreshMeshCutInput();
            m_FaceOperation.Reset();

            if (DeformationCommitBusy)
            {
                ResetCageDrag();
            }

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

            base.OnStopRunning();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }
    }
}