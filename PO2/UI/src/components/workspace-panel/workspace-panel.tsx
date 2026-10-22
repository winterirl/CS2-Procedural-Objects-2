import { ScrollArea } from "@/components/common/scroll-area";
import typography from "@/styles/typography.module.scss";
import type { ReactNode } from "react";
import { Panel } from "cs2/ui";
import styles from "@/styles/shell.module.scss";

type WorkspacePanelProps = {
    id: string;
    title: string;
    context?: ReactNode;
    headerAction?: ReactNode;
    status?: ReactNode;
    wide?: boolean;
    scrollable?: boolean;
    children: ReactNode;
};

export const WorkspacePanel = ({
    id,
    title,
    context,
    headerAction,
    status,
    wide = false,
    scrollable = false,
    children,
}: WorkspacePanelProps) => (
    <Panel
        id={id}
        className={`${typography.panel} ${styles.workspacePanel} ${wide ? styles.workspacePanelWide : ""}`}
        aria-label={title}
        header={headerAction
            ? <div className={styles.workspacePanelHeader}><span className={typography.title}>{title}</span>{headerAction}</div>
            : <span className={typography.title}>{title}</span>}
        contentClassName={styles.workspacePanelContent}
    >
        {context && <div className={`${typography.normal} ${styles.workspacePanelContext}`}>{context}</div>}
        {scrollable ? (
            <ScrollArea
                className={`${typography.normal} ${styles.workspacePanelScroll}`}
                trackVisibility="scrollable"
            >
                <div className={styles.workspacePanelScrollContent}>{children}</div>
            </ScrollArea>
        ) : <div className={`${typography.normal} ${styles.workspacePanelBody}`}>{children}</div>}
        {status && <div className={`${typography.normal} ${styles.workspacePanelStatus}`}>{status}</div>}
    </Panel>
);
