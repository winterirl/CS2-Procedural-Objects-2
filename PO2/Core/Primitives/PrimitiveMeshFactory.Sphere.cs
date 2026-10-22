using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Core.Primitives
{
    public static partial class PrimitiveMeshFactory
    {
        private static PrimitiveMesh CreateSpherePrimitive(float radius, int segments, int verticalSegments)
        {
            var gores = new PrimitiveGores(segments);
            int stride = gores.Stride;
            float circumference = radius * Mathf.PI * 2f;
            float meridianLength = radius * Mathf.PI;
            int topStart = (verticalSegments - 1) * stride;
            int bottomStart = topStart + segments;
            int vertexCount = bottomStart + segments;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uv0 = new Vector4[vertexCount];

            // Keep the poles out of the ring grid so no quad collapses to a line.
            for (int row = 1; row < verticalSegments; row++)
            {
                float v = row / (float)verticalSegments;
                float latitude = v * Mathf.PI;
                float ringRadius = Mathf.Sin(latitude);
                float y = Mathf.Cos(latitude);
                int start = (row - 1) * stride;
                for (int column = 0; column < segments; column++)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        int longitude = column + side;
                        float u = longitude / (float)segments;
                        float angle = longitude == segments ? 0f : u * Mathf.PI * 2f;
                        float x = Mathf.Cos(angle) * ringRadius;
                        float z = Mathf.Sin(angle) * ringRadius;
                        int index = start + gores.Left[column] + side;
                        Vector3 normal = new Vector3(x, y, z);
                        vertices[index] = normal * radius;
                        normals[index] = normal;
                        float textureV = 1f - v;
                        uv0[index] = gores.UV(column, u, ringRadius, textureV, circumference, meridianLength);
                    }
                }
            }

            for (int column = 0; column < segments; column++)
            {
                // Each strip comes to a point in the unwrap as well as on the mesh.
                vertices[topStart + column] = Vector3.up * radius;
                vertices[bottomStart + column] = Vector3.down * radius;
                normals[topStart + column] = Vector3.up;
                normals[bottomStart + column] = Vector3.down;
                uv0[topStart + column] = gores.UV(column, 0f, 0f, 1f, circumference, meridianLength);
                uv0[bottomStart + column] = gores.UV(column, 0f, 0f, 0f, circumference, meridianLength);
            }

            var triangles = new List<int>(segments * (verticalSegments - 1) * 6);
            var faces = new List<StructuralFace>(segments * verticalSegments);
            for (int row = 0; row < verticalSegments - 2; row++)
            {
                for (int column = 0; column < segments; column++)
                {
                    int a = row * stride + gores.Left[column];
                    int b = a + 1;
                    int c = b + stride;
                    int d = a + stride;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(d);
                    faces.Add(new StructuralQuad(faces.Count, a, b, c, d));
                }
            }

            int lastRing = (verticalSegments - 2) * stride;
            for (int column = 0; column < segments; column++)
            {
                int top = topStart + column;
                triangles.Add(top);
                triangles.Add(gores.Left[column] + 1);
                triangles.Add(gores.Left[column]);
                faces.Add(new StructuralFace(faces.Count, top, gores.Left[column] + 1, gores.Left[column]));

                int bottom = bottomStart + column;
                int a = lastRing + gores.Left[column];
                triangles.Add(bottom);
                triangles.Add(a);
                triangles.Add(a + 1);
                faces.Add(new StructuralFace(faces.Count, bottom, a, a + 1));
            }

            var mesh = new MeshData(vertices, normals, null, uv0, triangles.ToArray());
            mesh.RecalculateTangents();
            return new PrimitiveMesh(mesh, faces.ToArray());
        }
    }
}