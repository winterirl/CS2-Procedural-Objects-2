using System;
using System.Globalization;
using Colossal.UI.Binding;
using PO2.Core.Lod;
using PO2.Interface.Models;
using PO2.Localization;
using PO2.Rendering.Lod;
using PO2.Tools.ObjectTool;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private void RegisterLodBindings()
        {
            AddUpdateBinding(new GetterValueBinding<LodPanelState>(kGroup, "lodState", CaptureLodPanelState, new ValueWriter<LodPanelState>()));
            AddBinding(new TriggerBinding(kGroup, "navigateLodWorkspace", GuardNavigation(() => NavigateWorkspace(m_ProceduralObjectToolSystem.NavigateToLodWorkspace))));
            AddBinding(new TriggerBinding<string, int, double>(kGroup, "setLodValue", SetLodValue));
            AddBinding(new TriggerBinding<string, int>(kGroup, "setLodDisplay", (token, display) =>
            {
                if (CurrentLodSession(token)) m_ProceduralObjectSystem.SetLodPreviewDisplay((LodPreviewDisplay)display);
            }));
            AddBinding(new TriggerBinding<string>(kGroup, "generateLodPreview", token =>
            {
                if (CurrentLodSession(token)) m_ProceduralObjectSystem.GenerateLodPreview();
            }));
            AddBinding(new TriggerBinding<string>(kGroup, "applyLodPreview", token =>
            {
                if (CurrentLodSession(token)) m_ProceduralObjectSystem.ApplyLodPreview(m_ProceduralObjectSystem.LodPreview.DraftProfile == null ? LocalizedText.For("PO2.History.Action.Lod.RestoreDefaults") : LocalizedText.For("PO2.History.Action.Lod.Apply"));
            }));
            AddBinding(new TriggerBinding<string>(kGroup, "cancelLodPreview", token =>
            {
                if (CurrentLodSession(token)) m_ProceduralObjectSystem.CancelLodPreview();
            }));
        }

        private LodPanelState CaptureLodPanelState()
        {
            var obj = m_ProceduralObjectSystem.SelectedObject;
            if (m_ProceduralObjectToolSystem.Workspace != Workspace.Lod || obj == null)
            {
                return LodPanelState.s_Empty;
            }

            return new LodPanelState(obj, m_ProceduralObjectSystem.LodPreview, m_ProceduralObjectSystem.LodGenerationAvailable, NavigationInputBlocked || m_MaterialImportBusy, Mod.Settings?.LOD1Distance ?? 1f, Mod.Settings?.LOD2Distance ?? 1f);
        }

        private bool CurrentLodSession(string token)
        {
            LodPreviewSession session = m_ProceduralObjectSystem.LodPreview;
            return !NavigationInputBlocked && !m_MaterialImportBusy && session != null && m_ProceduralObjectSystem.IsToolActive && m_ProceduralObjectToolSystem.Workspace == Workspace.Lod && m_ProceduralObjectSystem.SelectedObjectId == session.ObjectId && ulong.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id == session.SessionId;
        }

        private void SetLodValue(string token, int field, double value)
        {
            if (!CurrentLodSession(token) || m_ProceduralObjectSystem.SelectedObjectLocked)
            {
                return;
            }

            if (LodDraftEditing.TryChange(m_ProceduralObjectSystem.LodPreview.DraftProfile, (LodDraftField)field, value, Mod.Settings?.LOD1Distance ?? 1f, Mod.Settings?.LOD2Distance ?? 1f, out LodProfile changed))
            {
                m_ProceduralObjectSystem.SetLodPreviewDraft(changed);
            }
        }
    }
}