import { usePO2Localization } from "@/l10n/localization";
import { Icon, Tooltip } from "cs2/ui";
import { ToolbarButton as Button, Panel } from "@/components/common/toolbar-controls";
import { HistoryControls } from "@/components/history/history-controls";
import verticalDividerIcon from "@/images/navigation/divider-vertical.svg";
import dividerIcon from "@/images/navigation/divider-slanted.svg";
import po2Icon from "@/images/procedural-objects.svg";
import saveOptionsIcon from "@/images/navigation/save-options.svg";
import copyIcon from "@/images/navigation/copy.svg";
import pasteIcon from "@/images/navigation/paste.svg";
import bulldozerIcon from "@/images/navigation/bulldozer.svg";
import helpIcon from "@/images/navigation/help.svg";
import backIcon from "@/images/navigation/back.svg";
import closeIcon from "@/images/navigation/close.svg";
import type { TopToolbarProps } from "./top-toolbar-props";
import styles from "@/shell/main-toolbar.module.scss";

export const TopToolbar = ({
    workspaceItems,
    copyActive,
    pasteActive,
    copyDisabled,
    pasteDisabled,
    onCopy,
    onPaste,
    deleteActive,
    deleteDisabled,
    onDelete,
    helpActive,
    helpDisabled = false,
    backDisabled = false,
    closeDisabled = false,
    saveManagerActive = false,
    saveManagerDisabled = false,
    onSaveManager,
    onBack,
    onHelp,
    onClose,
}: TopToolbarProps) => {
    const { t } = usePO2Localization();
    return (
        <Panel className={styles.panel} contentClassName={styles.content}>
            <nav className={styles.row} aria-label={t("PO2.Navigation.Toolbar.AccessibleLabel")}>
                <span className={styles.brand}>
                    <span className={styles.brandIcon} aria-hidden="true"><Icon src={po2Icon} tinted={false} className={styles.brandIconImage} /></span>
                    <span>PO2</span>
                </span>
                <span className={styles.divider} aria-hidden="true">
                    <Icon src={dividerIcon} tinted={false} className={styles.dividerImage} />
                </span>
                <div className={styles.group} role="group" aria-label={t("PO2.Navigation.Workspaces.AccessibleLabel")}>
                    {workspaceItems.map((item) => (
                        <Tooltip key={item.id} tooltip={item.tooltip ?? (item.disabled && item.disabledReason ? item.disabledReason : item.label)}>
                            <div className={styles.control}>
                                <Button className={styles.textButton} variant="menu" selected={item.active} disabled={item.disabled} onSelect={item.onSelect}>
                                    {item.label}
                                </Button>
                            </div>
                        </Tooltip>
                    ))}
                </div>
                <span className={styles.divider} aria-hidden="true">
                    <Icon src={verticalDividerIcon} tinted={false} className={styles.dividerImage} />
                </span>
                <div className={styles.group}>
                    <HistoryControls />
                    <Tooltip tooltip={t("PO2.Navigation.Copy.Tooltip")}>
                        <div className={styles.control}>
                            <Button className={styles.navigationButton} variant="round" src={copyIcon}
                                aria-label={t("PO2.Clipboard.Copy.Mode.Label")} selected={copyActive}
                                disabled={copyDisabled} onSelect={onCopy} />
                        </div>
                    </Tooltip>
                    <Tooltip tooltip={t("PO2.Navigation.Paste.Tooltip")}>
                        <div className={styles.control}>
                            <Button className={styles.navigationButton} variant="round" src={pasteIcon}
                                aria-label={t("PO2.Clipboard.Paste.Mode.Label")} selected={pasteActive}
                                disabled={pasteDisabled} onSelect={onPaste} />
                        </div>
                    </Tooltip>
                    <Tooltip tooltip={t("PO2.Navigation.Delete.Tooltip")}>
                        <div className={styles.control}>
                            <Button className={styles.navigationButton} variant="round" src={bulldozerIcon}
                                aria-label={t("PO2.Delete.Mode.Label")} selected={deleteActive}
                                disabled={deleteDisabled} onSelect={onDelete} />
                        </div>
                    </Tooltip>
                </div>
                <span className={styles.divider} aria-hidden="true">
                    <Icon src={verticalDividerIcon} tinted={false} className={styles.dividerImage} />
                </span>
                <div className={styles.group}>
                    <Tooltip tooltip={t("PO2.Navigation.Help.Tooltip")}>
                        <div className={styles.control}>
                            <Button className={styles.navigationButton} variant="round" src={helpIcon} aria-label={t("PO2.Common.Help")} selected={helpActive} disabled={helpDisabled} onSelect={onHelp} />
                        </div>
                    </Tooltip>
                    <Tooltip tooltip={t("PO2.Navigation.SaveManager.Action")}>
                        <div className={styles.control}>
                            <Button className={styles.navigationButton} variant="round" src={saveOptionsIcon} aria-label={t("PO2.Navigation.SaveManager.Action")} selected={saveManagerActive} disabled={saveManagerDisabled} onSelect={onSaveManager} />
                        </div>
                    </Tooltip>
                </div>
                <span className={styles.divider} aria-hidden="true">
                    <Icon src={verticalDividerIcon} tinted={false} className={styles.dividerImage} />
                </span>
                <div className={styles.group} role="group" aria-label={t("PO2.Navigation.Controls.AccessibleLabel")}>
                    <Tooltip tooltip={t("PO2.Navigation.Back.Tooltip")}>
                        <div className={styles.control}>
                            <Button className={styles.navigationButton} variant="round" src={backIcon} aria-label={t("PO2.Common.Back")} disabled={backDisabled} onSelect={onBack} />
                        </div>
                    </Tooltip>
                    <Tooltip tooltip={t("PO2.Navigation.Close.Tooltip")}>
                        <div className={styles.control}>
                            <Button className={styles.navigationButton} variant="round" src={closeIcon} aria-label={t("PO2.Common.Close")} disabled={closeDisabled} onSelect={onClose} />
                        </div>
                    </Tooltip>
                </div>
            </nav>
        </Panel>
    );
};
