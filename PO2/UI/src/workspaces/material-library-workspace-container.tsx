import type { ComponentProps } from "react";
import { group } from "@/bindings/commands";
import {
    canDeleteManagedMaterial$,
    canEditManagedMaterial$,
    managedMaterialCategory$,
    managedMaterialCoatStrength$,
    managedMaterialDisplayName$,
    managedMaterialHasNormal$,
    managedMaterialHasPhysicalSize$,
    managedMaterialMappingMode$,
    managedMaterialMetallic$,
    managedMaterialPhysicalHeight$,
    managedMaterialPhysicalWidth$,
    managedMaterialSmoothness$,
    managedMaterialSource$,
    materialImportBaseColorName$,
    materialImportBusy$,
    materialImportCandidateCount$,
    materialImportCandidates$,
    materialImportCategory$,
    materialImportCoatStrength$,
    materialImportDisplayName$,
    materialImportMappingMode$,
    materialImportMessageKind$,
    materialImportMetallic$,
    materialImportNormalName$,
    materialImportPhysicalHeight$,
    materialImportPhysicalWidth$,
    materialImportSmoothness$,
    materialImportStatus$,
    materialLibraryEntries$,
    materialLibraryStatus$,
    materialPreviewExposure$,
    materialPreviewFillIntensity$,
    materialPreviewIndirectDiffuse$,
    materialPreviewKeyIntensity$,
    materialPreviewReflectionLighting$,
    materialPreviewStatus$,
    materialPreviewUrl$,
    selectedManagedMaterialId$,
    selectedMaterialImportCandidateIndex$,} from "@/bindings/material-authoring";
import { useScopedValue } from "@/bindings/scoped-value";
import type { usePO2Localization } from "@/l10n/localization";
import { MaterialLibraryWorkspace } from "@/workspaces/material-library-workspace";
import { trigger, useValue } from "cs2/api";

