using System;
using System.Runtime.InteropServices;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Materials;

namespace PO2.Conversion
{
    // Conversion runs on a detached worker.
    // A cached Burst function pointer keeps the packed-stream work on that worker
    // while removing the super slow managed per-vertex copies frpm the previous implementation
    [BurstCompile]
    internal static unsafe class NativeVertexStreamDecoder
    {
        private enum StreamEncoding
        {
            None = 0,
            Float32 = 1,
            Float16 = 2,
            UnpackedFloat3 = 3,
            UnpackedFloat4 = 4,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeStreamDecodeContext
        {
            public byte* Positions;
            public int PositionEncoding;
            public int PositionDimension;

            public byte* Normals;
            public int NormalEncoding;
            public int NormalDimension;

            public byte* Tangents;
            public int TangentEncoding;
            public int TangentDimension;

            public byte* UV0;
            public int UV0Encoding;
            public int UV0Dimension;

            public byte* Colors;
            public int HasColors;

            public byte* TexCoord1;

            public byte* TexCoord3;
            public int TexCoord1Encoding;
            public int TexCoord3Encoding;
            public int TexCoord1Dimension;
            public int TexCoord3Dimension;

            public float3* DestinationPositions;
            public float3* DestinationNormals;
            public float4* DestinationTangents;
            public float4* DestinationUV0;
            public byte* DestinationColors;
            public float4* DestinationTexCoord1;
            public float4* DestinationTexCoord3;
            public int* DestinationTriangleAreaIds;

            public int VertexCount;
            public int TriangleCount;
            public int SurfaceAreaId;
            public ConversionTransform Transform;
            public float3 BoundsMin;
            public float3 BoundsMax;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StreamDecodeKernel(ref NativeStreamDecodeContext context);

        private static readonly object s_KernelLock = new object();
        private static FunctionPointer<StreamDecodeKernel> s_Function;
        private static StreamDecodeKernel s_Kernel;
        private static bool s_InitializationAttempted;
        private static string s_InitializationFailure = string.Empty;

        public static bool TryDecode(AssetConversionPartWorkItem source, out AssetConversionDecodedStreams decoded, out string fallbackReason)
        {
            decoded = null;
            fallbackReason = string.Empty;

            if (source == null || !source.GeometryData.IsValid)
            {
                fallbackReason = "The captured GeometryAsset payload is invalid.";
                return false;
            }

            if (!TryGetKernel(out StreamDecodeKernel kernel, out fallbackReason))
            {
                return false;
            }

            ref GeometryAsset.Data data = ref source.GeometryData;
            int totalVertexCount = GeometryAsset.GetAllVertexCount(ref data);
            int totalIndexCount = GeometryAsset.GetAllIndicesCount(ref data);
            if (totalVertexCount <= 0 || totalIndexCount <= 0 || totalIndexCount % 3 != 0)
            {
                fallbackReason = "The captured geometry counts are invalid.";
                return false;
            }

            Vector3[] vertices = new Vector3[totalVertexCount];
            Vector3[] normals = new Vector3[totalVertexCount];
            Vector4[] tangents = new Vector4[totalVertexCount];
            Vector4[] uv0 = new Vector4[totalVertexCount];
            Color32[] vertexColors = new Color32[totalVertexCount];
            Vector4[] texCoord1 = AssetMeshExtractor.HasTexCoord1(ref data) ? new Vector4[totalVertexCount] : null;
            Vector4[] texCoord3 = new Vector4[totalVertexCount];
            int[] triangleAreaIds = new int[totalIndexCount / 3];
            VertexAttributeFlags[] sourceVertexAttributes = new VertexAttributeFlags[data.meshCount];
            AssetConversionRegionRange[] regionRanges = new AssetConversionRegionRange[data.meshCount];

            bool allNormalsPresent = true;
            bool allTangentsPresent = true;
            bool allUV0Present = true;
            bool anyVertexColorsPresent = false;
            bool anyTexCoord1Present = false;
            bool anyTexCoord3Present = false;
            int vertexOffset = 0;
            int triangleOffset = 0;
            ConversionBoundsSnapshot localBounds = default(ConversionBoundsSnapshot);

            fixed (Vector3* destinationPositions = vertices)
            fixed (Vector3* destinationNormals = normals)
            fixed (Vector4* destinationTangents = tangents)
            fixed (Vector4* destinationUV0 = uv0)
            fixed (Color32* destinationColors = vertexColors)
            fixed (Vector4* destinationTexCoord1 = texCoord1)
            fixed (Vector4* destinationTexCoord3 = texCoord3)
            fixed (int* destinationTriangleAreaIds = triangleAreaIds)
            {
                for (int meshIndex = 0; meshIndex < data.meshCount; meshIndex++)
                {
                    int vertexCount = GeometryAsset.GetVertexCount(ref data, meshIndex);
                    int indexCount = GeometryAsset.GetIndicesCount(ref data, meshIndex);

                    if (vertexCount <= 0 || indexCount <= 0 || indexCount % 3 != 0)
                    {
                        fallbackReason = $"Geometry region {meshIndex} has invalid counts.";
                        return false;
                    }

                    int regionTriangleCount = indexCount / 3;
                    regionRanges[meshIndex] = new AssetConversionRegionRange(meshIndex, vertexOffset, vertexCount, triangleOffset, regionTriangleCount);

                    GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.Position, out VertexAttributeFormat positionFormat, out int positionDimension);
                    if (positionDimension != 3 || !TryGetRawEncoding(positionFormat, positionDimension, 3, out StreamEncoding positionEncoding))
                    {
                        fallbackReason = $"Position region {meshIndex} uses unsupported " + $"format={positionFormat}, dimension={positionDimension}.";
                        return false;
                    }

                    NativeSlice<byte> positionData = GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.Position);
                    if (!HasRequiredBytes(positionData, vertexCount, positionFormat, positionDimension))
                    {
                        fallbackReason = $"Position region {meshIndex} is shorter than expected.";
                        return false;
                    }

                    NativeSlice<byte> normalData = default(NativeSlice<byte>);
                    NativeSlice<byte> tangentData = default(NativeSlice<byte>);
                    NativeSlice<byte> uv0Data = default(NativeSlice<byte>);
                    NativeSlice<byte> colorData = default(NativeSlice<byte>);
                    NativeSlice<byte> texCoord1Data = default(NativeSlice<byte>);
                    NativeSlice<byte> texCoord3Data = default(NativeSlice<byte>);
                    NativeArray<MeshNormal> unpackedNormals = default(NativeArray<MeshNormal>);
                    NativeArray<MeshTangent> unpackedTangents = default(NativeArray<MeshTangent>);

                    try
                    {
                        if (!TryPrepareNormalStream(ref data, meshIndex, vertexCount, ref normalData, ref unpackedNormals, out StreamEncoding normalEncoding, out int normalDimension, out bool hasNormals, out fallbackReason))
                        {
                            return false;
                        }

                        if (!TryPrepareTangentStream(ref data, meshIndex, vertexCount, ref tangentData, ref unpackedTangents, out StreamEncoding tangentEncoding, out int tangentDimension, out bool hasTangents, out fallbackReason))
                        {
                            return false;
                        }

                        if (!TryPrepareUV0Stream(ref data, meshIndex, vertexCount, ref uv0Data, out StreamEncoding uv0Encoding, out int uv0Dimension, out bool hasUV0, out fallbackReason))
                        {
                            return false;
                        }

                        if (!TryPrepareColorStream(ref data, meshIndex, vertexCount, ref colorData, out bool hasColors, out fallbackReason))
                        {
                            return false;
                        }

                        if (!TryPrepareTexCoord1Stream(ref data, meshIndex, vertexCount, ref texCoord1Data, out StreamEncoding texCoord1Encoding, out int texCoord1Dimension, out bool hasTexCoord1, out fallbackReason))
                        {
                            return false;
                        }

                        if (!TryPrepareTexCoord3Stream(ref data, meshIndex, vertexCount, ref texCoord3Data, out StreamEncoding texCoord3Encoding, out int texCoord3Dimension, out bool hasTexCoord3, out fallbackReason))
                        {
                            return false;
                        }

                        allNormalsPresent &= hasNormals;
                        allTangentsPresent &= hasTangents;
                        allUV0Present &= hasUV0;
                        anyVertexColorsPresent |= hasColors;
                        anyTexCoord1Present |= hasTexCoord1;
                        anyTexCoord3Present |= hasTexCoord3;

                        if (uv0Dimension > 2)
                        {
                            sourceVertexAttributes[meshIndex] |= VertexAttributeFlags.UV0FourComponent;
                        }

                        if (hasColors)
                        {
                            sourceVertexAttributes[meshIndex] |= VertexAttributeFlags.VertexColor;
                        }

                        if (hasTexCoord1)
                        {
                            sourceVertexAttributes[meshIndex] |= VertexAttributeFlags.TexCoord1;
                        }

                        if (hasTexCoord3)
                        {
                            sourceVertexAttributes[meshIndex] |= VertexAttributeFlags.TexCoord3;
                        }

                        byte* normalPointer = GetStreamPointer(normalData, unpackedNormals, normalEncoding);
                        byte* tangentPointer = GetStreamPointer(tangentData, unpackedTangents, tangentEncoding);

                        NativeStreamDecodeContext context = new NativeStreamDecodeContext
                        {
                            Positions = GetPointer(positionData),
                            PositionEncoding = (int)positionEncoding,
                            PositionDimension = positionDimension,
                            Normals = normalPointer,
                            NormalEncoding = (int)normalEncoding,
                            NormalDimension = normalDimension,
                            Tangents = tangentPointer,
                            TangentEncoding = (int)tangentEncoding,
                            TangentDimension = tangentDimension,
                            UV0 = GetPointer(uv0Data),
                            UV0Encoding = (int)uv0Encoding,
                            UV0Dimension = uv0Dimension,
                            Colors = GetPointer(colorData),
                            HasColors = hasColors ? 1 : 0,
                            TexCoord1 = GetPointer(texCoord1Data),
                            TexCoord3 = GetPointer(texCoord3Data),
                            TexCoord1Encoding = (int)texCoord1Encoding,
                            TexCoord3Encoding = (int)texCoord3Encoding,
                            TexCoord1Dimension = texCoord1Dimension,
                            TexCoord3Dimension = texCoord3Dimension,
                            DestinationPositions = (float3*)destinationPositions + vertexOffset,
                            DestinationNormals = (float3*)destinationNormals + vertexOffset,
                            DestinationTangents = (float4*)destinationTangents + vertexOffset,
                            DestinationUV0 = (float4*)destinationUV0 + vertexOffset,
                            DestinationColors = (byte*)destinationColors + vertexOffset * 4,
                            DestinationTexCoord1 = (float4*)destinationTexCoord1 + vertexOffset,
                            DestinationTexCoord3 = (float4*)destinationTexCoord3 + vertexOffset,
                            DestinationTriangleAreaIds = destinationTriangleAreaIds + triangleOffset,
                            VertexCount = vertexCount,
                            TriangleCount = regionTriangleCount,
                            SurfaceAreaId = meshIndex,
                            Transform = new ConversionTransform(new float3(source.SourceMeshPosition.x, source.SourceMeshPosition.y, source.SourceMeshPosition.z), new quaternion(source.SourceMeshRotation.x, source.SourceMeshRotation.y, source.SourceMeshRotation.z, source.SourceMeshRotation.w), new float3(source.SourceMeshScale.x, source.SourceMeshScale.y, source.SourceMeshScale.z)),
                        };
                        kernel(ref context);

                        ConversionBoundsSnapshot regionBounds = new ConversionBoundsSnapshot
                        {
                            HasBounds = true,
                            Min = new Vector3(context.BoundsMin.x, context.BoundsMin.y, context.BoundsMin.z),
                            Max = new Vector3(context.BoundsMax.x, context.BoundsMax.y, context.BoundsMax.z),
                        };
                        localBounds.Encapsulate(regionBounds);
                    }
                    finally
                    {
                        if (unpackedNormals.IsCreated)
                        {
                            unpackedNormals.Dispose();
                        }

                        if (unpackedTangents.IsCreated)
                        {
                            unpackedTangents.Dispose();
                        }
                    }

                    vertexOffset += vertexCount;
                    triangleOffset += regionTriangleCount;
                }
            }

