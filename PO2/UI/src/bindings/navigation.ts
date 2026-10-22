import { bindScopedValue } from "@/bindings/scoped-value";
import type { PerformanceDiagnostics } from "@/utils/ui-payloads";
import { bindValue } from "cs2/api";
import { group } from "./commands";

export const toolMode = {
    select: 0,
    placePrimitive: 1,
    placePrimitivePart: 2,
    combineSource: 3,
    convertAsset: 4,
    transform: 5,
    meshEdit: 6,
    texturing: 7,
    delete: 8,
    copy: 9,
    paste: 10,
    lod: 11,
} as const;
export const workspace = {
    select: 0,
    create: 1,
    materialLibrary: 2,
    transform: 3,
    meshEdit: 4,
    texturing: 5,
    objectList: 6,
    convert: 7,
    saveManager: 8,
    delete: 9,
    copy: 10,
    paste: 11,
    lod: 12,
} as const;
export const helpView = {
    none: 0,
    overview: 1,
    performance: 2,
} as const;
export const deleteCursorVisible$ = bindValue<boolean>(group, "deleteCursorVisible", false);
export const toolActive$ = bindValue<boolean>(group, "toolActive", false);
export const activeWorkspace$ = bindValue<number>(group, "activeWorkspace", workspace.select);
export const activeToolMode$ = bindValue<number>(group, "activeToolMode", toolMode.select);
export const activeInteraction$ = bindValue<number>(group, "activeInteraction", 0);
export const helpOpen$ = bindValue<boolean>(group, "helpOpen", false);
export const helpView$ = bindValue<number>(group, "helpView", helpView.none);
export const performanceDiagnostics$ = bindScopedValue<PerformanceDiagnostics>(
    group,
    "performanceDiagnostics",
    { state: "idle" },
);