export const useMaterialLibraryWorkspace = (
    materialImportViewActive: boolean,
    materialAuthoringVisible: boolean,
    materialManagementVisible: boolean,
    message: ReturnType<typeof usePO2Localization>["message"],
    t: ReturnType<typeof usePO2Localization>["t"],
) => {
    const materialImportCandidates = useScopedValue(materialImportCandidates$, materialImportViewActive);
    const materialImportCandidateCount = useScopedValue(materialImportCandidateCount$, materialImportViewActive);
    const selectedMaterialImportCandidateIndex = useScopedValue(selectedMaterialImportCandidateIndex$, materialImportViewActive);
    const materialImportBaseColorName = useScopedValue(materialImportBaseColorName$, materialImportViewActive);
    const materialImportNormalName = useScopedValue(materialImportNormalName$, materialImportViewActive) ?? t("PO2.Common.None");
    const materialImportMessageKind = useScopedValue(materialImportMessageKind$, materialAuthoringVisible);
    const materialImportStatus = message(useScopedValue(materialImportStatus$, materialAuthoringVisible));
    const materialImportBusy = useValue(materialImportBusy$);
    const materialLibraryEntries = useScopedValue(materialLibraryEntries$, materialManagementVisible);
    const selectedManagedMaterialId = useScopedValue(selectedManagedMaterialId$, materialManagementVisible);
    const materialLibraryStatus = message(useScopedValue(materialLibraryStatus$, materialManagementVisible));
    const managedMaterialDisplayName = useScopedValue(managedMaterialDisplayName$, materialManagementVisible);
    const managedMaterialCategory = useScopedValue(managedMaterialCategory$, materialManagementVisible);
    const managedMaterialSource = useScopedValue(managedMaterialSource$, materialManagementVisible);
    const canEditManagedMaterial = useScopedValue(canEditManagedMaterial$, materialManagementVisible);
    const canDeleteManagedMaterial = useScopedValue(canDeleteManagedMaterial$, materialManagementVisible);
    const managedMaterialMappingMode = useScopedValue(managedMaterialMappingMode$, materialManagementVisible);
    const managedMaterialHasPhysicalSize = useScopedValue(managedMaterialHasPhysicalSize$, materialManagementVisible);
    const managedMaterialPhysicalWidth = useScopedValue(managedMaterialPhysicalWidth$, materialManagementVisible);
    const managedMaterialPhysicalHeight = useScopedValue(managedMaterialPhysicalHeight$, materialManagementVisible);
    const managedMaterialMetallic = useScopedValue(managedMaterialMetallic$, materialManagementVisible);
    const managedMaterialSmoothness = useScopedValue(managedMaterialSmoothness$, materialManagementVisible);
    const managedMaterialCoatStrength = useScopedValue(managedMaterialCoatStrength$, materialManagementVisible);
    const managedMaterialHasNormal = useScopedValue(managedMaterialHasNormal$, materialManagementVisible);
    const materialPreviewUrl = useScopedValue(materialPreviewUrl$, materialManagementVisible);
    const materialPreviewStatus = message(useScopedValue(materialPreviewStatus$, materialManagementVisible));
    const materialPreviewExposure = useScopedValue(materialPreviewExposure$, materialManagementVisible);
    const materialPreviewIndirectDiffuse = useScopedValue(materialPreviewIndirectDiffuse$, materialManagementVisible);
    const materialPreviewReflectionLighting = useScopedValue(materialPreviewReflectionLighting$, materialManagementVisible);
    const materialPreviewKeyIntensity = useScopedValue(materialPreviewKeyIntensity$, materialManagementVisible);
    const materialPreviewFillIntensity = useScopedValue(materialPreviewFillIntensity$, materialManagementVisible);
    const materialImportDisplayName = useScopedValue(materialImportDisplayName$, materialAuthoringVisible);
    const materialImportCategory = useScopedValue(materialImportCategory$, materialAuthoringVisible);
    const materialImportPhysicalWidth = useScopedValue(materialImportPhysicalWidth$, materialAuthoringVisible);
    const materialImportPhysicalHeight = useScopedValue(materialImportPhysicalHeight$, materialAuthoringVisible);
    const materialImportMappingMode = useScopedValue(materialImportMappingMode$, materialAuthoringVisible);
    const materialImportMetallic = useScopedValue(materialImportMetallic$, materialAuthoringVisible);
    const materialImportSmoothness = useScopedValue(materialImportSmoothness$, materialAuthoringVisible);
    const materialImportCoatStrength = useScopedValue(materialImportCoatStrength$, materialAuthoringVisible);

    return {
        materialImportCandidates,
        materialImportCandidateCount,
        selectedMaterialImportCandidateIndex,
        materialImportBaseColorName,
        materialImportNormalName,
        materialImportMessageKind,
        materialImportStatus,
        materialImportBusy,
        materialLibraryEntries,
        selectedManagedMaterialId,
        materialLibraryStatus,
        managedMaterialDisplayName,
        managedMaterialCategory,
        managedMaterialSource,
        canEditManagedMaterial,
        canDeleteManagedMaterial,
        managedMaterialMappingMode,
        managedMaterialHasPhysicalSize,
        managedMaterialPhysicalWidth,
        managedMaterialPhysicalHeight,
        managedMaterialMetallic,
        managedMaterialSmoothness,
        managedMaterialCoatStrength,
        managedMaterialHasNormal,
        materialPreviewUrl,
        materialPreviewStatus,
        materialPreviewExposure,
        materialPreviewIndirectDiffuse,
        materialPreviewReflectionLighting,
        materialPreviewKeyIntensity,
        materialPreviewFillIntensity,
        materialImportDisplayName,
        materialImportCategory,
        materialImportPhysicalWidth,
        materialImportPhysicalHeight,
        materialImportMappingMode,
        materialImportMetallic,
        materialImportSmoothness,
        materialImportCoatStrength,
    };
};

type MaterialLibraryWorkspaceContainerProps = {
    state: ReturnType<typeof useMaterialLibraryWorkspace>;
    materialManagementView: ComponentProps<typeof MaterialLibraryWorkspace>["managementView"];
    workspaceContext: ComponentProps<typeof MaterialLibraryWorkspace>["context"];
    historyStatus: ComponentProps<typeof MaterialLibraryWorkspace>["status"];
    hasSelection: ComponentProps<typeof MaterialLibraryWorkspace>["hasSelection"];
    selectedObjectLocked: ComponentProps<typeof MaterialLibraryWorkspace>["selectedObjectLocked"];
    selectedSurfaceAreaId: ComponentProps<typeof MaterialLibraryWorkspace>["selectedSurfaceAreaId"];
};

