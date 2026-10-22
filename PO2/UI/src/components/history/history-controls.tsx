import { usePO2Localization } from "@/l10n/localization";
import { useValue } from "cs2/api";
import { Tooltip } from "cs2/ui";
import { ToolbarButton as Button } from "@/components/common/toolbar-controls";
import { canUndo$, canRedo$, busy$, undoDescription$, redoDescription$, runHistoryCommand } from "@/bindings/history";
import undoIcon from "@/images/navigation/undo.svg";
import redoIcon from "@/images/navigation/redo.svg";
import styles from "@/shell/main-toolbar.module.scss";

export const HistoryControls = () => {
    const { t, message } = usePO2Localization();
    const canUndo = useValue(canUndo$);
    const canRedo = useValue(canRedo$);
    const busy = useValue(busy$);
    const undo = message(useValue(undoDescription$));
    const redo = message(useValue(redoDescription$));

    return <>
        <Tooltip tooltip={t("PO2.History.Undo.Tooltip")}>
            <div className={styles.control}>
                <Button className={styles.navigationButton} variant="round" src={undoIcon} disabled={!canUndo || busy} onSelect={() => runHistoryCommand("undo")}
                    aria-label={undo ? t("PO2.History.Undo.AccessibleLabel", { action: undo }) : t("PO2.Common.Undo")} />
            </div>
        </Tooltip>
        <Tooltip tooltip={t("PO2.History.Redo.Tooltip")}>
            <div className={styles.control}>
                <Button className={styles.navigationButton} variant="round" src={redoIcon} disabled={!canRedo || busy} onSelect={() => runHistoryCommand("redo")}
                    aria-label={redo ? t("PO2.History.Redo.AccessibleLabel", { action: redo }) : t("PO2.Common.Redo")} />
            </div>
        </Tooltip>
    </>;
};
