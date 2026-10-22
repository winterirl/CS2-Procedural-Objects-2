using System;
using Colossal.IO.AssetDatabase;
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
        private static bool TryDecodeCapturedPart(AssetConversionPartWorkItem source, out AssetConversionDecodedPartData decoded, out string failureReason)
        {
            decoded = null;
            failureReason = string.Empty;

            ref GeometryAsset.Data data = ref source.GeometryData;
            int totalVertexCount = GeometryAsset.GetAllVertexCount(ref data);
            int totalIndexCount = GeometryAsset.GetAllIndicesCount(ref data);
            if (totalVertexCount <= 0 || totalIndexCount <= 0 || totalIndexCount % 3 != 0)
            {
                failureReason = $"Captured geometry for '{source.PartName}' has invalid " + $"counts: vertices={totalVertexCount}, indices={totalIndexCount}.";
                return false;
            }

            ConversionPartProfile conversionProfile = source.Profile;
            AssetConversionDecodedStreams nativeStreams = null;
            string nativeFallbackReason = string.Empty;
            bool nativeDecoded = false;

            // This is the old decoder below.
            // It is both the safe fallback and a reference for the conversion Burst path.
            // todo: clean up when burst conversion is finalized? Keep as a fallback?
            try
            {
                nativeDecoded = NativeVertexStreamDecoder.TryDecode(source, out nativeStreams, out nativeFallbackReason);
            }
            catch (InvalidOperationException exception)
            {
                nativeFallbackReason = "Native stream decoding failed: " + exception.Message;
            }
            catch (Exception exception)
            {
                nativeFallbackReason = "Native stream decoding was unavailable: " + exception.Message;
            }

            if (!nativeDecoded)
            {
                if (conversionProfile != null)
                {
                    conversionProfile.StreamDecodeFallbackReason = nativeFallbackReason;
                }

                return TryDecodeCapturedPartManaged(source, out decoded, out failureReason);
            }

            int[] triangles = null;
            if (totalVertexCount <= kStreamCompatibilityValidationVertexLimit)
            {
                if (!TryDecodeCapturedPartManaged(source, out AssetConversionDecodedPartData reference, out string referenceFailureReason))
                {
                    if (conversionProfile != null)
                    {
                        conversionProfile.StreamDecodeFallbackReason = "Managed compatibility decode failed: " + referenceFailureReason;
                    }

                    failureReason = "Managed stream compatibility decode failed: " + referenceFailureReason;
                    return false;
                }

                try
                {
                    ValidateDecodedStreams(nativeStreams, reference);
                }
                catch (InvalidOperationException exception)
                {
                    if (conversionProfile != null)
                    {
                        conversionProfile.StreamDecodeFallbackReason = "Native stream validation failed: " + exception.Message;
                    }

                    decoded = reference;
                }

                if (decoded != null)
                {
                    return true;
                }
            }

            if (triangles == null)
            {
                NativeArray<int> convertedIndices = GeometryAsset.ConvertAllIndicesTo32(ref data, Allocator.TempJob);

                try
                {
                    triangles = convertedIndices.ToArray();
                }
                finally
                {
                    if (convertedIndices.IsCreated)
                    {
                        convertedIndices.Dispose();
                    }
                }
            }

            decoded = CreateDecodedPart(source, nativeStreams, triangles);
            return true;
        }

        private static bool TryDecodeCapturedPartManaged(AssetConversionPartWorkItem source, out AssetConversionDecodedPartData decoded, out string failureReason)
        {
            decoded = null;
            failureReason = string.Empty;

            ref GeometryAsset.Data data = ref source.GeometryData;
            int totalVertexCount = GeometryAsset.GetAllVertexCount(ref data);
            int totalIndexCount = GeometryAsset.GetAllIndicesCount(ref data);

            if (totalVertexCount <= 0 || totalIndexCount <= 0 || totalIndexCount % 3 != 0)
            {
                failureReason = $"Captured geometry for '{source.PartName}' has invalid " + $"counts: vertices={totalVertexCount}, indices={totalIndexCount}.";
                return false;
            }

            // Combine mesh regions into one part then map them back to Surface Areas.
            Vector3[] vertices = new Vector3[totalVertexCount];
            Vector3[] normals = new Vector3[totalVertexCount];
            Vector4[] tangents = new Vector4[totalVertexCount];
            Vector4[] uv0 = new Vector4[totalVertexCount];
            Color32[] vertexColors = new Color32[totalVertexCount];
            Vector4[] texCoord1 = HasTexCoord1(ref data) ? new Vector4[totalVertexCount] : null;
            Vector4[] texCoord3 = new Vector4[totalVertexCount];
            int[] triangleAreaIds = new int[totalIndexCount / 3];
            VertexAttributeFlags[] sourceVertexAttributes = new VertexAttributeFlags[data.meshCount];
            AssetConversionRegionRange[] regionRanges = new AssetConversionRegionRange[data.meshCount];

            SourceMeshTransform sourceMeshTransform = new SourceMeshTransform(source.SourceMeshPosition, source.SourceMeshRotation, source.SourceMeshScale);

            // Optional streams are all or nothing on MeshData.
            // A missing region drops/rebuilds that whole stream.
            bool allNormalsPresent = true;
            bool allTangentsPresent = true;
            bool allUV0Present = true;
            bool anyVertexColorsPresent = false;
            bool anyTexCoord1Present = false;
            bool anyTexCoord3Present = false;
            int vertexOffset = 0;
            int triangleOffset = 0;
            ConversionBoundsSnapshot localBounds = default(ConversionBoundsSnapshot);

            for (int meshIndex = 0; meshIndex < data.meshCount; meshIndex++)
            {
                int vertexCount = GeometryAsset.GetVertexCount(ref data, meshIndex);
                int indexCount = GeometryAsset.GetIndicesCount(ref data, meshIndex);

                if (indexCount % 3 != 0)
                {
                    failureReason = $"Geometry mesh {meshIndex} on '{source.PartName}' has " + $"a non-triangle index count ({indexCount}).";
                    return false;
                }

                int meshTriangleCount = indexCount / 3;
                regionRanges[meshIndex] = new AssetConversionRegionRange(meshIndex, vertexOffset, vertexCount, triangleOffset, meshTriangleCount);

                if (!DecodePositions(ref data, meshIndex, vertexCount, vertices, vertexOffset, sourceMeshTransform, ref localBounds))
                {
                    failureReason = $"Could not decode positions for mesh region {meshIndex}.";
                    return false;
                }

                if (!DecodeNormals(ref data, meshIndex, vertexCount, normals, vertexOffset, sourceMeshTransform))
                {
                    allNormalsPresent = false;
                }

                if (!DecodeTangents(ref data, meshIndex, vertexCount, tangents, vertexOffset, sourceMeshTransform))
                {
                    allTangentsPresent = false;
                }

                if (!DecodeUV0(ref data, meshIndex, vertexCount, uv0, vertexOffset))
                {
                    allUV0Present = false;
                }

                // Window shaders need the full four-component UV0 payload
                // NOT just the ordinary texture coordinates in xy.
                GeometryAsset.GetAttributeFormat(ref data, meshIndex, VertexAttribute.TexCoord0, out VertexAttributeFormat _, out int uv0Dimension);
                if (uv0Dimension > 2)
                {
                    sourceVertexAttributes[meshIndex] |= VertexAttributeFlags.UV0FourComponent;
                }

                if (DecodeVertexColors(ref data, meshIndex, vertexCount, vertexColors, vertexOffset))
                {
                    anyVertexColorsPresent = true;
                    sourceVertexAttributes[meshIndex] |= VertexAttributeFlags.VertexColor;
                }
                else
                {
                    FillDefaultVertexColors(vertexColors, vertexOffset, vertexCount);
                }

                if (DecodeTexCoord1(ref data, meshIndex, vertexCount, texCoord1, vertexOffset))
                {
                    anyTexCoord1Present = true;
                    sourceVertexAttributes[meshIndex] |= VertexAttributeFlags.TexCoord1;
                }

                if (DecodeTexCoord3(ref data, meshIndex, vertexCount, texCoord3, vertexOffset, sourceMeshTransform))
                {
                    anyTexCoord3Present = true;
                    sourceVertexAttributes[meshIndex] |= VertexAttributeFlags.TexCoord3;
                }

                // ConvertAllIndicesTo32 keeps meshes in this same order
                // so triangle position is enough to recover material membership.
                for (int triangleIndex = 0; triangleIndex < meshTriangleCount; triangleIndex++)
                {
                    triangleAreaIds[triangleOffset + triangleIndex] = meshIndex;
                }

                vertexOffset += vertexCount;
                triangleOffset += meshTriangleCount;
            }

            // Worker allocations cannot use Unity's frame-scoped Temp allocator
            NativeArray<int> convertedIndices = GeometryAsset.ConvertAllIndicesTo32(ref data, Allocator.TempJob);

            int[] triangles;
            try
            {
                triangles = convertedIndices.ToArray();
            }
            finally
            {
                if (convertedIndices.IsCreated)
                {
                    convertedIndices.Dispose();
                }
            }

            decoded = new AssetConversionDecodedPartData(source.PartName, vertices, allNormalsPresent ? normals : null, allTangentsPresent ? tangents : null, allUV0Present ? uv0 : null, anyVertexColorsPresent ? vertexColors : null, anyTexCoord1Present ? texCoord1 : null, anyTexCoord3Present ? texCoord3 : null, triangles, triangleAreaIds, sourceVertexAttributes, regionRanges, source.Materials, source.NativeWindowKinds, source.IsolationHints, localBounds, source.SourceColorMask);

            return true;
        }
    }
}