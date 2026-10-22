import type { ComponentProps } from "react";
import { group } from "@/bindings/commands";
import {
    boxSelectionHeight$,
    boxSelectionLeft$,
    boxSelectionTop$,
    boxSelectionVisible$,
    boxSelectionWidth$,
    canApplyMeshNumericMove$,
    canCommitFaceExtrude$,
    canCommitFaceInset$,
    canContinueFaceExtrude$,
    canContinueFaceInset$,
    canDissolveLoop$,
    canDissolveSurfaceCut$,
    canExtrudeFaces$,
    canInsetFaces$,
    canLoopCut$,
    canMergeVertices$,
    canSetMeshPivot$,
    canSubdivideEdges$,
    canSurfaceCut$,
    faceExtrudeDistance$,
    faceExtrudePositioning$,
    faceExtrudeSelecting$,
    faceInsetAmount$,
    faceInsetPositioning$,
    faceInsetSelecting$,
    faceSelectionActive$,
    loopCutPositioning$,
    loopCutTargeting$,
    meshCutCount$,
    meshEditSelectionMode$,
    meshEditSelectionVersion$,
    meshEditVertexScope$,
    meshEditVisibilityMode$,
    meshPivotUsesSelection$,
    meshTransformMode$,
    meshTransformPivot$,
    selectedFaceCount$,
    selectedSurfaceCount$,
    selectedVertexCount$,
    subdivideEdgesUnavailableReason$,
    surfaceCutPositioning$,
    surfaceCutTargeting$,
    vertexAlignmentAxis$,
    vertexSlideDragging$,
    vertexSlideTargeting$,} from "@/bindings/mesh-edit";
import { useScopedValue } from "@/bindings/scoped-value";
import type { usePO2Localization } from "@/l10n/localization";
import { EditWorkspace } from "@/workspaces/edit-workspace";
import { trigger, useValue } from "cs2/api";

