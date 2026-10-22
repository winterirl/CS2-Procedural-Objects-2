using System;
using System.Collections.Generic;
using Game.Input;
using PO2.Input;
using PO2.Interface.Models;
using PO2.Tools.ObjectTool;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private sealed class Hotkey
        {
            internal HotkeyDefinition Definition;
            internal ProxyAction Action;
            internal Func<ProxyBinding> Binding;
            internal Action Execute;
            internal Func<bool> CanExecute;
            internal HotkeyPressState Press;
            internal Keyboard Keyboard;
            internal string Path;
            internal ButtonControl Key;
            internal bool KeyHeld, Pending;

            internal void ReadKey(ProxyBinding binding)
            {
                if (Keyboard != UnityEngine.InputSystem.Keyboard.current || Path != binding.path)
                {
                    Keyboard = UnityEngine.InputSystem.Keyboard.current;
                    Path = binding.path;
                    Key = Keyboard != null && binding.isSet ? InputControlPath.TryFindControl<ButtonControl>(Keyboard, Path) : null;
                    Press = default;
                }

                KeyHeld = Key != null && Key.isPressed;
            }
        }

        private readonly List<Hotkey> m_Hotkeys = new List<Hotkey>();
        private bool m_HotkeyUiFocused;
        private int m_LastHotkeyFrame = -1;

        private void AddHotkey(string name, Func<ProxyBinding> binding, Action execute, Func<bool> canExecute)
        {
            HotkeyDefinition definition = Array.Find(HotkeyCatalog.Entries, entry => entry.Name == name);
            if (definition.Name == null) throw new InvalidOperationException("Missing hotkey: " + name);
            ProxyAction action = Mod.Settings.GetAction(name);
            action.shouldBeEnabled = false;
            m_Hotkeys.Add(new Hotkey
            {
                Definition = definition, Action = action, Binding = binding,
                Execute = execute, CanExecute = canExecute,
            });
        }

        private void SetHotkeysDisabled()
        {
            foreach (Hotkey hotkey in m_Hotkeys)
            {
                hotkey.Action.shouldBeEnabled = false;
                hotkey.Press = default;
            }

            m_HotkeyUiFocused = false;
        }

        private void UpdateHotkeys(bool allowKeyboardInput)
        {
            HotkeyScope scope = GetHotkeyScope();
            Keyboard keyboard = Keyboard.current;
            // These inputs already belong to the tool's cancel/continue handlers.
            bool toolInput = keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
            toolInput |= Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            bool inputAllowed = allowKeyboardInput && !m_HotkeyUiFocused && !InputManager.instance.overlayActive;
            foreach (Hotkey hotkey in m_Hotkeys)
            {
                ProxyBinding binding = hotkey.Binding();
                hotkey.ReadKey(binding);
                bool eligible = inputAllowed && binding.isSet && (hotkey.Definition.Scope & scope) != 0 && hotkey.CanExecute();
                if (hotkey.Action.shouldBeEnabled != eligible) hotkey.Action.shouldBeEnabled = eligible;
                bool active = eligible && hotkey.Action.enabled;
                hotkey.Pending = hotkey.Press.Update(active, hotkey.KeyHeld, active && hotkey.Action.WasPressedThisFrame());
            }

            if (toolInput || m_LastHotkeyFrame == UnityEngine.Time.frameCount)
            {
                ConsumeHotkeyPresses();
                return;
            }

            // Eligibility is sampled before dispatch. A mode change cannot run a second command.
            foreach (Hotkey hotkey in m_Hotkeys)
            {
                if (!hotkey.Pending) continue;
                m_LastHotkeyFrame = UnityEngine.Time.frameCount;
                ConsumeHotkeyPresses();
                hotkey.Execute();
                return;
            }
        }

        private void ConsumeHotkeyPresses()
        {
            foreach (Hotkey hotkey in m_Hotkeys) hotkey.Press.Consume(hotkey.KeyHeld);
        }

        private HotkeyScope GetHotkeyScope()
        {
            if (!m_ProceduralObjectSystem.IsToolActive) return HotkeyScope.Inactive;
            if (m_MaterialManagementView != MaterialManagementView.None || m_ProceduralObjectToolSystem.Workspace == Workspace.MaterialLibrary || m_ProceduralObjectToolSystem.Workspace == Workspace.SaveManager || m_PersistenceCoordinatorSystem.RecoveryResultPending) return HotkeyScope.Management;
            if (m_ProceduralObjectSystem.HistoryBusy || m_ProceduralObjectSystem.ClipboardBusy || m_ProceduralObjectToolSystem.AssetConversionBusy || m_ProceduralObjectToolSystem.ArrayBusy || m_ProceduralObjectToolSystem.MeshNavigationBusy) return HotkeyScope.Busy;
            if (m_ProceduralObjectToolSystem.ArrayActive)
                return m_ProceduralObjectToolSystem.ArrayDragging ? HotkeyScope.Drag : HotkeyScope.Array;
            switch (m_ProceduralObjectToolSystem.ActiveInteraction)
            {
                case NestedInteraction.GizmoDrag:
                case NestedInteraction.BoxSelection:
                case NestedInteraction.VertexSlideDrag:
                    return HotkeyScope.Drag;
                case NestedInteraction.OrientationTarget:
                    return HotkeyScope.Axis;
                case NestedInteraction.FaceOperationSelection:
                    return m_ProceduralObjectToolSystem.IsMeshEditFaceExtrudePositioning || m_ProceduralObjectToolSystem.IsMeshEditFaceInsetPositioning ? HotkeyScope.FacePosition : HotkeyScope.FaceSelection;
                case NestedInteraction.PrimitivePlacement:
                case NestedInteraction.PrimitivePartPlacement:
                    return HotkeyScope.Create;
                case NestedInteraction.AssetConversionTarget:
                    return HotkeyScope.Convert;
                case NestedInteraction.None:
                    break;
                default:
                    return HotkeyScope.Target;
            }

            switch (m_ProceduralObjectToolSystem.Workspace)
            {
                case Workspace.Create: return HotkeyScope.Create;
                case Workspace.Convert: return HotkeyScope.Convert;
                case Workspace.Copy: return HotkeyScope.Copy;
                case Workspace.Paste: return HotkeyScope.Paste;
                case Workspace.Delete: return HotkeyScope.Delete;
                case Workspace.Transform:
                    if (m_ProceduralObjectToolSystem.DeformMode) return HotkeyScope.Deform;
                    if (m_ProceduralObjectToolSystem.MirrorMode) return HotkeyScope.Mirror;
                    return HotkeyScope.Object;
                case Workspace.MeshEdit: return HotkeyScope.Edit;
                case Workspace.Texturing: return HotkeyScope.Texturing;
                case Workspace.Lod: return HotkeyScope.Lod;
                default: return HotkeyScope.Select;
            }
        }

        private void NavigateMainMode(Workspace workspace)
        {
            if (!m_ProceduralObjectSystem.IsToolActive || NavigationInputBlocked || m_MaterialImportBusy) return;
            m_ProceduralObjectToolSystem.CancelAxisPicking();
            switch (workspace)
            {
                case Workspace.Select:
                    NavigateWorkspace(m_ProceduralObjectToolSystem.NavigateToSelectWorkspace);
                    break;
                case Workspace.Create:
                    NavigateWorkspace(m_ProceduralObjectToolSystem.NavigateToCreateWorkspace);
                    break;
                case Workspace.Convert:
                    if (!m_ProceduralObjectToolSystem.AssetConversionBusy)
                        NavigateWorkspace(m_ProceduralObjectToolSystem.NavigateToConvertWorkspace);
                    break;
                case Workspace.Delete:
                    bool deleting = m_ProceduralObjectToolSystem.Workspace == Workspace.Delete;
                    NavigateWorkspace(() =>
                    {
                        if (deleting) m_ProceduralObjectToolSystem.NavigateToSelectWorkspace();
                        else m_ProceduralObjectToolSystem.NavigateToDeleteWorkspace();
                    });
                    break;
                case Workspace.Copy:
                    bool copying = m_ProceduralObjectToolSystem.Workspace == Workspace.Copy;
                    NavigateWorkspace(() =>
                    {
                        if (copying) m_ProceduralObjectToolSystem.NavigateToSelectWorkspace();
                        else m_ProceduralObjectToolSystem.NavigateToCopyWorkspace();
                    });
                    break;
                case Workspace.Paste:
                    if (!m_ProceduralObjectSystem.HasClipboard) break;
                    bool pasting = m_ProceduralObjectToolSystem.Workspace == Workspace.Paste;
                    NavigateWorkspace(() =>
                    {
                        if (pasting) m_ProceduralObjectToolSystem.NavigateToSelectWorkspace();
                        else m_ProceduralObjectToolSystem.NavigateToPasteWorkspace();
                    });
                    break;
                case Workspace.MaterialLibrary:
                    OpenMaterialLibraryView();
                    break;
            }
        }
    }
}