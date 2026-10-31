import { usePO2Localization } from "@/l10n/localization";
import { bindValue, trigger, useValue } from "cs2/api";
import { Button, ConfirmationDialog, Dropdown, DropdownItem, DropdownToggle } from "cs2/ui";
import { useEffect, useMemo, useState } from "react";
import { MenuButton } from "@/components/common/menu-button";
import { SaveDatePicker } from "@/components/save-tile/save-date-picker";
import { getNativeDropdownTheme } from "@/components/common/native-dropdown-theme";
import { WorkspacePanel } from "@/components/workspace-panel/workspace-panel";
import { SaveTile } from "@/components/save-tile/save-tile";
import type { RuntimeText } from "@/l10n/localization";
import type { SaveManagerState } from "@/utils/ui-payloads";
import { unknownLineage, validCleanupDate, type SaveStatusFilter } from "@/utils/save-manager";
import materialStyles from "@/styles/materials.module.scss";
import tileStyles from "@/components/save-tile/save-tile.module.scss";
import styles from "./save-manager-workspace.module.scss";

const state$ = bindValue<SaveManagerState | null>("po2", "saveManager", null);
const busy$ = bindValue<boolean>("po2", "saveManagerBusy", false);
const maintenance$ = bindValue<RuntimeText | null>("po2", "saveMaintenanceStatus", null);
const action = (name: string, ...args: string[]) => trigger("po2", name, ...args);

type Choice = { value: string; label: string };
const ChoiceField = ({ label, value, choices, disabled, onChange }: {
    label: string; value: string; choices: Choice[]; disabled: boolean; onChange: (value: string) => void;
}) => {
    const theme = getNativeDropdownTheme();
    return <div className={`${materialStyles.categoryDropdown} ${styles.choice}`}>
        <Dropdown alignment="left" theme={{ ...theme, dropdownPopup: `${theme.dropdownPopup || ""} po2-ui ${materialStyles.categoryDropdownPopup} ${styles.popup}` }}
            content={<>{choices.map(choice => <DropdownItem key={choice.value} value={choice.value}
                selected={value === choice.value} closeOnSelect onChange={onChange} onToggleSelected={onChange}>
                {choice.label}
            </DropdownItem>)}</>}>
            <DropdownToggle disabled={disabled} aria-label={label}>
                {choices.find(choice => choice.value === value)?.label || label}
            </DropdownToggle>
        </Dropdown>
    </div>;
};

export const SaveManagerWorkspace = () => {
    const { t, message } = usePO2Localization();
    const maintenance = useValue(maintenance$);
    const state = useValue(state$);
    const busy = useValue(busy$);
    if (!state) return <WorkspacePanel id="PO2SaveManager" title={t("PO2.SaveManager.Title")} wide>
        <div role="status">{busy ? t("PO2.SaveManager.Maintenance.Scanning") : message(maintenance)}</div>
        <MenuButton disabled={busy} onSelect={() => action("refreshSaveManager")}>{t("PO2.SaveManager.Refresh.Action")}</MenuButton>
    </WorkspacePanel>;
    return <SaveManagerPanel state={state} busy={busy} />;
};

