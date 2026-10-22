using System;
using System.Collections.Generic;
using Game.Settings;

namespace PO2.Input
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    internal sealed class HotkeyActionAttribute : SettingsUIKeyboardActionAttribute
    {
        internal HotkeyScope Scope { get; }

        public HotkeyActionAttribute(string name, HotkeyScope scope) : base(name, GetUsages(scope))
        {
            Scope = scope;
        }

        private static string[] GetUsages(HotkeyScope scope)
        {
            var usages = new List<string>();
            foreach (HotkeyScope value in Enum.GetValues(typeof(HotkeyScope)))
            {
                int bit = (int)value;
                if (bit != 0 && (bit & (bit - 1)) == 0 && (scope & value) != 0)
                    usages.Add("PO2.Hotkeys." + value);
            }

            // Broad hotkeys also keep the game's normal conflict warnings.
            // Mode-only usages let mutually exclusive PO2 commands share a key.
            if ((scope & HotkeyScope.Open) == HotkeyScope.Open)
            {
                usages.AddRange(new[] { "DefaultTool", "Overlay", "Tool", "CancelableTool", "DiscardableTool" });
            }

            return usages.ToArray();
        }
    }
}