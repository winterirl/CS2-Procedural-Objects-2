using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Materials;
using PO2.Rendering;

namespace PO2.Conversion
{
    // Builds vertex remaps and streams on the worker. The main thread still
    // resolves materials, creates Unity objects, and uploads the finished data.
    internal sealed class ConvertedRenderPreparation
    {
        public ConvertedRenderPartPreparation[] Parts { get; }

        public ConvertedRenderPreparation(ConvertedRenderPartPreparation[] parts)
        {
            Parts = parts ?? Array.Empty<ConvertedRenderPartPreparation>();
        }
    }

    internal sealed class ConvertedRenderPartPreparation
    {
        public ConvertedRenderAreaPreparation[] Areas { get; }
        public Vector3[] CombinedVertices { get; }
        public Vector3[] CombinedNormals { get; }
        public Vector4[] CombinedUVs { get; }
        public Vector4[] CombinedTangents { get; }
        public Color32[] CombinedColors { get; }
        public Vector4[] CombinedTexCoord1 { get; }
        public Vector4[] CombinedTexCoord3 { get; }
        public int CombinedUV0Dimension { get; }
        public int[][] SubmeshTriangles { get; }

        internal RenderGeometryPreparation GeometryUpdates;

        internal void PrepareGeometryUpdates(int sourceCount)
        {
            var maps = new int[Areas.Length][];
            var isolated = new Vector3[Areas.Length][];
            for (int i = 0; i < Areas.Length; i++)
            {
                maps[i] = Areas[i].SourceVertexIndices;
                if (Areas[i].ExpectedIsolatedRenderer) isolated[i] = Areas[i].AreaMesh.Vertices;
            }

            GeometryUpdates = RenderGeometryPreparation.TryBuild(sourceCount, maps, SubmeshTriangles.Length == 0 ? null : CombinedVertices, isolated);
        }

        public ConvertedRenderPartPreparation(ConvertedRenderAreaPreparation[] areas, Vector3[] combinedVertices, Vector3[] combinedNormals, Vector4[] combinedUVs, Vector4[] combinedTangents, Color32[] combinedColors, Vector4[] combinedTexCoord1, Vector4[] combinedTexCoord3, int combinedUV0Dimension, int[][] submeshTriangles)
        {
            Areas = areas ?? Array.Empty<ConvertedRenderAreaPreparation>();
            CombinedVertices = combinedVertices ?? Array.Empty<Vector3>();
            CombinedNormals = combinedNormals ?? Array.Empty<Vector3>();
            CombinedUVs = combinedUVs ?? Array.Empty<Vector4>();
            CombinedTangents = combinedTangents ?? Array.Empty<Vector4>();
            CombinedColors = combinedColors ?? Array.Empty<Color32>();
            CombinedTexCoord1 = combinedTexCoord1 ?? Array.Empty<Vector4>();
            CombinedTexCoord3 = combinedTexCoord3 ?? Array.Empty<Vector4>();
            CombinedUV0Dimension = combinedUV0Dimension <= 2 ? 2 : 4;
            SubmeshTriangles = submeshTriangles ?? Array.Empty<int[]>();
        }
    }

    internal sealed class ConvertedRenderAreaPreparation
    {
        public int SurfaceAreaId { get; }
        public bool ExpectedIsolatedRenderer { get; }
        public int MaterialIndex { get; }
        public MeshData AreaMesh { get; }
        public int[] SourceVertexIndices { get; }
        public int CombinedVertexOffset { get; }
        public int CombinedVertexCount { get; }
        public int UV0Dimension { get; }

        public ConvertedRenderAreaPreparation(int surfaceAreaId, bool expectedIsolatedRenderer, int materialIndex, MeshData areaMesh, int[] sourceVertexIndices, int combinedVertexOffset, int combinedVertexCount, int uv0Dimension)
        {
            SurfaceAreaId = surfaceAreaId;
            ExpectedIsolatedRenderer = expectedIsolatedRenderer;
            MaterialIndex = materialIndex;
            AreaMesh = areaMesh ?? throw new ArgumentNullException(nameof(areaMesh));
            SourceVertexIndices = sourceVertexIndices ?? throw new ArgumentNullException(nameof(sourceVertexIndices));
            CombinedVertexOffset = combinedVertexOffset;
            CombinedVertexCount = combinedVertexCount;
            UV0Dimension = uv0Dimension <= 2 ? 2 : 4;
        }
    }

    internal struct ConversionBoundsSnapshot
    {
        public bool HasBounds;
        public Vector3 Min;
        public Vector3 Max;

