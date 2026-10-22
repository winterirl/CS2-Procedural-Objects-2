using System;
using Colossal.IO.AssetDatabase;
using Unity.Entities;
using UnityEngine;
using PO2.Core;
using PO2.Materials;

namespace PO2.Conversion
{
    // Captures ECS and material data on the main thread.
    // The worker decodes each loaded GeometryAsset.
    // The main thread later validates and replaces the source entity.
    internal sealed class AssetConversionWorkItem : IDisposable
    {
        public Entity SourceEntity { get; }
        public Entity SourcePrefabEntity { get; }
        public string RequestedName { get; }
        public ObjectTransform InitialTransform { get; }
        public AssetConversionPartWorkItem[] Parts { get; }
        public ConversionProfile Profile { get; }

        public AssetConversionWorkItem(Entity sourceEntity, Entity sourcePrefabEntity, string requestedName, ObjectTransform initialTransform, AssetConversionPartWorkItem[] parts, ConversionProfile profile)
        {
            SourceEntity = sourceEntity;
            SourcePrefabEntity = sourcePrefabEntity;
            RequestedName = requestedName ?? string.Empty;
            InitialTransform = initialTransform;
            Parts = parts;
            Profile = profile;
        }

        public void Dispose()
        {
            if (Parts == null)
            {
                return;
            }

            for (int i = 0; i < Parts.Length; i++)
            {
                Parts[i]?.Dispose();
            }
        }
    }

    // Moves the loaded GeometryAsset out of the capture local.
    // Only this work item can dispose its native buffers, even after the worker takes over.
    internal sealed class AssetConversionPartWorkItem : IDisposable
    {
        internal GeometryAsset.Data GeometryData;

        public string PartName { get; }
        public Vector3 SourceMeshPosition { get; }
        public Quaternion SourceMeshRotation { get; }
        public Vector3 SourceMeshScale { get; }
        public MaterialData[] Materials { get; }
        public NativeWindowKind[] NativeWindowKinds { get; }
        public bool[] IsolationHints { get; }
        public ColorMaskData SourceColorMask { get; }
        public ConversionPartProfile Profile { get; }

        public AssetConversionPartWorkItem(string partName, ref GeometryAsset.Data geometryData, Vector3 sourceMeshPosition, Quaternion sourceMeshRotation, Vector3 sourceMeshScale, MaterialData[] materials, NativeWindowKind[] nativeWindowKinds, bool[] isolationHints, ColorMaskData sourceColorMask, ConversionPartProfile profile)
        {
            PartName = partName ?? string.Empty;
            GeometryData = geometryData;
            geometryData = default(GeometryAsset.Data);
            SourceMeshPosition = sourceMeshPosition;
            SourceMeshRotation = sourceMeshRotation;
            SourceMeshScale = sourceMeshScale;
            Materials = materials;
            NativeWindowKinds = nativeWindowKinds;
            IsolationHints = isolationHints;
            SourceColorMask = sourceColorMask;
            Profile = profile;
        }

        public void Dispose()
        {
            GeometryData.Dispose();
            GeometryData = default(GeometryAsset.Data);
        }
    }

    // Managed arrays produced by the background decoder.
    // This is super short-lived~ the same worker immediately turns it into MeshData/Part state.
    internal sealed class AssetConversionDecodedStreams
    {
        public Vector3[] Vertices { get; }
        public Vector3[] Normals { get; }
        public Vector4[] Tangents { get; }
        public Vector4[] UV0 { get; }
        public Color32[] VertexColors { get; }
        public Vector4[] TexCoord1 { get; }
        public Vector4[] TexCoord3 { get; }
        public int[] TriangleAreaIds { get; }
        public VertexAttributeFlags[] SourceVertexAttributes { get; }
        public AssetConversionRegionRange[] RegionRanges { get; }
        public ConversionBoundsSnapshot LocalBounds { get; }

