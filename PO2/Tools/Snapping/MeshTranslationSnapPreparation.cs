using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;

namespace PO2.Tools.Snapping
{
    internal sealed class MeshTranslationSnapStamp
    {
        private readonly ProceduralObject m_Object;
        private readonly Guid m_Id;
        private readonly uint m_PartVersion;
        private uint m_GeometryVersion, m_DeformationVersion;
        private readonly int m_SelectionVersion, m_ActivePart, m_VertexCount, m_EdgeCount;
        private readonly MeshSelectionMode m_Mode;
        private readonly MeshEditVertexScope m_Scope;
        private readonly ObjectTransform m_Transform;
        private readonly Part[] m_Parts;

        private struct Part
        {
            internal MeshPart Identity;
            internal MeshData Mesh;
            internal MeshTopology Topology;
            internal StructuralTopology Structural;
            internal UnityEngine.Vector3[] Positions;
            internal uint GeometryVersion, TopologyVersion;
        }

        internal MeshTranslationSnapStamp(ProceduralObject obj, int version, MeshSelectionMode mode, MeshEditVertexScope scope, int part, int vertices, int edges)
        {
            m_Object = obj;
            m_Id = obj.PersistentId;
            m_PartVersion = obj.PartVersion;
            m_GeometryVersion = obj.GeometryVersion;
            m_DeformationVersion = obj.DeformationVersion;
            m_SelectionVersion = version;
            m_Mode = mode;
            m_Scope = scope;
            m_ActivePart = part;
            m_VertexCount = vertices;
            m_EdgeCount = edges;
            m_Transform = obj.Transform;
            m_Parts = new Part[obj.Parts.Count];
            for (int i = 0; i < m_Parts.Length; i++)
            {
                var p = obj.Parts[i];
                m_Parts[i] = new Part
                {
                    Identity = p, Mesh = p.Mesh, Topology = p.Topology,
                    Structural = p.StructuralTopology, Positions = p.Mesh.Vertices,
                    GeometryVersion = p.GeometryVersion, TopologyVersion = p.TopologyVersion
                };
            }
        }

        internal bool Matches(ProceduralObject obj, int version, MeshSelectionMode mode, MeshEditVertexScope scope, int part, int vertices, int edges, bool geometry = true)
        {
            if (!ReferenceEquals(obj, m_Object) || obj.PersistentId != m_Id || obj.IsLocked || obj.PartVersion != m_PartVersion || (geometry && (obj.GeometryVersion != m_GeometryVersion || obj.DeformationVersion != m_DeformationVersion)) || version != m_SelectionVersion || mode != m_Mode || scope != m_Scope || part != m_ActivePart || vertices != m_VertexCount || edges != m_EdgeCount || obj.Parts.Count != m_Parts.Length || (geometry && !SameTransform(obj.Transform))) return false;
            for (int i = 0; i < m_Parts.Length; i++)
            {
                var saved = m_Parts[i];
                var live = obj.Parts[i];
                if (!ReferenceEquals(saved.Identity, live) || !ReferenceEquals(saved.Mesh, live.Mesh) || !ReferenceEquals(saved.Topology, live.Topology) || !ReferenceEquals(saved.Structural, live.StructuralTopology) || !ReferenceEquals(saved.Positions, live.Mesh.Vertices) || (geometry && saved.GeometryVersion != live.GeometryVersion) || saved.TopologyVersion != live.TopologyVersion) return false;
            }

            return true;
        }

        internal bool Current(ProceduralObject obj, bool geometry = true) => Matches(obj, m_SelectionVersion, m_Mode, m_Scope, m_ActivePart, m_VertexCount, m_EdgeCount, geometry);

        internal bool SameTransform(ObjectTransform transform) =>
            transform.Position.Equals(m_Transform.Position) && transform.Scale.Equals(m_Transform.Scale) && transform.Rotation.Equals(m_Transform.Rotation);

        internal bool AcceptOwnGeometry(ProceduralObject obj)
        {
            if (!Current(obj, false) || !SameTransform(obj.Transform)) return false;
            m_GeometryVersion = obj.GeometryVersion;
            m_DeformationVersion = obj.DeformationVersion;
            for (int i = 0; i < m_Parts.Length; i++) m_Parts[i].GeometryVersion = obj.Parts[i].GeometryVersion;
            return true;
        }
    }

    // One worker, one current request. All owner methods run on the tool thread;
    // tasks retain only detached inputs and their own budget reservation.
    internal sealed class MeshTranslationSnapPreparation : IDisposable
    {
        private sealed class Work
        {
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal MeshTranslationSnapCapture Capture;
            internal Task<TranslationSnapSourceData> Build;
            internal MeshTranslationSnapBudget.Reservation Reservation;
            internal TranslationSnapSourceData Reuse;
            internal bool Final;
            internal Task Completion => (Task)Build ?? Capture?.Copy.Allocation ?? Task.CompletedTask;

            internal void Cancel()
            {
                Cancellation.Cancel();
                Capture?.Dispose();
            }

