using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.Localization;
using PO2.Persistence.Model;
using PO2.Tools.Arrays;

namespace PO2.Core.Operations
{
    // Capture the source once then build copies off-thread.
    // The bounded queue limits upload scratch while the main thread stages inactive renderers.
    // Only Publish exposes the complete batch to the model and history.
    internal interface IArrayRenderWork
    {
        void PrepareArray(MeshPart source, Matrix4x4 transform, CancellationToken token);
        ITopologyRenderData BuildArray(MeshPart part, PersistencePartData before, Matrix4x4 transform, CancellationToken token);
    }

    internal sealed class PartArrayPreparation : IDisposable
    {
        private sealed class PreparedPart
        {
            internal int Index;
            internal MeshPart Part;
            internal ITopologyRenderData Render;
        }

        private readonly ProceduralObjectSystem m_System;
        private readonly ProceduralObject m_Object;
        private readonly int m_SourceIndex, m_First;
        private readonly TopologyTargetStamp m_Stamp;
        private readonly ObjectTransform m_Transform;
        private readonly CancellationTokenSource m_Cancellation;
        private readonly PartArrayLayout m_Layout;
        private readonly PartArrayPlacements m_Placements;
        private readonly MeshPart[] m_Redo;
        private readonly bool m_ReplaceSource;
        private MeshPart m_Replacement;
        private ITopologyPublication m_SourcePublication;
        private bool m_ReversibleSource;
        private readonly string[] m_Names;
        private readonly ConcurrentQueue<PreparedPart> m_Queue = new ConcurrentQueue<PreparedPart>();
        private readonly SemaphoreSlim m_Slots = new SemaphoreSlim(2, 2);
        private TopologySnapshotCapture m_Capture;
        private ITopologyRenderCapture m_RenderCapture;
        private PersistencePartData m_Source;
        private Task m_Work;
        private readonly ITopologyPublication[] m_Publications;
        private ITopologyPublication m_Upload;
        private int m_Completed;
        private bool m_Disposed;
        internal readonly MeshPart[] Parts;
        internal MeshPart SourceBefore { get; }

        internal Matrix4x4 BakeTransform { get; }

        // Filled by the worker once then retained by history for renderer redo.
        // Published geometry is already baked and is never transformed again.
        internal Matrix4x4[] CopyTransforms { get; }
        internal int FirstIndex => m_First;
        private int Total => Parts.Length + (m_ReplaceSource ? 1 : 0);
        internal bool Ready => m_Completed == Total;
        internal Task Drain { get; private set; } = Task.CompletedTask;
        internal LocalizedText Status => m_Work == null ? LocalizedText.For("PO2.PartArray.Status.PreparingSource") : LocalizedText.For("PO2.PartArray.Status.PreparingParts", ("completed", m_Completed), ("total", Total));

