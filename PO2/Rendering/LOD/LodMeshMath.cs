using System;
using UnityEngine;
using PO2.Rendering.Lod.Native;

namespace PO2.Rendering.Lod
{
    internal sealed class LodMetricData
    {
        internal LodMetricData(float[] positions, float[] attributes, float[] attributeWeights, int attributeCount, float positionExtent)
        {
            Positions = positions;
            Attributes = attributes;
            AttributeWeights = attributeWeights;
            AttributeCount = attributeCount;
            PositionExtent = positionExtent;
        }

        internal float[] Positions { get; }
        internal float[] Attributes { get; }
        internal float[] AttributeWeights { get; }
        internal int AttributeCount { get; }
        internal float PositionExtent { get; }
    }

    internal static class LodMeshMath
    {
        private const float kMinimumScale = 0.000001f;
        private const float kMinimumLengthSquared = 0.000000000001f;
        private const double kMinimumUvArea = 0.000000000001;

        internal static LodMetricData BuildMetric(LodMeshData mesh, Vector3 objectScale, LodGenerationSettings settings)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (!IsFinite(objectScale.x) || !IsFinite(objectScale.y) || !IsFinite(objectScale.z))
            {
                throw new ArgumentException("LOD object scale must be finite.", nameof(objectScale));
            }

            float scaleX = SanitizeScale(objectScale.x);
            float scaleY = SanitizeScale(objectScale.y);
            float scaleZ = SanitizeScale(objectScale.z);

            float uvWeight = mesh.UV0 != null ? CalculateUvWeight(mesh, settings) : 0.0f;
            bool includeNormals = mesh.Normals != null && settings.NormalWeight > 0.0f;
            bool includeTangents = mesh.Tangents != null && settings.TangentWeight > 0.0f;
            bool includeUv0 = mesh.UV0 != null && uvWeight > 0.0f;
            bool includeColors = mesh.VertexColors != null && settings.ColorWeight > 0.0f;
            bool includeTexCoord3 = mesh.TexCoord3 != null && settings.TexCoord3Weight > 0.0f;

            int attributeCount = 0;
            attributeCount += includeNormals ? 3 : 0;
            attributeCount += includeTangents ? 4 : 0;
            attributeCount += includeUv0 ? mesh.UV0Dimension : 0;
            attributeCount += includeColors ? 4 : 0;
            attributeCount += includeTexCoord3 ? 4 : 0;
            if (attributeCount > Rendering.Lod.Native.MeshOptimizerNative.kMaxAttributeCount)
            {
                throw new InvalidOperationException("LOD attribute packing exceeded meshoptimizer's limit.");
            }

            int positionValueCount = checked(mesh.VertexCount * 3);
            int attributeValueCount = checked(mesh.VertexCount * attributeCount);
            float[] positions = new float[positionValueCount];
            float[] attributes = attributeCount > 0 ? new float[attributeValueCount] : Array.Empty<float>();
            float[] weights = attributeCount > 0 ? new float[attributeCount] : Array.Empty<float>();

            int normalOffset = -1;
            int tangentOffset = -1;
            int uv0Offset = -1;
            int colorOffset = -1;
            int texCoord3Offset = -1;
            int nextOffset = 0;

            if (includeNormals)
            {
                normalOffset = nextOffset;
                SetWeights(weights, nextOffset, 3, settings.NormalWeight);
                nextOffset += 3;
            }

            if (includeTangents)
            {
                tangentOffset = nextOffset;
                SetWeights(weights, nextOffset, 4, settings.TangentWeight);
                nextOffset += 4;
            }

            if (includeUv0)
            {
                uv0Offset = nextOffset;
                SetWeights(weights, nextOffset, mesh.UV0Dimension, uvWeight);
                nextOffset += mesh.UV0Dimension;
            }

            if (includeColors)
            {
                colorOffset = nextOffset;
                SetWeights(weights, nextOffset, 4, settings.ColorWeight);
                nextOffset += 4;
            }

            if (includeTexCoord3)
            {
                texCoord3Offset = nextOffset;
                SetWeights(weights, nextOffset, 4, settings.TexCoord3Weight);
            }

