using UnityEngine;

namespace PO2.Geometry.Picking
{
    // Preserve the clicked ray while its mesh cache is prepared. A newer click
    // replaces it; leaving the mode or changing the scene cancels it.
    internal sealed class PendingObjectPick
    {
        internal bool HasClick { get; private set; }
        internal Ray Ray { get; private set; }
        private int m_Mode;
        private int m_SelectedId;
        private uint m_SceneVersion;

        internal void Set(Ray ray, int mode, int selectedId, uint sceneVersion)
        {
            Ray = ray;
            m_Mode = mode;
            m_SelectedId = selectedId;
            m_SceneVersion = sceneVersion;
            HasClick = true;
        }

        internal void Validate(int mode, int selectedId, uint sceneVersion)
        {
            if (mode != m_Mode || selectedId != m_SelectedId || sceneVersion != m_SceneVersion) Clear();
        }

        internal void Clear()
        {
            HasClick = false;
        }
    }
}