export const useMeshEditWorkspace = (
    meshControlsVisible: boolean,
    normalWorkspace: boolean,
    editorActive: boolean,
    message: ReturnType<typeof usePO2Localization>["message"],
) => {
    const selectedVertexCount = useScopedValue(selectedVertexCount$, meshControlsVisible);
    const meshEditSelectionVersion = useScopedValue(meshEditSelectionVersion$, meshControlsVisible);
    const meshTransformMode = useScopedValue(meshTransformMode$, meshControlsVisible);
    const meshTransformPivot = useScopedValue(meshTransformPivot$, meshControlsVisible);
    const canApplyMeshNumericMove = useScopedValue(canApplyMeshNumericMove$, meshControlsVisible);
    const canSetMeshPivot = useScopedValue(canSetMeshPivot$, meshControlsVisible);
    const meshPivotUsesSelection = useScopedValue(meshPivotUsesSelection$, meshControlsVisible);
    const selectedSurfaceCount = useScopedValue(selectedSurfaceCount$, normalWorkspace && editorActive);
    const meshEditSelectionMode = useScopedValue(meshEditSelectionMode$, meshControlsVisible);
    const vertexSelectionMode = meshEditSelectionMode === 0;
    const edgeSelectionMode = meshEditSelectionMode === 1;
    const boxSelectionVisible = useValue(boxSelectionVisible$);
    const boxSelectionLeft = useScopedValue(boxSelectionLeft$, boxSelectionVisible);
    const boxSelectionTop = useScopedValue(boxSelectionTop$, boxSelectionVisible);
    const boxSelectionWidth = useScopedValue(boxSelectionWidth$, boxSelectionVisible);
    const boxSelectionHeight = useScopedValue(boxSelectionHeight$, boxSelectionVisible);
    const meshEditVisibilityMode = useScopedValue(meshEditVisibilityMode$, meshControlsVisible);
    const meshEditVertexScope = useScopedValue(meshEditVertexScope$, meshControlsVisible);
    const vertexAlignmentAxis = useScopedValue(vertexAlignmentAxis$, meshControlsVisible);
    const vertexSlideTargeting = useScopedValue(vertexSlideTargeting$, meshControlsVisible);
    const vertexSlideDragging = useScopedValue(vertexSlideDragging$, meshControlsVisible);
    const canLoopCut = useScopedValue(canLoopCut$, meshControlsVisible);
    const loopCutTargeting = useScopedValue(loopCutTargeting$, meshControlsVisible);
    const loopCutPositioning = useScopedValue(loopCutPositioning$, meshControlsVisible);
    const meshCutCount = useScopedValue(meshCutCount$, meshControlsVisible);
    const canSurfaceCut = useScopedValue(canSurfaceCut$, meshControlsVisible);
    const surfaceCutTargeting = useScopedValue(surfaceCutTargeting$, meshControlsVisible);
    const surfaceCutPositioning = useScopedValue(surfaceCutPositioning$, meshControlsVisible);
    const canExtrudeFaces = useScopedValue(canExtrudeFaces$, meshControlsVisible);
    const canInsetFaces = useScopedValue(canInsetFaces$, meshControlsVisible);
    const faceSelectionActive = useScopedValue(faceSelectionActive$, meshControlsVisible);
    const faceExtrudeSelecting = useScopedValue(faceExtrudeSelecting$, meshControlsVisible);
    const faceExtrudePositioning = useScopedValue(faceExtrudePositioning$, meshControlsVisible);
    const canContinueFaceExtrude = useScopedValue(canContinueFaceExtrude$, meshControlsVisible);
    const canCommitFaceExtrude = useScopedValue(canCommitFaceExtrude$, meshControlsVisible);
    const faceExtrudeDistance = useScopedValue(faceExtrudeDistance$, meshControlsVisible);
    const faceInsetSelecting = useScopedValue(faceInsetSelecting$, meshControlsVisible);
    const faceInsetPositioning = useScopedValue(faceInsetPositioning$, meshControlsVisible);
    const canContinueFaceInset = useScopedValue(canContinueFaceInset$, meshControlsVisible);
    const canCommitFaceInset = useScopedValue(canCommitFaceInset$, meshControlsVisible);
    const faceInsetAmount = useScopedValue(faceInsetAmount$, meshControlsVisible);
    const selectedFaceCount = useScopedValue(selectedFaceCount$, meshControlsVisible);
    const canSubdivideEdges = useScopedValue(canSubdivideEdges$, meshControlsVisible);
    const subdivideEdgesUnavailableReason = message(useScopedValue(subdivideEdgesUnavailableReason$, meshControlsVisible));
    const canDissolveLoop = useScopedValue(canDissolveLoop$, meshControlsVisible);
    const canDissolveSurfaceCut = useScopedValue(canDissolveSurfaceCut$, meshControlsVisible);
    const canMergeVertices = useScopedValue(canMergeVertices$, meshControlsVisible);
    const loopCutActive = loopCutTargeting || loopCutPositioning;
    const surfaceCutActive = surfaceCutTargeting || surfaceCutPositioning;
    const cutActive = loopCutActive || surfaceCutActive;
    const topologyInteractionActive = cutActive || faceSelectionActive;
    const vertexSlideActive = vertexSlideTargeting || vertexSlideDragging;

    return {
        selectedVertexCount,
        meshEditSelectionVersion,
        meshTransformMode,
        meshTransformPivot,
        canApplyMeshNumericMove,
        canSetMeshPivot,
        meshPivotUsesSelection,
        selectedSurfaceCount,
        meshEditSelectionMode,
        vertexSelectionMode,
        edgeSelectionMode,
        boxSelectionVisible,
        boxSelectionLeft,
        boxSelectionTop,
        boxSelectionWidth,
        boxSelectionHeight,
        meshEditVisibilityMode,
        meshEditVertexScope,
        vertexAlignmentAxis,
        vertexSlideTargeting,
        vertexSlideDragging,
        canLoopCut,
        loopCutTargeting,
        loopCutPositioning,
        meshCutCount,
        canSurfaceCut,
        surfaceCutTargeting,
        surfaceCutPositioning,
        canExtrudeFaces,
        canInsetFaces,
        faceSelectionActive,
        faceExtrudeSelecting,
        faceExtrudePositioning,
        canContinueFaceExtrude,
        canCommitFaceExtrude,
        faceExtrudeDistance,
        faceInsetSelecting,
        faceInsetPositioning,
        canContinueFaceInset,
        canCommitFaceInset,
        faceInsetAmount,
        selectedFaceCount,
        canSubdivideEdges,
        subdivideEdgesUnavailableReason,
        canDissolveLoop,
        canDissolveSurfaceCut,
        canMergeVertices,
        loopCutActive,
        surfaceCutActive,
        cutActive,
        topologyInteractionActive,
        vertexSlideActive,
    };
};

type EditWorkspaceContainerProps = {
    state: ReturnType<typeof useMeshEditWorkspace>;
    canDissolveVertices: ComponentProps<typeof EditWorkspace>["canDissolveVertices"];
    dissolveVerticesUnavailableReason: ComponentProps<typeof EditWorkspace>["dissolveVerticesUnavailableReason"];
    canDeleteVertices: ComponentProps<typeof EditWorkspace>["canDeleteVertices"];
    deleteVerticesUnavailableReason: ComponentProps<typeof EditWorkspace>["deleteVerticesUnavailableReason"];
    topologyEditStatus: ComponentProps<typeof EditWorkspace>["topologyEditStatus"];
    meshEditRequiresBake: ComponentProps<typeof EditWorkspace>["requiresBake"];
    canBakeMeshDeformation: ComponentProps<typeof EditWorkspace>["canBakeDeformation"];
    deformationStatus: ComponentProps<typeof EditWorkspace>["deformationStatus"];
    numericDeltaDraftKey: ComponentProps<typeof EditWorkspace>["numericDeltaDraftKey"];
};

