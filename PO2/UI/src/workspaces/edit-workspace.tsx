import { usePO2Localization } from "@/l10n/localization";
import { TwoColumnRow } from "@/components/common/two-column-row";
import { AxisVectorFields } from "@/components/selected-object/axis-vector-fields";
import selectedStyles from "@/components/selected-object/selected-object.module.scss";
import typography from "@/styles/typography.module.scss";
import inputStyles from "@/components/common/input.module.scss";
import { MenuButton as Button } from "@/components/common/menu-button";
import { NumericDeltaInput, NumericInput } from "@/components/common/numeric-input";
import styles from "@/styles/content.module.scss";
import layout from "@/styles/layout.module.scss";

type EditWorkspaceProps = {
    canDeleteVertices: boolean;
    canDissolveVertices: boolean;
    dissolveVerticesUnavailableReason: string;
    deleteVerticesUnavailableReason: string;
    topologyEditStatus: string;
    requiresBake: boolean;
    canBakeDeformation: boolean;
    deformationStatus: string;
    vertexSelectionMode: boolean;
    selectedVertexCount: number;
    canApplyNumericMove: boolean;
    transformMode: number;
    transformPivot: number;
    currentPartScope: boolean;
    canSetPivot: boolean;
    pivotUsesSelection: boolean;
    numericDeltaDraftKey: string;
    vertexAlignmentAxis: number;
    vertexSlideTargeting: boolean;
    vertexSlideDragging: boolean;
    canLoopCut: boolean;
    loopCutTargeting: boolean;
    loopCutPositioning: boolean;
    meshCutCount: number;
    canSurfaceCut: boolean;
    surfaceCutTargeting: boolean;
    surfaceCutPositioning: boolean;
    canExtrudeFaces: boolean;
    canInsetFaces: boolean;
    faceSelectionActive: boolean;
    faceExtrudeSelecting: boolean;
    faceExtrudePositioning: boolean;
    canContinueFaceExtrude: boolean;
    canCommitFaceExtrude: boolean;
    faceExtrudeDistance: number;
    faceInsetSelecting: boolean;
    faceInsetPositioning: boolean;
    canContinueFaceInset: boolean;
    canCommitFaceInset: boolean;
    faceInsetAmount: number;
    selectedFaceCount: number;
    canSubdivideEdges: boolean;
    subdivideEdgesUnavailableReason: string;
    canDissolveLoop: boolean;
    canDissolveSurfaceCut: boolean;
    canMergeVertices: boolean;
    onAction: (action: string, ...args: number[]) => void;
};

type InteractionPromptProps = {
    heading?: string;
    message: string;
    cancelAction: string;
    onAction: (action: string) => void;
};

const InteractionPrompt = ({ heading, message, cancelAction, onAction }: InteractionPromptProps) => {
    const { t } = usePO2Localization();
    return (
        <div className={styles.interactionPrompt}>
            <div className={styles.faceSelectionCopy}>
                {heading && <strong className={styles.interactionHeading} role="status" aria-live="polite">{heading}</strong>}
                <span>{message}</span>
            </div>
            <Button onSelect={() => onAction(cancelAction)}>{t("PO2.Common.Cancel")}</Button>
        </div>
    );
};

