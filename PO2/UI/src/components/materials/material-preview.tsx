import { usePO2Localization } from "@/l10n/localization";
import typography from "@/styles/typography.module.scss";
import styles from "@/styles/materials.module.scss";

type MaterialPreviewProps = {
    url: string;
    status: string;
    alt: string;
    framed?: boolean;
};

export const MaterialPreview = ({
    url,
    status,
    alt,
    framed = false,
}: MaterialPreviewProps) => {
    const { t } = usePO2Localization();
    if (url.length === 0 && status.length === 0) {
        return null;
    }

    const rootClass = `${styles.previewRoot} ${framed ? styles.previewFramed : ""}`;

    return (
        <div className={rootClass}>
            {url.length > 0 ? (
                <img
                    src={url}
                    alt={alt}
                    draggable={false}
                />
            ) : (
                <div className={styles.previewUnavailable}>
                    {t("PO2.MaterialPreview.Status.Unavailable")}
                </div>
            )}
            {status.length > 0 && <span className={typography.small}>{status}</span>}
        </div>
    );
};
