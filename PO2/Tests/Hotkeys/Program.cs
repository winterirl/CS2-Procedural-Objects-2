using System;
using System.Linq;
using System.Reflection;
using Game.Input;
using Game.Settings;
using PO2;
using PO2.Input;
using PO2.Interface;
using PO2.Interface.Models;
using PO2.Tools.ObjectTool;
using UnityEngine.InputSystem;

internal static class Program {
    private static int s_Checks;
    private static void Check(bool value, string message) {
        s_Checks++;
        if (!value) throw new Exception(message);
    }

    private static ProceduralObjectUiSystem Setup(Workspace workspace = Workspace.Transform) {
        Mod.Settings = new Setting(null);
        Mod.Settings.SetDefaults();
        Keyboard.current = new Keyboard();
        Mouse.current = new Mouse();
        InputManager.instance.overlayActive = false;
        var ui = new ProceduralObjectUiSystem();
        ui.m_ProceduralObjectSystem.IsToolActive = true;
        ui.m_ProceduralObjectSystem.SelectedObject = new object();
        ui.m_ProceduralObjectSystem.SelectedPartCount = 2;
        ui.m_ProceduralObjectToolSystem.Workspace = workspace;
        ui.RegisterForTests();
        return ui;
    }

    private static void Bind(string name, string key = "test") => typeof(Setting).GetProperty(name).SetValue(Mod.Settings, new ProxyBinding { path = "<Keyboard>/" + key });
    private static void Press(string name, bool held = true) {
        var binding = (ProxyBinding)typeof(Setting).GetProperty(name).GetValue(Mod.Settings);
        Keyboard.current.Key(binding.path).isPressed = held;
        Mod.Settings.GetAction(name).Pressed = held;
    }
    private static void Release() {
        foreach (var key in Keyboard.current.Keys.Values) key.isPressed = false;
        foreach (var entry in HotkeyCatalog.Entries) Mod.Settings.GetAction(entry.Name).Pressed = false;
    }

    public static void Main() {
        Metadata();
        ContextualCopy();
        ScopeReuse();
        LodScope();
        FocusAndBusy();
        NestedTools();
        Transitions();
        History();
        Console.WriteLine($"PASS: {s_Checks} hotkey settings and dispatch checks.");
    }

    private static void Metadata() {
        var ui = Setup();
        var attributes = typeof(Setting).GetCustomAttributes<HotkeyActionAttribute>().ToArray();
        Check(attributes.Length == HotkeyCatalog.Entries.Length, "Every action has native usage metadata");
        Check(HotkeyCatalog.Entries.Select(e => e.Name).Distinct().Count() == attributes.Length, "Unique action names");
        foreach (var entry in HotkeyCatalog.Entries) {
            var property = typeof(Setting).GetProperty(entry.Name);
            Check(property != null && property.PropertyType == typeof(ProxyBinding), "Binding property: " + entry.Name);
            Check(attributes.Single(a => a.Name == entry.Name).Scope == entry.Scope, "Usage scope: " + entry.Name);
            var defaults = property.GetCustomAttribute<SettingsUIKeyboardBindingAttribute>();
            bool legacy = new[] { "TogglePO2", "SelectMode", "CreateMode", "ConvertMode", "MaterialsMode", "Undo", "Redo", "RedoAlternate" }.Contains(entry.Name);
            bool newDefault = new[] { "CopyMode", "PasteMode", "DeleteMode", "Help" }.Contains(entry.Name);
            Check(defaults.Key == BindingKeyboard.None || legacy || newDefault, "Optional command starts unassigned: " + entry.Name);

        }
        foreach (var pair in new[] { ("CopyMode", BindingKeyboard.C), ("PasteMode", BindingKeyboard.V), ("DeleteMode", BindingKeyboard.B), ("Help", BindingKeyboard.H) }) {
            var binding = typeof(Setting).GetProperty(pair.Item1).GetCustomAttribute<SettingsUIKeyboardBindingAttribute>();
            Check(binding.Key == pair.Item2 && binding.Ctrl && binding.Shift && !binding.Alt, "Approved mode default: " + pair.Item1);
        }
        var materials = typeof(Setting).GetProperty("MaterialsMode").GetCustomAttribute<SettingsUIKeyboardBindingAttribute>();
        Check(materials.Key == BindingKeyboard.Digit4 && materials.Ctrl && !materials.Shift, "Ctrl+4 preserved");
        for (int i = 0; i < attributes.Length; i++) for (int j = i + 1; j < attributes.Length; j++) {
            bool overlap = (attributes[i].Scope & attributes[j].Scope) != 0;
            Check(attributes[i].Usages.Intersect(attributes[j].Usages).Any() == overlap, "Native warning matches PO2 scope overlap");
        }
        ui.Disable();
        Check(HotkeyCatalog.Entries.All(e => !Mod.Settings.GetAction(e.Name).shouldBeEnabled), "Stop disables all actions");
    }

