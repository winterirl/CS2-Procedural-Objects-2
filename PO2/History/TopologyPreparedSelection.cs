using System;
using System.Collections.Generic;
using System.Threading;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.History
{
    // Build selection sets from the detached candidate, then pass them to the controller
    // at publication so a large selection does not need rebuilding on the main thread.
    internal sealed class TopologyPreparedSelection
    {
        internal readonly TopologySelection Selection;
        internal readonly HistoryPartKey Part;
        internal readonly int PartIndex;
        internal readonly UnityEngine.Vector3 LocalCentroid;
        internal readonly bool HasCentroid;
        internal readonly HashSet<VertexRef> Vertices = new HashSet<VertexRef>();
        internal readonly HashSet<StructuralEdgeRef> Edges = new HashSet<StructuralEdgeRef>();
        internal readonly HashSet<VertexRef> Endpoints = new HashSet<VertexRef>();

        internal TopologyPreparedSelection(TopologySelection selection, HistoryPartKey key, int index, MeshPart candidate, CancellationToken token)
        {
            Selection = selection;
            Part = key;
            PartIndex = index;
            if (selection == null || (selection.Mode != 0 && selection.Mode != 1))
                throw new InvalidOperationException("Unsupported prepared topology selection.");
            for (int i = 0; i < selection.Items.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                var item = selection.Items[i];
                if (!item.Part.Owner.Equals(key.Owner) || item.Part.PartId != key.PartId || item.Element < 0)
                    throw new InvalidOperationException("The prepared selection belongs to another part.");
                if (item.Kind == 0 && selection.Mode == 0 && item.Element < candidate.Topology.LogicalVertexCount)
                    Vertices.Add(new VertexRef(index, item.Element));
                else if (item.Kind == 1 && selection.Mode == 1 && item.Element < candidate.StructuralTopology.StructuralEdgeCount)
                {
                    var edge = candidate.StructuralTopology.GetEdge(item.Element);
                    Edges.Add(new StructuralEdgeRef(index, item.Element));
                    Endpoints.Add(new VertexRef(index, edge.LogicalVertexA));
                    Endpoints.Add(new VertexRef(index, edge.LogicalVertexB));
                }
                else throw new InvalidOperationException("The prepared topology selection is invalid.");
            }

            var effective = selection.Mode == 1 ? Endpoints : Vertices;
            double x = 0, y = 0, z = 0;
            int count = 0;
            foreach (var vertex in effective)
            {
                if ((count & 4095) == 0) token.ThrowIfCancellationRequested();
                var position = candidate.Topology.GetVertex(vertex.VertexId).GetPosition(candidate.Mesh);
                x += position.x;
                y += position.y;
                z += position.z;
                count++;
            }

            HasCentroid = count > 0;
            if (HasCentroid) LocalCentroid = new UnityEngine.Vector3((float)(x / count), (float)(y / count), (float)(z / count));
        }
    }
}