            internal TranslationSnapSourceData Take()
            {
                var result = Build.GetAwaiter().GetResult();
                Reservation = null;
                Release();
                return result;
            }

            internal void Release()
            {
                if (Completion.IsFaulted)
                {
                    var observed = Completion.Exception;
                }

                // A result owns the reservation once successfully returned.
                if (Reservation != null && Build != null && Build.Status == TaskStatus.RanToCompletion) Build.Result.Release();
                else Reservation?.Dispose();
                Reservation = null;
                Reuse?.Release();
                Reuse = null;
                Capture?.Dispose();
                Cancellation.Dispose();
            }
        }

        private Work m_Work;
        private TranslationSnapSourceData m_Ready, m_Reuse;
        private MeshTranslationSnapVertex[] m_Frozen;
        private MeshTranslationSnapBudget.Reservation m_Reservation;
        private bool m_Sealed, m_FinalPending, m_Disposed;
        private UnityEngine.Vector3 m_FinalDelta;
        private int m_StableUpdates;
        internal readonly MeshTranslationSnapBudget Budget = new MeshTranslationSnapBudget();
        internal MeshTranslationSnapStamp Stamp { get; private set; }
        internal TranslationSnapSourceData Ready => m_FinalPending ? null : m_Ready;
        internal bool ActiveGesture { get; private set; }
        internal bool HasFailed { get; private set; }
        internal string Failure { get; private set; } = string.Empty;
        internal long EstimatedBytes { get; private set; }
        internal long EstimatedResidentBytes => Budget.Bytes;
        internal bool Draining => m_Work != null && m_Work.Cancellation.IsCancellationRequested;
        internal bool UsesFrozenInput => m_Frozen != null || m_Work?.Build != null || Ready != null;

        internal bool Matches(ProceduralObject obj, int version, MeshSelectionMode mode, MeshEditVertexScope scope, int part, int vertices, int edges) =>
            Stamp != null && Stamp.Matches(obj, version, mode, scope, part, vertices, edges);

        private void SetContext(ProceduralObject obj, int version, MeshSelectionMode mode, MeshEditVertexScope scope, int part, int vertices, int edges)
        {
            if (Matches(obj, version, mode, scope, part, vertices, edges)) return;
            bool layout = Stamp != null && Stamp.Matches(obj, version, mode, scope, part, vertices, edges, false);
            var reuse = layout ? m_Ready ?? m_Reuse : null;
            reuse?.Retain();
            Invalidate();
            m_Reuse = reuse;
            Stamp = new MeshTranslationSnapStamp(obj, version, mode, scope, part, vertices, edges);
        }

        // Called before the existing mandatory selected-vertex capture. The
        // returned array is filled in that traversal and sealed before movement.
        internal MeshTranslationSnapVertex[] BeginGesture(ProceduralObject obj, int version, MeshSelectionMode mode, MeshEditVertexScope scope, int part, int vertices, int edges)
        {
            SetContext(obj, version, mode, scope, part, vertices, edges);
            ActiveGesture = true;
            HasFailed = false;
            Failure = string.Empty;
            if (Ready != null) return null;
            if (!m_FinalPending && m_Work?.Build != null && !Draining) return null;
            if (m_Ready != null)
            {
                SetReuse(m_Ready);
                m_Ready.Release();
                m_Ready = null;
            }

            CancelWork();
            Drain();
            m_FinalPending = false;
            ReleaseRequest();
            try
            {
                EstimatedBytes = MeshTranslationSnapCapture.EstimateBytes(vertices, edges);
                m_Reservation = Budget.TryReserve((long)vertices * 24 + 65536);
                if (m_Reservation == null) throw new InvalidOperationException("Snapping memory is still in use. Movement remains available.");
                m_Frozen = new MeshTranslationSnapVertex[vertices];
                return m_Frozen;
            }
            catch (Exception error)
            {
                Fail(error.Message);
                return null;
            }
        }

        internal void SealGestureCapture(int written)
        {
            if (m_Frozen == null) return;
            if (written != m_Frozen.Length)
            {
                Fail("The snapping selection changed during capture.");
                return;
            }

            m_Sealed = true;
        }

        internal bool ValidateActive(ProceduralObject obj) => ActiveGesture && !HasFailed && Stamp != null && Stamp.Current(obj);

        internal void AcceptOwnGeometry(ProceduralObject obj)
        {
            if (ActiveGesture && Stamp != null && !Stamp.AcceptOwnGeometry(obj)) Fail("The snapping geometry changed unexpectedly.");
        }

        internal void EndGesture(ProceduralObject obj, bool valid, UnityEngine.Vector3 finalDelta)
        {
            if (!ActiveGesture) return;
            ActiveGesture = false;
            if (!valid || Stamp == null || !Stamp.AcceptOwnGeometry(obj))
            {
                Invalidate();
                return;
            }

            if (!finalDelta.Equals(UnityEngine.Vector3.zero))
            {
                m_FinalPending = true;
                m_FinalDelta = finalDelta;
            }
        }

