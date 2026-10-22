import { usePO2Localization } from "@/l10n/localization";
import { Dropdown, DropdownItem, DropdownToggle } from "cs2/ui";
import { getNativeDropdownTheme } from "@/components/common/native-dropdown-theme";
import { NavigationButton } from "@/components/common/navigation-button";
import styles from "./selected-object.module.scss";

type SurfaceAreaNavigationProps = {
    ids: number[];
    selectedId: number;
    onAction: (action: string, ...args: number[]) => void;
};

export const SurfaceAreaNavigation = ({
    ids,
    selectedId,
    onAction,
}: SurfaceAreaNavigationProps) => {
    const { t } = usePO2Localization();
    const areaName = (id: number) => id === 0 ? t("PO2.SurfaceArea.Name.Base") : t("PO2.SurfaceArea.Name.Numbered", { id });
    const theme = getNativeDropdownTheme();
    return (
        <div className={styles.row}>
            <NavigationButton
                direction="previous"
                label={t("PO2.SurfaceArea.Navigation.Previous.Action")}
                disabled={ids.length < 2}
                onSelect={() => onAction("previousSelectedSurfaceArea")}
            />
            <div className={styles.dropdown}>
                <Dropdown
                    alignment="left"
                    theme={{
                        ...theme,
                        dropdownPopup: `${theme.dropdownPopup || ""} po2-ui ${styles.dropdownPopup}`,
                    }}
                    content={(
                        <>
                            {ids.map((id) => (
                                <DropdownItem
                                    key={id}
                                    value={id}
                                    selected={id === selectedId}
                                    closeOnSelect
                                    onChange={(value) => onAction("setSelectedSurfaceArea", value)}
                                    onToggleSelected={(value) => onAction("setSelectedSurfaceArea", value)}
                                >
                                    {areaName(id)}
                                </DropdownItem>
                            ))}
                        </>
                    )}
                >
                    <DropdownToggle disabled={ids.length === 0}>
                        {areaName(selectedId)}
                    </DropdownToggle>
                </Dropdown>
            </div>
            <NavigationButton
                direction="next"
                label={t("PO2.SurfaceArea.Navigation.Next.Action")}
                disabled={ids.length < 2}
                onSelect={() => onAction("nextSelectedSurfaceArea")}
            />
        </div>
    );
};
