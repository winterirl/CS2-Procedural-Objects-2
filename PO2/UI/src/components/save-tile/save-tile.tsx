import { usePO2Localization } from "@/l10n/localization";
import type { ReactNode } from "react";
import { LocalizedDate } from "cs2/l10n";
import type { SaveTileData } from "@/utils/ui-payloads";
import { formatSaveTimestamp } from "@/utils/format";
import styles from "./save-tile.module.scss";

type SaveTileProps = {
    save: SaveTileData;
    actions?: ReactNode;
    selection?: ReactNode;
    details?: ReactNode;
};

// Recovery and management share the same details; callers supply their actions.
export const SaveTile = ({ save, actions, selection, details }: SaveTileProps) => {
    const { t, formatInteger, formatSaveBytes } = usePO2Localization();
    return (
        <div className={styles.tile}>
            {selection}
            <div className={styles.value}>
                {save.dateEstimated
                    ? t("PO2.SaveTile.Date.Estimated", { date: formatSaveTimestamp(save.savedAtUtc, t("PO2.Common.Unknown")) })
                    : formatSaveTimestamp(save.savedAtUtc, t("PO2.Common.Unknown"))}
            </div>
            <div className={styles.detail}>
                <span className={styles.fieldName}>{t("PO2.SaveTile.CityName.Label")}</span>
                <span className={styles.value}>{save.cityName.trim() || t("PO2.Common.Unknown")}</span>
            </div>
            <div className={styles.detail}>
                <span className={styles.fieldName}>{t("PO2.SaveTile.CityDate.Label")}</span>
                <span className={styles.value}>
                    {save.cityDate ? <LocalizedDate value={save.cityDate} /> : t("PO2.Common.Unknown")}
                </span>
            </div>
            <div className={styles.detail}>
                <span className={styles.fieldName}>{t("PO2.SaveTile.SaveName.Label")}</span>
                <span className={styles.value}>{save.saveName.trim() || t("PO2.Common.Unknown")}</span>
            </div>
            <div className={styles.stats}>
                <span>{t("PO2.SaveTile.ObjectCount.Summary", { count: formatInteger(save.objectCount) })}</span>
                <span>{formatSaveBytes(save.bytes)}</span>
            </div>
            {details}
            {actions}
        </div>
    );
};
