using Colossal.UI.Binding;
using PO2.Interface.Bindings;
using PO2.Localization;
using PO2.Materials;
using PO2.Rendering.Preview;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private readonly MaterialPreviewService m_MaterialPreviewService = new MaterialPreviewService();

        private bool m_MaterialPreviewDisposePending;

        private int m_MaterialPreviewDisposeCountdown;

        private void RegisterMaterialPreviewStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "materialPreviewUrl", () => m_MaterialPreviewService.Url));

            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "materialPreviewStatus", () => m_MaterialPreviewService.ErrorMessage, LocalizedTextWriter.s_Instance));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialPreviewExposure", () => m_MaterialPreviewService.PreviewExposure));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialPreviewIndirectDiffuse", () => m_MaterialPreviewService.IndirectDiffuse));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialPreviewReflectionLighting", () => m_MaterialPreviewService.ReflectionLighting));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialPreviewKeyIntensity", () => m_MaterialPreviewService.KeyLightIntensity));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialPreviewFillIntensity", () => m_MaterialPreviewService.FillLightIntensity));
        }

        private void RegisterMaterialPreviewActionBindings()
        {
            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialPreviewExposure", GuardHistory(() => m_MaterialPreviewService.AdjustPreviewExposure(-0.5f))));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialPreviewExposure", GuardHistory(() => m_MaterialPreviewService.AdjustPreviewExposure(0.5f))));

            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialPreviewExposure", GuardHistory<float>(SetMaterialPreviewExposure)));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialPreviewIndirectDiffuse", GuardHistory(() => m_MaterialPreviewService.AdjustIndirectDiffuse(-0.1f))));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialPreviewIndirectDiffuse", GuardHistory(() => m_MaterialPreviewService.AdjustIndirectDiffuse(0.1f))));

            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialPreviewIndirectDiffuse", GuardHistory<float>(SetMaterialPreviewIndirectDiffuse)));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialPreviewReflectionLighting", GuardHistory(() => m_MaterialPreviewService.AdjustReflectionLighting(-0.1f))));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialPreviewReflectionLighting", GuardHistory(() => m_MaterialPreviewService.AdjustReflectionLighting(0.1f))));

            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialPreviewReflectionLighting", GuardHistory<float>(SetMaterialPreviewReflectionLighting)));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialPreviewKeyIntensity", GuardHistory(() => m_MaterialPreviewService.AdjustKeyLightIntensity(-100f))));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialPreviewKeyIntensity", GuardHistory(() => m_MaterialPreviewService.AdjustKeyLightIntensity(100f))));

            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialPreviewKeyIntensity", GuardHistory<float>(SetMaterialPreviewKeyIntensity)));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialPreviewFillIntensity", GuardHistory(() => m_MaterialPreviewService.AdjustFillLightIntensity(-100f))));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialPreviewFillIntensity", GuardHistory(() => m_MaterialPreviewService.AdjustFillLightIntensity(100f))));

            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialPreviewFillIntensity", GuardHistory<float>(SetMaterialPreviewFillIntensity)));

            AddBinding(new TriggerBinding(kGroup, "resetMaterialPreviewLighting", GuardHistory(m_MaterialPreviewService.ResetLightingTuning)));
        }

        private void RefreshMaterialPreviewScalars()
        {
            m_MaterialPreviewService.UpdateMaterialScalars(m_MaterialAuthoringState.Metallic, m_MaterialAuthoringState.Smoothness, m_MaterialAuthoringState.CoatStrength);
        }

        private void SetMaterialPreviewExposure(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            m_MaterialPreviewService.AdjustPreviewExposure(value - m_MaterialPreviewService.PreviewExposure);
        }

        private void SetMaterialPreviewIndirectDiffuse(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            m_MaterialPreviewService.AdjustIndirectDiffuse(value - m_MaterialPreviewService.IndirectDiffuse);
        }

        private void SetMaterialPreviewReflectionLighting(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            m_MaterialPreviewService.AdjustReflectionLighting(value - m_MaterialPreviewService.ReflectionLighting);
        }

        private void SetMaterialPreviewKeyIntensity(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            m_MaterialPreviewService.AdjustKeyLightIntensity(value - m_MaterialPreviewService.KeyLightIntensity);
        }

        private void SetMaterialPreviewFillIntensity(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            m_MaterialPreviewService.AdjustFillLightIntensity(value - m_MaterialPreviewService.FillLightIntensity);
        }

        private void RefreshMaterialPreview()
        {
            CancelPendingMaterialPreviewDispose();

            MaterialImportCandidate candidate = GetSelectedImportCandidate();
            if (candidate == null)
            {
                m_MaterialPreviewService.Hide();
                ScheduleMaterialPreviewDispose();
                return;
            }

            m_MaterialPreviewService.ShowCandidate(candidate, m_MaterialAuthoringState);
        }

        private void ScheduleMaterialPreviewDispose()
        {
            // Hide immediately, then let OnUpdate perform the delayed teardown.
            m_MaterialPreviewDisposePending = true;
            m_MaterialPreviewDisposeCountdown = 2;
        }

        private void CancelPendingMaterialPreviewDispose()
        {
            if (!m_MaterialPreviewDisposePending)
            {
                return;
            }

            m_MaterialPreviewDisposePending = false;
            m_MaterialPreviewDisposeCountdown = 0;
            // Recreate from scratch on the next Show call.
            // Reusing a preview whose old URL is being retired caused stale UserImagesManager mappings.
            m_MaterialPreviewService.Dispose();
        }

        private void RefreshMaterialPreviewMapping()
        {
            m_MaterialPreviewService.UpdateMaterialMapping(m_MaterialAuthoringState.PhysicalWidthMeters, m_MaterialAuthoringState.PhysicalHeightMeters, m_MaterialAuthoringState.MappingMode);
        }

        private void RefreshManagedMaterialPreview()
        {
            CancelPendingMaterialPreviewDispose();

            if (!TryGetSelectedManagedMaterial(out MaterialDefinition definition))
            {
                m_MaterialPreviewService.Hide();
                return;
            }

            m_MaterialPreviewService.ShowDefinition(definition);
        }
    }
}