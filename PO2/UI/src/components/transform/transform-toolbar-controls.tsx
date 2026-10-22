import { usePO2Localization } from "@/l10n/localization";
import { useReportHelpTopics } from "@/help/help-local-context";
import { CustomAxisControls } from "@/components/selected-object/custom-axis-controls";
import { Icon } from "cs2/ui";
import { useState } from "react";
import { ToolbarButton } from "@/components/common/toolbar-controls";
import toolbar from "@/shell/main-toolbar.module.scss";
import dividerIcon from "@/images/navigation/divider-vertical.svg";
import context from "@/shell/context-toolbar.module.scss";
import { ToolbarButton as Button } from "@/components/common/toolbar-controls";
import { ToolbarPopover } from "@/components/common/toolbar-popover";
import { NumericStepper } from "@/components/common/numeric-stepper";
import { MenuButton } from "@/components/common/menu-button";
import type { CustomOrientation } from "@/utils/ui-payloads";
import styles from "@/shell/toolbar-popover.module.scss";

type TransformToolbarControlsProps = {
    showTargetControls: boolean;
    selectedObjectId: number;
    canBeginAxisPicking: boolean;
    selectedObjectLocked: boolean;
    selectedPartIndex: number;
    transformTarget: number;
    transformOrientation: number;
    customOrientations: CustomOrientation[];
    activeCustomOrientation: CustomOrientation | null;
    snapMagnetEnabled: boolean;
    snapIncrementEnabled: boolean;
    snapPOVerticesEnabled: boolean;
    snapPOEdgesEnabled: boolean;
    snapGameSurfacesEnabled: boolean;
    moveSnapIncrement: number;
    precisionMoveSnapIncrement: number;
    rotationSnapIncrement: number;
    precisionRotationSnapIncrement: number;
    scaleSnapIncrement: number;
    precisionScaleSnapIncrement: number;
    onAction: (action: string, ...args: Array<string | number>) => void;
};

type IncrementInputProps = {
    value: number;
    digits: number;
    minimum: number;
    step: number;
    resetKey: string;
    ariaLabel: string;
    onChange: (value: number) => void;
};

const IncrementInput = ({
    value,
    digits,
    minimum,
    step,
    resetKey,
    ariaLabel,
    onChange,
}: IncrementInputProps) => (
    <NumericStepper
        value={value}
        digits={digits}
        minimum={minimum}
        resetKey={resetKey}
        ariaLabel={ariaLabel}
        onCommit={onChange}
        onDecrease={() => onChange(Math.max(minimum, Number((value - step).toFixed(digits))))}
        onIncrease={() => onChange(Number((value + step).toFixed(digits)))}
    />
);

