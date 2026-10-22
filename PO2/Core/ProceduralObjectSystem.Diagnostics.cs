using System;
using System.Collections.Generic;
using PO2.Diagnostics;
using PO2.Geometry;
using PO2.Rendering.Lod;
using PO2.Rendering.Objects;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        internal PerformanceDiagnosticsSnapshot CapturePerformanceDiagnostics()
        {
            PerformanceDiagnosticsSnapshot snapshot = new PerformanceDiagnosticsSnapshot
            {
                CapturedAtUtc = DateTime.UtcNow,
                ObjectCount = m_Objects.Count,
            };

            List<PerformanceObjectSummary> heaviestObjects = new List<PerformanceObjectSummary>(m_Objects.Count);

            foreach (ProceduralObject proceduralObject in m_Objects.Values)
            {
                long objectMeshVertexCount = 0L;
                long objectTriangleCount = 0L;

                for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
                {
                    MeshPart part = proceduralObject.Parts[partIndex];
                    long meshVertexCount = part.Mesh.Vertices.Length;
                    long triangleCount = part.Mesh.Triangles.Length / 3L;

                    snapshot.PartCount++;
                    snapshot.SurfaceAreaCount += part.SurfaceAreas.Count;
                    snapshot.LogicalVertexCount += part.Topology.LogicalVertexCount;
                    snapshot.MeshVertexCount += meshVertexCount;
                    snapshot.TriangleCount += triangleCount;

                    objectMeshVertexCount += meshVertexCount;
                    objectTriangleCount += triangleCount;
                }

                heaviestObjects.Add(new PerformanceObjectSummary
                {
                    ObjectId = proceduralObject.Id,
                    Name = proceduralObject.Name,
                    MeshVertexCount = objectMeshVertexCount,
                    TriangleCount = objectTriangleCount,
                });
            }

            heaviestObjects.Sort(CompareHeaviestObjects);
            if (heaviestObjects.Count > 5)
            {
                heaviestObjects.RemoveRange(5, heaviestObjects.Count - 5);
            }

            for (int i = 0; i < heaviestObjects.Count; i++)
            {
                ApplyLodObjectDiagnostics(heaviestObjects[i]);
            }

            snapshot.HeaviestObjects = heaviestObjects.ToArray();

            PerformanceRendererSnapshot rendererSnapshot = m_Renderer != null ? m_Renderer.CapturePerformanceDiagnostics() : default(PerformanceRendererSnapshot);
            snapshot.ActiveMaterialBatchCount = rendererSnapshot.ActiveMaterialBatchCount;
            snapshot.ActiveRendererCount = rendererSnapshot.ActiveRendererCount;
            snapshot.CachedMaterialVariantCount = rendererSnapshot.CachedMaterialVariantCount;
            snapshot.LoadedLibraryTextureCount = rendererSnapshot.LoadedLibraryTextureCount;

            LodSchedulerDiagnostics lod = CaptureLodDiagnostics();
            snapshot.LodGenerationEnabled = m_LodCoordinator != null;
            snapshot.LodCaptureActive = lod.CaptureActive;
            snapshot.LodTrackedObjectCount = lod.TrackedObjects;
            snapshot.LodOutstandingJobCount = lod.OutstandingJobs;
            snapshot.LodCapturedBytes = lod.CapturedBytes;
            snapshot.LodSubmittedJobCount = lod.SubmittedJobs;
            snapshot.LodCompletedJobCount = lod.CompletedJobs;
            snapshot.LodAcceptedResultCount = lod.AcceptedResults;
            snapshot.LodPublishableResultCount = lod.PublishableResults;
            snapshot.LodPassThroughResultCount = lod.PassThroughResults;
            snapshot.LodAttemptedAreaLevelCount = lod.AttemptedAreaLevels;
            snapshot.LodSimplifiedAreaLevelCount = lod.SimplifiedAreaLevels;
            snapshot.LodLimitedAreaLevelCount = lod.LimitedAreaLevels;
            snapshot.LodBudgetMissedAreaLevelCount = lod.BudgetMissedAreaLevels;
            snapshot.LodAggressiveAreaLevelCount = lod.AggressiveAreaLevels;
            snapshot.LodPrunedTriangleCount = lod.PrunedTriangles;
            snapshot.LodStaleResultCount = lod.StaleResults;
            snapshot.LodCancelledJobCount = lod.CancelledJobs;
            snapshot.LodFailedJobCount = lod.FailedJobs;
            snapshot.LodRejectedCaptureCount = lod.RejectedCaptures;
            snapshot.LodMaximumCaptureSetupMilliseconds = lod.MaximumCaptureSetupMilliseconds;
            snapshot.LodMaximumCaptureCopyUpdateMilliseconds = lod.MaximumCaptureCopyUpdateMilliseconds;
            snapshot.LodMaximumWorkerJobMilliseconds = lod.MaximumWorkerJobMilliseconds;
            LodPublicationDiagnostics publication = m_Renderer != null ? m_Renderer.CaptureLodPublicationDiagnostics() : default(LodPublicationDiagnostics);
            snapshot.LodPendingPublicationObjectCount = publication.PendingObjects;
            snapshot.LodPendingMeshUploadCount = publication.PendingMeshUploads;
            snapshot.LodPublishedObjectCount = publication.PublishedObjects;
            snapshot.LodPublishedMeshCount = publication.PublishedMeshes;
            snapshot.LodPublishedResultCount = publication.PublishedResults;
            snapshot.LodDiscardedPublicationResultCount = publication.DiscardedResults;
            snapshot.LodFailedPublicationCount = publication.FailedPublications;
            snapshot.LodLastPublicationStepMilliseconds = publication.LastPublicationStepMilliseconds;
            snapshot.LodMaximumPublicationStepMilliseconds = publication.MaximumPublicationStepMilliseconds;
            snapshot.LodLastPublicationFailureReason = publication.LastFailureReason ?? string.Empty;
            snapshot.LodHasLastAcceptedResult = lod.HasLastAcceptedResult;
            snapshot.LodLastAcceptedWorthPublishing = lod.LastAcceptedWorthPublishing;
            snapshot.LodLastSourceTriangleCount = lod.LastSourceTriangles;
            snapshot.LodLastLod1TriangleCount = lod.LastLod1Triangles;
            snapshot.LodLastLod2TriangleCount = lod.LastLod2Triangles;
            snapshot.LodLastLod1TargetTriangleCount = lod.LastLod1TargetTriangles;
            snapshot.LodLastLod2TargetTriangleCount = lod.LastLod2TargetTriangles;
            snapshot.LodLastLod1BudgetMet = lod.LastLod1BudgetMet;
            snapshot.LodLastLod2BudgetMet = lod.LastLod2BudgetMet;
            snapshot.LodLastLod1MaximumAbsoluteError = lod.LastLod1MaximumAbsoluteError;
            snapshot.LodLastLod2MaximumAbsoluteError = lod.LastLod2MaximumAbsoluteError;
            snapshot.LodLastFailureReason = m_LodCoordinator != null ? lod.LastFailureReason : m_LodInitializationFailureReason;

            return snapshot;
        }

        private void ApplyLodObjectDiagnostics(PerformanceObjectSummary summary)
        {
            if (!TryCaptureLodObjectDiagnostics(summary.ObjectId, out LodObjectDiagnostics diagnostics))
            {
                return;
            }

            summary.HasLodResult = true;
            summary.LodWorthPublishing = diagnostics.WorthPublishing;
            summary.Lod1TriangleCount = diagnostics.Lod1Triangles;
            summary.Lod2TriangleCount = diagnostics.Lod2Triangles;
            summary.Lod1TargetTriangleCount = diagnostics.Lod1TargetTriangles;
            summary.Lod2TargetTriangleCount = diagnostics.Lod2TargetTriangles;
            summary.Lod1BudgetMet = diagnostics.Lod1BudgetMet;
            summary.Lod2BudgetMet = diagnostics.Lod2BudgetMet;
            summary.Lod1MaximumRelativeError = diagnostics.Lod1MaximumRelativeError;
            summary.Lod2MaximumRelativeError = diagnostics.Lod2MaximumRelativeError;
            summary.Lod1MaximumAbsoluteError = diagnostics.Lod1MaximumAbsoluteError;
            summary.Lod2MaximumAbsoluteError = diagnostics.Lod2MaximumAbsoluteError;
            summary.Lod1UsedPositionFocusedSimplification = diagnostics.Lod1UsedPositionFocusedSimplification;
            summary.Lod2UsedAggressiveSimplification = diagnostics.Lod2UsedAggressiveSimplification;
            summary.Lod2PrunedTriangleCount = diagnostics.PrunedTriangles;
            summary.LodGenerationMilliseconds = diagnostics.GenerationMilliseconds;
        }

        private static int CompareHeaviestObjects(PerformanceObjectSummary left, PerformanceObjectSummary right)
        {
            int result = right.TriangleCount.CompareTo(left.TriangleCount);
            if (result != 0)
            {
                return result;
            }

            result = right.MeshVertexCount.CompareTo(left.MeshVertexCount);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return result != 0 ? result : left.ObjectId.CompareTo(right.ObjectId);
        }
    }
}