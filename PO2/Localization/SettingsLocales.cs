using System.Collections.Generic;
using PO2.Persistence.Format;
using PO2.Input;

namespace PO2.Localization
{
    internal static class SettingsLocales
    {
        internal static void AddAliases(Dictionary<string, string> entries, Setting setting)
        {
            entries[setting.GetOptionTabLocaleID("Hotkeys")] = entries["PO2.Settings.Tab.Hotkeys"];
            foreach (HotkeyDefinition hotkey in HotkeyCatalog.Entries)
            {
                if (!hotkey.HasSettingsEntry) continue;
                string label = entries[hotkey.LabelKey];
                string description = entries[hotkey.DescriptionKey];
                entries[setting.GetOptionGroupLocaleID(hotkey.Group)] = entries[hotkey.GroupKey];
                entries[setting.GetOptionLabelLocaleID(hotkey.Name)] = label;
                entries[setting.GetOptionDescLocaleID(hotkey.Name)] = description;
                entries[setting.GetBindingKeyLocaleID(hotkey.Name)] = label;
                entries[setting.GetBindingKeyHintLocaleID(hotkey.Name)] = description;
            }

            entries[setting.GetSettingsLocaleID()] = entries["PO2.Settings.Title"];
            entries[setting.GetOptionTabLocaleID(Setting.kSection)] = entries["PO2.Settings.Tab.Main"];
            entries[setting.GetOptionGroupLocaleID(Setting.kControlsGroup)] = entries["PO2.Settings.Group.Controls"];
            entries[setting.GetOptionGroupLocaleID(Setting.kMainModeHotkeysGroup)] = entries["PO2.Settings.Group.ModeHotkeys"];
            entries[setting.GetOptionLabelLocaleID(nameof(Setting.SelectMode))] = entries["PO2.Settings.Hotkey.SelectMode.Label"];
            entries[setting.GetOptionDescLocaleID(nameof(Setting.SelectMode))] = entries["PO2.Settings.Hotkey.SelectMode.Description"];
            entries[setting.GetBindingKeyLocaleID(nameof(Setting.SelectMode))] = entries["PO2.Settings.Hotkey.SelectMode.BindingLabel"];
            entries[setting.GetBindingKeyHintLocaleID(nameof(Setting.SelectMode))] = entries["PO2.Settings.Hotkey.SelectMode.BindingHint"];
            entries[setting.GetOptionLabelLocaleID(nameof(Setting.CreateMode))] = entries["PO2.Settings.Hotkey.CreateMode.Label"];
            entries[setting.GetOptionDescLocaleID(nameof(Setting.CreateMode))] = entries["PO2.Settings.Hotkey.CreateMode.Description"];
            entries[setting.GetBindingKeyLocaleID(nameof(Setting.CreateMode))] = entries["PO2.Settings.Hotkey.CreateMode.BindingLabel"];
            entries[setting.GetBindingKeyHintLocaleID(nameof(Setting.CreateMode))] = entries["PO2.Settings.Hotkey.CreateMode.BindingHint"];
            entries[setting.GetOptionLabelLocaleID(nameof(Setting.ConvertMode))] = entries["PO2.Settings.Hotkey.ConvertMode.Label"];
            entries[setting.GetOptionDescLocaleID(nameof(Setting.ConvertMode))] = entries["PO2.Settings.Hotkey.ConvertMode.Description"];
            entries[setting.GetBindingKeyLocaleID(nameof(Setting.ConvertMode))] = entries["PO2.Settings.Hotkey.ConvertMode.BindingLabel"];
            entries[setting.GetBindingKeyHintLocaleID(nameof(Setting.ConvertMode))] = entries["PO2.Settings.Hotkey.ConvertMode.BindingHint"];
            entries[setting.GetOptionLabelLocaleID(nameof(Setting.MaterialsMode))] = entries["PO2.Settings.Hotkey.MaterialsMode.Label"];
            entries[setting.GetOptionDescLocaleID(nameof(Setting.MaterialsMode))] = entries["PO2.Settings.Hotkey.MaterialsMode.Description"];
            entries[setting.GetBindingKeyLocaleID(nameof(Setting.MaterialsMode))] = entries["PO2.Settings.Hotkey.MaterialsMode.BindingLabel"];
            entries[setting.GetBindingKeyHintLocaleID(nameof(Setting.MaterialsMode))] = entries["PO2.Settings.Hotkey.MaterialsMode.BindingHint"];
            entries[setting.GetOptionGroupLocaleID(Setting.kSavingGroup)] = entries["PO2.Settings.Group.Saving"];
            entries[setting.GetOptionLabelLocaleID(nameof(Setting.SaveCompression))] = entries["PO2.Settings.SaveCompression.Label"];
            entries[setting.GetOptionDescLocaleID(nameof(Setting.SaveCompression))] = entries["PO2.Settings.SaveCompression.Description"];
            entries[setting.GetOptionLabelLocaleID(nameof(Setting.CleanupOrphanedAutosaves))] = entries["PO2.Settings.AutosaveCleanup.Label"];
            entries[setting.GetOptionDescLocaleID(nameof(Setting.CleanupOrphanedAutosaves))] = entries["PO2.Settings.AutosaveCleanup.Description"];
            entries[setting.GetOptionLabelLocaleID(nameof(Setting.RetainedOrphanedAutosaves))] = entries["PO2.Settings.AutosaveRetention.Label"];
            entries[setting.GetOptionDescLocaleID(nameof(Setting.RetainedOrphanedAutosaves))] = entries["PO2.Settings.AutosaveRetention.Description"];
            entries[setting.GetEnumValueLocaleID(SaveCompression.Compact)] = entries["PO2.Settings.SaveCompression.Choice.Compact"];
            entries[setting.GetEnumValueLocaleID(SaveCompression.Fast)] = entries["PO2.Settings.SaveCompression.Choice.Fast"];
            entries[setting.GetOptionGroupLocaleID(Setting.kHistoryGroup)] = entries["PO2.Settings.Group.History"];
            entries[setting.GetOptionLabelLocaleID(nameof(Setting.UndoHistorySteps))] = entries["PO2.Settings.UndoHistorySteps.Label"];
            entries[setting.GetOptionDescLocaleID(nameof(Setting.UndoHistorySteps))] = entries["PO2.Settings.UndoHistorySteps.Description"];
            entries[setting.GetOptionLabelLocaleID(nameof(Setting.TogglePO2))] = entries["PO2.Settings.Hotkey.TogglePanel.Label"];
            entries[setting.GetOptionDescLocaleID(nameof(Setting.TogglePO2))] = entries["PO2.Settings.Hotkey.TogglePanel.Description"];
            entries[setting.GetBindingMapLocaleID()] = entries["PO2.Settings.KeyBindings.Title"];
            entries[setting.GetBindingKeyLocaleID(nameof(Setting.TogglePO2))] = entries["PO2.Settings.Hotkey.TogglePanel.BindingLabel"];
            entries[setting.GetBindingKeyHintLocaleID(nameof(Setting.TogglePO2))] = entries["PO2.Settings.Hotkey.TogglePanel.BindingHint"];
            entries[setting.GetOptionGroupLocaleID(Setting.kLodGroup)] = entries["PO2.Settings.Group.LevelOfDetail"];
            entries[setting.GetOptionLabelLocaleID(nameof(Setting.LOD1Distance))] = entries["PO2.Settings.LOD1Distance.Label"];
            entries[setting.GetOptionDescLocaleID(nameof(Setting.LOD1Distance))] = entries["PO2.Settings.LOD1Distance.Description"];
            entries[setting.GetOptionLabelLocaleID(nameof(Setting.LOD2Distance))] = entries["PO2.Settings.LOD2Distance.Label"];
            entries[setting.GetOptionDescLocaleID(nameof(Setting.LOD2Distance))] = entries["PO2.Settings.LOD2Distance.Description"];
        }
    }
}
