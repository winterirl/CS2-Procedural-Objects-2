using MeshData = PO2.Geometry.MeshData;
using MaterialData = PO2.Materials.MaterialData;
using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Geometry;
using PO2.Materials;

namespace PO2.Conversion
{
    internal sealed partial class AssetMeshExtractor
    {
        private static bool TryBuildCapturedPart(AssetConversionPartWorkItem source, out MeshPart part, out ConvertedRenderPartPreparation renderPreparation, out ConversionBoundsSnapshot localBounds, out string failureReason)
        {
            part = null;
            renderPreparation = null;
            localBounds = default(ConversionBoundsSnapshot);
            failureReason = string.Empty;

            if (source == null || !source.GeometryData.IsValid || source.Materials == null || source.Materials.Length == 0 || source.NativeWindowKinds == null || source.IsolationHints == null)
            {
                failureReason = "Captured mesh-part data is incomplete.";
                return false;
            }

            if (source.Materials.Length != source.GeometryData.meshCount || source.NativeWindowKinds.Length != source.GeometryData.meshCount || source.IsolationHints.Length != source.GeometryData.meshCount)
            {
                failureReason = "Captured mesh-part region metadata does not match its geometry.";
                return false;
            }

            AssetConversionDecodedPartData decoded;
            try
            {
                if (!TryDecodeCapturedPart(source, out decoded, out failureReason))
                {
                    return false;
                }
            }
            finally
            {
                // Once the managed streams exist release the loaded asset payload before logical topology build.
                // This keeps peak memory in check on large conversions.
                source.Dispose();
            }

            if (!TryBuildDecodedPart(decoded, source.Profile, out part, out failureReason))
            {
                return false;
            }

            localBounds = decoded.LocalBounds;
            return ConvertedRenderPreparationBuilder.TryBuildPart(part, decoded.IsolationHints, decoded.RegionRanges, source.Profile, out renderPreparation, out failureReason);
        }

        private static AssetConversionDecodedPartData CreateDecodedPart(AssetConversionPartWorkItem source, AssetConversionDecodedStreams streams, int[] triangles)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (streams == null)
            {
                throw new ArgumentNullException(nameof(streams));
            }

            return new AssetConversionDecodedPartData(source.PartName, streams.Vertices, streams.Normals, streams.Tangents, streams.UV0, streams.VertexColors, streams.TexCoord1, streams.TexCoord3, triangles, streams.TriangleAreaIds, streams.SourceVertexAttributes, streams.RegionRanges, source.Materials, source.NativeWindowKinds, source.IsolationHints, streams.LocalBounds, source.SourceColorMask);
        }

        private static bool TryBuildDecodedPart(AssetConversionDecodedPartData source, ConversionPartProfile conversionProfile, out MeshPart part, out string failureReason)
        {
            part = null;
            failureReason = string.Empty;

            if (source == null || source.Vertices == null || source.Triangles == null || source.Materials == null || source.Materials.Length == 0 || source.SourceVertexAttributes == null || source.RegionRanges == null || source.NativeWindowKinds == null || source.IsolationHints == null || source.TriangleAreaIds == null)
            {
                failureReason = "Decoded mesh-part data is incomplete.";
                return false;
            }

            int sourceRegionCount = source.Materials.Length;
            if (source.SourceVertexAttributes.Length != sourceRegionCount || source.RegionRanges.Length != sourceRegionCount || source.NativeWindowKinds.Length != sourceRegionCount || source.IsolationHints.Length != sourceRegionCount || source.TriangleAreaIds.Length != source.Triangles.Length / 3)
            {
                failureReason = "Decoded mesh-part region metadata does not match its geometry.";
                return false;
            }

            MeshData meshData = new MeshData(source.Vertices, source.Normals, source.Tangents, source.UV0, source.VertexColors, source.TexCoord1, source.TexCoord3, source.Triangles);

            // Recalculate only when the authored stream could not be kept.
            // Native tangents are hihgly preferable for special shader families.
            if (source.Normals == null)
            {
                meshData.RecalculateNormals();
            }

            if (source.Tangents == null && meshData.UV0 != null)
            {
                meshData.RecalculateTangents();
            }

            MaterialData firstMaterial = source.Materials[0];
            SurfaceAreaOriginalState firstOriginalState = new SurfaceAreaOriginalState(firstMaterial, source.SourceColorMask);

            part = new MeshPart(source.PartName, meshData, firstMaterial, source.SourceColorMask, firstOriginalState, structuralFaces: null, nativeWindowKind: source.NativeWindowKinds[0], conversionProfile: conversionProfile, convertedTriangleSurfaceAreaIds: source.TriangleAreaIds);
            part.SetSurfaceAreaSourceVertexAttributes(MeshPart.kDefaultSurfaceAreaId, source.SourceVertexAttributes[0]);

            for (int meshIndex = 1; meshIndex < source.Materials.Length; meshIndex++)
            {
                MaterialData areaMaterial = source.Materials[meshIndex];
                SurfaceAreaOriginalState originalState = new SurfaceAreaOriginalState(areaMaterial, source.SourceColorMask);
                int areaId = part.CreateSurfaceArea(areaMaterial, source.SourceVertexAttributes[meshIndex], source.SourceColorMask, originalState, source.NativeWindowKinds[meshIndex]);
                int triangleCount = 0;

                for (int i = 0; i < source.TriangleAreaIds.Length; i++)
                {
                    if (source.TriangleAreaIds[i] == meshIndex)
                    {
                        triangleCount++;
                    }
                }

                int[] areaTriangles = new int[triangleCount];
                int cursor = 0;

                for (int i = 0; i < source.TriangleAreaIds.Length; i++)
                {
                    if (source.TriangleAreaIds[i] == meshIndex)
                    {
                        areaTriangles[cursor++] = i;
                    }
                }

                if (!part.AssignTrianglesToSurfaceArea(areaTriangles, areaId, out string assignmentFailureReason))
                {
                    failureReason = $"Could not preserve source geometry region {meshIndex} " + $"as Surface Area: {assignmentFailureReason}";
                    part = null;
                    return false;
                }
            }

            return true;
        }

        private static string GetUniquePartName(string requestedName, HashSet<string> usedNames)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName) ? "Mesh Part" : requestedName.Trim();

            if (usedNames.Add(baseName))
            {
                return baseName;
            }

            int suffix = 1;
            while (true)
            {
                string candidate = $"{baseName} ({suffix})";
                if (usedNames.Add(candidate))
                {
                    return candidate;
                }

                suffix++;
            }
        }
    }
}