using System;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    internal sealed class LifetimeAction : HistoryCaptureAction, IHistoryPayloadOwner, IHistoryBackgroundWork, IHistoryOwnerCleanup
    {
        private readonly HistoryObjectKey m_Owner;
        private readonly HistoryPartKey? m_PartKey;
        private readonly HistoryIdentityManifest m_Manifest;
        private readonly bool m_InitialPresent;
        private bool m_Present;
        private LifetimeStorage m_Payload;
        private HistoryContext m_Context;
        private readonly object m_PreparationGate = new object();
        private RestorePreparation m_Restore;
        private bool m_Disposed;

        private sealed class RestorePreparation
        {
            internal CancellationToken Token;
            internal Task<HistoryResult> Decode;
            internal IHistoryObjectRenderer Renderer;
            internal readonly TaskCompletionSource<HistoryResult> Completion = new TaskCompletionSource<HistoryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal bool IsCold => m_Payload != null && m_Payload.IsCold;

        internal LifetimeAction(ProceduralObject obj, bool present, LocalizedText description) : base(description)
        {
            m_Owner = new HistoryObjectKey(obj.Id, obj.PersistentId);
            m_Manifest = HistoryIdentityManifest.Capture(obj);
            m_InitialPresent = m_Present = present;
            m_Payload = new LifetimeStorage(new LifetimePayload(obj));
        }

        internal LifetimeAction(ProceduralObject owner, MeshPart part, int index, bool present, LocalizedText description) : base(description)
        {
            m_Owner = new HistoryObjectKey(owner.Id, owner.PersistentId);
            m_PartKey = new HistoryPartKey(m_Owner, part.PartHistoryId, index);
            m_InitialPresent = m_Present = present;
            m_Payload = new LifetimeStorage(new LifetimePayload(m_Owner, part));
        }

        public System.Collections.Generic.IEnumerable<HistoryResidentPayload> Payloads
        {
            get
            {
                if (m_Payload?.EncodedPayload != null) yield return m_Payload.EncodedPayload;
            }
        }

        public void CoolManagedPayload()
        {
            m_Payload?.RequestCooling();
        }

        public void PollBackgroundWork(Action<string> log)
        {
            m_Payload?.PollBackgroundWork(log);
            lock (m_PreparationGate)
            {
                var restore = m_Restore;
                if (restore == null || restore.Completion.Task.IsCompleted) return;
                try
                {
                    restore.Token.ThrowIfCancellationRequested();
                    if (!restore.Decode.IsCompleted) return;
                    var result = restore.Decode.GetAwaiter().GetResult();
                    if (!result.Success)
                    {
                        restore.Completion.TrySetResult(result);
                        return;
                    }

                    if (restore.Renderer == null) restore.Renderer = m_Context.PrepareObjectRenderer(m_Payload.GetObject());
                    if (restore.Renderer.Poll(restore.Token)) restore.Completion.TrySetResult(HistoryResult.Ok);
                }
                catch (Exception exception)
                {
                    restore.Renderer?.Dispose();
                    restore.Renderer = null;
                    restore.Completion.TrySetResult(new HistoryResult(false, exception.Message, true));
                }
            }
        }

        public override Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken cancellation)
        {
            lock (m_PreparationGate)
            {
                cancellation.ThrowIfCancellationRequested();
                if (m_Disposed) throw new ObjectDisposedException(nameof(LifetimeAction));
                var decode = m_Payload?.Prepare(cancellation) ?? Task.FromResult(HistoryResult.Ok);
                if (m_Present || m_PartKey.HasValue || m_Context == null) return decode;
                m_Restore = new RestorePreparation { Token = cancellation, Decode = decode };
                return m_Restore.Completion.Task;
            }
        }

        public override HistoryFootprint Footprint => new HistoryFootprint(192 + (m_Payload?.ManagedBytes ?? 0), m_Payload?.EncodedPayload?.Footprint.EncodedBytes ?? 0, m_Payload?.EncodedPayload?.Footprint.ColdBytes ?? 0, m_Payload?.EstimatedBytes ?? 192);
        private bool Exists(HistoryContext context) => m_PartKey.HasValue ? context.TryResolve(m_PartKey.Value, out _, out _) : context.TryResolve(m_Owner, out _);

        internal override void Seal(HistoryContext context)
        {
            m_Context = context;
            m_Present = Exists(context);
            HasChanges = m_Present != m_InitialPresent;
            if (m_Present)
            {
                m_Payload?.Dispose();
                m_Payload = null;
            }
            else if (HasChanges) m_Payload.RequestCoolingIfLarge();
        }

        internal override void RestoreCapture(HistoryContext context)
        {
            m_Present = Exists(context);
            if (m_Present == m_InitialPresent) return;
            var result = Swap(context, false);
            if (!result.Success) throw new InvalidOperationException(result.Reason);
        }

        internal void Cool()
        {
            m_Payload?.Cool();
        }

        public override HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            try
            {
                if (Exists(context) != m_Present) return new HistoryResult(false, "Lifetime target presence changed.", true);
                return Swap(context, true);
            }
            finally
            {
                m_Restore?.Renderer?.Dispose();
                m_Restore = null;
            }
        }

        private HistoryResult Swap(HistoryContext context, bool allowEncoding)
        {
            string reason;
            if (m_Present)
            {
                LifetimeStorage detached;
                if (m_PartKey.HasValue)
                {
                    var key = m_PartKey.Value;
                    if (!context.TryResolve(key, out var part, out int index) || index != key.IndexHint)
                        return new HistoryResult(false, "History part order changed.", true);
                    detached = new LifetimeStorage(new LifetimePayload(m_Owner, part));
                    if (allowEncoding) detached.RequestCoolingIfLarge();
                    if (!context.TryRemovePart(key, index, out _, out reason))
                    {
                        detached.Dispose();
                        return new HistoryResult(false, reason, true);
                    }
                }
                else
                {
                    if (!context.TryResolve(m_Owner, out var obj) || !m_Manifest.Matches(HistoryIdentityManifest.Capture(obj)))
                        return new HistoryResult(false, "History child part identities changed.", true);
                    detached = new LifetimeStorage(new LifetimePayload(obj));
                    if (allowEncoding) detached.RequestCoolingIfLarge();
                    if (!context.TryRemove(m_Owner, out _, out reason))
                    {
                        detached.Dispose();
                        return new HistoryResult(false, reason, true);
                    }
                }

                m_Payload?.Dispose();
                m_Payload = detached;
            }
            else
            {
                if (m_Payload == null) return new HistoryResult(false, "Missing detached lifetime payload.", true);
                bool restored;
                if (m_PartKey.HasValue)
                {
                    var key = m_PartKey.Value;
                    restored = context.TryInsertPart(key, key.IndexHint, m_Payload.GetPart(), out reason);
                }
                else restored = context.TryRegister(m_Owner, m_Payload.GetObject(), out reason, m_Restore?.Renderer);

                if (!restored) return new HistoryResult(false, reason, true);
                m_Payload.Dispose();
                m_Payload = null;
            }

            m_Present = !m_Present;
            return HistoryResult.Ok;
        }

        public void CancelPreparation()
        {
            lock (m_PreparationGate)
            {
                var restore = m_Restore;
                m_Restore = null;
                if (restore == null) return;
                restore.Renderer?.Dispose();
                var drain = Task.WhenAll(restore.Decode, restore.Renderer?.Drain ?? Task.CompletedTask);
                restore.Renderer = null;
                drain.ContinueWith(completed =>
                {
                    var ignored = completed.Exception;
                    restore.Completion.TrySetCanceled();
                }, TaskScheduler.Default);
            }
        }

        public override void Dispose()
        {
            lock (m_PreparationGate)
            {
                if (m_Disposed) return;
                m_Disposed = true;
                CancelPreparation();
                m_Payload?.Dispose();
                m_Payload = null;
            }
        }
    }
}