using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Core.Primitives
{
    public static partial class PrimitiveMeshFactory
    {
        private static PrimitiveMesh CreateConePrimitive(float bottomDiameter, float topDiameter, float height, int segments)
        {
            if (bottomDiameter <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(bottomDiameter), "Cone bottom diameter must be greater than zero.");
            }

            if (topDiameter < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(topDiameter), "Cone top diameter cannot be negative.");
            }

            if (height <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Cone height must be greater than zero.");
            }

            if (segments < ConeParameters.kMinimumSegments || segments > ConeParameters.kMaximumSegments)
            {
                throw new ArgumentOutOfRangeException(nameof(segments), $"Cone segments must be between " + $"{ConeParameters.kMinimumSegments} and " + $"{ConeParameters.kMaximumSegments}.");
            }

            float bottomRadius = bottomDiameter * 0.5f;
            float topRadius = topDiameter * 0.5f;

            return topRadius == 0f ? CreatePointedConePrimitive(bottomRadius, height, segments) : CreateFrustumPrimitive(bottomRadius, topRadius, height, segments);
        }

        private static PrimitiveMesh CreatePointedConePrimitive(float bottomRadius, float height, int segments)
        {
            float halfHeight = height * 0.5f;
            int sideRimStart = 0;
            int sideApexStart = segments + 1;
            int bottomCenterIndex = sideApexStart + segments;
            int bottomRimStart = bottomCenterIndex + 1;
            int renderVertexCount = bottomRimStart + segments;

            Vector3[] vertices = new Vector3[renderVertexCount];
            Vector3[] normals = new Vector3[renderVertexCount];
            Vector4[] uv0 = new Vector4[renderVertexCount];
            float normalY = bottomRadius / height;

            // Duplicate the rim at the UV seam and the render apex for each face.
            // Topology still welds one logical apex without zero-area quads at the tip.
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                float angle = u * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                vertices[sideRimStart + i] = new Vector3(cos * bottomRadius, -halfHeight, sin * bottomRadius);
                normals[sideRimStart + i] = new Vector3(cos, normalY, sin).normalized;
                uv0[sideRimStart + i] = new Vector2(u, 0f);
            }

            for (int i = 0; i < segments; i++)
            {
                float u = (i + 0.5f) / segments;
                float angle = u * Mathf.PI * 2f;

                vertices[sideApexStart + i] = new Vector3(0f, halfHeight, 0f);
                normals[sideApexStart + i] = new Vector3(Mathf.Cos(angle), normalY, Mathf.Sin(angle)).normalized;
                uv0[sideApexStart + i] = new Vector2(u, 1f);
            }

            vertices[bottomCenterIndex] = new Vector3(0f, -halfHeight, 0f);
            normals[bottomCenterIndex] = Vector3.down;
            uv0[bottomCenterIndex] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < segments; i++)
            {
                float u = i / (float)segments;
                float angle = u * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                int rimIndex = bottomRimStart + i;

                vertices[rimIndex] = new Vector3(cos * bottomRadius, -halfHeight, sin * bottomRadius);
                normals[rimIndex] = Vector3.down;
                uv0[rimIndex] = new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f);
            }

            List<int> triangles = new List<int>(segments * 6);
            List<StructuralFace> faces = new List<StructuralFace>(segments * 2);
            int faceId = 0;

            for (int i = 0; i < segments; i++)
            {
                int rimA = sideRimStart + i;
                int apex = sideApexStart + i;
                int rimB = sideRimStart + i + 1;

                triangles.Add(rimA);
                triangles.Add(apex);
                triangles.Add(rimB);
                faces.Add(new StructuralFace(faceId++, rimA, apex, rimB));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int rimA = bottomRimStart + i;
                int rimB = bottomRimStart + next;

                triangles.Add(bottomCenterIndex);
                triangles.Add(rimA);
                triangles.Add(rimB);
                faces.Add(new StructuralFace(faceId++, bottomCenterIndex, rimA, rimB));
            }

            MeshData mesh = new MeshData(vertices, normals, null, uv0, triangles.ToArray());
            mesh.RecalculateTangents();

            return new PrimitiveMesh(mesh, faces.ToArray());
        }

        private static PrimitiveMesh CreateFrustumPrimitive(float bottomRadius, float topRadius, float height, int segments)
        {
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
            float normalY = (bottomRadius - topRadius) / height;

            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                float angle = u * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                Vector3 sideNormal = new Vector3(cos, normalY, sin).normalized;
                int bottomIndex = i * 2;
                int topIndex = bottomIndex + 1;

                vertices[bottomIndex] = new Vector3(cos * bottomRadius, -halfHeight, sin * bottomRadius);
                vertices[topIndex] = new Vector3(cos * topRadius, halfHeight, sin * topRadius);
                normals[bottomIndex] = sideNormal;
                normals[topIndex] = sideNormal;
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
                Vector2 capUV = new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f);
                int topIndex = topRimStart + i;
                int bottomIndex = bottomRimStart + i;

                vertices[topIndex] = new Vector3(cos * topRadius, halfHeight, sin * topRadius);
                normals[topIndex] = Vector3.up;
                uv0[topIndex] = capUV;

                vertices[bottomIndex] = new Vector3(cos * bottomRadius, -halfHeight, sin * bottomRadius);
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