export const TransformToolbarControls = ({
    showTargetControls,
    selectedObjectId,
    canBeginAxisPicking,
    selectedObjectLocked,
    selectedPartIndex,
    transformTarget,
    transformOrientation,
    customOrientations,
    activeCustomOrientation,
    snapMagnetEnabled,
    snapIncrementEnabled,
    snapPOVerticesEnabled,
    snapPOEdgesEnabled,
    snapGameSurfacesEnabled,
    moveSnapIncrement,
    precisionMoveSnapIncrement,
    rotationSnapIncrement,
    precisionRotationSnapIncrement,
    scaleSnapIncrement,
    precisionScaleSnapIncrement,
    onAction,
}: TransformToolbarControlsProps) => {
    const { t } = usePO2Localization();
    const [openPopover, setOpenPopover] = useState<"axes" | "snap" | null>(null);
    const [snapSettingsExpanded, setSnapSettingsExpanded] = useState(false);
    useReportHelpTopics("toolbar", openPopover === "axes" ? "axisOptions" : openPopover === "snap" ? "snapOptions" : null, openPopover === "snap" && snapSettingsExpanded ? "snapIncrements" : null);

    const orientationOptions = [
        {
            key: "local",
            label: t("PO2.Transform.Orientation.Choice.Local"),
            selected: transformOrientation === 1,
            onSelect: () => onAction("setLocalTransformOrientation"),
        },
        {
            key: "global",
            label: t("PO2.Transform.Orientation.Choice.Global"),
            selected: transformOrientation === 0,
            onSelect: () => onAction("setGlobalTransformOrientation"),
        },
        ...customOrientations.map((orientation) => ({
            key: `custom-${orientation.index}`,
            label: orientation.name,
            selected: activeCustomOrientation?.index === orientation.index,
            onSelect: () => onAction("setCustomTransformOrientation", orientation.index),
        })),
    ];
    const orientationRows = [];
    for (let index = 0; index < orientationOptions.length; index += 2) {
        orientationRows.push(orientationOptions.slice(index, index + 2));
    }

    const orientationName = transformOrientation === 0
        ? t("PO2.Transform.Orientation.Choice.Global")
        : transformOrientation === 1
            ? t("PO2.Transform.Orientation.Choice.Local")
            : activeCustomOrientation?.name ?? t("PO2.Transform.Orientation.CustomFallback");

    return (
        <>
            {showTargetControls && (
                <div className={toolbar.group} role="group" aria-label={t("PO2.Transform.Target.AccessibleLabel")}>
                    <div className={toolbar.control}>
                        <ToolbarButton
                            variant="menu"
                            selected={transformTarget === 0}
                            onSelect={() => onAction("setObjectTransformTarget")}
                            disabled={selectedObjectLocked}
                            className={toolbar.textButton}
                        >
                            {t("PO2.Common.Object")}
                        </ToolbarButton>
                    </div>
                    <div className={toolbar.control}>
                        <ToolbarButton
                            variant="menu"
                            selected={transformTarget === 1}
                            onSelect={() => onAction("setActivePartTransformTarget")}
                            disabled={
                                selectedObjectLocked ||
                                selectedPartIndex < 0
                            }
                            className={toolbar.textButton}
                        >
                            {t("PO2.Transform.Target.Choice.Part")}
                        </ToolbarButton>
                    </div>
                </div>
            )}

            {showTargetControls && (
                <span className={context.divider} aria-hidden="true">
                    <Icon src={dividerIcon} tinted={false} className={toolbar.dividerImage} />
                </span>
            )}

            <ToolbarPopover
                label={t("PO2.Transform.Orientation.Selector.Label", { orientation: orientationName })}
                selected={transformOrientation === 2}
                open={openPopover === "axes"}
                onToggle={(open) => setOpenPopover(open ? "axes" : null)}
                className={styles.axesPanel}
            >
                <div className={styles.orientationList}>
                    {orientationRows.map((row) => (
                        <div key={row[0].key} className={styles.orientationRow}>
                            {row.map((option) => (
                                <div key={option.key} className={styles.orientationCell}>
                                    <Button
                                        className={styles.button}
                                        variant="menu"
                                        selected={option.selected}
                                        onSelect={option.onSelect}
                                    >
                                        {option.label}
                                    </Button>
                                </div>
                            ))}
                            {row.length === 1 && <div className={styles.orientationCell} aria-hidden="true" />}
                        </div>
                    ))}
                </div>
                <CustomAxisControls selectedObjectId={selectedObjectId} selectedObjectLocked={selectedObjectLocked}
                    customOrientations={customOrientations} activeCustomOrientation={activeCustomOrientation}
                    canBeginAxisPicking={canBeginAxisPicking} onAction={(action, ...args) => {
                        if (action === "beginAxisPicking") setOpenPopover(null);
                        onAction(action, ...args);
                    }} />
            </ToolbarPopover>

            <ToolbarPopover
                label={t("PO2.Transform.Snapping.Toggle.Label", { state: snapMagnetEnabled ? t("PO2.Common.On") : t("PO2.Common.Off") })}
                selected={snapMagnetEnabled}
                open={openPopover === "snap"}
                onToggle={(open) => setOpenPopover(open ? "snap" : null)}
                className={styles.snapPanel}
            >
                <div className={styles.snapSettings}>
                    <div className={styles.snapHeader}>
                        <span>{t("PO2.Transform.Snapping.Title")}</span>
                        <Button
                            variant="menu"
                            selected={snapMagnetEnabled}
                            onSelect={() => onAction("toggleSnapMagnet")}
                            className={styles.textButton}
                        >
                            {snapMagnetEnabled ? t("PO2.Transform.Snapping.State.Enabled") : t("PO2.Transform.Snapping.State.Disabled")}
                        </Button>
                    </div>

                    <div className={styles.snapHint}>
                        {snapMagnetEnabled
                            ? t("PO2.Transform.Snapping.Hint.DisableTemporarily")
                            : t("PO2.Transform.Snapping.Hint.EnableTemporarily")}
                        {t("PO2.Transform.Snapping.Hint.Precision")}
                    </div>

                    <div className={styles["target-header"]}>{t("PO2.Transform.Snapping.Targets.Title")}</div>
                    <div className={styles.targetGrid}>
                        <div className={styles.targetRow}>
                            <Button
                                variant="menu"
                                selected={snapIncrementEnabled}
                                onSelect={() => onAction("toggleSnapIncrement")}
                                className={styles.targetButton}
                            >
                                {t("PO2.Transform.Snapping.Target.Increment")}
                            </Button>
                            <Button
                                variant="menu"
                                selected={snapPOVerticesEnabled}
                                onSelect={() => onAction("toggleSnapPOVertices")}
                                className={styles.targetButton}
                            >
                                {t("PO2.Transform.Snapping.Target.Vertices")}
                            </Button>
                        </div>
                        <div className={styles.targetRow}>
                            <Button
                                variant="menu"
                                selected={snapPOEdgesEnabled}
                                onSelect={() => onAction("toggleSnapPOEdges")}
                                className={styles.targetButton}
                            >
                                {t("PO2.Transform.Snapping.Target.Edges")}
                            </Button>
                            <Button
                                variant="menu"
                                selected={snapGameSurfacesEnabled}
                                onSelect={() => onAction("toggleSnapGameSurfaces")}
                                className={styles.targetButton}
                            >
                                {t("PO2.Transform.Snapping.Target.GameSurfaces")}
                            </Button>
                        </div>
                    </div>

                    <MenuButton
                        selected={snapSettingsExpanded}
                        onSelect={() => setSnapSettingsExpanded(
                            (expanded) => !expanded,
                        )}
                        className={styles.incrementToggle}
                    >
                        {snapSettingsExpanded
                            ? t("PO2.Transform.SnapIncrements.Hide.Action")
                            : t("PO2.Transform.SnapIncrements.Edit.Action")}
                    </MenuButton>

                    {snapSettingsExpanded && (
                        <div className={styles["increment-settings"]}>
                            <div className={styles["increment-header"]}>
                                <span />
                                <span>{t("PO2.Transform.SnapIncrements.Normal.Label")}</span>
                                <span>{t("PO2.Transform.SnapIncrements.Precision.Label")}</span>
                            </div>

                            <div className={styles["increment-row"]}>
                                <span>{t("PO2.Transform.SnapIncrements.Move.Label")}</span>
                                <IncrementInput
                                    value={moveSnapIncrement}
                                    digits={3}
                                    minimum={0.001}
                                    step={0.1}
                                    resetKey="snap-increments"
                                    ariaLabel={t("PO2.Transform.SnapIncrements.Move.Normal.AccessibleLabel")}
                                    onChange={(value) => onAction(
                                        "setMoveSnapIncrement",
                                        value,
                                    )}
                                />
                                <IncrementInput
                                    value={precisionMoveSnapIncrement}
                                    digits={3}
                                    minimum={0.001}
                                    step={0.01}
                                    resetKey="snap-increments"
                                    ariaLabel={t("PO2.Transform.SnapIncrements.Move.Precision.AccessibleLabel")}
                                    onChange={(value) => onAction(
                                        "setPrecisionMoveSnapIncrement",
                                        value,
                                    )}
                                />
                            </div>

                            <div className={styles["increment-row"]}>
                                <span>{t("PO2.Transform.SnapIncrements.Rotate.Label")}</span>
                                <IncrementInput
                                    value={rotationSnapIncrement}
                                    digits={2}
                                    minimum={0.01}
                                    step={1}
                                    resetKey="snap-increments"
                                    ariaLabel={t("PO2.Transform.SnapIncrements.Rotate.Normal.AccessibleLabel")}
                                    onChange={(value) => onAction(
                                        "setRotationSnapIncrement",
                                        value,
                                    )}
                                />
                                <IncrementInput
                                    value={precisionRotationSnapIncrement}
                                    digits={2}
                                    minimum={0.01}
                                    step={0.1}
                                    resetKey="snap-increments"
                                    ariaLabel={t("PO2.Transform.SnapIncrements.Rotate.Precision.AccessibleLabel")}
                                    onChange={(value) => onAction(
                                        "setPrecisionRotationSnapIncrement",
                                        value,
                                    )}
                                />
                            </div>

                            <div className={styles["increment-row"]}>
                                <span>{t("PO2.Common.Scale")}</span>
                                <IncrementInput
                                    value={scaleSnapIncrement}
                                    digits={3}
                                    minimum={0.001}
                                    step={0.01}
                                    resetKey="snap-increments"
                                    ariaLabel={t("PO2.Transform.SnapIncrements.Scale.Normal.AccessibleLabel")}
                                    onChange={(value) => onAction(
                                        "setScaleSnapIncrement",
                                        value,
                                    )}
                                />
                                <IncrementInput
                                    value={precisionScaleSnapIncrement}
                                    digits={3}
                                    minimum={0.001}
                                    step={0.001}
                                    resetKey="snap-increments"
                                    ariaLabel={t("PO2.Transform.SnapIncrements.Scale.Precision.AccessibleLabel")}
                                    onChange={(value) => onAction(
                                        "setPrecisionScaleSnapIncrement",
                                        value,
                                    )}
                                />
                            </div>

                            <Button
                                variant="menu"
                                onSelect={() => onAction("resetSnapIncrements")}
                                className={styles.wideButton}
                            >
                                {t("PO2.Transform.SnapIncrements.Reset.Action")}
                            </Button>
                        </div>
                    )}
                </div>
            </ToolbarPopover>
        </>
    );
};
