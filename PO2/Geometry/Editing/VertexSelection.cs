using System.Collections.Generic;
using PO2.Geometry.Topology;

namespace PO2.Geometry.Editing
{
    // Tiny wrapper, but having every mutation report whether it changed
    // anything keeps cache invalidation sane in the controller.
    public sealed class VertexSelection
    {
        private HashSet<VertexRef> m_Selected = new HashSet<VertexRef>();

        internal void AdoptPrepared(HashSet<VertexRef> selection)
        {
            m_Selected = selection;
        }

        public int Count => m_Selected.Count;

        public IEnumerable<VertexRef> Vertices =>
            m_Selected;

        public bool Contains(VertexRef vertex)
        {
            return m_Selected.Contains(vertex);
        }

        public bool Add(VertexRef vertex)
        {
            return m_Selected.Add(vertex);
        }

        public bool Remove(VertexRef vertex)
        {
            return m_Selected.Remove(vertex);
        }

        public bool Clear()
        {
            if (m_Selected.Count == 0)
            {
                return false;
            }

            m_Selected.Clear();
            return true;
        }

        public bool SelectOnly(VertexRef vertex)
        {
            if (m_Selected.Count == 1 && m_Selected.Contains(vertex))
            {
                return false;
            }

            m_Selected.Clear();
            m_Selected.Add(vertex);
            return true;
        }

        public bool Toggle(VertexRef vertex)
        {
            // Toggle always changes the set, unlike Add/Remove/SelectOnly.
            if (!m_Selected.Remove(vertex))
            {
                m_Selected.Add(vertex);
            }

            return true;
        }
    }
}