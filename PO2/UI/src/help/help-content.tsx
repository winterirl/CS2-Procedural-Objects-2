import { LocalizedText } from "@/l10n/localization";
// Edit prose in Localization/en-US.json. Keep topic keys and empty fragments in this file.
// JSX still owns paragraph structure and emphasis.
export const helpContent = {
    lod: <></>,
    partArray: <></>,
    delete: <></>,
    copy: <></>,
    paste: <></>,
    // Workspace overviews (Workspace; ObjectList shares Select in the current UI).
    // Select and PO list; workspace 0 or legacy 6.
    select: (
        <>
            <p><LocalizedText id="PO2.Help.Select.Overview" /></p>
            <p><LocalizedText id="PO2.Help.Select.SelectionMethods" /></p>
        </>
    ),

    saveManager: (
        <>
        </>
    ),

    // Create workspace, including no primitive selected.
    create: (
        <>
            <p><LocalizedText id="PO2.Help.Create.Overview" /></p>
        </>
    ),

    // Convert workspace.
    convert: (
        <>
            <p><LocalizedText id="PO2.Help.Convert.Overview" /></p>
        </>
    ),

    // Object workspace; internal Transform.
    object: (
        <>
            
        </>
    ),

    // Edit workspace; internal MeshEdit.
    edit: (
        <>
        </>
    ),

    // Texturing workspace.
    texturing: (
        <>
            <p><LocalizedText id="PO2.Help.Texturing.SurfaceAreas" /></p>
            <p><LocalizedText id="PO2.Help.Texturing.Projection.Behavior" args={{ emphasis: <em><LocalizedText id="PO2.Help.Texturing.Projection.Emphasis" /></em> }} /></p>
            <p><LocalizedText id="PO2.Help.Texturing.Projection.Placement" /></p>
        </>
    ),

    // Material library browsing.
    materials: (
        <>
        </>
    ),

    // Creation and part placement (shared primitive descriptions).
    // PlacePrimitive interaction 3; creating a new PO.
    primitivePlacement: (
        <>
            <p><LocalizedText id="PO2.Help.Create.ObjectPlacement" /></p>
        </>
    ),

    // PlacePrimitivePart interaction 4; adding to the selected PO.
    primitivePartPlacement: (
        <>
        <p><LocalizedText id="PO2.Help.Create.PartPlacement" /></p>
        </>
    ),

    // Primitive type 0: Box.
    primitiveBox: (
        <>
            <p><LocalizedText id="PO2.Help.Primitive.Box.Description" /></p>
        </>
    ),

    // Primitive type 1: Plane.
    primitivePlane: (
        <>
            <p><LocalizedText id="PO2.Help.Primitive.Plane.Description" /></p>
        </>
    ),

    // Primitive type 2: Wedge.
    primitiveWedge: (
        <>
            <p><LocalizedText id="PO2.Help.Primitive.Wedge.Description" /></p>
        </>
    ),

    // Primitive type 3: Cylinder.
    primitiveCylinder: (
        <>
            <p><LocalizedText id="PO2.Help.Primitive.Cylinder.Description" args={{ radius: <strong><LocalizedText id="PO2.Help.Primitive.Cylinder.RadiusEmphasis" /></strong> }} /></p>
        </>
    ),

    // Primitive type 4: Cone.
    primitiveCone: (
        <>
            <p><LocalizedText id="PO2.Help.Primitive.Cone.Description" /></p>
        </>
    ),

    // Primitive type 5: Arch.
    primitiveArch: (
        <>
            <p><LocalizedText id="PO2.Help.Primitive.Arch.Description" /></p>
        </>
    ),

    // Primitive type 6: Stairs.
    primitiveStairs: (
        <>
            <p><LocalizedText id="PO2.Help.Primitive.Stairs.Description" /></p>
        </>
    ),

    // Primitive type 7: Sphere.
    primitiveSphere: (
        <>
        </>
    ),

    // Primitive type 8: Dome.
    primitiveDome: (
        <>
        </>
    ),

    // AssetConversionTarget interaction 5.
    convertTarget: (
        <>
        </>
    ),

    // CombineSourceTarget interaction 6; Add Parts > From PO.
    combineSource: (
        <>
        </>
    ),

    // Object and Edit operation choices.
    // Object workspace, gizmoMode 0, not deforming.
    objectMove: (
        <>
        </>
    ),

    // Object workspace, gizmoMode 1, not deforming.
    objectRotate: (
        <>
        </>
    ),

    // Object workspace, gizmoMode 2; whole-object scale is absolute.
    objectScale: (
        <>
        </>
    ),

    // Object Mirror axis row.
    objectMirror: (
        <>
        </>
    ),

    // Transform target Object, or Edit Object scope.
    objectScope: (
        <>
        </>
    ),

    // Transform target ActivePart, or Edit Part scope.
    partScope: (
        <>
        </>
    ),

    // deformMode with gizmoMode 0.
    deformMove: (
        <>
        </>
    ),

    // deformMode with gizmoMode 2.
    deformScale: (
        <>
        </>
    ),

    // Deform scaling around selection center.
    deformPivotSelection: (
        <>
        </>
    ),

    // Deform scaling around saved pivot.
    deformPivotSaved: (
        <>
        </>
    ),

    // selectedHasDeformation; apply/reset workflow.
    unappliedDeformation: (
        <>
        </>
    ),

    // meshEditRequiresBake; editing blocked until apply/reset.
    editRequiresDeform: (
        <>
        </>
    ),

    // meshEditSelectionMode: vertex selection.
    editVertices: (
        <>
        </>
    ),

    // meshEditSelectionMode: edge selection.
    editEdges: (
        <>
        </>
    ),

    // meshTransformMode 0.
    editMove: (
        <>
        </>
    ),

    // meshTransformMode 1.
    editRotate: (
        <>
        </>
    ),

    // meshTransformMode 2; relative selection scale.
    editScale: (
        <>
        </>
    ),

    // Set/recenter pivot and meshTransformPivot options; no separate targeting mode.
    meshPivot: (
        <>
        </>
    ),

    // Pointer interactions and operation phases.
    // GizmoDrag interaction 1; shared supplement, retain operation help.
    gizmoDrag: (
        <>
        </>
    ),

    // BoxSelection interaction 2; retain originating workspace help.
    boxSelection: (
        <>
        </>
    ),

    // VertexAlignmentTarget interaction 7; vertexAlignmentAxis supplies X/Y/Z.
    alignTarget: (
        <>
        </>
    ),

    // VertexSlideTarget interaction 8.
    slideTarget: (
        <>
        </>
    ),

    // VertexSlideDrag interaction 9.
    slideDrag: (
        <>
        </>
    ),

    // LoopCutTarget interaction 10.
    loopCutTarget: (
        <>
        </>
    ),

    // LoopCutPosition interaction 11.
    loopCutPosition: (
        <>
        </>
    ),

    // SurfaceCutTarget interaction 12.
    surfaceCutTarget: (
        <>
        </>
    ),

    // SurfaceCutPosition interaction 13.
    surfaceCutPosition: (
        <>
        </>
    ),

    // OrientationTarget interaction 14; selecting an edge.
    axisPick: (
        <>
        </>
    ),

    // Axis picker with canConfirmAxisPicking true.
    axisPickReady: (
        <>
        </>
    ),

    // FaceOperationSelection 15 plus faceExtrudeSelecting.
    extrudeSelect: (
        <>
        </>
    ),

    // FaceOperationSelection 15 plus faceExtrudePositioning.
    extrudePosition: (
        <>
        </>
    ),

    // FaceOperationSelection 15 plus faceInsetSelecting.
    insetSelect: (
        <>
        </>
    ),

    // FaceOperationSelection 15 plus faceInsetPositioning.
    insetPosition: (
        <>
        </>
    ),

    // Texturing options (supplements within Texturing).
    // selectedSurfaceAreaId 0: Base Area.
    surfaceBase: (
        <>
        </>
    ),

    // selectedSurfaceAreaId > 0: named/numbered area.
    surfaceCustom: (
        <>
        </>
    ),

    // textureSettingsOpen; scalar settings independent of mapping.
    materialSettings: (
        <>
        </>
    ),

    // mappingMode 0; while material settings expanded.
    mappingOriginal: (
        <>
        </>
    ),

    // mappingMode 1; while material settings expanded.
    mappingRepeat: (
        <>
        </>
    ),

    // mappingMode 2; while material settings expanded.
    mappingStretch: (
        <>
        </>
    ),

    // Color-mask modes and override inputs; describe all three choices here.
    colorMasks: (
        <>
            <p><LocalizedText id="PO2.Help.Texturing.ColorMasks.Behavior" /></p>
        </>
    ),

    // Show decals, netlanes, and surfaces; enabled/disabled choices.
    projections: (
        <>
        </>
    ),

    // Material management.
    // managementView 2 before authoring form is active; folder/scan.
    materialImport: (
        <>
        </>
    ),

    // managementView 2 with formActive in MaterialAuthoringView.
    materialImportForm: (
        <>
        </>
    ),

    // managementView 3; edit existing user material.
    materialEdit: (
        <>
        </>
    ),

    // MaterialLibraryWorkspace.deleteConfirming.
    materialDelete: (
        <>
        </>
    ),

    // MaterialAuthoringView.creatingCategory.
    materialCategoryCreate: (
        <>
        </>
    ),

    // MaterialPreviewTuner.expanded.
    materialPreviewLighting: (
        <>
        </>
    ),

    // materialImportBusy; scan/import/edit in progress.
    materialBusy: (
        <>
        </>
    ),

    // Shared controls and exceptional visible states.
    // ObjectNameRow.renameActive.
    renameObject: (
        <>
        </>
    ),

    // PartNavigation.renaming.
    renamePart: (
        <>
        </>
    ),

    // TransformToolbarControls.openPopover === axes; Global/Local/custom choices.
    axisOptions: (
        <>
        </>
    ),

    // TransformToolbarControls.openPopover === snap.
    snapOptions: (
        <>
        </>
    ),

    // snapSettingsExpanded within Snap popover.
    snapIncrements: (
        <>
        </>
    ),

    // selectedObjectLocked; supplement, not a different workspace.
    lockedObject: (
        <>
        </>
    ),

    // SelectWorkspace recoveryStatus nonempty; recovery panel visible.
    recovery: (
        <>
        </>
    ),

    // historyBusy; undo/redo work in progress.
    historyBusy: (
        <>
        </>
    ),

    // deformationCommitBusy; apply/reset commit in progress.
    deformationBusy: (
        <>
        </>
    ),

    // topologyEditBusy; mesh mutation in progress.
    topologyBusy: (
        <>
        </>
    ),

    // Help-local Performance Diagnostics view; live diagnostics remain separate.
    performance: (
        <>
        </>
    ),

};

export type HelpTopic = keyof typeof helpContent;
