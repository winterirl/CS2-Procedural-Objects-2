import { MenuButton } from "@/components/common/menu-button";
import { TwoColumnRow } from "@/components/common/two-column-row";
import { usePO2Localization } from "@/l10n/localization";

export const ClipboardWorkspace = ({ copying, canClear, onClear }: {
    copying: boolean;
    canClear: boolean;
    onClear: () => void;
}) => {
    const { t } = usePO2Localization();
    return <>
        <div>{t(copying ? "PO2.Clipboard.Copy.Target.Instruction" : "PO2.Clipboard.Paste.Target.Instruction")}</div>
        {copying && <TwoColumnRow>
            <MenuButton disabled={!canClear} onSelect={onClear}>{t("PO2.Clipboard.Clear.Action")}</MenuButton>
        </TwoColumnRow>}
    </>;
};
