using System;
using System.Collections.Generic;
using System.Diagnostics;
using PO2.Core.Lod;
using UnityEngine;

namespace PO2.Rendering.Lod
{
    internal delegate ILodSourceCapture LodSourceCaptureFactory(LodGenerationKey key);

    internal struct LodObjectDiagnostics
    {
        internal LodObjectDiagnostics(LodGenerationResult result)
        {
            LodGenerationStatistics statistics = result.Statistics;
            SourceRevision = result.Key.SourceRevision;
            ConfigurationRevision = result.Key.ConfigurationRevision;
            WorthPublishing = result.WorthPublishing;
            SourceTriangles = statistics.SourceTriangles;
            Lod1Triangles = statistics.Lod1Triangles;
            Lod2Triangles = statistics.Lod2Triangles;
            Lod1TargetTriangles = statistics.Lod1TargetTriangles;
            Lod2TargetTriangles = statistics.Lod2TargetTriangles;
            Lod1BudgetMet = statistics.Lod1BudgetMet;
            Lod2BudgetMet = statistics.Lod2BudgetMet;
            Lod1MaximumRelativeError = result.Lod1MaximumRelativeError;
            Lod2MaximumRelativeError = result.Lod2MaximumRelativeError;
            Lod1MaximumAbsoluteError = result.Lod1MaximumAbsoluteError;
            Lod2MaximumAbsoluteError = result.Lod2MaximumAbsoluteError;
            Lod1UsedPositionFocusedSimplification = UsesLod1Method(result, LodSimplificationMethod.PositionFocused);
            Lod2UsedAggressiveSimplification = Array.Exists(result.Areas, x => x.Lod2.UsedAggressiveSimplification);
            PrunedTriangles = statistics.PrunedTriangles;
            GenerationMilliseconds = statistics.GenerationMilliseconds;
        }

        internal ulong SourceRevision { get; private set; }
        internal ulong ConfigurationRevision { get; private set; }

        internal LodObjectDiagnostics WithKey(LodGenerationKey key)
        {
            LodObjectDiagnostics copy = this;
            copy.SourceRevision = key.SourceRevision;
            copy.ConfigurationRevision = key.ConfigurationRevision;
            return copy;
        }

        internal bool WorthPublishing { get; private set; }
        internal int SourceTriangles { get; }
        internal int Lod1Triangles { get; private set; }
        internal int Lod2Triangles { get; private set; }
        internal int Lod1TargetTriangles { get; private set; }
        internal int Lod2TargetTriangles { get; private set; }
        internal bool Lod1BudgetMet { get; private set; }
        internal bool Lod2BudgetMet { get; private set; }
        internal double Lod1MaximumRelativeError { get; }
        internal double Lod2MaximumRelativeError { get; }
        internal double Lod1MaximumAbsoluteError { get; }
        internal double Lod2MaximumAbsoluteError { get; }
        internal bool Lod1UsedPositionFocusedSimplification { get; }
        internal bool Lod2UsedAggressiveSimplification { get; }
        internal int PrunedTriangles { get; }
        internal double GenerationMilliseconds { get; }

        internal LodObjectDiagnostics ForProfile(LodProfile profile)
        {
            LodObjectDiagnostics copy = this;
            LodProfile settings = LodProfile.Resolve(profile);
            if (!settings.Lod1.Enabled)
            {
                copy.Lod1Triangles = copy.Lod1TargetTriangles = 0;
                copy.Lod1BudgetMet = true;
            }

            if (!settings.Lod2.Enabled)
            {
                copy.Lod2Triangles = copy.Lod2TargetTriangles = 0;
                copy.Lod2BudgetMet = true;
            }

            copy.WorthPublishing = LodAutomaticChain.Resolve(profile, SourceTriangles, copy.Lod1Triangles, copy.Lod2Triangles, LodGenerationSettings.CreateCalibrationBaseline().MinimumWholeObjectTriangleReduction).HasGeneratedLevels;
            return copy;
        }

