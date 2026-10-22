import { usePO2Localization } from "@/l10n/localization";
import { MaterialImportMessageKind } from "@/bindings/operation-state";
import { useReportHelpTopics } from "@/help/help-local-context";
import { InputLabel } from "@/components/common/input-label";
import typography from "@/styles/typography.module.scss";
import { TextInput } from "@/components/common/text-input";
import { useEffect, useState } from "react";
import { MenuButton as Button } from "@/components/common/menu-button";
import type {
    MaterialActionHandler,
    MaterialAuthoringState,
    MaterialPreviewState,
} from "@/components/materials/material-controls-types";
import { MaterialCategoryDropdown } from "@/components/materials/material-category-dropdown";
import { MaterialPreview } from "@/components/materials/material-preview";
import { MaterialPreviewTuner } from "@/components/materials/material-preview-tuner";
import { MaterialScalarControl } from "@/components/materials/material-scalar-control";
import content from "@/styles/content.module.scss";
import styles from "@/styles/materials.module.scss";

type MaterialAuthoringViewProps = {
    editMode: boolean;
    categories: string[];
    sourceDirectory: string;
    authoring: MaterialAuthoringState;
    preview: MaterialPreviewState;
    onSourceDirectoryChange: (sourceDirectory: string) => void;
    onAction: MaterialActionHandler;
};