export const EditWorkspaceContainer = ({
    state,
    canDissolveVertices,
    dissolveVerticesUnavailableReason,
    canDeleteVertices,
    deleteVerticesUnavailableReason,
    topologyEditStatus,
    meshEditRequiresBake,
    canBakeMeshDeformation,
    deformationStatus,
    numericDeltaDraftKey,
}: EditWorkspaceContainerProps) => {
    const {
        vertexSelectionMode,
        selectedVertexCount,
        canApplyMeshNumericMove,
        meshTransformMode,
        meshTransformPivot,
        meshEditVertexScope,
        canSetMeshPivot,
        meshPivotUsesSelection,
        vertexAlignmentAxis,
        vertexSlideTargeting,
        vertexSlideDragging,
        canLoopCut,
        loopCutTargeting,
        loopCutPositioning,
        meshCutCount,
        canSurfaceCut,
        surfaceCutTargeting,
        surfaceCutPositioning,
        canExtrudeFaces,
        canInsetFaces,
        faceSelectionActive,
        faceExtrudeSelecting,
        faceExtrudePositioning,
        canContinueFaceExtrude,
        canCommitFaceExtrude,
        faceExtrudeDistance,
        faceInsetSelecting,
        faceInsetPositioning,
        canContinueFaceInset,
        canCommitFaceInset,
        faceInsetAmount,
        selectedFaceCount,
        canSubdivideEdges,
        subdivideEdgesUnavailableReason,
        canDissolveLoop,
        canDissolveSurfaceCut,
        canMergeVertices,
    } = state;

    return (
        <EditWorkspace
            canDissolveVertices={canDissolveVertices}
            dissolveVerticesUnavailableReason={dissolveVerticesUnavailableReason}
            canDeleteVertices={canDeleteVertices}
            deleteVerticesUnavailableReason={deleteVerticesUnavailableReason}
            topologyEditStatus={topologyEditStatus}
            requiresBake={meshEditRequiresBake}
            canBakeDeformation={canBakeMeshDeformation}
            deformationStatus={deformationStatus}
            vertexSelectionMode={vertexSelectionMode}
            selectedVertexCount={selectedVertexCount}
            canApplyNumericMove={canApplyMeshNumericMove}
            transformMode={meshTransformMode}
            transformPivot={meshTransformPivot}
            currentPartScope={meshEditVertexScope === 0}
            canSetPivot={canSetMeshPivot}
            pivotUsesSelection={meshPivotUsesSelection}
            numericDeltaDraftKey={numericDeltaDraftKey}
            vertexAlignmentAxis={vertexAlignmentAxis}
            vertexSlideTargeting={vertexSlideTargeting}
            vertexSlideDragging={vertexSlideDragging}
            canLoopCut={canLoopCut}
            loopCutTargeting={loopCutTargeting}
            loopCutPositioning={loopCutPositioning}
            meshCutCount={meshCutCount}
            canSurfaceCut={canSurfaceCut}
            surfaceCutTargeting={surfaceCutTargeting}
            surfaceCutPositioning={surfaceCutPositioning}
            canExtrudeFaces={canExtrudeFaces}
            canInsetFaces={canInsetFaces}
            faceSelectionActive={faceSelectionActive}
            faceExtrudeSelecting={faceExtrudeSelecting}
            faceExtrudePositioning={faceExtrudePositioning}
            canContinueFaceExtrude={canContinueFaceExtrude}
            canCommitFaceExtrude={canCommitFaceExtrude}
            faceExtrudeDistance={faceExtrudeDistance}
            faceInsetSelecting={faceInsetSelecting}
            faceInsetPositioning={faceInsetPositioning}
            canContinueFaceInset={canContinueFaceInset}
            canCommitFaceInset={canCommitFaceInset}
            faceInsetAmount={faceInsetAmount}
            selectedFaceCount={selectedFaceCount}
            canSubdivideEdges={canSubdivideEdges}
            subdivideEdgesUnavailableReason={subdivideEdgesUnavailableReason}
            canDissolveLoop={canDissolveLoop}
            canDissolveSurfaceCut={canDissolveSurfaceCut}
            canMergeVertices={canMergeVertices}
            onAction={(action, ...args) => trigger(
                group,
                action,
                ...args,
            )}
        />
    );
};
