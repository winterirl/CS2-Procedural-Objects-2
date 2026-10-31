import type { RuntimeText } from "@/l10n/localization";

export type ProceduralObjectListEntry = {
    objectId: number;
    name: string;
};

export type CustomOrientation = {
    index: number;
    name: string;
};

export type MeshPartEntry = {
    index: number;
    name: string;
};

export type SaveCityDate = {
    year: number;
    month: number;
    hour: number;
    minute: number;
};

export type SaveTileData = {
    fileKey: string;
    sessionGuid: string;
    objectCount: number;
    bytes: number;
    savedAtUtc: string;
    dateEstimated: boolean;
    cityName: string;
    saveName: string;
    mapName: string;
    saveKind: "Unknown" | "Manual" | "AutoSave";
    gameSaveStatus: "Unknown" | "Matched" | "Orphaned";
    cityDate: SaveCityDate | null;
};

export type MaterialLibraryEntry = {
    id: string;
    displayName: string;
    category: string;
    source: number;
};

export type PerformanceObjectSummary = {
    objectId: number;
    name: string;
    meshVertexCount: number;
    triangleCount: number;
    hasLodResult: boolean;
    lodWorthPublishing: boolean;
    lod1TriangleCount: number;
    lod2TriangleCount: number;
    lod1TargetTriangleCount: number;
    lod2TargetTriangleCount: number;
    lod1BudgetMet: boolean;
    lod2BudgetMet: boolean;
    lod1MaximumRelativeError: number;
    lod2MaximumRelativeError: number;
    lod1MaximumAbsoluteError: number;
    lod2MaximumAbsoluteError: number;
    lod1UsedPositionFocusedSimplification: boolean;
    lod2UsedAggressiveSimplification: boolean;
    lod2PrunedTriangleCount: number;
    lodGenerationMilliseconds: number;
};

export type PerformanceDiagnostics =
    | { state: "idle" | "capturing" }
    | { state: "error"; error: RuntimeText }
    | {
        state: "ready";
        capturedAtUtc: string;
        objectCount: number;
        partCount: number;
        surfaceAreaCount: number;
        logicalVertexCount: number;
        meshVertexCount: number;
        triangleCount: number;
        activeMaterialBatchCount: number;
        activeRendererCount: number;
        cachedMaterialVariantCount: number;
        loadedLibraryTextureCount: number;
        lodGenerationEnabled: boolean;
        lodCaptureActive: boolean;
        lodTrackedObjectCount: number;
        lodOutstandingJobCount: number;
        lodCapturedBytes: number;
        lodSubmittedJobCount: number;
        lodCompletedJobCount: number;
        lodAcceptedResultCount: number;
        lodPublishableResultCount: number;
        lodPassThroughResultCount: number;
        lodAttemptedAreaLevelCount: number;
        lodSimplifiedAreaLevelCount: number;
        lodLimitedAreaLevelCount: number;
        lodBudgetMissedAreaLevelCount: number;
        lodAggressiveAreaLevelCount: number;
        lodPrunedTriangleCount: number;
        lodStaleResultCount: number;
        lodCancelledJobCount: number;
        lodFailedJobCount: number;
        lodRejectedCaptureCount: number;
        lodMaximumCaptureSetupMilliseconds: number;
        lodMaximumCaptureCopyUpdateMilliseconds: number;
        lodMaximumWorkerJobMilliseconds: number;
        lodPendingPublicationObjectCount: number;
        lodPendingMeshUploadCount: number;
        lodPublishedObjectCount: number;
        lodPublishedMeshCount: number;
        lodPublishedResultCount: number;
        lodDiscardedPublicationResultCount: number;
        lodFailedPublicationCount: number;
        lodLastPublicationStepMilliseconds: number;
        lodMaximumPublicationStepMilliseconds: number;
        lodLastPublicationFailureReason: string;
        lodHasLastAcceptedResult: boolean;
        lodLastAcceptedWorthPublishing: boolean;
        lodLastSourceTriangleCount: number;
        lodLastLod1TriangleCount: number;
        lodLastLod2TriangleCount: number;
        lodLastLod1TargetTriangleCount: number;
        lodLastLod2TargetTriangleCount: number;
        lodLastLod1BudgetMet: boolean;
        lodLastLod2BudgetMet: boolean;
        lodLastLod1MaximumAbsoluteError: number;
        lodLastLod2MaximumAbsoluteError: number;
        lodLastFailureReason: string;
        heaviestObjects: PerformanceObjectSummary[];
    };

export type SaveManagerState = {
    page: number;
    pageCount: number;
    matchCount: number;
    totalCount: number;
    totalBytes: number;
    pageBytes: number;
    session: string;
    filter: string;
    oldest: boolean;
    issueCount: number;
    lineages: { id: string; name: string; count: number }[];
    revision: number;
    saves: SaveTileData[];
    scanSucceeded: boolean;
    catalogComplete: boolean;
    protectedKeys: string[];
    status: RuntimeText;
    issues: RuntimeText[];
    confirmation: null | {
        token: string;
    };
};

