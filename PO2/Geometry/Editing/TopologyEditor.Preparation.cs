using PO2.Persistence.Model;

namespace PO2.Geometry.Editing
{
    public static partial class TopologyEditor
    {
        internal static PersistencePartData PrepareTopology(PersistencePartData source, MeshMutationResult mutation, PersistenceSurfaceAreaData[] areas = null)
        {
            return PersistencePartData.FromDecoded(source.Name, mutation.Vertices, mutation.Normals, mutation.Tangents, mutation.UV0, mutation.VertexColors, mutation.TexCoord1, mutation.TexCoord3, mutation.Triangles, mutation.RenderToLogical, mutation.LogicalVertexCount, mutation.StructuralFaces, source.StructuralTopologySource, areas ?? source.SurfaceAreas, mutation.TriangleSurfaceAreaIds, source.NextSurfaceAreaId, source.LocalPivot);
        }
    }
}