import { usePO2Localization } from "@/l10n/localization";
import { ScrollArea } from "@/components/common/scroll-area";
import { InputLabel } from "@/components/common/input-label";
import typography from "@/styles/typography.module.scss";
import { TextInput } from "@/components/common/text-input";
import { MenuButton as Button } from "@/components/common/menu-button";
import type { MaterialLibraryEntry } from "@/utils/ui-payloads";
import type { MaterialActionHandler } from "@/components/materials/material-controls-types";
import { MaterialCategoryDropdown } from "@/components/materials/material-category-dropdown";
import { MaterialPreview } from "@/components/materials/material-preview";
import styles from "@/styles/materials.module.scss";
import content from "@/styles/content.module.scss";
import layout from "@/styles/layout.module.scss";

type MaterialLibraryViewProps = {
    entries: MaterialLibraryEntry[];
    categories: string[];
    search: string;
    category: string | null;
    selectedManagedMaterialId: string;
    materialPreviewUrl: string;
    materialPreviewStatus: string;
    managedMaterialDisplayName: string;
    managedMaterialCategory: string;
    managedMaterialSource: number;
    managedMaterialMappingMode: number;
    managedMaterialHasPhysicalSize: boolean;
    managedMaterialPhysicalWidth: number;
    managedMaterialPhysicalHeight: number;
    managedMaterialMetallic: number;
    managedMaterialSmoothness: number;
    managedMaterialCoatStrength: number;
    managedMaterialHasNormal: boolean;
    materialLibraryStatus: string;
    hasSelection: boolean;
    selectedObjectLocked: boolean;
    selectedSurfaceAreaId: number;
    canEditManagedMaterial: boolean;
    canDeleteManagedMaterial: boolean;
    materialImportBusy: boolean;
    deleteConfirming: boolean;
    onSearchChange: (value: string) => void;
    onCategoryChange: (category: string | null) => void;
    onDeleteConfirmingChange: (confirming: boolean) => void;
    onAction: MaterialActionHandler;
};

