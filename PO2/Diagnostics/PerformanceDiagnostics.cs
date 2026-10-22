using System;

namespace PO2.Diagnostics
{
    internal sealed class PerformanceDiagnosticsSnapshot
    {
        internal DateTime CapturedAtUtc;
        internal long ObjectCount;
        internal long PartCount;
        internal long SurfaceAreaCount;
        internal long LogicalVertexCount;
        internal long MeshVertexCount;
        internal long TriangleCount;
        internal long ActiveMaterialBatchCount;
        internal long ActiveRendererCount;
        internal long CachedMaterialVariantCount;
        internal long LoadedLibraryTextureCount;
        internal bool LodGenerationEnabled;
        internal bool LodCaptureActive;
        internal long LodTrackedObjectCount;
        internal long LodOutstandingJobCount;
        internal long LodCapturedBytes;
        internal long LodSubmittedJobCount;
        internal long LodCompletedJobCount;
        internal long LodAcceptedResultCount;
        internal long LodPublishableResultCount;
        internal long LodPassThroughResultCount;
        internal long LodAttemptedAreaLevelCount;
        internal long LodSimplifiedAreaLevelCount;
        internal long LodLimitedAreaLevelCount;
        internal long LodBudgetMissedAreaLevelCount;
        internal long LodAggressiveAreaLevelCount;
        internal long LodPrunedTriangleCount;
        internal long LodStaleResultCount;
        internal long LodCancelledJobCount;
        internal long LodFailedJobCount;
        internal long LodRejectedCaptureCount;
        internal double LodMaximumCaptureSetupMilliseconds;
        internal double LodMaximumCaptureCopyUpdateMilliseconds;
        internal double LodMaximumWorkerJobMilliseconds;
        internal long LodPendingPublicationObjectCount;
        internal long LodPendingMeshUploadCount;
        internal long LodPublishedObjectCount;
        internal long LodPublishedMeshCount;
        internal long LodPublishedResultCount;
        internal long LodDiscardedPublicationResultCount;
        internal long LodFailedPublicationCount;
        internal double LodLastPublicationStepMilliseconds;
        internal double LodMaximumPublicationStepMilliseconds;
        internal string LodLastPublicationFailureReason = string.Empty;
        internal bool LodHasLastAcceptedResult;
        internal bool LodLastAcceptedWorthPublishing;
        internal long LodLastSourceTriangleCount;
        internal long LodLastLod1TriangleCount;
        internal long LodLastLod2TriangleCount;
        internal long LodLastLod1TargetTriangleCount;
        internal long LodLastLod2TargetTriangleCount;
        internal bool LodLastLod1BudgetMet;
        internal bool LodLastLod2BudgetMet;
        internal double LodLastLod1MaximumAbsoluteError;
        internal double LodLastLod2MaximumAbsoluteError;
        internal string LodLastFailureReason = string.Empty;
        internal PerformanceObjectSummary[] HeaviestObjects = Array.Empty<PerformanceObjectSummary>();
    }

    internal sealed class PerformanceObjectSummary
    {
        internal int ObjectId;
        internal string Name;
        internal long MeshVertexCount;
        internal long TriangleCount;
        internal bool HasLodResult;
        internal bool LodWorthPublishing;
        internal long Lod1TriangleCount;
        internal long Lod2TriangleCount;
        internal long Lod1TargetTriangleCount;
        internal long Lod2TargetTriangleCount;
        internal bool Lod1BudgetMet;
        internal bool Lod2BudgetMet;
        internal double Lod1MaximumRelativeError;
        internal double Lod2MaximumRelativeError;
        internal double Lod1MaximumAbsoluteError;
        internal double Lod2MaximumAbsoluteError;
        internal bool Lod1UsedPositionFocusedSimplification;
        internal bool Lod2UsedAggressiveSimplification;
        internal long Lod2PrunedTriangleCount;
        internal double LodGenerationMilliseconds;
    }

    internal readonly struct PerformanceRendererSnapshot
    {
        internal readonly long ActiveMaterialBatchCount;
        internal readonly long ActiveRendererCount;
        internal readonly long CachedMaterialVariantCount;
        internal readonly long LoadedLibraryTextureCount;

        internal PerformanceRendererSnapshot(long activeMaterialBatchCount, long activeRendererCount, long cachedMaterialVariantCount, long loadedLibraryTextureCount)
        {
            ActiveMaterialBatchCount = activeMaterialBatchCount;
            ActiveRendererCount = activeRendererCount;
            CachedMaterialVariantCount = cachedMaterialVariantCount;
            LoadedLibraryTextureCount = loadedLibraryTextureCount;
        }
    }
}