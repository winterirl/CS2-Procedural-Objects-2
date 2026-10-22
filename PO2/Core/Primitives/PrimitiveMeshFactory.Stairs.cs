using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Core.Primitives
{
    public static partial class PrimitiveMeshFactory
    {
        private static PrimitiveMesh CreateStairsPrimitive(float width, float treadDepth, int steps)
        {
            if (width <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Stair width must be greater than zero.");
            }

            if (treadDepth <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(treadDepth), "Stair tread depth must be greater than zero.");
            }

            if (steps < StairsParameters.kMinimumSteps || steps > StairsParameters.kMaximumSteps)
            {
                throw new ArgumentOutOfRangeException(nameof(steps), $"Stair count must be between " + $"{StairsParameters.kMinimumSteps} and " + $"{StairsParameters.kMaximumSteps}.");
            }

            float totalHeight = steps * StairsParameters.kStepHeight;
            float totalDepth = steps * treadDepth;
            float halfWidth = width * 0.5f;
            float bottomY = totalHeight * -0.5f;
            float lowZ = totalDepth * -0.5f;
            float highZ = totalDepth * 0.5f;

            List<Vector3> vertices = new List<Vector3>(steps * 16 + 32);
            List<Vector3> normals = new List<Vector3>(steps * 16 + 32);
            List<Vector4> uv0 = new List<Vector4>(steps * 16 + 32);
            List<int> triangles = new List<int>(steps * 24 + 36);
            List<StructuralFace> faces = new List<StructuralFace>(steps * 6 + 8);
            int faceId = 0;

            for (int i = 0; i < steps; i++)
            {
                float z0 = lowZ + i * treadDepth;
                float z1 = z0 + treadDepth;
                float lowerY = bottomY + i * StairsParameters.kStepHeight;
                float treadY = lowerY + StairsParameters.kStepHeight;

                AddStairQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(halfWidth, lowerY, z0), new Vector3(-halfWidth, lowerY, z0), new Vector3(-halfWidth, treadY, z0), new Vector3(halfWidth, treadY, z0), Vector3.back, StairUV(1f, (lowerY - bottomY) / totalHeight), StairUV(0f, (lowerY - bottomY) / totalHeight), StairUV(0f, (treadY - bottomY) / totalHeight), StairUV(1f, (treadY - bottomY) / totalHeight));

                AddStairQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(-halfWidth, treadY, z1), new Vector3(halfWidth, treadY, z1), new Vector3(halfWidth, treadY, z0), new Vector3(-halfWidth, treadY, z0), Vector3.up, StairUV(0f, (z1 - lowZ) / totalDepth), StairUV(1f, (z1 - lowZ) / totalDepth), StairUV(1f, (z0 - lowZ) / totalDepth), StairUV(0f, (z0 - lowZ) / totalDepth));
            }

            float topY = bottomY + totalHeight;

            AddStairQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(-halfWidth, bottomY, highZ), new Vector3(halfWidth, bottomY, highZ), new Vector3(halfWidth, topY, highZ), new Vector3(-halfWidth, topY, highZ), Vector3.forward, StairUV(0f, 0f), StairUV(1f, 0f), StairUV(1f, 1f), StairUV(0f, 1f));

            AddStairQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(-halfWidth, bottomY, lowZ), new Vector3(halfWidth, bottomY, lowZ), new Vector3(halfWidth, bottomY, highZ), new Vector3(-halfWidth, bottomY, highZ), Vector3.down, StairUV(0f, 0f), StairUV(1f, 0f), StairUV(1f, 1f), StairUV(0f, 1f));

            // The complete side outline is triangulated from the lower high-end
            // corner. That point can see the whole monotone stair profile, so
            // every boundary vertex is retained without overlaps or T-junctions.
            List<Vector2> sidePath = new List<Vector2>(steps * 2 + 1)
            {
                new Vector2(highZ, topY),
            };

            for (int i = steps - 1; i >= 0; i--)
            {
                float z = lowZ + i * treadDepth;
                float treadY = bottomY + (i + 1) * StairsParameters.kStepHeight;
                float lowerY = bottomY + i * StairsParameters.kStepHeight;

                sidePath.Add(new Vector2(z, treadY));
                sidePath.Add(new Vector2(z, lowerY));
            }

            AddStairSide(halfWidth, Vector3.right, sidePath, lowZ, highZ, bottomY, totalDepth, totalHeight, vertices, normals, uv0, triangles, faces, ref faceId);

            AddStairSide(-halfWidth, Vector3.left, sidePath, lowZ, highZ, bottomY, totalDepth, totalHeight, vertices, normals, uv0, triangles, faces, ref faceId);

            MeshData mesh = new MeshData(vertices.ToArray(), normals.ToArray(), null, uv0.ToArray(), triangles.ToArray());
            mesh.RecalculateTangents();

            return new PrimitiveMesh(mesh, faces.ToArray());
        }

        private static void AddStairSide(float x, Vector3 normal, List<Vector2> sidePath, float lowZ, float highZ, float bottomY, float totalDepth, float totalHeight, List<Vector3> vertices, List<Vector3> normals, List<Vector4> uv0, List<int> triangles, List<StructuralFace> faces, ref int faceId)
        {
            int anchorIndex = vertices.Count;
            vertices.Add(new Vector3(x, bottomY, highZ));
            normals.Add(normal);
            uv0.Add(StairUV(1f, 0f));

            int pathStart = vertices.Count;
            for (int i = 0; i < sidePath.Count; i++)
            {
                Vector2 point = sidePath[i];
                vertices.Add(new Vector3(x, point.y, point.x));
                normals.Add(normal);
                uv0.Add(StairUV((point.x - lowZ) / totalDepth, (point.y - bottomY) / totalHeight));
            }

            for (int i = 0; i + 1 < sidePath.Count; i++)
            {
                int a = anchorIndex;
                int b = pathStart + i;
                int c = pathStart + i + 1;
                Vector3 faceNormal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);

                if (Vector3.Dot(faceNormal, normal) < 0f)
                {
                    int swap = b;
                    b = c;
                    c = swap;
                }

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                faces.Add(new StructuralFace(faceId++, a, b, c));
            }
        }

        private static void AddStairQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector4> uv0, List<int> triangles, List<StructuralFace> faces, ref int faceId, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Vector4 texture0, Vector4 texture1, Vector4 texture2, Vector4 texture3)
        {
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) < 0f)
            {
                Vector3 positionSwap = p1;
                p1 = p3;
                p3 = positionSwap;

                Vector4 textureSwap = texture1;
                texture1 = texture3;
                texture3 = textureSwap;
            }

            int start = vertices.Count;
            vertices.Add(p0);
            vertices.Add(p1);
            vertices.Add(p2);
            vertices.Add(p3);

            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);

            uv0.Add(texture0);
            uv0.Add(texture1);
            uv0.Add(texture2);
            uv0.Add(texture3);

            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);

            faces.Add(new StructuralQuad(faceId++, start, start + 1, start + 2, start + 3));
        }

        private static Vector4 StairUV(float u, float v)
        {
            return new Vector4(u, v, 0f, 0f);
        }
    }
}