    private static void ContextualCopy() {
        var ui = Setup();
        ui.Tick(); Press("CopyMode"); ui.Tick();
        Check(ui.Calls.SequenceEqual(new[] { "copySelected" }), "Object mode copies the selection directly");
        Check(ui.m_ProceduralObjectToolSystem.Workspace == Workspace.Transform, "Copy leaves Object mode open");
        Release(); ui.m_ProceduralObjectSystem.SelectedObjectLocked = true; ui.Tick();
        Press("CopyMode"); ui.Tick();
        Check(ui.Calls.Count == 2 && ui.Calls.Last() == "copySelected", "Locked objects can still be copied");

        foreach (Workspace workspace in new[] { Workspace.Select, Workspace.Create, Workspace.Convert, Workspace.MeshEdit,
            Workspace.Texturing, Workspace.Copy, Workspace.Paste, Workspace.Delete, Workspace.SaveManager, Workspace.MaterialLibrary }) {
            ui = Setup(workspace); ui.Tick(); Press("CopyMode"); ui.Tick();
            Check(ui.Calls.SequenceEqual(new[] { "toggleCopyMode" }), "Copy opens its mode outside Object: " + workspace);
        }
        ui = Setup(); ui.m_ProceduralObjectSystem.SelectedObject = null;
        ui.Tick(); Press("CopyMode"); ui.Tick();
        Check(ui.Calls.SequenceEqual(new[] { "toggleCopyMode" }), "No selection opens Copy mode");

        foreach (bool mirror in new[] { false, true }) {
            ui = Setup(); ui.m_ProceduralObjectToolSystem.MirrorMode = mirror; ui.m_ProceduralObjectToolSystem.DeformMode = !mirror;
            ui.Tick(); Press("CopyMode"); ui.Tick();
            Check(ui.Calls.SequenceEqual(new[] { "copySelected" }), "Object submode uses the object-panel copy action");
        }
        foreach (NestedInteraction interaction in Enum.GetValues(typeof(NestedInteraction))) {
            if (interaction == NestedInteraction.None) continue;
            ui = Setup(); ui.m_ProceduralObjectToolSystem.ActiveInteraction = interaction;
            ui.Tick(); Press("CopyMode"); ui.Tick();
            Check(ui.Calls.Count == 0, "Object copy waits for the active step: " + interaction);
            ui.m_ProceduralObjectToolSystem.ActiveInteraction = NestedInteraction.None; ui.Tick();
            Check(ui.Calls.Count == 0, "Completing a step cannot reuse a held Copy press");
        }
        foreach (Action<ProceduralObjectUiSystem> block in new Action<ProceduralObjectUiSystem>[] {
            value => value.m_ProceduralObjectToolSystem.ArrayActive = true,
            value => value.m_ProceduralObjectToolSystem.MeshMutationBusy = true,
            value => value.m_ProceduralObjectToolSystem.Mode = ObjectToolMode.CombineSource,
            value => value.m_ProceduralObjectSystem.HistoryBusy = true,
            value => value.m_ProceduralObjectSystem.ClipboardBusy = true,
            value => value.m_MaterialImportBusy = true,
            value => value.UiFocus(true),
        }) {
            ui = Setup(); block(ui); ui.Tick(); Press("CopyMode"); ui.Tick();
            Check(ui.Calls.Count == 0, "Copy cannot interrupt blocked Object work");
        }
        ui = Setup(); ui.m_MaterialManagementView = MaterialManagementView.Library;
        ui.Tick(); Press("CopyMode"); ui.Tick();
        Check(ui.Calls.SequenceEqual(new[] { "toggleCopyMode" }), "Hidden Object selection is not copied behind Materials");
        ui = Setup(); ui.m_PersistenceCoordinatorSystem.RecoveryResultPending = true;
        ui.Tick(); Press("CopyMode"); ui.Tick();
        Check(ui.Calls.SequenceEqual(new[] { "toggleCopyMode" }), "Recovery does not act on a hidden Object selection");
    }

