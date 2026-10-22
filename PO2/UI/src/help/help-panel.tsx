import { usePO2Localization } from "@/l10n/localization";
import { Fragment, memo, type ReactNode } from "react";
import { WorkspacePanel } from "@/components/workspace-panel/workspace-panel";
import { MenuButton } from "@/components/common/menu-button";
import { ToolbarButton } from "@/components/common/toolbar-controls";
import closeIcon from "@/images/navigation/close.svg";
import { helpContent } from "./help-content";
import { useLocalHelpTopics } from "./help-local-context";
import type { HelpTopic } from "./help-content";
import styles from "./help-panel.module.scss";

type Props = {
    topicKey: string;
    performance: boolean;
    captureDisabled: boolean;
    diagnostics: ReactNode;
    onClose: () => void;
    onPerformance: () => void;
    onContext: () => void;
};

// A string topic key stays stable while world positions and selection counts change.
const Content = memo(({ topicKey }: { topicKey: string }) => (
    <div className={styles.prose}>
        {topicKey.split("|").filter(Boolean).map(key => (
            <Fragment key={key}>{helpContent[key as HelpTopic]}</Fragment>
        ))}
    </div>
));

export const HelpPanel = ({ topicKey, performance, captureDisabled, diagnostics, onClose, onPerformance, onContext }: Props) => {
    const { t } = usePO2Localization();
    const local = useLocalHelpTopics();
    const primary = topicKey.split("|")[0];
    // Axis picking owns its own panel; ignore controls still mounted behind it.
    const topics = performance ? "performance" : Array.from(new Set([
        ...topicKey.split("|"), ...(primary === "axisPick" ? [] : local),
    ])).join("|");
    return (
        <WorkspacePanel
            key={performance ? "performance" : primary}
            id="PO2HelpPanel"
            title={performance ? t("PO2.Help.Performance.Title") : t("PO2.Help.Title")}
            wide={performance}
            scrollable
            headerAction={<ToolbarButton variant="round" src={closeIcon} className={styles.close} aria-label={t("PO2.Help.Close.AccessibleLabel")} onSelect={onClose} />}
        >
            <Content topicKey={topics} />
            {performance && diagnostics}
            <div className={styles.actions}>
                <MenuButton onSelect={performance ? onContext : onPerformance} disabled={!performance && captureDisabled}>
                    {performance ? t("PO2.Help.Return.Action") : t("PO2.Help.Performance.Action")}
                </MenuButton>
            </div>
        </WorkspacePanel>
    );
};
