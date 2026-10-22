import { usePO2Localization } from "@/l10n/localization";
import { MenuButton as Button } from "@/components/common/menu-button";

type MaterialLibraryToolbarControlsProps = {
    onImport: () => void;
};

export const MaterialLibraryToolbarControls = ({
    onImport,
}: MaterialLibraryToolbarControlsProps) => {
    const { t } = usePO2Localization();
    return (
        <Button
            onSelect={onImport}
        >
            {t("PO2.Materials.Toolbar.Import.Action")}
        </Button>
    );
};
