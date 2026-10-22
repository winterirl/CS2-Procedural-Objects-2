using System.Collections.Generic;
using PO2.Geometry.Topology;

namespace PO2.Geometry.Editing
{
    // Kept separate from the effective vertex selection.
    // Selected edges are what the UI means ~ their endpoints are just what move commands consume.
    public sealed class StructuralEdgeSelection
    {
        private HashSet<StructuralEdgeRef> m_Selected = new HashSet<StructuralEdgeRef>();

        internal void AdoptPrepared(HashSet<StructuralEdgeRef> selection)
        {
            m_Selected = selection;
        }

        public int Count => m_Selected.Count;

        public IEnumerable<StructuralEdgeRef> Edges =>
            m_Selected;

        public bool Contains(StructuralEdgeRef edge)
        {
            return m_Selected.Contains(edge);
        }

        public bool Add(StructuralEdgeRef edge)
        {
            return m_Selected.Add(edge);
        }

        public bool Remove(StructuralEdgeRef edge)
        {
            return m_Selected.Remove(edge);
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

        public bool SelectOnly(StructuralEdgeRef edge)
        {
            if (m_Selected.Count == 1 && m_Selected.Contains(edge))
            {
                return false;
            }

            m_Selected.Clear();
            m_Selected.Add(edge);
            return true;
        }

        public bool Toggle(StructuralEdgeRef edge)
        {
            if (!m_Selected.Remove(edge))
            {
                m_Selected.Add(edge);
            }

            return true;
        }
    }
}