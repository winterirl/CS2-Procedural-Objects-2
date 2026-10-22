import { useCallback, useState } from "react";
import { trigger } from "cs2/api";
import { Dropdown, DropdownItem, DropdownToggle } from "cs2/ui";
import { lodState$, LodDisplay, LodMethod, LodLevelStatus, type LodLevel, type LodState } from "@/bindings/lod";
import { useScopedValue } from "@/bindings/scoped-value";
import { group } from "@/bindings/commands";
import { usePO2Localization, type LocaleKey } from "@/l10n/localization";
import { usePO2NumberFormat } from "@/l10n/number-format";
import { MenuButton } from "@/components/common/menu-button";
import { TwoColumnRow } from "@/components/common/two-column-row";
import { NumericStepper } from "@/components/common/numeric-stepper";
import { getNativeDropdownTheme } from "@/components/common/native-dropdown-theme";
import layout from "@/styles/layout.module.scss";
import content from "@/styles/content.module.scss";
import materialStyles from "@/styles/materials.module.scss";
import styles from "./lod-workspace.module.scss";

const statusKeys: LocaleKey[] = ["PO2.Lod.Status.Idle", "PO2.Lod.Status.Waiting", "PO2.Lod.Status.Capturing",
    "PO2.Lod.Status.Generating", "PO2.Lod.Status.Uploading", "PO2.Lod.Status.Ready", "PO2.Lod.Status.OutOfDate",
    "PO2.Lod.Status.Cancelled", "PO2.Lod.Status.Failed"];
const previewKeys: LocaleKey[] = ["PO2.Lod.Preview.Auto", "PO2.Lod.Preview.Original", "PO2.Lod.Level.One", "PO2.Lod.Level.Two"];

export const LodWorkspace = () => {
    const state = useScopedValue(lodState$, true);
    return state ? <LodControls key={`${state.objectId}:${state.session}`} state={state} /> : null;
};

