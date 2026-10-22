import { usePO2Localization } from "@/l10n/localization";
import { TwoColumnRow } from "@/components/common/two-column-row";
import { MenuButton as Button } from "@/components/common/menu-button";
import content from "@/styles/content.module.scss";
import layout from "@/styles/layout.module.scss";

type PartManagementControlsProps = {
    canAdd: boolean;
    canCombine: boolean;
    canDuplicate: boolean;
    canArray: boolean;
    canExtract: boolean;
    canDelete: boolean;
    combining: boolean;
    placing: boolean;
    onAction: (action: string) => void;
};

export const PartManagementControls = ({
    canAdd,
    canCombine,
    canDuplicate,
    canArray,
    canExtract,
    canDelete,
    combining,
    placing,
    onAction,
}: PartManagementControlsProps) => {
    const { t } = usePO2Localization();
    return (
        <div className={layout.stack}>
            <div>{t("PO2.Parts.Add.Title")}</div>
            <div className={layout.actionRow}>
                <Button
                    selected={placing}
                    disabled={!canAdd || combining}
                    onSelect={() => onAction("addPrimitivePart")}
                >
                    {t("PO2.Parts.Add.FromPrimitive.Action")}
                </Button>
                <Button
                    selected={combining}
                    disabled={!canCombine}
                    onSelect={() => onAction("combineSelected")}
                >
                    {combining ? t("PO2.Parts.Combine.Cancel.Action") : t("PO2.Parts.Combine.Start.Action")}
                </Button>
            </div>
            <TwoColumnRow>
                <Button
                    disabled={!canDuplicate || combining}
                    onSelect={() => onAction("duplicateSelectedPart")}
                >
                    {t("PO2.Parts.Duplicate.Action")}
                </Button>
                <Button disabled={!canArray || combining} onSelect={() => onAction("beginArray")}>
                    {t("PO2.Parts.Array.Action")}
                </Button>
            </TwoColumnRow>
            {combining && (
                <div className={content.instruction}>
                    {t("PO2.Parts.Combine.Target.Instruction")}
                </div>
            )}
            <div>{t("PO2.Parts.Remove.Title")}</div>
            <div className={layout.actionRow}>
                <Button
                    disabled={!canExtract || combining}
                    onSelect={() => onAction("extractSelectedPart")}
                >
                    {t("PO2.Parts.Extract.Action")}
                </Button>
                <Button
                    disabled={!canDelete || combining}
                    onSelect={() => onAction("deleteSelectedPart")}
                >
                    {t("PO2.Parts.Delete.Action")}
                </Button>
            </div>
        </div>
    );
};
