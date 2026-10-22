import { usePO2Localization } from "@/l10n/localization";
import { useState } from "react";
import type { ReactNode } from "react";
import { ToolbarPopoverHost } from "@/components/common/toolbar-popover";
import { Icon, Tooltip } from "cs2/ui";
import { ToolbarButton as Button, Panel } from "@/components/common/toolbar-controls";
import type { ToolbarWorkspaceItem } from "@/components/top-toolbar/toolbar-types";
import dividerIcon from "@/images/navigation/divider-vertical.svg";
import toolbar from "@/shell/main-toolbar.module.scss";
import styles from "@/shell/context-toolbar.module.scss";

type ContextToolbarProps = {
    workspaceItems: ToolbarWorkspaceItem[];
    tools?: ReactNode;
};

export const ContextToolbar = ({ workspaceItems, tools }: ContextToolbarProps) => {
    const { t } = usePO2Localization();
    const [popoverHost, setPopoverHost] = useState<HTMLDivElement | null>(null);

    if (workspaceItems.length === 0 && !tools) {
        return null;
    }

    return (
        <ToolbarPopoverHost.Provider value={popoverHost}>
            <div className={styles.wrapper}>
                <Panel
                    className={`${toolbar.panel} ${styles.panel}`}
                    contentClassName={`${toolbar.content} ${styles.content}`}
                >
                    <div className={styles.innerContent}>
                        <nav className={toolbar.row} aria-label={t("PO2.Navigation.ContextToolbar.AccessibleLabel")}>
                            {workspaceItems.length > 0 && (
                                <div className={toolbar.group} role="group" aria-label={t("PO2.Navigation.ObjectWorkspaces.AccessibleLabel")}>
                                    {workspaceItems.map((item) => (
                                        <Tooltip key={item.id} tooltip={item.disabled && item.disabledReason ? item.disabledReason : item.label}>
                                            <div className={toolbar.control}>
                                                <Button
                                                    variant="menu"
                                                    selected={item.active}
                                                    disabled={item.disabled}
                                                    onSelect={item.onSelect}
                                                    className={toolbar.textButton}
                                                >
                                                    {item.label}
                                                </Button>
                                            </div>
                                        </Tooltip>
                                    ))}
                                </div>
                            )}
                            {workspaceItems.length > 0 && tools && (
                                <span className={toolbar.divider} aria-hidden="true">
                                    <Icon src={dividerIcon} tinted={false} className={toolbar.dividerImage} />
                                </span>
                            )}
                            {tools}
                        </nav>
                    </div>
                </Panel>
                <div ref={setPopoverHost} className={styles.popoverHost} />
            </div>
        </ToolbarPopoverHost.Provider>
    );
};
