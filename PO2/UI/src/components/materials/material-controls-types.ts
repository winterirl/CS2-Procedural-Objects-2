import type { MaterialImportMessageKind } from "@/bindings/operation-state";
import type { MaterialLibraryEntry } from "@/utils/ui-payloads";

export type MaterialActionArgument = number | string;

export type MaterialActionHandler = (
    action: string,
    ...args: MaterialActionArgument[]
) => void;

export type ManagedMaterialState = {
    entries: MaterialLibraryEntry[];
    selectedId: string;
    displayName: string;
    category: string;
    source: number;
    canEdit: boolean;
    canDelete: boolean;
    mappingMode: number;
    hasPhysicalSize: boolean;
    physicalWidth: number;
    physicalHeight: number;
    metallic: number;
    smoothness: number;
    coatStrength: number;
    hasNormal: boolean;
    status: string;
};

export type MaterialPreviewState = {
    url: string;
    status: string;
    exposure: number;
    indirectDiffuse: number;
    reflectionLighting: number;
    keyIntensity: number;
    fillIntensity: number;
};

export type MaterialAuthoringState = {
    candidates: string[];
    candidateCount: number;
    selectedCandidateIndex: number;
    baseColorName: string;
    normalName: string;
    status: string;
    messageKind: MaterialImportMessageKind;
    busy: boolean;
    displayName: string;
    category: string;
    physicalWidth: number;
    physicalHeight: number;
    mappingMode: number;
    metallic: number;
    smoothness: number;
    coatStrength: number;
};
