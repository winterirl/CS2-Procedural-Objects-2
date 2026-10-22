import { usePO2Localization } from "@/l10n/localization";
import { MenuButton } from "@/components/common/menu-button";
import layout from "@/styles/layout.module.scss";
import content from "@/styles/content.module.scss";

export const AxisPickingPanel = ({
    canConfirm,
    onAction,
}: {
    canConfirm: boolean;
    onAction: (action: string) => void;
}) => {
    const { t } = usePO2Localization();
    return (
        <div className={layout.stack}>
            <div>{t("PO2.CustomAxis.Picker.Title")}</div>
            <div className={content.instruction}>
                {t("PO2.CustomAxis.Picker.Selection.Instruction")}
            </div>
            <div className={layout.actionRow}>
                <MenuButton disabled={!canConfirm} onSelect={() => onAction("confirmAxisPicking")}>
                    {t("PO2.CustomAxis.Picker.Confirm.Action")}
                </MenuButton>
                <MenuButton onSelect={() => onAction("cancelAxisPicking")}>{t("PO2.Common.Cancel")}</MenuButton>
            </div>
            <div className={content.instruction}>
                {t("PO2.CustomAxis.Picker.Cancellation.Instruction")}
            </div>
        </div>
    );
};
