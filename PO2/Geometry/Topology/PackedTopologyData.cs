using System;
using PO2.Persistence.Model;

namespace PO2.Geometry.Topology
{
    // Arrays belong to one restored part. The encoded copy can be shared separately.
    internal sealed class PackedTopologyData
    {
        internal readonly int[][] Sections = new int[22][];
        internal byte[] Forward;
        internal int RenderCount, LogicalCount, TriangleCount, FaceCount, EdgeCount, StructuralEdgeCount;
        internal long BoundaryCount;
        internal int[] this[int id] { get => Sections[id - 1]; set => Sections[id - 1] = value; }
    }

    internal sealed class ValidatedPartTopology
    {
        internal readonly PersistencePartData Source;
        internal readonly PackedTopologyData Data;
        internal readonly bool CanonicalMaps;
        internal ValidatedPartTopology(PersistencePartData source, PackedTopologyData data, bool canonicalMaps)
        {
            Source = source;
            Data = data;
            CanonicalMaps = canonicalMaps;
        }
    }
}
