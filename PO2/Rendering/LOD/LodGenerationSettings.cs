using System;

namespace PO2.Rendering.Lod
{
    internal sealed class LodGenerationSettings
    {
        // Internal safety and stream weights shared by every generation request.
        internal static LodGenerationSettings CreateCalibrationBaseline()
        {
            return new LodGenerationSettings(minimumSourceTrianglesPerArea: 24, minimumTargetTrianglesPerArea: 4, minimumLevelTriangleReduction: 0.05f, minimumWholeObjectTriangleReduction: 0.05f, normalWeight: 0.01f, tangentWeight: 0.01f, colorWeight: 1.0f, texCoord3Weight: 0.01f, minimumUvWeight: 0.01f, maximumUvWeight: 0.01f);
        }

        internal LodGenerationSettings(int minimumSourceTrianglesPerArea, int minimumTargetTrianglesPerArea, float minimumLevelTriangleReduction, float minimumWholeObjectTriangleReduction, float normalWeight, float tangentWeight, float colorWeight, float texCoord3Weight, float minimumUvWeight, float maximumUvWeight)
        {
            if (minimumSourceTrianglesPerArea < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumSourceTrianglesPerArea));
            }

            if (minimumTargetTrianglesPerArea < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumTargetTrianglesPerArea));
            }

            if (minimumTargetTrianglesPerArea >= minimumSourceTrianglesPerArea)
            {
                throw new ArgumentException("The per-area LOD target must be below the " + "simplification threshold.", nameof(minimumTargetTrianglesPerArea));
            }

            ValidateUnitInterval(minimumLevelTriangleReduction, nameof(minimumLevelTriangleReduction));
            ValidateUnitInterval(minimumWholeObjectTriangleReduction, nameof(minimumWholeObjectTriangleReduction));
            ValidateNonNegative(normalWeight, nameof(normalWeight));
            ValidateNonNegative(tangentWeight, nameof(tangentWeight));
            ValidateNonNegative(colorWeight, nameof(colorWeight));
            ValidateNonNegative(texCoord3Weight, nameof(texCoord3Weight));
            ValidateNonNegative(minimumUvWeight, nameof(minimumUvWeight));
            ValidateNonNegative(maximumUvWeight, nameof(maximumUvWeight));
            if (maximumUvWeight < minimumUvWeight)
            {
                throw new ArgumentException("Maximum UV weight cannot be lower than minimum UV weight.", nameof(maximumUvWeight));
            }

            MinimumSourceTrianglesPerArea = minimumSourceTrianglesPerArea;
            MinimumTargetTrianglesPerArea = minimumTargetTrianglesPerArea;
            MinimumLevelTriangleReduction = minimumLevelTriangleReduction;
            MinimumWholeObjectTriangleReduction = minimumWholeObjectTriangleReduction;
            NormalWeight = normalWeight;
            TangentWeight = tangentWeight;
            ColorWeight = colorWeight;
            TexCoord3Weight = texCoord3Weight;
            MinimumUvWeight = minimumUvWeight;
            MaximumUvWeight = maximumUvWeight;
        }

        internal int MinimumSourceTrianglesPerArea { get; }
        internal int MinimumTargetTrianglesPerArea { get; }
        internal float MinimumLevelTriangleReduction { get; }
        internal float MinimumWholeObjectTriangleReduction { get; }
        internal float NormalWeight { get; }
        internal float TangentWeight { get; }
        internal float ColorWeight { get; }
        internal float TexCoord3Weight { get; }
        internal float MinimumUvWeight { get; }
        internal float MaximumUvWeight { get; }

        private static void ValidateUnitInterval(float value, string parameterName)
        {
            if (!IsFinite(value) || value < 0.0f || value >= 1.0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidateNonNegative(float value, string parameterName)
        {
            if (!IsFinite(value) || value < 0.0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}