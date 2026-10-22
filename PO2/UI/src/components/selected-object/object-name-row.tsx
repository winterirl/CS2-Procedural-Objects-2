import { usePO2Localization } from "@/l10n/localization";
import { useReportHelpTopics } from "@/help/help-local-context";
import { TextInput } from "@/components/common/text-input";
import { useEffect, useState } from "react";
import { MenuButton as Button } from "@/components/common/menu-button";
import controls from "@/styles/controls.module.scss";
import styles from "./selected-object.module.scss";

type ObjectNameRowProps = {
    selectedObjectId: number;
    selectedObjectName: string;
    selectedObjectLocked: boolean;
    combineSourceActive: boolean;
    onRename: (name: string) => void;
};

export const ObjectNameRow = ({
    selectedObjectId,
    selectedObjectName,
    selectedObjectLocked,
    combineSourceActive,
    onRename,
}: ObjectNameRowProps) => {
    const { t } = usePO2Localization();
    const [renameActive, setRenameActive] = useState(false);
    useReportHelpTopics("objectName", renameActive ? "renameObject" : null);
    const [renameValue, setRenameValue] = useState(selectedObjectName);

    useEffect(() => {
        setRenameValue(selectedObjectName);
        setRenameActive(false);
    }, [selectedObjectId, selectedObjectName]);

    const cancelRename = () => {
        setRenameValue(selectedObjectName);
        setRenameActive(false);
    };

    const submitRename = () => {
        const nextName = renameValue.trim();
        if (nextName.length === 0 || selectedObjectLocked || combineSourceActive) {
            cancelRename();
            return;
        }

        onRename(nextName);
        setRenameActive(false);
    };

    return (
        <div className={styles.row}>
            {renameActive ? (
                <TextInput
                    className={controls.growInput}
                    value={renameValue}
                    maxLength={96}
                    onChange={(event) => setRenameValue(event.currentTarget.value)}
                    onKeyDown={(event) => {
                        if (event.key === "Enter") {
                            submitRename();
                        } else if (event.key === "Escape") {
                            cancelRename();
                            event.preventDefault();
                        }
                    }}
                    aria-label={t("PO2.Object.Name.AccessibleLabel")}
                />
            ) : (
                <div className={styles.name}>
                    {selectedObjectName || t("PO2.Object.Name.Fallback")}
                </div>
            )}

            <Button
                selected={renameActive}
                onSelect={() => {
                    if (renameActive) {
                        cancelRename();
                    } else {
                        setRenameValue(selectedObjectName);
                        setRenameActive(true);
                    }
                }}
                disabled={selectedObjectLocked || combineSourceActive}
            >
                {renameActive ? t("PO2.Common.Cancel") : t("PO2.Common.Rename")}
            </Button>
        </div>
    );
};
