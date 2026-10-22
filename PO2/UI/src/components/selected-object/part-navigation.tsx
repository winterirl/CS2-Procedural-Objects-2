import { usePO2Localization } from "@/l10n/localization";
import { useReportHelpTopics } from "@/help/help-local-context";
import { useEffect, useState } from "react";
import { Dropdown, DropdownItem, DropdownToggle } from "cs2/ui";
import { getNativeDropdownTheme } from "@/components/common/native-dropdown-theme";
import { NavigationButton } from "@/components/common/navigation-button";
import { MenuButton } from "@/components/common/menu-button";
import { TextInput } from "@/components/common/text-input";
import type { MeshPartEntry } from "@/utils/ui-payloads";
import styles from "./selected-object.module.scss";
import layout from "@/styles/layout.module.scss";

type PartNavigationProps = {
    objectId: number;
    index: number;
    name: string;
    parts: MeshPartEntry[];
    canRename: boolean;
    disabled: boolean;
    showDivider?: boolean;
    onAction: (action: string, ...args: Array<string | number>) => void;
};

export const PartNavigation = ({
    objectId,
    index,
    name,
    parts,
    canRename,
    disabled,
    showDivider = true,
    onAction,
}: PartNavigationProps) => {
    const { t } = usePO2Localization();
    const [renaming, setRenaming] = useState(false);
    useReportHelpTopics("partName", renaming ? "renamePart" : null);
    const [draft, setDraft] = useState(name);
    useEffect(() => {
        setDraft(name);
        setRenaming(false);
    }, [objectId, index, name]);
    const cancel = () => {
        setDraft(name);
        setRenaming(false);
    };
    const submit = () => {
        const next = draft.trim();
        if (!disabled && canRename && next && next !== name) onAction("renameSelectedPart", next);
        cancel();
    };
    const theme = getNativeDropdownTheme();
    return (
        <div className={styles.section}>
            {showDivider && <div className={layout.divider} />}
            <div>{t("PO2.Parts.Navigation.Label")}</div>
            <div className={styles.row}>
                <NavigationButton
                    direction="previous"
                    label={t("PO2.Parts.Navigation.Previous.Action")}
                    disabled={disabled || parts.length < 2 || renaming}
                    onSelect={() => onAction("previousSelectedPart")}
                />
                {renaming ? (
                    <TextInput
                        className={styles.name}
                        value={draft}
                        maxLength={96}
                        autoFocus
                        disabled={disabled}
                        onChange={(event) => setDraft(event.currentTarget.value)}
                        onKeyDown={(event) => {
                            if (event.key === "Enter") submit();
                            else if (event.key === "Escape") {
                                cancel();
                                event.preventDefault();
                            }
                        }}
                        aria-label={t("PO2.Parts.Name.AccessibleLabel")}
                    />
                ) : (
                    <div className={styles.dropdown}>
                        <Dropdown
                            alignment="left"
                            theme={{
                                ...theme,
                                dropdownPopup: `${theme.dropdownPopup || ""} po2-ui ${styles.dropdownPopup}`,
                            }}
                            content={
                                <>
                                    {parts.map((part) => (
                                        <DropdownItem
                                            key={part.index}
                                            value={part.index}
                                            selected={part.index === index}
                                            disabled={disabled}
                                            closeOnSelect
                                            onChange={(value) => onAction("setSelectedPart", value)}
                                            onToggleSelected={(value) =>
                                                onAction("setSelectedPart", value)
                                            }
                                        >
                                            {part.name}
                                        </DropdownItem>
                                    ))}
                                </>
                            }
                        >
                            <DropdownToggle disabled={disabled || parts.length === 0}>
                                {name}
                            </DropdownToggle>
                        </Dropdown>
                    </div>
                )}
                <NavigationButton
                    direction="next"
                    label={t("PO2.Parts.Navigation.Next.Action")}
                    disabled={disabled || parts.length < 2 || renaming}
                    onSelect={() => onAction("nextSelectedPart")}
                />
                <MenuButton
                    selected={renaming}
                    disabled={disabled || !canRename}
                    onSelect={() => (renaming ? cancel() : setRenaming(true))}
                >
                    {renaming ? t("PO2.Common.Cancel") : t("PO2.Common.Rename")}
                </MenuButton>
            </div>
        </div>
    );
};
