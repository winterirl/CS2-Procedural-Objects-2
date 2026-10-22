using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core.Operations;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using UnityEngine;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        private ObjectClipboard m_Clipboard;
        private long m_ClipboardStatusExpiresAt;
        private ProceduralObject m_ClipboardNoticeSelection;
        private ObjectClipboardCapture m_ClipboardCapture;
        private ProceduralObject m_CopySource;
        private Task<ProceduralObject> m_PasteBuild;
        private IHistoryObjectRenderer m_PasteRenderer;
        private CancellationTokenSource m_ClipboardCancellation;
        private HistoryTransaction m_ClipboardTransaction;
        internal bool HasClipboard => m_Clipboard != null;
        internal bool ClipboardBusy => m_ClipboardCancellation != null;
        internal LocalizedText ClipboardStatus { get; private set; } = LocalizedText.Empty;

        internal bool TryCopyObject(int objectId)
        {
            if (ClipboardBusy || ActionHistory.IsBusy || !TryGetObject(objectId, out var source)) return false;
            var transaction = ActionHistory.TryBegin();
            if (transaction == null) return false;
            m_ClipboardTransaction = transaction;
            m_ClipboardCancellation = new CancellationTokenSource();
            try
            {
                m_CopySource = source;
                m_ClipboardCapture = new ObjectClipboardCapture(source, m_ClipboardCancellation.Token);
                SetClipboardStatus(LocalizedText.For("PO2.Clipboard.Copy.Status.Preparing"));
                return true;
            }
            catch (Exception exception)
            {
                FailClipboardOperation(exception);
                return false;
            }
        }

        internal bool TryPasteObject(Vector3 position)
        {
            if (!HasClipboard || ClipboardBusy || ActionHistory.IsBusy || !IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z)) return false;
            var transaction = ActionHistory.TryBegin();
            if (transaction == null) return false;
            m_ClipboardTransaction = transaction;
            m_ClipboardCancellation = new CancellationTokenSource();
            var token = m_ClipboardCancellation.Token;
            try
            {
                var clipboard = m_Clipboard;
                int id = m_NextId;
                string name = GetUniqueDuplicateName(clipboard.Name);
                m_PasteBuild = Task.Run(() => clipboard.CreateObject(id, name, position, token), token);
                SetClipboardStatus(LocalizedText.For("PO2.Clipboard.Paste.Status.Preparing"));
                return true;
            }
            catch (Exception exception)
            {
                FailClipboardOperation(exception);
                return false;
            }
        }

        private void PollClipboardOperation()
        {
            if (m_ClipboardStatusExpiresAt != 0 && (Stopwatch.GetTimestamp() >= m_ClipboardStatusExpiresAt || !ReferenceEquals(SelectedObject, m_ClipboardNoticeSelection))) ClearClipboardNotice();
            if (!ClipboardBusy) return;
            var token = m_ClipboardCancellation.Token;
            try
            {
                if (m_ClipboardTransaction == null || !m_ClipboardTransaction.IsValid)
                {
                    CancelClipboardOperation();
                    return;
                }

                if (m_ClipboardCapture != null)
                {
                    if (!TryGetObject(m_CopySource.Id, out var current) || !ReferenceEquals(current, m_CopySource))
                    {
                        CancelClipboardOperation();
                        return;
                    }

                    if (!m_ClipboardCapture.Poll(token)) return;
                    m_Clipboard = m_ClipboardCapture.Finish();
                    ResetClipboardOperation();
                    SetClipboardStatus(LocalizedText.For("PO2.Clipboard.Copy.Status.Ready"), expire: true);
                    return;
                }

                if (!m_PasteBuild.IsCompleted) return;
                var obj = m_PasteBuild.GetAwaiter().GetResult();
                if (m_PasteRenderer == null) m_PasteRenderer = m_Renderer.PrepareHistoryObject(obj);
                if (!m_PasteRenderer.Poll(token)) return;
                var description = LocalizedText.For("PO2.History.Action.Object.Paste");
                using (var history = new HistoryEdit(m_HistoryContext, m_ClipboardTransaction, description, new List<HistoryCaptureAction> { new LifetimeAction(obj, false, description) }, message => Mod.log.Error(message), true))
                {
                    m_ClipboardTransaction = null;
                    if (!TryRegisterNewObject(obj, false, null, null, out var reason, m_PasteRenderer)) throw new InvalidOperationException(reason);
                    if (!history.Complete()) throw new InvalidOperationException("Could not record the pasted PO in history.");
                }

                ResetClipboardOperation();
                SetClipboardStatus(LocalizedText.Empty);
            }
            catch (OperationCanceledException)
            {
                CancelClipboardOperation();
            }
            catch (Exception exception)
            {
                FailClipboardOperation(exception);
            }
        }

        internal void CancelClipboardOperation()
        {
            ResetClipboardOperation();
            SetClipboardStatus(LocalizedText.Empty);
        }

        internal void ClearClipboard()
        {
            CancelClipboardOperation();
            m_Clipboard = null;
        }

        private void FailClipboardOperation(Exception exception)
        {
            bool copying = m_CopySource != null;
            ResetClipboardOperation();
            SetClipboardStatus(copying ? LocalizedText.For("PO2.Clipboard.Copy.Status.Failed") : LocalizedText.For("PO2.Clipboard.Paste.Status.Failed"));
            Mod.log.Error("[PO Clipboard] " + exception);
        }

        private void SetClipboardStatus(LocalizedText status, bool expire = false)
        {
            ClipboardStatus = status;
            m_ClipboardNoticeSelection = expire ? SelectedObject : null;
            m_ClipboardStatusExpiresAt = expire ? Stopwatch.GetTimestamp() + 5L * Stopwatch.Frequency : 0;
        }

        private void ClearClipboardNotice()
        {
            if (m_ClipboardStatusExpiresAt != 0) SetClipboardStatus(LocalizedText.Empty);
        }

        private void ResetClipboardOperation()
        {
            var cancellation = m_ClipboardCancellation;
            if (cancellation == null) return;
            cancellation.Cancel();
            m_PasteRenderer?.Dispose();
            var drain = Task.WhenAll(m_ClipboardCapture?.Drain ?? Task.CompletedTask, (Task)m_PasteBuild ?? Task.CompletedTask, m_PasteRenderer?.Drain ?? Task.CompletedTask);
            m_ClipboardCapture = null;
            m_CopySource = null;
            m_PasteBuild = null;
            m_PasteRenderer = null;
            m_ClipboardCancellation = null;
            m_ClipboardTransaction?.Dispose();
            m_ClipboardTransaction = null;
            drain.ContinueWith(completed =>
            {
                var ignored = completed.Exception;
                cancellation.Dispose();
            }, TaskScheduler.Default);
        }
    }
}