const SaveManagerPanel = ({ state, busy }: { state: SaveManagerState; busy: boolean }) => {
    const { t, message, formatInteger, formatSaveBytes } = usePO2Localization();
    const maintenance = useValue(maintenance$);
    const lineage = state.session;
    const status = state.filter;
    const oldest = state.oldest;
    const query = (session: string, filter: string, ascending: boolean, page = 0) =>
        action("saveManagerPage", session, filter, `${ascending ? "oldest" : "newest"}:${page}`);
    const setLineage = (value: string) => query(value, status, oldest);
    const setStatus = (value: SaveStatusFilter) => query(lineage, value, oldest);
    const setOldest = (value: boolean) => query(lineage, status, value);
    const [selected, setSelected] = useState<Set<string>>(new Set());
    const [cleanup, setCleanup] = useState(false);
    const [before, setBefore] = useState("");
    const [issuesOpen, setIssuesOpen] = useState(false);
    const [detailsKey, setDetailsKey] = useState("");
    const lineages = state.lineages;
    const protectedKeys = useMemo(() => new Set(state.protectedKeys), [state.protectedKeys]);
    const visible = state.saves;
    const selectable = visible.filter(save => !protectedKeys.has(save.fileKey));
    const selectedKeys = selectable.filter(save => selected.has(save.fileKey)).map(save => save.fileKey);
    const confirmation = state.confirmation;
    const blocked = busy || !state.scanSucceeded || confirmation !== null;
    const totalBytes = state.totalBytes;
    const visibleBytes = state.pageBytes;

    useEffect(() => { setSelected(new Set()); }, [state.saves, lineage, status]);
    const preview = (kind: string, value = "", scope = lineage) => action("previewSaveDeletion", kind, scope, value);
    const toggleSelected = (key: string) => setSelected(current => {
        const next = new Set(current);
        if (next.has(key)) next.delete(key); else next.add(key);
        return next;
    });
    const controls = <div className={styles.controls}>
        <div className={styles.row}>
            <ChoiceField label={t("PO2.SaveManager.LineageFilter.Label")} value={lineage} disabled={blocked} onChange={setLineage}
                choices={[{ value: "", label: t("PO2.SaveManager.LineageFilter.All") }, ...lineages.map(group => ({ value: group.id,
                    label: group.id === unknownLineage ? t("PO2.SaveManager.LineageFilter.Unknown", { count: group.count }) : `${group.name ?? t("PO2.Common.Unknown")} / ${group.id.slice(0, 8)} (${group.count})` }))]} />
            <MenuButton disabled={busy || confirmation !== null} onSelect={() => action("refreshSaveManager")}>{t("PO2.Common.Refresh")}</MenuButton>
        </div>
        <div className={styles.row}>
            <ChoiceField label={t("PO2.SaveManager.StatusFilter.Label")} value={status} disabled={blocked} onChange={value => setStatus(value as SaveStatusFilter)}
                choices={[{ value: "all", label: t("PO2.SaveManager.StatusFilter.All") }, { value: "Matched", label: t("PO2.SaveManager.SaveStatus.Matched") },
                    { value: "Orphaned", label: t("PO2.SaveManager.SaveStatus.Orphaned") }, { value: "Unknown", label: t("PO2.SaveManager.SaveStatus.Unknown") }]} />
            <ChoiceField label={t("PO2.SaveManager.DateOrder.Label")} value={oldest ? "oldest" : "newest"} disabled={blocked} onChange={value => setOldest(value === "oldest")}
                choices={[{ value: "newest", label: t("PO2.SaveManager.DateOrder.NewestFirst") }, { value: "oldest", label: t("PO2.SaveManager.DateOrder.OldestFirst") }]} />
        </div>
        <div className={styles.summary}>{t("PO2.SaveManager.Results.Summary", { shownCount: formatInteger(visible.length), shownSize: formatSaveBytes(visibleBytes), totalCount: formatInteger(state.totalCount), totalSize: formatSaveBytes(totalBytes) })}</div>
        <div className={styles.row}>
            <MenuButton disabled={blocked || state.page === 0} onSelect={() => query(lineage, status, oldest, state.page - 1)}>{t("PO2.SaveManager.Page.Previous")}</MenuButton>
            <span>{t("PO2.SaveManager.Page.Summary", { page: state.page + 1, pages: state.pageCount, count: state.matchCount })}</span>
            <MenuButton disabled={blocked || state.page + 1 >= state.pageCount} onSelect={() => query(lineage, status, oldest, state.page + 1)}>{t("PO2.SaveManager.Page.Next")}</MenuButton>
        </div>
        <div className={styles.row}>
            <MenuButton disabled={blocked || selectable.length === 0} onSelect={() => setSelected(new Set(selectable.map(save => save.fileKey)))}>{t("PO2.SaveManager.Selection.SelectVisible.Action")}</MenuButton>
            <MenuButton disabled={blocked || selectedKeys.length === 0} onSelect={() => setSelected(new Set())}>{t("PO2.SaveManager.Selection.Clear.Action")}</MenuButton>
            <MenuButton disabled={blocked || selectedKeys.length === 0} onSelect={() => preview("selected", selectedKeys.join(","), "")}>
                {t("PO2.SaveManager.Selection.Delete.Action", { count: formatInteger(selectedKeys.length) })}
            </MenuButton>
        </div>
        <MenuButton disabled={blocked} selected={cleanup} onSelect={() => setCleanup(!cleanup)}>{t("PO2.SaveManager.Cleanup.Toggle.Action")}</MenuButton>
        {cleanup && <div className={styles.controls}>
            <div className={styles.muted}>{lineage ? t("PO2.SaveManager.Cleanup.Scope.SelectedLineage") : t("PO2.SaveManager.Cleanup.Scope.AllLineages")}</div>
            <div className={styles.row}>
                <MenuButton disabled={blocked || !lineage || lineage === unknownLineage} onSelect={() => preview("lineage")}>{t("PO2.SaveManager.Cleanup.DeleteLineage.Action")}</MenuButton>
                <MenuButton disabled={blocked || !state.catalogComplete} onSelect={() => preview("orphaned")}>{t("PO2.SaveManager.Cleanup.DeleteOrphaned.Action")}</MenuButton>
            </div>
            <div className={styles.row}>
                <SaveDatePicker value={before} disabled={blocked} onChange={setBefore} />
                <MenuButton disabled={blocked || !validCleanupDate(before)} onSelect={() => preview("before", before)}>{t("PO2.SaveManager.Cleanup.DeleteBeforeDate.Action")}</MenuButton>
            </div>
        </div>}
        {!state.catalogComplete && <div className={styles.muted}>{t("PO2.SaveManager.Catalog.IncompleteNotice")}</div>}
        {busy && <div role="status">{t("PO2.SaveManager.Status.OperationPending")}</div>}
    </div>;

    return <>
        <WorkspacePanel id="PO2SaveManager" title={t("PO2.SaveManager.Title")} wide scrollable context={controls}>
            <div className={tileStyles.list}>
                {maintenance && <div className={styles.message} role="status">{message(maintenance)}</div>}
                {state.status && <div className={styles.message} role="status" aria-live="polite">{message(state.status)}</div>}
                {state.issues.length > 0 && <>
                    <MenuButton onSelect={() => setIssuesOpen(!issuesOpen)}>{t("PO2.SaveManager.Scan.Errors.ToggleLabel", { count: state.issueCount })}</MenuButton>
                    {issuesOpen && <div className={styles.message}>{state.issues.map(message).join("\n")}</div>}
                </>}
                {visible.length === 0 && <div>{state.scanSucceeded ? t("PO2.SaveManager.Results.Empty") : t("PO2.SaveManager.Scan.FailedNotice")}</div>}
                {visible.map(save => {
                    const protectedSave = protectedKeys.has(save.fileKey);
                    const selectionDisabled = blocked || protectedSave;
                    return <SaveTile key={save.fileKey} save={save}
                        selection={<div className={styles.row}>
                            <Button variant="text" role="checkbox" className={`${styles.selection} ${selectionDisabled ? styles.selectionDisabled : styles.selectionEnabled}`}
                                aria-checked={selected.has(save.fileKey) && !protectedSave}
                                aria-label={t("PO2.SaveManager.Selection.Toggle.AccessibleLabel", { saveName: save.saveName || save.fileKey })} disabled={selectionDisabled}
                                onSelect={() => toggleSelected(save.fileKey)}>
                                <span className={styles.checkbox} aria-hidden="true">
                                    {selected.has(save.fileKey) && !protectedSave && <span className={styles.checkmark} />}
                                </span>
                                <span>{protectedSave ? t("PO2.SaveManager.Protection.Label") : t("PO2.Common.Select")}</span>
                            </Button>
                            <span className={styles.tag}>{save.gameSaveStatus === "Matched" ? t("PO2.SaveManager.SaveStatus.Matched") : save.gameSaveStatus === "Orphaned" ? t("PO2.SaveManager.SaveStatus.Orphaned") : t("PO2.SaveManager.SaveStatus.Unknown")}</span>
                            <span>{save.saveKind === "AutoSave" ? t("PO2.SaveManager.SaveKind.Auto") : save.saveKind === "Manual" ? t("PO2.SaveManager.SaveKind.Manual") : ""}</span>
                        </div>}
                        details={detailsKey === save.fileKey && <div className={styles.identities}>
                            <div>{t("PO2.SaveManager.Details.SaveId", { saveId: save.fileKey })}</div>
                            <div>{t("PO2.SaveManager.Details.LineageId", { lineageId: save.sessionGuid })}</div>
                            <div>{t("PO2.SaveManager.Details.MapName", { mapName: save.mapName.trim() || t("PO2.Common.Unknown") })}</div>
                            {protectedSave && <div>{t("PO2.SaveManager.Protection.Description")}</div>}
                        </div>}
                        actions={<div className={styles.row}>
                            <MenuButton selected={detailsKey === save.fileKey} onSelect={() => setDetailsKey(detailsKey === save.fileKey ? "" : save.fileKey)}>{t("PO2.SaveManager.Details.Toggle.Action")}</MenuButton>
                            <MenuButton disabled={blocked || protectedSave} onSelect={() => preview("selected", save.fileKey, "")}>{t("PO2.Common.Delete")}</MenuButton>
                        </div>} />;
                })}
            </div>
        </WorkspacePanel>
        {confirmation && <div className={styles.confirmation}>
            <ConfirmationDialog key={confirmation.token} title={t("PO2.SaveManager.DeleteConfirmation.Title")} zIndex={10200}
                message={t("PO2.SaveManager.DeleteConfirmation.Message")}
                confirm={t("PO2.Common.Delete")} cancel={t("PO2.Common.Cancel")} cancellable disableContinue={busy}
                onConfirm={() => action("confirmSaveDeletion", confirmation.token)}
                onCancel={() => action("cancelSaveDeletion")} />
        </div>}
    </>;
};
