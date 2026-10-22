import { usePO2Localization } from "@/l10n/localization";
import { bindValue, trigger, useValue } from "cs2/api";
import { MenuButton } from "@/components/common/menu-button";
import { NumericStepper } from "@/components/common/numeric-stepper";
import { TwoColumnRow } from "@/components/common/two-column-row";
import content from "@/styles/content.module.scss";
import layout from "@/styles/layout.module.scss";
import styles from "./selected-object.module.scss";

const group = "po2";
const busy$ = bindValue<boolean>(group, "arrayBusy", false);
const dragging$ = bindValue<boolean>(group, "arrayDragging", false);
const canResetRotation$ = bindValue<boolean>(group, "canResetArrayRotation", false);
const drawing$ = bindValue<boolean>(group, "arrayDrawing", false);
const canApply$ = bindValue<boolean>(group, "canApplyArray", false);
const gap$ = bindValue<number>(group, "arrayGap", 0);
const mode$ = bindValue<number>(group, "arrayMode", 0);
const pathKind$ = bindValue<number>(group, "arrayPathKind", 0);
const alignment$ = bindValue<number>(group, "arrayAlignment", 0);
const controlPoint$ = bindValue<number>(group, "arrayControlPoint", 0);
const curveLength$ = bindValue<number>(group, "arrayCurveLength", 0);
const copies$ = bindValue<number>(group, "arrayCopies", 1);
const capacity$ = bindValue<number>(group, "arrayCapacity", 1);
const spacing$ = bindValue<number>(group, "arraySpacing", 0);
const angle$ = bindValue<number>(group, "arrayPathAngle", 0);
const length$ = bindValue<number>(group, "arrayPathLength", 0);
const count$ = bindValue<number>(group, "arrayCount", 0);
const effectiveGap$ = bindValue<number>(group, "arrayEffectiveGap", 0);
const normalizeAngle = (value: number) => ((value % 360) + 360) % 360;
const enum ArrayPath { Line, Curve }
const enum ArrayAlignment { EndToEnd, FixedOrientation }
const enum ArrayPoint { Endpoint, StartHandle, EndHandle }
const enum ArrayMode { FixedGap, FitEndpoint, FixedCount }

