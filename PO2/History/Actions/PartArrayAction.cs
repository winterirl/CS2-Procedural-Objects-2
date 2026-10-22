using System;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.History;
using PO2.Localization;
using PO2.Materials;

namespace PO2.History.Actions
{
    // Undo holds the removed copies; redo puts them back in the live PO.
    // A rotated source swaps exact geometry states to avoid accumulating rotation error.
    internal sealed class PartArrayAction : IHistoryAction, IHistoryBackgroundWork, IHistoryOwnerCleanup
    {
        private readonly HistoryPartKey m_Source;
        private readonly int m_First;
        private readonly UnityEngine.Matrix4x4 m_BakeTransform;
        private readonly UnityEngine.Matrix4x4[] m_CopyTransforms;
        private readonly Guid[] m_Ids;
        private readonly HistoryContext m_Context;
        private readonly HistoryFocusState m_Focus;
        private MeshPart[] m_Detached;
        private MeshPart m_InactiveSource;
        private IArrayRemoval m_RecentRemoval;
        private PartArrayPreparation m_Preparation, m_RecentUndo;
        private HistoryDirection m_Direction;
        private TaskCompletionSource<HistoryResult> m_Completion;
        private CancellationToken m_Token;
        private bool m_Present = true, m_Disposed;
        private readonly long m_GeometryBytes, m_SourceBytes;
        public LocalizedText Description => LocalizedText.For("PO2.History.Action.Part.Array");
        public bool HasChanges => true;
        public HistoryFootprint Footprint => new HistoryFootprint(256L + m_Ids.Length * 16L + 64L * (m_CopyTransforms?.Length ?? 0) + m_SourceBytes + (m_Detached == null ? 0 : m_GeometryBytes), 0, 0, m_GeometryBytes + m_SourceBytes);

        internal PartArrayAction(HistoryPartKey source, int first, MeshPart[] parts, HistoryContext context, HistoryFocusState focus, UnityEngine.Matrix4x4? bakeTransform = null, MeshPart sourceBefore = null, UnityEngine.Matrix4x4[] copyTransforms = null)
        {
            m_BakeTransform = bakeTransform ?? UnityEngine.Matrix4x4.identity;
            m_CopyTransforms = copyTransforms;
            m_InactiveSource = sourceBefore;
            m_Source = source;
            m_First = first;
            m_Context = context;
            m_Focus = focus;
            m_Ids = new Guid[parts.Length];
            var seeds = new System.Collections.Generic.HashSet<GeneratedMappingSeed>();
            for (int i = 0; i < parts.Length; i++)
            {
                m_Ids[i] = parts[i].PartHistoryId;
                foreach (var area in parts[i].SurfaceAreas)
                    if (area.MappingSeed != null && seeds.Add(area.MappingSeed))
                        m_GeometryBytes += area.MappingSeed.Bytes;
                m_GeometryBytes += MeshBytes(parts[i]);
            }

            if (sourceBefore != null)
            {
                m_SourceBytes = MeshBytes(sourceBefore);
                foreach (var area in sourceBefore.SurfaceAreas)
                    if (area.MappingSeed != null && seeds.Add(area.MappingSeed))
                        m_SourceBytes += area.MappingSeed.Bytes;
            }
        }

        private static long MeshBytes(MeshPart part)
        {
            var mesh = part.Mesh;
            return 12L * mesh.Vertices.Length + 12L * (mesh.Normals?.Length ?? 0) + 16L * ((mesh.Tangents?.Length ?? 0) + (long)(mesh.UV0?.Length ?? 0) + (mesh.TexCoord1?.Length ?? 0) + (long)(mesh.TexCoord3?.Length ?? 0)) + 4L * ((mesh.VertexColors?.Length ?? 0) + (long)mesh.Triangles.Length);
        }

        public Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken token)
        {
            m_Direction = direction;
            if (direction == HistoryDirection.Undo && m_InactiveSource == null) return Task.FromResult(HistoryResult.Ok);
            m_Token = token;
            m_Completion = new TaskCompletionSource<HistoryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            return m_Completion.Task;
        }

