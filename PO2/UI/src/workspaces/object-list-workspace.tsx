import { usePO2Localization } from "@/l10n/localization";
import { InputLabel } from "@/components/common/input-label";
import { TextInput } from "@/components/common/text-input";
import { useMemo, useState } from "react";
import { ScrollArea } from "@/components/common/scroll-area";
import { MenuButton as Button } from "@/components/common/menu-button";
import type { ProceduralObjectListEntry } from "@/utils/ui-payloads";
import styles from "@/styles/content.module.scss";

type ObjectListWorkspaceProps = {
    entries: ProceduralObjectListEntry[];
    selectedObjectId: number;
    onSelect: (objectId: number) => void;
};

export const ObjectListWorkspace = ({
    entries,
    selectedObjectId,
    onSelect,
}: ObjectListWorkspaceProps) => {
    const { t } = usePO2Localization();
    const [search, setSearch] = useState("");
    const visibleEntries = useMemo(() => {
        const normalizedSearch = search.trim().toLocaleLowerCase();
        if (normalizedSearch.length === 0) {
            return entries;
        }

        return entries.filter((entry) =>
            entry.name.toLocaleLowerCase().includes(normalizedSearch),
        );
    }, [entries, search]);

    return (
        <div className={styles.objectListWorkspace}>
            <InputLabel className={styles.objectListSearch}>
                <span>{t("PO2.ObjectList.Search.Label")}</span>
                <TextInput
                    className={styles.objectListSearchInput}
                    value={search}
                    onChange={(event) => setSearch(event.currentTarget.value)}
                    placeholder={t("PO2.ObjectList.Search.Placeholder")}
                    aria-label={t("PO2.ObjectList.Search.AccessibleLabel")}
                />
            </InputLabel>

            <ScrollArea>
                <div className={styles.objectList}>
                    {visibleEntries.length > 0 ? (
                        visibleEntries.map((entry) => (
                            <Button
                                key={entry.objectId}
                                selected={entry.objectId === selectedObjectId}
                                onSelect={() => onSelect(entry.objectId)}
                                className={styles.objectListButton}
                                title={entry.name}
                            >
                                {entry.name}
                            </Button>
                        ))
                    ) : (
                        <div className={styles.instruction}>
                            {entries.length === 0
                                ? t("PO2.ObjectList.Empty.Scene")
                                : t("PO2.ObjectList.Empty.Search")}
                        </div>
                    )}
                </div>
            </ScrollArea>
        </div>
    );
};
