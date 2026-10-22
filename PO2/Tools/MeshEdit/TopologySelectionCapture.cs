using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    internal readonly struct TopologySelectionElement
    {
        internal readonly int Part, Id, A, B;

        internal TopologySelectionElement(int part, int id, int a = -1, int b = -1)
        {
            Part = part;
            Id = id;
            A = a;
            B = b;
        }
    }

    // Enumerators and live structural edges are only read by the owner thread.
    internal sealed class TopologySelectionCapture : IDisposable
    {
        internal readonly BudgetedCopy Copy = new BudgetedCopy();
        private readonly BudgetedCopy.Stream<TopologySelectionElement> m_Items;
        private readonly IDisposable m_Enumerator;
        internal TopologySelectionElement[] Items => m_Items.Data;
        internal Task Drain => Copy.Allocation;

        internal TopologySelectionCapture(ProceduralObject obj, int count, IEnumerable<StructuralEdgeRef> edges)
        {
            var iterator = edges.GetEnumerator();
            m_Enumerator = iterator;
            m_Items = Copy.Add(count, 16, i =>
            {
                if (!iterator.MoveNext()) throw new InvalidOperationException("The edge selection changed.");
                var item = iterator.Current;
                if (item.PartIndex < 0 || item.PartIndex >= obj.Parts.Count) throw new InvalidOperationException("Invalid selection part.");
                var topology = obj.Parts[item.PartIndex].StructuralTopology;
                if (item.EdgeId < 0 || item.EdgeId >= topology.StructuralEdgeCount) throw new InvalidOperationException("The edge selection is no longer current.");
                var edge = topology.GetEdge(item.EdgeId);
                return new TopologySelectionElement(item.PartIndex, item.EdgeId, edge.LogicalVertexA, edge.LogicalVertexB);
            });
        }

        internal TopologySelectionCapture(int count, IEnumerable<VertexRef> vertices)
        {
            var iterator = vertices.GetEnumerator();
            m_Enumerator = iterator;
            m_Items = Copy.Add(count, 16, i =>
            {
                if (!iterator.MoveNext()) throw new InvalidOperationException("The vertex selection changed.");
                var item = iterator.Current;
                return new TopologySelectionElement(item.PartIndex, item.VertexId);
            });
        }

        internal void Start(CancellationToken token) => Copy.Start(token);
        internal bool Poll(Func<bool> current, CancellationToken token) => Copy.Poll(current, token, 64 * 1024);
        public void Dispose() => m_Enumerator.Dispose();
    }
}