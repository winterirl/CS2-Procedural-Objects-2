import { usePO2Localization } from "@/l10n/localization";
import typography from "@/styles/typography.module.scss";
import { TextInput } from "@/components/common/text-input";
import { MenuButton as Button } from "@/components/common/menu-button";
import { useEffect, useState } from "react";
import type { CustomOrientation } from "@/utils/ui-payloads";
import selectedStyles from "@/components/selected-object/selected-object.module.scss";
import styles from "@/styles/content.module.scss";
import controls from "@/styles/controls.module.scss";
import layout from "@/styles/layout.module.scss";

type CustomAxisControlsProps = {
    selectedObjectId: number;
    selectedObjectLocked: boolean;
    customOrientations: CustomOrientation[];
    activeCustomOrientation: CustomOrientation | null;
    canBeginAxisPicking: boolean;
    onAction: (action: string, ...args: Array<string | number>) => void;
};

export const CustomAxisControls = ({
    selectedObjectId,
    selectedObjectLocked,
    customOrientations,
    activeCustomOrientation,
    canBeginAxisPicking,
    onAction,
}: CustomAxisControlsProps) => {
    const { t } = usePO2Localization();
    const [nameValue, setNameValue] = useState(activeCustomOrientation?.name ?? "");

    useEffect(() => {
        setNameValue(activeCustomOrientation?.name ?? "");
    }, [activeCustomOrientation?.index, activeCustomOrientation?.name, selectedObjectId]);

    const submitRename = () => {
        if (activeCustomOrientation === null || selectedObjectLocked) {
            return;
        }

        const nextName = nameValue.trim();
        if (nextName.length === 0 || nextName === activeCustomOrientation.name) {
            setNameValue(activeCustomOrientation.name);
            return;
        }

        onAction("renameActiveCustomOrientation", nextName);
    };

    return (
        <div className={layout.stack}>
            <div className={styles.sectionHeader}>
                <span>{t("PO2.CustomAxis.Title")}</span>
                <span className={typography.small}>{customOrientations.length}/12</span>
            </div>

            {customOrientations.length === 0 && (
                <div className={typography.small}>{t("PO2.CustomAxis.Empty.Instruction")}</div>
            )}

            {activeCustomOrientation !== null && (
                <div className={selectedStyles.row}>
                    <TextInput
                        className={controls.growInput}
                        value={nameValue}
                        disabled={selectedObjectLocked}
                        maxLength={64}
                        onChange={(event) => setNameValue(event.currentTarget.value)}
                        onBlur={submitRename}
                        onKeyDown={(event) => {
                            if (event.key === "Enter") {
                                event.currentTarget.blur();
                            } else if (event.key === "Escape") {
                                setNameValue(activeCustomOrientation.name);
                                event.preventDefault();
                            }
                        }}
                        aria-label={t("PO2.CustomAxis.Name.AccessibleLabel")}
                    />
                    <Button
                        onSelect={() => onAction("deleteActiveCustomOrientation")}
                        disabled={selectedObjectLocked}
                    >
                        {t("PO2.Common.Delete")}
                    </Button>
                </div>
            )}

            <Button
                onSelect={() => onAction("beginAxisPicking")}
                disabled={!canBeginAxisPicking}
                className={layout.wide}
            >
                {t("PO2.CustomAxis.Create.Action")}
            </Button>
            {!canBeginAxisPicking && customOrientations.length >= 12 && (
                <div className={typography.small}>
                    {t("PO2.CustomAxis.CapacityNotice")}
                </div>
            )}
        </div>
    );
};