            float minimumX = float.MaxValue;
            float minimumY = float.MaxValue;
            float minimumZ = float.MaxValue;
            float maximumX = float.MinValue;
            float maximumY = float.MinValue;
            float maximumZ = float.MinValue;

            for (int vertexIndex = 0; vertexIndex < mesh.VertexCount; ++vertexIndex)
            {
                Vector3 sourcePosition = mesh.Vertices[vertexIndex];
                float x = ScalePosition(sourcePosition.x, scaleX);
                float y = ScalePosition(sourcePosition.y, scaleY);
                float z = ScalePosition(sourcePosition.z, scaleZ);
                int positionOffset = vertexIndex * 3;
                positions[positionOffset] = x;
                positions[positionOffset + 1] = y;
                positions[positionOffset + 2] = z;

                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                minimumZ = Math.Min(minimumZ, z);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
                maximumZ = Math.Max(maximumZ, z);

                int attributeBase = vertexIndex * attributeCount;
                if (normalOffset >= 0)
                {
                    Vector3 normal = mesh.Normals[vertexIndex];
                    Normalize((double)normal.x / scaleX, (double)normal.y / scaleY, (double)normal.z / scaleZ, 0.0f, 1.0f, 0.0f, out float normalX, out float normalY, out float normalZ);
                    attributes[attributeBase + normalOffset] = normalX;
                    attributes[attributeBase + normalOffset + 1] = normalY;
                    attributes[attributeBase + normalOffset + 2] = normalZ;
                }

                if (tangentOffset >= 0)
                {
                    Vector4 tangent = mesh.Tangents[vertexIndex];
                    Normalize((double)tangent.x * scaleX, (double)tangent.y * scaleY, (double)tangent.z * scaleZ, 1.0f, 0.0f, 0.0f, out float tangentX, out float tangentY, out float tangentZ);
                    attributes[attributeBase + tangentOffset] = tangentX;
                    attributes[attributeBase + tangentOffset + 1] = tangentY;
                    attributes[attributeBase + tangentOffset + 2] = tangentZ;
                    attributes[attributeBase + tangentOffset + 3] = tangent.w;
                }

                if (uv0Offset >= 0)
                {
                    Vector4 uv0 = mesh.UV0[vertexIndex];
                    attributes[attributeBase + uv0Offset] = uv0.x;
                    attributes[attributeBase + uv0Offset + 1] = uv0.y;
                    if (mesh.UV0Dimension >= 3)
                    {
                        attributes[attributeBase + uv0Offset + 2] = uv0.z;
                    }

                    if (mesh.UV0Dimension >= 4)
                    {
                        attributes[attributeBase + uv0Offset + 3] = uv0.w;
                    }
                }

                if (colorOffset >= 0)
                {
                    Color32 color = mesh.VertexColors[vertexIndex];
                    attributes[attributeBase + colorOffset] = color.r / 255.0f;
                    attributes[attributeBase + colorOffset + 1] = color.g / 255.0f;
                    attributes[attributeBase + colorOffset + 2] = color.b / 255.0f;
                    attributes[attributeBase + colorOffset + 3] = color.a / 255.0f;
                }

                if (texCoord3Offset >= 0)
                {
                    Vector4 texCoord3 = mesh.TexCoord3[vertexIndex];
                    attributes[attributeBase + texCoord3Offset] = texCoord3.x;
                    attributes[attributeBase + texCoord3Offset + 1] = texCoord3.y;
                    attributes[attributeBase + texCoord3Offset + 2] = texCoord3.z;
                    attributes[attributeBase + texCoord3Offset + 3] = texCoord3.w;
                }
            }

            float positionExtent = Math.Max(maximumX - minimumX, Math.Max(maximumY - minimumY, maximumZ - minimumZ));
            if (!IsFinite(positionExtent))
            {
                throw new InvalidOperationException("LOD position extent exceeds float range.");
            }

            return new LodMetricData(positions, attributes, weights, attributeCount, positionExtent);
        }