        internal PartArrayPreparation(ProceduralObjectSystem system, ProceduralObject obj, int sourceIndex, PartArrayLayout layout, CancellationToken token, MeshPart[] redo = null, Matrix4x4? bakeTransform = null, MeshPart sourceReplacement = null, PartArrayPlacements placements = null, Matrix4x4[] copyTransforms = null)
        {
            m_System = system;
            m_Object = obj;
            m_SourceIndex = sourceIndex;
            m_First = obj.Parts.Count;
            m_Stamp = new TopologyTargetStamp(obj, sourceIndex);
            m_Transform = obj.Transform;
            m_Layout = layout;
            m_Redo = redo;
            m_Placements = placements;
            Vector3 pivot = obj.Parts[sourceIndex].LocalPivot;
            BakeTransform = bakeTransform ?? (placements != null ? placements.Source.LocalTransform(m_Transform, pivot) : redo == null ? layout.LocalTransform(m_Transform, pivot) : Matrix4x4.identity);
            m_ReplaceSource = sourceReplacement != null || redo == null && !BakeTransform.Equals(Matrix4x4.identity);
            m_Replacement = sourceReplacement;
            if (m_ReplaceSource) SourceBefore = obj.Parts[sourceIndex].CaptureTopologyReference();
            Parts = new MeshPart[redo == null ? placements?.Count ?? layout.Count : redo.Length];
            CopyTransforms = placements == null ? copyTransforms : new Matrix4x4[Parts.Length];
            if (CopyTransforms != null && CopyTransforms.Length != Parts.Length)
                throw LocalizedText.For("PO2.PartArray.Error.TransformMismatch").AsException(message => new ArgumentException(message));
            m_Publications = new ITopologyPublication[Parts.Length];
            if (Total == 0) throw new LocalizedTextException(LocalizedText.For("PO2.PartArray.Error.NoChanges"));
            m_Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            m_Names = new string[Parts.Length];
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in obj.Parts) names.Add(part.Name);
            string name = obj.Parts[sourceIndex].Name;
            int suffix = 1;
            for (int i = 0; i < m_Names.Length; i++)
            {
                string candidate;
                do
                {
                    candidate = name + " (" + suffix++ + ")";
                } while (!names.Add(candidate));

                m_Names[i] = candidate;
            }

