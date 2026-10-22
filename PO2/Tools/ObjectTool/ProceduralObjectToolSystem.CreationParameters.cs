using Game;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core.Primitives;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private const float kFinePrimitiveDimensionStep = 0.1f;
        private const float kCoarsePrimitiveDimensionStep = 1f;
        private const float kMinimumPrimitiveDimension = 0.1f;
        private const float kFinePrimitiveDimensionThreshold = 1f;
        private const int kCylinderSegmentStep = 1;
        private const int kConeSegmentStep = 1;
        private const int kArchCurveSegmentStep = 2;
        private const int kStairCountStep = 1;

        private BoxParameters m_BoxCreationParameters;
        private PlaneParameters m_PlaneCreationParameters;
        private WedgeParameters m_WedgeCreationParameters;
        private CylinderParameters m_CylinderCreationParameters;
        private ConeParameters m_ConeCreationParameters;
        private ArchParameters m_ArchCreationParameters;
        private StairsParameters m_StairsCreationParameters;
        private SphereParameters m_SphereCreationParameters;
        private DomeParameters m_DomeCreationParameters;

        public float BoxCreationWidth =>
            m_BoxCreationParameters != null ? m_BoxCreationParameters.Width : 0f;

        public float BoxCreationHeight =>
            m_BoxCreationParameters != null ? m_BoxCreationParameters.Height : 0f;

        public float BoxCreationDepth =>
            m_BoxCreationParameters != null ? m_BoxCreationParameters.Depth : 0f;

        public float PlaneCreationWidth =>
            m_PlaneCreationParameters != null ? m_PlaneCreationParameters.Width : 0f;

        public float PlaneCreationDepth =>
            m_PlaneCreationParameters != null ? m_PlaneCreationParameters.Depth : 0f;

        public float WedgeCreationWidth =>
            m_WedgeCreationParameters != null ? m_WedgeCreationParameters.Width : 0f;

        public float WedgeCreationHeight =>
            m_WedgeCreationParameters != null ? m_WedgeCreationParameters.Height : 0f;

        public float WedgeCreationDepth =>
            m_WedgeCreationParameters != null ? m_WedgeCreationParameters.Depth : 0f;

        public float CylinderCreationRadius =>
            m_CylinderCreationParameters != null ? m_CylinderCreationParameters.Radius : 0f;

        public float CylinderCreationHeight =>
            m_CylinderCreationParameters != null ? m_CylinderCreationParameters.Height : 0f;

        public int CylinderCreationSegments =>
            m_CylinderCreationParameters != null ? m_CylinderCreationParameters.Segments : 0;

        public float ConeCreationBottomDiameter =>
            m_ConeCreationParameters != null ? m_ConeCreationParameters.BottomDiameter : 0f;

        public float ConeCreationTopDiameter =>
            m_ConeCreationParameters != null ? m_ConeCreationParameters.TopDiameter : 0f;

        public float ConeCreationHeight =>
            m_ConeCreationParameters != null ? m_ConeCreationParameters.Height : 0f;

        public int ConeCreationSegments =>
            m_ConeCreationParameters != null ? m_ConeCreationParameters.Segments : 0;

        public float ArchCreationWidth =>
            m_ArchCreationParameters != null ? m_ArchCreationParameters.Width : 0f;

        public float ArchCreationHeight =>
            m_ArchCreationParameters != null ? m_ArchCreationParameters.Height : 0f;

        public float ArchCreationDepth =>
            m_ArchCreationParameters != null ? m_ArchCreationParameters.Depth : 0f;

        public float ArchCreationOpeningWidth =>
            m_ArchCreationParameters != null ? m_ArchCreationParameters.OpeningWidth : 0f;

        public float ArchCreationOpeningHeight =>
            m_ArchCreationParameters != null ? m_ArchCreationParameters.OpeningHeight : 0f;

        public int ArchCreationCurveSegments =>
            m_ArchCreationParameters != null ? m_ArchCreationParameters.CurveSegments : 0;

        public float StairsCreationWidth =>
            m_StairsCreationParameters != null ? m_StairsCreationParameters.Width : 0f;

        public float StairsCreationTreadDepth =>
            m_StairsCreationParameters != null ? m_StairsCreationParameters.TreadDepth : 0f;

        public int StairsCreationSteps =>
            m_StairsCreationParameters != null ? m_StairsCreationParameters.Steps : 0;

        public float SphereCreationRadius =>
            m_SphereCreationParameters != null ? m_SphereCreationParameters.Radius : 0f;

        public int SphereCreationSegments =>
            m_SphereCreationParameters != null ? m_SphereCreationParameters.Segments : 0;

        public int SphereCreationVerticalSegments =>
            m_SphereCreationParameters != null ? m_SphereCreationParameters.VerticalSegments : 0;

        public float DomeCreationRadius =>
            m_DomeCreationParameters != null ? m_DomeCreationParameters.Radius : 0f;

        public float DomeCreationHeight =>
            m_DomeCreationParameters != null ? m_DomeCreationParameters.Height : 0f;

        public int DomeCreationSegments =>
            m_DomeCreationParameters != null ? m_DomeCreationParameters.Segments : 0;

        public int DomeCreationVerticalSegments =>
            m_DomeCreationParameters != null ? m_DomeCreationParameters.VerticalSegments : 0;

        private void InitializePrimitiveCreationParameters()
        {
            if (PrimitiveDefinitions.TryGet(PrimitiveKind.Box, out PrimitiveDefinition boxDefinition) && boxDefinition.DefaultParameters is BoxParameters boxDefaults)
            {
                m_BoxCreationParameters = new BoxParameters(boxDefaults.Width, boxDefaults.Height, boxDefaults.Depth);
            }

            if (PrimitiveDefinitions.TryGet(PrimitiveKind.Plane, out PrimitiveDefinition planeDefinition) && planeDefinition.DefaultParameters is PlaneParameters planeDefaults)
            {
                m_PlaneCreationParameters = new PlaneParameters(planeDefaults.Width, planeDefaults.Depth);
            }

            if (PrimitiveDefinitions.TryGet(PrimitiveKind.Wedge, out PrimitiveDefinition wedgeDefinition) && wedgeDefinition.DefaultParameters is WedgeParameters wedgeDefaults)
            {
                m_WedgeCreationParameters = new WedgeParameters(wedgeDefaults.Width, wedgeDefaults.Height, wedgeDefaults.Depth);
            }

            if (PrimitiveDefinitions.TryGet(PrimitiveKind.Cylinder, out PrimitiveDefinition cylinderDefinition) && cylinderDefinition.DefaultParameters is CylinderParameters cylinderDefaults)
            {
                m_CylinderCreationParameters = new CylinderParameters(cylinderDefaults.Radius, cylinderDefaults.Height, cylinderDefaults.Segments);
            }

            if (PrimitiveDefinitions.TryGet(PrimitiveKind.Cone, out PrimitiveDefinition coneDefinition) && coneDefinition.DefaultParameters is ConeParameters coneDefaults)
            {
                m_ConeCreationParameters = new ConeParameters(coneDefaults.BottomDiameter, coneDefaults.TopDiameter, coneDefaults.Height, coneDefaults.Segments);
            }

            if (PrimitiveDefinitions.TryGet(PrimitiveKind.Arch, out PrimitiveDefinition archDefinition) && archDefinition.DefaultParameters is ArchParameters archDefaults)
            {
                m_ArchCreationParameters = new ArchParameters(archDefaults.Width, archDefaults.Height, archDefaults.Depth, archDefaults.OpeningWidth, archDefaults.OpeningHeight, archDefaults.CurveSegments);
            }

            if (PrimitiveDefinitions.TryGet(PrimitiveKind.Stairs, out PrimitiveDefinition stairsDefinition) && stairsDefinition.DefaultParameters is StairsParameters stairsDefaults)
            {
                m_StairsCreationParameters = new StairsParameters(stairsDefaults.Width, stairsDefaults.TreadDepth, stairsDefaults.Steps);
            }

            if (PrimitiveDefinitions.TryGet(PrimitiveKind.Sphere, out PrimitiveDefinition sphereDefinition) && sphereDefinition.DefaultParameters is SphereParameters sphereDefaults)
            {
                m_SphereCreationParameters = new SphereParameters(sphereDefaults.Radius, sphereDefaults.Segments, sphereDefaults.VerticalSegments);
            }

            if (PrimitiveDefinitions.TryGet(PrimitiveKind.Dome, out PrimitiveDefinition domeDefinition) && domeDefinition.DefaultParameters is DomeParameters domeDefaults)
            {
                m_DomeCreationParameters = new DomeParameters(domeDefaults.Radius, domeDefaults.Height, domeDefaults.Segments, domeDefaults.VerticalSegments);
            }
        }

        private PrimitiveParameters CreatePlacementParametersSnapshot(PrimitiveKind primitiveType)
        {
            // Placement receives a copy so changing the panel defaults after a
            // click cannot mutate parameters already attached to an object.
            switch (primitiveType)
            {
                case PrimitiveKind.Box:
                    return m_BoxCreationParameters != null ? new BoxParameters(m_BoxCreationParameters.Width, m_BoxCreationParameters.Height, m_BoxCreationParameters.Depth) : null;

                case PrimitiveKind.Plane:
                    return m_PlaneCreationParameters != null ? new PlaneParameters(m_PlaneCreationParameters.Width, m_PlaneCreationParameters.Depth) : null;

                case PrimitiveKind.Wedge:
                    return m_WedgeCreationParameters != null ? new WedgeParameters(m_WedgeCreationParameters.Width, m_WedgeCreationParameters.Height, m_WedgeCreationParameters.Depth) : null;

                case PrimitiveKind.Cylinder:
                    return m_CylinderCreationParameters != null ? new CylinderParameters(m_CylinderCreationParameters.Radius, m_CylinderCreationParameters.Height, m_CylinderCreationParameters.Segments) : null;

                case PrimitiveKind.Cone:
                    return m_ConeCreationParameters != null ? new ConeParameters(m_ConeCreationParameters.BottomDiameter, m_ConeCreationParameters.TopDiameter, m_ConeCreationParameters.Height, m_ConeCreationParameters.Segments) : null;

                case PrimitiveKind.Arch:
                    return m_ArchCreationParameters != null ? new ArchParameters(m_ArchCreationParameters.Width, m_ArchCreationParameters.Height, m_ArchCreationParameters.Depth, m_ArchCreationParameters.OpeningWidth, m_ArchCreationParameters.OpeningHeight, m_ArchCreationParameters.CurveSegments) : null;

                case PrimitiveKind.Stairs:
                    return m_StairsCreationParameters != null ? new StairsParameters(m_StairsCreationParameters.Width, m_StairsCreationParameters.TreadDepth, m_StairsCreationParameters.Steps) : null;

                case PrimitiveKind.Sphere:
                    return m_SphereCreationParameters != null ? new SphereParameters(m_SphereCreationParameters.Radius, m_SphereCreationParameters.Segments, m_SphereCreationParameters.VerticalSegments) : null;

                case PrimitiveKind.Dome:
                    return m_DomeCreationParameters != null ? new DomeParameters(m_DomeCreationParameters.Radius, m_DomeCreationParameters.Height, m_DomeCreationParameters.Segments, m_DomeCreationParameters.VerticalSegments) : null;

                default:
                    return null;
            }
        }

        private void SetSphereCreationParameters(float radius, int segments, int verticalSegments)
        {
            m_SphereCreationParameters = new SphereParameters(ClampPrimitiveDimension(radius), Mathf.Clamp(segments, SphereParameters.kMinimumSegments, SphereParameters.kMaximumSegments), Mathf.Clamp(verticalSegments, SphereParameters.kMinimumVerticalSegments, SphereParameters.kMaximumVerticalSegments));
        }

        private void SetDomeCreationParameters(float radius, float height, int segments, int verticalSegments)
        {
            m_DomeCreationParameters = new DomeParameters(ClampPrimitiveDimension(radius), ClampPrimitiveDimension(height), Mathf.Clamp(segments, DomeParameters.kMinimumSegments, DomeParameters.kMaximumSegments), Mathf.Clamp(verticalSegments, DomeParameters.kMinimumVerticalSegments, DomeParameters.kMaximumVerticalSegments));
        }

        private void SetBoxCreationParameters(float width, float height, float depth)
        {
            m_BoxCreationParameters = new BoxParameters(ClampPrimitiveDimension(width), ClampPrimitiveDimension(height), ClampPrimitiveDimension(depth));
        }

        private void SetPlaneCreationParameters(float width, float depth)
        {
            m_PlaneCreationParameters = new PlaneParameters(ClampPrimitiveDimension(width), ClampPrimitiveDimension(depth));
        }

        private void SetWedgeCreationParameters(float width, float height, float depth)
        {
            m_WedgeCreationParameters = new WedgeParameters(ClampPrimitiveDimension(width), ClampPrimitiveDimension(height), ClampPrimitiveDimension(depth));
        }

        private void SetCylinderCreationParameters(float radius, float height, int segments)
        {
            m_CylinderCreationParameters = new CylinderParameters(ClampPrimitiveDimension(radius), ClampPrimitiveDimension(height), ClampCylinderSegments(segments));
        }

        private void SetConeCreationParameters(float bottomDiameter, float topDiameter, float height, int segments)
        {
            m_ConeCreationParameters = new ConeParameters(ClampPrimitiveDimension(bottomDiameter), ClampOptionalPrimitiveDimension(topDiameter), ClampPrimitiveDimension(height), ClampConeSegments(segments));
        }

        private void SetArchCreationParameters(float width, float height, float depth, float openingWidth, float openingHeight, int curveSegments)
        {
            m_ArchCreationParameters = new ArchParameters(width, height, depth, openingWidth, openingHeight, curveSegments);
        }

        private void SetArchCreationWidthValue(float value)
        {
            float minimumWidth = ArchCreationOpeningWidth + ArchParameters.kMinimumSolidThickness * 2f;
            SetArchCreationParameters(Mathf.Max(minimumWidth, value), ArchCreationHeight, ArchCreationDepth, ArchCreationOpeningWidth, ArchCreationOpeningHeight, ArchCreationCurveSegments);
        }

        private void SetArchCreationHeightValue(float value)
        {
            float minimumHeight = ArchCreationOpeningHeight + ArchParameters.kMinimumSolidThickness;
            SetArchCreationParameters(ArchCreationWidth, Mathf.Max(minimumHeight, value), ArchCreationDepth, ArchCreationOpeningWidth, ArchCreationOpeningHeight, ArchCreationCurveSegments);
        }

        private void SetArchCreationDepthValue(float value)
        {
            SetArchCreationParameters(ArchCreationWidth, ArchCreationHeight, ClampPrimitiveDimension(value), ArchCreationOpeningWidth, ArchCreationOpeningHeight, ArchCreationCurveSegments);
        }

        private void SetArchCreationOpeningWidthValue(float value)
        {
            float maximumWidth = Mathf.Min(ArchCreationWidth - ArchParameters.kMinimumSolidThickness * 2f, ArchCreationOpeningHeight * 2f);
            SetArchCreationParameters(ArchCreationWidth, ArchCreationHeight, ArchCreationDepth, Mathf.Clamp(value, ArchParameters.kMinimumOpeningWidth, maximumWidth), ArchCreationOpeningHeight, ArchCreationCurveSegments);
        }

        private void SetArchCreationOpeningHeightValue(float value)
        {
            float minimumHeight = ArchCreationOpeningWidth * 0.5f;
            float maximumHeight = ArchCreationHeight - ArchParameters.kMinimumSolidThickness;
            SetArchCreationParameters(ArchCreationWidth, ArchCreationHeight, ArchCreationDepth, ArchCreationOpeningWidth, Mathf.Clamp(value, minimumHeight, maximumHeight), ArchCreationCurveSegments);
        }

        private void SetArchCreationCurveSegmentsValue(int value)
        {
            SetArchCreationParameters(ArchCreationWidth, ArchCreationHeight, ArchCreationDepth, ArchCreationOpeningWidth, ArchCreationOpeningHeight, ClampArchCurveSegments(value));
        }

        private void SetStairsCreationParameters(float width, float treadDepth, int steps)
        {
            m_StairsCreationParameters = new StairsParameters(ClampPrimitiveDimension(width), ClampPrimitiveDimension(treadDepth), ClampStairCount(steps));
        }

        private static int ClampCylinderSegments(int value)
        {
            return Mathf.Clamp(value, CylinderParameters.kMinimumSegments, CylinderParameters.kMaximumSegments);
        }

        private static int ClampConeSegments(int value)
        {
            return Mathf.Clamp(value, ConeParameters.kMinimumSegments, ConeParameters.kMaximumSegments);
        }

        private static int ClampArchCurveSegments(int value)
        {
            int clamped = Mathf.Clamp(value, ArchParameters.kMinimumCurveSegments, ArchParameters.kMaximumCurveSegments);

            if (clamped % 2 != 0)
            {
                clamped++;
            }

            return Mathf.Min(clamped, ArchParameters.kMaximumCurveSegments);
        }

        private static int ClampStairCount(int value)
        {
            return Mathf.Clamp(value, StairsParameters.kMinimumSteps, StairsParameters.kMaximumSteps);
        }

        private static float AdjustOptionalPrimitiveDimension(float value, int direction)
        {
            if (direction == 0)
            {
                return ClampOptionalPrimitiveDimension(value);
            }

            if (direction > 0 && value <= 0f)
            {
                return kFinePrimitiveDimensionStep;
            }

            if (direction < 0 && value <= kFinePrimitiveDimensionStep)
            {
                return 0f;
            }

            return AdjustPrimitiveDimension(value, direction);
        }

        private static float AdjustPrimitiveDimension(float value, int direction)
        {
            if (direction == 0)
            {
                return ClampPrimitiveDimension(value);
            }

            if (direction > 0)
            {
                // Small primitives need tenth-meter control.
                // Once a dimension reaches one meter, whole-meter steps are much less tedious.
                if (value < kFinePrimitiveDimensionThreshold)
                {
                    float nextFineValue = Mathf.Round((value + kFinePrimitiveDimensionStep) * 10f) / 10f;

                    return Mathf.Min(kFinePrimitiveDimensionThreshold, nextFineValue);
                }

                return value + kCoarsePrimitiveDimensionStep;
            }

            if (value <= kFinePrimitiveDimensionThreshold)
            {
                float nextFineValue = Mathf.Round((value - kFinePrimitiveDimensionStep) * 10f) / 10f;

                return Mathf.Max(kMinimumPrimitiveDimension, nextFineValue);
            }

            float nextCoarseValue = value - kCoarsePrimitiveDimensionStep;
            // Do not jump from a coarse value straight below the fine-step boundary.
            // Land on one meter first, then continue by tenths.
            return nextCoarseValue < kFinePrimitiveDimensionThreshold ? kFinePrimitiveDimensionThreshold : nextCoarseValue;
        }

        private static float ClampPrimitiveDimension(float value)
        {
            return Mathf.Max(kMinimumPrimitiveDimension, value);
        }

        private static float ClampOptionalPrimitiveDimension(float value)
        {
            return Mathf.Max(0f, value);
        }
    }
}