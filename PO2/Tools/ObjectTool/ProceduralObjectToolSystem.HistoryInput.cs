using Game.Input;
using UnityEngine.InputSystem;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private int m_HistoryCancelFrame = -1;

        internal bool ProcessHistoryInput()
        {
            // A later update in this frame must not also back out of the tool.
            if (m_HistoryCancelFrame == UnityEngine.Time.frameCount) return true;
            if (!m_ProceduralObjectSystem.HistoryBusy) return false;

            bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !InputManager.instance.hasInputFieldFocus;
            bool rightClick = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            if (escape || rightClick)
            {
                m_ProceduralObjectSystem.CancelHistoryReplay();
                m_HistoryCancelFrame = UnityEngine.Time.frameCount;
            }

            return true;
        }
    }
}