import { bindScopedValue } from "@/bindings/scoped-value";
import type { MeshPartEntry, ProceduralObjectListEntry } from "@/utils/ui-payloads";
import { bindValue } from "cs2/api";
import { group } from "./commands";

export const showObjectMarkers$ = bindScopedValue<boolean>(group, "showObjectMarkers", true);
export const objectListEntries$ = bindScopedValue<ProceduralObjectListEntry[]>(group, "objectListEntries", []);
export const selectedObjectId$ = bindValue<number>(group, "selectedObjectId", -1);
export const selectedObjectName$ = bindValue<string>(group, "selectedObjectName", "");
export const selectedObjectLocked$ = bindValue<boolean>(group, "selectedObjectLocked", false);
export const selectedObjectReceivesDecals$ = bindValue<boolean>(
    group,
    "selectedObjectReceivesDecals",
    false,
);
export const canDuplicateSelected$ = bindValue<boolean>(group, "canDuplicateSelected", false);
export const canRenameSelectedPart$ = bindScopedValue<boolean>(group, "canRenameSelectedPart", false);
export const canDuplicateSelectedPart$ = bindValue<boolean>(group, "canDuplicateSelectedPart", false);
export const canAddPrimitivePart$ = bindValue<boolean>(group, "canAddPrimitivePart", false);
export const canCombineSelected$ = bindValue<boolean>(group, "canCombineSelected", false);
export const canDeleteSelectedPart$ = bindValue<boolean>(group, "canDeleteSelectedPart", false);
export const canExtractSelectedPart$ = bindValue<boolean>(group, "canExtractSelectedPart", false);
export const selectedObjectHasOriginalAppearance$ = bindValue<boolean>(group, "selectedObjectHasOriginalAppearance", false);
export const selectedPartIndex$ = bindValue<number>(group, "selectedPartIndex", -1);
export const selectedPartCount$ = bindValue<number>(group, "selectedPartCount", 0);
export const selectedPartName$ = bindScopedValue<string>(group, "selectedPartName", "");
export const selectedParts$ = bindScopedValue<MeshPartEntry[]>(group, "selectedParts", []);
