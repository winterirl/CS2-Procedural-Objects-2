import { usePO2Localization } from "@/l10n/localization";
import { MenuButton as Button } from "@/components/common/menu-button";
import { NumericStepper } from "@/components/common/numeric-stepper";
import styles from "@/styles/content.module.scss";

export type PrimitiveCreationParameters = {
    sphereRadius: number;
    sphereSegments: number;
    sphereVerticalSegments: number;
    domeRadius: number;
    domeHeight: number;
    domeSegments: number;
    domeVerticalSegments: number;
    boxWidth: number;
    boxHeight: number;
    boxDepth: number;
    planeWidth: number;
    planeDepth: number;
    wedgeWidth: number;
    wedgeHeight: number;
    wedgeDepth: number;
    cylinderRadius: number;
    cylinderHeight: number;
    cylinderSegments: number;
    coneBottomDiameter: number;
    coneTopDiameter: number;
    coneHeight: number;
    coneSegments: number;
    archWidth: number;
    archHeight: number;
    archDepth: number;
    archOpeningWidth: number;
    archOpeningHeight: number;
    archCurveSegments: number;
    stairsWidth: number;
    stairsTreadDepth: number;
    stairsSteps: number;
};

const primitiveType = {
    box: 0,
    plane: 1,
    wedge: 2,
    cylinder: 3,
    cone: 4,
    arch: 5,
    stairs: 6,
    sphere: 7,
    dome: 8,
} as const;

type CreateWorkspaceProps = {
    placingPrimitiveType: number;
    selectedPrimitiveType: number;
    parameters: PrimitiveCreationParameters;
    onAction: (action: string, ...args: number[]) => void;
};

type ParameterRowProps = {
    label: string;
    value: number;
    digits: number;
    resetKey: string;
    minimum?: number;
    maximum?: number;
    onDecrease: () => void;
    onCommit: (value: number) => void;
    onIncrease: () => void;
};

const ParameterRow = ({
    label,
    value,
    digits,
    resetKey,
    minimum,
    maximum,
    onDecrease,
    onCommit,
    onIncrease,
}: ParameterRowProps) => (
    <div className={styles.valueRow}>
        <span className={styles.parameterLabel}>{label}</span>
        <NumericStepper
            value={value}
            digits={digits}
            resetKey={resetKey}
            ariaLabel={label}
            minimum={minimum}
            maximum={maximum}
            onCommit={onCommit}
            onDecrease={onDecrease}
            onIncrease={onIncrease}
        />
    </div>
);

