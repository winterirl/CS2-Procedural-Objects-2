using Colossal.UI.Binding;
using PO2.Interface.Bindings;
using PO2.Localization;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private void RegisterHistoryBindings()
        {
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canUndo", () => !m_MaterialImportBusy && m_ProceduralObjectSystem.CanUndoHistory));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canRedo", () => !m_MaterialImportBusy && m_ProceduralObjectSystem.CanRedoHistory));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "historyBusy", () => m_ProceduralObjectSystem.HistoryBusy));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "deformationCommitBusy", () => m_ProceduralObjectToolSystem.DeformationCommitBusy));
            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "deformationCommitStatus", () => m_ProceduralObjectToolSystem.DeformationCommitStatus, LocalizedTextWriter.s_Instance));
            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "undoDescription", () => m_ProceduralObjectSystem.ActionHistory.UndoDescription, LocalizedTextWriter.s_Instance));
            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "redoDescription", () => m_ProceduralObjectSystem.ActionHistory.RedoDescription, LocalizedTextWriter.s_Instance));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "historyFailure", () => !string.IsNullOrEmpty(m_ProceduralObjectSystem.ActionHistory.LastFailure)));
            // These methods enforce replay eligibility and cancel an uncommitted array preview.
            AddBinding(CommandBinding("undo", GuardHistoryReplay(() =>
            {
                if (!m_MaterialImportBusy) m_ProceduralObjectSystem.UndoHistory();
            })));
            AddBinding(CommandBinding("redo", GuardHistoryReplay(() =>
            {
                if (!m_MaterialImportBusy) m_ProceduralObjectSystem.RedoHistory();
            })));
        }
    }
}