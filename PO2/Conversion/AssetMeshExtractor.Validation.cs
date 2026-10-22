using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PO2.Conversion
{
    internal sealed partial class AssetMeshExtractor
    {
        private const int kStreamCompatibilityValidationVertexLimit = 4096;
        private const float kStreamValidationTolerance = 0.00001f;

        private static void ValidateDecodedStreams(AssetConversionDecodedStreams actual, AssetConversionDecodedPartData expected)
        {
            if (actual == null || expected == null)
            {
                throw new InvalidOperationException("One of the decoded stream snapshots is missing.");
            }

            ValidateVector3Array(actual.Vertices, expected.Vertices, "positions");
            ValidateVector3Array(actual.Normals, expected.Normals, "normals");
            ValidateVector4Array(actual.Tangents, expected.Tangents, "tangents");
            ValidateVector4Array(actual.UV0, expected.UV0, "UV0");
            ValidateColorArray(actual.VertexColors, expected.VertexColors, "vertex colors");
            ValidateVector4Array(actual.TexCoord1, expected.TexCoord1, "TexCoord1");
            ValidateVector4Array(actual.TexCoord3, expected.TexCoord3, "TexCoord3");
            ValidateIntArray(actual.TriangleAreaIds, expected.TriangleAreaIds, "triangle Surface Area membership");
            ValidateRegionRanges(actual.RegionRanges, expected.RegionRanges);

            if (actual.SourceVertexAttributes == null || expected.SourceVertexAttributes == null || actual.SourceVertexAttributes.Length != expected.SourceVertexAttributes.Length)
            {
                throw new InvalidOperationException("Source vertex-attribute flags have different lengths.");
            }

            for (int i = 0; i < actual.SourceVertexAttributes.Length; i++)
            {
                if (actual.SourceVertexAttributes[i] != expected.SourceVertexAttributes[i])
                {
                    throw new InvalidOperationException($"Source vertex-attribute flags differ in region {i}.");
                }
            }

            if (actual.LocalBounds.HasBounds != expected.LocalBounds.HasBounds || (actual.LocalBounds.HasBounds && (!Approximately(actual.LocalBounds.Min, expected.LocalBounds.Min) || !Approximately(actual.LocalBounds.Max, expected.LocalBounds.Max))))
            {
                throw new InvalidOperationException("Converted local bounds differ from the managed decoder.");
            }
        }

        private static void ValidateVector3Array(Vector3[] actual, Vector3[] expected, string label)
        {
            ValidateArrayShape(actual, expected, label);
            if (actual == null)
            {
                return;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                if (!Approximately(actual[i], expected[i]))
                {
                    throw new InvalidOperationException($"Decoded {label} differ at vertex {i}.");
                }
            }
        }

        private static void ValidateVector4Array(Vector4[] actual, Vector4[] expected, string label)
        {
            ValidateArrayShape(actual, expected, label);
            if (actual == null)
            {
                return;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                if (!Approximately(actual[i], expected[i]))
                {
                    throw new InvalidOperationException($"Decoded {label} differ at vertex {i}.");
                }
            }
        }

        private static void ValidateColorArray(Color32[] actual, Color32[] expected, string label)
        {
            ValidateArrayShape(actual, expected, label);
            if (actual == null)
            {
                return;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                Color32 actualColor = actual[i];
                Color32 expectedColor = expected[i];
                if (actualColor.r != expectedColor.r || actualColor.g != expectedColor.g || actualColor.b != expectedColor.b || actualColor.a != expectedColor.a)
                {
                    throw new InvalidOperationException($"Decoded {label} differ at vertex {i}.");
                }
            }
        }

        private static void ValidateIntArray(int[] actual, int[] expected, string label)
        {
            ValidateArrayShape(actual, expected, label);
            if (actual == null)
            {
                return;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    throw new InvalidOperationException($"Decoded {label} differ at entry {i}.");
                }
            }
        }

        private static void ValidateRegionRanges(AssetConversionRegionRange[] actual, AssetConversionRegionRange[] expected)
        {
            if (actual == null || expected == null || actual.Length != expected.Length)
            {
                throw new InvalidOperationException("Decoded geometry region ranges have different shapes.");
            }

            for (int i = 0; i < actual.Length; i++)
            {
                AssetConversionRegionRange actualRange = actual[i];
                AssetConversionRegionRange expectedRange = expected[i];
                if (actualRange.SurfaceAreaId != expectedRange.SurfaceAreaId || actualRange.VertexOffset != expectedRange.VertexOffset || actualRange.VertexCount != expectedRange.VertexCount || actualRange.TriangleOffset != expectedRange.TriangleOffset || actualRange.TriangleCount != expectedRange.TriangleCount)
                {
                    throw new InvalidOperationException($"Decoded geometry region range {i} differs from the managed decoder.");
                }
            }
        }

        private static void ValidateArrayShape(Array actual, Array expected, string label)
        {
            if ((actual == null) != (expected == null) || (actual != null && actual.Length != expected.Length))
            {
                throw new InvalidOperationException($"Decoded {label} have different stream shapes.");
            }
        }

        private static bool Approximately(Vector3 actual, Vector3 expected)
        {
            return Approximately(actual.x, expected.x) && Approximately(actual.y, expected.y) && Approximately(actual.z, expected.z);
        }

        private static bool Approximately(Vector4 actual, Vector4 expected)
        {
            return Approximately(actual.x, expected.x) && Approximately(actual.y, expected.y) && Approximately(actual.z, expected.z) && Approximately(actual.w, expected.w);
        }

        private static bool Approximately(float actual, float expected)
        {
            if (actual.Equals(expected))
            {
                return true;
            }

            if (float.IsNaN(actual) || float.IsNaN(expected) || float.IsInfinity(actual) || float.IsInfinity(expected))
            {
                return false;
            }

            return Math.Abs(actual - expected) <= kStreamValidationTolerance;
        }
    }
}