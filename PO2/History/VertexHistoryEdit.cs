using System;
using System.Collections.Generic;
using PO2.Core;
using PO2.Geometry;
using PO2.History.Actions;

namespace PO2.History
{
    internal sealed class VertexHistoryEdit : IDisposable
    {
        private readonly HistoryEdit m_Edit;
        private readonly Dictionary<int, VertexPatchAction> m_Parts;

        internal VertexHistoryEdit(HistoryEdit edit, Dictionary<int, VertexPatchAction> parts)
        {
            m_Edit = edit;
            m_Parts = parts;
        }

        internal bool PreviewFailed { get; private set; }

        internal bool Recalculate(ProceduralObject obj, int partIndex)
        {
            if (PreviewFailed) return false;
            if (m_Parts[partIndex].Recalculate(obj.Parts[partIndex])) return true;
            PreviewFailed = true;
            // This also restores earlier parts when a later part fails.
            m_Edit.Dispose();
            return false;
        }

        internal GeometryChange Change(ProceduralObject obj, int partIndex, uint before) =>
            m_Parts[partIndex].Change(obj.Parts[partIndex], partIndex, before);

        internal bool HasFiniteNormals(ProceduralObject obj)
        {
            foreach (var pair in m_Parts)
            {
                if (!pair.Value.HasFiniteNormals(obj.Parts[pair.Key])) return false;
            }

            return true;
        }

        internal bool Complete()
        {
            return m_Edit.Complete();
        }

        public void Dispose()
        {
            m_Edit.Dispose();
        }
    }
}