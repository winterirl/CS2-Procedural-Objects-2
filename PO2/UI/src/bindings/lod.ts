import { bindScopedValue } from "./scoped-value";
import { group } from "./commands";

export const enum LodStatus { Idle, Waiting, Capturing, Generating, Uploading, Ready, OutOfDate, Cancelled, Failed }
export const enum LodDisplay { Auto, Original, Lod1, Lod2 }
export const enum LodMethod { EdgeCollapse, Aggressive }
export const enum LodLevelStatus { Disabled, Limited, Usable }
export type LodLevel = {
    enabled: boolean;
    canPreview: boolean;
    method: LodMethod;
    ratio: number;
    cap: number;
    error: number;
    prune: number;
    actual: number;
    target: number;
    targetMet: boolean;
    status: LodLevelStatus;
    inAuto: boolean;
};
export type LodState = {
    objectId: number;
    session: string;
    available: boolean;
    editable: boolean;
    dirty: boolean;
    canGenerate: boolean;
    canApply: boolean;
    inherit: boolean;
    inheritDistances: boolean;
    distance1: number;
    distance2: number;
    transitionsAdjusted: boolean;
    height1: number;
    height2: number;
    status: LodStatus;
    display: LodDisplay;
    progress: number;
    sourceTriangles: number;
    hasResult: boolean;
    lod1: LodLevel;
    lod2: LodLevel;
};
export const lodState$ = bindScopedValue<LodState | null>(group, "lodState", null);
