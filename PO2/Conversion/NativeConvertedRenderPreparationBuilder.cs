using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using PO2.Geometry;
using PO2.Materials;

namespace PO2.Conversion
{
    // Use each converted region's contiguous range to build a dense vertex remap without dictionaries or growing lists.

    // This is so many pointers ~ be careful! 
    [BurstCompile]
    internal static unsafe class NativeConvertedRenderPreparationBuilder
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RemapContext
        {
            public int* SourceTriangles;
            public int SourceIndexOffset;
            public int IndexCount;
            public int SourceVertexOffset;
            public int SourceVertexCount;
            public int* DenseRemap;
            public int* SourceVertexIndices;
            public int* AreaTriangles;
            public int UniqueVertexCount;
            public int ErrorCode;
            public int ErrorItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CopyContext
        {
            public float3* SourceVertices;
            public float3* SourceNormals;
            public float4* SourceTangents;
            public uint4* SourceUV0;
            public byte* SourceColors;
            public uint4* SourceTexCoord1;
            public uint4* SourceTexCoord3;
            public int SourceVertexCount;

            public int* SourceVertexIndices;
            public int VertexCount;
            public int* AreaTriangles;
            public int IndexCount;

            public float3* AreaVertices;
            public float3* AreaNormals;
            public float4* AreaTangents;
            public uint4* AreaUV0;
            public byte* AreaColors;
            public uint4* AreaTexCoord1;
            public uint4* AreaTexCoord3;

            public float3* CombinedVertices;
            public float3* CombinedNormals;
            public float4* CombinedTangents;
            public uint4* CombinedUV0;
            public byte* CombinedColors;
            public uint4* CombinedTexCoord1;
            public uint4* CombinedTexCoord3;
            public int CombinedVertexOffset;
            public int* CombinedTriangles;

            public int HasSourceNormals;
            public int HasSourceTangents;
            public int HasSourceUV0;
            public int AreaHasColors;
            public int AreaHasTexCoord1;
            public int AreaHasTexCoord3;
            public int ErrorCode;
            public int ErrorItem;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RemapKernel(ref RemapContext context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CopyKernel(ref CopyContext context);

        private static readonly object s_KernelLock = new object();
        private static FunctionPointer<RemapKernel> s_RemapFunction;
        private static FunctionPointer<CopyKernel> s_CopyFunction;
        private static RemapKernel s_RemapKernel;
        private static CopyKernel s_CopyKernel;
        private static bool s_InitializationAttempted;
        private static string s_InitializationFailure = string.Empty;

        public static bool TryBuild(MeshPart sourcePart, bool[] isolationHints, AssetConversionRegionRange[] regionRanges, out ConvertedRenderPartPreparation preparation, out string fallbackReason)
        {
            preparation = null;
            fallbackReason = string.Empty;
            if (!TryGetKernels(out RemapKernel remapKernel, out CopyKernel copyKernel, out fallbackReason))
            {
                return false;
            }

            if (!TryValidateSource(sourcePart, isolationHints, regionRanges, out fallbackReason))
            {
                return false;
            }

            MeshData sourceMesh = sourcePart.Mesh;
            int areaCount = sourcePart.SurfaceAreas.Count;
            AreaBuildState[] states = new AreaBuildState[areaCount];
            int combinedVertexCount = 0;
            int combinedAreaCount = 0;
            bool hasCombinedColors = false;
            bool hasCombinedTexCoord1 = false;
            bool hasCombinedTexCoord3 = false;
            int combinedUV0Dimension = 2;
            for (int areaIndex = 0; areaIndex < areaCount; areaIndex++)
            {
                SurfaceArea sourceArea = sourcePart.SurfaceAreas[areaIndex];
                AssetConversionRegionRange range = regionRanges[areaIndex];
                int indexCount = checked(range.TriangleCount * 3);
                int[] denseRemap = new int[range.VertexCount];
                int[] sourceVertexScratch = new int[range.VertexCount];
                int[] areaTriangles = new int[indexCount];

                RemapContext remapContext;
                fixed (int* sourceTriangles = sourceMesh.Triangles)
                fixed (int* denseRemapPointer = denseRemap)
                fixed (int* sourceVertexPointer = sourceVertexScratch)
                fixed (int* areaTrianglePointer = areaTriangles)
                {
                    remapContext = new RemapContext
                    {
                        SourceTriangles = sourceTriangles,
                        SourceIndexOffset = checked(range.TriangleOffset * 3),
                        IndexCount = indexCount,
                        SourceVertexOffset = range.VertexOffset,
                        SourceVertexCount = range.VertexCount,
                        DenseRemap = denseRemapPointer,
                        SourceVertexIndices = sourceVertexPointer,
                        AreaTriangles = areaTrianglePointer,
                    };

                    remapKernel(ref remapContext);
                }

                ThrowIfRemapFailed(remapContext, areaIndex);
                if (remapContext.UniqueVertexCount <= 0 || remapContext.UniqueVertexCount > range.VertexCount)
                {
                    throw new InvalidOperationException($"Native renderer preparation produced an invalid " + $"vertex count for Surface Area {sourceArea.Id}.");
                }

                int[] sourceVertexIndices = new int[remapContext.UniqueVertexCount];
                Array.Copy(sourceVertexScratch, sourceVertexIndices, sourceVertexIndices.Length);

                bool isolated = isolationHints[areaIndex];
                bool hasColors = (sourceArea.SourceVertexAttributes & VertexAttributeFlags.VertexColor) != 0;
                bool hasTexCoord1 = (sourceArea.SourceVertexAttributes & VertexAttributeFlags.TexCoord1) != 0;
                bool hasTexCoord3 = (sourceArea.SourceVertexAttributes & VertexAttributeFlags.TexCoord3) != 0;
                int uv0Dimension = (sourceArea.SourceVertexAttributes & VertexAttributeFlags.UV0FourComponent) != 0 ? 4 : 2;

                AreaBuildState state = new AreaBuildState
                {
                    SourceArea = sourceArea,
                    Isolated = isolated,
                    SourceVertexIndices = sourceVertexIndices,
                    AreaTriangles = areaTriangles,
                    HasColors = hasColors,
                    HasTexCoord1 = hasTexCoord1,
                    HasTexCoord3 = hasTexCoord3,
                    UV0Dimension = uv0Dimension,
                    MaterialIndex = -1,
                    CombinedVertexOffset = -1,
                };

                if (!isolated)
                {
                    state.MaterialIndex = combinedAreaCount++;
                    state.CombinedVertexOffset = combinedVertexCount;
                    combinedVertexCount = checked(combinedVertexCount + sourceVertexIndices.Length);
                    hasCombinedColors |= hasColors;
                    hasCombinedTexCoord1 |= hasTexCoord1;
                    hasCombinedTexCoord3 |= hasTexCoord3;
                    combinedUV0Dimension = Math.Max(combinedUV0Dimension, uv0Dimension);
                }

                states[areaIndex] = state;
            }

            Vector3[] combinedVertices = new Vector3[combinedVertexCount];
            Vector3[] combinedNormals = new Vector3[combinedVertexCount];
            Vector4[] combinedUVs = new Vector4[combinedVertexCount];
            Vector4[] combinedTangents = new Vector4[combinedVertexCount];
            Color32[] combinedColors = hasCombinedColors ? new Color32[combinedVertexCount] : Array.Empty<Color32>();
            Vector4[] combinedTexCoord1 = hasCombinedTexCoord1 ? new Vector4[combinedVertexCount] : Array.Empty<Vector4>();
            Vector4[] combinedTexCoord3 = hasCombinedTexCoord3 ? new Vector4[combinedVertexCount] : Array.Empty<Vector4>();
            int[][] submeshTriangles = new int[combinedAreaCount][];
            ConvertedRenderAreaPreparation[] preparedAreas = new ConvertedRenderAreaPreparation[areaCount];

            bool sourceHasNormals = sourceMesh.Normals != null && sourceMesh.Normals.Length == sourceMesh.Vertices.Length;
            bool sourceHasTangents = sourceMesh.Tangents != null && sourceMesh.Tangents.Length == sourceMesh.Vertices.Length;
            bool sourceHasUV0 = sourceMesh.UV0 != null && sourceMesh.UV0.Length == sourceMesh.Vertices.Length;

            for (int areaIndex = 0; areaIndex < states.Length; areaIndex++)
            {
                AreaBuildState state = states[areaIndex];
                int vertexCount = state.SourceVertexIndices.Length;

                Vector3[] areaVertices = new Vector3[vertexCount];
                Vector3[] areaNormals = sourceHasNormals ? new Vector3[vertexCount] : null;
                Vector4[] areaTangents = sourceHasTangents ? new Vector4[vertexCount] : null;
                Vector4[] areaUV0 = sourceHasUV0 ? new Vector4[vertexCount] : null;
                Color32[] areaColors = state.HasColors ? new Color32[vertexCount] : null;
                Vector4[] areaTexCoord1 = state.HasTexCoord1 ? new Vector4[vertexCount] : null;
                Vector4[] areaTexCoord3 = state.HasTexCoord3 ? new Vector4[vertexCount] : null;
                int[] adjustedTriangles = state.Isolated ? null : new int[state.AreaTriangles.Length];

                CopyContext copyContext;
                fixed (Vector3* sourceVerticesPointer = sourceMesh.Vertices)
                fixed (Vector3* sourceNormalsPointer = sourceMesh.Normals)
                fixed (Vector4* sourceTangentsPointer = sourceMesh.Tangents)
                fixed (Vector4* sourceUV0Pointer = sourceMesh.UV0)
                fixed (Color32* sourceColorsPointer = sourceMesh.VertexColors)
                fixed (Vector4* sourceTexCoord1Pointer = sourceMesh.TexCoord1)
                fixed (Vector4* sourceTexCoord3Pointer = sourceMesh.TexCoord3)
                fixed (int* sourceVertexIndicesPointer = state.SourceVertexIndices)
                fixed (int* areaTrianglesPointer = state.AreaTriangles)
                fixed (Vector3* areaVerticesPointer = areaVertices)
                fixed (Vector3* areaNormalsPointer = areaNormals)
                fixed (Vector4* areaTangentsPointer = areaTangents)
                fixed (Vector4* areaUV0Pointer = areaUV0)
                fixed (Color32* areaColorsPointer = areaColors)
                fixed (Vector4* areaTexCoord1Pointer = areaTexCoord1)
                fixed (Vector4* areaTexCoord3Pointer = areaTexCoord3)
                fixed (Vector3* combinedVerticesPointer = combinedVertices)
                fixed (Vector3* combinedNormalsPointer = combinedNormals)
                fixed (Vector4* combinedTangentsPointer = combinedTangents)
                fixed (Vector4* combinedUV0Pointer = combinedUVs)
                fixed (Color32* combinedColorsPointer = combinedColors)
                fixed (Vector4* combinedTexCoord1Pointer = combinedTexCoord1)
                fixed (Vector4* combinedTexCoord3Pointer = combinedTexCoord3)
                fixed (int* combinedTrianglesPointer = adjustedTriangles)
                {
                    copyContext = new CopyContext
                    {
                        SourceVertices = (float3*)sourceVerticesPointer,
                        SourceNormals = (float3*)sourceNormalsPointer,
                        SourceTangents = (float4*)sourceTangentsPointer,
                        SourceUV0 = (uint4*)sourceUV0Pointer,
                        SourceColors = (byte*)sourceColorsPointer,
                        SourceTexCoord1 = (uint4*)sourceTexCoord1Pointer,
                        SourceTexCoord3 = (uint4*)sourceTexCoord3Pointer,
                        SourceVertexCount = sourceMesh.Vertices.Length,
                        SourceVertexIndices = sourceVertexIndicesPointer,
                        VertexCount = vertexCount,
                        AreaTriangles = areaTrianglesPointer,
                        IndexCount = state.AreaTriangles.Length,
                        AreaVertices = (float3*)areaVerticesPointer,
                        AreaNormals = (float3*)areaNormalsPointer,
                        AreaTangents = (float4*)areaTangentsPointer,
                        AreaUV0 = (uint4*)areaUV0Pointer,
                        AreaColors = (byte*)areaColorsPointer,
                        AreaTexCoord1 = (uint4*)areaTexCoord1Pointer,
                        AreaTexCoord3 = (uint4*)areaTexCoord3Pointer,
                        CombinedVertices = state.Isolated ? null : (float3*)combinedVerticesPointer,
                        CombinedNormals = state.Isolated ? null : (float3*)combinedNormalsPointer,
                        CombinedTangents = state.Isolated ? null : (float4*)combinedTangentsPointer,
                        CombinedUV0 = state.Isolated ? null : (uint4*)combinedUV0Pointer,
                        CombinedColors = state.Isolated ? null : (byte*)combinedColorsPointer,
                        CombinedTexCoord1 = state.Isolated ? null : (uint4*)combinedTexCoord1Pointer,
                        CombinedTexCoord3 = state.Isolated ? null : (uint4*)combinedTexCoord3Pointer,
                        CombinedVertexOffset = state.CombinedVertexOffset,
                        CombinedTriangles = combinedTrianglesPointer,
                        HasSourceNormals = sourceHasNormals ? 1 : 0,
                        HasSourceTangents = sourceHasTangents ? 1 : 0,
                        HasSourceUV0 = sourceHasUV0 ? 1 : 0,
                        AreaHasColors = state.HasColors ? 1 : 0,
                        AreaHasTexCoord1 = state.HasTexCoord1 ? 1 : 0,
                        AreaHasTexCoord3 = state.HasTexCoord3 ? 1 : 0,
                    };

                    copyKernel(ref copyContext);
                }

                ThrowIfCopyFailed(copyContext, state.SourceArea.Id);

                MeshData areaMesh = new MeshData(areaVertices, areaNormals, areaTangents, areaUV0, areaColors, areaTexCoord1, areaTexCoord3, state.AreaTriangles);

                if (!state.Isolated)
                {
                    submeshTriangles[state.MaterialIndex] = adjustedTriangles;
                }

                preparedAreas[areaIndex] = new ConvertedRenderAreaPreparation(state.SourceArea.Id, state.Isolated, state.MaterialIndex, areaMesh, state.SourceVertexIndices, state.CombinedVertexOffset, vertexCount, state.UV0Dimension);
            }

            preparation = new ConvertedRenderPartPreparation(preparedAreas, combinedVertices, combinedNormals, combinedUVs, combinedTangents, combinedColors, combinedTexCoord1, combinedTexCoord3, combinedUV0Dimension, submeshTriangles);
            return true;
        }

        private static bool TryValidateSource(MeshPart sourcePart, bool[] isolationHints, AssetConversionRegionRange[] regionRanges, out string fallbackReason)
        {
            fallbackReason = string.Empty;

            if (sourcePart == null || sourcePart.Mesh == null)
            {
                fallbackReason = "Converted renderer preparation has no source part.";
                return false;
            }

            int areaCount = sourcePart.SurfaceAreas.Count;
            if (areaCount <= 0 || isolationHints == null || isolationHints.Length != areaCount || regionRanges == null || regionRanges.Length != areaCount)
            {
                fallbackReason = "Converted renderer region metadata does not match its Surface Areas.";
                return false;
            }

            MeshData mesh = sourcePart.Mesh;
            if (mesh.Vertices == null || mesh.Triangles == null || mesh.Triangles.Length % 3 != 0)
            {
                fallbackReason = "Converted renderer source geometry is incomplete.";
                return false;
            }

            int triangleCount = mesh.Triangles.Length / 3;
            if (sourcePart.TriangleSurfaceAreaIds.Count != triangleCount)
            {
                fallbackReason = "Converted renderer triangle membership is incomplete.";
                return false;
            }

            int expectedVertexOffset = 0;
            int expectedTriangleOffset = 0;
            for (int areaIndex = 0; areaIndex < areaCount; areaIndex++)
            {
                SurfaceArea area = sourcePart.SurfaceAreas[areaIndex];
                AssetConversionRegionRange range = regionRanges[areaIndex];

                if (area == null || area.Material.Mapping.Mode != TextureMappingMode.Original)
                {
                    fallbackReason = $"Surface Area {area?.Id ?? -1} does not use Original mapping.";
                    return false;
                }

                bool requiresColors = (area.SourceVertexAttributes & VertexAttributeFlags.VertexColor) != 0;
                if (requiresColors && (mesh.VertexColors == null || mesh.VertexColors.Length != mesh.Vertices.Length))
                {
                    fallbackReason = $"Surface Area {area.Id} has no complete vertex-color stream.";
                    return false;
                }

                bool requiresTexCoord1 = (area.SourceVertexAttributes & VertexAttributeFlags.TexCoord1) != 0;

                bool requiresTexCoord3 = (area.SourceVertexAttributes & VertexAttributeFlags.TexCoord3) != 0;
                if (requiresTexCoord1 && (mesh.TexCoord1 == null || mesh.TexCoord1.Length != mesh.Vertices.Length))
                {
                    fallbackReason = $"Surface Area {area.Id} has no complete TexCoord1 stream.";
                    return false;
                }

                if (requiresTexCoord3 && (mesh.TexCoord3 == null || mesh.TexCoord3.Length != mesh.Vertices.Length))
                {
                    fallbackReason = $"Surface Area {area.Id} has no complete TexCoord3 stream.";
                    return false;
                }

                if (range.SurfaceAreaId != area.Id || range.VertexOffset != expectedVertexOffset || range.TriangleOffset != expectedTriangleOffset || range.VertexCount <= 0 || range.TriangleCount <= 0 || range.VertexOffset > mesh.Vertices.Length - range.VertexCount || range.TriangleOffset > triangleCount - range.TriangleCount)
                {
                    fallbackReason = $"Geometry range {areaIndex} does not match Surface Area {area.Id}.";
                    return false;
                }

                for (int triangleId = range.TriangleOffset; triangleId < range.TriangleOffset + range.TriangleCount; triangleId++)
                {
                    if (sourcePart.TriangleSurfaceAreaIds[triangleId] != area.Id)
                    {
                        fallbackReason = $"Geometry range {areaIndex} no longer matches triangle membership.";
                        return false;
                    }
                }

                expectedVertexOffset = checked(expectedVertexOffset + range.VertexCount);
                expectedTriangleOffset = checked(expectedTriangleOffset + range.TriangleCount);
            }

            if (expectedVertexOffset != mesh.Vertices.Length || expectedTriangleOffset != triangleCount)
            {
                fallbackReason = "Converted renderer region ranges do not cover the source mesh.";
                return false;
            }

            return true;
        }

        private static bool TryGetKernels(out RemapKernel remapKernel, out CopyKernel copyKernel, out string failureReason)
        {
            remapKernel = null;
            copyKernel = null;
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
                        s_RemapFunction = BurstCompiler.CompileFunctionPointer<RemapKernel>(new RemapKernel(BuildRemapKernel));
                        s_CopyFunction = BurstCompiler.CompileFunctionPointer<CopyKernel>(new CopyKernel(CopyStreamsKernel));
                        s_RemapKernel = s_RemapFunction.Invoke;
                        s_CopyKernel = s_CopyFunction.Invoke;
                    }
                    catch (Exception exception)
                    {
                        s_InitializationFailure = "Burst renderer preparation kernels could not compile: " + exception.Message;
                    }
                }

                if (s_RemapKernel == null || s_CopyKernel == null)
                {
                    failureReason = string.IsNullOrWhiteSpace(s_InitializationFailure) ? "Burst renderer preparation kernels are unavailable." : s_InitializationFailure;
                    return false;
                }

                remapKernel = s_RemapKernel;
                copyKernel = s_CopyKernel;
                return true;
            }
        }