            if (vertexOffset != totalVertexCount || triangleOffset != totalIndexCount / 3 || !localBounds.HasBounds)
            {
                fallbackReason = "The native stream decoder produced incomplete output.";
                return false;
            }

            decoded = new AssetConversionDecodedStreams(vertices, allNormalsPresent ? normals : null, allTangentsPresent ? tangents : null, allUV0Present ? uv0 : null, anyVertexColorsPresent ? vertexColors : null, anyTexCoord1Present ? texCoord1 : null, anyTexCoord3Present ? texCoord3 : null, triangleAreaIds, sourceVertexAttributes, regionRanges, localBounds);
            return true;
        }

        private static bool TryPrepareNormalStream(ref GeometryAsset.Data data, int meshIndex, int vertexCount, ref NativeSlice<byte> sourceData, ref NativeArray<MeshNormal> unpacked, out StreamEncoding encoding, out int dimension, out bool present, out string fallbackReason)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.Normal, out VertexAttributeFormat format, out dimension);

            fallbackReason = string.Empty;
            present = dimension > 0;
            encoding = StreamEncoding.None;
            if (!present)
            {
                return true;
            }

            sourceData = GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.Normal);
            if (!HasRequiredBytes(sourceData, vertexCount, format, dimension))
            {
                fallbackReason = $"Normal region {meshIndex} is shorter than expected.";
                return false;
            }

            if ((format == VertexAttributeFormat.Float16 || format == VertexAttributeFormat.Float32) && dimension == 3 && TryGetRawEncoding(format, dimension, 3, out encoding))
            {
                return true;
            }

            if (format != VertexAttributeFormat.SNorm16 || dimension != 2)
            {
                fallbackReason = $"Normal region {meshIndex} uses unsupported " + $"format={format}, dimension={dimension}.";
                return false;
            }

            unpacked = new NativeArray<MeshNormal>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            MeshNormal.Unpack(sourceData, unpacked, vertexCount, format, dimension);

            encoding = StreamEncoding.UnpackedFloat3;
            dimension = 3;
            return true;
        }

        private static bool TryPrepareTangentStream(ref GeometryAsset.Data data, int meshIndex, int vertexCount, ref NativeSlice<byte> sourceData, ref NativeArray<MeshTangent> unpacked, out StreamEncoding encoding, out int dimension, out bool present, out string fallbackReason)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.Tangent, out VertexAttributeFormat format, out dimension);

            fallbackReason = string.Empty;
            present = dimension > 0;
            encoding = StreamEncoding.None;
            if (!present)
            {
                return true;
            }

            sourceData = GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.Tangent);
            if (!HasRequiredBytes(sourceData, vertexCount, format, dimension))
            {
                fallbackReason = $"Tangent region {meshIndex} is shorter than expected.";
                return false;
            }

            if ((format == VertexAttributeFormat.Float16 || format == VertexAttributeFormat.Float32) && dimension == 4 && TryGetRawEncoding(format, dimension, 4, out encoding))
            {
                return true;
            }

            if (format != VertexAttributeFormat.Float32 || dimension != 1)
            {
                fallbackReason = $"Tangent region {meshIndex} uses unsupported " + $"format={format}, dimension={dimension}.";
                return false;
            }

            unpacked = new NativeArray<MeshTangent>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            MeshTangent.Unpack(sourceData, unpacked, vertexCount, format, dimension);

            encoding = StreamEncoding.UnpackedFloat4;
            dimension = 4;
            return true;
        }

        private static bool TryPrepareUV0Stream(ref GeometryAsset.Data data, int meshIndex, int vertexCount, ref NativeSlice<byte> sourceData, out StreamEncoding encoding, out int dimension, out bool present, out string fallbackReason)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.TexCoord0, out VertexAttributeFormat format, out dimension);

            fallbackReason = string.Empty;
            present = dimension > 0;
            encoding = StreamEncoding.None;
            if (!present)
            {
                return true;
            }

            if (!TryGetRawEncoding(format, dimension, 4, out encoding) || dimension == 1)
            {
                fallbackReason = $"UV0 region {meshIndex} uses unsupported " + $"format={format}, dimension={dimension}.";
                return false;
            }

            sourceData = GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.TexCoord0);
            if (!HasRequiredBytes(sourceData, vertexCount, format, dimension))
            {
                fallbackReason = $"UV0 region {meshIndex} is shorter than expected.";
                return false;
            }

            return true;
        }

        private static bool TryPrepareColorStream(ref GeometryAsset.Data data, int meshIndex, int vertexCount, ref NativeSlice<byte> sourceData, out bool present, out string fallbackReason)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.Color, out VertexAttributeFormat format, out int dimension);

            fallbackReason = string.Empty;
            present = dimension > 0;
            if (!present)
            {
                return true;
            }

            if (format != VertexAttributeFormat.UNorm8 || dimension != 4)
            {
                fallbackReason = $"Color region {meshIndex} uses unsupported " + $"format={format}, dimension={dimension}.";
                return false;
            }

            sourceData = GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.Color);
            if (sourceData.Length < checked(vertexCount * 4))
            {
                fallbackReason = $"Color region {meshIndex} is shorter than expected.";
                return false;
            }

            return true;
        }

        private static bool TryPrepareTexCoord1Stream(ref GeometryAsset.Data data, int meshIndex, int vertexCount, ref NativeSlice<byte> sourceData, out StreamEncoding encoding, out int dimension, out bool present, out string fallbackReason)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.TexCoord1, out VertexAttributeFormat format, out dimension);

            fallbackReason = string.Empty;
            present = dimension > 0;
            encoding = StreamEncoding.None;
            if (!present)
            {
                return true;
            }

            if ((dimension < 1 || dimension > 4) || !TryGetRawEncoding(format, dimension, 4, out encoding))
            {
                fallbackReason = $"TexCoord1 region {meshIndex} uses unsupported " + $"format={format}, dimension={dimension}.";
                return false;
            }

            sourceData = GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.TexCoord1);
            if (!HasRequiredBytes(sourceData, vertexCount, format, dimension))
            {
                fallbackReason = $"TexCoord1 region {meshIndex} is shorter than expected.";
                return false;
            }

            return true;
        }

        private static bool TryPrepareTexCoord3Stream(ref GeometryAsset.Data data, int meshIndex, int vertexCount, ref NativeSlice<byte> sourceData, out StreamEncoding encoding, out int dimension, out bool present, out string fallbackReason)
        {
            GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.TexCoord3, out VertexAttributeFormat format, out dimension);

            fallbackReason = string.Empty;
            present = dimension > 0;
            encoding = StreamEncoding.None;
            if (!present)
            {
                return true;
            }

            if (dimension != 4 || !TryGetRawEncoding(format, dimension, 4, out encoding))
            {
                fallbackReason = $"TexCoord3 region {meshIndex} uses unsupported " + $"format={format}, dimension={dimension}.";
                return false;
            }

            sourceData = GeometryAsset.GetAttributeData(ref data, meshIndex, VertexAttribute.TexCoord3);
            if (!HasRequiredBytes(sourceData, vertexCount, format, dimension))
            {
                fallbackReason = $"TexCoord3 region {meshIndex} is shorter than expected.";
                return false;
            }

            return true;
        }

        private static bool TryGetRawEncoding(VertexAttributeFormat format, int dimension, int maximumDimension, out StreamEncoding encoding)
        {
            encoding = StreamEncoding.None;
            if (dimension <= 0 || dimension > maximumDimension)
            {
                return false;
            }

            if (format == VertexAttributeFormat.Float32)
            {
                encoding = StreamEncoding.Float32;
                return true;
            }

            if (format == VertexAttributeFormat.Float16)
            {
                encoding = StreamEncoding.Float16;
                return true;
            }

            return false;
        }

        private static bool HasRequiredBytes(NativeSlice<byte> data, int vertexCount, VertexAttributeFormat format, int dimension)
        {
            int bytesPerComponent;
            if (format == VertexAttributeFormat.Float16 || format == VertexAttributeFormat.SNorm16)
            {
                bytesPerComponent = 2;
            }
            else if (format == VertexAttributeFormat.Float32)
            {
                bytesPerComponent = 4;
            }
            else
            {
                return false;
            }

            long required = (long)vertexCount * dimension * bytesPerComponent;
            return required <= data.Length;
        }

        private static byte* GetStreamPointer(NativeSlice<byte> rawData, NativeArray<MeshNormal> unpacked, StreamEncoding encoding)
        {
            if (encoding == StreamEncoding.None)
            {
                return null;
            }

            if (encoding == StreamEncoding.UnpackedFloat3)
            {
                return (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(unpacked);
            }

            return GetPointer(rawData);
        }

        private static byte* GetStreamPointer(NativeSlice<byte> rawData, NativeArray<MeshTangent> unpacked, StreamEncoding encoding)
        {
            if (encoding == StreamEncoding.None)
            {
                return null;
            }

            if (encoding == StreamEncoding.UnpackedFloat4)
            {
                return (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(unpacked);
            }

            return GetPointer(rawData);
        }

        private static byte* GetPointer(NativeSlice<byte> data)
        {
            return data.Length > 0 ? (byte*)NativeSliceUnsafeUtility.GetUnsafeReadOnlyPtr(data) : null;
        }

        private static bool TryGetKernel(out StreamDecodeKernel kernel, out string failureReason)
        {
            kernel = null;
            failureReason = string.Empty;

            if (!BurstCompiler.IsEnabled)
            {
                failureReason = "Burst compilation is disabled.";
                return false;
            }

            lock (s_KernelLock)
            {
                if (!s_InitializationAttempted)
                {
                    s_InitializationAttempted = true;
                    try
                    {
                        s_Function = BurstCompiler.CompileFunctionPointer<StreamDecodeKernel>(new StreamDecodeKernel(DecodeKernel));
                        s_Kernel = s_Function.Invoke;
                    }
                    catch (Exception exception)
                    {
                        s_InitializationFailure = "Burst stream kernel could not compile: " + exception.Message;
                    }
                }

                if (s_Kernel == null)
                {
                    failureReason = string.IsNullOrWhiteSpace(s_InitializationFailure) ? "The Burst stream kernel is unavailable." : s_InitializationFailure;
                    return false;
                }

                kernel = s_Kernel;
                return true;
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void DecodeKernel(ref NativeStreamDecodeContext context)
        {
            for (int i = 0; i < context.VertexCount; i++)
            {
                float3 position = ReadFloat3(context.Positions, (StreamEncoding)context.PositionEncoding, context.PositionDimension, i);
                float3 transformedPosition = context.Transform.TransformPoint(position);
                context.DestinationPositions[i] = transformedPosition;

                if (i == 0)
                {
                    context.BoundsMin = transformedPosition;
                    context.BoundsMax = transformedPosition;
                }
                else
                {
                    context.BoundsMin = math.min(context.BoundsMin, transformedPosition);
                    context.BoundsMax = math.max(context.BoundsMax, transformedPosition);
                }

                if (context.NormalEncoding != (int)StreamEncoding.None)
                {
                    float3 normal = ReadFloat3(context.Normals, (StreamEncoding)context.NormalEncoding, context.NormalDimension, i);
                    context.DestinationNormals[i] = context.Transform.TransformNormal(normal);
                }

                if (context.TangentEncoding != (int)StreamEncoding.None)
                {
                    float4 tangent = ReadFloat4(context.Tangents, (StreamEncoding)context.TangentEncoding, context.TangentDimension, i);
                    float3 direction = context.Transform.TransformTangent(tangent.xyz);
                    context.DestinationTangents[i] = new float4(direction, tangent.w);
                }

                if (context.UV0Encoding != (int)StreamEncoding.None)
                {
                    context.DestinationUV0[i] = ReadFloat4(context.UV0, (StreamEncoding)context.UV0Encoding, context.UV0Dimension, i);
                }

                int colorOffset = i * 4;
                if (context.HasColors != 0)
                {
                    context.DestinationColors[colorOffset] = context.Colors[colorOffset];
                    context.DestinationColors[colorOffset + 1] = context.Colors[colorOffset + 1];
                    context.DestinationColors[colorOffset + 2] = context.Colors[colorOffset + 2];
                    context.DestinationColors[colorOffset + 3] = context.Colors[colorOffset + 3];
                }
                else
                {
                    context.DestinationColors[colorOffset] = 255;
                    context.DestinationColors[colorOffset + 1] = 255;
                    context.DestinationColors[colorOffset + 2] = 255;
                    context.DestinationColors[colorOffset + 3] = 255;
                }

                if (context.TexCoord1Encoding != (int)StreamEncoding.None)
                {
                    float4 authored = ReadFloat4(context.TexCoord1, (StreamEncoding)context.TexCoord1Encoding, context.TexCoord1Dimension, i);
                    context.DestinationTexCoord1[i] = authored;
                }

                if (context.TexCoord3Encoding != (int)StreamEncoding.None)
                {
                    float4 authored = ReadFloat4(context.TexCoord3, (StreamEncoding)context.TexCoord3Encoding, context.TexCoord3Dimension, i);
                    quaternion baked = math.mul(context.Transform.Rotation, new quaternion(authored));
                    context.DestinationTexCoord3[i] = baked.value;
                }
            }

            for (int triangleIndex = 0; triangleIndex < context.TriangleCount; triangleIndex++)
            {
                context.DestinationTriangleAreaIds[triangleIndex] = context.SurfaceAreaId;
            }
        }

        private static float3 ReadFloat3(byte* source, StreamEncoding encoding, int dimension, int vertexIndex)
        {
            if (encoding == StreamEncoding.UnpackedFloat3)
            {
                return ((float3*)source)[vertexIndex];
            }

            return new float3(ReadComponent(source, encoding, dimension, vertexIndex, 0), ReadComponent(source, encoding, dimension, vertexIndex, 1), ReadComponent(source, encoding, dimension, vertexIndex, 2));
        }

        private static float4 ReadFloat4(byte* source, StreamEncoding encoding, int dimension, int vertexIndex)
        {
            if (encoding == StreamEncoding.UnpackedFloat4)
            {
                return ((float4*)source)[vertexIndex];
            }

            return new float4(ReadComponent(source, encoding, dimension, vertexIndex, 0), ReadComponent(source, encoding, dimension, vertexIndex, 1), ReadComponent(source, encoding, dimension, vertexIndex, 2), ReadComponent(source, encoding, dimension, vertexIndex, 3));
        }

        private static float ReadComponent(byte* source, StreamEncoding encoding, int dimension, int vertexIndex, int componentIndex)
        {
            if (componentIndex >= dimension)
            {
                return 0f;
            }

            int component = vertexIndex * dimension + componentIndex;
            if (encoding == StreamEncoding.Float16)
            {
                int offset = component * 2;
                ushort bits = (ushort)(source[offset] | (source[offset + 1] << 8));
                return HalfToSingle(bits);
            }

            int floatOffset = component * 4;
            uint value = (uint)(source[floatOffset] | (source[floatOffset + 1] << 8) | (source[floatOffset + 2] << 16) | (source[floatOffset + 3] << 24));
            return math.asfloat(value);
        }

        private static float HalfToSingle(ushort value)
        {
            uint sign = (uint)(value & 0x8000) << 16;
            uint exponent = (uint)(value >> 10) & 0x1Fu;
            uint mantissa = (uint)value & 0x03FFu;
            uint resultBits;

            if (exponent == 0)
            {
                if (mantissa == 0)
                {
                    resultBits = sign;
                }
                else
                {
                    int shift = 0;
                    while ((mantissa & 0x0400u) == 0)
                    {
                        mantissa <<= 1;
                        shift++;
                    }

                    mantissa &= 0x03FFu;
                    uint adjustedExponent = (uint)(127 - 15 - shift + 1);
                    resultBits = sign | (adjustedExponent << 23) | (mantissa << 13);
                }
            }
            else if (exponent == 31)
            {
                resultBits = sign | 0x7F800000u | (mantissa << 13);
            }
            else
            {
                uint adjustedExponent = exponent + (127u - 15u);
                resultBits = sign | (adjustedExponent << 23) | (mantissa << 13);
            }

            return math.asfloat(resultBits);
        }
    }
}