import type { ReactNode } from "react";
import { TopToolbar } from "@/components/top-toolbar/top-toolbar";
import { ContextToolbar } from "@/components/top-toolbar/context-toolbar";
import type { TopToolbarProps } from "@/components/top-toolbar/top-toolbar-props";
import styles from "./toolbars.module.scss";

const selectedObjectWorkspaceIds = ["transform", "mesh-edit", "texturing", "lod"];

type ToolbarsProps = TopToolbarProps & { contextTools?: ReactNode };

export const Toolbars = ({ workspaceItems, contextTools, ...props }: ToolbarsProps) => {
    const mainItems = workspaceItems.filter((item) => !selectedObjectWorkspaceIds.includes(item.id));
    const contextItems = workspaceItems.filter((item) => selectedObjectWorkspaceIds.includes(item.id));

    return (
        <div className={styles.stack}>
            <TopToolbar {...props} workspaceItems={mainItems} />
            <ContextToolbar workspaceItems={contextItems} tools={contextTools} />
        </div>
    );
};