const LodControls = ({ state }: { state: LodState }) => {
    const { t } = usePO2Localization();
    const { formatNumber } = usePO2NumberFormat();
    const [invalid, setInvalid] = useState<ReadonlySet<number>>(new Set());
    const valid = invalid.size === 0;
    const disabled = !state.editable || state.inherit;
    const set = (field: number, value: number) => trigger(group, "setLodValue", state.session, field, value);
    const action = (name: string) => trigger(group, name, state.session);
    const validity = useCallback((field: number, isValid: boolean) => setInvalid(previous => {
        if (previous.has(field) === !isValid) return previous;
        const next = new Set(previous);
        if (isValid) next.delete(field); else next.add(field);
        return next;
    }), []);
    const number = (field: number, label: LocaleKey, value: number, min: number, max: number,
        step: number, digits: number, blocked = disabled, scale = 1) => (
        <div className={content.valueRow}>
            <span className={content.parameterLabel}>{t(label)}</span>
            <NumericStepper value={value * scale} digits={digits} minimum={min} maximum={max}
                resetKey={`${state.session}:${field}`} ariaLabel={t(label)} disabled={blocked}
                onValidityChange={isValid => validity(field, isValid)}
                normalizeValue={field === 4 || field === 14 ? Math.round : undefined}
                onCommit={next => set(field, next / scale)}
                onDecrease={() => set(field, Math.max(min, value * scale - step) / scale)}
                onIncrease={() => set(field, Math.min(max, value * scale + step) / scale)} />
        </div>
    );
    const level = (index: number, value: LodLevel) => {
        const offset = index === 1 ? 0 : 10;
        const blocked = disabled || !value.enabled;
        const native = getNativeDropdownTheme();
        const methods = [t("PO2.Lod.Method.EdgeCollapse"), t("PO2.Lod.Method.Aggressive")];
        return <section className={styles.level}>
            <div className={content.sectionHeader}>
                <span>{t(index === 1 ? "PO2.Lod.Level.One" : "PO2.Lod.Level.Two")}</span>
                <MenuButton selected={value.enabled} disabled={disabled}
                    onSelect={() => set(offset + 1, value.enabled ? 0 : 1)}>{t("PO2.Lod.Level.Enabled")}</MenuButton>
            </div>
            <div className={content.valueRow}>
                <span className={content.parameterLabel}>{t("PO2.Lod.Method.Label")}</span>
                <div className={materialStyles.categoryDropdown}>
                    <Dropdown alignment="left" theme={{ ...native,
                        dropdownPopup: `${native.dropdownPopup || ""} po2-ui ${materialStyles.categoryDropdownPopup}` }}
                        content={<>{methods.map((label, method) => <DropdownItem key={method} value={method}
                            selected={value.method === method} closeOnSelect
                            onChange={next => set(offset + 2, next)} onToggleSelected={next => set(offset + 2, next)}>{label}</DropdownItem>)}</>}>
                        <DropdownToggle disabled={blocked} aria-label={t("PO2.Lod.Method.Label")}>{methods[value.method]}</DropdownToggle>
                    </Dropdown>
                </div>
            </div>
            <div className={styles.note}>{t(value.method === LodMethod.EdgeCollapse ? "PO2.Lod.Method.EdgeCollapse.Description" : "PO2.Lod.Method.Aggressive.Description")}</div>
            {number(offset + 3, "PO2.Lod.Target.Ratio", value.ratio, 0.01, 99.99, 1, 2, blocked, 100)}
            <TwoColumnRow><MenuButton selected={value.cap > 0} disabled={blocked}
                onSelect={() => set(offset + 4, value.cap > 0 ? 0 : 1500)}>{t("PO2.Lod.Target.UseCap")}</MenuButton></TwoColumnRow>
            {value.cap > 0 && number(offset + 4, "PO2.Lod.Target.Cap", value.cap, 1, 715827882, 100, 0, blocked)}
            {number(offset + 5, "PO2.Lod.Error.Label", value.error, 0, 1, 0.001, 6, blocked)}
            {value.method === LodMethod.Aggressive && number(offset + 6, "PO2.Lod.Prune.Label", value.prune, 0, 1, 0.001, 6, blocked)}
            {state.hasResult && value.enabled && <div className={styles.note}>
                <div>{t("PO2.Lod.Result.Triangles", { actual: value.actual, target: value.target })}</div>
                {!value.targetMet && <div>{t("PO2.Lod.Result.TargetNotReached")}</div>}
                {value.status === LodLevelStatus.Limited && <div>{t("PO2.Lod.Result.NoUsefulReduction")}</div>}
                {value.canPreview && value.status === LodLevelStatus.Usable && !value.inAuto && <div>{t("PO2.Lod.Result.ExcludedFromAuto")}</div>}
            </div>}
        </section>;
    };
    const preview = (display: LodDisplay) => <MenuButton selected={state.display === display}
        disabled={!state.available || (display >= LodDisplay.Lod1 &&
            !(display === LodDisplay.Lod1 ? state.lod1 : state.lod2).canPreview)}
        onSelect={() => trigger(group, "setLodDisplay", state.session, display)}>{t(previewKeys[display])}</MenuButton>;
    return <div className={`${layout.stackRoomy} ${styles.panel}`}>
        <div className={`${styles.note} ${styles.intro}`}>{t("PO2.Lod.Scope.Description")}</div>
        <div>{t("PO2.Lod.Source.Triangles", { count: state.sourceTriangles })}</div>
        {!state.available && <div role="status">{t("PO2.Lod.Status.Unavailable")}</div>}
        <MenuButton selected={state.inherit} disabled={!state.editable}
            onSelect={() => set(0, state.inherit ? 0 : 1)}>{t("PO2.Lod.Inherit.Action")}</MenuButton>
        <div>{t("PO2.Lod.Preview.Label")}</div>
        <TwoColumnRow>{preview(LodDisplay.Auto)}{preview(LodDisplay.Original)}</TwoColumnRow>
        <TwoColumnRow>{preview(LodDisplay.Lod1)}{preview(LodDisplay.Lod2)}</TwoColumnRow>
        <div className={styles.status} role="status" aria-live="polite">
            {t(statusKeys[state.status] ?? "PO2.Lod.Status.Idle", { progress: state.progress })}
        </div>
        {state.inherit && state.hasResult && !state.lod1.canPreview && !state.lod2.canPreview &&
            <div className={styles.note}>{t("PO2.Lod.Result.DefaultsKeepOriginal")}</div>}
        {level(1, state.lod1)}
        {level(2, state.lod2)}
        <div className={styles.note}>{t("PO2.Lod.Error.Description")}</div>
        <div className={layout.divider} />
        <div>{t("PO2.Lod.Transitions.Title")}</div>
        <MenuButton selected={state.inheritDistances} disabled={disabled}
            onSelect={() => set(20, state.inheritDistances ? 0 : 1)}>{t("PO2.Lod.Transitions.Inherit")}</MenuButton>
        {number(21, "PO2.Lod.Transitions.First", state.distance1, 50, 200, 5, 0, disabled || state.inheritDistances, 100)}
        {number(22, "PO2.Lod.Transitions.Second", state.distance2, 50, 200, 5, 0, disabled || state.inheritDistances, 100)}
        {state.transitionsAdjusted && <div className={styles.note}>{t("PO2.Lod.Transitions.Adjusted", {
            first: formatNumber(state.height1 * 100, 2), second: formatNumber(state.height2 * 100, 2),
        })}</div>}
        <div className={layout.divider} />
        <TwoColumnRow>
            <MenuButton disabled={!state.canGenerate || !valid} onSelect={() => action("generateLodPreview")}>{t("PO2.Lod.Generate.Action")}</MenuButton>
            <MenuButton disabled={!state.editable || state.inherit} onSelect={() => set(0, 1)}>{t("PO2.Lod.RestoreDefaults.Action")}</MenuButton>
        </TwoColumnRow>
        <TwoColumnRow>
            <MenuButton disabled={!state.canApply || !valid} onSelect={() => action("applyLodPreview")}>{t("PO2.Common.Apply")}</MenuButton>
            <MenuButton disabled={!state.available} onSelect={() => action("cancelLodPreview")}>{t("PO2.Common.Cancel")}</MenuButton>
        </TwoColumnRow>
        {!valid && <div role="status">{t("PO2.Lod.Input.Invalid")}</div>}
    </div>;
};
