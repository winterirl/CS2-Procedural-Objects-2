import { bindValue, trigger, useValue } from "cs2/api";
import { usePO2Localization } from "@/l10n/localization";
import { MenuButton } from "@/components/common/menu-button";
import { WorkspacePanel } from "@/components/workspace-panel/workspace-panel";
import type { SaveTileData } from "@/utils/ui-payloads";
import typography from "@/styles/typography.module.scss";
import layout from "@/styles/layout.module.scss";
import { SaveTile } from "./save-tile";
import styles from "./save-tile.module.scss";

type RecoveryPanelProps = {
    status: string;
    resultPending: boolean;
    onDismiss: () => void;
    candidates: SaveTileData[];
    disabled: boolean;
    onRecover: (fileKey: string) => void;
};

const total$ = bindValue<number>("po2", "recoveryCandidateCount", 0);
const page$ = bindValue<number>("po2", "recoveryPage", 0);
const pages$ = bindValue<number>("po2", "recoveryPageCount", 1);
const busy$ = bindValue<boolean>("po2", "recoveryBusy", false);

export const RecoveryPanel = ({ status, resultPending, onDismiss, candidates, disabled, onRecover }: RecoveryPanelProps) => {
    const { t, formatInteger } = usePO2Localization();
    const page = useValue(page$);
    const pages = useValue(pages$);
    const busy = useValue(busy$);
    const total = useValue(total$);
    return (
        <WorkspacePanel
            id="PO2RecoveryPanel"
            title={t("PO2.Recovery.Title")}
            headerAction={<span className={typography.small}>{t("PO2.Recovery.Candidates.Summary", { count: formatInteger(total) })}</span>}
            context={<div role="status" aria-live="polite">{status}</div>}
            wide
            scrollable={candidates.length > 0}
        >
            <div className={styles.list}>
                {resultPending && (
                    <MenuButton onSelect={onDismiss} className={layout.wide}>{t("PO2.Recovery.Dismiss.Action")}</MenuButton>
                )}
                {pages > 1 && <div>
                    <MenuButton disabled={busy || page === 0} onSelect={() => trigger("po2", "setRecoveryPage", page - 1)}>{t("PO2.SaveManager.Page.Previous")}</MenuButton>
                    <span>{page + 1} / {pages}</span>
                    <MenuButton disabled={busy || page + 1 >= pages} onSelect={() => trigger("po2", "setRecoveryPage", page + 1)}>{t("PO2.SaveManager.Page.Next")}</MenuButton>
                </div>}
                {candidates.map((candidate) => (
                    <SaveTile
                        key={candidate.fileKey}
                        save={candidate}
                        actions={(
                            <MenuButton
                                disabled={disabled || busy}
                                onSelect={() => onRecover(candidate.fileKey)}
                                className={layout.wide}
                            >
                                {t("PO2.Recovery.Recover.Action")}
                            </MenuButton>
                        )}
                    />
                ))}
            </div>
        </WorkspacePanel>
    );
};