export const MaterialAuthoringView = ({
    editMode,
    categories,
    sourceDirectory,
    authoring,
    preview,
    onSourceDirectoryChange,
    onAction,
}: MaterialAuthoringViewProps) => {
    const { t } = usePO2Localization();
    const [creatingCategory, setCreatingCategory] = useState(false);
    useReportHelpTopics("authoring", creatingCategory ? "materialCategoryCreate" : null);
    const [previousCategory, setPreviousCategory] = useState("");
    const importMode = !editMode;
    const formActive = editMode || authoring.candidateCount > 0;
    const categoryOptions = Array.from(new Set([
        authoring.category,
        ...categories,
    ].filter((category) => category.length > 0)));
    const inputResetKey = `${editMode ? "edit" : "import"}:${authoring.selectedCandidateIndex}`;

    useEffect(() => {
        setCreatingCategory(false);
        setPreviousCategory("");
    }, [editMode, authoring.selectedCandidateIndex]);

    const scanSourceDirectory = () => {
        const trimmedDirectory = sourceDirectory.trim();
        if (trimmedDirectory.length === 0 || authoring.busy) {
            return;
        }

        onAction("scanMaterialImportDirectory", trimmedDirectory);
    };

    const selectCandidate = (index: number) => {
        if (!Number.isInteger(index) ||
            index < 0 ||
            index >= authoring.candidateCount ||
            authoring.busy) {
            return;
        }

        onAction("selectMaterialImportCandidate", index);
    };

    const save = () => {
        if (authoring.displayName.trim().length === 0 ||
            authoring.category.trim().length === 0 ||
            authoring.busy) {
            return;
        }

        if (editMode) {
            onAction("saveManagedMaterialEdit");
            return;
        }

        if (authoring.selectedCandidateIndex >= 0) {
            onAction("saveMaterialImport");
        }
    };

    const cancel = () => {
        if (!authoring.busy) {
            onAction("cancelMaterialImportView");
        }
    };

    const toggleNewCategory = () => {
        if (authoring.busy) {
            return;
        }

        if (creatingCategory) {
            onAction("setMaterialImportCategory", previousCategory);
            setCreatingCategory(false);
            setPreviousCategory("");
            return;
        }

        setPreviousCategory(authoring.category);
        setCreatingCategory(true);
        onAction("setMaterialImportCategory", "");
    };

    const sourceControls = (
        <>
            <div className={typography.label}>{t("PO2.MaterialEditor.SourceFolder.Label")}</div>
            <div className={styles.sourceRow}>
                <TextInput
                    value={sourceDirectory}
                    disabled={authoring.busy}
                    onChange={(event) => onSourceDirectoryChange(
                        event.currentTarget.value,
                    )}
                    onKeyDown={(event) => {
                        if (event.key === "Enter") {
                            scanSourceDirectory();
                        }
                    }}
                    placeholder={t("PO2.MaterialEditor.SourceFolder.Placeholder")}
                    aria-label={t("PO2.MaterialEditor.SourceFolder.AccessibleLabel")}
                />
                <Button
                    onSelect={scanSourceDirectory}

                    disabled={
                        authoring.busy ||
                        sourceDirectory.trim().length === 0
                    }
                >
                    {formActive ? t("PO2.MaterialEditor.Rescan.Action") : t("PO2.MaterialEditor.Scan.Action")}
                </Button>
            </div>
        </>
    );

    return (
        <div className={styles.authoringRoot}>
            <div className={styles.authoringPanel}>
                {!formActive && importMode && (
                    <div className={styles.initialScan}>
                        {sourceControls}
                        <div className={content.instruction}>
                            {t("PO2.MaterialEditor.ImportRequirements.SingleMaterial")}
                        </div>
                        <div className={content.instruction}>
                            {t("PO2.MaterialEditor.ImportRequirements.ImageFormat")}
                        </div>
                        <div className={content.instruction}>
                            {t("PO2.MaterialEditor.ImportRequirements.TextureNames")}
                        </div>
                        {authoring.status.length > 0 && (
                            <div className={typography.small}>{authoring.status}</div>
                        )}
                    </div>
                )}

                {formActive && (
                    <div className={styles.authoringColumns}>
                        <div className={styles.authoringSettings}>
                            {importMode && sourceControls}

                            {importMode && (
                                <div className={styles.field}>
                                    <span>{t("PO2.MaterialEditor.DetectedMaterial.Label")}</span>
                                    <div className={styles.candidateName}>
                                        {authoring.selectedCandidateIndex >= 0 &&
                                        authoring.selectedCandidateIndex < authoring.candidates.length
                                            ? authoring.candidates[authoring.selectedCandidateIndex]
                                            : t("PO2.Common.None")}
                                    </div>
                                    {authoring.candidateCount > 1 && (
                                        <div className={styles.pairedActions}>
                                            <Button
                                                onSelect={() => selectCandidate(
                                                    authoring.selectedCandidateIndex <= 0
                                                        ? authoring.candidateCount - 1
                                                        : authoring.selectedCandidateIndex - 1,
                                                )}
                                                disabled={authoring.busy}
                                            >
                                                {t("PO2.Common.Previous")}
                                            </Button>
                                            <span>
                                                {authoring.selectedCandidateIndex + 1}/
                                                {authoring.candidateCount}
                                            </span>
                                            <Button
                                                onSelect={() => selectCandidate(
                                                    authoring.selectedCandidateIndex >=
                                                    authoring.candidateCount - 1
                                                        ? 0
                                                        : authoring.selectedCandidateIndex + 1,
                                                )}
                                                disabled={authoring.busy}
                                            >
                                                {t("PO2.Common.Next")}
                                            </Button>
                                        </div>
                                    )}
                                </div>
                            )}

                            <div className={styles.files}>
                                <div>
                                    <span>{t("PO2.MaterialEditor.BaseColorTexture.Label")}</span>
                                    <span>{authoring.baseColorName}</span>
                                </div>
                                <div>
                                    <span>{t("PO2.MaterialEditor.NormalTexture.Label")}</span>
                                    <span>{authoring.normalName}</span>
                                </div>
                            </div>

                            <InputLabel className={styles.formRow}>
                                <span>{t("PO2.MaterialEditor.Name.Label")}</span>
                                <TextInput
                                    value={authoring.displayName}
                                    disabled={authoring.busy}
                                    maxLength={96}
                                    onChange={(event) => onAction(
                                        "setMaterialImportDisplayName",
                                        event.currentTarget.value,
                                    )}
                                    aria-label={t("PO2.MaterialEditor.Name.AccessibleLabel")}
                                />
                            </InputLabel>

                            <div className={styles.formRow}>
                                <span>{t("PO2.MaterialEditor.Category.Label")}</span>
                                <div className={styles.categoryControl}>
                                    {creatingCategory ? (
                                        <TextInput
                                            className={styles.categoryInput}
                                            value={authoring.category}
                                            disabled={authoring.busy}
                                            maxLength={64}
                                            autoFocus
                                            onChange={(event) => onAction(
                                                "setMaterialImportCategory",
                                                event.currentTarget.value,
                                            )}
                                            onKeyDown={(event) => {
                                                if (event.key === "Escape") {
                                                    toggleNewCategory();
                                                    event.preventDefault();
                                                }
                                            }}
                                            placeholder={t("PO2.MaterialEditor.NewCategory.Placeholder")}
                                            aria-label={t("PO2.MaterialEditor.NewCategory.AccessibleLabel")}
                                        />
                                    ) : (
                                        <MaterialCategoryDropdown
                                            categories={categoryOptions}
                                            value={authoring.category}
                                            disabled={authoring.busy}
                                            onChange={(category) => onAction(
                                                "setMaterialImportCategory",
                                                category,
                                            )}
                                        />
                                    )}
                                    <Button
                                        selected={creatingCategory}
                                        onSelect={toggleNewCategory}
                                        className={styles.categoryToggle}
                                        disabled={authoring.busy}
                                        title={creatingCategory
                                            ? t("PO2.MaterialEditor.NewCategory.Cancel.Action")
                                            : t("PO2.MaterialEditor.NewCategory.Create.Action")}
                                        aria-label={creatingCategory
                                            ? t("PO2.MaterialEditor.NewCategory.Cancel.Action")
                                            : t("PO2.MaterialEditor.NewCategory.Create.Action")}
                                    >
                                        +
                                    </Button>
                                </div>
                            </div>

                            <div className={`${styles.field} ${styles.mappingField}`}>
                                <span>{t("PO2.MaterialEditor.Mapping.Label")}</span>
                                <div className={`${styles.pairedActions} ${styles.mappingActions}`}>
                                    <Button
                                        selected={authoring.mappingMode === 1}
                                        onSelect={() => onAction(
                                            "setMaterialImportMappingRepeat",
                                        )}
                                        disabled={authoring.busy}
                                    >
                                        {t("PO2.MaterialEditor.Mapping.Choice.Repeat")}
                                    </Button>
                                    <Button
                                        selected={authoring.mappingMode === 2}
                                        onSelect={() => onAction(
                                            "setMaterialImportMappingStretch",
                                        )}
                                        disabled={authoring.busy}
                                    >
                                        {t("PO2.MaterialEditor.Mapping.Choice.Stretch")}
                                    </Button>
                                </div>
                            </div>

                            <div className={styles.field}>
                                <span>{t("PO2.MaterialEditor.PhysicalSize.Title")}</span>
                                <MaterialScalarControl
                                    label={t("PO2.MaterialEditor.PhysicalSize.Width.Label")}
                                    value={authoring.physicalWidth}
                                    digits={1}
                                    resetKey={`${inputResetKey}:width`}
                                    minimum={0.1}
                                    disabled={authoring.busy}
                                    onDecrease={() => onAction(
                                        "decreaseMaterialImportWidth",
                                    )}
                                    onCommit={(value) => onAction(
                                        "setMaterialImportWidth",
                                        value,
                                    )}
                                    onIncrease={() => onAction(
                                        "increaseMaterialImportWidth",
                                    )}
                                />
                                <MaterialScalarControl
                                    label={t("PO2.MaterialEditor.PhysicalSize.Height.Label")}
                                    value={authoring.physicalHeight}
                                    digits={1}
                                    resetKey={`${inputResetKey}:height`}
                                    minimum={0.1}
                                    disabled={authoring.busy}
                                    onDecrease={() => onAction(
                                        "decreaseMaterialImportHeight",
                                    )}
                                    onCommit={(value) => onAction(
                                        "setMaterialImportHeight",
                                        value,
                                    )}
                                    onIncrease={() => onAction(
                                        "increaseMaterialImportHeight",
                                    )}
                                />
                            </div>

                            <div className={styles.field}>
                                <span>{t("PO2.MaterialEditor.Defaults.Title")}</span>
                                <MaterialScalarControl
                                    label={t("PO2.MaterialEditor.Defaults.Metallic.Label")}
                                    value={authoring.metallic}
                                    digits={2}
                                    resetKey={`${inputResetKey}:metallic`}
                                    minimum={0}
                                    maximum={1}
                                    disabled={authoring.busy}
                                    onDecrease={() => onAction(
                                        "decreaseMaterialImportMetallic",
                                    )}
                                    onCommit={(value) => onAction(
                                        "setMaterialImportMetallic",
                                        value,
                                    )}
                                    onIncrease={() => onAction(
                                        "increaseMaterialImportMetallic",
                                    )}
                                />
                                <MaterialScalarControl
                                    label={t("PO2.MaterialEditor.Defaults.Smoothness.Label")}
                                    value={authoring.smoothness}
                                    digits={2}
                                    resetKey={`${inputResetKey}:smoothness`}
                                    minimum={0}
                                    maximum={1}
                                    disabled={authoring.busy}
                                    onDecrease={() => onAction(
                                        "decreaseMaterialImportSmoothness",
                                    )}
                                    onCommit={(value) => onAction(
                                        "setMaterialImportSmoothness",
                                        value,
                                    )}
                                    onIncrease={() => onAction(
                                        "increaseMaterialImportSmoothness",
                                    )}
                                />
                                <MaterialScalarControl
                                    label={t("PO2.MaterialEditor.Defaults.Coat.Label")}
                                    value={authoring.coatStrength}
                                    digits={2}
                                    resetKey={`${inputResetKey}:coat`}
                                    minimum={0}
                                    maximum={1}
                                    disabled={authoring.busy}
                                    onDecrease={() => onAction(
                                        "decreaseMaterialImportCoatStrength",
                                    )}
                                    onCommit={(value) => onAction(
                                        "setMaterialImportCoatStrength",
                                        value,
                                    )}
                                    onIncrease={() => onAction(
                                        "increaseMaterialImportCoatStrength",
                                    )}
                                />
                            </div>

                            <div className={`${styles.actions} ${styles.authoringActions}`}>
                                <Button
                                    onSelect={cancel}
                                    disabled={authoring.busy}
                                >
                                    {t("PO2.Common.Cancel")}
                                </Button>
                                <Button
                                    onSelect={save}
                                    disabled={
                                        authoring.busy ||
                                        authoring.displayName.trim().length === 0 ||
                                        authoring.category.trim().length === 0
                                    }
                                >
                                    {authoring.busy
                                        ? t("PO2.MaterialEditor.Save.Status.Pending")
                                        : editMode
                                            ? t("PO2.MaterialEditor.SaveChanges.Action")
                                            : t("PO2.MaterialEditor.SaveNew.Action")}
                                </Button>
                            </div>

                            {authoring.status.length > 0 && authoring.messageKind !== MaterialImportMessageKind.ScanResult && (
                                <div className={typography.small}>{authoring.status}</div>
                            )}

                            {editMode && (
                                <div className={typography.small}>
                                    {t("PO2.MaterialEditor.Save.ExistingObjectsNotice")}
                                </div>
                            )}
                        </div>

                        <div className={styles.previewColumn}>
                            <MaterialPreview
                                url={preview.url}
                                status={preview.status}
                                alt={t("PO2.MaterialEditor.Preview.AccessibleLabel")}
                                framed
                            />

                            {preview.url.length > 0 && (
                                <MaterialPreviewTuner
                                    exposure={preview.exposure}
                                    indirectDiffuse={preview.indirectDiffuse}
                                    reflectionLighting={preview.reflectionLighting}
                                    keyIntensity={preview.keyIntensity}
                                    fillIntensity={preview.fillIntensity}
                                    onAction={onAction}
                                />
                            )}
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
};