        public void Encapsulate(Vector3 position)
        {
            if (!HasBounds)
            {
                Min = position;
                Max = position;
                HasBounds = true;
                return;
            }

            Min = new Vector3(Math.Min(Min.x, position.x), Math.Min(Min.y, position.y), Math.Min(Min.z, position.z));
            Max = new Vector3(Math.Max(Max.x, position.x), Math.Max(Max.y, position.y), Math.Max(Max.z, position.z));
        }

        public void Encapsulate(ConversionBoundsSnapshot other)
        {
            if (!other.HasBounds)
            {
                return;
            }

            Encapsulate(other.Min);
            Encapsulate(other.Max);
        }

        public Bounds ToBounds()
        {
            if (!HasBounds)
            {
                return default(Bounds);
            }

            Vector3 center = (Min + Max) * 0.5f;
            return new Bounds(center, Max - Min);
        }
    }

    internal static class ConvertedRenderPreparationBuilder
    {
        private const int kCompatibilityValidationVertexLimit = 4096;

        public static bool TryBuildPart(MeshPart sourcePart, bool[] isolationHints, AssetConversionRegionRange[] regionRanges, ConversionPartProfile conversionProfile, out ConvertedRenderPartPreparation preparation, out string failureReason)
        {
            preparation = null;
            failureReason = string.Empty;
            bool nativeBuilt = false;
            string nativeFallbackReason = string.Empty;

            try
            {
                nativeBuilt = NativeConvertedRenderPreparationBuilder.TryBuild(sourcePart, isolationHints, regionRanges, out preparation, out nativeFallbackReason);
            }
            catch (InvalidOperationException exception)
            {
                nativeFallbackReason = "Native renderer preparation failed: " + exception.Message;
            }
            catch (Exception exception)
            {
                nativeFallbackReason = "Native renderer preparation was unavailable: " + exception.Message;
            }

            if (!nativeBuilt)
            {
                if (conversionProfile != null)
                {
                    conversionProfile.RendererPreparationFallbackReason = nativeFallbackReason;
                }

                bool managed = TryBuildPartManaged(sourcePart, isolationHints, out preparation, out failureReason);
                if (managed) preparation.PrepareGeometryUpdates(sourcePart.Mesh.Vertices.Length);
                return managed;
            }

            if (sourcePart.Mesh.Vertices.Length <= kCompatibilityValidationVertexLimit)
            {
                if (!TryBuildPartManaged(sourcePart, isolationHints, out ConvertedRenderPartPreparation reference, out string referenceFailureReason))
                {
                    failureReason = "Managed renderer compatibility preparation failed: " + referenceFailureReason;
                    preparation = null;
                    return false;
                }

                try
                {
                    ValidateEquivalent(preparation, reference);
                }
                catch (InvalidOperationException exception)
                {
                    if (conversionProfile != null)
                    {
                        conversionProfile.RendererPreparationFallbackReason = "Native renderer preparation validation failed: " + exception.Message;
                    }

                    preparation = reference;
                }
            }

            preparation.PrepareGeometryUpdates(sourcePart.Mesh.Vertices.Length);
            return true;
        }

