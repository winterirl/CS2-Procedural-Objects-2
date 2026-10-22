using System;
using System.Diagnostics;
using PO2.Core.Lod;

namespace PO2.Rendering.Lod
{
    internal sealed partial class LodGenerationCoordinator
    {
        private ulong m_NextStamp = 1;
        private LodPreviewSession m_Preview;

        internal LodPreviewSession Preview => m_Preview;

        internal bool IsCurrentRequest(LodGenerationKey key)
        {
            return !m_Disposed && m_Objects.TryGetValue(key.ObjectId, out ObjectState state) && IsCurrent(state, key) && (!state.Selected || key.IsPreview);
        }

        internal LodPreviewSession BeginPreview(int objectId)
        {
            ThrowIfDisposed();
            if (!m_Objects.TryGetValue(objectId, out ObjectState state) || !state.Selected)
            {
                return null;
            }

            EndPreview();
            CancelCurrentWork(state);
            m_Preview = new LodPreviewSession(objectId, NextStamp(), state.Profile, state.ConfigurationRevision);
            m_Preview.RequestKey = CreatePreviewKey(state);
            return m_Preview;
        }

        internal bool SetPreviewDraft(LodProfile profile)
        {
            if (m_Preview == null || Equals(m_Preview.DraftProfile, profile))
            {
                return false;
            }

            bool generationChanged = !LodProfile.HasSameGeneration(m_Preview.DraftProfile, profile);
            m_Preview.DraftProfile = profile;
            if (generationChanged)
            {
                ObjectState state = m_Objects[m_Preview.ObjectId];
                CancelCurrentWork(state);
                m_Preview.ConfigurationRevision = NextStamp();
                m_Preview.RequestKey = CreatePreviewKey(state);
                m_Preview.HasCompletedGeneration = false;
                m_Preview.Status = LodPreviewStatus.OutOfDate;
                m_Preview.FailureReason = string.Empty;
            }

            return true;
        }

        internal bool RequestPreview()
        {
            if (m_Preview == null || !m_Objects.TryGetValue(m_Preview.ObjectId, out ObjectState state) || !state.Selected)
            {
                return false;
            }

            CancelCurrentWork(state);
            m_Preview.RequestKey = CreatePreviewKey(state);
            m_Preview.HasCompletedGeneration = false;
            m_Preview.CapturedBytes = m_Preview.CaptureBytes = 0;
            m_Preview.UploadedMeshes = m_Preview.UploadMeshes = 0;
            m_Preview.FailureReason = string.Empty;
            m_Preview.Status = LodPreviewStatus.Waiting;
            return true;
        }

        internal void CancelPreview()
        {
            if (m_Preview == null || !m_Objects.TryGetValue(m_Preview.ObjectId, out ObjectState state))
            {
                return;
            }

            CancelCurrentWork(state);
            ResetPreviewDraft(state, LodPreviewStatus.Cancelled);
        }

        internal void EndPreview()
        {
            if (m_Preview == null)
            {
                return;
            }

            int objectId = m_Preview.ObjectId;
            m_Preview = null;
            if (m_Objects.TryGetValue(objectId, out ObjectState state))
            {
                CancelCurrentWork(state);
                QueueIfNeeded(state);
            }
        }

        internal void MarkPreviewReady(LodGenerationKey key, int meshes)
        {
            if (!key.IsPreview || !IsCurrentRequest(key))
            {
                return;
            }

            m_Preview.UploadedMeshes = m_Preview.UploadMeshes = meshes;
            m_Preview.HasCompletedGeneration = true;
            m_Preview.Status = LodPreviewStatus.Ready;
        }

        internal void MarkPreviewUpload(LodGenerationKey key, int completed, int total)
        {
            if (!key.IsPreview || !IsCurrentRequest(key))
            {
                return;
            }

            m_Preview.UploadedMeshes = completed;
            m_Preview.UploadMeshes = total;
        }

        internal void MarkPreviewFailed(LodGenerationKey key, string reason)
        {
            if (key.IsPreview && IsCurrentRequest(key))
            {
                FailPreview(reason);
            }
        }

        internal void PublicationDiscarded(LodGenerationKey key)
        {
            if (key.IsPreview || !m_Objects.TryGetValue(key.ObjectId, out ObjectState state) || state.AttemptedKey != key || state.SourceRevision != key.SourceRevision || state.ConfigurationRevision != key.ConfigurationRevision)
            {
                return;
            }

            state.AttemptedRevision = 0;
            QueueIfNeeded(state);
        }

        internal bool TryPreparePromotion(out LodGenerationKey acceptedKey)
        {
            acceptedKey = default;
            if (m_Preview == null || m_Preview.Status != LodPreviewStatus.Ready || !m_Preview.HasCompletedGeneration || !IsCurrentRequest(m_Preview.RequestKey))
            {
                return false;
            }

            ObjectState state = m_Objects[m_Preview.ObjectId];
            acceptedKey = new LodGenerationKey(m_WorldEpoch, state.ObjectId, state.PersistentId, state.SourceRevision, m_Preview.ConfigurationRevision, 0, NextStamp());
            return true;
        }

