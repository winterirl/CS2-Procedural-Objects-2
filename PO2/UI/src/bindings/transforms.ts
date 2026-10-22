import { bindScopedValue } from "@/bindings/scoped-value";
import { RuntimeText } from "@/l10n/localization";
import type { CustomOrientation } from "@/utils/ui-payloads";
import { bindValue } from "cs2/api";
import { group } from "./commands";

export const deformationCommitBusy$ = bindValue<boolean>(
    group,
    "deformationCommitBusy",
    false,
);
export const deformationCommitStatus$ = bindValue<RuntimeText>(
    group,
    "deformationCommitStatus",
    null,
);
export const mirrorMode$ = bindValue<boolean>(group, "mirrorMode", false);
export const canMirrorSelected$ = bindValue<boolean>(group, "canMirrorSelected", false);
export const mirrorRequiresBake$ = bindValue<boolean>(group, "mirrorRequiresBake", false);
export const mirrorStatus$ = bindValue<RuntimeText>(group, "mirrorStatus", null);
export const arrayActive$ = bindValue<boolean>(group, "arrayActive", false);
export const canArraySelectedPart$ = bindValue<boolean>(group, "canArraySelectedPart", false);
export const arrayStatus$ = bindValue<RuntimeText>(group, "arrayStatus", null);
export const deformMode$ = bindValue<boolean>(group, "deformMode", false);
export const deformationPivot$ = bindValue<number>(group, "deformationPivot", 0);
export const deformationSelectionCount$ = bindValue<number>(group, "deformationSelectionCount", 0);
export const deformationStatus$ = bindValue<RuntimeText>(group, "deformationStatus", null);
export const meshEditRequiresBake$ = bindValue<boolean>(group, "meshEditRequiresBake", false);
export const canBakeMeshDeformation$ = bindValue<boolean>(group, "canBakeMeshDeformation", false);
export const canBakeDeformation$ = bindValue<boolean>(group, "canBakeDeformation", false);
export const selectedHasDeformation$ = bindValue<boolean>(group, "selectedHasDeformation", false);
export const gizmoMode$ = bindValue<number>(group, "gizmoMode", 0);
export const transformTarget$ = bindValue<number>(group, "transformTarget", 0);
export const transformOrientation$ = bindValue<number>(group, "transformOrientation", 1);
export const snapMagnetEnabled$ = bindValue<boolean>(group, "snapMagnetEnabled", false);
export const snapIncrementEnabled$ = bindValue<boolean>(group, "snapIncrementEnabled", true);
export const canSnapToTerrain$ = bindValue<boolean>(group, "canSnapToTerrain", false);
export const snapPOVerticesEnabled$ = bindValue<boolean>(group, "snapPOVerticesEnabled", true);
export const snapPOEdgesEnabled$ = bindValue<boolean>(group, "snapPOEdgesEnabled", false);
export const snapGameSurfacesEnabled$ = bindValue<boolean>(group, "snapGameSurfacesEnabled", true);
export const moveSnapIncrement$ = bindScopedValue<number>(group, "moveSnapIncrement", 1);
export const precisionMoveSnapIncrement$ = bindScopedValue<number>(group, "precisionMoveSnapIncrement", 0.1);
export const rotationSnapIncrement$ = bindScopedValue<number>(group, "rotationSnapIncrement", 5);
export const precisionRotationSnapIncrement$ = bindScopedValue<number>(group, "precisionRotationSnapIncrement", 1);
export const scaleSnapIncrement$ = bindScopedValue<number>(group, "scaleSnapIncrement", 0.1);
export const precisionScaleSnapIncrement$ = bindScopedValue<number>(group, "precisionScaleSnapIncrement", 0.01);
export const customOrientations$ = bindScopedValue<CustomOrientation[]>(group, "customOrientations", []);
export const activeCustomOrientationIndex$ = bindScopedValue<number>(group, "activeCustomOrientationIndex", -1);
export const canBeginAxisPicking$ = bindScopedValue<boolean>(group, "canBeginAxisPicking", false);
export const canConfirmAxisPicking$ = bindScopedValue<boolean>(group, "canConfirmAxisPicking", false);
export const scaleX$ = bindScopedValue<number>(group, "scaleX", 1);
export const scaleY$ = bindScopedValue<number>(group, "scaleY", 1);
export const scaleZ$ = bindScopedValue<number>(group, "scaleZ", 1);
export const transformRequiresBake$ = bindScopedValue<boolean>(group, "transformRequiresBake", false);