        private static bool TryBuildPartManaged(MeshPart sourcePart, bool[] isolationHints, out ConvertedRenderPartPreparation preparation, out string failureReason)
        {
            preparation = null;
            failureReason = string.Empty;

            if (sourcePart == null || sourcePart.Mesh == null)
            {
                failureReason = "Converted renderer preparation has no source part.";
                return false;
            }

            if (isolationHints == null || isolationHints.Length != sourcePart.SurfaceAreas.Count)
            {
                failureReason = "Converted renderer isolation metadata does not match its Surface Areas.";
                return false;
            }

            int areaCount = sourcePart.SurfaceAreas.Count;
            int[] triangleCounts = new int[areaCount];
            Dictionary<int, int> areaIndexById = new Dictionary<int, int>(areaCount);

            for (int i = 0; i < areaCount; i++)
            {
                areaIndexById[sourcePart.SurfaceAreas[i].Id] = i;
            }

            IReadOnlyList<int> membership = sourcePart.TriangleSurfaceAreaIds;
            for (int triangleId = 0; triangleId < membership.Count; triangleId++)
            {
                if (!areaIndexById.TryGetValue(membership[triangleId], out int areaIndex))
                {
                    failureReason = "Converted renderer preparation found an unknown Surface Area membership.";
                    return false;
                }

                triangleCounts[areaIndex]++;
            }

            int[][] areaTriangleIds = new int[areaCount][];
            int[] areaTriangleCursors = new int[areaCount];
            for (int i = 0; i < areaCount; i++)
            {
                areaTriangleIds[i] = new int[triangleCounts[i]];
            }

            for (int triangleId = 0; triangleId < membership.Count; triangleId++)
            {
                int areaIndex = areaIndexById[membership[triangleId]];
                areaTriangleIds[areaIndex][areaTriangleCursors[areaIndex]++] = triangleId;
            }

            List<Vector3> combinedVertices = new List<Vector3>();
            List<Vector3> combinedNormals = new List<Vector3>();
            List<Vector4> combinedUVs = new List<Vector4>();
            List<Vector4> combinedTangents = new List<Vector4>();
            List<Color32> combinedColors = new List<Color32>();
            List<Vector4> combinedTexCoord1 = new List<Vector4>();
            List<Vector4> combinedTexCoord3 = new List<Vector4>();
            List<int[]> submeshTriangles = new List<int[]>();
            List<ConvertedRenderAreaPreparation> preparedAreas = new List<ConvertedRenderAreaPreparation>(areaCount);

            bool hasCombinedVertexColors = false;
            bool hasCombinedTexCoord1 = false;
            bool hasCombinedTexCoord3 = false;
            int combinedUV0Dimension = 2;

            var geometry = new SurfaceAreaGeometryBuilder(sourcePart.Mesh);
            for (int areaIndex = 0; areaIndex < areaCount; areaIndex++)
            {
                SurfaceArea sourceArea = sourcePart.SurfaceAreas[areaIndex];
                int[] sourceTriangleIds = areaTriangleIds[areaIndex];
                if (sourceTriangleIds.Length == 0)
                {
                    continue;
                }

                // Native surfaces start with Original mapping. Fall back to the normal renderer
                // if that changes so preparation cannot bake the wrong layout.
                if (sourceArea.Material.Mapping.Mode != TextureMappingMode.Original)
                {
                    failureReason = $"Surface Area {sourceArea.Id} does not use Original mapping.";
                    return false;
                }

                geometry.Build(sourceArea, sourceTriangleIds, out MeshData areaMesh, out int[] sourceVertexIndices);

                bool isolated = isolationHints[areaIndex];
                int materialIndex = -1;
                int combinedVertexOffset = -1;
                int combinedVertexCount = areaMesh.Vertices.Length;
                int uv0Dimension = (sourceArea.SourceVertexAttributes & VertexAttributeFlags.UV0FourComponent) != 0 ? 4 : 2;

                if (!isolated)
                {
                    combinedVertexOffset = combinedVertices.Count;
                    AppendAreaVertices(combinedVertices, combinedNormals, combinedUVs, combinedTangents, combinedColors, combinedTexCoord1, combinedTexCoord3, areaMesh.Vertices, areaMesh.Normals, areaMesh.UV0, areaMesh.Tangents, areaMesh.VertexColors, areaMesh.TexCoord1, areaMesh.TexCoord3);

                    int[] adjustedTriangles = new int[areaMesh.Triangles.Length];
                    for (int i = 0; i < adjustedTriangles.Length; i++)
                    {
                        adjustedTriangles[i] = areaMesh.Triangles[i] + combinedVertexOffset;
                    }

                    materialIndex = submeshTriangles.Count;
                    submeshTriangles.Add(adjustedTriangles);
                    hasCombinedVertexColors |= areaMesh.VertexColors != null && areaMesh.VertexColors.Length == areaMesh.Vertices.Length;
                    hasCombinedTexCoord1 |= areaMesh.TexCoord1 != null && areaMesh.TexCoord1.Length == areaMesh.Vertices.Length;
                    hasCombinedTexCoord3 |= areaMesh.TexCoord3 != null && areaMesh.TexCoord3.Length == areaMesh.Vertices.Length;
                    if (uv0Dimension > combinedUV0Dimension)
                    {
                        combinedUV0Dimension = uv0Dimension;
                    }
                }

                preparedAreas.Add(new ConvertedRenderAreaPreparation(sourceArea.Id, isolated, materialIndex, areaMesh, sourceVertexIndices, combinedVertexOffset, combinedVertexCount, uv0Dimension));
            }

            preparation = new ConvertedRenderPartPreparation(preparedAreas.ToArray(), combinedVertices.ToArray(), combinedNormals.ToArray(), combinedUVs.ToArray(), combinedTangents.ToArray(), hasCombinedVertexColors ? combinedColors.ToArray() : Array.Empty<Color32>(), hasCombinedTexCoord1 ? combinedTexCoord1.ToArray() : Array.Empty<Vector4>(), hasCombinedTexCoord3 ? combinedTexCoord3.ToArray() : Array.Empty<Vector4>(), combinedUV0Dimension, submeshTriangles.ToArray());

            return true;
        }