    private static void ScopeReuse() {
        var ui = Setup();
        Bind("ObjectMove"); Bind("EditSelectAll"); Bind("TextureAll");
        ui.Tick();
        Check(Mod.Settings.GetAction("ObjectMove").shouldBeEnabled, "Object command enabled");
        Check(!Mod.Settings.GetAction("EditSelectAll").shouldBeEnabled, "Edit action actually disabled");
        Press("ObjectMove"); Press("EditSelectAll"); Press("TextureAll"); ui.Tick();
        Check(ui.Calls.SequenceEqual(new[] { "setTranslateMode" }), "Shared key dispatches Object only");
        ui.m_ProceduralObjectToolSystem.Workspace = Workspace.MeshEdit; ui.Tick();
        Check(ui.Calls.Count == 1, "Held key cannot fire after switching to Edit");
        Release(); ui.Tick(); Press("EditSelectAll"); ui.Tick();
        Check(ui.Calls.Last() == "selectAllMeshElements" && ui.Calls.Count == 2, "Released/repressed key dispatches Edit");
        Release(); ui.m_ProceduralObjectToolSystem.Workspace = Workspace.Texturing; ui.Tick();
        Press("TextureAll"); ui.Tick(); Check(ui.Calls.Count == 3, "Same key works in Texturing");
        Release(); ui.m_MaterialManagementView = MaterialManagementView.Library; ui.Tick();
        Press("TextureAll"); ui.Tick(); Check(ui.Calls.Count == 3, "Management view blocks underlying editor");
        ui.m_MaterialManagementView = MaterialManagementView.None;
        ui.m_ProceduralObjectToolSystem.Workspace = Workspace.SaveManager; ui.Tick();
        Check(ui.Scope == HotkeyScope.Management, "Save Manager has no operation scope");
        ui.m_ProceduralObjectToolSystem.Workspace = Workspace.Select; ui.m_PersistenceCoordinatorSystem.RecoveryResultPending = true; ui.Tick();
        Check(ui.Scope == HotkeyScope.Management, "Recovery blocks Select commands");
    }

