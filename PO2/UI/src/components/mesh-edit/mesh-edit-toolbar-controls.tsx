import { usePO2Localization } from "@/l10n/localization";
import type { ReactNode } from "react";
import { Icon } from "cs2/ui";
import dividerIcon from "@/images/navigation/divider-vertical.svg";
import context from "@/shell/context-toolbar.module.scss";
import { ToolbarButton as Button } from "@/components/common/toolbar-controls";
import toolbar from "@/shell/main-toolbar.module.scss";

type MeshEditToolbarControlsProps = {
    vertexSelectionMode: boolean;
    edgeSelectionMode: boolean;
    vertexScope: number;
    visibilityMode: number;
    interactionBlocked: boolean;
    transformControls: ReactNode;
    onAction: (action: string) => void;
};

export const MeshEditToolbarControls = ({
    vertexSelectionMode,
    edgeSelectionMode,
    vertexScope,
    visibilityMode,
    interactionBlocked,
    transformControls,
    onAction,
}: MeshEditToolbarControlsProps) => {
    const { t } = usePO2Localization();
    return (
        <>
            <Button
                variant="menu"
                selected={vertexScope === 1}
                onSelect={() => onAction("setMeshVertexScopeAllParts")}
                disabled={interactionBlocked}
                className={toolbar.textButton}
            >
                {t("PO2.Common.Object")}
            </Button>
            <Button
                variant="menu"
                selected={vertexScope === 0}
                onSelect={() => onAction("setMeshVertexScopeCurrentPart")}
                disabled={interactionBlocked}
                className={toolbar.textButton}
            >
                {t("PO2.Edit.Target.Choice.Part")}
            </Button>
            <span className={context.divider} aria-hidden="true">
                <Icon src={dividerIcon} tinted={false} className={toolbar.dividerImage} />
            </span>
            <Button
                variant="menu"
                selected={vertexSelectionMode}
                onSelect={() => onAction("setVertexSelectionMode")}
                disabled={interactionBlocked}
                className={toolbar.textButton}
            >
                {t("PO2.Edit.SelectionMode.Vertex")}
            </Button>
            <Button
                variant="menu"
                selected={edgeSelectionMode}
                onSelect={() => onAction("setEdgeSelectionMode")}
                disabled={interactionBlocked}
                className={toolbar.textButton}
            >
                {t("PO2.Edit.SelectionMode.Edge")}
            </Button>
            <span className={context.divider} aria-hidden="true">
                <Icon src={dividerIcon} tinted={false} className={toolbar.dividerImage} />
            </span>
            <Button
                variant="menu"
                selected={visibilityMode === 1}
                onSelect={() => onAction("setMeshVisibilityXRay")}
                disabled={interactionBlocked}
                className={toolbar.textButton}
            >
                {t("PO2.Edit.VisibilityMode.XRay")}
            </Button>
            <Button
                variant="menu"
                selected={visibilityMode === 0}
                onSelect={() => onAction("setMeshVisibilitySolid")}
                disabled={interactionBlocked}
                className={toolbar.textButton}
            >
                {t("PO2.Edit.VisibilityMode.Solid")}
            </Button>
            <span className={context.divider} aria-hidden="true">
                <Icon src={dividerIcon} tinted={false} className={toolbar.dividerImage} />
            </span>
            {transformControls}
        </>
    );
};
