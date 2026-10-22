namespace PO2.Input
{
    // Re-enabling an action while its key is held must not count as a new press.
    internal struct HotkeyPressState
    {
        private bool m_Eligible;
        private bool m_WaitForRelease;

        internal bool Update(bool eligible, bool keyHeld, bool pressed)
        {
            if (!eligible)
            {
                m_Eligible = false;
                m_WaitForRelease = keyHeld;
                return false;
            }

            if (!m_Eligible) m_WaitForRelease = keyHeld;
            m_Eligible = true;
            if (!keyHeld) m_WaitForRelease = false;
            return pressed && !m_WaitForRelease;
        }

        internal void Consume(bool keyHeld)
        {
            if (keyHeld) m_WaitForRelease = true;
        }
    }
}