    private static void FocusAndBusy() {
        var ui = Setup(); Bind("ObjectDelete"); Bind("ObjectLock", "lock"); ui.Tick();
        ui.UiFocus(true); Press("ObjectDelete"); ui.Tick(); Check(ui.Calls.Count == 0, "UI text focus blocks deletion");
        ui.UiFocus(false); ui.Tick(); Check(ui.Calls.Count == 0, "Leaving text focus while held cannot delete");
        Release(); ui.Tick(); ui.m_ProceduralObjectSystem.SelectedObjectLocked = true;
        Press("ObjectDelete"); ui.Tick(); Check(ui.Calls.Count == 0, "Locked PO cannot be deleted");
        Release(); ui.Tick(); Press("ObjectLock"); ui.Tick(); Check(ui.Calls.Last() == "toggleSelectedLocked", "Unlock remains available");
        Release(); ui.m_ProceduralObjectSystem.SelectedObjectLocked = false; ui.m_ProceduralObjectSystem.HistoryBusy = true; ui.Tick();
        Press("ObjectDelete"); ui.Tick(); Check(ui.Calls.Count == 1, "Busy history blocks mutation");
        Release(); ui.m_ProceduralObjectSystem.HistoryBusy = false; ui.Tick();
        InputManager.instance.overlayActive = true; Press("ObjectDelete"); ui.Tick(); Check(ui.Calls.Count == 1, "Game overlay blocks operation");
        InputManager.instance.overlayActive = false; Release(); ui.Tick();
        Mod.Settings.GetAction("ObjectDelete").Blocked = true; Press("ObjectDelete"); ui.Tick();
        Check(ui.Calls.Count == 1, "Native conflict/barrier is respected");
        Mod.Settings.GetAction("ObjectDelete").Blocked = false; ui.Tick();
        Check(ui.Calls.Count == 1, "Removing a native barrier while held cannot delete");
        Release(); ui.Tick();
        Press("ObjectDelete"); ui.Tick(false); Check(ui.Calls.Count == 1, "Game input focus blocks operation");
        Release(); ui.Tick(); Press("ObjectDelete"); ui.Tick(); Check(ui.Calls.Count == 2, "Valid deletion reaches UI command");
        Release(); ui.Tick(); Bind("ObjectDelete", "rebound"); ui.Tick();
        Press("ObjectDelete"); ui.Tick(); Check(ui.Calls.Count == 3, "Rebinding takes effect without restart");
    }

    private static void NestedTools() {
        var ui = Setup(); Bind("ObjectDelete"); Bind("MirrorX"); Bind("ArrayPathCurve"); Bind("Confirm", "confirm");
        ui.m_ProceduralObjectToolSystem.MirrorMode = true; ui.m_ProceduralObjectToolSystem.CanMirrorSelected = true; ui.Tick();
        Press("MirrorX"); Press("ObjectDelete"); ui.Tick();
        Check(ui.Calls.SequenceEqual(new[] { "mirrorSelected:0" }), "Mirror uses integer command, parent delete blocked");
        Release(); ui.m_ProceduralObjectToolSystem.MirrorMode = false; ui.m_ProceduralObjectToolSystem.ArrayActive = true; ui.Tick();
        Press("ArrayPathCurve"); ui.Tick(); Check(ui.Calls.Last() == "setArrayPathKind:1", "Array choice routed to UI command");
        Release(); ui.m_ProceduralObjectToolSystem.ArrayDragging = true; ui.Tick();
        Press("ArrayPathCurve"); ui.Tick(); Check(ui.Calls.Count == 2, "Array drag blocks array settings");
        Release(); ui.m_ProceduralObjectToolSystem.ArrayDragging = false; ui.m_ProceduralObjectToolSystem.CanApplyArray = true; ui.Tick();
        Press("Confirm"); ui.Tick(); Check(ui.Calls.Last() == "applyArray", "Contextual confirm applies array");
        Release(); ui.m_ProceduralObjectToolSystem.CanApplyArray = false; ui.m_ProceduralObjectToolSystem.ArrayActive = false;
        ui.m_ProceduralObjectToolSystem.ActiveInteraction = NestedInteraction.OrientationTarget;
        ui.m_ProceduralObjectToolSystem.CanConfirmAxisPicking = true; ui.Tick(); Press("Confirm"); ui.Tick();
        Check(ui.Calls.Last() == "confirmAxisPicking", "Contextual confirm accepts axis");
        foreach (var interaction in new[] { NestedInteraction.GizmoDrag, NestedInteraction.BoxSelection, NestedInteraction.VertexSlideDrag,
            NestedInteraction.LoopCutTarget, NestedInteraction.LoopCutPosition, NestedInteraction.SurfaceCutTarget,
            NestedInteraction.CombineSourceTarget, NestedInteraction.VertexAlignmentTarget }) {
            Release(); ui.m_ProceduralObjectToolSystem.ActiveInteraction = interaction; ui.Tick();
            Check(!Mod.Settings.GetAction("ObjectDelete").shouldBeEnabled, "Nested operation disables parent action: " + interaction);
        }
    }

