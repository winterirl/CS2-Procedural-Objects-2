using System;
using Colossal.UI.Binding;
using PO2.Diagnostics;
using PO2.Interface.Models;
using PO2.Localization;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private PerformancePayload m_PerformanceDiagnostics = PerformancePayload.s_Idle;

        private void SetHelpView(HelpView view)
        {
            m_HelpView = view;
        }

        private void CloseHelpView()
        {
            SetHelpView(HelpView.None);
        }

        private void OpenPerformanceView()
        {
            if (m_MaterialImportBusy || m_HelpView == HelpView.None)
            {
                return;
            }

            SetHelpView(HelpView.Performance);
            CapturePerformanceDiagnostics();
        }

        private void RefreshPerformanceDiagnostics()
        {
            if (m_MaterialImportBusy || m_HelpView != HelpView.Performance)
            {
                return;
            }

            CapturePerformanceDiagnostics();
        }

        private void CapturePerformanceDiagnostics()
        {
            m_PerformanceDiagnostics = PerformancePayload.s_Capturing;

            try
            {
                if (m_ProceduralObjectSystem == null)
                {
                    throw new InvalidOperationException("The Procedural Object system is unavailable.");
                }

                PerformanceDiagnosticsSnapshot snapshot = m_ProceduralObjectSystem.CapturePerformanceDiagnostics();
                m_PerformanceDiagnostics = new PerformancePayload(snapshot);
            }
            catch (Exception exception)
            {
                Mod.log.Error("[PO Diagnostics] Snapshot capture failed: " + exception);
                m_PerformanceDiagnostics = new PerformancePayload(LocalizedText.For("PO2.Performance.Snapshot.Error.CaptureFailed"));
            }
        }


        private void RegisterPerformanceBindings()
        {
            AddUpdateBinding(new GetterValueBinding<PerformancePayload>(kGroup, "performanceDiagnostics", () => m_PerformanceDiagnostics, new ValueWriter<PerformancePayload>()));
        }

        private void RegisterPerformanceActionBindings()
        {
            AddBinding(new TriggerBinding(kGroup, "openPerformanceView", GuardHistory(OpenPerformanceView)));

            AddBinding(new TriggerBinding(kGroup, "refreshPerformanceDiagnostics", GuardHistory(RefreshPerformanceDiagnostics)));
        }
    }
}