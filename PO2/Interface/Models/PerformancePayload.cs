using System;
using System.Globalization;
using Colossal.UI.Binding;
using PO2.Diagnostics;
using PO2.Interface.Bindings;
using PO2.Localization;

namespace PO2.Interface.Models
{
    internal sealed class PerformancePayload : IJsonWritable
    {
        internal static readonly PerformancePayload s_Idle = new PerformancePayload("idle", LocalizedText.Empty);
        internal static readonly PerformancePayload s_Capturing = new PerformancePayload("capturing", LocalizedText.Empty);
        private readonly string m_State;
        private readonly LocalizedText m_Error;
        private readonly PerformanceDiagnosticsSnapshot m_Snapshot;

        private PerformancePayload(string state, LocalizedText error)
        {
            m_State = state;
            m_Error = error;
        }

        internal PerformancePayload(LocalizedText error) : this("error", error)
        {
        }

        internal PerformancePayload(PerformanceDiagnosticsSnapshot snapshot) : this("ready", LocalizedText.Empty)
        {
            m_Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("PO2.PerformanceDiagnostics");
            writer.PropertyName("state");
            writer.Write(m_State);
            if (!m_Error.IsEmpty)
            {
                writer.PropertyName("error");
                LocalizedTextWriter.s_Instance.Write(writer, m_Error);
            }

            if (m_Snapshot != null) WriteSnapshot(writer, m_Snapshot);
            writer.TypeEnd();
        }

