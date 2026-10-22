import {
    canAddPrimitivePart$,
    canCombineSelected$,
    canDeleteSelectedPart$,
    canDuplicateSelected$,
    canDuplicateSelectedPart$,
    canExtractSelectedPart$,
    canRenameSelectedPart$,
    objectListEntries$,
    selectedObjectHasOriginalAppearance$,
    selectedObjectId$,
    selectedObjectLocked$,
    selectedObjectName$,
    selectedObjectReceivesDecals$,
    selectedPartCount$,
    selectedPartIndex$,
    selectedPartName$,
    selectedParts$,
    showObjectMarkers$,} from "@/bindings/objects";
import { useScopedValue } from "@/bindings/scoped-value";
import { useValue } from "cs2/api";

export const useObjectState = (
    objectListVisible: boolean,
    partControlsVisible: boolean,
    materialManagementVisible: boolean,
) => {
    const showObjectMarkers = useScopedValue(showObjectMarkers$, objectListVisible);
    const objectListEntries = useScopedValue(objectListEntries$, objectListVisible);
    const selectedObjectId = useValue(selectedObjectId$);
    const selectedObjectName = useValue(selectedObjectName$);
    const selectedObjectLocked = useValue(selectedObjectLocked$);
    const selectedObjectReceivesDecals = useValue(
        selectedObjectReceivesDecals$,
    );
    const canDuplicateSelected = useValue(canDuplicateSelected$);
    const canRenameSelectedPart = useScopedValue(canRenameSelectedPart$, partControlsVisible);
    const canDuplicateSelectedPart = useValue(canDuplicateSelectedPart$);
    const canAddPrimitivePart = useValue(canAddPrimitivePart$);
    const canCombineSelected = useValue(canCombineSelected$);
    const canDeleteSelectedPart = useValue(canDeleteSelectedPart$);
    const canExtractSelectedPart = useValue(canExtractSelectedPart$);
    const selectedObjectHasOriginalAppearance = useValue(selectedObjectHasOriginalAppearance$);
    const selectedPartIndex = useValue(selectedPartIndex$);
    const selectedPartCount = useValue(selectedPartCount$);
    const selectedPartName = useScopedValue(selectedPartName$, partControlsVisible || materialManagementVisible);
    const selectedParts = useScopedValue(selectedParts$, partControlsVisible);

    return {
        showObjectMarkers,
        objectListEntries,
        selectedObjectId,
        selectedObjectName,
        selectedObjectLocked,
        selectedObjectReceivesDecals,
        canDuplicateSelected,
        canRenameSelectedPart,
        canDuplicateSelectedPart,
        canAddPrimitivePart,
        canCombineSelected,
        canDeleteSelectedPart,
        canExtractSelectedPart,
        selectedObjectHasOriginalAppearance,
        selectedPartIndex,
        selectedPartCount,
        selectedPartName,
        selectedParts,
    };
};