export const ArrayPartControls = ({ objectId }: { objectId: number }) => {
    const { t, formatNumber } = usePO2Localization();
    const busy = useValue(busy$);
    const drawing = useValue(drawing$);
    const dragging = useValue(dragging$);
    const canResetRotation = useValue(canResetRotation$);
    const canApply = useValue(canApply$);
    const gap = useValue(gap$);
    const mode = useValue(mode$);
    const pathKind = useValue(pathKind$);
    const alignment = useValue(alignment$);
    const controlPoint = useValue(controlPoint$);
    const curveLength = useValue(curveLength$);
    const curve = pathKind === ArrayPath.Curve;
    const copies = useValue(copies$);
    const capacity = useValue(capacity$);
    const spacing = useValue(spacing$);
    const angle = useValue(angle$);
    const length = useValue(length$);
    const count = useValue(count$);
    const effectiveGap = useValue(effectiveGap$);
    const disabled = busy || dragging;
    const fixedCount = mode === ArrayMode.FixedCount;
    const fit = mode === ArrayMode.FitEndpoint;
    const setGap = (value: number) => trigger(group, "setArrayGap", value);
    const setCopies = (value: number) => trigger(group, "setArrayCopies", Math.max(1, Math.min(capacity, Math.round(value))));
    const setAngle = (value: number) => trigger(group, "setArrayPathAngle", normalizeAngle(value));
    const setLength = (value: number) => trigger(group, "setArrayPathLength", Math.max(0, value));

    return (
        <div className={layout.stack}>
            <div>{t("PO2.PartArray.Title")}</div>
            <TwoColumnRow>
                <MenuButton selected={!curve} disabled={disabled} onSelect={() => trigger(group, "setArrayPathKind", ArrayPath.Line)}>{t("PO2.PartArray.Path.Choice.Line")}</MenuButton>
                <MenuButton selected={curve} disabled={disabled} onSelect={() => trigger(group, "setArrayPathKind", ArrayPath.Curve)}>{t("PO2.PartArray.Path.Choice.Curve")}</MenuButton>
            </TwoColumnRow>
            {curve && <TwoColumnRow>
                <MenuButton selected={alignment === ArrayAlignment.EndToEnd} disabled={disabled} onSelect={() => trigger(group, "setArrayAlignment", ArrayAlignment.EndToEnd)}>
                    {t("PO2.PartArray.Alignment.Choice.EndToEnd")}
                </MenuButton>
                <MenuButton selected={alignment === ArrayAlignment.FixedOrientation} disabled={disabled} onSelect={() => trigger(group, "setArrayAlignment", ArrayAlignment.FixedOrientation)}>
                    {t("PO2.PartArray.Alignment.Choice.FixedOrientation")}
                </MenuButton>
            </TwoColumnRow>}
            <TwoColumnRow>
                <MenuButton selected={mode === ArrayMode.FixedGap} disabled={disabled} onSelect={() => trigger(group, "setArrayMode", ArrayMode.FixedGap)}>
                    {t("PO2.PartArray.Mode.Choice.FixedGap")}
                </MenuButton>
                <MenuButton selected={fit} disabled={disabled} onSelect={() => trigger(group, "setArrayMode", ArrayMode.FitEndpoint)}>
                    {t("PO2.PartArray.Mode.Choice.FitEndpoint")}
                </MenuButton>
            </TwoColumnRow>
            <TwoColumnRow>
                <MenuButton selected={fixedCount} disabled={disabled} onSelect={() => trigger(group, "setArrayMode", ArrayMode.FixedCount)}>
                    {t("PO2.PartArray.Mode.Choice.FixedCount")}
                </MenuButton>
            </TwoColumnRow>
            <div className={content.valueRow}>
                <span className={content.parameterLabel}>{fixedCount ? t("PO2.PartArray.Copies.Label") : fit ? t("PO2.PartArray.MinimumGap.Label") : t("PO2.PartArray.Gap.Label")}</span>
                {fixedCount ? <NumericStepper
                    value={copies} digits={0} minimum={1} maximum={capacity} disabled={disabled || capacity < 1}
                    normalizeValue={Math.round} resetKey={`${objectId}:array-copies`} ariaLabel={t("PO2.PartArray.Copies.AccessibleLabel")}
                    onCommit={setCopies} onDecrease={() => setCopies(copies - 1)} onIncrease={() => setCopies(copies + 1)}
                /> : <NumericStepper
                    value={gap} digits={2} minimum={0} disabled={disabled}
                    resetKey={`${objectId}:array-gap`} ariaLabel={t("PO2.PartArray.Gap.AccessibleLabel")}
                    onCommit={setGap}
                    onDecrease={() => setGap(Math.max(0, gap - (gap <= 1 ? 0.1 : 1)))}
                    onIncrease={() => setGap(gap + (gap < 1 ? 0.1 : 1))}
                />}
            </div>
            <div className={styles.arraySummary}>{t("PO2.PartArray.NewParts.Summary", { count: count })}</div>
            {fit && <div className={styles.arraySummary}>{t("PO2.PartArray.EffectiveGap.Summary", { gap: formatNumber(effectiveGap, 2) })}</div>}
            {fixedCount && <div className={styles.arraySummary}>{t("PO2.PartArray.PivotSpacing.Summary", { spacing: formatNumber(spacing, 2) })}</div>}
            <div className={content.valueRow}>
                <span className={content.parameterLabel}>{curve ? t("PO2.PartArray.EndpointAngle.Label") : t("PO2.PartArray.PathAngle.Label")}</span>
                <NumericStepper
                    value={angle} digits={1} disabled={disabled} normalizeValue={normalizeAngle}
                    resetKey={`${objectId}:array-angle`} ariaLabel={curve ? t("PO2.PartArray.EndpointAngle.AccessibleLabel") : t("PO2.PartArray.PathAngle.AccessibleLabel")}
                    onCommit={setAngle} onDecrease={() => setAngle(angle - 1)} onIncrease={() => setAngle(angle + 1)}
                />
            </div>
            <div className={content.valueRow}>
                <span className={content.parameterLabel}>{curve ? t("PO2.PartArray.EndpointDistance.Label") : t("PO2.PartArray.PathLength.Label")}</span>
                <NumericStepper
                    value={length} digits={2} minimum={0} disabled={disabled}
                    resetKey={`${objectId}:array-length`} ariaLabel={curve ? t("PO2.PartArray.EndpointDistance.AccessibleLabel") : t("PO2.PartArray.PathLength.AccessibleLabel")}
                    onCommit={setLength}
                    onDecrease={() => setLength(length - (length <= 1 ? 0.1 : 1))}
                    onIncrease={() => setLength(length + (length < 1 ? 0.1 : 1))}
                />
            </div>
            {curve && <>
                <div className={styles.arraySummary}>{t("PO2.PartArray.CurveLength.Summary", { length: formatNumber(curveLength, 2) })}</div>
                <div>{t("PO2.Common.Select")}</div>
                <TwoColumnRow>
                    <MenuButton selected={!drawing && controlPoint === ArrayPoint.StartHandle} disabled={disabled || drawing} onSelect={() => trigger(group, "selectArrayControlPoint", ArrayPoint.StartHandle)}>
                        {t("PO2.PartArray.ControlPoint.Choice.StartHandle")}
                    </MenuButton>
                    <MenuButton selected={!drawing && controlPoint === ArrayPoint.EndHandle} disabled={disabled || drawing} onSelect={() => trigger(group, "selectArrayControlPoint", ArrayPoint.EndHandle)}>
                        {t("PO2.PartArray.ControlPoint.Choice.EndHandle")}
                    </MenuButton>
                </TwoColumnRow>
                <TwoColumnRow>
                    <MenuButton selected={!drawing && controlPoint === ArrayPoint.Endpoint} disabled={disabled || drawing} onSelect={() => trigger(group, "selectArrayControlPoint", ArrayPoint.Endpoint)}>
                        {t("PO2.PartArray.ControlPoint.Choice.Endpoint")}
                    </MenuButton>
                </TwoColumnRow>
            </>}
            <div>{t("PO2.PartArray.Operations.Title")}</div>
            <TwoColumnRow>
                <MenuButton selected={drawing} disabled={busy || dragging} onSelect={() => trigger(group, "chooseArrayEndpoint")}>
                    {t("PO2.PartArray.SetEndpoint.Action")}
                </MenuButton>
                <MenuButton disabled={!canResetRotation} onSelect={() => trigger(group, "resetArrayRotation")}>
                    {t("PO2.PartArray.ResetRotation.Action")}
                </MenuButton>
            </TwoColumnRow>
            <TwoColumnRow>
                <MenuButton disabled={!canApply} onSelect={() => trigger(group, "applyArray")}>{t("PO2.Common.Apply")}</MenuButton>
                <MenuButton onSelect={() => trigger(group, "cancelArray")}>{t("PO2.Common.Cancel")}</MenuButton>
            </TwoColumnRow>
        </div>
    );
};
