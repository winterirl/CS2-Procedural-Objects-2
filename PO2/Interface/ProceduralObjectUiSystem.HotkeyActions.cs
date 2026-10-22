using PO2.Input;
using PO2.Geometry.Editing;
using PO2.Interface.Models;
using PO2.Tools.ObjectTool;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private bool EditableSelection => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked;

        private bool ObjectCopyContext => m_ProceduralObjectToolSystem.Workspace == Workspace.Transform && m_MaterialManagementView == MaterialManagementView.None && !m_PersistenceCoordinatorSystem.RecoveryResultPending;

        private bool CanUseCopyHotkey()
        {
            if (NavigationInputBlocked || m_MaterialImportBusy) return false;
            if (!ObjectCopyContext) return true;
            return (GetHotkeyScope() & HotkeyScope.ObjectModes) != 0 && m_ProceduralObjectToolSystem.Mode == ObjectToolMode.Transform && !m_ProceduralObjectToolSystem.MeshMutationBusy;
        }

        private void CopyHotkey()
        {
            string command = ObjectCopyContext && m_ProceduralObjectSystem.SelectedObject != null ? "copySelected" : "toggleCopyMode";
            GetHotkeyCommand(command)();
        }

        private void InitializeHotkeys()
        {
            AddHotkey(nameof(Setting.TogglePO2), () => Mod.Settings.TogglePO2, GetHotkeyCommand("toggleTool"), () => true);
            AddHotkey(nameof(Setting.SelectMode), () => Mod.Settings.SelectMode, GetHotkeyCommand("navigateSelectWorkspace"), () => !NavigationInputBlocked && !m_MaterialImportBusy);
            AddHotkey(nameof(Setting.CreateMode), () => Mod.Settings.CreateMode, GetHotkeyCommand("navigateCreateWorkspace"), () => !NavigationInputBlocked && !m_MaterialImportBusy);
            AddHotkey(nameof(Setting.ConvertMode), () => Mod.Settings.ConvertMode, GetHotkeyCommand("navigateConvertWorkspace"), () => !NavigationInputBlocked && !m_MaterialImportBusy);
            AddHotkey(nameof(Setting.MaterialsMode), () => Mod.Settings.MaterialsMode, GetHotkeyCommand("openMaterialLibraryView"), () => !NavigationInputBlocked && !m_MaterialImportBusy);
            AddHotkey(nameof(Setting.CopyMode), () => Mod.Settings.CopyMode, CopyHotkey, CanUseCopyHotkey);
            AddHotkey(nameof(Setting.PasteMode), () => Mod.Settings.PasteMode, GetHotkeyCommand("togglePasteMode"), () => !NavigationInputBlocked && !m_MaterialImportBusy && m_ProceduralObjectSystem.HasClipboard);
            AddHotkey(nameof(Setting.DeleteMode), () => Mod.Settings.DeleteMode, GetHotkeyCommand("toggleDeleteMode"), () => !NavigationInputBlocked && !m_MaterialImportBusy);
            AddHotkey(nameof(Setting.Undo), () => Mod.Settings.Undo, GetHotkeyCommand("undo"), () => !m_MaterialImportBusy && m_ProceduralObjectSystem.CanUndoHistory);
            AddHotkey(nameof(Setting.Redo), () => Mod.Settings.Redo, GetHotkeyCommand("redo"), () => !m_MaterialImportBusy && m_ProceduralObjectSystem.CanRedoHistory);
            AddHotkey(nameof(Setting.RedoAlternate), () => Mod.Settings.RedoAlternate, GetHotkeyCommand("redo"), () => !m_MaterialImportBusy && m_ProceduralObjectSystem.CanRedoHistory);
            AddHotkey(nameof(Setting.Help), () => Mod.Settings.Help, GetHotkeyCommand("toggleHelpView"), () => !m_MaterialImportBusy);
            AddHotkey(nameof(Setting.ObjectWorkspace), () => Mod.Settings.ObjectWorkspace, GetHotkeyCommand("navigateTransformWorkspace"), () => m_ProceduralObjectSystem.SelectedObject != null && !NavigationInputBlocked);
            AddHotkey(nameof(Setting.EditWorkspace), () => Mod.Settings.EditWorkspace, GetHotkeyCommand("navigateMeshEditWorkspace"), () => m_ProceduralObjectSystem.SelectedObject != null && !NavigationInputBlocked);
            AddHotkey(nameof(Setting.TexturingWorkspace), () => Mod.Settings.TexturingWorkspace, GetHotkeyCommand("navigateTexturingWorkspace"), () => m_ProceduralObjectSystem.SelectedObject != null && !NavigationInputBlocked);
            AddHotkey(nameof(Setting.PreviousPart), () => Mod.Settings.PreviousPart, GetHotkeyCommand("previousSelectedPart"), () => m_ProceduralObjectSystem.SelectedPartCount > 1);
            AddHotkey(nameof(Setting.NextPart), () => Mod.Settings.NextPart, GetHotkeyCommand("nextSelectedPart"), () => m_ProceduralObjectSystem.SelectedPartCount > 1);
            AddHotkey(nameof(Setting.ToggleMarkers), () => Mod.Settings.ToggleMarkers, GetHotkeyCommand("toggleObjectMarkers"), () => true);
            AddHotkey(nameof(Setting.ClearClipboard), () => Mod.Settings.ClearClipboard, GetHotkeyCommand("clearClipboard"), () => m_ProceduralObjectSystem.HasClipboard);
            AddHotkey(nameof(Setting.CreateBox), () => Mod.Settings.CreateBox, GetHotkeyCommand("placeBox"), () => true);
            AddHotkey(nameof(Setting.CreatePlane), () => Mod.Settings.CreatePlane, GetHotkeyCommand("placePlane"), () => true);
            AddHotkey(nameof(Setting.CreateWedge), () => Mod.Settings.CreateWedge, GetHotkeyCommand("placeWedge"), () => true);
            AddHotkey(nameof(Setting.CreateCylinder), () => Mod.Settings.CreateCylinder, GetHotkeyCommand("placeCylinder"), () => true);
            AddHotkey(nameof(Setting.CreateCone), () => Mod.Settings.CreateCone, GetHotkeyCommand("placeCone"), () => true);
            AddHotkey(nameof(Setting.CreateArch), () => Mod.Settings.CreateArch, GetHotkeyCommand("placeArch"), () => true);
            AddHotkey(nameof(Setting.CreateStairs), () => Mod.Settings.CreateStairs, GetHotkeyCommand("placeStairs"), () => true);
            AddHotkey(nameof(Setting.CreateSphere), () => Mod.Settings.CreateSphere, GetHotkeyCommand("placeSphere"), () => true);
            AddHotkey(nameof(Setting.CreateDome), () => Mod.Settings.CreateDome, GetHotkeyCommand("placeDome"), () => true);
            AddHotkey(nameof(Setting.ObjectMove), () => Mod.Settings.ObjectMove, GetHotkeyCommand("setTranslateMode"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (!m_ProceduralObjectToolSystem.TransformRequiresBake));
            AddHotkey(nameof(Setting.ObjectRotate), () => Mod.Settings.ObjectRotate, GetHotkeyCommand("setRotateMode"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (!m_ProceduralObjectToolSystem.TransformRequiresBake));
            AddHotkey(nameof(Setting.ObjectScale), () => Mod.Settings.ObjectScale, GetHotkeyCommand("setScaleMode"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (!m_ProceduralObjectToolSystem.TransformRequiresBake));
            AddHotkey(nameof(Setting.ObjectTerrain), () => Mod.Settings.ObjectTerrain, GetHotkeyCommand("snapToTerrain"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (!m_ProceduralObjectToolSystem.TransformRequiresBake && m_ProceduralObjectToolSystem.CanSnapToTerrain));
            AddHotkey(nameof(Setting.ObjectMirror), () => Mod.Settings.ObjectMirror, GetHotkeyCommand("setMirrorMode"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (true));
            AddHotkey(nameof(Setting.ObjectDeform), () => Mod.Settings.ObjectDeform, GetHotkeyCommand("setDeformMode"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (true));
            AddHotkey(nameof(Setting.ObjectObjectScope), () => Mod.Settings.ObjectObjectScope, GetHotkeyCommand("setObjectTransformTarget"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (true));
            AddHotkey(nameof(Setting.ObjectPartScope), () => Mod.Settings.ObjectPartScope, GetHotkeyCommand("setActivePartTransformTarget"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (m_ProceduralObjectSystem.SelectedPartIndex >= 0));
            AddHotkey(nameof(Setting.ObjectAddPrimitivePart), () => Mod.Settings.ObjectAddPrimitivePart, GetHotkeyCommand("addPrimitivePart"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (m_ProceduralObjectSystem.CanAddPrimitivePart));
            AddHotkey(nameof(Setting.ObjectCombine), () => Mod.Settings.ObjectCombine, GetHotkeyCommand("combineSelected"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (m_ProceduralObjectSystem.CanCombineSelected));
            AddHotkey(nameof(Setting.ObjectDuplicatePart), () => Mod.Settings.ObjectDuplicatePart, GetHotkeyCommand("duplicateSelectedPart"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (m_ProceduralObjectSystem.CanDuplicateSelectedPart));
            AddHotkey(nameof(Setting.ObjectArrayPart), () => Mod.Settings.ObjectArrayPart, GetHotkeyCommand("beginArray"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (m_ProceduralObjectToolSystem.CanArraySelectedPart));
            AddHotkey(nameof(Setting.ObjectExtractPart), () => Mod.Settings.ObjectExtractPart, GetHotkeyCommand("extractSelectedPart"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (m_ProceduralObjectSystem.CanExtractSelectedPart));
            AddHotkey(nameof(Setting.ObjectDeletePart), () => Mod.Settings.ObjectDeletePart, GetHotkeyCommand("deleteSelectedPart"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (m_ProceduralObjectSystem.CanDeleteSelectedPart));
            AddHotkey(nameof(Setting.ObjectDuplicate), () => Mod.Settings.ObjectDuplicate, GetHotkeyCommand("duplicateSelected"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (m_ProceduralObjectSystem.CanDuplicateSelected));
            AddHotkey(nameof(Setting.ObjectLock), () => Mod.Settings.ObjectLock, GetHotkeyCommand("toggleSelectedLocked"), () => m_ProceduralObjectSystem.SelectedObject != null && (true));
            AddHotkey(nameof(Setting.ObjectDelete), () => Mod.Settings.ObjectDelete, GetHotkeyCommand("deleteSelected"), () => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObjectLocked && (true));
            AddHotkey(nameof(Setting.EditVertices), () => Mod.Settings.EditVertices, GetHotkeyCommand("setVertexSelectionMode"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.EditEdges), () => Mod.Settings.EditEdges, GetHotkeyCommand("setEdgeSelectionMode"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.EditSolid), () => Mod.Settings.EditSolid, GetHotkeyCommand("setMeshVisibilitySolid"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.EditXRay), () => Mod.Settings.EditXRay, GetHotkeyCommand("setMeshVisibilityXRay"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.EditPartScope), () => Mod.Settings.EditPartScope, GetHotkeyCommand("setMeshVertexScopeCurrentPart"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.EditObjectScope), () => Mod.Settings.EditObjectScope, GetHotkeyCommand("setMeshVertexScopeAllParts"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.EditSelectAll), () => Mod.Settings.EditSelectAll, GetHotkeyCommand("selectAllMeshElements"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.EditSelectConnected), () => Mod.Settings.EditSelectConnected, GetHotkeyCommand("selectConnectedMeshElements"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.EditInvert), () => Mod.Settings.EditInvert, GetHotkeyCommand("invertMeshSelection"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.EditPivot), () => Mod.Settings.EditPivot, GetHotkeyCommand("setMeshPivot"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.CanSetMeshPivot)));
            AddHotkey(nameof(Setting.EditSlide), () => Mod.Settings.EditSlide, GetHotkeyCommand("beginVertexSlide"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.MeshEditSelectionMode == MeshSelectionMode.Vertex && m_ProceduralObjectToolSystem.MeshEditSelectedVertexCount == 1)));
            AddHotkey(nameof(Setting.EditAlignX), () => Mod.Settings.EditAlignX, GetHotkeyCommand("alignVerticesX"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.MeshEditSelectionMode == MeshSelectionMode.Vertex && m_ProceduralObjectToolSystem.MeshEditSelectedVertexCount > 0)));
            AddHotkey(nameof(Setting.EditAlignY), () => Mod.Settings.EditAlignY, GetHotkeyCommand("alignVerticesY"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.MeshEditSelectionMode == MeshSelectionMode.Vertex && m_ProceduralObjectToolSystem.MeshEditSelectedVertexCount > 0)));
            AddHotkey(nameof(Setting.EditAlignZ), () => Mod.Settings.EditAlignZ, GetHotkeyCommand("alignVerticesZ"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.MeshEditSelectionMode == MeshSelectionMode.Vertex && m_ProceduralObjectToolSystem.MeshEditSelectedVertexCount > 0)));
            AddHotkey(nameof(Setting.EditLoopCut), () => Mod.Settings.EditLoopCut, GetHotkeyCommand("beginLoopCut"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.CanMeshLoopCut)));
            AddHotkey(nameof(Setting.EditSurfaceCut), () => Mod.Settings.EditSurfaceCut, GetHotkeyCommand("beginSurfaceCut"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.CanMeshSurfaceCut)));
            AddHotkey(nameof(Setting.EditDissolveLoop), () => Mod.Settings.EditDissolveLoop, GetHotkeyCommand("dissolveLoop"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.CanDissolveSelectedLoop)));
            AddHotkey(nameof(Setting.EditDissolveCut), () => Mod.Settings.EditDissolveCut, GetHotkeyCommand("dissolveSurfaceCut"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.CanDissolveSelectedSurfaceCut)));
            AddHotkey(nameof(Setting.EditSubdivide), () => Mod.Settings.EditSubdivide, GetHotkeyCommand("subdivideEdges"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.CanSubdivideSelectedEdges)));
            AddHotkey(nameof(Setting.EditMerge), () => Mod.Settings.EditMerge, GetHotkeyCommand("mergeVertices"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.CanMergeSelectedVertices)));
            AddHotkey(nameof(Setting.EditExtrude), () => Mod.Settings.EditExtrude, GetHotkeyCommand("beginFaceExtrude"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.CanBeginMeshFaceExtrude)));
            AddHotkey(nameof(Setting.EditInset), () => Mod.Settings.EditInset, GetHotkeyCommand("beginFaceInset"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.CanBeginMeshFaceInset)));
            AddHotkey(nameof(Setting.EditDelete), () => Mod.Settings.EditDelete, GetHotkeyCommand("deleteVertices"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.CanDeleteSelectedVertices)));
            AddHotkey(nameof(Setting.EditDissolve), () => Mod.Settings.EditDissolve, GetHotkeyCommand("dissolveVertices"), () => EditableSelection && (!m_ProceduralObjectToolSystem.MeshEditRequiresBake && (m_ProceduralObjectToolSystem.CanDissolveSelectedVertices)));
            AddHotkey(nameof(Setting.EditApplyDeform), () => Mod.Settings.EditApplyDeform, GetHotkeyCommand("bakeMeshDeformation"), () => EditableSelection && (m_ProceduralObjectToolSystem.CanBakeMeshDeformation));
            AddHotkey(nameof(Setting.EditResetDeform), () => Mod.Settings.EditResetDeform, GetHotkeyCommand("resetMeshDeformation"), () => EditableSelection && (m_ProceduralObjectToolSystem.CanBakeMeshDeformation));
            AddHotkey(nameof(Setting.EditMove), () => Mod.Settings.EditMove, GetHotkeyCommand("setMeshTransformMode", 0), () => EditableSelection && !m_ProceduralObjectToolSystem.MeshEditRequiresBake && m_ProceduralObjectToolSystem.CanApplyMeshNumericMove);
            AddHotkey(nameof(Setting.EditRotate), () => Mod.Settings.EditRotate, GetHotkeyCommand("setMeshTransformMode", 1), () => EditableSelection && !m_ProceduralObjectToolSystem.MeshEditRequiresBake && m_ProceduralObjectToolSystem.CanApplyMeshNumericMove);
            AddHotkey(nameof(Setting.EditScale), () => Mod.Settings.EditScale, GetHotkeyCommand("setMeshTransformMode", 2), () => EditableSelection && !m_ProceduralObjectToolSystem.MeshEditRequiresBake && m_ProceduralObjectToolSystem.CanApplyMeshNumericMove);
            AddHotkey(nameof(Setting.EditSelectionPivot), () => Mod.Settings.EditSelectionPivot, GetHotkeyCommand("setMeshTransformPivot", 0), () => EditableSelection && !m_ProceduralObjectToolSystem.MeshEditRequiresBake && m_ProceduralObjectToolSystem.CanApplyMeshNumericMove);
            AddHotkey(nameof(Setting.EditSavedPivot), () => Mod.Settings.EditSavedPivot, GetHotkeyCommand("setMeshTransformPivot", 1), () => EditableSelection && !m_ProceduralObjectToolSystem.MeshEditRequiresBake && m_ProceduralObjectToolSystem.CanApplyMeshNumericMove);
            AddHotkey(nameof(Setting.TextureAll), () => Mod.Settings.TextureAll, GetHotkeyCommand("selectAllMeshElements"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TextureInvert), () => Mod.Settings.TextureInvert, GetHotkeyCommand("invertMeshSelection"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TexturePreviousArea), () => Mod.Settings.TexturePreviousArea, GetHotkeyCommand("previousSelectedSurfaceArea"), () => EditableSelection && (m_ProceduralObjectSystem.SelectedSurfaceAreaCount > 1));
            AddHotkey(nameof(Setting.TextureNextArea), () => Mod.Settings.TextureNextArea, GetHotkeyCommand("nextSelectedSurfaceArea"), () => EditableSelection && (m_ProceduralObjectSystem.SelectedSurfaceAreaCount > 1));
            AddHotkey(nameof(Setting.TextureNewArea), () => Mod.Settings.TextureNewArea, GetHotkeyCommand("newSurfaceAreaFromSelection"), () => EditableSelection && (m_ProceduralObjectToolSystem.MeshEditSelectedSurfaceCount > 0));
            AddHotkey(nameof(Setting.TextureDeleteArea), () => Mod.Settings.TextureDeleteArea, GetHotkeyCommand("deleteSurfaceArea"), () => EditableSelection && (m_ProceduralObjectSystem.SelectedSurfaceAreaId != 0));
            AddHotkey(nameof(Setting.TextureAddToArea), () => Mod.Settings.TextureAddToArea, GetHotkeyCommand("assignSelectionToSurfaceArea"), () => EditableSelection && (m_ProceduralObjectToolSystem.MeshEditSelectedSurfaceCount > 0));
            AddHotkey(nameof(Setting.TexturePreviousMaterial), () => Mod.Settings.TexturePreviousMaterial, GetHotkeyCommand("previousLibraryMaterial"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TextureNextMaterial), () => Mod.Settings.TextureNextMaterial, GetHotkeyCommand("nextLibraryMaterial"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TextureGlass), () => Mod.Settings.TextureGlass, GetHotkeyCommand("setVanillaSpecialGlass"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TextureGrass), () => Mod.Settings.TextureGrass, GetHotkeyCommand("setVanillaSpecialGrass"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TextureWater), () => Mod.Settings.TextureWater, GetHotkeyCommand("setVanillaSpecialWater"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TextureMapping), () => Mod.Settings.TextureMapping, GetHotkeyCommand("cycleMappingMode"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TextureResetMapping), () => Mod.Settings.TextureResetMapping, GetHotkeyCommand("resetMappingTransform"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TextureResetMaterial), () => Mod.Settings.TextureResetMaterial, GetHotkeyCommand("resetMaterialProperties"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TextureResetArea), () => Mod.Settings.TextureResetArea, GetHotkeyCommand("restoreSurfaceAreaOriginalAppearance"), () => EditableSelection && (m_ProceduralObjectSystem.SelectedSurfaceAreaHasOriginalAppearance));
            AddHotkey(nameof(Setting.TextureResetObject), () => Mod.Settings.TextureResetObject, GetHotkeyCommand("restoreSelectedObjectOriginalAppearance"), () => EditableSelection && (m_ProceduralObjectSystem.SelectedObjectHasOriginalAppearance));
            AddHotkey(nameof(Setting.TextureDecals), () => Mod.Settings.TextureDecals, GetHotkeyCommand("toggleSelectedReceivesDecals"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TextureMaskSource), () => Mod.Settings.TextureMaskSource, GetHotkeyCommand("setColorMaskSource"), () => EditableSelection && (m_ProceduralObjectSystem.SelectedColorMaskHasSource));
            AddHotkey(nameof(Setting.TextureMaskOverride), () => Mod.Settings.TextureMaskOverride, GetHotkeyCommand("setColorMaskOverride"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TextureMaskOff), () => Mod.Settings.TextureMaskOff, GetHotkeyCommand("setColorMaskOff"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.TransformGlobal), () => Mod.Settings.TransformGlobal, GetHotkeyCommand("setGlobalTransformOrientation"), () => true);
            AddHotkey(nameof(Setting.TransformLocal), () => Mod.Settings.TransformLocal, GetHotkeyCommand("setLocalTransformOrientation"), () => true);
            AddHotkey(nameof(Setting.TransformNewAxis), () => Mod.Settings.TransformNewAxis, GetHotkeyCommand("beginAxisPicking"), () => m_ProceduralObjectToolSystem.CanBeginAxisPicking);
            AddHotkey(nameof(Setting.TransformMagnet), () => Mod.Settings.TransformMagnet, GetHotkeyCommand("toggleSnapMagnet"), () => true);
            AddHotkey(nameof(Setting.TransformIncrement), () => Mod.Settings.TransformIncrement, GetHotkeyCommand("toggleSnapIncrement"), () => true);
            AddHotkey(nameof(Setting.TransformVertices), () => Mod.Settings.TransformVertices, GetHotkeyCommand("toggleSnapPOVertices"), () => true);
            AddHotkey(nameof(Setting.TransformEdges), () => Mod.Settings.TransformEdges, GetHotkeyCommand("toggleSnapPOEdges"), () => true);
            AddHotkey(nameof(Setting.TransformSurfaces), () => Mod.Settings.TransformSurfaces, GetHotkeyCommand("toggleSnapGameSurfaces"), () => true);
            AddHotkey(nameof(Setting.MirrorX), () => Mod.Settings.MirrorX, GetHotkeyCommand("mirrorSelected", 0), () => EditableSelection && m_ProceduralObjectToolSystem.CanMirrorSelected);
            AddHotkey(nameof(Setting.MirrorY), () => Mod.Settings.MirrorY, GetHotkeyCommand("mirrorSelected", 1), () => EditableSelection && m_ProceduralObjectToolSystem.CanMirrorSelected);
            AddHotkey(nameof(Setting.MirrorZ), () => Mod.Settings.MirrorZ, GetHotkeyCommand("mirrorSelected", 2), () => EditableSelection && m_ProceduralObjectToolSystem.CanMirrorSelected);
            AddHotkey(nameof(Setting.DeformMove), () => Mod.Settings.DeformMove, GetHotkeyCommand("setDeformationMoveMode"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.DeformScale), () => Mod.Settings.DeformScale, GetHotkeyCommand("setDeformationScaleMode"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.DeformApply), () => Mod.Settings.DeformApply, GetHotkeyCommand("bakeDeformation"), () => EditableSelection && (m_ProceduralObjectToolSystem.CanBakeDeformation));
            AddHotkey(nameof(Setting.DeformReset), () => Mod.Settings.DeformReset, GetHotkeyCommand("resetDeformation"), () => EditableSelection && (m_ProceduralObjectToolSystem.CanBakeDeformation));
            AddHotkey(nameof(Setting.DeformExisting), () => Mod.Settings.DeformExisting, GetHotkeyCommand("editExistingDeformation"), () => EditableSelection && (true));
            AddHotkey(nameof(Setting.DeformSelectionPivot), () => Mod.Settings.DeformSelectionPivot, GetHotkeyCommand("setDeformationPivot", 0), () => EditableSelection);
            AddHotkey(nameof(Setting.DeformSavedPivot), () => Mod.Settings.DeformSavedPivot, GetHotkeyCommand("setDeformationPivot", 1), () => EditableSelection);
            AddHotkey(nameof(Setting.ArrayApply), () => Mod.Settings.ArrayApply, GetHotkeyCommand("applyArray"), () => m_ProceduralObjectToolSystem.CanApplyArray);
            AddHotkey(nameof(Setting.ArrayEndpoint), () => Mod.Settings.ArrayEndpoint, GetHotkeyCommand("chooseArrayEndpoint"), () => !m_ProceduralObjectToolSystem.ArrayDragging);
            AddHotkey(nameof(Setting.ArrayResetRotation), () => Mod.Settings.ArrayResetRotation, GetHotkeyCommand("resetArrayRotation"), () => m_ProceduralObjectToolSystem.CanResetArrayRotation);
            AddHotkey(nameof(Setting.ArrayPathLine), () => Mod.Settings.ArrayPathLine, GetHotkeyCommand("setArrayPathKind", 0), () => !m_ProceduralObjectToolSystem.ArrayDragging && !m_ProceduralObjectToolSystem.ArrayDrawing);
            AddHotkey(nameof(Setting.ArrayPathCurve), () => Mod.Settings.ArrayPathCurve, GetHotkeyCommand("setArrayPathKind", 1), () => !m_ProceduralObjectToolSystem.ArrayDragging && !m_ProceduralObjectToolSystem.ArrayDrawing);
            AddHotkey(nameof(Setting.ArrayAlignmentEndToEnd), () => Mod.Settings.ArrayAlignmentEndToEnd, GetHotkeyCommand("setArrayAlignment", 0), () => !m_ProceduralObjectToolSystem.ArrayDragging && !m_ProceduralObjectToolSystem.ArrayDrawing);
            AddHotkey(nameof(Setting.ArrayAlignmentFixedOrientation), () => Mod.Settings.ArrayAlignmentFixedOrientation, GetHotkeyCommand("setArrayAlignment", 1), () => !m_ProceduralObjectToolSystem.ArrayDragging && !m_ProceduralObjectToolSystem.ArrayDrawing);
            AddHotkey(nameof(Setting.ArrayDistributionFixedGap), () => Mod.Settings.ArrayDistributionFixedGap, GetHotkeyCommand("setArrayMode", 0), () => !m_ProceduralObjectToolSystem.ArrayDragging && !m_ProceduralObjectToolSystem.ArrayDrawing);
            AddHotkey(nameof(Setting.ArrayDistributionFitEndpoint), () => Mod.Settings.ArrayDistributionFitEndpoint, GetHotkeyCommand("setArrayMode", 1), () => !m_ProceduralObjectToolSystem.ArrayDragging && !m_ProceduralObjectToolSystem.ArrayDrawing);
            AddHotkey(nameof(Setting.ArrayDistributionFixedCount), () => Mod.Settings.ArrayDistributionFixedCount, GetHotkeyCommand("setArrayMode", 2), () => !m_ProceduralObjectToolSystem.ArrayDragging && !m_ProceduralObjectToolSystem.ArrayDrawing);
            AddHotkey(nameof(Setting.ArrayControlEndpoint), () => Mod.Settings.ArrayControlEndpoint, GetHotkeyCommand("selectArrayControlPoint", 0), () => !m_ProceduralObjectToolSystem.ArrayDragging && !m_ProceduralObjectToolSystem.ArrayDrawing);
            AddHotkey(nameof(Setting.ArrayControlStartHandle), () => Mod.Settings.ArrayControlStartHandle, GetHotkeyCommand("selectArrayControlPoint", 1), () => !m_ProceduralObjectToolSystem.ArrayDragging && !m_ProceduralObjectToolSystem.ArrayDrawing && m_ProceduralObjectToolSystem.ArrayPathKind == 1);
            AddHotkey(nameof(Setting.ArrayControlEndHandle), () => Mod.Settings.ArrayControlEndHandle, GetHotkeyCommand("selectArrayControlPoint", 2), () => !m_ProceduralObjectToolSystem.ArrayDragging && !m_ProceduralObjectToolSystem.ArrayDrawing && m_ProceduralObjectToolSystem.ArrayPathKind == 1);
            AddHotkey(nameof(Setting.Continue), () => Mod.Settings.Continue, ContinueHotkey, () => m_ProceduralObjectToolSystem.CanContinueMeshFaceExtrude || m_ProceduralObjectToolSystem.CanContinueMeshFaceInset);
            AddHotkey(nameof(Setting.Confirm), () => Mod.Settings.Confirm, ConfirmHotkey, () => m_ProceduralObjectToolSystem.CanCommitMeshFaceExtrude || m_ProceduralObjectToolSystem.CanCommitMeshFaceInset || m_ProceduralObjectToolSystem.CanConfirmAxisPicking || m_ProceduralObjectToolSystem.CanApplyArray);
        }

        private void ContinueHotkey()
        {
            if (m_ProceduralObjectToolSystem.CanContinueMeshFaceExtrude) m_HotkeyCommands["continueFaceExtrude"]();
            else if (m_ProceduralObjectToolSystem.CanContinueMeshFaceInset) m_HotkeyCommands["continueFaceInset"]();
        }

        private void ConfirmHotkey()
        {
            if (m_ProceduralObjectToolSystem.CanConfirmAxisPicking) m_HotkeyCommands["confirmAxisPicking"]();
            else if (m_ProceduralObjectToolSystem.CanApplyArray) m_HotkeyCommands["applyArray"]();
            else if (m_ProceduralObjectToolSystem.CanCommitMeshFaceExtrude) m_HotkeyCommands["confirmFaceExtrude"]();
            else if (m_ProceduralObjectToolSystem.CanCommitMeshFaceInset) m_HotkeyCommands["confirmFaceInset"]();
        }
    }
}