        private static void ValidateEquivalent(ConvertedRenderPartPreparation actual, ConvertedRenderPartPreparation expected)
        {
            if (actual == null || expected == null)
            {
                throw new InvalidOperationException("One of the renderer preparations is missing.");
            }

            if (actual.CombinedUV0Dimension != expected.CombinedUV0Dimension)
            {
                throw new InvalidOperationException("Combined UV0 dimensions are differnet.");
            }

            ValidateArray(actual.CombinedVertices, expected.CombinedVertices, "combined vertices");
            ValidateArray(actual.CombinedNormals, expected.CombinedNormals, "combined normals");
            ValidatePayloadArray(actual.CombinedUVs, expected.CombinedUVs, "combined UV0");
            ValidateArray(actual.CombinedTangents, expected.CombinedTangents, "combined tangents");
            ValidateArray(actual.CombinedColors, expected.CombinedColors, "combined colors");
            ValidatePayloadArray(actual.CombinedTexCoord1, expected.CombinedTexCoord1, "combined TexCoord1");
            ValidatePayloadArray(actual.CombinedTexCoord3, expected.CombinedTexCoord3, "combined TexCoord3");

            if (actual.SubmeshTriangles.Length != expected.SubmeshTriangles.Length)
            {
                throw new InvalidOperationException("Combined submesh counts are different.");
            }

            for (int i = 0; i < actual.SubmeshTriangles.Length; i++)
            {
                ValidateArray(actual.SubmeshTriangles[i], expected.SubmeshTriangles[i], $"combined submesh {i} triangles");
            }

            if (actual.Areas.Length != expected.Areas.Length)
            {
                throw new InvalidOperationException("Prepared Surface Area counts are different.");
            }

            for (int i = 0; i < actual.Areas.Length; i++)
            {
                ValidateAreaEquivalent(actual.Areas[i], expected.Areas[i], i);
            }
        }

        private static void ValidateAreaEquivalent(ConvertedRenderAreaPreparation actual, ConvertedRenderAreaPreparation expected, int areaIndex)
        {
            if (actual == null || expected == null)
            {
                throw new InvalidOperationException($"Prepared Surface Area {areaIndex} is missing.");
            }

            if (actual.SurfaceAreaId != expected.SurfaceAreaId || actual.ExpectedIsolatedRenderer != expected.ExpectedIsolatedRenderer || actual.MaterialIndex != expected.MaterialIndex || actual.CombinedVertexOffset != expected.CombinedVertexOffset || actual.CombinedVertexCount != expected.CombinedVertexCount || actual.UV0Dimension != expected.UV0Dimension)
            {
                throw new InvalidOperationException($"Prepared Surface Area {areaIndex} metadata differs.");
            }

            ValidateArray(actual.SourceVertexIndices, expected.SourceVertexIndices, $"Surface Area {areaIndex} source indices");
            ValidateMeshEquivalent(actual.AreaMesh, expected.AreaMesh, areaIndex);
        }

        private static void ValidateMeshEquivalent(MeshData actual, MeshData expected, int areaIndex)
        {
            if (actual == null || expected == null)
            {
                throw new InvalidOperationException($"Prepared Surface Area {areaIndex} mesh is missing.");
            }

            ValidateArray(actual.Vertices, expected.Vertices, $"Surface Area {areaIndex} vertices");
            ValidateArray(actual.Normals, expected.Normals, $"Surface Area {areaIndex} normals");
            ValidateArray(actual.Tangents, expected.Tangents, $"Surface Area {areaIndex} tangents");
            ValidatePayloadArray(actual.UV0, expected.UV0, $"Surface Area {areaIndex} UV0");
            ValidateArray(actual.VertexColors, expected.VertexColors, $"Surface Area {areaIndex} colors");
            ValidatePayloadArray(actual.TexCoord1, expected.TexCoord1, $"Surface Area {areaIndex} TexCoord1");
            ValidatePayloadArray(actual.TexCoord3, expected.TexCoord3, $"Surface Area {areaIndex} TexCoord3");
            ValidateArray(actual.Triangles, expected.Triangles, $"Surface Area {areaIndex} triangles");
        }