            try
            {
                m_Capture = new TopologySnapshotCapture(obj, sourceIndex);
                m_Capture.Start(m_Cancellation.Token);
                m_RenderCapture = system.CaptureTopologyRenderer(obj, sourceIndex, obj.Parts[sourceIndex], mode: TopologyRenderCaptureMode.Array);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal bool Matches() => m_Stamp.Matches(m_Object, m_SourceIndex) && m_Object.Transform.Equals(m_Transform) && !m_Object.IsLocked && m_Object.CanEditGeometry(-1, out _) && m_System.TryGetObject(m_Object.Id, out var current) && ReferenceEquals(current, m_Object);

        internal bool Poll()
        {
            var token = m_Cancellation.Token;
            token.ThrowIfCancellationRequested();
            if (!Matches()) throw new LocalizedTextException(LocalizedText.For("PO2.PartArray.Error.SourceChanged"));
            long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 500;
            if (m_Work == null)
            {
                if (m_Source == null)
                {
                    if (!m_Capture.Poll(token, deadline)) return false;
                    m_Source = m_Capture.Finish();
                    m_Capture = null;
                }

                do
                {
                    if (m_RenderCapture.Poll(token, deadline))
                    {
                        var work = m_RenderCapture.Finish();
                        m_RenderCapture.Dispose();
                        m_RenderCapture = null;
                        var source = m_Source;
                        m_Source = null;
                        m_Work = Task.Run(() => Build(source, work, token), token);
                        break;
                    }

                    if (!m_RenderCapture.Drain.IsCompleted) return false;
                } while (Stopwatch.GetTimestamp() < deadline);

                return false;
            }

            if (m_Work.IsFaulted || m_Work.IsCanceled) m_Work.GetAwaiter().GetResult();
            do
            {
                if (m_Upload == null)
                {
                    if (!m_Queue.TryDequeue(out var prepared)) return Ready;
                    try
                    {
                        if (prepared.Index < 0)
                        {
                            m_Replacement = prepared.Part;
                            m_Upload = m_System.StageTopologyRenderer(m_Object, m_SourceIndex, prepared.Part, prepared.Render);
                            m_SourcePublication = m_Upload;
                        }
                        else
                        {
                            Parts[prepared.Index] = prepared.Part;
                            m_Upload = m_System.StageArrayPart(m_Object, m_First + prepared.Index, prepared.Part, prepared.Render, m_First + Parts.Length);
                            m_Publications[prepared.Index] = m_Upload;
                        }
                    }
                    catch
                    {
                        (prepared.Render as IDisposable)?.Dispose();
                        throw;
                    }
                    finally
                    {
                        m_Slots.Release();
                    }
                }

                if (m_Upload.Poll(token))
                {
                    m_Completed++;
                    m_Upload = null;
                }
            } while (!Ready && Stopwatch.GetTimestamp() < deadline);

            return Ready;
        }

        private void Build(PersistencePartData data, ITopologyRenderWork renderer, CancellationToken token)
        {
            var source = m_Redo == null ? data.RestoreOwned() : null;
            if (CopyTransforms != null)
            {
                BuildCurve(source, data, renderer, token);
                return;
            }

            (renderer as IArrayRenderWork)?.PrepareArray(source, BakeTransform, token);
            if (source != null && !BakeTransform.Equals(Matrix4x4.identity))
                source = source.CloneWithGeometry(source.Name, ArrayKernels.CopyTransformed(source.Mesh, BakeTransform, token), source.StructuralTopology.CloneFaces(), source.LocalPivot);
            if (m_ReplaceSource)
            {
                m_Slots.Wait(token);
                QueuePart(-1, m_Replacement ?? source, data, renderer, token);
            }

            for (int i = 0; i < Parts.Length; i++)
            {
                m_Slots.Wait(token);
                var part = m_Redo == null ? Clone(source, m_Layout.Offset(i), m_Names[i], token) : m_Redo[i];
                QueuePart(i, part, data, renderer, token);
            }
        }

        private void BuildCurve(MeshPart source, PersistencePartData data, ITopologyRenderWork renderer, CancellationToken token)
        {
            if (!(renderer is IArrayRenderWork arrayRenderer))
                throw new LocalizedTextException(LocalizedText.For("PO2.PartArray.Error.RendererUnsupported"));
            // Keep the capture in the original source frame.
            // Every part rotates from that frame directly so geometry and tangents dont drift.
            arrayRenderer.PrepareArray(source, Matrix4x4.identity, token);
            if (m_ReplaceSource)
            {
                m_Slots.Wait(token);
                var replacement = m_Replacement ?? source.CloneWithGeometry(source.Name, ArrayKernels.CopyTransformed(source.Mesh, BakeTransform, token), source.StructuralTopology.CloneFaces(), source.LocalPivot);
                QueuePart(-1, replacement, data, renderer, token, BakeTransform);
            }

            for (int i = 0; i < Parts.Length; i++)
            {
                m_Slots.Wait(token);
                MeshPart part;
                if (m_Redo != null) part = m_Redo[i];
                else
                {
                    var placement = m_Placements[i];
                    CopyTransforms[i] = placement.LocalTransform(m_Transform, source.LocalPivot);
                    var mesh = ArrayKernels.CopyTransformed(source.Mesh, CopyTransforms[i], token, copyPayloads: true);
                    var pivot = source.LocalPivot + m_Transform.InverseTransformVector(placement.Pivot - m_Placements.Source.Pivot);
                    token.ThrowIfCancellationRequested();
                    part = source.CloneWithGeometry(m_Names[i], mesh, source.StructuralTopology.CloneFaces(), pivot);
                }

                QueuePart(i, part, data, renderer, token, CopyTransforms[i]);
            }
        }

        private void QueuePart(int index, MeshPart part, PersistencePartData data, ITopologyRenderWork renderer, CancellationToken token, Matrix4x4? transform = null)
        {
            ITopologyRenderData render = null;
            try
            {
                token.ThrowIfCancellationRequested();
                render = transform.HasValue ? ((IArrayRenderWork)renderer).BuildArray(part, data, transform.Value, token) : renderer.Build(part, data, null, null, token);
                token.ThrowIfCancellationRequested();
                m_Queue.Enqueue(new PreparedPart { Index = index, Part = part, Render = render });
                render = null;
            }
            finally
            {
                (render as IDisposable)?.Dispose();
            }
        }

        private MeshPart Clone(MeshPart source, Vector3 worldOffset, string name, CancellationToken token)
        {
            Vector3 offset = m_Transform.InverseTransformVector(worldOffset);
            var mesh = source.Mesh;
            var copied = new MeshData(ArrayKernels.CopyTranslated(mesh.Vertices, offset, token), Copy(mesh.Normals), Copy(mesh.Tangents), Copy(mesh.UV0), Copy(mesh.VertexColors), Copy(mesh.TexCoord1), Copy(mesh.TexCoord3), Copy(mesh.Triangles));
            token.ThrowIfCancellationRequested();
            return source.CloneWithGeometry(name, copied, source.StructuralTopology.CloneFaces(), source.LocalPivot + offset);
        }

        private static T[] Copy<T>(T[] source) => source == null ? null : (T[])source.Clone();

        internal bool Publish(Func<bool> record, out LocalizedText reason, bool deferSourceCommit = false)
        {
            reason = LocalizedText.Empty;
            m_Cancellation.Token.ThrowIfCancellationRequested();
            if (!Ready || !Matches())
            {
                reason = LocalizedText.For("PO2.PartArray.Error.ChangedBeforeApply");
                return false;
            }

            if (Parts.Length > 0 && (!m_System.ValidateArrayIdentities(Parts, out reason) || !m_Object.TryAppendHistoryParts(Parts, m_First, out reason))) return false;
            int adopted = 0;
            bool committed = false;
            try
            {
                if (m_ReplaceSource)
                {
                    m_ReversibleSource = true;
                    SetSource(m_Replacement);
                    m_SourcePublication.Adopt();
                }

                for (int i = 0; i < m_Publications.Length; i++)
                {
                    adopted = i + 1;
                    m_Publications[i].Adopt();
                }

                if (!record())
                {
                    reason = LocalizedText.For("PO2.PartArray.Error.HistoryUnavailable");
                    return false;
                }

                committed = true;
                foreach (var publication in m_Publications)
                {
                    try
                    {
                        publication.Commit();
                    }
                    catch
                    {
                        // Cleanup cannot undo a recorded action.
                    }
                }

                if (!deferSourceCommit) CommitSource();
                return true;
            }
            finally
            {
                if (!committed)
                {
                    for (int i = adopted - 1; i >= 0; i--) m_Publications[i].Rollback();
                    RollbackSource();
                    if (Parts.Length > 0 && !m_Object.TryRollbackAddedParts(m_First, Parts)) throw new LocalizedTextException(LocalizedText.For("PO2.PartArray.Error.PartListRestore"));
                }
            }
        }

        private void SetSource(MeshPart part)
        {
            m_Object.Parts[m_SourceIndex].ApplyHistoryTopology(part);
            m_Object.MarkDirty(ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
        }

        // Undo keeps this swap reversible until the history replay finishes.
        internal void RollbackSource()
        {
            if (!m_ReversibleSource) return;
            SetSource(SourceBefore);
            m_SourcePublication.Rollback();
            m_ReversibleSource = false;
        }

        private void CommitSource()
        {
            if (!m_ReversibleSource) return;
            m_ReversibleSource = false;
            try
            {
                m_SourcePublication.Commit();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Cancellation.Cancel();
            CommitSource();
            var allocation = m_Capture?.Copy.Allocation ?? Task.CompletedTask;
            var renderAllocation = m_RenderCapture?.Drain ?? Task.CompletedTask;
            m_RenderCapture?.Dispose();
            m_RenderCapture = null;
            m_Capture = null;
            m_Source = null;
            for (int i = m_Publications.Length - 1; i >= 0; i--) m_Publications[i]?.Dispose();
            m_SourcePublication?.Dispose();
            Drain = Task.WhenAll(allocation, renderAllocation, m_Work ?? Task.CompletedTask).ContinueWith(task =>
            {
                var ignored = task.Exception;
                while (m_Queue.TryDequeue(out var part)) (part.Render as IDisposable)?.Dispose();
                m_Slots.Dispose();
                m_Cancellation.Dispose();
            }, TaskScheduler.Default);
        }
    }
}