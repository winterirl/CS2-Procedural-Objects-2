using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Picking;

namespace PO2.Tools.MeshEdit
{
    // Structural face IDs are a tool concern; the geometry picker is shared.
    internal sealed class StructuralFacePickCache
    {
        private readonly TrianglePickCache m_Picker;
        private readonly int[] m_TriangleFaceIds;

        private StructuralFacePickCache(MeshPart part, int[] triangleFaceIds)
        {
            m_Picker = new TrianglePickCache(part.Mesh.Vertices, part.Mesh.Triangles);
            m_TriangleFaceIds = triangleFaceIds;
        }

        public static bool TryCreate(MeshPart part, out StructuralFacePickCache cache)
        {
            cache = null;
            if (part == null || part.StructuralTopology == null || part.StructuralTopology.FaceCount == 0)
            {
                return false;
            }

            Vector3[] vertices = part.Mesh.Vertices;
            int[] indices = part.Mesh.Triangles;
            if (vertices == null || indices == null || indices.Length == 0 || indices.Length % 3 != 0)
            {
                return false;
            }

            int triangleCount = indices.Length / 3;
            int[] triangleFaceIds = new int[triangleCount];
            for (int triangleId = 0; triangleId < triangleCount; triangleId++)
            {
                int offset = triangleId * 3;
                int a = indices[offset];
                int b = indices[offset + 1];
                int c = indices[offset + 2];

                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length || !part.TryGetTriangleStructuralFaceId(triangleId, out int faceId) || faceId < 0 || faceId >= part.StructuralTopology.FaceCount)
                {
                    return false;
                }

                triangleFaceIds[triangleId] = faceId;
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                if (!TrianglePickCache.IsFinite(vertices[i]))
                {
                    return false;
                }
            }

            cache = new StructuralFacePickCache(part, triangleFaceIds);
            return true;
        }

        public bool TryPick(Vector3 origin, Vector3 direction, out int faceId)
        {
            faceId = -1;
            if (!m_Picker.TryPick(origin, direction, out int triangleId, out float _))
            {
                return false;
            }

            faceId = m_TriangleFaceIds[triangleId];
            return true;
        }
    }
}