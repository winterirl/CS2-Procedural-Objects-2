using PO2.Persistence.Model;

namespace PO2.Geometry.Editing
{
    internal sealed class VertexDeletePlan
    {
        internal readonly PersistencePartData Source;
        internal readonly bool[] RemovedTriangles, RemovedFaces;
        internal readonly int[] OldToNew;
        internal readonly int VertexCount, TriangleCount, FaceCount;

        internal VertexDeletePlan(PersistencePartData source, bool[] triangles, bool[] faces, int[] oldToNew, int vertexCount, int triangleCount, int faceCount)
        {
            Source = source;
            RemovedTriangles = triangles;
            RemovedFaces = faces;
            OldToNew = oldToNew;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
            FaceCount = faceCount;
        }
    }
}