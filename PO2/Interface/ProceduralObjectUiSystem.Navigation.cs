using System;
using Colossal.UI.Binding;
using PO2.Interface.Models;
using PO2.Tools.ObjectTool;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private HelpView m_HelpView = HelpView.None;

        private void RegisterNavigationStateBindings()
        {
            // Update bindings are the C# -> React side.
            // They are getters instead of duplicated UI state so the panel always reflects runtime state.
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "toolActive", () => m_ProceduralObjectSystem.IsToolActive));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "activeWorkspace", () => (int)m_ProceduralObjectToolSystem.Workspace));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "activeToolMode", () => (int)m_ProceduralObjectToolSystem.Mode));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "deleteCursorVisible", () => m_ProceduralObjectToolSystem.DeleteCursorVisible));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "activeInteraction", () => (int)m_ProceduralObjectToolSystem.ActiveInteraction));
        }

        private void RegisterHelpBindings()
        {
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "workspaceReturnTarget", () => (int)m_ProceduralObjectToolSystem.WorkspaceReturnTarget));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "helpOpen", () => m_HelpView != HelpView.None));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "helpView", () => (int)m_HelpView));
        }

        private void RegisterNavigationActionBindings()
        {
            // Trigger bindings are the React -> C# side.
            // Most forward straight to the owning system~ UI-only workflows below keep a local handler.
            AddBinding(CommandBinding("toggleTool", ToggleTool));

            AddBinding(new TriggerBinding(kGroup, "closeTool", GuardNavigation(CloseTool)));

            AddBinding(new TriggerBinding(kGroup, "navigateBack", NavigateBack));

            AddBinding(CommandBinding("toggleHelpView", ToggleHelpView));

            AddBinding(new TriggerBinding(kGroup, "closeHelpView", CloseHelpView));
            AddBinding(new TriggerBinding(kGroup, "showContextHelp", () => SetHelpView(HelpView.Overview)));
        }

        private void RegisterWorkspaceNavigationBindings()
        {
            AddBinding(CommandBinding("toggleDeleteMode", () => NavigateMainMode(Workspace.Delete)));

            AddBinding(CommandBinding("navigateSelectWorkspace", () => NavigateMainMode(Workspace.Select)));

            AddBinding(CommandBinding("navigateCreateWorkspace", () => NavigateMainMode(Workspace.Create)));

            AddBinding(CommandBinding("navigateConvertWorkspace", () => NavigateMainMode(Workspace.Convert)));

            AddBinding(new TriggerBinding(kGroup, "navigateObjectListWorkspace", GuardNavigation(OpenObjectListView)));
        }

        private void RegisterEditorNavigationBindings()
        {
            AddBinding(CommandBinding("navigateTransformWorkspace", GuardNavigation(() => NavigateWorkspace(m_ProceduralObjectToolSystem.NavigateToTransformWorkspace))));

            AddBinding(CommandBinding("navigateMeshEditWorkspace", GuardNavigation(() => NavigateWorkspace(m_ProceduralObjectToolSystem.NavigateToMeshEditWorkspace))));

            AddBinding(CommandBinding("navigateTexturingWorkspace", GuardNavigation(() => NavigateWorkspace(m_ProceduralObjectToolSystem.NavigateToTexturingWorkspace))));
        }

        public static bool TryBackOutMaterialManagementView()
        {
            if (s_Instance == null)
            {
                return false;
            }

            // Import/edit backs out to the library.
            // Library backs out to the main PO panel.
            // A busy disk operation cannot be interrupted halfway.
            if (s_Instance.m_MaterialManagementView == MaterialManagementView.ImportNew || s_Instance.m_MaterialManagementView == MaterialManagementView.EditUser)
            {
                if (!s_Instance.m_MaterialImportBusy)
                {
                    s_Instance.CloseMaterialImportView();
                }

                return true;
            }

            if (s_Instance.m_MaterialManagementView == MaterialManagementView.Library)
            {
                s_Instance.CloseMaterialLibraryView();
                return true;
            }

            return false;
        }

        private void NavigateWorkspace(Action navigation)
        {
            if (navigation == null || m_MaterialImportBusy)
            {
                return;
            }

            if (m_MaterialManagementView != MaterialManagementView.None)
            {
                m_MaterialManagementView = MaterialManagementView.None;
                m_MaterialPreviewService.Hide();
                ScheduleMaterialPreviewDispose();
                m_ProceduralObjectToolSystem.ExitMaterialLibraryWorkspace();
            }

            CloseSaveManager();
            navigation();
        }

        private void ToggleHelpView()
        {
            if (m_HelpView == HelpView.None)
            {
                SetHelpView(HelpView.Overview);
            }
            else
            {
                CloseHelpView();
            }
        }

        private void CloseTool()
        {
            if (m_MaterialImportBusy)
            {
                return;
            }

            if (m_MaterialManagementView != MaterialManagementView.None)
            {
                m_MaterialManagementView = MaterialManagementView.None;
                m_MaterialPreviewService.Hide();
                ScheduleMaterialPreviewDispose();
                m_ProceduralObjectToolSystem.ExitMaterialLibraryWorkspace();
            }

            CloseSaveManager();
            m_ProceduralObjectToolSystem.CloseTool();
        }

        private void ToggleTool()
        {
            if (!m_ProceduralObjectSystem.IsToolActive && m_ProceduralObjectSystem.HistoryBusy)
            {
                m_ProceduralObjectSystem.ActivateTool();
                return;
            }

            // The launcher and hotkey share the same navigation safeguards.
            if (NavigationInputBlocked) return;
            m_ProceduralObjectToolSystem.CancelAxisPicking();

            if (m_ProceduralObjectSystem.IsToolActive)
            {
                m_PersistenceCoordinatorSystem?.DismissRecoveryResult();
                CloseSaveManager();
                m_ProceduralObjectToolSystem.CloseTool();
            }
            else
            {
                m_ProceduralObjectSystem.ActivateTool();
            }
        }

        private void NavigateBack()
        {
            if (m_ProceduralObjectSystem.ClipboardBusy)
            {
                m_ProceduralObjectSystem.CancelClipboardOperation();
                return;
            }

            if (m_ProceduralObjectSystem.HistoryBusy)
            {
                m_ProceduralObjectSystem.CancelHistoryReplay();
                return;
            }

            if (!NavigationInputBlocked) m_ProceduralObjectToolSystem.BackOutOneLevel();
        }
    }
}