    private static void Transitions() {
        var ui = Setup(Workspace.Select); Bind("CreateBox", "C");
        ui.ReplaceCommand("CopyMode", () => { ui.Calls.Add("copy"); ui.m_ProceduralObjectToolSystem.Workspace = Workspace.Create; });
        ui.Tick(); Press("CopyMode"); Press("CreateBox"); ui.Tick(); ui.Tick();
        Check(ui.Calls.SequenceEqual(new[] { "copy" }), "Opening a mode cannot also execute its matching key");
        Release(); ui.Tick(); Press("CreateBox"); ui.Tick(); Check(ui.Calls.Last() == "placeBox", "Next deliberate press works");
        Release(); Bind("CreatePlane", "C"); ui.Tick(); Press("CreateBox"); Press("CreatePlane"); ui.Tick();
        Check(ui.Calls.Count == 3, "Accidental same-scope duplicate runs at most one command");
        Release(); ui.Tick(); Keyboard.current.escapeKey.wasPressedThisFrame = true; Press("CreateBox"); ui.Tick();
        Check(ui.Calls.Count == 3, "Existing cancel handler owns Escape frame");
        Keyboard.current.escapeKey.wasPressedThisFrame = false;
        Release(); ui.m_ProceduralObjectSystem.IsToolActive = false; ui.Tick(); Press("CreateBox"); ui.Tick();
        Check(ui.Calls.Count == 3, "Closed PO2 disables operation bindings");
    }

    private static void LodScope() {
        Check((HotkeyScope.Any & HotkeyScope.Lod) != 0 && (HotkeyScope.Open & HotkeyScope.Lod) != 0 &&
            (HotkeyScope.Editors & HotkeyScope.Lod) == 0 && (HotkeyScope.TransformEditors & HotkeyScope.Lod) == 0,
            "LOD has navigation without inheriting editor mutations");
        var ui = Setup(Workspace.Lod);
        string[] blocked = { "ObjectMove", "EditSelectAll", "TextureAll", "NextPart", "PreviousPart" };
        string[] allowed = { "ObjectWorkspace", "EditWorkspace", "TexturingWorkspace", "Help" };
        foreach (string name in blocked.Concat(allowed)) Bind(name, name);
        ui.m_ProceduralObjectSystem.CanUndoHistory = true;
        ui.Tick();
        foreach (string name in blocked) {
            Check(!Mod.Settings.GetAction(name).shouldBeEnabled, "LOD blocks " + name);
            Press(name);
        }
        ui.Tick(); Check(ui.Calls.Count == 0, "Editor actions cannot mutate a LOD preview");
        Release(); ui.Tick();
        foreach (string name in allowed.Concat(new[] { "Undo", "TogglePO2" })) {
            Check(Mod.Settings.GetAction(name).shouldBeEnabled, "LOD keeps " + name);
        }
        Press("ObjectWorkspace"); ui.Tick();
        Check(ui.Calls.Last() == "navigateTransformWorkspace", "Object workspace hotkey leaves LOD");
    }

    private static void History() {
        var ui = Setup(); ui.m_ProceduralObjectSystem.CanUndoHistory = true; ui.m_ProceduralObjectSystem.CanRedoHistory = true;
        ui.Tick(); Press("Undo"); ui.Tick(); Check(ui.Calls.SequenceEqual(new[] { "undo" }), "Existing undo default dispatches once");
        ui.Tick(); Check(ui.Calls.Count == 1, "Held undo does not repeat");
        Release(); ui.Tick(); Press("Redo"); ui.Tick(); Check(ui.Calls.Last() == "redo", "Existing redo default preserved");
        Release(); ui.Tick(); Press("RedoAlternate"); ui.Tick(); Check(ui.Calls.Count == 3 && ui.Calls.Last() == "redo", "Alternate redo preserved");
        Release(); ui.m_ProceduralObjectSystem.CanRedoHistory = false; ui.Tick(); Press("Redo"); ui.Tick();
        Check(ui.Calls.Count == 3, "Unavailable redo does nothing");
    }
}