        private static bool UsesLod1Method(LodGenerationResult result, LodSimplificationMethod method)
        {
            for (int i = 0; i < result.Areas.Length; ++i)
            {
                if (result.Areas[i].Lod1.Method == method)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal readonly struct LodSchedulerDiagnostics
    {
        internal LodSchedulerDiagnostics(long worldEpoch, int trackedObjects, int outstandingJobs, bool captureActive, long capturesStarted, long capturedBytes, long submittedJobs, long completedJobs, long acceptedResults, long publishableResults, long passThroughResults, long attemptedAreaLevels, long simplifiedAreaLevels, long limitedAreaLevels, long budgetMissedAreaLevels, long aggressiveAreaLevels, long prunedTriangles, long staleResults, long cancelledJobs, long failedJobs, long rejectedCaptures, double captureSetupMilliseconds, double maximumCaptureSetupMilliseconds, double captureCopyMilliseconds, double maximumCaptureCopyUpdateMilliseconds, double workerMilliseconds, double maximumWorkerJobMilliseconds, bool hasLastAcceptedResult, bool lastAcceptedWorthPublishing, int lastSourceTriangles, int lastLod1Triangles, int lastLod2Triangles, int lastLod1TargetTriangles, int lastLod2TargetTriangles, bool lastLod1BudgetMet, bool lastLod2BudgetMet, double lastLod1MaximumAbsoluteError, double lastLod2MaximumAbsoluteError, string lastFailureReason, double allocationMilliseconds = 0, double maximumAllocationMilliseconds = 0)
        {
            WorldEpoch = worldEpoch;
            TrackedObjects = trackedObjects;
            OutstandingJobs = outstandingJobs;
            CaptureActive = captureActive;
            CapturesStarted = capturesStarted;
            CapturedBytes = capturedBytes;
            SubmittedJobs = submittedJobs;
            CompletedJobs = completedJobs;
            AcceptedResults = acceptedResults;
            PublishableResults = publishableResults;
            PassThroughResults = passThroughResults;
            AttemptedAreaLevels = attemptedAreaLevels;
            SimplifiedAreaLevels = simplifiedAreaLevels;
            LimitedAreaLevels = limitedAreaLevels;
            BudgetMissedAreaLevels = budgetMissedAreaLevels;
            AggressiveAreaLevels = aggressiveAreaLevels;
            PrunedTriangles = prunedTriangles;
            StaleResults = staleResults;
            CancelledJobs = cancelledJobs;
            FailedJobs = failedJobs;
            RejectedCaptures = rejectedCaptures;
            CaptureSetupMilliseconds = captureSetupMilliseconds;
            MaximumCaptureSetupMilliseconds = maximumCaptureSetupMilliseconds;
            CaptureCopyMilliseconds = captureCopyMilliseconds;
            MaximumCaptureCopyUpdateMilliseconds = maximumCaptureCopyUpdateMilliseconds;
            WorkerMilliseconds = workerMilliseconds;
            MaximumWorkerJobMilliseconds = maximumWorkerJobMilliseconds;
            HasLastAcceptedResult = hasLastAcceptedResult;
            LastAcceptedWorthPublishing = lastAcceptedWorthPublishing;
            LastSourceTriangles = lastSourceTriangles;
            LastLod1Triangles = lastLod1Triangles;
            LastLod2Triangles = lastLod2Triangles;
            LastLod1TargetTriangles = lastLod1TargetTriangles;
            LastLod2TargetTriangles = lastLod2TargetTriangles;
            LastLod1BudgetMet = lastLod1BudgetMet;
            LastLod2BudgetMet = lastLod2BudgetMet;
            LastLod1MaximumAbsoluteError = lastLod1MaximumAbsoluteError;
            LastLod2MaximumAbsoluteError = lastLod2MaximumAbsoluteError;
            LastFailureReason = lastFailureReason ?? string.Empty;
            CaptureAllocationMilliseconds = allocationMilliseconds;
            MaximumCaptureAllocationMilliseconds = maximumAllocationMilliseconds;
        }

        internal long WorldEpoch { get; }
        internal int TrackedObjects { get; }
        internal int OutstandingJobs { get; }
        internal bool CaptureActive { get; }
        internal long CapturesStarted { get; }
        internal long CapturedBytes { get; }
        internal long SubmittedJobs { get; }
        internal long CompletedJobs { get; }
        internal long AcceptedResults { get; }
        internal long PublishableResults { get; }
        internal long PassThroughResults { get; }
        internal long AttemptedAreaLevels { get; }
        internal long SimplifiedAreaLevels { get; }
        internal long LimitedAreaLevels { get; }
        internal long BudgetMissedAreaLevels { get; }
        internal long AggressiveAreaLevels { get; }
        internal long PrunedTriangles { get; }
        internal long StaleResults { get; }
        internal long CancelledJobs { get; }
        internal long FailedJobs { get; }
        internal long RejectedCaptures { get; }
        internal double CaptureSetupMilliseconds { get; }
        internal double MaximumCaptureSetupMilliseconds { get; }
        internal double CaptureCopyMilliseconds { get; }
        internal double MaximumCaptureCopyUpdateMilliseconds { get; }
        internal double WorkerMilliseconds { get; }
        internal double MaximumWorkerJobMilliseconds { get; }
        internal bool HasLastAcceptedResult { get; }
        internal bool LastAcceptedWorthPublishing { get; }
        internal int LastSourceTriangles { get; }
        internal int LastLod1Triangles { get; }
        internal int LastLod2Triangles { get; }
        internal int LastLod1TargetTriangles { get; }
        internal int LastLod2TargetTriangles { get; }
        internal bool LastLod1BudgetMet { get; }
        internal bool LastLod2BudgetMet { get; }
        internal double LastLod1MaximumAbsoluteError { get; }
        internal double LastLod2MaximumAbsoluteError { get; }
        internal string LastFailureReason { get; }
        internal double CaptureAllocationMilliseconds { get; }
        internal double MaximumCaptureAllocationMilliseconds { get; }
    }

    // Polls detached LOD work without waiting. A result must match the full
    // request key before it reaches the callback.
    internal sealed partial class LodGenerationCoordinator : IDisposable
    {
        private readonly LodGenerationWorker m_Worker;
        private readonly LodSchedulerSettings m_Settings;

        private readonly Dictionary<int, ObjectState> m_Objects = new Dictionary<int, ObjectState>();

        // Queue<T> is exposed twice by the game's reference set.
        // A list with a read cursor keeps FIFO behavior without CS0433.
        private readonly List<int> m_Candidates = new List<int>();
        private int m_CandidateIndex;

        private long m_WorldEpoch = 1;
        private ulong m_NextSourceRevision = 1;
        private int? m_SelectedObjectId;
        private CaptureState m_Capture;
        private bool m_Disposed;

        private long m_CapturesStarted;
        private long m_CapturedBytes;
        private long m_SubmittedJobs;
        private long m_CompletedJobs;
        private long m_AcceptedResults;
        private long m_PublishableResults;
        private long m_PassThroughResults;
        private long m_AttemptedAreaLevels;
        private long m_SimplifiedAreaLevels;
        private long m_LimitedAreaLevels;
        private long m_BudgetMissedAreaLevels;
        private long m_AggressiveAreaLevels;
        private long m_PrunedTriangles;
        private long m_StaleResults;
        private long m_CancelledJobs;
        private long m_FailedJobs;
        private long m_RejectedCaptures;
        private double m_CaptureSetupMilliseconds;
        private double m_MaximumCaptureSetupMilliseconds;
        private double m_CaptureAllocationMilliseconds;
        private double m_MaximumCaptureAllocationMilliseconds;
        private double m_CaptureCopyMilliseconds;
        private double m_MaximumCaptureCopyUpdateMilliseconds;
        private double m_WorkerMilliseconds;
        private double m_MaximumWorkerJobMilliseconds;
        private bool m_HasLastAcceptedResult;
        private bool m_LastAcceptedWorthPublishing;
        private int m_LastSourceTriangles;
        private int m_LastLod1Triangles;
        private int m_LastLod2Triangles;
        private int m_LastLod1TargetTriangles;
        private int m_LastLod2TargetTriangles;
        private bool m_LastLod1BudgetMet;
        private bool m_LastLod2BudgetMet;
        private double m_LastLod1MaximumAbsoluteError;
        private double m_LastLod2MaximumAbsoluteError;
        private string m_LastFailureReason = string.Empty;

        internal LodGenerationCoordinator(LodGenerationWorker worker, LodSchedulerSettings settings)
        {
            m_Worker = worker ?? throw new ArgumentNullException(nameof(worker));
            m_Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        internal void SetSelectedObject(int? objectId)
        {
            ThrowIfDisposed();
            if (m_SelectedObjectId == objectId)
            {
                return;
            }

            if (m_Preview != null && m_Preview.ObjectId != objectId)
            {
                EndPreview();
            }

            if (m_SelectedObjectId.HasValue && m_Objects.TryGetValue(m_SelectedObjectId.Value, out ObjectState previous))
            {
                previous.Selected = false;
                QueueIfNeeded(previous);
            }

            m_SelectedObjectId = objectId;
            if (objectId.HasValue && m_Objects.TryGetValue(objectId.Value, out ObjectState selected))
            {
                selected.Selected = true;
                CancelCurrentWork(selected);
            }
        }

        internal void ObserveObject(int objectId, Guid persistentId, Vector3 objectScale, bool sourceContentChanged, LodProfile profile = null)
        {
            ThrowIfDisposed();
            if (objectId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(objectId));
            }

            if (persistentId == Guid.Empty)
            {
                throw new ArgumentException("LOD tracking requires a persistent object identity.", nameof(persistentId));
            }

            if (!m_Objects.TryGetValue(objectId, out ObjectState state) || state.PersistentId != persistentId)
            {
                if (state != null)
                {
                    RemoveObject(objectId);
                }

                state = new ObjectState(objectId, persistentId, objectScale, NextSourceRevision(), m_SelectedObjectId == objectId);
                state.Profile = profile;
                state.ConfigurationRevision = NextStamp();
                state.RequestId = NextStamp();
                m_Objects.Add(objectId, state);
                QueueIfNeeded(state);
                return;
            }

            ObserveProfile(state, profile);
            state.Selected = m_SelectedObjectId == objectId;
            bool scaleChanged = !ExactScaleEquals(state.ObjectScale, objectScale);
            if (sourceContentChanged || scaleChanged)
            {
                InvalidateSource(state, objectScale);
            }
            else
            {
                QueueIfNeeded(state);
            }
        }

        internal void RemoveObject(int objectId)
        {
            ThrowIfDisposed();
            if (!m_Objects.TryGetValue(objectId, out ObjectState state))
            {
                return;
            }

            if (m_Preview?.ObjectId == objectId)
            {
                EndPreview();
            }

            CancelCurrentWork(state);
            state.CandidateQueued = false;
            m_Objects.Remove(objectId);
            if (m_SelectedObjectId == objectId)
            {
                m_SelectedObjectId = null;
            }
        }

        internal void ResetWorld()
        {
            ThrowIfDisposed();
            EndPreview();
            foreach (ObjectState state in m_Objects.Values)
            {
                CancelCurrentWork(state);
            }

            m_Objects.Clear();
            m_Capture = null;
            ClearCandidates();
            m_SelectedObjectId = null;
            unchecked
            {
                m_WorldEpoch++;
                if (m_WorldEpoch == 0)
                {
                    m_WorldEpoch = 1;
                }
            }
        }

        internal void Update(LodSourceCaptureFactory captureFactory, Action<LodGenerationResult> acceptResult, Func<bool> canAdvanceGeneration = null)
        {
            ThrowIfDisposed();
            if (captureFactory == null)
            {
                throw new ArgumentNullException(nameof(captureFactory));
            }

            DrainCompletions(acceptResult);
            if (TryAdvancePreview(captureFactory))
            {
                return;
            }

            if (m_Capture != null)
            {
                AdvanceCapture();
                return;
            }

            if (canAdvanceGeneration != null && !canAdvanceGeneration())
            {
                return;
            }

            if (!m_Worker.HasCapacity)
            {
                return;
            }

            ObjectState candidate = DequeueCandidate();
            if (candidate == null)
            {
                return;
            }

            LodGenerationKey key = CreateKey(candidate);
            ILodSourceCapture capture;
            long captureSetupStart = Stopwatch.GetTimestamp();
            try
            {
                capture = captureFactory(key);
            }
            catch (Exception exception)
            {
                RejectCapture(candidate, exception.Message);
                return;
            }
            finally
            {
                double elapsed = GetElapsedMilliseconds(captureSetupStart);
                m_CaptureSetupMilliseconds += elapsed;
                m_MaximumCaptureSetupMilliseconds = Math.Max(m_MaximumCaptureSetupMilliseconds, elapsed);
            }

            if (capture == null || capture.Key != key)
            {
                RejectCapture(candidate, capture == null ? "Renderer source capture was unavailable." : "Renderer source capture returned the wrong key.");
                return;
            }

            m_Capture = new CaptureState(candidate, capture, candidate.Profile);
            m_CapturesStarted++;
            AdvanceCapture();
        }

        internal LodSchedulerDiagnostics CaptureDiagnostics()
        {
            return new LodSchedulerDiagnostics(m_WorldEpoch, m_Objects.Count, m_Worker.OutstandingJobs, m_Capture != null, m_CapturesStarted, m_CapturedBytes, m_SubmittedJobs, m_CompletedJobs, m_AcceptedResults, m_PublishableResults, m_PassThroughResults, m_AttemptedAreaLevels, m_SimplifiedAreaLevels, m_LimitedAreaLevels, m_BudgetMissedAreaLevels, m_AggressiveAreaLevels, m_PrunedTriangles, m_StaleResults, m_CancelledJobs, m_FailedJobs, m_RejectedCaptures, m_CaptureSetupMilliseconds, m_MaximumCaptureSetupMilliseconds, m_CaptureCopyMilliseconds, m_MaximumCaptureCopyUpdateMilliseconds, m_WorkerMilliseconds, m_MaximumWorkerJobMilliseconds, m_HasLastAcceptedResult, m_LastAcceptedWorthPublishing, m_LastSourceTriangles, m_LastLod1Triangles, m_LastLod2Triangles, m_LastLod1TargetTriangles, m_LastLod2TargetTriangles, m_LastLod1BudgetMet, m_LastLod2BudgetMet, m_LastLod1MaximumAbsoluteError, m_LastLod2MaximumAbsoluteError, m_LastFailureReason, m_CaptureAllocationMilliseconds, m_MaximumCaptureAllocationMilliseconds);
        }

        internal bool TryCaptureObjectDiagnostics(int objectId, out LodObjectDiagnostics diagnostics)
        {
            ThrowIfDisposed();
            if (m_Objects.TryGetValue(objectId, out ObjectState state) && state.HasAcceptedDiagnostics && state.AcceptedDiagnostics.SourceRevision == state.SourceRevision && state.AcceptedDiagnostics.ConfigurationRevision == state.ConfigurationRevision)
            {
                diagnostics = state.AcceptedDiagnostics;
                return true;
            }

            diagnostics = default(LodObjectDiagnostics);
            return false;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            EndPreview();
            m_Disposed = true;
            foreach (ObjectState state in m_Objects.Values)
            {
                state.InFlightCancellation?.Cancel();
            }

            m_Objects.Clear();
            m_Capture = null;
            ClearCandidates();
            m_Worker.Dispose();
        }

        private void AdvanceCapture()
        {
            CaptureState captureState = m_Capture;
            if (captureState == null)
            {
                return;
            }

            if (!IsCurrentRequest(captureState.Capture.Key))
            {
                m_Capture = null;
                QueueIfNeeded(captureState.Object);
                return;
            }

            try
            {
                if (!captureState.Capture.IsComplete)
                {
                    long before = captureState.Capture.CopiedBytes;
                    var metrics = captureState.Capture as ILodCaptureMetrics;
                    double previousAllocation = metrics?.AllocationMilliseconds ?? 0;
                    long copyStart = Stopwatch.GetTimestamp();
                    captureState.Capture.CopyNext(m_Settings.MaximumCaptureBytesPerUpdate);
                    double copyElapsed = GetElapsedMilliseconds(copyStart);
                    if (metrics != null)
                    {
                        m_CaptureAllocationMilliseconds += metrics.AllocationMilliseconds - previousAllocation;
                        m_MaximumCaptureAllocationMilliseconds = Math.Max(m_MaximumCaptureAllocationMilliseconds, metrics.MaximumAllocationMilliseconds);
                    }

                    m_CaptureCopyMilliseconds += copyElapsed;
                    m_MaximumCaptureCopyUpdateMilliseconds = Math.Max(m_MaximumCaptureCopyUpdateMilliseconds, copyElapsed);
                    long copied = captureState.Capture.CopiedBytes - before;
                    if (copied > 0)
                    {
                        m_CapturedBytes += copied;
                    }

                    if (captureState.Capture.Key.IsPreview && m_Preview != null)
                    {
                        m_Preview.CapturedBytes = captureState.Capture.CopiedBytes;
                    }
                }

                if (!captureState.Capture.IsComplete)
                {
                    return;
                }

                if (!m_Worker.HasCapacity)
                {
                    return;
                }

                LodCapturedSource source = captureState.Capture.Complete();
                if (source == null || source.Key != captureState.Capture.Key)
                {
                    throw new InvalidOperationException("Completed renderer capture returned the wrong key.");
                }

                if (!IsCurrentRequest(source.Key))
                {
                    m_Capture = null;
                    return;
                }

                if (!m_Worker.TrySubmit(source, captureState.Profile, out LodCancellationSignal cancellation))
                {
                    return;
                }

                ObjectState state = captureState.Object;
                state.HasInFlight = true;
                state.InFlightKey = source.Key;
                state.InFlightCancellation = cancellation;
                if (source.Key.IsPreview)
                {
                    m_Preview.Status = LodPreviewStatus.Generating;
                }

                m_SubmittedJobs++;
                m_Capture = null;
            }
            catch (Exception exception)
            {
                m_Capture = null;
                if (captureState.Capture.Key.IsPreview)
                {
                    FailPreview(exception.Message);
                }
                else
                {
                    RejectCapture(captureState.Object, exception.Message);
                }
            }
        }

        private void DrainCompletions(Action<LodGenerationResult> acceptResult)
        {
            for (int i = 0; i < m_Settings.MaximumCompletionsPerUpdate && m_Worker.TryDequeueCompletion(out LodGenerationCompletion completion); ++i)
            {
                m_CompletedJobs++;
                m_WorkerMilliseconds += completion.WorkerMilliseconds;
                m_MaximumWorkerJobMilliseconds = Math.Max(m_MaximumWorkerJobMilliseconds, completion.WorkerMilliseconds);
                LodGenerationAttempt attempt = completion.Attempt;
                if (attempt.Status == LodGenerationStatus.Cancelled)
                {
                    m_CancelledJobs++;
                }
                else if (attempt.Status == LodGenerationStatus.Failed)
                {
                    m_FailedJobs++;
                    m_LastFailureReason = attempt.FailureReason;
                }

                if (!m_Objects.TryGetValue(completion.Key.ObjectId, out ObjectState state) || !IsCurrentRequest(completion.Key))
                {
                    m_StaleResults++;
                    continue;
                }

                if (state.HasInFlight && state.InFlightKey == completion.Key)
                {
                    state.HasInFlight = false;
                    state.InFlightCancellation = null;
                }

                if (state.Selected && !completion.Key.IsPreview)
                {
                    m_StaleResults++;
                    continue;
                }

                if (attempt.Status == LodGenerationStatus.Cancelled)
                {
                    if (completion.Key.IsPreview)
                    {
                        m_Preview.Status = LodPreviewStatus.Cancelled;
                    }

                    QueueIfNeeded(state);
                    continue;
                }

                if (!completion.Key.IsPreview)
                {
                    state.AttemptedRevision = state.SourceRevision;
                    state.AttemptedConfiguration = state.ConfigurationRevision;
                    state.AttemptedKey = completion.Key;
                }

                if (attempt.Status != LodGenerationStatus.Completed || attempt.Result == null || attempt.Result.Key != completion.Key || !LodProfile.HasSameGeneration(attempt.Result.Profile, completion.Key.IsPreview ? m_Preview.DraftProfile : state.Profile))
                {
                    if (attempt.Status == LodGenerationStatus.Completed)
                    {
                        m_FailedJobs++;
                        m_LastFailureReason = "Completed LOD work returned an invalid result.";
                    }

                    if (completion.Key.IsPreview)
                    {
                        FailPreview(attempt.FailureReason);
                    }

                    continue;
                }

                LodGenerationResult result = attempt.Result;
                m_AcceptedResults++;
                if (result.WorthPublishing)
                {
                    m_PublishableResults++;
                }
                else
                {
                    m_PassThroughResults++;
                }

                m_AttemptedAreaLevels += result.Statistics.AttemptedAreaLevels;
                m_SimplifiedAreaLevels += result.Statistics.SimplifiedAreaLevels;
                m_LimitedAreaLevels += result.Statistics.LimitedAreaLevels;
                m_BudgetMissedAreaLevels += result.Statistics.BudgetMissedAreaLevels;
                m_AggressiveAreaLevels += result.Statistics.AggressiveAreaLevels;
                m_PrunedTriangles += result.Statistics.PrunedTriangles;
                m_HasLastAcceptedResult = true;
                m_LastAcceptedWorthPublishing = result.WorthPublishing;
                m_LastSourceTriangles = result.Statistics.SourceTriangles;
                m_LastLod1Triangles = result.Statistics.Lod1Triangles;
                m_LastLod2Triangles = result.Statistics.Lod2Triangles;
                m_LastLod1TargetTriangles = result.Statistics.Lod1TargetTriangles;
                m_LastLod2TargetTriangles = result.Statistics.Lod2TargetTriangles;
                m_LastLod1BudgetMet = result.Statistics.Lod1BudgetMet;
                m_LastLod2BudgetMet = result.Statistics.Lod2BudgetMet;
                m_LastLod1MaximumAbsoluteError = result.Lod1MaximumAbsoluteError;
                m_LastLod2MaximumAbsoluteError = result.Lod2MaximumAbsoluteError;
                if (completion.Key.IsPreview)
                {
                    m_Preview.Diagnostics = new LodObjectDiagnostics(result);
                    m_Preview.AutomaticChain = result.AutomaticChain;
                    m_Preview.HasCompletedGeneration = true;
                    m_Preview.Status = LodPreviewStatus.Uploading;
                }
                else
                {
                    state.AcceptedDiagnostics = new LodObjectDiagnostics(result);
                    state.HasAcceptedDiagnostics = true;
                }

                try
                {
                    acceptResult?.Invoke(result);
                }
                catch (Exception exception)
                {
                    m_FailedJobs++;
                    m_LastFailureReason = "LOD result publication failed: " + exception.Message;
                    if (completion.Key.IsPreview)
                    {
                        FailPreview(m_LastFailureReason);
                    }
                }
            }
        }

        private ObjectState DequeueCandidate()
        {
            while (m_CandidateIndex < m_Candidates.Count)
            {
                int objectId = m_Candidates[m_CandidateIndex++];
                if (!m_Objects.TryGetValue(objectId, out ObjectState state))
                {
                    continue;
                }

                state.CandidateQueued = false;
                if (state.Selected || state.HasInFlight || (state.AttemptedRevision == state.SourceRevision && state.AttemptedConfiguration == state.ConfigurationRevision))
                {
                    continue;
                }

                return state;
            }

            ClearCandidates();
            return null;
        }

        private void ClearCandidates()
        {
            m_Candidates.Clear();
            m_CandidateIndex = 0;
        }

        private void InvalidateSource(ObjectState state, Vector3 objectScale)
        {
            CancelCurrentWork(state);
            state.ObjectScale = objectScale;
            state.SourceRevision = NextSourceRevision();
            if (m_Preview?.ObjectId == state.ObjectId)
            {
                ResetPreviewDraft(state, LodPreviewStatus.OutOfDate);
            }

            state.AttemptedRevision = 0;
            state.HasAcceptedDiagnostics = false;
            state.AcceptedDiagnostics = default(LodObjectDiagnostics);
            QueueIfNeeded(state);
        }

        private void CancelCurrentWork(ObjectState state)
        {
            state.RequestId = NextStamp();
            state.InFlightCancellation?.Cancel();
            state.HasInFlight = false;
            state.InFlightCancellation = null;
            m_Worker.DiscardQueued();

            if (m_Capture != null && m_Capture.Object.ObjectId == state.ObjectId)
            {
                m_Capture = null;
            }
        }

        private void QueueIfNeeded(ObjectState state)
        {
            if (state.Selected || state.CandidateQueued || state.HasInFlight || (state.AttemptedRevision == state.SourceRevision && state.AttemptedConfiguration == state.ConfigurationRevision))
            {
                return;
            }

            state.CandidateQueued = true;
            m_Candidates.Add(state.ObjectId);
        }

        private void RejectCapture(ObjectState state, string reason)
        {
            if (state != null && m_Objects.TryGetValue(state.ObjectId, out ObjectState current) && ReferenceEquals(state, current))
            {
                state.AttemptedRevision = state.SourceRevision;
                state.AttemptedConfiguration = state.ConfigurationRevision;
            }

            m_RejectedCaptures++;
            m_LastFailureReason = reason ?? string.Empty;
        }

        private LodGenerationKey CreateKey(ObjectState state)
        {
            return new LodGenerationKey(m_WorldEpoch, state.ObjectId, state.PersistentId, state.SourceRevision, state.ConfigurationRevision, 0, state.RequestId);
        }

        private bool IsCurrent(ObjectState state, LodGenerationKey key)
        {
            return state != null && key.WorldEpoch == m_WorldEpoch && key.ObjectId == state.ObjectId && key.PersistentId == state.PersistentId && key.SourceRevision == state.SourceRevision && (key.IsPreview ? m_Preview != null && key == m_Preview.RequestKey && key.PreviewSessionId == m_Preview.SessionId : key.ConfigurationRevision == state.ConfigurationRevision && key.RequestId == state.RequestId);
        }

        private ulong NextSourceRevision()
        {
            ulong revision = m_NextSourceRevision++;
            if (revision == 0)
            {
                revision = m_NextSourceRevision++;
            }

            return revision;
        }

        private static bool ExactScaleEquals(Vector3 left, Vector3 right)
        {
            return left.x.Equals(right.x) && left.y.Equals(right.y) && left.z.Equals(right.z);
        }

        private static double GetElapsedMilliseconds(long start)
        {
            return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
            {
                throw new ObjectDisposedException(nameof(LodGenerationCoordinator));
            }
        }

        private sealed class ObjectState
        {
            internal ObjectState(int objectId, Guid persistentId, Vector3 objectScale, ulong sourceRevision, bool selected)
            {
                ObjectId = objectId;
                PersistentId = persistentId;
                ObjectScale = objectScale;
                SourceRevision = sourceRevision;
                Selected = selected;
            }

            internal int ObjectId { get; }
            internal Guid PersistentId { get; }
            internal Vector3 ObjectScale { get; set; }
            internal ulong SourceRevision { get; set; }
            internal ulong AttemptedRevision { get; set; }
            internal ulong AttemptedConfiguration;
            internal LodGenerationKey AttemptedKey;
            internal ulong ConfigurationRevision;
            internal ulong RequestId;
            internal LodProfile Profile;
            internal bool Selected { get; set; }
            internal bool CandidateQueued { get; set; }
            internal bool HasInFlight { get; set; }
            internal LodGenerationKey InFlightKey { get; set; }
            internal LodCancellationSignal InFlightCancellation { get; set; }
            internal bool HasAcceptedDiagnostics { get; set; }
            internal LodObjectDiagnostics AcceptedDiagnostics { get; set; }
        }

        private sealed class CaptureState
        {
            internal CaptureState(ObjectState state, ILodSourceCapture capture, LodProfile profile)
            {
                Profile = profile;
                Object = state;
                Capture = capture;
            }

            internal LodProfile Profile { get; }
            internal ObjectState Object { get; }
            internal ILodSourceCapture Capture { get; }
        }
    }
}