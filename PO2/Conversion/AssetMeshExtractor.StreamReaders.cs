using System;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Materials;

namespace PO2.Conversion
{
    internal sealed partial class AssetMeshExtractor
    {
        private static bool DecodePositions(ref GeometryAsset.Data data, int meshIndex, int vertexCount, Vector3[] destination, int destinationOffset, SourceMeshTransform sourceTransform, ref ConversionBoundsSnapshot localBounds)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.Position, out VertexAttributeFormat format, out int dimension);

            if (dimension == 0)
            {
                Mod.log.Warn($"[PO Conversion] Geometry mesh {meshIndex} has no Position attribute.");
                return false;
            }

            // Use unpacker from Colossal for packed/quantized position formats.
            // Hand-decoding these would duplicate asset-pipeline assumptions.
            NativeArray<MeshVertex> unpacked = new NativeArray<MeshVertex>(vertexCount, Allocator.TempJob);

            try
            {
                MeshVertex.Unpack(GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.Position), unpacked, vertexCount, format, dimension);

                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3 position = ToVector3(unpacked[i].m_Vertex);
                    Vector3 transformed = sourceTransform.TransformPoint(position);
                    destination[destinationOffset + i] = transformed;
                    localBounds.Encapsulate(transformed);
                }
            }
            finally
            {
                unpacked.Dispose();
            }

            return true;
        }

        private static bool DecodeNormals(ref GeometryAsset.Data data, int meshIndex, int vertexCount, Vector3[] destination, int destinationOffset, SourceMeshTransform sourceTransform)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.Normal, out VertexAttributeFormat format, out int dimension);

            if (dimension == 0)
            {
                return false;
            }

            NativeArray<MeshNormal> unpacked = new NativeArray<MeshNormal>(vertexCount, Allocator.TempJob);

            try
            {
                MeshNormal.Unpack(GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.Normal), unpacked, vertexCount, format, dimension);

                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3 normal = ToVector3(unpacked[i].m_Normal);
                    destination[destinationOffset + i] = sourceTransform.TransformNormal(normal);
                }
            }
            finally
            {
                unpacked.Dispose();
            }

            return true;
        }

        private static bool DecodeTangents(ref GeometryAsset.Data data, int meshIndex, int vertexCount, Vector4[] destination, int destinationOffset, SourceMeshTransform sourceTransform)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.Tangent, out VertexAttributeFormat format, out int dimension);

            if (dimension == 0)
            {
                return false;
            }

            NativeArray<MeshTangent> unpacked = new NativeArray<MeshTangent>(vertexCount, Allocator.TempJob);

            try
            {
                MeshTangent.Unpack(GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.Tangent), unpacked, vertexCount, format, dimension);

                for (int i = 0; i < vertexCount; i++)
                {
                    float4 source = unpacked[i].m_Tangent;
                    Vector3 tangentDirection = sourceTransform.TransformTangent(new Vector3(source.x, source.y, source.z));

                    // xyz follows the baked scale and rotation.
                    // w is tangent handedness and must be copied unchanged.
                    destination[destinationOffset + i] = new Vector4(tangentDirection.x, tangentDirection.y, tangentDirection.z, source.w);
                }
            }
            finally
            {
                unpacked.Dispose();
            }

            return true;
        }

        private static bool DecodeUV0(ref GeometryAsset.Data data, int meshIndex, int vertexCount, Vector4[] destination, int destinationOffset)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.TexCoord0, out VertexAttributeFormat format, out int dimension);

            if (dimension == 0)
            {
                return false;
            }

            // MeshUV0.Unpack only exposes xy.
            // Read supported float formats directly when native shaders have useful data in z/w.
            if (dimension > 2 && (format == VertexAttributeFormat.Float16 || format == VertexAttributeFormat.Float32))
            {
                NativeSlice<byte> source = GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.TexCoord0);
                int bytesPerComponent = format == VertexAttributeFormat.Float16 ? 2 : 4;
                int bytesPerVertex = dimension * bytesPerComponent;

                for (int i = 0; i < vertexCount; i++)
                {
                    int offset = i * bytesPerVertex;
                    float x = ReadFloatComponent(source, offset, format);
                    float y = dimension > 1 ? ReadFloatComponent(source, offset + bytesPerComponent, format) : 0f;
                    float z = dimension > 2 ? ReadFloatComponent(source, offset + bytesPerComponent * 2, format) : 0f;
                    float w = dimension > 3 ? ReadFloatComponent(source, offset + bytesPerComponent * 3, format) : 0f;

                    destination[destinationOffset + i] = new Vector4(x, y, z, w);
                }

                return true;
            }

            NativeArray<MeshUV0> unpacked = new NativeArray<MeshUV0>(vertexCount, Allocator.TempJob);

            try
            {
                MeshUV0.Unpack(GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.TexCoord0), unpacked, vertexCount, format, dimension);

                for (int i = 0; i < vertexCount; i++)
                {
                    float2 uv = unpacked[i].m_Uv;
                    destination[destinationOffset + i] = new Vector4(uv.x, uv.y, 0f, 0f);
                }
            }
            finally
            {
                unpacked.Dispose();
            }

            if (dimension > 2)
            {
                Mod.log.Warn($"[PO Conversion] UV0 mesh {meshIndex} uses unsupported " + $"format={format}, dimension={dimension}; only xy were " + "preserved for this stream.");
            }

            return true;
        }


        private static bool DecodeVertexColors(ref GeometryAsset.Data data, int meshIndex, int vertexCount, Color32[] destination, int destinationOffset)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.Color, out VertexAttributeFormat format, out int dimension);

            if (dimension == 0)
            {
                return false;
            }

            // This is shader metadata on window assets, NOT a display color.
            // Preserve the packed bytes exactly when possible.
            if (format != VertexAttributeFormat.UNorm8 || dimension != 4)
            {
                Mod.log.Warn($"[PO Conversion] Vertex Color mesh {meshIndex} uses " + $"unsupported format={format}, dimension={dimension}; " + "the authored color stream was not preserved.");
                return false;
            }

            NativeSlice<byte> source = GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.Color);

            for (int i = 0; i < vertexCount; i++)
            {
                int offset = i * 4;
                destination[destinationOffset + i] = new Color32(source[offset], source[offset + 1], source[offset + 2], source[offset + 3]);
            }

            return true;
        }

        private static void FillDefaultVertexColors(Color32[] destination, int destinationOffset, int vertexCount)
        {
            // Regions without the stream still need aligned placeholders when
            // another region causes the combined stream to be retained.
            Color32 white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < vertexCount; i++)
            {
                destination[destinationOffset + i] = white;
            }
        }

        // UV1 can be native material data. Keep all components exactly as decoded.
        private static bool DecodeTexCoord1(ref GeometryAsset.Data data, int meshIndex, int vertexCount, Vector4[] destination, int destinationOffset)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.TexCoord1, out VertexAttributeFormat format, out int dimension);
            if (dimension == 0) return false;
            if ((format != VertexAttributeFormat.Float16 && format != VertexAttributeFormat.Float32) || dimension < 1 || dimension > 4)
            {
                throw new InvalidOperationException($"TexCoord1 region {meshIndex} uses unsupported {format}x{dimension}.");
            }

            var bytes = GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.TexCoord1);
            int componentBytes = format == VertexAttributeFormat.Float16 ? 2 : 4;
            int stride = componentBytes * dimension;
            if ((long)vertexCount * stride > bytes.Length)
            {
                throw new InvalidOperationException($"TexCoord1 region {meshIndex} is shorter than expected.");
            }

            for (int i = 0; i < vertexCount; i++)
            {
                int at = i * stride;
                destination[destinationOffset + i] = new Vector4(ReadFloatComponent(bytes, at, format), dimension > 1 ? ReadFloatComponent(bytes, at + componentBytes, format) : 0f, dimension > 2 ? ReadFloatComponent(bytes, at + componentBytes * 2, format) : 0f, dimension > 3 ? ReadFloatComponent(bytes, at + componentBytes * 3, format) : 0f);
            }

            return true;
        }

        internal static bool HasTexCoord1(ref GeometryAsset.Data data)
        {
            for (int i = 0; i < data.meshCount; i++)
            {
                GeometryAsset.GetAttributeFormat(ref data, i, VertexAttribute.TexCoord1, out _, out int dimension);
                if (dimension != 0) return true;
            }

            return false;
        }

        private static bool DecodeTexCoord3(ref GeometryAsset.Data data, int meshIndex, int vertexCount, Vector4[] destination, int destinationOffset, SourceMeshTransform sourceTransform)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.TexCoord3, out VertexAttributeFormat format, out int dimension);

            if (dimension == 0)
            {
                return false;
            }

            if ((format != VertexAttributeFormat.Float32 && format != VertexAttributeFormat.Float16) || dimension != 4)
            {
                Mod.log.Warn($"[PO Conversion] TexCoord3 mesh {meshIndex} uses " + $"unsupported format={format}, dimension={dimension}; " + "the authored TexCoord3 stream was not preserved.");
                return false;
            }

            NativeSlice<byte> source = GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.TexCoord3);
            int bytesPerComponent = format == VertexAttributeFormat.Float16 ? 2 : 4;
            int bytesPerVertex = bytesPerComponent * 4;

            for (int i = 0; i < vertexCount; i++)
            {
                int offset = i * bytesPerVertex;
                Quaternion authoredRotation = new Quaternion(ReadFloatComponent(source, offset, format), ReadFloatComponent(source, offset + bytesPerComponent, format), ReadFloatComponent(source, offset + bytesPerComponent * 2, format), ReadFloatComponent(source, offset + bytesPerComponent * 3, format));

                // TexCoord3 holds window orientation. Bake in the SubMesh rotation too,
                // or the windows rotate differently from the mesh.
                Quaternion bakedRotation = sourceTransform.TransformRotation(authoredRotation);
                destination[destinationOffset + i] = new Vector4(bakedRotation.x, bakedRotation.y, bakedRotation.z, bakedRotation.w);
            }

            return true;
        }

        private ColorMaskData CaptureNativeColorMask(Entity entity, int sourceColorIndex)
        {
            if (!m_EntityManager.HasBuffer<MeshColor>(entity))
            {
                return new ColorMaskData();
            }

            DynamicBuffer<MeshColor> colors = m_EntityManager.GetBuffer<MeshColor>(entity, true);
            if (colors.Length == 0)
            {
                return new ColorMaskData();
            }

            // The caller resolves the original mesh index and any group color offset.
            // A single color is also used as a shared fallback by some simple prefabs.
            int colorIndex = sourceColorIndex;
            if (colorIndex < 0 || colorIndex >= colors.Length)
            {
                if (colors.Length == 1)
                {
                    colorIndex = 0;
                }
                else
                {
                    Mod.log.Warn($"[PO Conversion] MeshColor buffer length={colors.Length} " + $"does not contain source color index {sourceColorIndex}. " + "Native color masks were not copied.");
                    return new ColorMaskData();
                }
            }

            ColorSet set = colors[colorIndex].m_ColorSet;

            // MeshColor is already the resolved ECS color. Color.linear would convert it twice.
            return ColorMaskData.FromSource(set.m_Channel0, set.m_Channel1, set.m_Channel2);
        }

        private static float ReadFloatComponent(NativeSlice<byte> source, int offset, VertexAttributeFormat format)
        {
            if (format == VertexAttributeFormat.Float16)
            {
                return ReadHalf(source, offset);
            }

            uint bits = (uint)(source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16) | (source[offset + 3] << 24));
            return math.asfloat(bits);
        }

        private static float ReadHalf(NativeSlice<byte> source, int offset)
        {
            ushort bits = (ushort)(source[offset] | (source[offset + 1] << 8));
            return HalfToSingle(bits);
        }

        private static float HalfToSingle(ushort value)
        {
            // It's a bitwise floating point converter to take 16-bit floats and make them 32-bit for the cpu 
            // There's no built-in unpacker for the raw four-component streams preserved above
            uint sign = (uint)(value & 0x8000) << 16; //slide first bit to 32nd bit spot
            uint exponent = (uint)(value >> 10) & 0x1Fu; // isolate middle 5 bits
            uint significand = (uint)value & 0x03FFu; // isolate late 10 bits
            uint resultBits;

            if (exponent == 0)
            {
                if (significand == 0)
                {
                    resultBits = sign;
                }
                else
                {
                    int shift = 0;
                    while ((significand & 0x0400u) == 0)
                    {
                        significand <<= 1;
                        shift++;
                    }

                    significand &= 0x03FFu;
                    // 16 bit exponets have bias of 15, 32 bit have bias of 12
                    uint adjustedExponent = (uint)(127 - 15 - shift + 1);
                    resultBits = sign | (adjustedExponent << 23) | (significand << 13);
                }
            }
            else if (exponent == 31)
            {
                resultBits = sign | 0x7F800000u | (significand << 13);
            }
            else
            {
                uint adjustedExponent = exponent + (127u - 15u);
                resultBits = sign | (adjustedExponent << 23) | (significand << 13);
            }

            return math.asfloat(resultBits);
        }
    }
}