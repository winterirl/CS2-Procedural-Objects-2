using UnityEngine;

namespace PO2.Tools.Snapping
{
    // Readiness is not input. A cold result needs a later pointer move or an
    // explicit activation before it may change the geometry.
    internal sealed class MeshTranslationSnapInputGate
    {
        private Vector2 m_Pointer;
        private bool m_Requested;
        private long m_ReadySequence;
        internal long Sequence { get; private set; }
        internal bool Ready { get; private set; }
        internal bool Armed { get; private set; }

        internal void Begin(Vector2 pointer, bool requested)
        {
            m_Pointer = pointer;
            m_Requested = requested;
            Sequence = 0;
            Ready = Armed = false;
            m_ReadySequence = 0;
        }

        internal void Observe(Vector2 pointer, bool requested)
        {
            if (!pointer.Equals(m_Pointer) || (requested && !m_Requested)) Sequence++;
            m_Pointer = pointer;
            m_Requested = requested;
            if (Ready && Sequence > m_ReadySequence) Armed = true;
        }

        internal void Adopt(bool warm)
        {
            Ready = true;
            Armed = warm;
            m_ReadySequence = Sequence;
        }

        internal void Reset()
        {
            Ready = Armed = false;
            Sequence = 0;
        }
    }
}