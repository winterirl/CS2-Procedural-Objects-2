using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Core.Primitives
{
    public static partial class PrimitiveMeshFactory
    {
        private static PrimitiveMesh CreateCylinderPrimitive(float radius, float height, int segments)
        {
            if (radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), "Cylinder radius must be greater than zero.");
            }

            if (height <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Cylinder height must be greater than zero.");
            }

            // UI actually enforces a 5 minimum b/c i'd just be a wedge or box otherwise
            // 3 is just the actual min it would need to even work
            if (segments < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(segments), "Cylinder must have at least three segments.");
            }

            float halfHeight = height * 0.5f;
            int sideVertexCount = (segments + 1) * 2;
            int topCenterIndex = sideVertexCount;
            int topRimStart = topCenterIndex + 1;
            int bottomCenterIndex = topRimStart + segments;
            int bottomRimStart = bottomCenterIndex + 1;
            int renderVertexCount = bottomRimStart + segments;

            Vector3[] vertices = new Vector3[renderVertexCount];
            Vector3[] normals = new Vector3[renderVertexCount];
            Vector4[] uv0 = new Vector4[renderVertexCount];

            // Side vertices share render indices between neighboring quads so the cylindrical surface can shade smoothly.
            // The final column duplicates the first position to create the U=1 UV seam and logical topology welds them back together.
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                float angle = u * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                Vector3 radialNormal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                int bottomIndex = i * 2;
                int topIndex = bottomIndex + 1;

                vertices[bottomIndex] = new Vector3(x, -halfHeight, z);
                vertices[topIndex] = new Vector3(x, halfHeight, z);

                normals[bottomIndex] = radialNormal;
                normals[topIndex] = radialNormal;

                uv0[bottomIndex] = new Vector2(u, 0f);
                uv0[topIndex] = new Vector2(u, 1f);
            }

            vertices[topCenterIndex] = new Vector3(0f, halfHeight, 0f);
            normals[topCenterIndex] = Vector3.up;
            uv0[topCenterIndex] = new Vector2(0.5f, 0.5f);

            vertices[bottomCenterIndex] = new Vector3(0f, -halfHeight, 0f);
            normals[bottomCenterIndex] = Vector3.down;
            uv0[bottomCenterIndex] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < segments; i++)
            {
                float u = i / (float)segments;
                float angle = u * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                float x = cos * radius;
                float z = sin * radius;
                Vector2 capUV = new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f);

                int topIndex = topRimStart + i;
                vertices[topIndex] = new Vector3(x, halfHeight, z);
                normals[topIndex] = Vector3.up;
                uv0[topIndex] = capUV;

                int bottomIndex = bottomRimStart + i;
                vertices[bottomIndex] = new Vector3(x, -halfHeight, z);
                normals[bottomIndex] = Vector3.down;
                uv0[bottomIndex] = capUV;
            }

            List<int> triangles = new List<int>(segments * 12);
            List<StructuralFace> faces = new List<StructuralFace>(segments * 3);

            int faceId = 0;

            for (int i = 0; i < segments; i++)
            {
                int bottomA = i * 2;
                int topA = bottomA + 1;
                int bottomB = (i + 1) * 2;
                int topB = bottomB + 1;

                // Winding faces outward from the cylinder axis
                triangles.Add(bottomA);
                triangles.Add(topA);
                triangles.Add(topB);
                triangles.Add(bottomA);
                triangles.Add(topB);
                triangles.Add(bottomB);

                faces.Add(new StructuralQuad(faceId++, bottomA, topA, topB, bottomB));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int rimA = topRimStart + i;
                int rimB = topRimStart + next;

                // Angular order is clockwise from +Y here. Reverse the rim for an outward cap.
                triangles.Add(topCenterIndex);
                triangles.Add(rimB);
                triangles.Add(rimA);

                faces.Add(new StructuralFace(faceId++, topCenterIndex, rimB, rimA));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int rimA = bottomRimStart + i;
                int rimB = bottomRimStart + next;

                // -Y cap uses the opposite winding
                triangles.Add(bottomCenterIndex);
                triangles.Add(rimA);
                triangles.Add(rimB);

                faces.Add(new StructuralFace(faceId++, bottomCenterIndex, rimA, rimB));
            }

            MeshData mesh = new MeshData(vertices, normals, null, uv0, triangles.ToArray());
            mesh.RecalculateTangents();

            return new PrimitiveMesh(mesh, faces.ToArray());
        }
    }
}