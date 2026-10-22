import { usePO2Localization } from "@/l10n/localization";
import { TwoColumnRow } from "@/components/common/two-column-row";
import { MenuButton as Button } from "@/components/common/menu-button";
import { NumericDeltaInput, NumericInput } from "@/components/common/numeric-input";
import { AxisVectorFields } from "@/components/selected-object/axis-vector-fields";
import typography from "@/styles/typography.module.scss";
import layout from "@/styles/layout.module.scss";

type ObjectWorkspaceProps = {
    selectedObjectLocked: boolean;
    canDuplicateSelected: boolean;
    combineSourceActive: boolean;
    canSnapToTerrain: boolean;
    deformMode: boolean;
    mirrorMode: boolean;
    canMirrorSelected: boolean;
    mirrorRequiresBake: boolean;
    mirrorStatus: string;
    deformationPivot: number;
    deformationSelectionCount: number;
    deformationStatus: string;
    canBakeDeformation: boolean;
    selectedHasDeformation: boolean;
    transformRequiresBake: boolean;
    gizmoMode: number;
    transformTarget: number;
    numericDeltaDraftKey: string;
    selectedObjectDraftKey: string;
    scaleX: number;
    scaleY: number;
    scaleZ: number;
    onAction: (action: string, ...args: number[]) => void;
};