        internal static LodMetricData BuildPositionMetric(LodMeshData mesh, Vector3 objectScale)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (!IsFinite(objectScale.x) || !IsFinite(objectScale.y) || !IsFinite(objectScale.z))
            {
                throw new ArgumentException("LOD object scale must be finite.", nameof(objectScale));
            }

            float scaleX = SanitizeScale(objectScale.x);
            float scaleY = SanitizeScale(objectScale.y);
            float scaleZ = SanitizeScale(objectScale.z);
            float[] positions = new float[checked(mesh.VertexCount * 3)];
            float minimumX = float.MaxValue;
            float minimumY = float.MaxValue;
            float minimumZ = float.MaxValue;
            float maximumX = float.MinValue;
            float maximumY = float.MinValue;
            float maximumZ = float.MinValue;

            for (int vertexIndex = 0; vertexIndex < mesh.VertexCount; ++vertexIndex)
            {
                Vector3 sourcePosition = mesh.Vertices[vertexIndex];
                float x = ScalePosition(sourcePosition.x, scaleX);
                float y = ScalePosition(sourcePosition.y, scaleY);
                float z = ScalePosition(sourcePosition.z, scaleZ);
                int positionOffset = vertexIndex * 3;
                positions[positionOffset] = x;
                positions[positionOffset + 1] = y;
                positions[positionOffset + 2] = z;

                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                minimumZ = Math.Min(minimumZ, z);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
                maximumZ = Math.Max(maximumZ, z);
            }

            float positionExtent = Math.Max(maximumX - minimumX, Math.Max(maximumY - minimumY, maximumZ - minimumZ));
            if (!IsFinite(positionExtent))
            {
                throw new InvalidOperationException("LOD position extent exceeds float range.");
            }

