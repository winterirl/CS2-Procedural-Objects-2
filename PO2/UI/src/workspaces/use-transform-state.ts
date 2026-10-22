import { useScopedValue } from "@/bindings/scoped-value";
import {
    activeCustomOrientationIndex$,
    arrayActive$,
    arrayStatus$,
    canArraySelectedPart$,
    canBakeDeformation$,
    canBakeMeshDeformation$,
    canBeginAxisPicking$,
    canConfirmAxisPicking$,
    canMirrorSelected$,
    canSnapToTerrain$,
    customOrientations$,
    deformationPivot$,
    deformationSelectionCount$,
    deformationStatus$,
    deformMode$,
    gizmoMode$,
    meshEditRequiresBake$,
    mirrorMode$,
    mirrorRequiresBake$,
    mirrorStatus$,
    moveSnapIncrement$,
    precisionMoveSnapIncrement$,
    precisionRotationSnapIncrement$,
    precisionScaleSnapIncrement$,
    rotationSnapIncrement$,
    scaleSnapIncrement$,
    selectedHasDeformation$,
    snapGameSurfacesEnabled$,
    snapIncrementEnabled$,
    snapMagnetEnabled$,
    snapPOEdgesEnabled$,
    snapPOVerticesEnabled$,
    transformOrientation$,
    transformTarget$,} from "@/bindings/transforms";
import type { usePO2Localization } from "@/l10n/localization";
import { useValue } from "cs2/api";

export const useTransformState = (
    transformControlsVisible: boolean,
    message: ReturnType<typeof usePO2Localization>["message"],
) => {
    const mirrorMode = useValue(mirrorMode$);
    const canMirrorSelected = useValue(canMirrorSelected$);
    const mirrorRequiresBake = useValue(mirrorRequiresBake$);
    const mirrorStatus = message(useValue(mirrorStatus$));
    const arrayActive = useValue(arrayActive$);
    const canArraySelectedPart = useValue(canArraySelectedPart$);
    const arrayStatus = message(useValue(arrayStatus$));
    const deformMode = useValue(deformMode$);
    const deformationPivot = useValue(deformationPivot$);
    const deformationSelectionCount = useValue(deformationSelectionCount$);
    const deformationStatus = message(useValue(deformationStatus$));
    const meshEditRequiresBake = useValue(meshEditRequiresBake$);
    const canBakeMeshDeformation = useValue(canBakeMeshDeformation$);
    const canBakeDeformation = useValue(canBakeDeformation$);
    const selectedHasDeformation = useValue(selectedHasDeformation$);
    const gizmoMode = useValue(gizmoMode$);
    const transformTarget = useValue(transformTarget$);
    const transformOrientation = useValue(transformOrientation$);
    const snapMagnetEnabled = useValue(snapMagnetEnabled$);
    const snapIncrementEnabled = useValue(snapIncrementEnabled$);
    const canSnapToTerrain = useValue(canSnapToTerrain$);
    const snapPOVerticesEnabled = useValue(snapPOVerticesEnabled$);
    const snapPOEdgesEnabled = useValue(snapPOEdgesEnabled$);
    const snapGameSurfacesEnabled = useValue(snapGameSurfacesEnabled$);
    const moveSnapIncrement = useScopedValue(moveSnapIncrement$, transformControlsVisible);
    const precisionMoveSnapIncrement = useScopedValue(precisionMoveSnapIncrement$, transformControlsVisible);
    const rotationSnapIncrement = useScopedValue(rotationSnapIncrement$, transformControlsVisible);
    const precisionRotationSnapIncrement = useScopedValue(precisionRotationSnapIncrement$, transformControlsVisible);
    const scaleSnapIncrement = useScopedValue(scaleSnapIncrement$, transformControlsVisible);
    const precisionScaleSnapIncrement = useScopedValue(precisionScaleSnapIncrement$, transformControlsVisible);
    const customOrientations = useScopedValue(customOrientations$, transformControlsVisible);
    const activeCustomOrientationIndex = useScopedValue(activeCustomOrientationIndex$, transformControlsVisible);
    const canBeginAxisPicking = useScopedValue(canBeginAxisPicking$, transformControlsVisible);
    const canConfirmAxisPicking = useScopedValue(canConfirmAxisPicking$, transformControlsVisible);

    return {
        mirrorMode,
        canMirrorSelected,
        mirrorRequiresBake,
        mirrorStatus,
        arrayActive,
        canArraySelectedPart,
        arrayStatus,
        deformMode,
        deformationPivot,
        deformationSelectionCount,
        deformationStatus,
        meshEditRequiresBake,
        canBakeMeshDeformation,
        canBakeDeformation,
        selectedHasDeformation,
        gizmoMode,
        transformTarget,
        transformOrientation,
        snapMagnetEnabled,
        snapIncrementEnabled,
        canSnapToTerrain,
        snapPOVerticesEnabled,
        snapPOEdgesEnabled,
        snapGameSurfacesEnabled,
        moveSnapIncrement,
        precisionMoveSnapIncrement,
        rotationSnapIncrement,
        precisionRotationSnapIncrement,
        scaleSnapIncrement,
        precisionScaleSnapIncrement,
        customOrientations,
        activeCustomOrientationIndex,
        canBeginAxisPicking,
        canConfirmAxisPicking,
    };
};
