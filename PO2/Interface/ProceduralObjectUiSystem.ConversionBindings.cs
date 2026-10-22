using Colossal.UI.Binding;
using PO2.Interface.Bindings;
using PO2.Localization;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private void RegisterConversionBindings()
        {
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "assetConversionBusy", () => m_ProceduralObjectToolSystem.AssetConversionBusy));

            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "assetConversionStatus", () => m_ProceduralObjectToolSystem.AssetConversionStatus, LocalizedTextWriter.s_Instance));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "assetConversionPhase", () => (int)m_ProceduralObjectToolSystem.AssetConversionPhase));
        }
    }
}