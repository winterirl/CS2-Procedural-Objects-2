using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Core.Primitives
{
    public static partial class PrimitiveMeshFactory
    {
        private static PrimitiveMesh CreatePlanePrimitive(float width, float depth)
        {
            float hx = width * 0.5f;
            float hz = depth * 0.5f;

            Vector3[] vertices =
            {
                new Vector3(-hx, 0f, hz),
                new Vector3(hx, 0f, hz),
                new Vector3(hx, 0f, -hz),
                new Vector3(-hx, 0f, -hz),
            };

            int[] triangles =
            {
                0, 1, 2,
                0, 2, 3,
            };

            Vector3[] normals =
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
            };

            Vector4 tangent = new Vector4(1f, 0f, 0f, 1f);
            Vector4[] tangents =
            {
                tangent, tangent, tangent, tangent,
            };

            Vector4[] uv0 =
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };

            MeshData mesh = new MeshData(vertices, normals, tangents, uv0, triangles);

            StructuralFace[] faces =
            {
                new StructuralQuad(0, 0, 1, 2, 3),
            };

            return new PrimitiveMesh(mesh, faces);
        }
    }
}