        public void PollBackgroundWork(Action<string> log)
        {
            if (m_Disposed) return;
            // Keep the renderer available until this replay can no longer roll back.
            m_RecentRemoval?.Dispose();
            m_RecentRemoval = null;
            m_RecentUndo?.Dispose();
            m_RecentUndo = null;
            if (m_Completion == null || m_Completion.Task.IsCompleted) return;
            try
            {
                m_Token.ThrowIfCancellationRequested();
                if (m_Preparation == null)
                {
                    bool undo = m_Direction == HistoryDirection.Undo;
                    if ((!undo && m_Detached == null) || !m_Context.TryResolve(m_Source, out _, out int sourceIndex) || !m_Context.TryResolve(m_Source.Owner, out var obj) || obj.Parts.Count != m_First + (undo ? m_Ids.Length : 0))
                        throw new InvalidOperationException("The array source or part order changed before replay.");
                    m_Preparation = m_Context.PrepareArrayReplay(obj, sourceIndex, undo ? Array.Empty<MeshPart>() : m_Detached, m_Token, undo ? m_BakeTransform.inverse : m_BakeTransform, m_InactiveSource, undo ? null : m_CopyTransforms);
                }

                if (m_Preparation.Poll()) m_Completion.TrySetResult(HistoryResult.Ok);
            }
            catch (Exception e)
            {
                m_Preparation?.Dispose();
                m_Preparation = null;
                m_Completion.TrySetResult(new HistoryResult(false, e is OperationCanceledException ? "Array replay cancelled." : e.Message));
            }
        }

        public HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            bool undo = direction == HistoryDirection.Undo;
            if (undo != m_Present) return new HistoryResult(false, "Array history presence changed.", true);
            if (undo)
            {
                MeshPart[] parts = null;
                IArrayRemoval removal = null;
                LocalizedText reason = LocalizedText.Empty;
                bool Detach() => context.TryDetachArray(m_Source.Owner, m_First, m_Ids, out parts, out removal, out reason);
                if (m_InactiveSource != null)
                {
                    if (m_Preparation == null) return new HistoryResult(false, "Array undo is not prepared.", true);
                    try
                    {
                        if (!m_Preparation.Publish(Detach, out reason, true)) return new HistoryResult(false, reason, true);
                        m_InactiveSource = m_Preparation.SourceBefore;
                        m_RecentUndo = m_Preparation;
                        m_Preparation = null;
                    }
                    finally
                    {
                        m_Preparation?.Dispose();
                        m_Preparation = null;
                        m_Completion = null;
                    }
                }
                else if (!Detach()) return new HistoryResult(false, reason, true);

                m_Detached = parts;
                m_RecentRemoval = removal;
                m_Present = false;
            }
            else
            {
                if (m_RecentRemoval != null)
                {
                    if (!context.TryResolve(m_Source.Owner, out var obj) || !obj.TryAppendHistoryParts(m_Detached, m_First, out _))
                        return new HistoryResult(false, "Could not roll back array removal.", true);
                    if (m_RecentUndo != null)
                    {
                        context.TryResolve(m_Source, out var source, out _);
                        var previous = source.CaptureTopologyReference();
                        m_RecentUndo.RollbackSource();
                        m_RecentUndo.Dispose();
                        m_RecentUndo = null;
                        m_InactiveSource = previous;
                    }

                    m_RecentRemoval.Restore();
                    m_RecentRemoval.Dispose();
                    m_RecentRemoval = null;
                }
                else
                {
                    if (m_Preparation == null) return new HistoryResult(false, "Array redo is not prepared.", true);
                    try
                    {
                        if (!m_Preparation.Publish(() => true, out LocalizedText reason)) return new HistoryResult(false, reason);
                        m_InactiveSource = m_Preparation.SourceBefore;
                    }
                    finally
                    {
                        m_Preparation.Dispose();
                        m_Preparation = null;
                        m_Completion = null;
                    }
                }

                m_Detached = null;
                m_Present = true;
            }

            if (m_InactiveSource != null && context.TryResolve(m_Source.Owner, out var changed)) context.GeometryChanged(changed);
            context.RestoreFocus(m_Focus);
            return HistoryResult.Ok;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            CancelPreparation();
            m_RecentRemoval?.Dispose();
            m_RecentRemoval = null;
            m_Detached = null;
            m_InactiveSource = null;
        }

        public void CancelPreparation()
        {
            var preparation = m_Preparation;
            m_Preparation = null;
            preparation?.Dispose();
            m_RecentRemoval?.Dispose();
            m_RecentRemoval = null;
            m_RecentUndo?.Dispose();
            m_RecentUndo = null;
            var completion = m_Completion;
            m_Completion = null;
            if (completion != null) (preparation?.Drain ?? Task.CompletedTask).ContinueWith(_ => completion.TrySetResult(new HistoryResult(false, "Array history was cleared.")), TaskScheduler.Default);
        }
    }
}