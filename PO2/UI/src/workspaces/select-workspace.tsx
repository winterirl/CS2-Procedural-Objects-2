import { usePO2Localization } from "@/l10n/localization";
import { MenuButton as Button } from "@/components/common/menu-button";
import styles from "@/styles/content.module.scss";
import layout from "@/styles/layout.module.scss";

type SelectWorkspaceProps = {
    showObjectMarkers: boolean;
    onToggleMarkers: () => void;
};

export const SelectWorkspace = ({
    showObjectMarkers,
    onToggleMarkers,
}: SelectWorkspaceProps) => {
    const { t } = usePO2Localization();
    return (
        <div className={layout.stackRoomy}>
            <Button
                selected={showObjectMarkers}
                onSelect={onToggleMarkers}
                className={layout.wide}
            >{t("PO2.Select.Markers.Toggle.Label", { state: showObjectMarkers ? t("PO2.Common.On") : t("PO2.Common.Off") })}</Button>
            <div className={styles.instruction}>
                {t("PO2.Select.Target.Instruction")}
            </div>
        </div>
    );
};
