using System;
using PO2.Core.Lod;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Rendering.Lod;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        private LodGenerationKey m_LodLoggedPreviewFailure;
        internal LodPreviewSession LodPreview => m_LodCoordinator?.Preview;
        internal bool LodGenerationAvailable => m_LodCoordinator != null;

        private void LogLodPreviewFailure()
        {
            LodPreviewSession session = LodPreview;
            if (session?.Status != LodPreviewStatus.Failed || session.RequestKey == m_LodLoggedPreviewFailure)
            {
                return;
            }

            m_LodLoggedPreviewFailure = session.RequestKey;
            Mod.log.Warn("[PO LOD] Preview failed: " + session.FailureReason);
        }

        internal bool BeginSelectedLodPreview()
        {
            if (m_LodCoordinator == null || m_Renderer == null || HistoryBusy || !IsToolActive || !TryGetSelected(out ProceduralObject obj))
            {
                return false;
            }

            EndLodPreview();
            m_LodCoordinator.SetSelectedObject(obj.Id);
            LodPreviewSession session = m_LodCoordinator.BeginPreview(obj.Id);
            if (session == null)
            {
                return false;
            }

            try
            {
                if (!m_Renderer.BeginLodPreview(obj.Id, session.SessionId, obj.LodProfile))
                {
                    EndLodPreview();
                    return false;
                }

                BorrowAcceptedLodPreview();
                SetLodPreviewDisplay(LodPreviewDisplay.Original);
                return true;
            }
            catch (Exception exception)
            {
                EndLodPreview();
                Mod.log.Warn("[PO LOD] Could not start preview: " + exception.Message);
                return false;
            }
        }

        internal bool SetLodPreviewDraft(LodProfile profile)
        {
            if (!CanUseLodPreview(out ProceduralObject obj) || obj.IsLocked)
            {
                return false;
            }

            LodPreviewSession session = LodPreview;
            if (Equals(session.DraftProfile, profile))
            {
                return false;
            }

            bool changedGeneration = !LodProfile.HasSameGeneration(session.DraftProfile, profile);
            LodPreviewDisplay display = session.Display;
            LodProfile settings = LodProfile.Resolve(profile);
            if ((display == LodPreviewDisplay.Lod1 && !settings.Lod1.Enabled) || (display == LodPreviewDisplay.Lod2 && !settings.Lod2.Enabled))
            {
                display = LodPreviewDisplay.Original;
            }

            try
            {
                m_Renderer.SetLodPreviewDraft(obj.Id, session.SessionId, profile, display);
            }
            catch (Exception exception)
            {
                Mod.log.Warn("[PO LOD] Could not display draft settings: " + exception.Message);
                return false;
            }

            if (!m_LodCoordinator.SetPreviewDraft(profile))
            {
                return false;
            }

            session.Display = display;
            if (changedGeneration)
            {
                m_Renderer.DiscardPendingLodPreview(obj.Id, session.SessionId);
            }

            BorrowAcceptedLodPreview();
            return true;
        }

        internal bool GenerateLodPreview()
        {
            if (!CanUseLodPreview(out ProceduralObject obj) || (obj.IsLocked && !Equals(obj.LodProfile, LodPreview.DraftProfile)))
            {
                return false;
            }

            m_Renderer.DiscardPendingLodPreview(obj.Id, LodPreview.SessionId);
            return m_LodCoordinator.RequestPreview();
        }

        internal bool SetLodPreviewDisplay(LodPreviewDisplay display)
        {
            if (!CanUseLodPreview(out ProceduralObject obj) || !LodPreview.CanDisplay(display))
            {
                return false;
            }

            try
            {
                m_Renderer.SetLodPreviewDraft(obj.Id, LodPreview.SessionId, LodPreview.DraftProfile, display);
            }
            catch (Exception exception)
            {
                Mod.log.Warn("[PO LOD] Could not change preview display: " + exception.Message);
                return false;
            }

            LodPreview.Display = display;
            return true;
        }

        internal bool ApplyLodPreview(LocalizedText description)
        {
            if (!CanUseLodPreview(out ProceduralObject obj) || obj.IsLocked || !m_LodCoordinator.TryPreparePromotion(out LodGenerationKey acceptedKey))
            {
                return false;
            }

            LodPreviewSession session = LodPreview;
            LodProfile before = obj.LodProfile;
            using (var history = BeginObjectHistory(obj, ObjectValueKind.LodProfile, description))
            {
                if (history == null)
                {
                    return false;
                }

                try
                {
                    bool promoted = m_Renderer.TryPromoteLodPreview(session.RequestKey, acceptedKey, session.DraftProfile, () =>
                    {
                        obj.SetLodProfile(session.DraftProfile);
                        return history.Complete();
                    });
                    if (!promoted)
                    {
                        obj.SetLodProfile(before);
                        return false;
                    }

                    m_LodCoordinator.AcceptPreview(acceptedKey);
                    return true;
                }
                catch (Exception exception)
                {
                    obj.SetLodProfile(before);
                    Mod.log.Warn("[PO LOD] Could not apply preview: " + exception.Message);
                    return false;
                }
            }
        }

        internal void CancelLodPreview()
        {
            LodPreviewSession session = LodPreview;
            if (session == null)
            {
                return;
            }

            m_LodCoordinator.CancelPreview();
            m_Renderer?.CancelLodPreview(session.ObjectId, session.SessionId, session.DraftProfile);
        }

        internal void EndLodPreview()
        {
            LodPreviewSession session = LodPreview;
            if (session == null)
            {
                return;
            }

            m_LodCoordinator.EndPreview();
            m_Renderer?.EndLodPreview(session.ObjectId, session.SessionId);
        }

        private bool CanUseLodPreview(out ProceduralObject obj)
        {
            obj = null;
            return IsToolActive && !HistoryBusy && LodPreview != null && m_SelectedObjectId == LodPreview.ObjectId && m_Objects.TryGetValue(LodPreview.ObjectId, out obj);
        }

        private void BorrowAcceptedLodPreview()
        {
            LodPreviewSession session = LodPreview;
            if (session == null || session.IsBusy || session.HasCompletedGeneration)
            {
                return;
            }

            if (m_Renderer.TryBorrowAcceptedLod(session.RequestKey, session.DraftProfile, out LodObjectDiagnostics diagnostics, out LodAutomaticChain chain))
            {
                session.Diagnostics = diagnostics;
                session.AutomaticChain = chain;
                m_LodCoordinator.MarkPreviewReady(session.RequestKey, 0);
            }
        }
    }
}