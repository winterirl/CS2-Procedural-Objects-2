import type { ComponentProps } from "react";
import { group } from "@/bindings/commands";
import { useScopedValue } from "@/bindings/scoped-value";
import {
    colorMask1Hex$,
    colorMask2Hex$,
    colorMask3Hex$,
    colorMaskHasSource$,
    colorMaskMode$,
    libraryMaterialCount$,
    mappingMode$,
    mappingOffsetU$,
    mappingOffsetV$,
    mappingRotation$,
    mappingTextureHeight$,
    mappingTextureWidth$,
    materialCoatStrength$,
    materialMetallic$,
    materialSmoothness$,
    selectedLibraryMaterialIndex$,
    selectedMaterialName$,
    selectedMaterialSourceKind$,
    selectedSurfaceAreaCount$,
    selectedSurfaceAreaId$,
    selectedSurfaceAreaIds$,
    selectedVanillaSpecialMaterialKind$,
    surfaceAreaHasOriginalAppearance$,} from "@/bindings/texturing";
import { TexturingWorkspace } from "@/workspaces/texturing-workspace";
import { trigger } from "cs2/api";

export const useTexturingWorkspace = (texturingVisible: boolean, materialManagementVisible: boolean) => {
    const selectedSurfaceAreaIds = useScopedValue(selectedSurfaceAreaIds$, texturingVisible);
    const selectedSurfaceAreaId = useScopedValue(selectedSurfaceAreaId$, texturingVisible || materialManagementVisible);
    const selectedSurfaceAreaCount = useScopedValue(selectedSurfaceAreaCount$, texturingVisible);
    const mappingMode = useScopedValue(mappingMode$, texturingVisible);
    const mappingTextureWidth = useScopedValue(mappingTextureWidth$, texturingVisible);
    const mappingTextureHeight = useScopedValue(mappingTextureHeight$, texturingVisible);
    const mappingOffsetU = useScopedValue(mappingOffsetU$, texturingVisible);
    const mappingOffsetV = useScopedValue(mappingOffsetV$, texturingVisible);
    const mappingRotation = useScopedValue(mappingRotation$, texturingVisible);
    const colorMaskMode = useScopedValue(colorMaskMode$, texturingVisible);
    const colorMaskHasSource = useScopedValue(colorMaskHasSource$, texturingVisible);
    const surfaceAreaHasOriginalAppearance = useScopedValue(surfaceAreaHasOriginalAppearance$, texturingVisible);
    const colorMask1Hex = useScopedValue(colorMask1Hex$, texturingVisible);
    const colorMask2Hex = useScopedValue(colorMask2Hex$, texturingVisible);
    const colorMask3Hex = useScopedValue(colorMask3Hex$, texturingVisible);
    const libraryMaterialCount = useScopedValue(libraryMaterialCount$, texturingVisible);
    const selectedLibraryMaterialIndex = useScopedValue(selectedLibraryMaterialIndex$, texturingVisible);
    const selectedMaterialName = useScopedValue(selectedMaterialName$, texturingVisible);
    const selectedMaterialSourceKind = useScopedValue(selectedMaterialSourceKind$, texturingVisible);
    const selectedVanillaSpecialMaterialKind = useScopedValue(selectedVanillaSpecialMaterialKind$, texturingVisible);
    const materialMetallic = useScopedValue(materialMetallic$, texturingVisible);
    const materialSmoothness = useScopedValue(materialSmoothness$, texturingVisible);
    const materialCoatStrength = useScopedValue(materialCoatStrength$, texturingVisible);

    return {
        selectedSurfaceAreaIds,
        selectedSurfaceAreaId,
        selectedSurfaceAreaCount,
        mappingMode,
        mappingTextureWidth,
        mappingTextureHeight,
        mappingOffsetU,
        mappingOffsetV,
        mappingRotation,
        colorMaskMode,
        colorMaskHasSource,
        surfaceAreaHasOriginalAppearance,
        colorMask1Hex,
        colorMask2Hex,
        colorMask3Hex,
        libraryMaterialCount,
        selectedLibraryMaterialIndex,
        selectedMaterialName,
        selectedMaterialSourceKind,
        selectedVanillaSpecialMaterialKind,
        materialMetallic,
        materialSmoothness,
        materialCoatStrength,
    };
};