        // Called after renderer activation and the small settings history entry succeed.
        internal void AcceptPreview(LodGenerationKey acceptedKey)
        {
            ObjectState state = m_Objects[m_Preview.ObjectId];
            state.Profile = m_Preview.DraftProfile;
            state.ConfigurationRevision = acceptedKey.ConfigurationRevision;
            state.RequestId = acceptedKey.RequestId;
            state.AttemptedRevision = state.SourceRevision;
            state.AttemptedConfiguration = state.ConfigurationRevision;
            state.AttemptedKey = acceptedKey;
            state.AcceptedDiagnostics = m_Preview.Diagnostics.WithKey(acceptedKey);
            state.HasAcceptedDiagnostics = true;
        }

        private bool TryAdvancePreview(LodSourceCaptureFactory captureFactory)
        {
            if (m_Preview == null)
            {
                return false;
            }

            if (m_Capture?.Capture.Key.IsPreview == true)
            {
                AdvanceCapture();
                return true;
            }

            // Keep background captures from delaying the selected result's upload.
            if (m_Preview.Status == LodPreviewStatus.Generating || m_Preview.Status == LodPreviewStatus.Uploading)
            {
                return true;
            }

            if (m_Preview.Status != LodPreviewStatus.Waiting)
            {
                return false;
            }

            if (!IsCurrentRequest(m_Preview.RequestKey))
            {
                FailPreview("The LOD source changed.");
                return true;
            }

            if (m_Capture != null)
            {
                ObjectState deferred = m_Capture.Object;
                m_Capture = null;
                QueueIfNeeded(deferred);
            }

            foreach (LodGenerationKey removed in m_Worker.DiscardQueued(includeBackground: true))
            {
                if (m_Objects.TryGetValue(removed.ObjectId, out ObjectState deferred) && deferred.HasInFlight && deferred.InFlightKey == removed)
                {
                    deferred.HasInFlight = false;
                    deferred.InFlightCancellation = null;
                    deferred.RequestId = NextStamp();
                    QueueIfNeeded(deferred);
                }
            }

            if (!m_Worker.HasCapacity)
            {
                return true;
            }

            long started = Stopwatch.GetTimestamp();
            try
            {
                LodGenerationKey key = m_Preview.RequestKey;
                ILodSourceCapture capture = captureFactory(key);
                if (capture == null || capture.Key != key || !IsCurrentRequest(key))
                {
                    FailPreview("Renderer source capture was unavailable.");
                    return true;
                }

                m_Capture = new CaptureState(m_Objects[key.ObjectId], capture, m_Preview.DraftProfile);
                ++m_CapturesStarted;
                m_Preview.CaptureBytes = capture.TotalBytes;
                m_Preview.Status = LodPreviewStatus.Capturing;
            }
            catch (Exception exception)
            {
                FailPreview(exception.Message);
            }
            finally
            {
                double elapsed = GetElapsedMilliseconds(started);
                m_CaptureSetupMilliseconds += elapsed;
                m_MaximumCaptureSetupMilliseconds = Math.Max(m_MaximumCaptureSetupMilliseconds, elapsed);
            }

            if (m_Capture != null)
            {
                AdvanceCapture();
            }

            return true;
        }

        private void ObserveProfile(ObjectState state, LodProfile profile)
        {
            if (Equals(state.Profile, profile))
            {
                return;
            }

            bool generationChanged = !LodProfile.HasSameGeneration(state.Profile, profile);
            state.Profile = profile;
            if (generationChanged)
            {
                CancelCurrentWork(state);
                state.ConfigurationRevision = NextStamp();
                state.HasAcceptedDiagnostics = false;
            }

            if (m_Preview?.ObjectId == state.ObjectId)
            {
                CancelCurrentWork(state);
                ResetPreviewDraft(state, LodPreviewStatus.OutOfDate);
            }
        }

        private void ResetPreviewDraft(ObjectState state, LodPreviewStatus status)
        {
            m_Preview.DraftProfile = state.Profile;
            m_Preview.ConfigurationRevision = state.ConfigurationRevision;
            m_Preview.RequestKey = CreatePreviewKey(state);
            m_Preview.Status = status;
            m_Preview.Display = LodPreviewDisplay.Original;
            m_Preview.HasCompletedGeneration = false;
            m_Preview.FailureReason = string.Empty;
        }

        private void FailPreview(string reason)
        {
            if (m_Preview == null)
            {
                return;
            }

            m_Preview.Status = LodPreviewStatus.Failed;
            m_Preview.HasCompletedGeneration = false;
            m_Preview.FailureReason = string.IsNullOrEmpty(reason) ? "LOD generation failed." : reason;
        }

        private LodGenerationKey CreatePreviewKey(ObjectState state)
        {
            return new LodGenerationKey(m_WorldEpoch, state.ObjectId, state.PersistentId, state.SourceRevision, m_Preview.ConfigurationRevision, m_Preview.SessionId, NextStamp());
        }

        private ulong NextStamp()
        {
            ulong value = m_NextStamp++;
            if (value == 0)
            {
                value = m_NextStamp++;
            }

            return value;
        }
    }
}