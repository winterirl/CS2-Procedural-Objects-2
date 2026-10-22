using System;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.Localization;
using PO2.Persistence.Model;

namespace PO2.Core.Operations
{
    internal enum TopologyRenderCaptureMode
    {
        Full,
        Mirror,
        Array,
        Restore
    }

    // Workers prepare data; these interfaces leave Unity resources on the main thread.
    internal interface ITopologyRenderCapture : IDisposable
    {
        bool Poll(CancellationToken token, long deadlineTimestamp = 0);
        Task Drain { get; }
        long CapturedBytes { get; }
        ITopologyRenderWork Finish();
    }

    internal interface ITopologyAppearanceCheck
    {
        void Validate(PersistencePartData before, int[] oldToNew, ITopologyRenderData candidate, CancellationToken token);
    }

    internal interface ITopologyRenderWork
    {
        ITopologyRenderData Build(MeshPart candidate, PersistencePartData before, int[] oldToNew, ITopologyAppearanceCheck appearance, CancellationToken token);
    }

    internal sealed class TopologyReplayProof
    {
        internal UnityEngine.Vector3 Scale;
        internal readonly System.Collections.Generic.Dictionary<int, UnityEngine.Vector2> RestoredTangents = new System.Collections.Generic.Dictionary<int, UnityEngine.Vector2>();
        internal long Bytes => 64 + 32L * RestoredTangents.Count;
    }

    internal interface ITopologyRenderData
    {
        TopologyReplayProof ReplayProof { get; }
        long PreparedBytes { get; }
    }

    internal interface ITopologyPublication : IDisposable
    {
        bool Poll(CancellationToken token);
        void Adopt();
        void Rollback();
        void Commit();
    }

    internal sealed class TopologyPreparedResult : IDisposable
    {
        internal MeshPart Part;
        internal ReversibleTopologyDelta Delta;
        internal MirrorDelta MirrorDelta;
        internal TopologySelection BeforeSelection, AfterSelection;
        internal TopologyPreparedSelection PreparedSelection;
        internal int[] OldToNew;
        internal ITopologyAppearanceCheck Appearance;
        internal ITopologyRenderData Render;
        internal bool RestorePivot;
        internal TopologyRenderCaptureMode RenderCaptureMode;

        public void Dispose()
        {
            Delta?.Dispose();
            Delta = null;
            MirrorDelta?.Dispose();
            MirrorDelta = null;
            BeforeSelection = null;
            AfterSelection = null;
            PreparedSelection = null;
            Part = null;
            OldToNew = null;
            Appearance = null;
            (Render as IDisposable)?.Dispose();
            Render = null;
        }
    }

    internal sealed class TopologyPreparation : IDisposable
    {
        private readonly ProceduralObject m_Object;
        private readonly int m_Index;
        private readonly TopologyTargetStamp m_Stamp;
        private readonly ProceduralObjectSystem m_System;
        private readonly TopologyReplayProof m_ReplayProof;
        private readonly TopologySize m_Size;
        private readonly CancellationTokenSource m_Cancellation;
        private Func<PersistencePartData, CancellationToken, TopologyPreparedResult> m_Build;
        private TopologySnapshotCapture m_Capture;
        private ITopologyRenderCapture m_RenderCapture;
        private PersistencePartData m_Source;
        private Task<TopologyPreparedResult> m_Work;
        private Task<ITopologyRenderData> m_RenderTask;
        private ITopologyPublication m_Publication;
        private bool m_Disposed;
        internal TopologyPreparedResult Result { get; private set; }
        internal Task Drain { get; private set; } = Task.CompletedTask;
        internal bool IsReady { get; private set; }
        internal bool CanContinue { get; private set; }
        private int m_Phase;
        private void SetPhase(int phase) => m_Phase = phase;
        internal LocalizedText Stage => m_Phase == 0 ? LocalizedText.For("PO2.Edit.Topology.Stage.Capturing") : m_Phase == 1 ? LocalizedText.For("PO2.Edit.Topology.Stage.Preparing") : LocalizedText.For("PO2.Edit.Topology.Stage.Renderer");