        private static void ThrowIfRemapFailed(RemapContext context, int areaIndex)
        {
            if (context.ErrorCode == 0)
            {
                return;
            }

            throw new InvalidOperationException($"Surface Area {areaIndex} references source vertex " + $"{context.ErrorItem} outside its GeometryAsset region.");
        }

        private static void ThrowIfCopyFailed(CopyContext context, int surfaceAreaId)
        {
            if (context.ErrorCode == 0)
            {
                return;
            }

            string detail = context.ErrorCode == 1 ? $"source vertex {context.ErrorItem}" : $"area vertex {context.ErrorItem}";
            throw new InvalidOperationException($"Surface Area {surfaceAreaId} contains invalid {detail}.");
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void BuildRemapKernel(ref RemapContext context)
        {
            for (int i = 0; i < context.SourceVertexCount; i++)
            {
                context.DenseRemap[i] = -1;
            }

            int uniqueVertexCount = 0;
            for (int i = 0; i < context.IndexCount; i++)
            {
                int sourceVertexIndex = context.SourceTriangles[context.SourceIndexOffset + i];
                int localVertexIndex = sourceVertexIndex - context.SourceVertexOffset;
                if (localVertexIndex < 0 || localVertexIndex >= context.SourceVertexCount)
                {
                    context.ErrorCode = 1;
                    context.ErrorItem = sourceVertexIndex;
                    return;
                }

                int areaVertexIndex = context.DenseRemap[localVertexIndex];
                if (areaVertexIndex < 0)
                {
                    areaVertexIndex = uniqueVertexCount++;
                    context.DenseRemap[localVertexIndex] = areaVertexIndex;
                    context.SourceVertexIndices[areaVertexIndex] = sourceVertexIndex;
                }

                context.AreaTriangles[i] = areaVertexIndex;
            }

            context.UniqueVertexCount = uniqueVertexCount;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void CopyStreamsKernel(ref CopyContext context)
        {
            float3 defaultNormal = new float3(0f, 1f, 0f);
            float4 defaultTangent = new float4(1f, 0f, 0f, 1f);

            for (int i = 0; i < context.VertexCount; i++)
            {
                int sourceVertexIndex = context.SourceVertexIndices[i];
                if (sourceVertexIndex < 0 || sourceVertexIndex >= context.SourceVertexCount)
                {
                    context.ErrorCode = 1;
                    context.ErrorItem = sourceVertexIndex;
                    return;
                }

                float3 vertex = context.SourceVertices[sourceVertexIndex];
                float3 normal = context.HasSourceNormals != 0 ? context.SourceNormals[sourceVertexIndex] : defaultNormal;
                float4 tangent = context.HasSourceTangents != 0 ? context.SourceTangents[sourceVertexIndex] : defaultTangent;
                uint4 uv0Bits = context.HasSourceUV0 != 0 ? context.SourceUV0[sourceVertexIndex] : default(uint4);

                context.AreaVertices[i] = vertex;
                if (context.AreaNormals != null)
                {
                    context.AreaNormals[i] = normal;
                }

                if (context.AreaTangents != null)
                {
                    context.AreaTangents[i] = tangent;
                }

                if (context.AreaUV0 != null)
                {
                    context.AreaUV0[i] = uv0Bits;
                }

                if (context.AreaColors != null)
                {
                    CopyColor(context.SourceColors + sourceVertexIndex * 4, context.AreaColors + i * 4);
                }

                if (context.AreaTexCoord1 != null)
                {
                    context.AreaTexCoord1[i] = context.SourceTexCoord1[sourceVertexIndex];
                }

                if (context.AreaTexCoord3 != null)
                {
                    context.AreaTexCoord3[i] = context.SourceTexCoord3[sourceVertexIndex];
                }

                if (context.CombinedVertices == null)
                {
                    continue;
                }

                int combinedIndex = context.CombinedVertexOffset + i;
                context.CombinedVertices[combinedIndex] = vertex;
                context.CombinedNormals[combinedIndex] = normal;
                context.CombinedTangents[combinedIndex] = tangent;
                context.CombinedUV0[combinedIndex] = uv0Bits;

                if (context.CombinedColors != null)
                {
                    byte* destination = context.CombinedColors + combinedIndex * 4;
                    if (context.AreaHasColors != 0)
                    {
                        CopyColor(context.SourceColors + sourceVertexIndex * 4, destination);
                    }
                    else
                    {
                        destination[0] = 255;
                        destination[1] = 255;
                        destination[2] = 255;
                        destination[3] = 255;
                    }
                }

                if (context.CombinedTexCoord1 != null)
                {
                    context.CombinedTexCoord1[combinedIndex] = context.AreaHasTexCoord1 != 0 ? context.SourceTexCoord1[sourceVertexIndex] : default(uint4);
                }

                if (context.CombinedTexCoord3 != null)
                {
                    context.CombinedTexCoord3[combinedIndex] = context.AreaHasTexCoord3 != 0 ? context.SourceTexCoord3[sourceVertexIndex] : default(uint4);
                }
            }

            if (context.CombinedTriangles == null)
            {
                return;
            }

            for (int i = 0; i < context.IndexCount; i++)
            {
                int areaVertexIndex = context.AreaTriangles[i];
                if (areaVertexIndex < 0 || areaVertexIndex >= context.VertexCount)
                {
                    context.ErrorCode = 2;
                    context.ErrorItem = areaVertexIndex;
                    return;
                }

                context.CombinedTriangles[i] = areaVertexIndex + context.CombinedVertexOffset;
            }
        }

        private static void CopyColor(byte* source, byte* destination)
        {
            destination[0] = source[0];
            destination[1] = source[1];
            destination[2] = source[2];
            destination[3] = source[3];
        }

        private sealed class AreaBuildState
        {
            public SurfaceArea SourceArea;
            public bool Isolated;
            public int[] SourceVertexIndices;
            public int[] AreaTriangles;
            public bool HasColors;
            public bool HasTexCoord1;
            public bool HasTexCoord3;
            public int UV0Dimension;
            public int MaterialIndex;
            public int CombinedVertexOffset;
        }
    }
}