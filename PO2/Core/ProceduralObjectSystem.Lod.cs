using System;
using PO2.Rendering.Lod;
using PO2.Rendering.Lod.Native;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        private LodGenerationCoordinator m_LodCoordinator;
        private long m_LodObservedFailureCount;
        private string m_LodInitializationFailureReason = string.Empty;

        private void InitializeLodGeneration()
        {
            try
            {
                if (!MeshOptimizerNative.TryLoad(Mod.ModRootPath, out MeshOptimizerNative native, out string failureReason))
                {
                    m_LodInitializationFailureReason = failureReason;
                    Mod.log.Warn("[PO LOD] Runtime LOD generation is disabled: " + failureReason);
                    return;
                }

                LodSchedulerSettings schedulerSettings = LodSchedulerSettings.CreateRuntimeDefaults();
                LodGenerator generator = new LodGenerator(native, LodGenerationSettings.CreateCalibrationBaseline());
                LodGenerationWorker worker = new LodGenerationWorker(generator, schedulerSettings.MaximumOutstandingJobs);
                m_LodCoordinator = new LodGenerationCoordinator(worker, schedulerSettings);
                m_Renderer.ConfigureLodRequests(m_LodCoordinator.IsCurrentRequest, m_LodCoordinator.MarkPreviewReady, m_LodCoordinator.MarkPreviewFailed, m_LodCoordinator.MarkPreviewUpload, m_LodCoordinator.PublicationDiscarded);
                m_LodInitializationFailureReason = string.Empty;

                Mod.log.Info("[PO LOD] Detached runtime generation initialized with " + "one below-normal worker and a two-job limit.");
            }
            catch (Exception exception)
            {
                m_LodCoordinator?.Dispose();
                m_LodCoordinator = null;
                m_LodInitializationFailureReason = exception.Message;
                Mod.log.Error("[PO LOD] Runtime LOD initialization failed: " + exception);
            }
        }

        private void ObserveLodObject(ProceduralObject proceduralObject, ProceduralObjectDirtyFlags dirtyBeforeSync)
        {
            if (m_LodCoordinator == null || proceduralObject == null)
            {
                return;
            }

            bool sourceContentChanged = (dirtyBeforeSync & (ProceduralObjectDirtyFlags.Geometry | ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material)) != 0;
            LodPreviewSession previousSession = m_LodCoordinator.Preview;
            LodGenerationKey previousKey = previousSession?.RequestKey ?? default;
            m_LodCoordinator.ObserveObject(proceduralObject.Id, proceduralObject.PersistentId, proceduralObject.Transform.Scale, sourceContentChanged, proceduralObject.LodProfile);
            m_Renderer.SyncAcceptedLodProfile(proceduralObject.Id, proceduralObject.LodProfile);
            if (previousSession != null && previousSession.ObjectId == proceduralObject.Id && previousSession.RequestKey != previousKey)
            {
                m_Renderer.CancelLodPreview(previousSession.ObjectId, previousSession.SessionId, proceduralObject.LodProfile);
                m_Renderer.BeginLodPreview(previousSession.ObjectId, previousSession.SessionId, proceduralObject.LodProfile);
            }
        }

        private void UpdateLodGeneration()
        {
            if (m_LodCoordinator == null || m_Renderer == null)
            {
                return;
            }

            if (!IsToolActive)
            {
                EndLodPreview();
            }

            Setting settings = Mod.Settings;
            m_Renderer.SetLodDistanceMultipliers(settings?.LOD1Distance ?? 1.0f, settings?.LOD2Distance ?? 1.0f);

            m_LodCoordinator.SetSelectedObject(m_SelectedObjectId);
            LodSchedulerDiagnostics beforeUpdate = m_LodCoordinator.CaptureDiagnostics();
            m_LodCoordinator.Update(BeginLodSourceCapture, AcceptLodResult, m_Renderer.CanAdvanceLodGeneration);

            LodSchedulerDiagnostics diagnostics = m_LodCoordinator.CaptureDiagnostics();
            bool captureAdvanced = beforeUpdate.CaptureActive || diagnostics.CaptureActive || diagnostics.CapturesStarted != beforeUpdate.CapturesStarted;
            if (!captureAdvanced)
            {
                // Never stack a capture-copy slice and a Unity upload slice
                // in the same rendering update.
                m_Renderer.UpdateLodPublication();
            }

            LogLodPreviewFailure();
            long observedFailures = diagnostics.FailedJobs + diagnostics.RejectedCaptures;
            if (observedFailures > m_LodObservedFailureCount)
            {
                bool firstFailure = m_LodObservedFailureCount == 0;
                m_LodObservedFailureCount = observedFailures;
                if (firstFailure)
                {
                    Mod.log.Warn("[PO LOD] Detached generation rejected work: " + diagnostics.LastFailureReason + " Further failures remain available in diagnostics.");
                }
            }
        }

        private ILodSourceCapture BeginLodSourceCapture(LodGenerationKey key)
        {
            if (!m_Objects.TryGetValue(key.ObjectId, out ProceduralObject proceduralObject) || proceduralObject.PersistentId != key.PersistentId || !m_LodCoordinator.IsCurrentRequest(key))
            {
                return null;
            }

            return m_Renderer.BeginLodSourceCapture(proceduralObject, key);
        }

        private void AcceptLodResult(LodGenerationResult result)
        {
            if (result == null)
            {
                return;
            }

            if (result.Key.IsPreview)
            {
                if (!m_Renderer.QueueLodResult(result))
                {
                    m_LodCoordinator.MarkPreviewFailed(result.Key, "The renderer rejected this preview.");
                }

                return;
            }

            m_Renderer.QueueLodResult(result);
        }

        private void NotifyLodSelectionChanged()
        {
            if (m_LodCoordinator?.Preview != null && m_LodCoordinator.Preview.ObjectId != m_SelectedObjectId)
            {
                EndLodPreview();
            }

            m_LodCoordinator?.SetSelectedObject(m_SelectedObjectId);
            m_Renderer?.SetSelectedLodObject(m_SelectedObjectId);
        }

        private void NotifyLodObjectRemoved(int objectId)
        {
            if (m_LodCoordinator?.Preview?.ObjectId == objectId)
            {
                EndLodPreview();
            }

            m_LodCoordinator?.RemoveObject(objectId);
        }

        private void ResetLodWorld()
        {
            EndLodPreview();
            m_LodCoordinator?.ResetWorld();
        }

        private void ShutdownLodGeneration()
        {
            if (m_LodCoordinator == null)
            {
                return;
            }

            EndLodPreview();
            m_LodCoordinator.Dispose();
            m_LodCoordinator = null;
            m_Renderer?.ConfigureLodRequests(null, null, null, null);
        }

        internal LodSchedulerDiagnostics CaptureLodDiagnostics()
        {
            return m_LodCoordinator != null ? m_LodCoordinator.CaptureDiagnostics() : default(LodSchedulerDiagnostics);
        }

        internal bool TryCaptureLodObjectDiagnostics(int objectId, out LodObjectDiagnostics diagnostics)
        {
            if (m_LodCoordinator != null)
            {
                return m_LodCoordinator.TryCaptureObjectDiagnostics(objectId, out diagnostics);
            }

            diagnostics = default(LodObjectDiagnostics);
            return false;
        }
    }
}