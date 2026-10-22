using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Colossal.Localization;
using PO2;
using PO2.Localization;

internal static partial class Program {
    private static int s_Checks;
    private static void Check(bool value, string message) {
        s_Checks++;
        if (!value) throw new Exception(message);
    }
    private static string Resource(string name) {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("PO2.L10n." + name + ".json");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static void Main() {
        RuntimeTextTests();
        NumberFormatTests();
        var english = LocaleCatalog.ReadEnglish(JsonSerializer.Serialize(new Dictionary<string, string> {
            ["PO2.Common.Cancel"] = "fallback action",
            ["PO2.Common.Apply"] = "fallback apply",
            ["PO2.Settings.Hotkey.TogglePanel.Label"] = "fallback setting",
            ["PO2.Settings.Hotkey.TogglePanel.Description"] = "fallback description",
            ["PO2.Edit.Cut.Count.Summary"] = "fallback {count}",
            ["PO2.SaveManager.Results.Summary"] = "{shownCount} {shownSize} {totalCount} {totalSize}",
            ["PO2.Test.Omitted"] = "fallback omitted"
        }));
        var original = new Dictionary<string, string>(english);
        var warnings = new List<string>();
        var french = LocaleCatalog.Overlay(english, Resource("fr-FR"), warnings.Add);
        Check(french.Count == english.Count, "Partial translation retains the complete catalog");
        Check(french["PO2.Common.Cancel"] == "Annuler", "Translated action");
        Check(french["PO2.Settings.Hotkey.TogglePanel.Label"] == "Ouvrir / Fermer PO2", "Translated setting");
        Check(french["PO2.Edit.Cut.Count.Summary"] == "Coupes : {count}", "Named placeholder preserved");
        foreach (var entry in english) {
            if (!new[] { "PO2.Common.Cancel", "PO2.Common.Apply", "PO2.Settings.Hotkey.TogglePanel.Label",
                "PO2.Settings.Hotkey.TogglePanel.Description", "PO2.Edit.Cut.Count.Summary" }.Contains(entry.Key))
                Check(french[entry.Key] == entry.Value, "Every omitted entry uses English: " + entry.Key);
        }
        foreach (object invalid in new object[] { null, 4, false, new[] { "bad" }, new { bad = true }, "", "  \t", "{missing}", "Unclosed {count" }) {
            warnings.Clear();
            var json = JsonSerializer.Serialize(new Dictionary<string, object> {
                ["PO2.Common.Cancel"] = "Annuler", ["PO2.Edit.Cut.Count.Summary"] = invalid
            });
            var result = LocaleCatalog.Overlay(english, json, warnings.Add);
            Check(result["PO2.Common.Cancel"] == "Annuler", "One invalid entry cannot discard valid entries");
            Check(result["PO2.Edit.Cut.Count.Summary"] == english["PO2.Edit.Cut.Count.Summary"], "Invalid entry falls back");
        }
        foreach (string json in new[] { null, "not JSON", "[]", "null", "{", "{\"PO2.Common.Cancel\":\"A\",\"PO2.Common.Cancel\":\"B\"}" }) {
            warnings.Clear();
            var result = LocaleCatalog.Overlay(english, json, warnings.Add);
            Check(result.Count == english.Count && result.All(pair => english[pair.Key] == pair.Value), "Missing/malformed file uses English");
        }
        warnings.Clear();
        var reordered = LocaleCatalog.Overlay(english,
            "{\"PO2.SaveManager.Results.Summary\":\"{totalCount} {totalSize} / {shownCount} {shownSize}\",\"PO2.Typo\":\"ignored\"}", warnings.Add);
        Check(reordered["PO2.SaveManager.Results.Summary"].StartsWith("{totalCount}"), "Translations may reorder arguments");
        Check(!reordered.ContainsKey("PO2.Typo"), "Unknown keys cannot enter the translated catalog");
        Check(english.Count == original.Count && original.All(pair => english[pair.Key] == pair.Value), "Overlay never modifies English");
        foreach (string invalid in new[] { "{}", "[]", "{\"PO2.Bad\":null}", "{\"PO2.Bad\":\"\"}", "{\"PO2.Bad\":\"{broken\"}" }) {
            bool failed = false;
            try { LocaleCatalog.ReadEnglish(invalid); } catch { failed = true; }
            Check(failed, "Invalid English is rejected");
        }
        english = LocaleCatalog.ReadEnglish(Resource("en-US"));
        var manager = new LocalizationManager();
        var unrelated = new MemorySource(new Dictionary<string, string> { ["OtherMod.Label"] = "Other" });
        manager.AddSource("fr-FR", unrelated);
        using (var registration = new LocalizationService(manager, new Setting())) {
            var fr = (MemorySource)manager.Sources["fr-FR"].Last();
            Check(fr.Entries["label:TogglePO2"] == "Ouvrir / Fermer PO2", "Settings alias uses the locale overlay");
            Check(fr.Entries["description:LOD1Distance"] == english["PO2.Settings.LOD1Distance.Description"], "Missing settings description uses English");
            var hotkeyEntries = PO2.Input.HotkeyCatalog.Entries.Where(entry => entry.HasSettingsEntry).ToArray();
            foreach (var hotkey in hotkeyEntries) {
                Check(fr.Entries["label:" + hotkey.Name] == english[hotkey.LabelKey], "Hotkey label falls back to English: " + hotkey.Name);
                Check(fr.Entries["description:" + hotkey.Name] == english[hotkey.DescriptionKey], "Hotkey description falls back to English: " + hotkey.Name);
            }
            var de = (MemorySource)manager.Sources["de-DE"].Single();
            Check(de.Entries["PO2.Common.Cancel"] == english["PO2.Common.Cancel"], "Unsupported translation uses English");
            int calls = manager.AddCount;
            manager.Notify(); manager.Notify();
            Check(manager.AddCount == calls, "Notifications do not duplicate sources");
            manager.Locales.Add("ja-JP"); manager.Notify();
            Check(manager.Sources["ja-JP"].Count == 1, "Later supported locales receive a source");
            Check(((MemorySource)manager.Sources["ja-JP"].Single()).Entries["PO2.Common.Cancel"] == english["PO2.Common.Cancel"], "Later locale uses English");
        }
        Check(manager.Sources["fr-FR"].Single() == unrelated, "Disposal preserves unrelated sources");
        Check(manager.Sources.Where(pair => pair.Key != "fr-FR").All(pair => pair.Value.Count == 0), "Disposal removes PO2 sources");
        int afterDispose = manager.AddCount;
        manager.Locales.Add("ko-KR"); manager.Notify();
        Check(manager.AddCount == afterDispose, "Disposal detaches the supported-locale event");
        Console.WriteLine($"PASS: {s_Checks} localization fallback, settings, and lifecycle checks.");
    }
}
