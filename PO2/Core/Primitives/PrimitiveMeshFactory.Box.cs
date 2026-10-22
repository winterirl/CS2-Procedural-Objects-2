using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Core.Primitives
{
    public static partial class PrimitiveMeshFactory
    {
        private static MeshData CreateBox(float width, float height, float depth)
        {
            float hx = width * 0.5f;
            float hy = height * 0.5f;
            float hz = depth * 0.5f;

            // Each hard-edged face owns render vertices for independent normals and UVs
            // Logical topology later welds at overlaping positions so the 8 corners remain the editable vertex set.
            Vector3[] vertices =
            {
                // +Z
                new Vector3(-hx, -hy, hz),
                new Vector3(hx, -hy, hz),
                new Vector3(hx, hy, hz),
                new Vector3(-hx, hy, hz),

                // -Z
                new Vector3(hx, -hy, -hz),
                new Vector3(-hx, -hy, -hz),
                new Vector3(-hx, hy, -hz),
                new Vector3(hx, hy, -hz),

                // +X
                new Vector3(hx, -hy, hz),
                new Vector3(hx, -hy, -hz),
                new Vector3(hx, hy, -hz),
                new Vector3(hx, hy, hz),

                // -X
                new Vector3(-hx, -hy, -hz),
                new Vector3(-hx, -hy, hz),
                new Vector3(-hx, hy, hz),
                new Vector3(-hx, hy, -hz),

                // +Y
                new Vector3(-hx, hy, hz),
                new Vector3(hx, hy, hz),
                new Vector3(hx, hy, -hz),
                new Vector3(-hx, hy, -hz),

                // -Y
                new Vector3(-hx, -hy, -hz),
                new Vector3(hx, -hy, -hz),
                new Vector3(hx, -hy, hz),
                new Vector3(-hx, -hy, hz),
            };

            int[] triangles =
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7,
                8, 9, 10, 8, 10, 11,
                12, 13, 14, 12, 14, 15,
                16, 17, 18, 16, 18, 19,
                20, 21, 22, 20, 22, 23,
            };

            Vector3[] normals =
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                Vector3.left, Vector3.left, Vector3.left, Vector3.left,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down,
            };

            Vector4[] tangents =
            {
                new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f),
                new Vector4(-1f, 0f, 0f, 1f), new Vector4(-1f, 0f, 0f, 1f), new Vector4(-1f, 0f, 0f, 1f), new Vector4(-1f, 0f, 0f, 1f),
                new Vector4(0f, 0f, -1f, 1f), new Vector4(0f, 0f, -1f, 1f), new Vector4(0f, 0f, -1f, 1f), new Vector4(0f, 0f, -1f, 1f),
                new Vector4(0f, 0f, 1f, 1f), new Vector4(0f, 0f, 1f, 1f), new Vector4(0f, 0f, 1f, 1f), new Vector4(0f, 0f, 1f, 1f),
                new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f),
                new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f),
            };

            Vector4[] uv0 =
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            };

            return new MeshData(vertices, normals, tangents, uv0, triangles);
        }

        private static StructuralFace[] CreateBoxStructuralFaces()
        {
            return new StructuralFace[]
            {
                new StructuralQuad(0, 0, 1, 2, 3),
                new StructuralQuad(1, 4, 5, 6, 7),
                new StructuralQuad(2, 8, 9, 10, 11),
                new StructuralQuad(3, 12, 13, 14, 15),
                new StructuralQuad(4, 16, 17, 18, 19),
                new StructuralQuad(5, 20, 21, 22, 23),
            };
        }
    }
}