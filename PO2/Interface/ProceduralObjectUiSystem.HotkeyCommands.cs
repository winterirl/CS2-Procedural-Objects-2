using System;
using System.Collections.Generic;
using Colossal.UI.Binding;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private readonly Dictionary<string, Action> m_HotkeyCommands = new Dictionary<string, Action>();
        private readonly Dictionary<string, Action<int>> m_IntegerHotkeyCommands = new Dictionary<string, Action<int>>();

        private TriggerBinding CommandBinding(string name, Action action)
        {
            m_HotkeyCommands.Add(name, action);
            return new TriggerBinding(kGroup, name, action);
        }

        private TriggerBinding<int> CommandBinding(string name, Action<int> action)
        {
            m_IntegerHotkeyCommands.Add(name, action);
            return new TriggerBinding<int>(kGroup, name, action);
        }

        private Action GetHotkeyCommand(string name) => m_HotkeyCommands[name];

        private Action GetHotkeyCommand(string name, int value)
        {
            Action<int> action = m_IntegerHotkeyCommands[name];
            return () => action(value);
        }
    }
}