import { usePO2Localization } from "@/l10n/localization";
import { useReportHelpTopics } from "@/help/help-local-context";
import { useState } from "react";
import { MenuButton as Button } from "@/components/common/menu-button";
import type { MaterialActionHandler } from "@/components/materials/material-controls-types";
import { MaterialScalarControl } from "@/components/materials/material-scalar-control";
import styles from "@/styles/materials.module.scss";

type MaterialPreviewTunerProps = {
    exposure: number;
    indirectDiffuse: number;
    reflectionLighting: number;
    keyIntensity: number;
    fillIntensity: number;
    onAction: MaterialActionHandler;
};

export const MaterialPreviewTuner = ({
    exposure,
    indirectDiffuse,
    reflectionLighting,
    keyIntensity,
    fillIntensity,
    onAction,
}: MaterialPreviewTunerProps) => {
    const { t } = usePO2Localization();
    const [expanded, setExpanded] = useState(false);
    useReportHelpTopics("preview", expanded ? "materialPreviewLighting" : null);

    return (
        <div className={styles.tunerRoot}>
            <Button
                className={styles.tunerToggle}
                onSelect={() => setExpanded((current) => !current)}
            >
                {t("PO2.MaterialPreview.Lighting.Toggle.Action")}
            </Button>

            {expanded && (
                <div className={styles.tunerContent}>
                    <MaterialScalarControl
                        label={t("PO2.MaterialPreview.Lighting.Exposure.Label")}
                        value={exposure}
                        digits={1}
                        resetKey="preview-exposure"
                        onDecrease={() => onAction("decreaseMaterialPreviewExposure")}
                        onCommit={(value) => onAction(
                            "setMaterialPreviewExposure",
                            value,
                        )}
                        onIncrease={() => onAction("increaseMaterialPreviewExposure")}
                    />
                    <MaterialScalarControl
                        label={t("PO2.MaterialPreview.Lighting.IndirectDiffuse.Label")}
                        value={indirectDiffuse}
                        digits={1}
                        resetKey="preview-indirect-diffuse"
                        onDecrease={() => onAction("decreaseMaterialPreviewIndirectDiffuse")}
                        onCommit={(value) => onAction(
                            "setMaterialPreviewIndirectDiffuse",
                            value,
                        )}
                        onIncrease={() => onAction("increaseMaterialPreviewIndirectDiffuse")}
                    />
                    <MaterialScalarControl
                        label={t("PO2.MaterialPreview.Lighting.Reflection.Label")}
                        value={reflectionLighting}
                        digits={1}
                        resetKey="preview-reflection"
                        onDecrease={() => onAction("decreaseMaterialPreviewReflectionLighting")}
                        onCommit={(value) => onAction(
                            "setMaterialPreviewReflectionLighting",
                            value,
                        )}
                        onIncrease={() => onAction("increaseMaterialPreviewReflectionLighting")}
                    />
                    <MaterialScalarControl
                        label={t("PO2.MaterialPreview.Lighting.KeyIntensity.Label")}
                        value={keyIntensity}
                        digits={0}
                        resetKey="preview-key-intensity"
                        onDecrease={() => onAction("decreaseMaterialPreviewKeyIntensity")}
                        onCommit={(value) => onAction(
                            "setMaterialPreviewKeyIntensity",
                            value,
                        )}
                        onIncrease={() => onAction("increaseMaterialPreviewKeyIntensity")}
                    />
                    <MaterialScalarControl
                        label={t("PO2.MaterialPreview.Lighting.FillIntensity.Label")}
                        value={fillIntensity}
                        digits={0}
                        resetKey="preview-fill-intensity"
                        onDecrease={() => onAction("decreaseMaterialPreviewFillIntensity")}
                        onCommit={(value) => onAction(
                            "setMaterialPreviewFillIntensity",
                            value,
                        )}
                        onIncrease={() => onAction("increaseMaterialPreviewFillIntensity")}
                    />
                    <Button
                        onSelect={() => onAction("resetMaterialPreviewLighting")}
                    >
                        {t("PO2.MaterialPreview.Lighting.Reset.Action")}
                    </Button>
                </div>
            )}
        </div>
    );
};
