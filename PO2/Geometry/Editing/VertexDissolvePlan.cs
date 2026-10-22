using PO2.Persistence.Model;

namespace PO2.Geometry.Editing
{
    internal sealed class VertexDissolvePlan
    {
        internal sealed class Patch
        {
            internal int Vertex, AreaId, FirstTriangle, FirstFace;
            internal int[] RemovedTriangles, Boundary, Triangles;
            internal bool IsQuad;
        }

        internal readonly VertexDeletePlan Removal;
        internal readonly Patch[] Patches;
        internal PersistencePartData Source => Removal.Source;

        internal VertexDissolvePlan(VertexDeletePlan removal, Patch[] patches)
        {
            Removal = removal;
            Patches = patches;
        }
    }
}