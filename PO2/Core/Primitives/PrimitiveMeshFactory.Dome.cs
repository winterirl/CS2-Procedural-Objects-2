using PO2.Geometry;
using PO2.Geometry.Topology;
using System.Collections.Generic;
using UnityEngine;

namespace PO2.Core.Primitives
{
    public static partial class PrimitiveMeshFactory
    {
        private static PrimitiveMesh CreateDomePrimitive(float radius, float height, int segments, int verticalSegments)
        {
            float halfHeight = height * 0.5f;
            var gores = new PrimitiveGores(segments);
            int stride = gores.Stride;
            float circumference = radius * Mathf.PI * 2f;
            float[] meridian = DomeMeridianDistances(radius, height, verticalSegments);
            float meridianLength = meridian[verticalSegments];
            int topStart = verticalSegments * stride;
            int capStart = topStart + segments;
            int capCenter = capStart + segments;
            var vertices = new Vector3[capCenter + 1];
            var normals = new Vector3[capCenter + 1];
            var uv0 = new Vector4[capCenter + 1];

            for (int row = 1; row <= verticalSegments; row++)
            {
                float v = row / (float)verticalSegments;
                float latitude = v * Mathf.PI * 0.5f;
                float ringRadius = row == verticalSegments ? 1f : Mathf.Sin(latitude);
                float y = row == verticalSegments ? 0f : Mathf.Cos(latitude);
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
                        vertices[index] = new Vector3(x * radius, y * height - halfHeight, z * radius);
                        normals[index] = new Vector3(x * height, y * radius, z * height).normalized;
                        float textureV = 1f - meridian[row] / meridianLength;
                        uv0[index] = gores.UV(column, u, ringRadius, textureV, circumference, meridianLength);
                    }
                }
            }

            int lastRing = (verticalSegments - 1) * stride;
            for (int column = 0; column < segments; column++)
            {
                int top = topStart + column;
                vertices[top] = new Vector3(0f, halfHeight, 0f);
                normals[top] = Vector3.up;
                uv0[top] = gores.UV(column, 0f, 0f, 1f, circumference, meridianLength);

                // The cap shares positions with the rim, but keeps its own flat normals and UVs.
                int cap = capStart + column;
                Vector3 rim = vertices[lastRing + gores.Left[column]];
                vertices[cap] = rim;
                normals[cap] = Vector3.down;
                uv0[cap] = new Vector4(0.5f + rim.x / radius * 0.5f, 0.5f + rim.z / radius * 0.5f, rim.x + radius, rim.z + radius);
            }

            vertices[capCenter] = new Vector3(0f, -halfHeight, 0f);
            normals[capCenter] = Vector3.down;
            uv0[capCenter] = new Vector4(0.5f, 0.5f, radius, radius);

            var triangles = new List<int>(segments * verticalSegments * 6);
            var faces = new List<StructuralFace>(segments * (verticalSegments + 1));
            for (int row = 0; row < verticalSegments - 1; row++)
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

            for (int column = 0; column < segments; column++)
            {
                int top = topStart + column;
                triangles.Add(top);
                triangles.Add(gores.Left[column] + 1);
                triangles.Add(gores.Left[column]);
                faces.Add(new StructuralFace(faces.Count, top, gores.Left[column] + 1, gores.Left[column]));

                int a = capStart + column;
                int b = capStart + (column + 1) % segments;
                triangles.Add(capCenter);
                triangles.Add(a);
                triangles.Add(b);
                faces.Add(new StructuralFace(faces.Count, capCenter, a, b));
            }

            var mesh = new MeshData(vertices, normals, null, uv0, triangles.ToArray());
            mesh.RecalculateTangents();
            return new PrimitiveMesh(mesh, faces.ToArray());
        }
    }
}