        internal void Refresh(ProceduralObject obj, int version, MeshSelectionMode mode, MeshEditVertexScope scope, int part, int vertexCount, IEnumerable<VertexRef> vertices, int edgeCount, IEnumerable<StructuralEdgeRef> edges, bool request = false, bool advance = true)
        {
            if (m_Disposed) return;
            Drain();
            if (obj == null || obj.IsLocked || vertexCount <= 0 || mode == MeshSelectionMode.SurfaceArea)
            {
                Invalidate();
                return;
            }

            if (ActiveGesture && !Matches(obj, version, mode, scope, part, vertexCount, edgeCount))
            {
                Fail("The snapping selection or geometry changed during the drag.");
                return;
            }

            SetContext(obj, version, mode, scope, part, vertexCount, edgeCount);
            if (HasFailed || !advance || Draining) return;
            try
            {
                if (m_Work != null)
                {
                    if (m_Work.Build == null)
                    {
                        bool complete = m_Work.Capture.Poll(() => Matches(obj, version, mode, scope, part, vertexCount, edgeCount), m_Work.Cancellation.Token);
                        if (!complete) return;
                        var input = m_Work.Capture.Detach();
                        m_Work.Build = BuildDetached(input, m_Work.Reuse, m_Work.Reservation, m_Work.Cancellation.Token, false, default);
                        return;
                    }

                    if (!m_Work.Build.IsCompleted) return;
                    var work = m_Work;
                    bool final = work.Final;
                    var result = work.Take();
                    m_Work = null;
                    m_Ready?.Release();
                    m_Ready = result;
                    m_Reuse?.Release();
                    m_Reuse = null;
                    if (final) m_FinalPending = false;
                }

                if (m_FinalPending && m_Ready != null)
                {
                    var reservation = Budget.TryReserve(MeshTranslationSnapCapture.EstimateBytes(vertexCount, edgeCount));
                    if (reservation == null) return; // Do not shift this work onto release.
                    m_Ready.Retain();
                    m_Work = new Work { Reservation = reservation, Reuse = m_Ready, Final = true };
                    m_Work.Build = BuildDetached(m_Ready.Input, m_Ready, reservation, m_Work.Cancellation.Token, true, m_FinalDelta);
                    return;
                }

                if (m_Ready != null || m_Work != null) return;
                if (ActiveGesture && (m_Frozen == null || !m_Sealed)) return;
                if (!ActiveGesture && !request && ++m_StableUpdates < 2) return;
                EstimatedBytes = MeshTranslationSnapCapture.EstimateBytes(vertexCount, edgeCount);
                if (m_Reservation == null) m_Reservation = Budget.TryReserve(EstimatedBytes);
                if (m_Reservation == null || !m_Reservation.TryGrow(EstimatedBytes)) return;
                var capture = new MeshTranslationSnapCapture(obj, vertexCount, vertices, edgeCount, edges, m_Frozen, m_Reuse?.Input?.Layout);
                m_Reuse?.Retain();
                m_Work = new Work { Capture = capture, Reservation = m_Reservation, Reuse = m_Reuse };
                m_Reservation = null;
                m_Frozen = null;
                m_Sealed = false;
                capture.Start(m_Work.Cancellation.Token);
            }
            catch (Exception error)
            {
                Fail(error.Message);
            }
        }

        private static Task<TranslationSnapSourceData> BuildDetached(MeshTranslationSnapCapture.Input input, TranslationSnapSourceData reuse, MeshTranslationSnapBudget.Reservation reservation, CancellationToken token, bool translate, UnityEngine.Vector3 delta) => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var source = translate ? input.Translate(delta, token) : input;
            return source.Build(token, reuse, reservation);
        }, token);

        private void SetReuse(TranslationSnapSourceData data)
        {
            data?.Retain();
            m_Reuse?.Release();
            m_Reuse = data;
        }

        private void ReleaseRequest()
        {
            m_Frozen = null;
            m_Sealed = false;
            m_Reservation?.Dispose();
            m_Reservation = null;
        }

        private void CancelWork()
        {
            m_Work?.Cancel();
        }

        internal void Drain()
        {
            if (Draining && m_Work.Completion.IsCompleted)
            {
                m_Work.Release();
                m_Work = null;
            }
        }

        internal void Suspend()
        {
            if (!ActiveGesture)
            {
                CancelWork();
                Drain();
            }
        }

        internal void Fail(string reason)
        {
            CancelWork();
            Drain();
            ReleaseRequest();
            m_Ready?.Release();
            m_Ready = null;
            m_Reuse?.Release();
            m_Reuse = null;
            m_FinalPending = false;
            HasFailed = true;
            Failure = reason ?? string.Empty;
        }

        internal void Invalidate()
        {
            CancelWork();
            Drain();
            ReleaseRequest();
            m_Ready?.Release();
            m_Ready = null;
            m_Reuse?.Release();
            m_Reuse = null;
            Stamp = null;
            ActiveGesture = false;
            m_FinalPending = false;
            m_StableUpdates = 0;
            HasFailed = false;
            Failure = string.Empty;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            Invalidate();
            var work = m_Work;
            m_Work = null;
            if (work != null) work.Completion.ContinueWith(_ => work.Release(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }
}