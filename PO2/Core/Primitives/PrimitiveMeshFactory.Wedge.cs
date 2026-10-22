using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Core.Primitives
{
    public static partial class PrimitiveMeshFactory
    {
        private static PrimitiveMesh CreateWedgePrimitive(float width, float height, float depth)
        {
            float hx = width * 0.5f;
            float hy = height * 0.5f;
            float hz = depth * 0.5f;

            // The wedge rises from local -Z to +Z. Separate render corners preserve normals
            // and UV seams; logical topology welds matching positions afterward.
            Vector3[] vertices =
            {
                // -X triangle
                new Vector3(-hx, -hy, -hz),
                new Vector3(-hx, -hy, hz),
                new Vector3(-hx, hy, hz),

                // +X triangle
                new Vector3(hx, -hy, -hz),
                new Vector3(hx, hy, hz),
                new Vector3(hx, -hy, hz),

                // -Y bottom quad
                new Vector3(-hx, -hy, -hz),
                new Vector3(hx, -hy, -hz),
                new Vector3(hx, -hy, hz),
                new Vector3(-hx, -hy, hz),

                // +Z vertical quad
                new Vector3(-hx, -hy, hz),
                new Vector3(hx, -hy, hz),
                new Vector3(hx, hy, hz),
                new Vector3(-hx, hy, hz),

                // Sloped quad, outward toward +Y/-Z
                new Vector3(-hx, -hy, -hz),
                new Vector3(-hx, hy, hz),
                new Vector3(hx, hy, hz),
                new Vector3(hx, -hy, -hz),
            };

            int[] triangles =
            {
                0, 1, 2,
                3, 4, 5,
                6, 7, 8, 6, 8, 9,
                10, 11, 12, 10, 12, 13,
                14, 15, 16, 14, 16, 17,
            };

            Vector4[] uv0 =
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            };

            MeshData mesh = new MeshData(vertices, null, null, uv0, triangles);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            StructuralFace[] faces =
            {
                new StructuralFace(0, 0, 1, 2),
                new StructuralFace(1, 3, 4, 5),
                new StructuralQuad(2, 6, 7, 8, 9),
                new StructuralQuad(3, 10, 11, 12, 13),
                new StructuralQuad(4, 14, 15, 16, 17),
            };

            return new PrimitiveMesh(mesh, faces);
        }
    }
}