using Colossal.UI.Binding;
using PO2.Interface.Bindings;
using PO2.Localization;
using PO2.Tools.ObjectTool;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private void RegisterClipboardBindings()
        {
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "hasClipboard", () => m_ProceduralObjectSystem.HasClipboard));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "clipboardBusy", () => m_ProceduralObjectSystem.ClipboardBusy));
            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "clipboardStatus", () => m_ProceduralObjectSystem.ClipboardStatus, LocalizedTextWriter.s_Instance));
            AddBinding(CommandBinding("toggleCopyMode", () => NavigateMainMode(Workspace.Copy)));
            AddBinding(CommandBinding("togglePasteMode", () => NavigateMainMode(Workspace.Paste)));
            AddBinding(CommandBinding("copySelected", GuardHistory(m_ProceduralObjectToolSystem.CopySelectedObject)));
            AddBinding(CommandBinding("clearClipboard", m_ProceduralObjectSystem.ClearClipboard));
        }
    }
}