        private static void ValidateArray<T>(T[] actual, T[] expected, string label)
        {
            if (actual == null || expected == null)
            {
                if (actual == null && expected == null)
                {
                    return;
                }

                throw new InvalidOperationException($"Prepared {label} stream presence differs.");
            }

            if (actual.Length != expected.Length)
            {
                throw new InvalidOperationException($"Prepared {label} counts differ: " + $"actual={actual.Length}, expected={expected.Length}.");
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < actual.Length; i++)
            {
                if (!comparer.Equals(actual[i], expected[i]))
                {
                    throw new InvalidOperationException($"Prepared {label} differs at index {i}.");
                }
            }
        }

        private static unsafe void ValidatePayloadArray(Vector4[] actual, Vector4[] expected, string label)
        {
            if (actual == null || expected == null)
            {
                if (actual == null && expected == null)
                {
                    return;
                }

                throw new InvalidOperationException($"Prepared {label} stream presence differs.");
            }

            if (actual.Length != expected.Length)
            {
                throw new InvalidOperationException($"Prepared {label} counts differ: " + $"actual={actual.Length}, expected={expected.Length}.");
            }

            fixed (Vector4* actualVectors = actual)
            fixed (Vector4* expectedVectors = expected)
            {
                uint* actualBits = (uint*)actualVectors;
                uint* expectedBits = (uint*)expectedVectors;
                int laneCount = checked(actual.Length * 4);

                for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
                {
                    if (actualBits[laneIndex] == expectedBits[laneIndex])
                    {
                        continue;
                    }

                    int vertexIndex = laneIndex / 4;
                    char component = "xyzw"[laneIndex % 4];
                    throw new InvalidOperationException($"Prepared {label} differs at index {vertexIndex}, " + $"component {component}: " + $"actual=0x{actualBits[laneIndex]:X8}, " + $"expected=0x{expectedBits[laneIndex]:X8}.");
                }
            }
        }

        internal static void BuildAreaSourceMesh(MeshPart part, SurfaceArea surfaceArea, int[] sourceTriangleIds, out MeshData areaMesh, out int[] sourceVertexIndices, System.Threading.CancellationToken cancellation = default)
        {
            BuildAreaSourceMesh(part.Mesh, surfaceArea, sourceTriangleIds, out areaMesh, out sourceVertexIndices, cancellation);
        }

        internal static void BuildAreaSourceMesh(MeshData source, SurfaceArea surfaceArea, int[] sourceTriangleIds, out MeshData areaMesh, out int[] sourceVertexIndices, System.Threading.CancellationToken cancellation = default)
        {
            new SurfaceAreaGeometryBuilder(source).Build(surfaceArea, sourceTriangleIds, out areaMesh, out sourceVertexIndices, cancellation);
        }

        internal static void AppendAreaVertices(List<Vector3> combinedVertices, List<Vector3> combinedNormals, List<Vector4> combinedUVs, List<Vector4> combinedTangents, List<Color32> combinedColors, List<Vector4> combinedTexCoord1, List<Vector4> combinedTexCoord3, Vector3[] vertices, Vector3[] normals, Vector4[] uvs, Vector4[] tangents, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3)
        {
            bool hasNormals = normals != null && normals.Length == vertices.Length;
            bool hasUVs = uvs != null && uvs.Length == vertices.Length;
            bool hasTangents = tangents != null && tangents.Length == vertices.Length;
            bool hasColors = vertexColors != null && vertexColors.Length == vertices.Length;
            bool hasTexCoord1 = texCoord1 != null && texCoord1.Length == vertices.Length;
            bool hasTexCoord3 = texCoord3 != null && texCoord3.Length == vertices.Length;
            Color32 defaultColor = new Color32(255, 255, 255, 255);

            if (hasTexCoord1 && combinedTexCoord1.Count == 0)
            {
                for (int i = 0; i < combinedVertices.Count; i++) combinedTexCoord1.Add(Vector4.zero);
            }

            bool appendTexCoord1 = hasTexCoord1 || combinedTexCoord1.Count != 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                combinedVertices.Add(vertices[i]);
                combinedNormals.Add(hasNormals ? normals[i] : Vector3.up);
                combinedUVs.Add(hasUVs ? uvs[i] : Vector4.zero);
                combinedTangents.Add(hasTangents ? tangents[i] : new Vector4(1f, 0f, 0f, 1f));
                combinedColors.Add(hasColors ? vertexColors[i] : defaultColor);
                if (appendTexCoord1) combinedTexCoord1.Add(hasTexCoord1 ? texCoord1[i] : Vector4.zero);
                combinedTexCoord3.Add(hasTexCoord3 ? texCoord3[i] : Vector4.zero);
            }
        }
    }
}