            return new LodMetricData(positions, Array.Empty<float>(), Array.Empty<float>(), 0, positionExtent);
        }

        internal static LodMetricData BuildUvMetric(LodMeshData mesh, Vector3 objectScale, LodGenerationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            LodMetricData positionMetric = BuildPositionMetric(mesh, objectScale);
            if (mesh.UV0 == null)
            {
                return positionMetric;
            }

            int attributeCount = mesh.UV0Dimension;
            float[] attributes = new float[checked(mesh.VertexCount * attributeCount)];
            float[] weights = new float[attributeCount];
            SetWeights(weights, 0, attributeCount, CalculateUvWeight(mesh, settings));

            for (int vertexIndex = 0; vertexIndex < mesh.VertexCount; ++vertexIndex)
            {
                Vector4 uv0 = mesh.UV0[vertexIndex];
                int attributeOffset = vertexIndex * attributeCount;
                attributes[attributeOffset] = uv0.x;
                attributes[attributeOffset + 1] = uv0.y;
                if (attributeCount >= 3)
                {
                    attributes[attributeOffset + 2] = uv0.z;
                }

                if (attributeCount >= 4)
                {
                    attributes[attributeOffset + 3] = uv0.w;
                }
            }

            return new LodMetricData(positionMetric.Positions, attributes, weights, attributeCount, positionMetric.PositionExtent);
        }

        internal static Vector4[] CalculateTangents(LodMeshData mesh)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (mesh.UV0 == null || mesh.Normals == null)
            {
                return null;
            }

            Vector3[] tangentS = new Vector3[mesh.VertexCount];
            Vector3[] tangentT = new Vector3[mesh.VertexCount];

            for (int triangleOffset = 0; triangleOffset < mesh.Triangles.Length; triangleOffset += 3)
            {
                int index1 = mesh.Triangles[triangleOffset];
                int index2 = mesh.Triangles[triangleOffset + 1];
                int index3 = mesh.Triangles[triangleOffset + 2];

                Vector3 vertex1 = mesh.Vertices[index1];
                Vector3 vertex2 = mesh.Vertices[index2];
                Vector3 vertex3 = mesh.Vertices[index3];
                Vector4 uv1 = mesh.UV0[index1];
                Vector4 uv2 = mesh.UV0[index2];
                Vector4 uv3 = mesh.UV0[index3];

                float x1 = vertex2.x - vertex1.x;
                float x2 = vertex3.x - vertex1.x;
                float y1 = vertex2.y - vertex1.y;
                float y2 = vertex3.y - vertex1.y;
                float z1 = vertex2.z - vertex1.z;
                float z2 = vertex3.z - vertex1.z;
                float s1 = uv2.x - uv1.x;
                float s2 = uv3.x - uv1.x;
                float t1 = uv2.y - uv1.y;
                float t2 = uv3.y - uv1.y;
                float denominator = s1 * t2 - s2 * t1;
                if (Math.Abs(denominator) < 0.00000001f)
                {
                    continue;
                }

                float reciprocal = 1.0f / denominator;
                Vector3 directionS = new Vector3((t2 * x1 - t1 * x2) * reciprocal, (t2 * y1 - t1 * y2) * reciprocal, (t2 * z1 - t1 * z2) * reciprocal);
                Vector3 directionT = new Vector3((s1 * x2 - s2 * x1) * reciprocal, (s1 * y2 - s2 * y1) * reciprocal, (s1 * z2 - s2 * z1) * reciprocal);

                Add(ref tangentS[index1], directionS);
                Add(ref tangentS[index2], directionS);
                Add(ref tangentS[index3], directionS);
                Add(ref tangentT[index1], directionT);
                Add(ref tangentT[index2], directionT);
                Add(ref tangentT[index3], directionT);
            }

            Vector4[] tangents = new Vector4[mesh.VertexCount];
            for (int vertexIndex = 0; vertexIndex < mesh.VertexCount; ++vertexIndex)
            {
                Vector3 sourceNormal = mesh.Normals[vertexIndex];
                Normalize(sourceNormal.x, sourceNormal.y, sourceNormal.z, 0.0f, 1.0f, 0.0f, out float normalX, out float normalY, out float normalZ);

                Vector3 sourceTangent = tangentS[vertexIndex];
                float projection = normalX * sourceTangent.x + normalY * sourceTangent.y + normalZ * sourceTangent.z;
                float tangentX = sourceTangent.x - normalX * projection;
                float tangentY = sourceTangent.y - normalY * projection;
                float tangentZ = sourceTangent.z - normalZ * projection;
                float tangentLengthSquared = tangentX * tangentX + tangentY * tangentY + tangentZ * tangentZ;

                if (tangentLengthSquared > kMinimumLengthSquared)
                {
                    float reciprocalLength = 1.0f / (float)Math.Sqrt(tangentLengthSquared);
                    tangentX *= reciprocalLength;
                    tangentY *= reciprocalLength;
                    tangentZ *= reciprocalLength;
                }
                else if (Math.Abs(normalY) < 0.999f)
                {
                    Normalize(-normalZ, 0.0f, normalX, 1.0f, 0.0f, 0.0f, out tangentX, out tangentY, out tangentZ);
                }
                else
                {
                    Normalize(0.0f, normalZ, -normalY, 1.0f, 0.0f, 0.0f, out tangentX, out tangentY, out tangentZ);
                }

                Vector3 bitangent = tangentT[vertexIndex];
                float crossX = normalY * tangentZ - normalZ * tangentY;
                float crossY = normalZ * tangentX - normalX * tangentZ;
                float crossZ = normalX * tangentY - normalY * tangentX;
                float handedness = crossX * bitangent.x + crossY * bitangent.y + crossZ * bitangent.z < 0.0f ? -1.0f : 1.0f;
                tangents[vertexIndex] = new Vector4(tangentX, tangentY, tangentZ, handedness);
            }

            return tangents;
        }

        internal static Vector3[] CalculateNormals(LodMeshData mesh)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            Vector3[] normals = new Vector3[mesh.VertexCount];
            for (int triangleOffset = 0; triangleOffset < mesh.Triangles.Length; triangleOffset += 3)
            {
                int index1 = mesh.Triangles[triangleOffset];
                int index2 = mesh.Triangles[triangleOffset + 1];
                int index3 = mesh.Triangles[triangleOffset + 2];
                Vector3 vertex1 = mesh.Vertices[index1];
                Vector3 vertex2 = mesh.Vertices[index2];
                Vector3 vertex3 = mesh.Vertices[index3];

                float edge1X = vertex2.x - vertex1.x;
                float edge1Y = vertex2.y - vertex1.y;
                float edge1Z = vertex2.z - vertex1.z;
                float edge2X = vertex3.x - vertex1.x;
                float edge2Y = vertex3.y - vertex1.y;
                float edge2Z = vertex3.z - vertex1.z;
                Vector3 faceNormal = new Vector3(edge1Y * edge2Z - edge1Z * edge2Y, edge1Z * edge2X - edge1X * edge2Z, edge1X * edge2Y - edge1Y * edge2X);

                Add(ref normals[index1], faceNormal);
                Add(ref normals[index2], faceNormal);
                Add(ref normals[index3], faceNormal);
            }

            for (int vertexIndex = 0; vertexIndex < normals.Length; ++vertexIndex)
            {
                Vector3 fallback = mesh.Normals != null ? mesh.Normals[vertexIndex] : new Vector3(0.0f, 1.0f, 0.0f);
                Normalize(normals[vertexIndex].x, normals[vertexIndex].y, normals[vertexIndex].z, fallback.x, fallback.y, fallback.z, out float normalX, out float normalY, out float normalZ);
                normals[vertexIndex] = new Vector3(normalX, normalY, normalZ);
            }

            return normals;
        }

        private static float CalculateUvWeight(LodMeshData mesh, LodGenerationSettings settings)
        {
            double totalArea = 0.0;
            for (int triangleOffset = 0; triangleOffset < mesh.Triangles.Length; triangleOffset += 3)
            {
                Vector4 uv1 = mesh.UV0[mesh.Triangles[triangleOffset]];
                Vector4 uv2 = mesh.UV0[mesh.Triangles[triangleOffset + 1]];
                Vector4 uv3 = mesh.UV0[mesh.Triangles[triangleOffset + 2]];
                double edge1X = uv2.x - uv1.x;
                double edge1Y = uv2.y - uv1.y;
                double edge2X = uv3.x - uv1.x;
                double edge2Y = uv3.y - uv1.y;
                totalArea += Math.Abs(edge1X * edge2Y - edge1Y * edge2X) * 0.5;
            }

            double averageArea = totalArea / mesh.TriangleCount;
            double calculatedWeight = averageArea > kMinimumUvArea ? 1.0 / Math.Sqrt(averageArea) : settings.MaximumUvWeight;
            return (float)Math.Max(settings.MinimumUvWeight, Math.Min(settings.MaximumUvWeight, calculatedWeight));
        }

        private static float SanitizeScale(float value)
        {
            return Math.Max(Math.Abs(value), kMinimumScale);
        }

        private static void SetWeights(float[] weights, int offset, int count, float value)
        {
            for (int i = 0; i < count; ++i)
            {
                weights[offset + i] = value;
            }
        }

        private static float ScalePosition(float value, float scale)
        {
            double result = (double)value * scale;
            if (result < -float.MaxValue || result > float.MaxValue)
            {
                throw new InvalidOperationException("LOD position and object scale exceed float range.");
            }

            return (float)result;
        }

        private static void Normalize(double x, double y, double z, double fallbackX, double fallbackY, double fallbackZ, out float resultX, out float resultY, out float resultZ)
        {
            double lengthSquared = x * x + y * y + z * z;
            if (lengthSquared <= kMinimumLengthSquared)
            {
                resultX = (float)fallbackX;
                resultY = (float)fallbackY;
                resultZ = (float)fallbackZ;
                return;
            }

            double reciprocalLength = 1.0 / Math.Sqrt(lengthSquared);
            resultX = (float)(x * reciprocalLength);
            resultY = (float)(y * reciprocalLength);
            resultZ = (float)(z * reciprocalLength);
        }

        private static void Add(ref Vector3 target, Vector3 value)
        {
            target.x += value.x;
            target.y += value.y;
            target.z += value.z;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}