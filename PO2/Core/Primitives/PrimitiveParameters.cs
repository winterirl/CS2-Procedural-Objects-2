using System;

namespace PO2.Core.Primitives
{
    // Validated parameter snapshots keep primitive placement independent of live geometry.
    public abstract class PrimitiveParameters
    {
        public abstract PrimitiveKind PrimitiveType { get; }

        protected static void ValidatePositiveFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Primitive dimensions must be finite and greater than zero.");
            }
        }

        protected static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Primitive dimensions must be finite and zero or greater.");
            }
        }
    }

    public sealed class BoxParameters : PrimitiveParameters
    {
        public override PrimitiveKind PrimitiveType =>
            PrimitiveKind.Box;

        public float Width { get; }
        public float Height { get; }
        public float Depth { get; }

        public BoxParameters(float width, float height, float depth)
        {
            ValidatePositiveFinite(width, nameof(width));
            ValidatePositiveFinite(height, nameof(height));
            ValidatePositiveFinite(depth, nameof(depth));

            Width = width;
            Height = height;
            Depth = depth;
        }
    }

    public sealed class PlaneParameters : PrimitiveParameters
    {
        public override PrimitiveKind PrimitiveType =>
            PrimitiveKind.Plane;

        public float Width { get; }
        public float Depth { get; }

        public PlaneParameters(float width, float depth)
        {
            ValidatePositiveFinite(width, nameof(width));
            ValidatePositiveFinite(depth, nameof(depth));

            Width = width;
            Depth = depth;
        }
    }

    public sealed class WedgeParameters : PrimitiveParameters
    {
        public override PrimitiveKind PrimitiveType =>
            PrimitiveKind.Wedge;

        public float Width { get; }
        public float Height { get; }
        public float Depth { get; }

        public WedgeParameters(float width, float height, float depth)
        {
            ValidatePositiveFinite(width, nameof(width));
            ValidatePositiveFinite(height, nameof(height));
            ValidatePositiveFinite(depth, nameof(depth));

            Width = width;
            Height = height;
            Depth = depth;
        }
    }

    public sealed class CylinderParameters : PrimitiveParameters
    {
        // 5 is the effective minimum.
        // 3 and 4 are represented by Wedge and Box rather than overloading Cylinder to make the same thing
        // But technically, cylinder could be created with 3 or 4, it's not invalid geometry.
        // I just don't like that paradigm.
        public const int kMinimumSegments = 5;
        public const int kMaximumSegments = 64;

        public override PrimitiveKind PrimitiveType =>
            PrimitiveKind.Cylinder;

        public float Radius { get; }
        public float Height { get; }
        public int Segments { get; }

        public CylinderParameters(float radius, float height, int segments)
        {
            ValidatePositiveFinite(radius, nameof(radius));
            ValidatePositiveFinite(height, nameof(height));

            if (segments < kMinimumSegments || segments > kMaximumSegments)
            {
                throw new ArgumentOutOfRangeException(nameof(segments), segments, $"Cylinder segments must be between {kMinimumSegments} and {kMaximumSegments}.");
            }

            Radius = radius;
            Height = height;
            Segments = segments;
        }
    }

    public sealed class ConeParameters : PrimitiveParameters
    {
        public const int kMinimumSegments = 3;
        public const int kMaximumSegments = 64;

        public override PrimitiveKind PrimitiveType =>
            PrimitiveKind.Cone;

        public float BottomDiameter { get; }
        public float TopDiameter { get; }
        public float Height { get; }
        public int Segments { get; }

        public ConeParameters(float bottomDiameter, float topDiameter, float height, int segments)
        {
            ValidatePositiveFinite(bottomDiameter, nameof(bottomDiameter));
            ValidateNonNegativeFinite(topDiameter, nameof(topDiameter));
            ValidatePositiveFinite(height, nameof(height));

            if (segments < kMinimumSegments || segments > kMaximumSegments)
            {
                throw new ArgumentOutOfRangeException(nameof(segments), segments, $"Cone segments must be between {kMinimumSegments} and {kMaximumSegments}.");
            }

            BottomDiameter = bottomDiameter;
            TopDiameter = topDiameter;
            Height = height;
            Segments = segments;
        }
    }

    public sealed class ArchParameters : PrimitiveParameters
    {
        public const float kMinimumSolidThickness = 0.1f;
        public const float kMinimumOpeningWidth = 0.2f;
        public const int kMinimumCurveSegments = 4;
        public const int kMaximumCurveSegments = 64;

        public override PrimitiveKind PrimitiveType =>
            PrimitiveKind.Arch;

        public float Width { get; }
        public float Height { get; }
        public float Depth { get; }
        public float OpeningWidth { get; }
        public float OpeningHeight { get; }
        public int CurveSegments { get; }

        public ArchParameters(float width, float height, float depth, float openingWidth, float openingHeight, int curveSegments)
        {
            ValidatePositiveFinite(width, nameof(width));
            ValidatePositiveFinite(height, nameof(height));
            ValidatePositiveFinite(depth, nameof(depth));
            ValidatePositiveFinite(openingWidth, nameof(openingWidth));
            ValidatePositiveFinite(openingHeight, nameof(openingHeight));

            if (openingWidth < kMinimumOpeningWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(openingWidth), openingWidth, $"Arch opening width must be at least {kMinimumOpeningWidth:0.0} meters.");
            }

            if (openingWidth > width - kMinimumSolidThickness * 2f)
            {
                throw new ArgumentOutOfRangeException(nameof(openingWidth), openingWidth, "Arch opening width must leave solid material on both sides.");
            }

            if (openingHeight > height - kMinimumSolidThickness)
            {
                throw new ArgumentOutOfRangeException(nameof(openingHeight), openingHeight, "Arch opening height must leave solid material above the crown.");
            }

            if (openingHeight < openingWidth * 0.5f)
            {
                throw new ArgumentOutOfRangeException(nameof(openingHeight), openingHeight, "Arch opening height must fit the semicircular crown.");
            }

            if (curveSegments < kMinimumCurveSegments || curveSegments > kMaximumCurveSegments || curveSegments % 2 != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(curveSegments), curveSegments, $"Arch curve segments must be an even number between {kMinimumCurveSegments} and {kMaximumCurveSegments}.");
            }

            Width = width;
            Height = height;
            Depth = depth;
            OpeningWidth = openingWidth;
            OpeningHeight = openingHeight;
            CurveSegments = curveSegments;
        }
    }

    public sealed class StairsParameters : PrimitiveParameters
    {
        public const float kStepHeight = 0.2f;
        public const int kMinimumSteps = 1;
        public const int kMaximumSteps = 128;

        public override PrimitiveKind PrimitiveType =>
            PrimitiveKind.Stairs;

        public float Width { get; }
        public float TreadDepth { get; }
        public int Steps { get; }
        public float TotalHeight => Steps * kStepHeight;
        public float TotalDepth => Steps * TreadDepth;

        public StairsParameters(float width, float treadDepth, int steps)
        {
            ValidatePositiveFinite(width, nameof(width));
            ValidatePositiveFinite(treadDepth, nameof(treadDepth));

            if (steps < kMinimumSteps || steps > kMaximumSteps)
            {
                throw new ArgumentOutOfRangeException(nameof(steps), steps, $"Stair count must be between {kMinimumSteps} and {kMaximumSteps}.");
            }

            Width = width;
            TreadDepth = treadDepth;
            Steps = steps;
        }
    }

    public sealed class SphereParameters : PrimitiveParameters
    {
        public const int kMinimumSegments = 3;
        public const int kMaximumSegments = 64;
        public const int kMinimumVerticalSegments = 2;
        public const int kMaximumVerticalSegments = 64;

        public override PrimitiveKind PrimitiveType =>
            PrimitiveKind.Sphere;

        public float Radius { get; }
        public int Segments { get; }
        public int VerticalSegments { get; }

        public SphereParameters(float radius, int segments, int verticalSegments)
        {
            ValidatePositiveFinite(radius, nameof(radius));
            if (segments < kMinimumSegments || segments > kMaximumSegments)
            {
                throw new ArgumentOutOfRangeException(nameof(segments), segments, $"Sphere segments must be between {kMinimumSegments} and {kMaximumSegments}.");
            }

            if (verticalSegments < kMinimumVerticalSegments || verticalSegments > kMaximumVerticalSegments)
            {
                throw new ArgumentOutOfRangeException(nameof(verticalSegments), verticalSegments, $"Sphere vertical segments must be between {kMinimumVerticalSegments} and {kMaximumVerticalSegments}.");
            }

            Radius = radius;
            Segments = segments;
            VerticalSegments = verticalSegments;
        }
    }

    public sealed class DomeParameters : PrimitiveParameters
    {
        public const int kMinimumSegments = 3;
        public const int kMaximumSegments = 64;
        public const int kMinimumVerticalSegments = 2;
        public const int kMaximumVerticalSegments = 64;

        public override PrimitiveKind PrimitiveType =>
            PrimitiveKind.Dome;

        public float Radius { get; }
        public float Height { get; }
        public int Segments { get; }
        public int VerticalSegments { get; }

        public DomeParameters(float radius, float height, int segments, int verticalSegments)
        {
            ValidatePositiveFinite(radius, nameof(radius));
            ValidatePositiveFinite(height, nameof(height));
            if (segments < kMinimumSegments || segments > kMaximumSegments)
            {
                throw new ArgumentOutOfRangeException(nameof(segments), segments, $"Dome segments must be between {kMinimumSegments} and {kMaximumSegments}.");
            }

            if (verticalSegments < kMinimumVerticalSegments || verticalSegments > kMaximumVerticalSegments)
            {
                throw new ArgumentOutOfRangeException(nameof(verticalSegments), verticalSegments, $"Dome vertical segments must be between {kMinimumVerticalSegments} and {kMaximumVerticalSegments}.");
            }

            Radius = radius;
            Height = height;
            Segments = segments;
            VerticalSegments = verticalSegments;
        }
    }
}