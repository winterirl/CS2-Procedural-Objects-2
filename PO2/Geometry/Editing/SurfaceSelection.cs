using System.Collections.Generic;
using PO2.Geometry.Topology;

namespace PO2.Geometry.Editing
{
    // This stores triangle refs.
    // Surface Area mode can then represent imported/non-structural meshes without inventing fake faces.
    public sealed class SurfaceSelection
    {
        private readonly HashSet<SurfaceRef> m_Selected = new HashSet<SurfaceRef>();

        public int Count => m_Selected.Count;
        public IEnumerable<SurfaceRef> Surfaces => m_Selected;

        public bool Contains(SurfaceRef surface) => m_Selected.Contains(surface);
        public bool Add(SurfaceRef surface) => m_Selected.Add(surface);
        public bool Remove(SurfaceRef surface) => m_Selected.Remove(surface);

        public bool Clear()
        {
            if (m_Selected.Count == 0)
            {
                return false;
            }

            m_Selected.Clear();
            return true;
        }
    }
}