        internal TopologyPreparation(ProceduralObject obj, int index, TopologyTargetStamp stamp, PersistencePartData source, ProceduralObjectSystem system, CancellationToken token, Func<PersistencePartData, CancellationToken, TopologyPreparedResult> build, TopologyReplayProof replayProof = null, TopologySize size = null)
        {
            m_Object = obj;
            m_Index = index;
            m_Stamp = stamp;
            m_Source = source;
            m_System = system;
            m_Build = build;
            m_ReplayProof = replayProof;
            m_Size = size ?? TopologySize.ForPart(obj.Parts[index]);
            m_Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            try
            {
                if (source == null)
                {
                    m_Capture = new TopologySnapshotCapture(obj, index);
                    m_Capture.Start(m_Cancellation.Token);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal bool Poll(long deadlineTimestamp = 0)
        {
            CanContinue = false;
            var token = m_Cancellation.Token;
            if (token.IsCancellationRequested && (m_Work != null && !m_Work.IsCompleted || m_RenderTask != null && !m_RenderTask.IsCompleted || m_Capture != null && !m_Capture.Copy.Allocation.IsCompleted || m_RenderCapture != null && !m_RenderCapture.Drain.IsCompleted)) return false;
            token.ThrowIfCancellationRequested();
            if (!m_Stamp.Matches(m_Object, m_Index) || m_Object.IsLocked || !m_Object.CanEditGeometry(m_Index, out _))
                throw new InvalidOperationException("The mesh changed while preparing topology.");
            if (Result == null)
            {
                if (m_Work == null)
                {
                    if (m_Capture != null)
                    {
                        long copied = m_Capture.Copy.CopiedBytes;
                        if (!m_Capture.Poll(token, deadlineTimestamp))
                        {
                            CanContinue = m_Capture.Copy.CopiedBytes != copied;
                            return false;
                        }

                        m_Source = m_Capture.Finish();
                        m_Capture = null;
                    }

                    var source = m_Source;
                    var build = m_Build;
                    m_Build = null;
                    SetPhase(1);
                    m_Work = Task.Run(() => build(source, token), token);
                    return false;
                }

                if (!m_Work.IsCompleted) return false;
                Result = m_Work.GetAwaiter().GetResult();
                // The completed task/closure can keep the whole before-mesh alive.
                m_Work = null;
                m_Size.ValidateCandidate(Result.Part);
                SetPhase(2);
                m_RenderCapture = m_System.CaptureTopologyRenderer(m_Object, m_Index, Result.Part, m_ReplayProof, Result.RenderCaptureMode);
            }

            if (Result.Render == null)
            {
                if (m_RenderTask == null)
                {
                    if (!m_RenderCapture.Poll(token, deadlineTimestamp))
                    {
                        CanContinue = m_RenderCapture.Drain.IsCompleted;
                        return false;
                    }

                    var renderWork = m_RenderCapture.Finish();
                    m_RenderCapture.Dispose();
                    m_RenderCapture = null;
                    var result = Result;
                    var source = m_Source;
                    SetPhase(3);
                    m_RenderTask = Task.Run(() =>
                    {
                        var render = renderWork.Build(result.Part, source, result.OldToNew, result.Appearance, token);
                        return render;
                    }, token);
                    return false;
                }

                if (!m_RenderTask.IsCompleted) return false;
                Result.Render = m_RenderTask.GetAwaiter().GetResult();
                m_RenderTask = null;
                m_Source = null;
                // Mapping/appearance scratch has no readers after renderer work completes.
                Result.OldToNew = null;
                Result.Appearance = null;
            }

            if (m_Publication == null)
            {
                SetPhase(4);
                m_Publication = m_System.StageTopologyRenderer(m_Object, m_Index, Result.Part, Result.Render);
            }

            IsReady = m_Publication.Poll(token);
            CanContinue = !IsReady;
            if (IsReady) SetPhase(5);
            return IsReady;
        }

        internal bool Publish(Func<bool> record) => PublishAll(new[] { this }, record);

        // Validate and retain every old part before adopting any replacement.
        // A multipart edit can (should) never leave a partially mirrored object behind.
        internal static bool PublishAll(TopologyPreparation[] parts, Func<bool> record)
        {
            var rollback = new MeshPart[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                if (p == null || !p.IsReady || !p.m_Stamp.Matches(p.m_Object, p.m_Index) || p.m_Object.IsLocked || !p.m_Object.CanEditGeometry(p.m_Index, out _))
                    throw new InvalidOperationException("The topology target changed before publication.");
                p.m_Cancellation.Token.ThrowIfCancellationRequested();
                rollback[i] = p.m_Object.Parts[p.m_Index].CaptureTopologyReference();
            }

            int adopted = 0;
            try
            {
                foreach (var p in parts)
                {
                    var live = p.m_Object.Parts[p.m_Index];
                    adopted++;
                    live.ApplyHistoryTopology(p.Result.Part);
                    if (p.Result.RestorePivot) live.SetLocalPivot(p.Result.Part.LocalPivot);
                    p.m_Publication.Adopt();
                    p.m_Object.MarkDirty(ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
                }

                if (!record()) return false;
                adopted = 0;
                foreach (var p in parts)
                {
                    try
                    {
                        p.m_Publication.Commit();
                    }
                    catch
                    {
                        // Release failures cannot reverse committed history.
                    }
                }

                return true;
            }
            finally
            {
                for (int i = adopted - 1; i >= 0; i--)
                {
                    var p = parts[i];
                    var live = p.m_Object.Parts[p.m_Index];
                    live.ApplyHistoryTopology(rollback[i]);
                    if (p.Result.RestorePivot) live.SetLocalPivot(rollback[i].LocalPivot);
                    p.m_Publication.Rollback();
                    p.m_Object.MarkDirty(ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
                }
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Cancellation.Cancel();
            var rendererAllocation = m_RenderCapture?.Drain ?? Task.CompletedTask;
            m_RenderCapture?.Dispose();
            m_RenderCapture = null;
            m_Publication?.Dispose();
            m_Publication = null;
            var allocation = m_Capture?.Copy.Allocation ?? Task.CompletedTask;
            m_Capture = null;
            m_Source = null;
            m_Build = null;
            var work = m_Work;
            var renderTask = m_RenderTask;
            var result = Result;
            m_Work = null;
            m_RenderTask = null;
            Result = null;
            Drain = Task.WhenAll(allocation, rendererAllocation, (Task)work ?? Task.CompletedTask, (Task)renderTask ?? Task.CompletedTask).ContinueWith(t =>
            {
                var ignored = t.Exception;
                if (renderTask != null && renderTask.Status == TaskStatus.RanToCompletion && !ReferenceEquals(result?.Render, renderTask.Result)) (renderTask.Result as IDisposable)?.Dispose();
                if (result != null) result.Dispose();
                else if (work != null && work.Status == TaskStatus.RanToCompletion) work.Result.Dispose();
                m_Cancellation.Dispose();
            }, TaskScheduler.Default);
        }
    }
}