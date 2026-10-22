using Colossal.UI.Binding;
using PO2.Interface.Bindings;
using PO2.Localization;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private void InitializeArrayBindings()
        {
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canArraySelectedPart", () => m_ProceduralObjectToolSystem.CanArraySelectedPart));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "arrayActive", () => m_ProceduralObjectToolSystem.ArrayActive));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "arrayDragging", () => m_ProceduralObjectToolSystem.ArrayDragging));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canResetArrayRotation", () => m_ProceduralObjectToolSystem.CanResetArrayRotation));
            AddBinding(CommandBinding("resetArrayRotation", m_ProceduralObjectToolSystem.ResetArrayRotation));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "arrayBusy", () => m_ProceduralObjectToolSystem.ArrayBusy));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "arrayDrawing", () => m_ProceduralObjectToolSystem.ArrayDrawing));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canApplyArray", () => m_ProceduralObjectToolSystem.CanApplyArray));
            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "arrayGap", () => m_ProceduralObjectToolSystem.ArrayGap));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "arrayMode", () => m_ProceduralObjectToolSystem.ArrayMode));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "arrayPathKind", () => m_ProceduralObjectToolSystem.ArrayPathKind));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "arrayAlignment", () => m_ProceduralObjectToolSystem.ArrayAlignment));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "arrayControlPoint", () => m_ProceduralObjectToolSystem.ArrayControlPoint));
            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "arrayCurveLength", () => m_ProceduralObjectToolSystem.ArrayCurveLength));
            AddBinding(CommandBinding("setArrayPathKind", m_ProceduralObjectToolSystem.SetArrayPathKind));
            AddBinding(CommandBinding("setArrayAlignment", m_ProceduralObjectToolSystem.SetArrayAlignment));
            AddBinding(CommandBinding("selectArrayControlPoint", m_ProceduralObjectToolSystem.SelectArrayControlPoint));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "arrayCount", () => m_ProceduralObjectToolSystem.ArrayCount));
            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "arrayEffectiveGap", () => m_ProceduralObjectToolSystem.ArrayEffectiveGap));
            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "arrayStatus", () => m_ProceduralObjectToolSystem.ArrayStatus, LocalizedTextWriter.s_Instance));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "arrayCopies", () => m_ProceduralObjectToolSystem.ArrayCopies));
            AddBinding(new TriggerBinding<int>(kGroup, "setArrayCopies", m_ProceduralObjectToolSystem.SetArrayCopies));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "arrayCapacity", () => m_ProceduralObjectToolSystem.ArrayCapacity));
            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "arraySpacing", () => m_ProceduralObjectToolSystem.ArraySpacing));
            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "arrayPathAngle", () => m_ProceduralObjectToolSystem.ArrayPathAngle));
            AddBinding(new TriggerBinding<float>(kGroup, "setArrayPathAngle", m_ProceduralObjectToolSystem.SetArrayPathAngle));
            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "arrayPathLength", () => m_ProceduralObjectToolSystem.ArrayPathLength));
            AddBinding(new TriggerBinding<float>(kGroup, "setArrayPathLength", m_ProceduralObjectToolSystem.SetArrayPathLength));
            AddBinding(CommandBinding("beginArray", GuardHistory(m_ProceduralObjectToolSystem.BeginArray)));
            AddBinding(new TriggerBinding(kGroup, "cancelArray", m_ProceduralObjectToolSystem.CancelArray));
            AddBinding(CommandBinding("applyArray", m_ProceduralObjectToolSystem.ApplyArray));
            AddBinding(CommandBinding("chooseArrayEndpoint", m_ProceduralObjectToolSystem.ChooseArrayEndpoint));
            AddBinding(new TriggerBinding<float>(kGroup, "setArrayGap", m_ProceduralObjectToolSystem.SetArrayGap));
            AddBinding(CommandBinding("setArrayMode", m_ProceduralObjectToolSystem.SetArrayMode));
        }
    }
}