export const CreateWorkspace = ({
    placingPrimitiveType,
    selectedPrimitiveType,
    parameters,
    onAction,
}: CreateWorkspaceProps) => {
    const { t, formatNumber } = usePO2Localization();
    const action = (name: string) => () => onAction(name);
    const commit = (name: string) => (value: number) => onAction(name, value);
    const parameterKey = (name: string) => `${selectedPrimitiveType}:${name}`;
    const isPlacing = placingPrimitiveType >= primitiveType.box && placingPrimitiveType <= primitiveType.dome;
    const stairsTotalHeight = parameters.stairsSteps * 0.2;
    const stairsTotalDepth = parameters.stairsSteps * parameters.stairsTreadDepth;

    return (
        <div className={styles.createWorkspace}>
            <div className={styles.creationActions}>
                <div className={styles.choiceRow}>
                    <div className={styles.choiceCell}>
                        <Button selected={placingPrimitiveType === primitiveType.box} onSelect={action("placeBox")}>{t("PO2.Create.Primitive.Box.Name")}</Button>
                    </div>
                    <div className={styles.choiceCell}>
                        <Button selected={placingPrimitiveType === primitiveType.plane} onSelect={action("placePlane")}>{t("PO2.Create.Primitive.Plane.Name")}</Button>
                    </div>
                </div>
                <div className={styles.choiceRow}>
                    <div className={styles.choiceCell}>
                        <Button selected={placingPrimitiveType === primitiveType.wedge} onSelect={action("placeWedge")}>{t("PO2.Create.Primitive.Wedge.Name")}</Button>
                    </div>
                    <div className={styles.choiceCell}>
                        <Button selected={placingPrimitiveType === primitiveType.cylinder} onSelect={action("placeCylinder")}>{t("PO2.Create.Primitive.Cylinder.Name")}</Button>
                    </div>
                </div>
                <div className={styles.choiceRow}>
                    <div className={styles.choiceCell}>
                        <Button selected={placingPrimitiveType === primitiveType.cone} onSelect={action("placeCone")}>{t("PO2.Create.Primitive.Cone.Name")}</Button>
                    </div>
                    <div className={styles.choiceCell}>
                        <Button selected={placingPrimitiveType === primitiveType.arch} onSelect={action("placeArch")}>{t("PO2.Create.Primitive.Arch.Name")}</Button>
                    </div>
                </div>
                <div className={styles.choiceRow}>
                    <div className={styles.choiceCell}>
                        <Button selected={placingPrimitiveType === primitiveType.sphere} onSelect={action("placeSphere")}>{t("PO2.Create.Primitive.Sphere.Name")}</Button>
                    </div>
                    <div className={styles.choiceCell}>
                        <Button selected={placingPrimitiveType === primitiveType.dome} onSelect={action("placeDome")}>{t("PO2.Create.Primitive.Dome.Name")}</Button>
                    </div>
                </div>
                <div className={styles.choiceRow}>
                    <div className={styles.choiceCell}>
                        <Button selected={placingPrimitiveType === primitiveType.stairs} onSelect={action("placeStairs")}>{t("PO2.Create.Primitive.Stairs.Name")}</Button>
                    </div>
                    <div className={styles.choiceCell} aria-hidden="true" />
                </div>
            </div>

            <div className={styles.instruction}>
                {isPlacing
                    ? t("PO2.Create.Placement.Instruction")
                    : t("PO2.Create.PrimitivePicker.Instruction")}
            </div>

            {isPlacing && (
                <div className={styles.parameters}>

                    {placingPrimitiveType === primitiveType.box && (
                        <>
                            <ParameterRow
                                label={t("PO2.Create.Width.Label")}
                                value={parameters.boxWidth}
                                digits={1}
                                resetKey={parameterKey("box-width")}
                                minimum={0.1}
                                onDecrease={action("decreaseBoxWidth")}
                                onCommit={commit("setBoxWidth")}
                                onIncrease={action("increaseBoxWidth")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Height.Label")}
                                value={parameters.boxHeight}
                                digits={1}
                                resetKey={parameterKey("box-height")}
                                minimum={0.1}
                                onDecrease={action("decreaseBoxHeight")}
                                onCommit={commit("setBoxHeight")}
                                onIncrease={action("increaseBoxHeight")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Depth.Label")}
                                value={parameters.boxDepth}
                                digits={1}
                                resetKey={parameterKey("box-depth")}
                                minimum={0.1}
                                onDecrease={action("decreaseBoxDepth")}
                                onCommit={commit("setBoxDepth")}
                                onIncrease={action("increaseBoxDepth")}
                            />
                        </>
                    )}

                    {placingPrimitiveType === primitiveType.plane && (
                        <>
                            <ParameterRow
                                label={t("PO2.Create.Width.Label")}
                                value={parameters.planeWidth}
                                digits={1}
                                resetKey={parameterKey("plane-width")}
                                minimum={0.1}
                                onDecrease={action("decreasePlaneWidth")}
                                onCommit={commit("setPlaneWidth")}
                                onIncrease={action("increasePlaneWidth")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Depth.Label")}
                                value={parameters.planeDepth}
                                digits={1}
                                resetKey={parameterKey("plane-depth")}
                                minimum={0.1}
                                onDecrease={action("decreasePlaneDepth")}
                                onCommit={commit("setPlaneDepth")}
                                onIncrease={action("increasePlaneDepth")}
                            />
                        </>
                    )}

                    {placingPrimitiveType === primitiveType.wedge && (
                        <>
                            <ParameterRow
                                label={t("PO2.Create.Width.Label")}
                                value={parameters.wedgeWidth}
                                digits={1}
                                resetKey={parameterKey("wedge-width")}
                                minimum={0.1}
                                onDecrease={action("decreaseWedgeWidth")}
                                onCommit={commit("setWedgeWidth")}
                                onIncrease={action("increaseWedgeWidth")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Height.Label")}
                                value={parameters.wedgeHeight}
                                digits={1}
                                resetKey={parameterKey("wedge-height")}
                                minimum={0.1}
                                onDecrease={action("decreaseWedgeHeight")}
                                onCommit={commit("setWedgeHeight")}
                                onIncrease={action("increaseWedgeHeight")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Depth.Label")}
                                value={parameters.wedgeDepth}
                                digits={1}
                                resetKey={parameterKey("wedge-depth")}
                                minimum={0.1}
                                onDecrease={action("decreaseWedgeDepth")}
                                onCommit={commit("setWedgeDepth")}
                                onIncrease={action("increaseWedgeDepth")}
                            />
                        </>
                    )}

                    {placingPrimitiveType === primitiveType.cylinder && (
                        <>
                            <ParameterRow
                                label={t("PO2.Create.Radius.Label")}
                                value={parameters.cylinderRadius}
                                digits={1}
                                resetKey={parameterKey("cylinder-radius")}
                                minimum={0.1}
                                onDecrease={action("decreaseCylinderRadius")}
                                onCommit={commit("setCylinderRadius")}
                                onIncrease={action("increaseCylinderRadius")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Height.Label")}
                                value={parameters.cylinderHeight}
                                digits={1}
                                resetKey={parameterKey("cylinder-height")}
                                minimum={0.1}
                                onDecrease={action("decreaseCylinderHeight")}
                                onCommit={commit("setCylinderHeight")}
                                onIncrease={action("increaseCylinderHeight")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Segments.Label")}
                                value={parameters.cylinderSegments}
                                digits={0}
                                resetKey={parameterKey("cylinder-segments")}
                                minimum={5}
                                maximum={64}
                                onDecrease={action("decreaseCylinderSegments")}
                                onCommit={(value) => onAction(
                                    "setCylinderSegments",
                                    Math.round(value),
                                )}
                                onIncrease={action("increaseCylinderSegments")}
                            />
                        </>
                    )}

                    {placingPrimitiveType === primitiveType.cone && (
                        <>
                            <ParameterRow
                                label={t("PO2.Create.BottomDiameter.Label")}
                                value={parameters.coneBottomDiameter}
                                digits={1}
                                resetKey={parameterKey("cone-bottom-diameter")}
                                minimum={0.1}
                                onDecrease={action("decreaseConeBottomDiameter")}
                                onCommit={commit("setConeBottomDiameter")}
                                onIncrease={action("increaseConeBottomDiameter")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.TopDiameter.Label")}
                                value={parameters.coneTopDiameter}
                                digits={1}
                                resetKey={parameterKey("cone-top-diameter")}
                                minimum={0}
                                onDecrease={action("decreaseConeTopDiameter")}
                                onCommit={commit("setConeTopDiameter")}
                                onIncrease={action("increaseConeTopDiameter")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Height.Label")}
                                value={parameters.coneHeight}
                                digits={1}
                                resetKey={parameterKey("cone-height")}
                                minimum={0.1}
                                onDecrease={action("decreaseConeHeight")}
                                onCommit={commit("setConeHeight")}
                                onIncrease={action("increaseConeHeight")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Segments.Label")}
                                value={parameters.coneSegments}
                                digits={0}
                                resetKey={parameterKey("cone-segments")}
                                minimum={3}
                                maximum={64}
                                onDecrease={action("decreaseConeSegments")}
                                onCommit={(value) => onAction(
                                    "setConeSegments",
                                    Math.round(value),
                                )}
                                onIncrease={action("increaseConeSegments")}
                            />
                        </>
                    )}

                    {placingPrimitiveType === primitiveType.arch && (
                        <>
                            <ParameterRow
                                label={t("PO2.Create.Width.Label")}
                                value={parameters.archWidth}
                                digits={1}
                                resetKey={parameterKey("arch-width")}
                                minimum={parameters.archOpeningWidth + 0.2}
                                onDecrease={action("decreaseArchWidth")}
                                onCommit={commit("setArchWidth")}
                                onIncrease={action("increaseArchWidth")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Height.Label")}
                                value={parameters.archHeight}
                                digits={1}
                                resetKey={parameterKey("arch-height")}
                                minimum={parameters.archOpeningHeight + 0.1}
                                onDecrease={action("decreaseArchHeight")}
                                onCommit={commit("setArchHeight")}
                                onIncrease={action("increaseArchHeight")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Depth.Label")}
                                value={parameters.archDepth}
                                digits={1}
                                resetKey={parameterKey("arch-depth")}
                                minimum={0.1}
                                onDecrease={action("decreaseArchDepth")}
                                onCommit={commit("setArchDepth")}
                                onIncrease={action("increaseArchDepth")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.OpeningWidth.Label")}
                                value={parameters.archOpeningWidth}
                                digits={1}
                                resetKey={parameterKey("arch-opening-width")}
                                minimum={0.2}
                                maximum={Math.min(
                                    parameters.archWidth - 0.2,
                                    parameters.archOpeningHeight * 2,
                                )}
                                onDecrease={action("decreaseArchOpeningWidth")}
                                onCommit={commit("setArchOpeningWidth")}
                                onIncrease={action("increaseArchOpeningWidth")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.OpeningHeight.Label")}
                                value={parameters.archOpeningHeight}
                                digits={1}
                                resetKey={parameterKey("arch-opening-height")}
                                minimum={parameters.archOpeningWidth * 0.5}
                                maximum={parameters.archHeight - 0.1}
                                onDecrease={action("decreaseArchOpeningHeight")}
                                onCommit={commit("setArchOpeningHeight")}
                                onIncrease={action("increaseArchOpeningHeight")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.CurveSegments.Label")}
                                value={parameters.archCurveSegments}
                                digits={0}
                                resetKey={parameterKey("arch-curve-segments")}
                                minimum={4}
                                maximum={64}
                                onDecrease={action("decreaseArchCurveSegments")}
                                onCommit={(value) => onAction(
                                    "setArchCurveSegments",
                                    Math.round(value),
                                )}
                                onIncrease={action("increaseArchCurveSegments")}
                            />
                        </>
                    )}

                    {placingPrimitiveType === primitiveType.sphere && (
                        <>
                            <ParameterRow
                                label={t("PO2.Create.Radius.Label")}
                                value={parameters.sphereRadius}
                                digits={1}
                                resetKey={parameterKey("sphere-radius")}
                                minimum={0.1}
                                onDecrease={action("decreaseSphereRadius")}
                                onCommit={commit("setSphereRadius")}
                                onIncrease={action("increaseSphereRadius")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.RadialSegments.Label")}
                                value={parameters.sphereSegments}
                                digits={0}
                                resetKey={parameterKey("sphere-segments")}
                                minimum={3}
                                maximum={64}
                                onDecrease={action("decreaseSphereSegments")}
                                onCommit={(value) => onAction("setSphereSegments", Math.round(value))}
                                onIncrease={action("increaseSphereSegments")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.VerticalSegments.Label")}
                                value={parameters.sphereVerticalSegments}
                                digits={0}
                                resetKey={parameterKey("sphere-vertical-segments")}
                                minimum={2}
                                maximum={64}
                                onDecrease={action("decreaseSphereVerticalSegments")}
                                onCommit={(value) => onAction("setSphereVerticalSegments", Math.round(value))}
                                onIncrease={action("increaseSphereVerticalSegments")}
                            />
                        </>
                    )}

                    {placingPrimitiveType === primitiveType.dome && (
                        <>
                            <ParameterRow
                                label={t("PO2.Create.Radius.Label")}
                                value={parameters.domeRadius}
                                digits={1}
                                resetKey={parameterKey("dome-radius")}
                                minimum={0.1}
                                onDecrease={action("decreaseDomeRadius")}
                                onCommit={commit("setDomeRadius")}
                                onIncrease={action("increaseDomeRadius")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Height.Label")}
                                value={parameters.domeHeight}
                                digits={1}
                                resetKey={parameterKey("dome-height")}
                                minimum={0.1}
                                onDecrease={action("decreaseDomeHeight")}
                                onCommit={commit("setDomeHeight")}
                                onIncrease={action("increaseDomeHeight")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.RadialSegments.Label")}
                                value={parameters.domeSegments}
                                digits={0}
                                resetKey={parameterKey("dome-segments")}
                                minimum={3}
                                maximum={64}
                                onDecrease={action("decreaseDomeSegments")}
                                onCommit={(value) => onAction("setDomeSegments", Math.round(value))}
                                onIncrease={action("increaseDomeSegments")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.VerticalSegments.Label")}
                                value={parameters.domeVerticalSegments}
                                digits={0}
                                resetKey={parameterKey("dome-vertical-segments")}
                                minimum={2}
                                maximum={64}
                                onDecrease={action("decreaseDomeVerticalSegments")}
                                onCommit={(value) => onAction("setDomeVerticalSegments", Math.round(value))}
                                onIncrease={action("increaseDomeVerticalSegments")}
                            />
                        </>
                    )}

                    {placingPrimitiveType === primitiveType.stairs && (
                        <>
                            <ParameterRow
                                label={t("PO2.Create.Width.Label")}
                                value={parameters.stairsWidth}
                                digits={1}
                                resetKey={parameterKey("stairs-width")}
                                minimum={0.1}
                                onDecrease={action("decreaseStairsWidth")}
                                onCommit={commit("setStairsWidth")}
                                onIncrease={action("increaseStairsWidth")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.TreadDepth.Label")}
                                value={parameters.stairsTreadDepth}
                                digits={1}
                                resetKey={parameterKey("stairs-tread-depth")}
                                minimum={0.1}
                                onDecrease={action("decreaseStairsTreadDepth")}
                                onCommit={commit("setStairsTreadDepth")}
                                onIncrease={action("increaseStairsTreadDepth")}
                            />
                            <ParameterRow
                                label={t("PO2.Create.Steps.Label")}
                                value={parameters.stairsSteps}
                                digits={0}
                                resetKey={parameterKey("stairs-steps")}
                                minimum={1}
                                maximum={128}
                                onDecrease={action("decreaseStairsSteps")}
                                onCommit={(value) => onAction(
                                    "setStairsSteps",
                                    Math.round(value),
                                )}
                                onIncrease={action("increaseStairsSteps")}
                            />
                            <div className={styles.placementInstruction}>
                                <div>{t("PO2.Create.Stairs.TotalHeight.Summary", { height: formatNumber(stairsTotalHeight, 1) })}</div>
                                <div>{t("PO2.Create.Stairs.TotalDepth.Summary", { depth: formatNumber(stairsTotalDepth, 1) })}</div>
                            </div>
                        </>
                    )}
                </div>
            )}
        </div>
    );
};
