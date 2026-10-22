using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Core.Primitives
{
    public static partial class PrimitiveMeshFactory
    {
        private static PrimitiveMesh CreateArchPrimitive(float width, float height, float depth, float openingWidth, float openingHeight, int curveSegments)
        {
            // Parameter objects already enforce these relationships.
            // Keep the factory guarded too since it is the last stop before array creation.
            _ = new ArchParameters(width, height, depth, openingWidth, openingHeight, curveSegments);

            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;
            var halfDepth = depth * 0.5f;
            var bottomY = -halfHeight;
            var topY = halfHeight;
            var radius = openingWidth * 0.5f;
            var springY = bottomY + openingHeight - radius;
            var jambHeight = springY - bottomY;
            var arcLength = Mathf.PI * radius;
            var revealLength = jambHeight * 2f + arcLength;

            var arc = new Vector2[curveSegments + 1];
            var inwardNormals = new Vector3[curveSegments + 1];
            var revealDistances = new float[curveSegments + 1];

            for (var i = 0; i <= curveSegments; i++)
            {
                var t = i / (float)curveSegments;
                var angle = Mathf.PI * (1f - t);
                var cos = Mathf.Cos(angle);
                var sin = Mathf.Sin(angle);

                arc[i] = new Vector2(cos * radius, springY + sin * radius);
                inwardNormals[i] = new Vector3(-cos, -sin, 0f);
                revealDistances[i] = jambHeight + arcLength * t;
            }

            var vertices = new List<Vector3>(curveSegments * 24 + 64);
            var normals = new List<Vector3>(curveSegments * 24 + 64);
            var uv0 = new List<Vector4>(curveSegments * 24 + 64);
            var triangles = new List<int>(curveSegments * 36 + 96);
            var faces = new List<StructuralFace>(curveSegments * 3 + 16);
            var faceId = 0;

            AddArchFrontAndBack(halfWidth, halfDepth, bottomY, topY, radius, width, height, arc, vertices, normals, uv0, triangles, faces, ref faceId);

            AddArchFlatQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(-halfWidth, bottomY, -halfDepth), new Vector3(-halfWidth, bottomY, halfDepth), new Vector3(-halfWidth, topY, halfDepth), new Vector3(-halfWidth, topY, -halfDepth), Vector3.left, ArchUV(0f, 0f), ArchUV(1f, 0f), ArchUV(1f, 1f), ArchUV(0f, 1f));

            AddArchFlatQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(halfWidth, bottomY, halfDepth), new Vector3(halfWidth, bottomY, -halfDepth), new Vector3(halfWidth, topY, -halfDepth), new Vector3(halfWidth, topY, halfDepth), Vector3.right, ArchUV(1f, 0f), ArchUV(0f, 0f), ArchUV(0f, 1f), ArchUV(1f, 1f));

            AddArchTop(vertices, normals, uv0, triangles, faces, ref faceId, -halfWidth, -radius, halfWidth, halfDepth, topY);

            for (var i = 0; i + 1 < arc.Length; i++)
                AddArchTop(vertices, normals, uv0, triangles, faces, ref faceId, arc[i].x, arc[i + 1].x, halfWidth, halfDepth, topY);

            AddArchTop(vertices, normals, uv0, triangles, faces, ref faceId, radius, halfWidth, halfWidth, halfDepth, topY);

            AddArchBottom(-halfWidth, -radius, halfDepth, bottomY, vertices, normals, uv0, triangles, faces, ref faceId);

            AddArchBottom(radius, halfWidth, halfDepth, bottomY, vertices, normals, uv0, triangles, faces, ref faceId);

            if (jambHeight > 0f)
            {
                AddArchQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(-radius, bottomY, halfDepth), new Vector3(-radius, bottomY, -halfDepth), new Vector3(-radius, springY, -halfDepth), new Vector3(-radius, springY, halfDepth), Vector3.right, Vector3.right, Vector3.right, Vector3.right, ArchRevealUV(halfDepth, 0f, halfDepth, depth, revealLength), ArchRevealUV(-halfDepth, 0f, halfDepth, depth, revealLength), ArchRevealUV(-halfDepth, jambHeight, halfDepth, depth, revealLength), ArchRevealUV(halfDepth, jambHeight, halfDepth, depth, revealLength));

                var rightSpringDistance = jambHeight + arcLength;
                AddArchQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(radius, springY, halfDepth), new Vector3(radius, springY, -halfDepth), new Vector3(radius, bottomY, -halfDepth), new Vector3(radius, bottomY, halfDepth), Vector3.left, Vector3.left, Vector3.left, Vector3.left, ArchRevealUV(halfDepth, rightSpringDistance, halfDepth, depth, revealLength), ArchRevealUV(-halfDepth, rightSpringDistance, halfDepth, depth, revealLength), ArchRevealUV(-halfDepth, revealLength, halfDepth, depth, revealLength), ArchRevealUV(halfDepth, revealLength, halfDepth, depth, revealLength));
            }

            for (var i = 0; i < curveSegments; i++)
            {
                var a = arc[i];
                var b = arc[i + 1];
                var normalA = inwardNormals[i];
                var normalB = inwardNormals[i + 1];

                AddArchQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(a.x, a.y, halfDepth), new Vector3(a.x, a.y, -halfDepth), new Vector3(b.x, b.y, -halfDepth), new Vector3(b.x, b.y, halfDepth), normalA, normalA, normalB, normalB, ArchRevealUV(halfDepth, revealDistances[i], halfDepth, depth, revealLength), ArchRevealUV(-halfDepth, revealDistances[i], halfDepth, depth, revealLength), ArchRevealUV(-halfDepth, revealDistances[i + 1], halfDepth, depth, revealLength), ArchRevealUV(halfDepth, revealDistances[i + 1], halfDepth, depth, revealLength));
            }

            var mesh = new MeshData(vertices.ToArray(), normals.ToArray(), null, uv0.ToArray(), triangles.ToArray());
            mesh.RecalculateTangents();

            return new PrimitiveMesh(mesh, faces.ToArray());
        }

        private static void AddArchFrontAndBack(float halfWidth, float halfDepth, float bottomY, float topY, float radius, float width, float height, Vector2[] arc, List<Vector3> vertices, List<Vector3> normals, List<Vector4> uv0, List<int> triangles, List<StructuralFace> faces, ref int faceId)
        {
            Vector3[] faceNormals =
            {
                Vector3.forward,
                Vector3.back
            };
            float[] zPositions =
            {
                halfDepth,
                -halfDepth
            };

            for (var side = 0; side < faceNormals.Length; side++)
            {
                var normal = faceNormals[side];
                var z = zPositions[side];
                var springY = arc[0].y;

                AddArchFrontColumn(-radius, -halfWidth, bottomY, springY, topY, z, normal, halfWidth, width, height, vertices, normals, uv0, triangles, faces, ref faceId);

                AddArchFrontColumn(radius, halfWidth, bottomY, springY, topY, z, normal, halfWidth, width, height, vertices, normals, uv0, triangles, faces, ref faceId);

                for (var i = 0; i + 1 < arc.Length; i++)
                {
                    var a = arc[i];
                    var b = arc[i + 1];

                    AddArchFlatQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(a.x, a.y, z), new Vector3(b.x, b.y, z), new Vector3(b.x, topY, z), new Vector3(a.x, topY, z), normal, ArchPlanarUV(a.x, a.y, halfWidth, bottomY, width, height), ArchPlanarUV(b.x, b.y, halfWidth, bottomY, width, height), ArchPlanarUV(b.x, topY, halfWidth, bottomY, width, height), ArchPlanarUV(a.x, topY, halfWidth, bottomY, width, height));
                }
            }
        }

        private static void AddArchFrontColumn(float innerX, float outerX, float bottomY, float springY, float topY, float z, Vector3 normal, float halfWidth, float width, float height, List<Vector3> vertices, List<Vector3> normals, List<Vector4> uv0, List<int> triangles, List<StructuralFace> faces, ref int faceId)
        {
            var outerBottom = new Vector3(outerX, bottomY, z);
            var innerBottom = new Vector3(innerX, bottomY, z);
            var innerSpring = new Vector3(innerX, springY, z);
            var innerTop = new Vector3(innerX, topY, z);
            var outerTop = new Vector3(outerX, topY, z);

            var outerBottomUV = ArchPlanarUV(outerX, bottomY, halfWidth, bottomY, width, height);
            var innerBottomUV = ArchPlanarUV(innerX, bottomY, halfWidth, bottomY, width, height);
            var innerSpringUV = ArchPlanarUV(innerX, springY, halfWidth, bottomY, width, height);
            var innerTopUV = ArchPlanarUV(innerX, topY, halfWidth, bottomY, width, height);
            var outerTopUV = ArchPlanarUV(outerX, topY, halfWidth, bottomY, width, height);

            if (springY > bottomY)
            {
                AddArchFlatTriangle(vertices, normals, uv0, triangles, faces, ref faceId, outerBottom, innerBottom, innerSpring, normal, outerBottomUV, innerBottomUV, innerSpringUV);
                AddArchFlatTriangle(vertices, normals, uv0, triangles, faces, ref faceId, outerBottom, innerSpring, innerTop, normal, outerBottomUV, innerSpringUV, innerTopUV);
                AddArchFlatTriangle(vertices, normals, uv0, triangles, faces, ref faceId, outerBottom, innerTop, outerTop, normal, outerBottomUV, innerTopUV, outerTopUV);
                return;
            }

            AddArchFlatQuad(vertices, normals, uv0, triangles, faces, ref faceId, outerBottom, innerBottom, innerTop, outerTop, normal, outerBottomUV, innerBottomUV, innerTopUV, outerTopUV);
        }

        private static void AddArchTop(List<Vector3> vertices, List<Vector3> normals, List<Vector4> uv0, List<int> triangles, List<StructuralFace> faces, ref int faceId, float minX, float maxX, float halfWidth, float halfDepth, float topY)
        {
            var width = halfWidth * 2f;
            var minU = (minX + halfWidth) / width;
            var maxU = (maxX + halfWidth) / width;

            AddArchFlatQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(minX, topY, halfDepth), new Vector3(maxX, topY, halfDepth), new Vector3(maxX, topY, -halfDepth), new Vector3(minX, topY, -halfDepth), Vector3.up, ArchUV(minU, 1f), ArchUV(maxU, 1f), ArchUV(maxU, 0f), ArchUV(minU, 0f));
        }

        private static void AddArchBottom(float minX, float maxX, float halfDepth, float bottomY, List<Vector3> vertices, List<Vector3> normals, List<Vector4> uv0, List<int> triangles, List<StructuralFace> faces, ref int faceId)
        {
            AddArchFlatQuad(vertices, normals, uv0, triangles, faces, ref faceId, new Vector3(minX, bottomY, -halfDepth), new Vector3(maxX, bottomY, -halfDepth), new Vector3(maxX, bottomY, halfDepth), new Vector3(minX, bottomY, halfDepth), Vector3.down, ArchUV(0f, 0f), ArchUV(1f, 0f), ArchUV(1f, 1f), ArchUV(0f, 1f));
        }

        private static void AddArchFlatQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector4> uv0, List<int> triangles, List<StructuralFace> faces, ref int faceId, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Vector4 texture0, Vector4 texture1, Vector4 texture2, Vector4 texture3)
        {
            AddArchQuad(vertices, normals, uv0, triangles, faces, ref faceId, p0, p1, p2, p3, normal, normal, normal, normal, texture0, texture1, texture2, texture3);
        }

        private static void AddArchFlatTriangle(List<Vector3> vertices, List<Vector3> normals, List<Vector4> uv0, List<int> triangles, List<StructuralFace> faces, ref int faceId, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 normal, Vector4 texture0, Vector4 texture1, Vector4 texture2)
        {
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) < 0f)
            {
                var positionSwap = p1;
                p1 = p2;
                p2 = positionSwap;

                var textureSwap = texture1;
                texture1 = texture2;
                texture2 = textureSwap;
            }

            var start = vertices.Count;
            vertices.Add(p0);
            vertices.Add(p1);
            vertices.Add(p2);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            uv0.Add(texture0);
            uv0.Add(texture1);
            uv0.Add(texture2);

            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);

            faces.Add(new StructuralFace(faceId++, start, start + 1, start + 2));
        }

        private static void AddArchQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector4> uv0, List<int> triangles, List<StructuralFace> faces, ref int faceId, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal0, Vector3 normal1, Vector3 normal2, Vector3 normal3, Vector4 texture0, Vector4 texture1, Vector4 texture2, Vector4 texture3)
        {
            var averageNormal = normal0 + normal1 + normal2 + normal3;
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), averageNormal) < 0f)
            {
                var positionSwap = p1;
                p1 = p3;
                p3 = positionSwap;

                var normalSwap = normal1;
                normal1 = normal3;
                normal3 = normalSwap;

                var textureSwap = texture1;
                texture1 = texture3;
                texture3 = textureSwap;
            }

            var start = vertices.Count;
            vertices.Add(p0);
            vertices.Add(p1);
            vertices.Add(p2);
            vertices.Add(p3);
            normals.Add(normal0);
            normals.Add(normal1);
            normals.Add(normal2);
            normals.Add(normal3);
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

        private static Vector4 ArchPlanarUV(float x, float y, float halfWidth, float bottomY, float width, float height)
        {
            return ArchUV((x + halfWidth) / width, (y - bottomY) / height);
        }

        private static Vector4 ArchRevealUV(float z, float distance, float halfDepth, float depth, float revealLength)
        {
            return ArchUV((z + halfDepth) / depth, distance / revealLength);
        }

        private static Vector4 ArchUV(float u, float v)
        {
            return new Vector4(u, v, 0f, 0f);
        }
    }
}