        public AssetConversionDecodedStreams(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int[] triangleAreaIds, VertexAttributeFlags[] sourceVertexAttributes, AssetConversionRegionRange[] regionRanges, ConversionBoundsSnapshot localBounds)
        {
            Vertices = vertices;
            Normals = normals;
            Tangents = tangents;
            UV0 = uv0;
            VertexColors = vertexColors;
            TexCoord1 = texCoord1;
            TexCoord3 = texCoord3;
            TriangleAreaIds = triangleAreaIds;
            SourceVertexAttributes = sourceVertexAttributes;
            RegionRanges = regionRanges;
            LocalBounds = localBounds;
        }
    }

    // Keep each material region contiguous so renderer preparation can reuse its ranges.
    internal readonly struct AssetConversionRegionRange
    {
        public int SurfaceAreaId { get; }
        public int VertexOffset { get; }
        public int VertexCount { get; }
        public int TriangleOffset { get; }
        public int TriangleCount { get; }

        public AssetConversionRegionRange(int surfaceAreaId, int vertexOffset, int vertexCount, int triangleOffset, int triangleCount)
        {
            SurfaceAreaId = surfaceAreaId;
            VertexOffset = vertexOffset;
            VertexCount = vertexCount;
            TriangleOffset = triangleOffset;
            TriangleCount = triangleCount;
        }
    }

    internal sealed class AssetConversionDecodedPartData
    {
        public string PartName { get; }
        public Vector3[] Vertices { get; }
        public Vector3[] Normals { get; }
        public Vector4[] Tangents { get; }
        public Vector4[] UV0 { get; }
        public Color32[] VertexColors { get; }
        public Vector4[] TexCoord1 { get; }
        public Vector4[] TexCoord3 { get; }
        public int[] Triangles { get; }
        public int[] TriangleAreaIds { get; }
        public VertexAttributeFlags[] SourceVertexAttributes { get; }
        public AssetConversionRegionRange[] RegionRanges { get; }
        public MaterialData[] Materials { get; }
        public NativeWindowKind[] NativeWindowKinds { get; }
        public bool[] IsolationHints { get; }
        public ConversionBoundsSnapshot LocalBounds { get; }
        public ColorMaskData SourceColorMask { get; }

        public AssetConversionDecodedPartData(string partName, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int[] triangles, int[] triangleAreaIds, VertexAttributeFlags[] sourceVertexAttributes, AssetConversionRegionRange[] regionRanges, MaterialData[] materials, NativeWindowKind[] nativeWindowKinds, bool[] isolationHints, ConversionBoundsSnapshot localBounds, ColorMaskData sourceColorMask)
        {
            PartName = partName ?? string.Empty;
            Vertices = vertices;
            Normals = normals;
            Tangents = tangents;
            UV0 = uv0;
            VertexColors = vertexColors;
            TexCoord1 = texCoord1;
            TexCoord3 = texCoord3;
            Triangles = triangles;
            TriangleAreaIds = triangleAreaIds;
            SourceVertexAttributes = sourceVertexAttributes;
            RegionRanges = regionRanges;
            Materials = materials;
            NativeWindowKinds = nativeWindowKinds;
            IsolationHints = isolationHints;
            LocalBounds = localBounds;
            SourceColorMask = sourceColorMask;
        }
    }

    internal sealed class AssetConversionBuildResult
    {
        public bool Success { get; }
        public AssetConversionResult Conversion { get; }
        public string FailureReason { get; }

        private AssetConversionBuildResult(bool success, AssetConversionResult conversion, string failureReason)
        {
            Success = success;
            Conversion = conversion;
            FailureReason = failureReason ?? string.Empty;
        }

        public static AssetConversionBuildResult Completed(AssetConversionResult conversion)
        {
            return new AssetConversionBuildResult(conversion != null, conversion, conversion != null ? string.Empty : "The conversion worker produced no result.");
        }

        public static AssetConversionBuildResult Failed(string failureReason)
        {
            return new AssetConversionBuildResult(false, null, failureReason);
        }
    }
}