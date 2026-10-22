using Game.Input;
using Game.Settings;
using PO2.Input;

namespace PO2
{
    [HotkeyAction(nameof(TogglePO2), HotkeyScope.Any)]
    [HotkeyAction(nameof(SelectMode), HotkeyScope.Open)]
    [HotkeyAction(nameof(CreateMode), HotkeyScope.Open)]
    [HotkeyAction(nameof(ConvertMode), HotkeyScope.Open)]
    [HotkeyAction(nameof(MaterialsMode), HotkeyScope.Open)]
    [HotkeyAction(nameof(CopyMode), HotkeyScope.Open)]
    [HotkeyAction(nameof(PasteMode), HotkeyScope.Open)]
    [HotkeyAction(nameof(DeleteMode), HotkeyScope.Open)]
    [HotkeyAction(nameof(Undo), HotkeyScope.Open)]
    [HotkeyAction(nameof(Redo), HotkeyScope.Open)]
    [HotkeyAction(nameof(RedoAlternate), HotkeyScope.Open)]
    [HotkeyAction(nameof(Help), HotkeyScope.Open)]
    [HotkeyAction(nameof(ObjectWorkspace), HotkeyScope.Editors | HotkeyScope.Lod)]
    [HotkeyAction(nameof(EditWorkspace), HotkeyScope.Editors | HotkeyScope.Lod)]
    [HotkeyAction(nameof(TexturingWorkspace), HotkeyScope.Editors | HotkeyScope.Lod)]
    [HotkeyAction(nameof(PreviousPart), HotkeyScope.Editors)]
    [HotkeyAction(nameof(NextPart), HotkeyScope.Editors)]
    [HotkeyAction(nameof(ToggleMarkers), HotkeyScope.Select)]
    [HotkeyAction(nameof(ClearClipboard), HotkeyScope.Copy)]
    [HotkeyAction(nameof(CreateBox), HotkeyScope.Create)]
    [HotkeyAction(nameof(CreatePlane), HotkeyScope.Create)]
    [HotkeyAction(nameof(CreateWedge), HotkeyScope.Create)]
    [HotkeyAction(nameof(CreateCylinder), HotkeyScope.Create)]
    [HotkeyAction(nameof(CreateCone), HotkeyScope.Create)]
    [HotkeyAction(nameof(CreateArch), HotkeyScope.Create)]
    [HotkeyAction(nameof(CreateStairs), HotkeyScope.Create)]
    [HotkeyAction(nameof(CreateSphere), HotkeyScope.Create)]
    [HotkeyAction(nameof(CreateDome), HotkeyScope.Create)]
    [HotkeyAction(nameof(ObjectMove), HotkeyScope.ObjectModes)]
    [HotkeyAction(nameof(ObjectRotate), HotkeyScope.ObjectModes)]
    [HotkeyAction(nameof(ObjectScale), HotkeyScope.ObjectModes)]
    [HotkeyAction(nameof(ObjectTerrain), HotkeyScope.Object)]
    [HotkeyAction(nameof(ObjectMirror), HotkeyScope.ObjectModes)]
    [HotkeyAction(nameof(ObjectDeform), HotkeyScope.ObjectModes)]
    [HotkeyAction(nameof(ObjectObjectScope), HotkeyScope.Object)]
    [HotkeyAction(nameof(ObjectPartScope), HotkeyScope.Object)]
    [HotkeyAction(nameof(ObjectAddPrimitivePart), HotkeyScope.Object)]
    [HotkeyAction(nameof(ObjectCombine), HotkeyScope.Object)]
    [HotkeyAction(nameof(ObjectDuplicatePart), HotkeyScope.Object)]
    [HotkeyAction(nameof(ObjectArrayPart), HotkeyScope.Object)]
    [HotkeyAction(nameof(ObjectExtractPart), HotkeyScope.Object)]
    [HotkeyAction(nameof(ObjectDeletePart), HotkeyScope.Object)]
    [HotkeyAction(nameof(ObjectDuplicate), HotkeyScope.Object)]
    [HotkeyAction(nameof(ObjectLock), HotkeyScope.Object)]
    [HotkeyAction(nameof(ObjectDelete), HotkeyScope.Object)]
    [HotkeyAction(nameof(EditVertices), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditEdges), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditSolid), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditXRay), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditPartScope), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditObjectScope), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditSelectAll), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditSelectConnected), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditInvert), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditPivot), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditSlide), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditAlignX), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditAlignY), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditAlignZ), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditLoopCut), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditSurfaceCut), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditDissolveLoop), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditDissolveCut), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditSubdivide), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditMerge), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditExtrude), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditInset), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditDelete), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditDissolve), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditApplyDeform), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditResetDeform), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditMove), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditRotate), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditScale), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditSelectionPivot), HotkeyScope.Edit)]
    [HotkeyAction(nameof(EditSavedPivot), HotkeyScope.Edit)]
    [HotkeyAction(nameof(TextureAll), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureInvert), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TexturePreviousArea), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureNextArea), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureNewArea), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureDeleteArea), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureAddToArea), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TexturePreviousMaterial), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureNextMaterial), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureGlass), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureGrass), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureWater), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureMapping), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureResetMapping), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureResetMaterial), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureResetArea), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureResetObject), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureDecals), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureMaskSource), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureMaskOverride), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TextureMaskOff), HotkeyScope.Texturing)]
    [HotkeyAction(nameof(TransformGlobal), HotkeyScope.TransformEditors)]
    [HotkeyAction(nameof(TransformLocal), HotkeyScope.TransformEditors)]
    [HotkeyAction(nameof(TransformNewAxis), HotkeyScope.TransformEditors)]
    [HotkeyAction(nameof(TransformMagnet), HotkeyScope.TransformEditors)]
    [HotkeyAction(nameof(TransformIncrement), HotkeyScope.TransformEditors)]
    [HotkeyAction(nameof(TransformVertices), HotkeyScope.TransformEditors)]
    [HotkeyAction(nameof(TransformEdges), HotkeyScope.TransformEditors)]
    [HotkeyAction(nameof(TransformSurfaces), HotkeyScope.TransformEditors)]
    [HotkeyAction(nameof(MirrorX), HotkeyScope.Mirror)]
    [HotkeyAction(nameof(MirrorY), HotkeyScope.Mirror)]
    [HotkeyAction(nameof(MirrorZ), HotkeyScope.Mirror)]
    [HotkeyAction(nameof(DeformMove), HotkeyScope.Deform)]
    [HotkeyAction(nameof(DeformScale), HotkeyScope.Deform)]
    [HotkeyAction(nameof(DeformApply), HotkeyScope.Deform)]
    [HotkeyAction(nameof(DeformReset), HotkeyScope.Deform)]
    [HotkeyAction(nameof(DeformExisting), HotkeyScope.Deform)]
    [HotkeyAction(nameof(DeformSelectionPivot), HotkeyScope.Deform)]
    [HotkeyAction(nameof(DeformSavedPivot), HotkeyScope.Deform)]
    [HotkeyAction(nameof(ArrayApply), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayEndpoint), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayResetRotation), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayPathLine), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayPathCurve), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayAlignmentEndToEnd), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayAlignmentFixedOrientation), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayDistributionFixedGap), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayDistributionFitEndpoint), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayDistributionFixedCount), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayControlEndpoint), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayControlStartHandle), HotkeyScope.Array)]
    [HotkeyAction(nameof(ArrayControlEndHandle), HotkeyScope.Array)]
    [HotkeyAction(nameof(Continue), HotkeyScope.FaceSelection)]
    [HotkeyAction(nameof(Confirm), HotkeyScope.FacePosition | HotkeyScope.Axis | HotkeyScope.Array)]
    public sealed partial class Setting
    {
        public const string kHotkeysSection = "Hotkeys";

        [SettingsUISection(kHotkeysSection, kControlsGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.T, ctrl: true)]
        public ProxyBinding TogglePO2 { get; set; }

        [SettingsUISection(kHotkeysSection, kMainModeHotkeysGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.Digit1, ctrl: true)]
        public ProxyBinding SelectMode { get; set; }

        [SettingsUISection(kHotkeysSection, kMainModeHotkeysGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.Digit2, ctrl: true)]
        public ProxyBinding CreateMode { get; set; }

        [SettingsUISection(kHotkeysSection, kMainModeHotkeysGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.Digit3, ctrl: true)]
        public ProxyBinding ConvertMode { get; set; }

        [SettingsUISection(kHotkeysSection, kMainModeHotkeysGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.Digit4, ctrl: true)]
        public ProxyBinding MaterialsMode { get; set; }

        [SettingsUISection(kHotkeysSection, "Modes")]
        [SettingsUIKeyboardBinding(BindingKeyboard.C, ctrl: true, shift: true)]
        public ProxyBinding CopyMode { get; set; }

        [SettingsUISection(kHotkeysSection, "Modes")]
        [SettingsUIKeyboardBinding(BindingKeyboard.V, ctrl: true, shift: true)]
        public ProxyBinding PasteMode { get; set; }

        [SettingsUISection(kHotkeysSection, "Modes")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ClearClipboard { get; set; }

        [SettingsUISection(kHotkeysSection, "Modes")]
        [SettingsUIKeyboardBinding(BindingKeyboard.B, ctrl: true, shift: true)]
        public ProxyBinding DeleteMode { get; set; }

        [SettingsUISection(kHotkeysSection, "Shared")]
        [SettingsUIKeyboardBinding(BindingKeyboard.Z, ctrl: true)]
        public ProxyBinding Undo { get; set; }

        [SettingsUISection(kHotkeysSection, "Shared")]
        [SettingsUIKeyboardBinding(BindingKeyboard.Y, ctrl: true)]
        public ProxyBinding Redo { get; set; }

        [SettingsUISection(kHotkeysSection, "Shared")]
        [SettingsUIKeyboardBinding(BindingKeyboard.Z, ctrl: true, shift: true)]
        public ProxyBinding RedoAlternate { get; set; }

        [SettingsUISection(kHotkeysSection, "Shared")]
        [SettingsUIKeyboardBinding(BindingKeyboard.H, ctrl: true, shift: true)]
        public ProxyBinding Help { get; set; }

        [SettingsUISection(kHotkeysSection, "Shared")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectWorkspace { get; set; }

        [SettingsUISection(kHotkeysSection, "Shared")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditWorkspace { get; set; }

        [SettingsUISection(kHotkeysSection, "Shared")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TexturingWorkspace { get; set; }

        [SettingsUISection(kHotkeysSection, "Shared")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding PreviousPart { get; set; }

        [SettingsUISection(kHotkeysSection, "Shared")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding NextPart { get; set; }

        [SettingsUISection(kHotkeysSection, "Select")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ToggleMarkers { get; set; }

        [SettingsUISection(kHotkeysSection, "Create")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding CreateBox { get; set; }

        [SettingsUISection(kHotkeysSection, "Create")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding CreatePlane { get; set; }

        [SettingsUISection(kHotkeysSection, "Create")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding CreateWedge { get; set; }

        [SettingsUISection(kHotkeysSection, "Create")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding CreateCylinder { get; set; }

        [SettingsUISection(kHotkeysSection, "Create")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding CreateCone { get; set; }

        [SettingsUISection(kHotkeysSection, "Create")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding CreateArch { get; set; }

        [SettingsUISection(kHotkeysSection, "Create")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding CreateStairs { get; set; }

        [SettingsUISection(kHotkeysSection, "Create")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding CreateSphere { get; set; }

        [SettingsUISection(kHotkeysSection, "Create")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding CreateDome { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectMove { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectRotate { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectScale { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectTerrain { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectMirror { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectDeform { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectObjectScope { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectPartScope { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectAddPrimitivePart { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectCombine { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectDuplicatePart { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectArrayPart { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectExtractPart { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectDeletePart { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectDuplicate { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectLock { get; set; }

        [SettingsUISection(kHotkeysSection, "Object")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ObjectDelete { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditVertices { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditEdges { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditSolid { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditXRay { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditPartScope { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditObjectScope { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditSelectAll { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditSelectConnected { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditInvert { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditPivot { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditSlide { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditAlignX { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditAlignY { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditAlignZ { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditLoopCut { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditSurfaceCut { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditDissolveLoop { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditDissolveCut { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditSubdivide { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditMerge { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditExtrude { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditInset { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditDelete { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditDissolve { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditApplyDeform { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditResetDeform { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditMove { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditRotate { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditScale { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditSelectionPivot { get; set; }

        [SettingsUISection(kHotkeysSection, "Edit")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding EditSavedPivot { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureAll { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureInvert { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TexturePreviousArea { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureNextArea { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureNewArea { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureDeleteArea { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureAddToArea { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TexturePreviousMaterial { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureNextMaterial { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureGlass { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureGrass { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureWater { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureMapping { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureResetMapping { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureResetMaterial { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureResetArea { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureResetObject { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureDecals { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureMaskSource { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureMaskOverride { get; set; }

        [SettingsUISection(kHotkeysSection, "Texturing")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TextureMaskOff { get; set; }

        [SettingsUISection(kHotkeysSection, "AxesSnapping")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TransformGlobal { get; set; }

        [SettingsUISection(kHotkeysSection, "AxesSnapping")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TransformLocal { get; set; }

        [SettingsUISection(kHotkeysSection, "AxesSnapping")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TransformNewAxis { get; set; }

        [SettingsUISection(kHotkeysSection, "AxesSnapping")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TransformMagnet { get; set; }

        [SettingsUISection(kHotkeysSection, "AxesSnapping")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TransformIncrement { get; set; }

        [SettingsUISection(kHotkeysSection, "AxesSnapping")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TransformVertices { get; set; }

        [SettingsUISection(kHotkeysSection, "AxesSnapping")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TransformEdges { get; set; }

        [SettingsUISection(kHotkeysSection, "AxesSnapping")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding TransformSurfaces { get; set; }

        [SettingsUISection(kHotkeysSection, "Mirror")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding MirrorX { get; set; }

        [SettingsUISection(kHotkeysSection, "Mirror")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding MirrorY { get; set; }

        [SettingsUISection(kHotkeysSection, "Mirror")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding MirrorZ { get; set; }

        [SettingsUISection(kHotkeysSection, "Deform")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding DeformMove { get; set; }

        [SettingsUISection(kHotkeysSection, "Deform")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding DeformScale { get; set; }

        [SettingsUISection(kHotkeysSection, "Deform")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding DeformApply { get; set; }

        [SettingsUISection(kHotkeysSection, "Deform")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding DeformReset { get; set; }

        [SettingsUISection(kHotkeysSection, "Deform")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding DeformExisting { get; set; }

        [SettingsUISection(kHotkeysSection, "Deform")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding DeformSelectionPivot { get; set; }

        [SettingsUISection(kHotkeysSection, "Deform")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding DeformSavedPivot { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayApply { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayEndpoint { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayResetRotation { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayPathLine { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayPathCurve { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayAlignmentEndToEnd { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayAlignmentFixedOrientation { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayDistributionFixedGap { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayDistributionFitEndpoint { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayDistributionFixedCount { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayControlEndpoint { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayControlStartHandle { get; set; }

        [SettingsUISection(kHotkeysSection, "Array")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding ArrayControlEndHandle { get; set; }

        [SettingsUISection(kHotkeysSection, "Workflow")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding Continue { get; set; }

        [SettingsUISection(kHotkeysSection, "Workflow")]
        [SettingsUIKeyboardBinding()]
        public ProxyBinding Confirm { get; set; }
    }
}