export const EditWorkspace = ({
    canDeleteVertices,
    canDissolveVertices,
    dissolveVerticesUnavailableReason,
    deleteVerticesUnavailableReason,
    topologyEditStatus,
    requiresBake,
    canBakeDeformation,
    deformationStatus,
    vertexSelectionMode,
    selectedVertexCount,
    canApplyNumericMove,
    transformMode,
    transformPivot,
    currentPartScope,
    canSetPivot,
    pivotUsesSelection,
    numericDeltaDraftKey,
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
    onAction,
}: EditWorkspaceProps) => {
    const { t } = usePO2Localization();
    const vertexSlideActive = vertexSlideTargeting || vertexSlideDragging;
    const loopCutActive = loopCutTargeting || loopCutPositioning;
    const surfaceCutActive = surfaceCutTargeting || surfaceCutPositioning;
    const cutActive = loopCutActive || surfaceCutActive;
    const topologyInteractionActive = cutActive || faceSelectionActive;
    const faceExtrudeActive = faceExtrudeSelecting || faceExtrudePositioning;
    const faceInsetActive = faceInsetSelecting || faceInsetPositioning;
    const faceOperationSelecting = faceExtrudeSelecting || faceInsetSelecting;
    const faceOperationPositioning = faceExtrudePositioning || faceInsetPositioning;
    const faceOperationIsInset = faceInsetSelecting || faceInsetPositioning;
    const canContinueFaceOperation = faceOperationIsInset
        ? canContinueFaceInset
        : canContinueFaceExtrude;
    const canCommitFaceOperation = faceOperationIsInset ? canCommitFaceInset : canCommitFaceExtrude;
    const faceOperationValue = faceOperationIsInset ? faceInsetAmount : faceExtrudeDistance;

    return (
        <div className={layout.stack}>
            {requiresBake && (
                <div className={layout.stack}>
                    <div className={typography.small}>{t("PO2.Edit.Deformation.RequiredNotice")}</div>
                    <div className={layout.actionRow}>
                        <Button
                            disabled={!canBakeDeformation}
                            onSelect={() => onAction("bakeMeshDeformation")}
                        >
                            {t("PO2.Edit.Deformation.Apply.Action")}
                        </Button>
                        <Button
                            disabled={!canBakeDeformation}
                            onSelect={() => onAction("resetMeshDeformation")}
                        >
                            {t("PO2.Edit.Deformation.Reset.Action")}
                        </Button>
                    </div>
                    {deformationStatus && (
                        <div className={typography.small}>{deformationStatus}</div>
                    )}
                </div>
            )}

            <div>{t("PO2.Common.Select")}</div>
            <div className={layout.actionRow}>
                <Button
                    onSelect={() => onAction("selectAllMeshElements")}
                    disabled={
                        vertexAlignmentAxis >= 0 || vertexSlideActive || topologyInteractionActive
                    }
                >
                    {t("PO2.Common.All")}
                </Button>
                <Button
                    onSelect={() => onAction("selectConnectedMeshElements")}
                    disabled={
                        vertexAlignmentAxis >= 0 || vertexSlideActive || topologyInteractionActive
                    }
                >
                    {t("PO2.Edit.Selection.Connected.Action")}
                </Button>
                <Button
                    onSelect={() => onAction("invertMeshSelection")}
                    disabled={
                        vertexAlignmentAxis >= 0 || vertexSlideActive || topologyInteractionActive
                    }
                >
                    {t("PO2.Edit.Selection.Invert.Action")}
                </Button>
            </div>

            <div className={layout.divider} />
            <div>{t("PO2.Edit.Selection.Title")}</div>
            <div className={layout.actionRow}>
                {[t("PO2.Common.Move"), t("PO2.Common.Rotate")].map((label, mode) => (
                    <Button
                        key={mode}
                        selected={transformMode === mode}
                        disabled={requiresBake || !canApplyNumericMove}
                        onSelect={() => onAction("setMeshTransformMode", mode)}
                    >
                        {label}
                    </Button>
                ))}
            </div>
            <div className={layout.actionRow}>
                <Button
                    selected={transformMode === 2}
                    disabled={requiresBake || !canApplyNumericMove}
                    onSelect={() => onAction("setMeshTransformMode", 2)}
                >
                    {t("PO2.Common.Scale")}
                </Button>
                <Button
                    selected={vertexSlideActive}
                    onSelect={() =>
                        onAction(vertexSlideActive ? "cancelVertexSlide" : "beginVertexSlide")
                    }
                    disabled={
                        requiresBake ||
                        !vertexSelectionMode ||
                        (selectedVertexCount !== 1 && !vertexSlideActive) ||
                        topologyInteractionActive
                    }
                >
                    {vertexSlideActive ? t("PO2.Edit.VertexSlide.Cancel.Action") : t("PO2.Edit.VertexSlide.Start.Action")}
                </Button>
            </div>

            {transformMode !== 0 && (
                <div className={`${layout.actionRow} ${layout.actionRowTight}`}>
                    <Button
                        selected={transformPivot === 0}
                        disabled={requiresBake || !canApplyNumericMove}
                        onSelect={() => onAction("setMeshTransformPivot", 0)}
                    >
                        {t("PO2.Edit.TransformPivot.Choice.SelectionCenter")}
                    </Button>
                    <Button
                        selected={transformPivot === 1}
                        disabled={requiresBake || !canApplyNumericMove}
                        onSelect={() => onAction("setMeshTransformPivot", 1)}
                    >
                        {currentPartScope ? t("PO2.Edit.TransformPivot.Choice.PartPivot") : t("PO2.Edit.TransformPivot.Choice.ObjectPivot")}
                    </Button>
                </div>
            )}

            <div className={layout.stack}>
                <div>
                    {transformMode === 0
                        ? t("PO2.Edit.Move.Delta.Label")
                        : transformMode === 1
                            ? t("PO2.Edit.Rotate.Delta.Label")
                            : t("PO2.Edit.Scale.Delta.Label")}
                </div>
                <AxisVectorFields>
                    {(axis) => (
                        <NumericDeltaInput
                            className={inputStyles.centered}
                            digits={3}
                            resetKey={numericDeltaDraftKey}
                            ariaLabel={t(transformMode === 0 ? "PO2.Edit.Move.AxisDelta.AccessibleLabel" : transformMode === 1 ? "PO2.Edit.Rotate.AxisDelta.AccessibleLabel" : "PO2.Edit.Scale.AxisDelta.AccessibleLabel", { axis })}
                            disabled={requiresBake || !canApplyNumericMove}
                            onCommit={(value) =>
                                onAction(
                                    `${transformMode === 0 ? "moveSelected" : transformMode === 1 ? "rotateSelected" : "setScale"}${axis}`,
                                    value,
                                )
                            }
                        />
                    )}
                </AxisVectorFields>
                {transformMode === 2 && (
                    <div className={styles.valueRow}>
                        <span>{t("PO2.Edit.Scale.UniformDelta.Label")}</span>
                        <NumericDeltaInput
                            className={inputStyles.centered}
                            digits={3}
                            resetKey={numericDeltaDraftKey}
                            ariaLabel={t("PO2.Edit.Scale.UniformDelta.AccessibleLabel")}
                            disabled={requiresBake || !canApplyNumericMove}
                            onCommit={(value) => onAction("scaleMeshSelectionUniform", value)}
                        />
                    </div>
                )}
            </div>

            {vertexSlideTargeting && (
                <InteractionPrompt
                    message={t("PO2.Edit.VertexSlide.Target.Instruction")}
                    cancelAction="cancelVertexSlide"
                    onAction={onAction}
                />
            )}

            <div className={styles.sectionHeader}>{t("PO2.Edit.Alignment.Title")}</div>

            <div className={layout.actionRow}>
                <Button
                    selected={vertexAlignmentAxis === 0}
                    onSelect={() => onAction("alignVerticesX")}
                    disabled={
                        requiresBake ||
                        !vertexSelectionMode ||
                        selectedVertexCount === 0 ||
                        vertexSlideActive ||
                        topologyInteractionActive
                    }
                >
                    <span className={selectedStyles.axisX}>X</span>
                </Button>
                <Button
                    selected={vertexAlignmentAxis === 1}
                    onSelect={() => onAction("alignVerticesY")}
                    disabled={
                        requiresBake ||
                        !vertexSelectionMode ||
                        selectedVertexCount === 0 ||
                        vertexSlideActive ||
                        topologyInteractionActive
                    }
                >
                    <span className={selectedStyles.axisY}>Y</span>
                </Button>
                <Button
                    selected={vertexAlignmentAxis === 2}
                    onSelect={() => onAction("alignVerticesZ")}
                    disabled={
                        requiresBake ||
                        !vertexSelectionMode ||
                        selectedVertexCount === 0 ||
                        vertexSlideActive ||
                        topologyInteractionActive
                    }
                >
                    <span className={selectedStyles.axisZ}>Z</span>
                </Button>
            </div>

            {vertexAlignmentAxis >= 0 && (
                <InteractionPrompt
                    message={t("PO2.Edit.Alignment.Target.Instruction", { axis: vertexAlignmentAxis === 0 ? "X" : vertexAlignmentAxis === 1 ? "Y" : "Z" })}
                    cancelAction="cancelVertexAlignment"
                    onAction={onAction}
                />
            )}

            <div className={layout.divider} />
            <div>{t("PO2.Edit.Pivot.Title")}</div>
            <TwoColumnRow>
                <Button
                    onSelect={() => onAction("setMeshPivot")}
                    disabled={!canSetPivot}
                >
                    {pivotUsesSelection ? t("PO2.Edit.Pivot.Set.Action") : t("PO2.Edit.Pivot.Recenter.Action")}
                </Button>
            </TwoColumnRow>

            <div className={layout.divider} />
            <div>{t("PO2.Edit.Topology.Title")}</div>
            <div className={layout.actionRow}>
                <Button
                    selected={loopCutActive}
                    onSelect={() => onAction(loopCutActive ? "cancelLoopCut" : "beginLoopCut")}
                    disabled={
                        requiresBake ||
                        vertexAlignmentAxis >= 0 ||
                        vertexSlideActive ||
                        surfaceCutActive ||
                        faceSelectionActive ||
                        (!loopCutActive && !canLoopCut)
                    }
                >
                    {loopCutActive ? t("PO2.Edit.LoopCut.Cancel.Action") : t("PO2.Edit.LoopCut.Start.Action")}
                </Button>
                <Button
                    selected={surfaceCutActive}
                    onSelect={() =>
                        onAction(surfaceCutActive ? "cancelSurfaceCut" : "beginSurfaceCut")
                    }
                    disabled={
                        requiresBake ||
                        vertexAlignmentAxis >= 0 ||
                        vertexSlideActive ||
                        loopCutActive ||
                        faceSelectionActive ||
                        (!surfaceCutActive && !canSurfaceCut)
                    }
                >
                    {surfaceCutActive ? t("PO2.Edit.SurfaceCut.Cancel.Action") : t("PO2.Edit.SurfaceCut.Start.Action")}
                </Button>
            </div>
            <div className={layout.actionRow}>
                <Button
                    onSelect={() => onAction("dissolveLoop")}
                    disabled={
                        requiresBake ||
                        !canDissolveLoop ||
                        vertexAlignmentAxis >= 0 ||
                        vertexSlideActive ||
                        topologyInteractionActive
                    }
                >
                    {t("PO2.Edit.LoopCut.Dissolve.Action")}
                </Button>
                <Button
                    onSelect={() => onAction("dissolveSurfaceCut")}
                    disabled={
                        requiresBake ||
                        !canDissolveSurfaceCut ||
                        vertexAlignmentAxis >= 0 ||
                        vertexSlideActive ||
                        topologyInteractionActive
                    }
                >
                    {t("PO2.Edit.SurfaceCut.Dissolve.Action")}
                </Button>
            </div>
            <div className={layout.actionRow}>
                <Button
                    onSelect={() => onAction("subdivideEdges")}
                    disabled={requiresBake || !canSubdivideEdges}
                    title={
                        canSubdivideEdges
                            ? t("PO2.Edit.Subdivide.Tooltip")
                            : subdivideEdgesUnavailableReason
                    }
                >
                    {t("PO2.Edit.Subdivide.Action")}
                </Button>
                <Button
                    onSelect={() => onAction("mergeVertices")}
                    disabled={
                        requiresBake ||
                        !vertexSelectionMode ||
                        !canMergeVertices ||
                        vertexAlignmentAxis >= 0 ||
                        vertexSlideActive ||
                        topologyInteractionActive
                    }
                >
                    {t("PO2.Edit.Merge.Action")}
                </Button>
            </div>
            <div className={layout.actionRow}>
                <Button
                    selected={faceExtrudeActive}
                    onSelect={() => onAction("beginFaceExtrude")}
                    disabled={
                        requiresBake ||
                        faceSelectionActive ||
                        vertexAlignmentAxis >= 0 ||
                        vertexSlideActive ||
                        cutActive ||
                        !canExtrudeFaces
                    }
                >
                    {t("PO2.Edit.Extrude.Action")}
                </Button>
                <Button
                    selected={faceInsetActive}
                    onSelect={() => onAction("beginFaceInset")}
                    disabled={
                        requiresBake ||
                        faceSelectionActive ||
                        vertexAlignmentAxis >= 0 ||
                        vertexSlideActive ||
                        cutActive ||
                        !canInsetFaces
                    }
                >
                    {t("PO2.Edit.Inset.Action")}
                </Button>
            </div>
            <div className={layout.actionRow}>
                <Button
                    onSelect={() => onAction("deleteVertices")}
                    disabled={requiresBake || !canDeleteVertices}
                    title={
                        canDeleteVertices
                            ? t("PO2.Edit.DeleteVertices.Tooltip")
                            : deleteVerticesUnavailableReason
                    }
                >
                    {t("PO2.Edit.DeleteVertices.Action")}
                </Button>
                <Button
                    onSelect={() => onAction("dissolveVertices")}
                    disabled={requiresBake || !canDissolveVertices}
                    title={
                        canDissolveVertices
                            ? t("PO2.Edit.DissolveVertices.Tooltip")
                            : dissolveVerticesUnavailableReason
                    }
                >
                    {t("PO2.Edit.DissolveVertices.Action")}
                </Button>
            </div>
            {!requiresBake && topologyEditStatus && <div className={typography.small}>{topologyEditStatus}</div>}

            {faceOperationSelecting && (
                <div className={styles.interactionPrompt}>
                    <div className={styles.faceSelectionCopy}>
                        <span>
                            {t(selectedFaceCount === 1 ? "PO2.Edit.FaceOperations.Selection.Singular" : "PO2.Edit.FaceOperations.Selection.Plural", { count: selectedFaceCount })}
                        </span>
                        <span className={typography.small}>{t("PO2.Edit.FaceOperations.Selection.Instruction")}</span>
                    </div>
                    <Button
                        disabled={!canContinueFaceOperation}
                        onSelect={() =>
                            onAction(
                                faceOperationIsInset ? "continueFaceInset" : "continueFaceExtrude",
                            )
                        }
                    >
                        {t("PO2.Edit.FaceOperations.Continue.Action")}
                    </Button>
                    <Button onSelect={() => onAction("cancelFaceOperation")}>{t("PO2.Common.Cancel")}</Button>
                </div>
            )}

            {faceOperationPositioning && (
                <div className={styles.numericEditor}>
                    <div className={styles.valueRow}>
                        <span>{faceOperationIsInset ? t("PO2.Edit.Inset.Distance.Label") : t("PO2.Edit.Extrude.Distance.Label")}</span>
                        <NumericInput
                            className={inputStyles.centered}
                            value={faceOperationValue}
                            digits={3}
                            resetKey={`${numericDeltaDraftKey}:${faceOperationIsInset ? "face-inset" : "face-extrude"}`}
                            ariaLabel={
                                faceOperationIsInset
                                    ? t("PO2.Edit.Inset.Distance.AccessibleLabel")
                                    : t("PO2.Edit.Extrude.Distance.AccessibleLabel")
                            }
                            minimum={faceOperationIsInset ? 0 : undefined}
                            onCommit={(value) =>
                                onAction(
                                    faceOperationIsInset
                                        ? "setFaceInsetAmount"
                                        : "setFaceExtrudeDistance",
                                    value,
                                )
                            }
                        />
                    </div>
                    <div className={layout.actionRow}>
                        <Button
                            onSelect={() =>
                                onAction(faceOperationIsInset ? "backFaceInset" : "backFaceExtrude")
                            }
                        >
                            {t("PO2.Common.Back")}
                        </Button>
                        <Button
                            disabled={!canCommitFaceOperation}
                            onSelect={() =>
                                onAction(
                                    faceOperationIsInset
                                        ? "confirmFaceInset"
                                        : "confirmFaceExtrude",
                                )
                            }
                        >
                            {t("PO2.Edit.FaceOperations.Confirm.Action")}
                        </Button>
                    </div>
                </div>
            )}

            {loopCutTargeting && (
                <InteractionPrompt
                    heading={t("PO2.Edit.Cut.Count.Summary", { count: meshCutCount })}
                    message={t("PO2.Edit.LoopCut.Target.Instruction")}
                    cancelAction="cancelLoopCut"
                    onAction={onAction}
                />
            )}

            {loopCutPositioning && (
                <InteractionPrompt
                    heading={t("PO2.Edit.Cut.Count.Summary", { count: meshCutCount })}
                    message={t("PO2.Edit.Cut.Position.Instruction")}
                    cancelAction="cancelLoopCut"
                    onAction={onAction}
                />
            )}

            {surfaceCutTargeting && (
                <InteractionPrompt
                    heading={t("PO2.Edit.Cut.Count.Summary", { count: meshCutCount })}
                    message={t("PO2.Edit.SurfaceCut.Target.Instruction")}
                    cancelAction="cancelSurfaceCut"
                    onAction={onAction}
                />
            )}

            {surfaceCutPositioning && (
                <InteractionPrompt
                    heading={t("PO2.Edit.Cut.Count.Summary", { count: meshCutCount })}
                    message={t("PO2.Edit.Cut.Position.Instruction")}
                    cancelAction="cancelSurfaceCut"
                    onAction={onAction}
                />
            )}

        </div>
    );
};
