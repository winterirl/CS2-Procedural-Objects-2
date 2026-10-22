using System;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Rendering;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        internal ITopologyPublication StagePartAppend(ProceduralObject obj, int index, MeshPart part, ITopologyRenderData data, int finalCount)
        {
            if (!(data is TopologyRendererPreparation prepared) || !m_Instances.TryGetValue(obj.Id, out var instance) || !ReferenceEquals(instance.Source, obj) || instance.Parts.Count != obj.Parts.Count || index < obj.Parts.Count)
                throw new InvalidOperationException("The renderer changed while preparing the array.");
            if (instance.Parts.Capacity < finalCount) instance.Parts.Capacity = finalCount;
            return new TopologyPublication(this, instance, obj, index, part, prepared, true);
        }

        internal bool CanDetachPartBatch(ProceduralObject obj, int first, Guid[] ids)
        {
            if (!m_Instances.TryGetValue(obj.Id, out var instance) || !ReferenceEquals(instance.Source, obj) || first < 1 || first + ids.Length != instance.Parts.Count) return false;
            for (int i = 0; i < ids.Length; i++)
                if (instance.Parts[first + i].PickSource.PartHistoryId != ids[i])
                    return false;
            // Reserve retirement slots before touching the model.
            int count = m_RetiredTopologyParts.Count + ids.Length;
            if (m_RetiredTopologyParts.Capacity < count) m_RetiredTopologyParts.Capacity = count;
            return true;
        }

        internal IArrayRemoval StagePartRemoval(ProceduralObject obj, int first, Guid[] ids)
        {
            if (!CanDetachPartBatch(obj, first, ids)) throw new InvalidOperationException("The array renderer changed before undo.");
            return new ArrayRemoval(this, m_Instances[obj.Id], first, ids.Length);
        }

        private sealed class ArrayRemoval : IArrayRemoval
        {
            private readonly ProceduralObjectRenderer m_Owner;
            private readonly RenderInstance m_Instance;
            private readonly int m_First;
            private RenderPart[] m_Parts;
            private bool m_Detached;

            internal ArrayRemoval(ProceduralObjectRenderer owner, RenderInstance instance, int first, int count)
            {
                m_Owner = owner;
                m_Instance = instance;
                m_First = first;
                m_Parts = instance.Parts.GetRange(first, count).ToArray();
            }

            public void Adopt()
            {
                m_Instance.Parts.RemoveRange(m_First, m_Parts.Length);
                m_Detached = true;
                foreach (var part in m_Parts) part.GameObject.SetActive(false);
                m_Owner.m_PickSceneVersion++;
            }

            public void Restore()
            {
                if (!m_Detached) return;
                m_Instance.Parts.AddRange(m_Parts);
                m_Detached = false;
                foreach (var part in m_Parts) part.GameObject.SetActive(true);
                m_Owner.m_PickSceneVersion++;
            }

            public void Dispose()
            {
                if (m_Parts == null) return;
                if (m_Detached) m_Owner.m_RetiredTopologyParts.AddRange(m_Parts);
                m_Parts = null;
            }
        }
    }
}