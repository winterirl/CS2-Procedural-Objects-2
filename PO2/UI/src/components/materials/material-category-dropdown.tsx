import { usePO2Localization } from "@/l10n/localization";
import { Dropdown, DropdownItem, DropdownToggle } from "cs2/ui";
import { getNativeDropdownTheme } from "@/components/common/native-dropdown-theme";
import styles from "@/styles/materials.module.scss";

type MaterialCategoryDropdownProps = {
    categories: string[];
    value: string | null;
    allLabel?: string;
    onSelectAll?: () => void;
    disabled?: boolean;
    onChange: (category: string) => void;
};

export const MaterialCategoryDropdown = ({
    categories,
    value,
    disabled = false,
    allLabel,
    onSelectAll,
    onChange,
}: MaterialCategoryDropdownProps) => {
    const { t } = usePO2Localization();
    const label = allLabel ?? t("PO2.Common.All");
    const nativeTheme = getNativeDropdownTheme();
    return (
        <div className={styles.categoryDropdown}>
            <Dropdown
                alignment="left"
                theme={{
                    ...nativeTheme,
                    dropdownPopup: `${nativeTheme.dropdownPopup || ""} po2-ui ${styles.categoryDropdownPopup}`,
                }}
                content={(
                    <>
                        {onSelectAll && (
                            <DropdownItem
                                key="all-categories"
                                value={null}
                                selected={value === null}
                                closeOnSelect
                                onChange={onSelectAll}
                                onToggleSelected={onSelectAll}
                            >
                                {label}
                            </DropdownItem>
                        )}
                        {categories.map((category) => (
                            <DropdownItem
                                key={`category:${category}`}
                                value={category}
                                selected={category === value}
                                closeOnSelect={true}
                                onChange={onChange}
                                onToggleSelected={onChange}
                            >
                                {category}
                            </DropdownItem>
                        ))}
                    </>
                )}
            >
                <DropdownToggle
                    disabled={disabled}

                >
                    {value === null ? label : value}
                </DropdownToggle>
            </Dropdown>
        </div>
    );
};
