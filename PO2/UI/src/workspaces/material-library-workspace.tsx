import { usePO2Localization } from "@/l10n/localization";
import { materialCategories, filterMaterials } from "@/utils/material-categories";
import { useReportHelpTopics } from "@/help/help-local-context";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { MaterialAuthoringView } from "@/components/materials/material-authoring-view";
import { MaterialLibraryView } from "@/components/materials/material-library-view";
import type {
    ManagedMaterialState,
    MaterialActionHandler,
    MaterialAuthoringState,
    MaterialPreviewState,
} from "../components/materials/material-controls-types";
import { WorkspacePanel } from "@/components/workspace-panel/workspace-panel";

type MaterialLibraryWorkspaceProps = {
    managementView: number;
    context: ReactNode;
    status: ReactNode;
    hasSelection: boolean;
    selectedObjectLocked: boolean;
    selectedSurfaceAreaId: number;
    managed: ManagedMaterialState;
    preview: MaterialPreviewState;
    authoring: MaterialAuthoringState;
    onAction: MaterialActionHandler;
};

export const MaterialLibraryWorkspace = ({
    managementView,
    context,
    status,
    hasSelection,
    selectedObjectLocked,
    selectedSurfaceAreaId,
    managed,
    preview,
    authoring,
    onAction,
}: MaterialLibraryWorkspaceProps) => {
    const { t } = usePO2Localization();
    const libraryViewActive = managementView === 1;
    const importViewActive = managementView === 2;
    const editViewActive = managementView === 3;
    const [category, setCategory] = useState<string | null>(null);
    const [search, setSearch] = useState("");
    const [deleteConfirming, setDeleteConfirming] = useState(false);
    useReportHelpTopics("materials", libraryViewActive && deleteConfirming ? "materialDelete" : null);
    const [sourceDirectory, setSourceDirectory] = useState("");

    const categories = useMemo(() => materialCategories(managed.entries), [managed.entries]);
    const visibleEntries = useMemo(() => filterMaterials(managed.entries, category, search),
        [category, managed.entries, search]);

    useEffect(() => {
        // A pending delete only belongs to the row and view that opened it.
        setDeleteConfirming(false);
    }, [managed.selectedId, libraryViewActive]);

    useEffect(() => {
        if (category !== null && !categories.includes(category)) {
            setCategory(null);
        }
    }, [categories, category]);

    if (libraryViewActive) {
        return (
            <WorkspacePanel
                id="PO2MaterialLibraryPanel"
                title={t("PO2.Materials.Title")}
                context={context}
                status={status}
                wide
            >
                <MaterialLibraryView
                    entries={visibleEntries}
                    categories={categories}
                    search={search}
                    category={category}
                    selectedManagedMaterialId={managed.selectedId}
                    materialPreviewUrl={preview.url}
                    materialPreviewStatus={preview.status}
                    managedMaterialDisplayName={managed.displayName}
                    managedMaterialCategory={managed.category}
                    managedMaterialSource={managed.source}
                    managedMaterialMappingMode={managed.mappingMode}
                    managedMaterialHasPhysicalSize={managed.hasPhysicalSize}
                    managedMaterialPhysicalWidth={managed.physicalWidth}
                    managedMaterialPhysicalHeight={managed.physicalHeight}
                    managedMaterialMetallic={managed.metallic}
                    managedMaterialSmoothness={managed.smoothness}
                    managedMaterialCoatStrength={managed.coatStrength}
                    managedMaterialHasNormal={managed.hasNormal}
                    materialLibraryStatus={managed.status}
                    hasSelection={hasSelection}
                    selectedObjectLocked={selectedObjectLocked}
                    selectedSurfaceAreaId={selectedSurfaceAreaId}
                    canEditManagedMaterial={managed.canEdit}
                    canDeleteManagedMaterial={managed.canDelete}
                    materialImportBusy={authoring.busy}
                    deleteConfirming={deleteConfirming}
                    onSearchChange={setSearch}
                    onCategoryChange={setCategory}
                    onDeleteConfirmingChange={setDeleteConfirming}
                    onAction={onAction}
                />
            </WorkspacePanel>
        );
    }

    if (importViewActive || editViewActive) {
        return (
            <WorkspacePanel
                id={editViewActive
                    ? "PO2MaterialEditPanel"
                    : "PO2MaterialImportPanel"}
                title={editViewActive
                    ? t("PO2.Materials.Edit.Title")
                    : t("PO2.Materials.Import.Title")}
                status={status}
                wide
            >
                <MaterialAuthoringView
                    editMode={editViewActive}
                    categories={categories}
                    sourceDirectory={sourceDirectory}
                    authoring={authoring}
                    preview={preview}
                    onSourceDirectoryChange={setSourceDirectory}
                    onAction={onAction}
                />
            </WorkspacePanel>
        );
    }

    return null;
};
