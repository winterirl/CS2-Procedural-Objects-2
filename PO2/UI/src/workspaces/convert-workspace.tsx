import { usePO2Localization } from "@/l10n/localization";
import { AssetConversionPhase } from "@/bindings/operation-state";
import styles from "@/styles/content.module.scss";

type ConvertWorkspaceProps = {
    conversionPhase: AssetConversionPhase;
    conversionStatus: string;
};

export const ConvertWorkspace = ({
    conversionPhase,
    conversionStatus,
}: ConvertWorkspaceProps) => {
    const { t } = usePO2Localization();
    const showInstructions = conversionPhase === AssetConversionPhase.Ready;
    const fallback = conversionPhase === AssetConversionPhase.Cancelling
        ? t("PO2.Convert.Status.Cancelling")
        : conversionPhase === AssetConversionPhase.Failed
            ? t("PO2.Convert.Status.Failed")
            : t("PO2.Convert.Status.Converting");
    if (conversionPhase === AssetConversionPhase.Inactive) return null;

    return (
        <div className={styles.createWorkspace}>
            {showInstructions ? (
                <>
                    <div className={styles.instruction}>
                        {t("PO2.Convert.Target.Instruction")}
                    </div>
                    <div className={styles.instruction}>
                        {t("PO2.Convert.Target.UnsupportedAssetsNotice")}
                    </div>
                </>
            ) : (
                <div className={styles.instruction}>
                    {conversionStatus || fallback}
                </div>
            )}
        </div>
    );
};