export const MaterialLibraryView = ({
    entries,
    categories,
    search,
    category,
    selectedManagedMaterialId,
    materialPreviewUrl,
    materialPreviewStatus,
    managedMaterialDisplayName,
    managedMaterialCategory,
    managedMaterialSource,
    managedMaterialMappingMode,
    managedMaterialHasPhysicalSize,
    managedMaterialPhysicalWidth,
    managedMaterialPhysicalHeight,
    managedMaterialMetallic,
    managedMaterialSmoothness,
    managedMaterialCoatStrength,
    managedMaterialHasNormal,
    materialLibraryStatus,
    hasSelection,
    selectedObjectLocked,
    selectedSurfaceAreaId,
    canEditManagedMaterial,
    canDeleteManagedMaterial,
    materialImportBusy,
    deleteConfirming,
    onSearchChange,
    onCategoryChange,
    onDeleteConfirmingChange,
    onAction,
}: MaterialLibraryViewProps) => {
    const { t, formatNumber } = usePO2Localization();
    return (
        <div className={styles.libraryRoot}>
            <InputLabel className={styles.controlRow}>
                <span>{t("PO2.Materials.Search.Label")}</span>
                <TextInput
                    className={styles.search}
                    value={search}
                    onChange={(event) => onSearchChange(event.currentTarget.value)}
                    placeholder={t("PO2.Materials.Search.Placeholder")}
                    aria-label={t("PO2.Materials.Search.AccessibleLabel")}
                />
            </InputLabel>

            <div className={styles.controlRow}>
                <span>{t("PO2.Materials.CategoryFilter.Label")}</span>
                <MaterialCategoryDropdown
                    categories={categories}
                    value={category}
                    onChange={onCategoryChange}
                    onSelectAll={() => onCategoryChange(null)}
                />
            </div>

            <div className={styles.browser}>
                <div className={styles.listColumn}>
                    <ScrollArea className={styles.listViewport}>
                        <div className={styles.list}>
                            {entries.length > 0 ? (
                                entries.map((entry) => (
                                    <Button
                                        key={entry.id}
                                        selected={selectedManagedMaterialId === entry.id}
                                        onSelect={() => onAction(
                                            "selectManagedMaterial",
                                            entry.id,
                                        )}
                                        className={styles.entry}
                                        labelClassName={styles.entryContent}
                                    >
                                        <span className={styles["entry-name"]}>
                                            {entry.displayName}
                                        </span>
                                        <span className={styles["entry-meta"]}>
                                            {entry.source === 1 ? t("PO2.Materials.Details.Source.User") : t("PO2.Materials.Details.Source.Bundled")}
                                        </span>
                                    </Button>
                                ))
                            ) : (
                                <div className={styles.empty}>
                                    {t("PO2.Materials.Results.Empty")}
                                </div>
                            )}
                        </div>
                    </ScrollArea>
                </div>

                <div className={styles.details}>
                    <MaterialPreview
                        url={materialPreviewUrl}
                        status={materialPreviewStatus}
                        alt={t("PO2.Materials.Preview.AccessibleLabel", { materialName: managedMaterialDisplayName || t("PO2.Materials.Details.Title") })}
                    />

                    {managedMaterialDisplayName.length > 0 ? (
                        <div className={styles["detail-content"]}>
                            <div className={typography.title}>
                                {managedMaterialDisplayName}
                            </div>
                            <div className={styles["detail-row"]}>
                                <span>{t("PO2.Materials.CategoryFilter.Label")}</span>
                                <span>{managedMaterialCategory}</span>
                            </div>
                            <div className={styles["detail-row"]}>
                                <span>{t("PO2.Materials.Details.Source.Label")}</span>
                                <span>{managedMaterialSource === 1
                                    ? t("PO2.Materials.Details.Source.User")
                                    : t("PO2.Materials.Details.Source.Bundled")}</span>
                            </div>
                            <div className={styles["detail-row"]}>
                                <span>{t("PO2.Materials.Details.Mapping.Label")}</span>
                                <span>{managedMaterialMappingMode === 1
                                    ? t("PO2.Materials.Details.Mapping.Repeat")
                                    : managedMaterialMappingMode === 2
                                        ? t("PO2.Materials.Details.Mapping.Stretch")
                                        : t("PO2.Materials.Details.Mapping.Original")}</span>
                            </div>
                            <div className={styles["detail-row"]}>
                                <span>{t("PO2.Materials.Details.PhysicalSize.Label")}</span>
                                <span>
                                    {managedMaterialHasPhysicalSize
                                        ? `${formatNumber(managedMaterialPhysicalWidth, 2)}m x ${formatNumber(managedMaterialPhysicalHeight, 2)}m`
                                        : t("PO2.Materials.Details.PhysicalSize.Unspecified")}
                                </span>
                            </div>
                            <div className={styles["detail-row"]}>
                                <span>{t("PO2.Materials.Details.Metallic.Label")}</span>
                                <span>{formatNumber(managedMaterialMetallic, 2)}</span>
                            </div>
                            <div className={styles["detail-row"]}>
                                <span>{t("PO2.Materials.Details.Smoothness.Label")}</span>
                                <span>{formatNumber(managedMaterialSmoothness, 2)}</span>
                            </div>
                            <div className={styles["detail-row"]}>
                                <span>{t("PO2.Materials.Details.Coat.Label")}</span>
                                <span>{formatNumber(managedMaterialCoatStrength, 2)}</span>
                            </div>
                            <div className={styles["detail-row"]}>
                                <span>{t("PO2.Materials.Details.NormalMap.Label")}</span>
                                <span>{managedMaterialHasNormal ? t("PO2.Common.Yes") : t("PO2.Common.No")}</span>
                            </div>

                            {hasSelection && (
                                <Button
                                    onSelect={() => {
                                        onAction(
                                            "setSelectedLibraryMaterial",
                                            selectedManagedMaterialId,
                                        );
                                        onAction("navigateBack");
                                    }}
                                    className={layout.wide}
                                    disabled={
                                        selectedObjectLocked ||
                                        selectedManagedMaterialId.length === 0
                                    }
                                >{t("PO2.Materials.ApplyToArea.Action", { areaId: selectedSurfaceAreaId })}</Button>
                            )}

                            {canEditManagedMaterial && (
                                <Button
                                    onSelect={() => onAction(
                                        "openManagedMaterialEditView",
                                    )}
                                    className={layout.wide}
                                    disabled={materialImportBusy}
                                >
                                    {t("PO2.Materials.Edit.Action")}
                                </Button>
                            )}

                            {canDeleteManagedMaterial && (
                                <div className={styles["delete-section"]}>
                                    {!deleteConfirming ? (
                                        <Button
                                            onSelect={() => onDeleteConfirmingChange(true)}
                                            className={layout.wide}
                                            disabled={materialImportBusy}
                                        >
                                            {t("PO2.Materials.Delete.Action")}
                                        </Button>
                                    ) : (
                                        <>
                                            <div className={styles["delete-warning"]}>{t("PO2.Materials.DeleteConfirmation.Message", { materialName: managedMaterialDisplayName })}</div>
                                            <div className={styles["delete-actions"]}>
                                                <Button
                                                    onSelect={() => {
                                                        onDeleteConfirmingChange(false);
                                                        onAction("deleteManagedMaterial");
                                                    }}
                                                    disabled={materialImportBusy}
                                                >
                                                    {t("PO2.Materials.DeleteConfirmation.Action")}
                                                </Button>
                                                <Button
                                                    onSelect={() => onDeleteConfirmingChange(
                                                        false,
                                                    )}
                                                    disabled={materialImportBusy}
                                                >
                                                    {t("PO2.Common.Cancel")}
                                                </Button>
                                            </div>
                                        </>
                                    )}
                                </div>
                            )}

                            {materialLibraryStatus.length > 0 && (
                                <div className={typography.small}>
                                    {materialLibraryStatus}
                                </div>
                            )}
                        </div>
                    ) : (
                        <div className={styles.empty}>
                            {t("PO2.Materials.Selection.Instruction")}
                        </div>
                    )}
                </div>
            </div>

            <div className={styles.libraryImportSection}>
                <hr className={layout.divider} />
                <Button
                    onSelect={() => onAction("openMaterialImportView")}
                    className={layout.wide}
                    disabled={materialImportBusy}
                >
                    {t("PO2.Materials.Import.Action")}
                </Button>
            </div>
        </div>
    );
};