type TexturingWorkspaceContainerProps = {
    state: ReturnType<typeof useTexturingWorkspace>;
    selectedObjectLocked: ComponentProps<typeof TexturingWorkspace>["selectedObjectLocked"];
    selectedObjectReceivesDecals: ComponentProps<typeof TexturingWorkspace>["selectedObjectReceivesDecals"];
    selectedObjectHasOriginalAppearance: ComponentProps<typeof TexturingWorkspace>["selectedObjectHasOriginalAppearance"];
    selectedSurfaceCount: ComponentProps<typeof TexturingWorkspace>["selectedSurfaceCount"];
    selectedAreaDraftKey: ComponentProps<typeof TexturingWorkspace>["selectedAreaDraftKey"];
};

export const TexturingWorkspaceContainer = ({
    state,
    selectedObjectLocked,
    selectedObjectReceivesDecals,
    selectedObjectHasOriginalAppearance,
    selectedSurfaceCount,
    selectedAreaDraftKey,
}: TexturingWorkspaceContainerProps) => {
    const {
        selectedSurfaceAreaId,
        selectedSurfaceAreaIds,
        mappingMode,
        mappingTextureWidth,
        mappingTextureHeight,
        mappingOffsetU,
        mappingOffsetV,
        mappingRotation,
        libraryMaterialCount,
        selectedLibraryMaterialIndex,
        selectedMaterialName,
        selectedMaterialSourceKind,
        selectedVanillaSpecialMaterialKind,
        materialMetallic,
        materialSmoothness,
        materialCoatStrength,
        colorMaskMode,
        colorMaskHasSource,
        colorMask1Hex,
        colorMask2Hex,
        colorMask3Hex,
        surfaceAreaHasOriginalAppearance,
    } = state;

    return (
        <TexturingWorkspace
            selectedObjectLocked={selectedObjectLocked}
            selectedObjectReceivesDecals={
                selectedObjectReceivesDecals
            }
            selectedObjectHasOriginalAppearance={
                selectedObjectHasOriginalAppearance
            }
            selectedSurfaceCount={selectedSurfaceCount}
            selectedSurfaceAreaId={selectedSurfaceAreaId}
            selectedSurfaceAreaIds={selectedSurfaceAreaIds}
            selectedAreaDraftKey={selectedAreaDraftKey}
            mappingMode={mappingMode}
            mappingTextureWidth={mappingTextureWidth}
            mappingTextureHeight={mappingTextureHeight}
            mappingOffsetU={mappingOffsetU}
            mappingOffsetV={mappingOffsetV}
            mappingRotation={mappingRotation}
            libraryMaterialCount={libraryMaterialCount}
            selectedLibraryMaterialIndex={
                selectedLibraryMaterialIndex
            }
            selectedMaterialName={selectedMaterialName}
            selectedMaterialSourceKind={
                selectedMaterialSourceKind
            }
            selectedVanillaSpecialMaterialKind={
                selectedVanillaSpecialMaterialKind
            }
            materialMetallic={materialMetallic}
            materialSmoothness={materialSmoothness}
            materialCoatStrength={materialCoatStrength}
            colorMaskMode={colorMaskMode}
            colorMaskHasSource={colorMaskHasSource}
            colorMask1Hex={colorMask1Hex}
            colorMask2Hex={colorMask2Hex}
            colorMask3Hex={colorMask3Hex}
            surfaceAreaHasOriginalAppearance={
                surfaceAreaHasOriginalAppearance
            }
            onAction={(action, ...args) => trigger(
                group,
                action,
                ...args,
            )}
        />
    );
};
