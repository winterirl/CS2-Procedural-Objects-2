using Colossal.UI.Binding;
using Game;
using Game.Input;
using Game.SceneFlow;
using Game.UI;
using UnityEngine.Scripting;
using PO2.Core;
using PO2.Interface.Bindings;
using PO2.Interface.Models;
using PO2.Localization;
using PO2.Persistence;
using PO2.Tools.ObjectTool;

namespace PO2.Interface
{
    // C# bindings and commands for the React panel.
    public partial class ProceduralObjectUiSystem : UISystemBase
    {
        public const string kGroup = "po2";

        private ProceduralObjectSystem m_ProceduralObjectSystem;

        private ProceduralObjectToolSystem m_ProceduralObjectToolSystem;

        private PersistenceCoordinatorSystem m_PersistenceCoordinatorSystem;

        private static ProceduralObjectUiSystem s_Instance;

        private readonly NumberFormatCache m_NumberFormat = new NumberFormatCache();

        public override GameMode gameMode => GameMode.GameOrEditor;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            AddUpdateBinding(new GetterValueBinding<NumberFormat>(kGroup, "numberFormat", () => m_NumberFormat.Get(GameManager.instance?.localizationManager?.activeLocaleId), NumberFormatWriter.s_Instance));
            s_Instance = this;
            m_ProceduralObjectSystem = World.GetOrCreateSystemManaged<ProceduralObjectSystem>();
            m_ProceduralObjectToolSystem = World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>();
            m_PersistenceCoordinatorSystem = World.GetOrCreateSystemManaged<PersistenceCoordinatorSystem>();
            InitializeSaveManagerBindings();
            InitializeArrayBindings();
            RegisterLodBindings();
            RegisterTopologyStateBindings();
            RegisterHistoryBindings();
            RegisterClipboardBindings();
            RegisterDeformationStateBindings();
            RegisterNavigationStateBindings();
            RegisterConversionBindings();
            RegisterHelpBindings();
            RegisterPerformanceBindings();
            RegisterCreationStateBindings();
            RegisterRecoveryStateBindings();
            RegisterObjectStateBindings();
            RegisterTransformStateBindings();
            RegisterMeshEditStateBindings();
            RegisterScaleBindings();
            RegisterSurfaceAreaSelectionBindings();
            RegisterDeformationResetBindings();
            RegisterTexturingStateBindings();
            RegisterMaterialImportStateBindings();
            RegisterMaterialLibraryStateBindings();
            RegisterMaterialPreviewStateBindings();
            RegisterMaterialAuthoringStateBindings();
            RegisterNavigationActionBindings();
            RegisterPerformanceActionBindings();
            RegisterRoundPrimitiveActions();
            RegisterPrimitivePlacementBindings();
            RegisterObjectCreationBindings();
            RegisterWorkspaceNavigationBindings();
            RegisterObjectListSelectionBinding();
            RegisterEditorNavigationBindings();
            RegisterPrimitiveParameterBindings();
            RegisterTransformModeBindings();
            RegisterSnappingActionBindings();
            RegisterTransformActionBindings();
            RegisterMeshEditActionBindings();
            RegisterDeselectBinding();
            RegisterMappingActionBindings();
            RegisterPartSelectionBindings();
            RegisterSurfaceAreaActionBindings();
            RegisterMaterialLibraryActionBindings();
            RegisterMaterialAuthoringActionBindings();
            RegisterMaterialPreviewActionBindings();
            RegisterAppearanceActionBindings();
            RegisterObjectActionBindings();
            RegisterRecoveryActionBindings();
            RegisterDeleteBinding();
            AddBinding(new TriggerBinding<bool>(kGroup, "setHotkeyUiFocused", focused => m_HotkeyUiFocused = focused));
            InitializeHotkeys();
        }

        protected override void OnUpdate()
        {
            m_ProceduralObjectToolSystem.ProcessHistoryInput();
            m_ProceduralObjectToolSystem.ProcessClipboardInput();
            UpdateSaveManager();
            // A completed recovery is a one-view result, not the default Select panel.
            // This also covers workspace changes made by world selection or hotkeys.
            if (m_PersistenceCoordinatorSystem != null && m_PersistenceCoordinatorSystem.RecoveryResultPending && (!m_ProceduralObjectSystem.IsToolActive || m_ProceduralObjectToolSystem.Workspace != Workspace.Select || m_MaterialManagementView != MaterialManagementView.None))
            {
                m_PersistenceCoordinatorSystem.DismissRecoveryResult();
            }

            // Keep visibility for this session, but reopen on contextual help.
            if (!m_ProceduralObjectSystem.IsToolActive && m_HelpView == HelpView.Performance)
            {
                SetHelpView(HelpView.Overview);
            }

            // Use the registered action so rebinding takes effect without a reload.
            bool allowToggle = Mod.Settings != null && (GameManager.instance.gameMode & GameMode.GameOrEditor) != 0 && !InputManager.instance.hasInputFieldFocus;
            UpdateHotkeys(allowToggle);

            base.OnUpdate();

            if (!m_MaterialPreviewDisposePending)
            {
                return;
            }

            // Give Cohtml a couple of frames to drop the old user-image URL before
            // its render texture and registration are removed.
            if (m_MaterialPreviewDisposeCountdown > 0)
            {
                m_MaterialPreviewDisposeCountdown--;
            }

            if (m_MaterialPreviewDisposeCountdown <= 0)
            {
                m_MaterialPreviewDisposePending = false;
                m_MaterialPreviewDisposeCountdown = 0;
                m_MaterialPreviewService.Dispose();
            }
        }

        protected override void OnStopRunning()
        {
            SetHotkeysDisabled();
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            SetHotkeysDisabled();
            m_MaterialPreviewDisposePending = false;
            m_MaterialPreviewDisposeCountdown = 0;
            m_MaterialPreviewService.Dispose();

            if (ReferenceEquals(s_Instance, this))
            {
                s_Instance = null;
            }

            base.OnDestroy();
        }

        [Preserve]
        public ProceduralObjectUiSystem()
        {
        }
    }
}