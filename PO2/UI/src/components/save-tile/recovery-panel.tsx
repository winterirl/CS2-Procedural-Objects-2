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

export const RecoveryPanel = ({ status, resultPending, onDismiss, candidates, disabled, onRecover }: RecoveryPanelProps) => {
    const { t, formatInteger } = usePO2Localization();
    return (
        <WorkspacePanel
            id="PO2RecoveryPanel"
            title={t("PO2.Recovery.Title")}
            headerAction={<span className={typography.small}>{t("PO2.Recovery.Candidates.Summary", { count: formatInteger(candidates.length) })}</span>}
            context={<div role="status" aria-live="polite">{status}</div>}
            wide
            scrollable={candidates.length > 0}
        >
            <div className={styles.list}>
                {resultPending && (
                    <MenuButton onSelect={onDismiss} className={layout.wide}>{t("PO2.Recovery.Dismiss.Action")}</MenuButton>
                )}
                {candidates.map((candidate) => (
                    <SaveTile
                        key={candidate.fileKey}
                        save={candidate}
                        actions={(
                            <MenuButton
                                disabled={disabled}
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
