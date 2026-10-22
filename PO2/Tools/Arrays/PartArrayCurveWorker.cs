using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using PO2.Localization;

namespace PO2.Tools.Arrays
{
    internal sealed class ArrayCurveRequest
    {
        internal readonly int Revision, Copies, Capacity;
        internal readonly PartArrayBezier Definition;
        internal readonly PartArraySource Source;
        internal readonly PartArrayAlignment Alignment;
        internal readonly PartArrayMode Mode;
        internal readonly Quaternion Rotation;
        internal readonly double Gap;

        internal ArrayCurveRequest(int revision, PartArrayBezier definition, PartArraySource source, PartArrayAlignment alignment, PartArrayMode mode, Quaternion rotation, double gap, int copies, int capacity)
        {
            Revision = revision;
            Definition = definition;
            Source = source;
            Alignment = alignment;
            Mode = mode;
            Rotation = rotation;
            Gap = gap;
            Copies = copies;
            Capacity = capacity;
        }

        internal bool SamePath(ArrayCurveRequest other) => other != null && ReferenceEquals(Source, other.Source) && Definition.Start.Equals(other.Definition.Start) && Definition.Control1.Equals(other.Definition.Control1) && Definition.Control2.Equals(other.Definition.Control2) && Definition.End.Equals(other.Definition.End);
        internal bool SameFit(ArrayCurveRequest other) => SamePath(other) && Alignment == other.Alignment && Mode == other.Mode && Gap == other.Gap && Copies == other.Copies && Capacity == other.Capacity;
    }

    internal sealed class ArrayCurveResult
    {
        internal readonly ArrayCurveRequest Request;
        internal readonly PartArrayCurve Curve;
        internal readonly PartArrayPlacements Placements;
        internal readonly LocalizedText Reason;
        internal readonly bool Cancelled;

        internal ArrayCurveResult(ArrayCurveRequest request, PartArrayCurve curve, PartArrayPlacements placements, LocalizedText reason, bool cancelled = false)
        {
            Request = request;
            Curve = curve;
            Placements = placements;
            Reason = reason;
            Cancelled = cancelled;
        }
    }

    // Main-thread mailbox, one worker and one replaceable pending request. Tasks
    // only receive immutable math snapshots, never the tool or a scene object.
    internal sealed class PartArrayCurveWorker : IDisposable
    {
        private Task<ArrayCurveResult> m_Task;
        private CancellationTokenSource m_Cancellation;
        private ArrayCurveRequest m_Pending;
        private int m_Generation, m_RunningGeneration;
        private ArrayCurveResult m_Cached;
        private bool m_Disposed;

        internal void Request(ArrayCurveRequest request)
        {
            if (m_Disposed) return;
            m_Pending = request;
            // Finish the current frame while keeping only the newest next frame.
            // Cancelling on every mouse move prevents any preview during a drag.
        }

        internal void Clear()
        {
            m_Pending = null;
            m_Generation++;
            m_Cancellation?.Cancel();
        }

        internal bool Poll(out ArrayCurveResult result)
        {
            result = null;
            if (m_Disposed) return false;
            if (m_Task != null && m_Task.IsCompleted)
            {
                var completed = m_Task.GetAwaiter().GetResult();
                m_Task = null;
                m_Cancellation.Dispose();
                m_Cancellation = null;
                // Even a cancelled layout can leave a useful, complete path cache.
                if (completed.Curve != null) m_Cached = completed;
                if (!completed.Cancelled && m_RunningGeneration == m_Generation) result = completed;
            }

            if (m_Task == null && m_Pending != null)
            {
                var request = m_Pending;
                var cached = m_Cached;
                m_Pending = null;
                m_RunningGeneration = m_Generation;
                m_Cancellation = new CancellationTokenSource();
                var token = m_Cancellation.Token;
                m_Task = Task.Run(() => Build(request, cached, token));
            }

            return result != null;
        }

        private static ArrayCurveResult Build(ArrayCurveRequest request, ArrayCurveResult cached, CancellationToken token)
        {
            PartArrayCurve curve = null;
            try
            {
                token.ThrowIfCancellationRequested();
                if (request.SamePath(cached?.Request)) curve = cached.Curve;
                if (curve == null && !PartArrayCurve.TryCreate(request.Definition, request.Source.ReferenceFrame * Vector3.up, token, out curve, out LocalizedText pathReason, referenceForward: request.Source.Forward))
                    return new ArrayCurveResult(request, null, null, pathReason);
                PartArrayPlacements placements;
                LocalizedText reason = LocalizedText.Empty;
                if (cached?.Placements?.CanReuseRotation == true && request.SameFit(cached.Request))
                    placements = cached.Placements.WithRotationOffset(request.Rotation, request.Revision);
                else PartArrayCurveLayout.TryCreate(curve, request.Source, request.Alignment, request.Rotation, request.Gap, request.Mode, request.Copies, request.Capacity, request.Revision, token, out placements, out reason);
                token.ThrowIfCancellationRequested();
                return new ArrayCurveResult(request, curve, placements, reason);
            }
            catch (OperationCanceledException)
            {
                return new ArrayCurveResult(request, curve, null, LocalizedText.Empty, true);
            }
            catch (Exception e)
            {
                return new ArrayCurveResult(request, curve, null, LocalizedText.For("PO2.PartArray.Curve.Error.Preview", ("reason", LocalizedText.FromException(e))));
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Pending = null;
            m_Cached = null;
            if (m_Task != null)
            {
                var cancellation = m_Cancellation;
                cancellation.Cancel();
                // No wait during Cancel, tool shutdown or scene changes.
                m_Task.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
            }
            else m_Cancellation?.Dispose();

            m_Task = null;
            m_Cancellation = null;
        }
    }
}