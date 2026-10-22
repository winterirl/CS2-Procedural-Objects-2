using System.Collections.Generic;

namespace PO2.Geometry
{
    // Borrowed indices are consumed during the notification. The renderer
    // copies the dirty indices; it never owns history's arrays. Precise producers
    // cover position/normal edits only: source tangents and auxiliary attributes
    // are unchanged. Other edits must use broad version invalidation.
    internal readonly struct GeometryChange
    {
        internal readonly MeshPart Part;
        internal readonly int PartIndex;
        internal readonly uint Before, After, Topology;
        internal readonly IReadOnlyList<int> Positions, Normals;

        internal GeometryChange(MeshPart part, int partIndex, uint before, IReadOnlyList<int> positions, IReadOnlyList<int> normals)
        {
            Part = part;
            PartIndex = partIndex;
            Before = before;
            After = part.GeometryVersion;
            Topology = part.TopologyVersion;
            Positions = positions;
            Normals = normals;
        }
    }
}