export const ObjectWorkspace = (props: ObjectWorkspaceProps) => {
    const { t } = usePO2Localization();
    const {
        selectedObjectLocked,
        canDuplicateSelected,
        combineSourceActive,
        canSnapToTerrain,
        deformMode,
        mirrorMode,
        canMirrorSelected,
        mirrorRequiresBake,
        mirrorStatus,
        deformationPivot,
        deformationSelectionCount,
        deformationStatus,
        canBakeDeformation,
        selectedHasDeformation,
        transformRequiresBake,
        gizmoMode,
        transformTarget,
        numericDeltaDraftKey,
        selectedObjectDraftKey,
        scaleX,
        scaleY,
        scaleZ,
        onAction,
    } = props;
    const disabled = selectedObjectLocked || combineSourceActive;
    const numericDisabled = disabled || (!deformMode && transformRequiresBake) || (deformMode && deformationSelectionCount === 0);
    const absoluteScale = !deformMode && gizmoMode === 2 && transformTarget === 0;
    const values = { X: scaleX, Y: scaleY, Z: scaleZ };
    return (
        <div className={layout.stack}>
            <div className={layout.divider} />
            <div>{t("PO2.Object.Transform.Mode.Label")}</div>
            <div className={layout.actionRow}>
                <Button
                    selected={!deformMode && !mirrorMode && gizmoMode === 0}
                    disabled={disabled || transformRequiresBake}
                    onSelect={() => onAction("setTranslateMode")}
                >
                    {t("PO2.Common.Move")}
                </Button>
                <Button
                    selected={!deformMode && !mirrorMode && gizmoMode === 1}
                    disabled={disabled || transformRequiresBake}
                    onSelect={() => onAction("setRotateMode")}
                >
                    {t("PO2.Common.Rotate")}
                </Button>
            </div>
            <div className={layout.actionRow}>
                <Button
                    selected={!deformMode && !mirrorMode && gizmoMode === 2}
                    disabled={disabled || transformRequiresBake}
                    onSelect={() => onAction("setScaleMode")}
                >
                    {t("PO2.Common.Scale")}
                </Button>
                <Button
                    selected={deformMode}
                    disabled={disabled}
                    onSelect={() => onAction("setDeformMode")}
                >
                    {t("PO2.Object.Deformation.Mode.Label")}
                </Button>
            </div>
            <div className={layout.actionRow}>
                <Button
                    selected={mirrorMode}
                    disabled={disabled}
                    onSelect={() => onAction("setMirrorMode")}
                >
                    {t("PO2.Object.Mirror.Mode.Label")}
                </Button>
                {deformMode ? <div aria-hidden="true" /> : (
                    <Button
                        disabled={disabled || transformRequiresBake || !canSnapToTerrain}
                        onSelect={() => onAction("snapToTerrain")}
                    >
                        {t("PO2.Object.TerrainAlignment.Action")}
                    </Button>
                )}
            </div>
            {mirrorMode && (
                <>
                    <div>{t("PO2.Object.Mirror.Mode.Label")}</div>
                    <div className={layout.actionRow}>
                        {(["X", "Y", "Z"] as const).map((axis, index) => (
                            <Button
                                key={axis}
                                disabled={disabled || !canMirrorSelected}
                                onSelect={() => onAction("mirrorSelected", index)}
                            >
                                {axis}
                            </Button>
                        ))}
                    </div>
                    {mirrorRequiresBake && (
                        <div className={typography.small}>{t("PO2.Object.Mirror.Disabled.DeformationPending")}</div>
                    )}
                </>
            )}
            {deformMode && (
                <>
                    <div>{t("PO2.Object.Deformation.Mode.Label")}</div>
                    <div className={layout.actionRow}>
                        <Button
                            selected={gizmoMode === 0}
                            disabled={disabled}
                            onSelect={() => onAction("setDeformationMoveMode")}
                        >
                            {t("PO2.Object.Deformation.Move.Action")}
                        </Button>
                        <Button
                            selected={gizmoMode === 2}
                            disabled={disabled}
                            onSelect={() => onAction("setDeformationScaleMode")}
                        >
                            {t("PO2.Object.Deformation.Scale.Action")}
                        </Button>
                    </div>
                    {gizmoMode === 2 && (<>
                        <div>{t("PO2.Object.Deformation.Pivot.Title")}</div>
                        <div className={layout.actionRow}>
                            <Button
                                selected={deformationPivot === 0}
                                disabled={disabled}
                                onSelect={() => onAction("setDeformationPivot", 0)}
                            >
                                {t("PO2.Object.Deformation.Pivot.Choice.SelectionCenter")}
                            </Button>
                            <Button
                                selected={deformationPivot === 1}
                                disabled={disabled}
                                onSelect={() => onAction("setDeformationPivot", 1)}
                            >
                                {t("PO2.Object.Deformation.Pivot.Choice.SavedPivot")}
                            </Button>
                        </div>
                    </>)}
                </>
            )}
            {!mirrorMode && (
                <>
                    <div>
                        {absoluteScale
                            ? t("PO2.Object.Scale.Local.Label")
                            : t(gizmoMode === 0 ? "PO2.Object.Move.Delta.Label" : gizmoMode === 1 ? "PO2.Object.Rotate.Delta.Label" : "PO2.Object.Scale.Delta.Label")}
                    </div>
                    <AxisVectorFields>
                        {(axis) =>
                            absoluteScale ? (
                                <NumericInput
                                    value={values[axis]}
                                    digits={3}
                                    minimum={0.01}
                                    resetKey={selectedObjectDraftKey}
                                    ariaLabel={t("PO2.Object.Scale.Axis.Label", { axis: axis })}
                                    disabled={numericDisabled}
                                    onCommit={(value) => onAction(`setScale${axis}`, value)}
                                />
                            ) : (
                                <NumericDeltaInput
                                    digits={gizmoMode === 1 ? 2 : 3}
                                    resetKey={`${numericDeltaDraftKey}:${gizmoMode}`}
                                    ariaLabel={t(gizmoMode === 0 ? "PO2.Object.Move.AxisDelta.AccessibleLabel" : gizmoMode === 1 ? "PO2.Object.Rotate.AxisDelta.AccessibleLabel" : "PO2.Object.Scale.AxisDelta.AccessibleLabel", { axis })}
                                    disabled={numericDisabled}
                                    onCommit={(value) =>
                                        onAction(
                                            `${gizmoMode === 0 ? "moveSelected" : gizmoMode === 1 ? "rotateSelected" : "setScale"}${axis}`,
                                            value,
                                        )
                                    }
                                />
                            )
                        }
                    </AxisVectorFields>
                </>
            )}
            {(deformMode || selectedHasDeformation) && (
                <>
                    <div className={layout.actionRow}>
                        <Button
                            disabled={!canBakeDeformation || disabled}
                            onSelect={() => onAction("bakeDeformation")}
                        >
                            {t("PO2.Object.Deformation.Apply.Action")}
                        </Button>
                        <Button
                            disabled={!canBakeDeformation || disabled}
                            onSelect={() => onAction("resetDeformation")}
                        >
                            {t("PO2.Object.Deformation.Reset.Action")}
                        </Button>
                    </div>
                    {transformRequiresBake && <div className={typography.small}>{t("PO2.Object.Transform.Disabled.DeformationPending")}</div>}
                    {selectedHasDeformation && !canBakeDeformation && (
                        <Button
                            disabled={disabled}
                            onSelect={() => onAction("editExistingDeformation")}
                        >
                            {t("PO2.Object.Deformation.SelectCage.Action")}
                        </Button>
                    )}
                    {deformationStatus && (
                        <div className={typography.small}>{deformationStatus}</div>
                    )}
                </>
            )}
            <div className={layout.divider} />
            <div>{t("PO2.Object.Actions.Title")}</div>
            <TwoColumnRow>
                <Button
                    disabled={!canDuplicateSelected || combineSourceActive}
                    onSelect={() => onAction("duplicateSelected")}
                >
                    {t("PO2.Object.Duplicate.Action")}
                </Button>
                <Button disabled={combineSourceActive} onSelect={() => onAction("copySelected")}>
                    {t("PO2.Object.Copy.Action")}
                </Button>
            </TwoColumnRow>
            <TwoColumnRow>
                <Button
                    disabled={combineSourceActive}
                    selected={selectedObjectLocked}
                    onSelect={() => onAction("toggleSelectedLocked")}
                >
                    {selectedObjectLocked ? t("PO2.Object.Unlock.Action") : t("PO2.Object.Lock.Action")}
                </Button>
                <Button disabled={disabled} onSelect={() => onAction("deleteSelected")}>
                    {t("PO2.Object.Delete.Action")}
                </Button>
            </TwoColumnRow>
            {mirrorMode && mirrorStatus && (
                <div className={typography.small} role="status" aria-live="polite">{mirrorStatus}</div>
            )}
        </div>
    );
};