        private static void WriteSnapshot(IJsonWriter writer, PerformanceDiagnosticsSnapshot snapshot)
        {
            writer.PropertyName("capturedAtUtc");
            writer.Write(snapshot.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.PropertyName("objectCount");
            writer.Write(snapshot.ObjectCount);
            writer.PropertyName("partCount");
            writer.Write(snapshot.PartCount);
            writer.PropertyName("surfaceAreaCount");
            writer.Write(snapshot.SurfaceAreaCount);
            writer.PropertyName("logicalVertexCount");
            writer.Write(snapshot.LogicalVertexCount);
            writer.PropertyName("meshVertexCount");
            writer.Write(snapshot.MeshVertexCount);
            writer.PropertyName("triangleCount");
            writer.Write(snapshot.TriangleCount);
            writer.PropertyName("activeMaterialBatchCount");
            writer.Write(snapshot.ActiveMaterialBatchCount);
            writer.PropertyName("activeRendererCount");
            writer.Write(snapshot.ActiveRendererCount);
            writer.PropertyName("cachedMaterialVariantCount");
            writer.Write(snapshot.CachedMaterialVariantCount);
            writer.PropertyName("loadedLibraryTextureCount");
            writer.Write(snapshot.LoadedLibraryTextureCount);
            writer.PropertyName("lodGenerationEnabled");
            writer.Write(snapshot.LodGenerationEnabled);
            writer.PropertyName("lodCaptureActive");
            writer.Write(snapshot.LodCaptureActive);
            writer.PropertyName("lodTrackedObjectCount");
            writer.Write(snapshot.LodTrackedObjectCount);
            writer.PropertyName("lodOutstandingJobCount");
            writer.Write(snapshot.LodOutstandingJobCount);
            writer.PropertyName("lodCapturedBytes");
            writer.Write(snapshot.LodCapturedBytes);
            writer.PropertyName("lodSubmittedJobCount");
            writer.Write(snapshot.LodSubmittedJobCount);
            writer.PropertyName("lodCompletedJobCount");
            writer.Write(snapshot.LodCompletedJobCount);
            writer.PropertyName("lodAcceptedResultCount");
            writer.Write(snapshot.LodAcceptedResultCount);
            writer.PropertyName("lodPublishableResultCount");
            writer.Write(snapshot.LodPublishableResultCount);
            writer.PropertyName("lodPassThroughResultCount");
            writer.Write(snapshot.LodPassThroughResultCount);
            writer.PropertyName("lodAttemptedAreaLevelCount");
            writer.Write(snapshot.LodAttemptedAreaLevelCount);
            writer.PropertyName("lodSimplifiedAreaLevelCount");
            writer.Write(snapshot.LodSimplifiedAreaLevelCount);
            writer.PropertyName("lodLimitedAreaLevelCount");
            writer.Write(snapshot.LodLimitedAreaLevelCount);
            writer.PropertyName("lodBudgetMissedAreaLevelCount");
            writer.Write(snapshot.LodBudgetMissedAreaLevelCount);
            writer.PropertyName("lodAggressiveAreaLevelCount");
            writer.Write(snapshot.LodAggressiveAreaLevelCount);
            writer.PropertyName("lodPrunedTriangleCount");
            writer.Write(snapshot.LodPrunedTriangleCount);
            writer.PropertyName("lodStaleResultCount");
            writer.Write(snapshot.LodStaleResultCount);
            writer.PropertyName("lodCancelledJobCount");
            writer.Write(snapshot.LodCancelledJobCount);
            writer.PropertyName("lodFailedJobCount");
            writer.Write(snapshot.LodFailedJobCount);
            writer.PropertyName("lodRejectedCaptureCount");
            writer.Write(snapshot.LodRejectedCaptureCount);
            writer.PropertyName("lodMaximumCaptureSetupMilliseconds");
            writer.Write(FiniteNumber(snapshot.LodMaximumCaptureSetupMilliseconds));
            writer.PropertyName("lodMaximumCaptureCopyUpdateMilliseconds");
            writer.Write(FiniteNumber(snapshot.LodMaximumCaptureCopyUpdateMilliseconds));
            writer.PropertyName("lodMaximumWorkerJobMilliseconds");
            writer.Write(FiniteNumber(snapshot.LodMaximumWorkerJobMilliseconds));
            writer.PropertyName("lodPendingPublicationObjectCount");
            writer.Write(snapshot.LodPendingPublicationObjectCount);
            writer.PropertyName("lodPendingMeshUploadCount");
            writer.Write(snapshot.LodPendingMeshUploadCount);
            writer.PropertyName("lodPublishedObjectCount");
            writer.Write(snapshot.LodPublishedObjectCount);
            writer.PropertyName("lodPublishedMeshCount");
            writer.Write(snapshot.LodPublishedMeshCount);
            writer.PropertyName("lodPublishedResultCount");
            writer.Write(snapshot.LodPublishedResultCount);
            writer.PropertyName("lodDiscardedPublicationResultCount");
            writer.Write(snapshot.LodDiscardedPublicationResultCount);
            writer.PropertyName("lodFailedPublicationCount");
            writer.Write(snapshot.LodFailedPublicationCount);
            writer.PropertyName("lodLastPublicationStepMilliseconds");
            writer.Write(FiniteNumber(snapshot.LodLastPublicationStepMilliseconds));
            writer.PropertyName("lodMaximumPublicationStepMilliseconds");
            writer.Write(FiniteNumber(snapshot.LodMaximumPublicationStepMilliseconds));
            writer.PropertyName("lodLastPublicationFailureReason");
            writer.Write(snapshot.LodLastPublicationFailureReason ?? string.Empty);
            writer.PropertyName("lodHasLastAcceptedResult");
            writer.Write(snapshot.LodHasLastAcceptedResult);
            writer.PropertyName("lodLastAcceptedWorthPublishing");
            writer.Write(snapshot.LodLastAcceptedWorthPublishing);
            writer.PropertyName("lodLastSourceTriangleCount");
            writer.Write(snapshot.LodLastSourceTriangleCount);
            writer.PropertyName("lodLastLod1TriangleCount");
            writer.Write(snapshot.LodLastLod1TriangleCount);
            writer.PropertyName("lodLastLod2TriangleCount");
            writer.Write(snapshot.LodLastLod2TriangleCount);
            writer.PropertyName("lodLastLod1TargetTriangleCount");
            writer.Write(snapshot.LodLastLod1TargetTriangleCount);
            writer.PropertyName("lodLastLod2TargetTriangleCount");
            writer.Write(snapshot.LodLastLod2TargetTriangleCount);
            writer.PropertyName("lodLastLod1BudgetMet");
            writer.Write(snapshot.LodLastLod1BudgetMet);
            writer.PropertyName("lodLastLod2BudgetMet");
            writer.Write(snapshot.LodLastLod2BudgetMet);
            writer.PropertyName("lodLastLod1MaximumAbsoluteError");
            writer.Write(FiniteNumber(snapshot.LodLastLod1MaximumAbsoluteError));
            writer.PropertyName("lodLastLod2MaximumAbsoluteError");
            writer.Write(FiniteNumber(snapshot.LodLastLod2MaximumAbsoluteError));
            writer.PropertyName("lodLastFailureReason");
            writer.Write(snapshot.LodLastFailureReason ?? string.Empty);
            writer.PropertyName("heaviestObjects");
            writer.ArrayBegin((uint)snapshot.HeaviestObjects.Length);
            for (int i = 0; i < snapshot.HeaviestObjects.Length; i++)
            {
                PerformanceObjectSummary summary = snapshot.HeaviestObjects[i];
                writer.TypeBegin("PO2.PerformanceObject");
                writer.PropertyName("objectId");
                writer.Write(summary.ObjectId);
                writer.PropertyName("name");
                writer.Write(summary.Name ?? string.Empty);
                writer.PropertyName("meshVertexCount");
                writer.Write(summary.MeshVertexCount);
                writer.PropertyName("triangleCount");
                writer.Write(summary.TriangleCount);
                writer.PropertyName("hasLodResult");
                writer.Write(summary.HasLodResult);
                writer.PropertyName("lodWorthPublishing");
                writer.Write(summary.LodWorthPublishing);
                writer.PropertyName("lod1TriangleCount");
                writer.Write(summary.Lod1TriangleCount);
                writer.PropertyName("lod2TriangleCount");
                writer.Write(summary.Lod2TriangleCount);
                writer.PropertyName("lod1TargetTriangleCount");
                writer.Write(summary.Lod1TargetTriangleCount);
                writer.PropertyName("lod2TargetTriangleCount");
                writer.Write(summary.Lod2TargetTriangleCount);
                writer.PropertyName("lod1BudgetMet");
                writer.Write(summary.Lod1BudgetMet);
                writer.PropertyName("lod2BudgetMet");
                writer.Write(summary.Lod2BudgetMet);
                writer.PropertyName("lod1MaximumRelativeError");
                writer.Write(FiniteNumber(summary.Lod1MaximumRelativeError));
                writer.PropertyName("lod2MaximumRelativeError");
                writer.Write(FiniteNumber(summary.Lod2MaximumRelativeError));
                writer.PropertyName("lod1MaximumAbsoluteError");
                writer.Write(FiniteNumber(summary.Lod1MaximumAbsoluteError));
                writer.PropertyName("lod2MaximumAbsoluteError");
                writer.Write(FiniteNumber(summary.Lod2MaximumAbsoluteError));
                writer.PropertyName("lod1UsedPositionFocusedSimplification");
                writer.Write(summary.Lod1UsedPositionFocusedSimplification);
                writer.PropertyName("lod2UsedAggressiveSimplification");
                writer.Write(summary.Lod2UsedAggressiveSimplification);
                writer.PropertyName("lod2PrunedTriangleCount");
                writer.Write(summary.Lod2PrunedTriangleCount);
                writer.PropertyName("lodGenerationMilliseconds");
                writer.Write(FiniteNumber(summary.LodGenerationMilliseconds));
                writer.TypeEnd();
            }

            writer.ArrayEnd();
        }

        private static double FiniteNumber(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0d ? 0d : value;
        }
    }
}