export const MaterialLibraryWorkspaceContainer = ({
    state,
    materialManagementView,
    workspaceContext,
    historyStatus,
    hasSelection,
    selectedObjectLocked,
    selectedSurfaceAreaId,
}: MaterialLibraryWorkspaceContainerProps) => {
    const {
        materialLibraryEntries,
        selectedManagedMaterialId,
        managedMaterialDisplayName,
        managedMaterialCategory,
        managedMaterialSource,
        canEditManagedMaterial,
        canDeleteManagedMaterial,
        managedMaterialMappingMode,
        managedMaterialHasPhysicalSize,
        managedMaterialPhysicalWidth,
        managedMaterialPhysicalHeight,
        managedMaterialMetallic,
        managedMaterialSmoothness,
        managedMaterialCoatStrength,
        managedMaterialHasNormal,
        materialLibraryStatus,
        materialPreviewUrl,
        materialPreviewStatus,
        materialPreviewExposure,
        materialPreviewIndirectDiffuse,
        materialPreviewReflectionLighting,
        materialPreviewKeyIntensity,
        materialPreviewFillIntensity,
        materialImportCandidates,
        materialImportCandidateCount,
        selectedMaterialImportCandidateIndex,
        materialImportBaseColorName,
        materialImportNormalName,
        materialImportStatus,
        materialImportMessageKind,
        materialImportBusy,
        materialImportDisplayName,
        materialImportCategory,
        materialImportPhysicalWidth,
        materialImportPhysicalHeight,
        materialImportMappingMode,
        materialImportMetallic,
        materialImportSmoothness,
        materialImportCoatStrength,
    } = state;

    return (
        <MaterialLibraryWorkspace
            managementView={materialManagementView}
            context={workspaceContext}
            status={historyStatus}
            hasSelection={hasSelection}
            selectedObjectLocked={selectedObjectLocked}
            selectedSurfaceAreaId={selectedSurfaceAreaId}
            managed={{
                entries: materialLibraryEntries,
                selectedId: selectedManagedMaterialId,
                displayName: managedMaterialDisplayName,
                category: managedMaterialCategory,
                source: managedMaterialSource,
                canEdit: canEditManagedMaterial,
                canDelete: canDeleteManagedMaterial,
                mappingMode: managedMaterialMappingMode,
                hasPhysicalSize: managedMaterialHasPhysicalSize,
                physicalWidth: managedMaterialPhysicalWidth,
                physicalHeight: managedMaterialPhysicalHeight,
                metallic: managedMaterialMetallic,
                smoothness: managedMaterialSmoothness,
                coatStrength: managedMaterialCoatStrength,
                hasNormal: managedMaterialHasNormal,
                status: materialLibraryStatus,
            }}
            preview={{
                url: materialPreviewUrl,
                status: materialPreviewStatus,
                exposure: materialPreviewExposure,
                indirectDiffuse: materialPreviewIndirectDiffuse,
                reflectionLighting: materialPreviewReflectionLighting,
                keyIntensity: materialPreviewKeyIntensity,
                fillIntensity: materialPreviewFillIntensity,
            }}
            authoring={{
                candidates: materialImportCandidates,
                candidateCount: materialImportCandidateCount,
                selectedCandidateIndex: selectedMaterialImportCandidateIndex,
                baseColorName: materialImportBaseColorName,
                normalName: materialImportNormalName,
                status: materialImportStatus,
                messageKind: materialImportMessageKind,
                busy: materialImportBusy,
                displayName: materialImportDisplayName,
                category: materialImportCategory,
                physicalWidth: materialImportPhysicalWidth,
                physicalHeight: materialImportPhysicalHeight,
                mappingMode: materialImportMappingMode,
                metallic: materialImportMetallic,
                smoothness: materialImportSmoothness,
                coatStrength: materialImportCoatStrength,
            }}
            onAction={(action, ...args) => trigger(
                group,
                action,
                ...args,
            )}
        />
    );
};
