using Game;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PO2.Tools.ObjectTool
{
    // Direct setters used by editable UI fields.
    // The existing parameter helpers still own clamping and
    // keep button changes and typed changes identical.
    public partial class ProceduralObjectToolSystem
    {
        public void SetSphereCreationRadius(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetSphereCreationParameters(value, SphereCreationSegments, SphereCreationVerticalSegments);
        }

        public void SetSphereCreationSegments(int value)
        {
            SetSphereCreationParameters(SphereCreationRadius, value, SphereCreationVerticalSegments);
        }

        public void SetSphereCreationVerticalSegments(int value)
        {
            SetSphereCreationParameters(SphereCreationRadius, SphereCreationSegments, value);
        }

        public void SetDomeCreationRadius(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetDomeCreationParameters(value, DomeCreationHeight, DomeCreationSegments, DomeCreationVerticalSegments);
        }

        public void SetDomeCreationHeight(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetDomeCreationParameters(DomeCreationRadius, value, DomeCreationSegments, DomeCreationVerticalSegments);
        }

        public void SetDomeCreationSegments(int value)
        {
            SetDomeCreationParameters(DomeCreationRadius, DomeCreationHeight, value, DomeCreationVerticalSegments);
        }

        public void SetDomeCreationVerticalSegments(int value)
        {
            SetDomeCreationParameters(DomeCreationRadius, DomeCreationHeight, DomeCreationSegments, value);
        }

        public void SetBoxCreationWidth(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetBoxCreationParameters(value, BoxCreationHeight, BoxCreationDepth);
        }

        public void SetBoxCreationHeight(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetBoxCreationParameters(BoxCreationWidth, value, BoxCreationDepth);
        }

        public void SetBoxCreationDepth(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetBoxCreationParameters(BoxCreationWidth, BoxCreationHeight, value);
        }

        public void SetPlaneCreationWidth(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetPlaneCreationParameters(value, PlaneCreationDepth);
        }

        public void SetPlaneCreationDepth(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetPlaneCreationParameters(PlaneCreationWidth, value);
        }

        public void SetWedgeCreationWidth(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetWedgeCreationParameters(value, WedgeCreationHeight, WedgeCreationDepth);
        }

        public void SetWedgeCreationHeight(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetWedgeCreationParameters(WedgeCreationWidth, value, WedgeCreationDepth);
        }

        public void SetWedgeCreationDepth(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetWedgeCreationParameters(WedgeCreationWidth, WedgeCreationHeight, value);
        }

        public void SetCylinderCreationRadius(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetCylinderCreationParameters(value, CylinderCreationHeight, CylinderCreationSegments);
        }

        public void SetCylinderCreationHeight(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetCylinderCreationParameters(CylinderCreationRadius, value, CylinderCreationSegments);
        }

        public void SetCylinderCreationSegments(int value)
        {
            SetCylinderCreationParameters(CylinderCreationRadius, CylinderCreationHeight, value);
        }

        public void SetConeCreationBottomDiameter(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetConeCreationParameters(value, ConeCreationTopDiameter, ConeCreationHeight, ConeCreationSegments);
        }

        public void SetConeCreationTopDiameter(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetConeCreationParameters(ConeCreationBottomDiameter, value, ConeCreationHeight, ConeCreationSegments);
        }

        public void SetConeCreationHeight(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetConeCreationParameters(ConeCreationBottomDiameter, ConeCreationTopDiameter, value, ConeCreationSegments);
        }

        public void SetConeCreationSegments(int value)
        {
            SetConeCreationParameters(ConeCreationBottomDiameter, ConeCreationTopDiameter, ConeCreationHeight, value);
        }

        public void SetArchCreationWidth(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetArchCreationWidthValue(value);
        }

        public void SetArchCreationHeight(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetArchCreationHeightValue(value);
        }

        public void SetArchCreationDepth(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetArchCreationDepthValue(value);
        }

        public void SetArchCreationOpeningWidth(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetArchCreationOpeningWidthValue(value);
        }

        public void SetArchCreationOpeningHeight(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetArchCreationOpeningHeightValue(value);
        }

        public void SetArchCreationCurveSegments(int value)
        {
            SetArchCreationCurveSegmentsValue(value);
        }

        public void SetStairsCreationWidth(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetStairsCreationParameters(value, StairsCreationTreadDepth, StairsCreationSteps);
        }

        public void SetStairsCreationTreadDepth(float value)
        {
            if (!IsFinite(value))
            {
                return;
            }

            SetStairsCreationParameters(StairsCreationWidth, value, StairsCreationSteps);
        }

        public void SetStairsCreationSteps(int value)
        {
            SetStairsCreationParameters(StairsCreationWidth, StairsCreationTreadDepth, value);
        }

        public void IncreaseSphereRadius()
        {
            SetSphereCreationParameters(AdjustPrimitiveDimension(SphereCreationRadius, 1), SphereCreationSegments, SphereCreationVerticalSegments);
        }

        public void DecreaseSphereRadius()
        {
            SetSphereCreationParameters(AdjustPrimitiveDimension(SphereCreationRadius, -1), SphereCreationSegments, SphereCreationVerticalSegments);
        }

        public void IncreaseSphereSegments()
        {
            SetSphereCreationParameters(SphereCreationRadius, SphereCreationSegments + 1, SphereCreationVerticalSegments);
        }

        public void DecreaseSphereSegments()
        {
            SetSphereCreationParameters(SphereCreationRadius, SphereCreationSegments - 1, SphereCreationVerticalSegments);
        }

        public void IncreaseSphereVerticalSegments()
        {
            SetSphereCreationParameters(SphereCreationRadius, SphereCreationSegments, SphereCreationVerticalSegments + 1);
        }

        public void DecreaseSphereVerticalSegments()
        {
            SetSphereCreationParameters(SphereCreationRadius, SphereCreationSegments, SphereCreationVerticalSegments - 1);
        }

        public void IncreaseDomeRadius()
        {
            SetDomeCreationParameters(AdjustPrimitiveDimension(DomeCreationRadius, 1), DomeCreationHeight, DomeCreationSegments, DomeCreationVerticalSegments);
        }

        public void DecreaseDomeRadius()
        {
            SetDomeCreationParameters(AdjustPrimitiveDimension(DomeCreationRadius, -1), DomeCreationHeight, DomeCreationSegments, DomeCreationVerticalSegments);
        }

        public void IncreaseDomeHeight()
        {
            SetDomeCreationParameters(DomeCreationRadius, AdjustPrimitiveDimension(DomeCreationHeight, 1), DomeCreationSegments, DomeCreationVerticalSegments);
        }

        public void DecreaseDomeHeight()
        {
            SetDomeCreationParameters(DomeCreationRadius, AdjustPrimitiveDimension(DomeCreationHeight, -1), DomeCreationSegments, DomeCreationVerticalSegments);
        }

        public void IncreaseDomeSegments()
        {
            SetDomeCreationParameters(DomeCreationRadius, DomeCreationHeight, DomeCreationSegments + 1, DomeCreationVerticalSegments);
        }

        public void DecreaseDomeSegments()
        {
            SetDomeCreationParameters(DomeCreationRadius, DomeCreationHeight, DomeCreationSegments - 1, DomeCreationVerticalSegments);
        }

        public void IncreaseDomeVerticalSegments()
        {
            SetDomeCreationParameters(DomeCreationRadius, DomeCreationHeight, DomeCreationSegments, DomeCreationVerticalSegments + 1);
        }

        public void DecreaseDomeVerticalSegments()
        {
            SetDomeCreationParameters(DomeCreationRadius, DomeCreationHeight, DomeCreationSegments, DomeCreationVerticalSegments - 1);
        }

        public void IncreaseBoxWidth()
        {
            SetBoxCreationParameters(AdjustPrimitiveDimension(BoxCreationWidth, 1), BoxCreationHeight, BoxCreationDepth);
        }

        public void DecreaseBoxWidth()
        {
            SetBoxCreationParameters(AdjustPrimitiveDimension(BoxCreationWidth, -1), BoxCreationHeight, BoxCreationDepth);
        }

        public void IncreaseBoxHeight()
        {
            SetBoxCreationParameters(BoxCreationWidth, AdjustPrimitiveDimension(BoxCreationHeight, 1), BoxCreationDepth);
        }

        public void DecreaseBoxHeight()
        {
            SetBoxCreationParameters(BoxCreationWidth, AdjustPrimitiveDimension(BoxCreationHeight, -1), BoxCreationDepth);
        }

        public void IncreaseBoxDepth()
        {
            SetBoxCreationParameters(BoxCreationWidth, BoxCreationHeight, AdjustPrimitiveDimension(BoxCreationDepth, 1));
        }

        public void DecreaseBoxDepth()
        {
            SetBoxCreationParameters(BoxCreationWidth, BoxCreationHeight, AdjustPrimitiveDimension(BoxCreationDepth, -1));
        }

        public void IncreasePlaneWidth()
        {
            SetPlaneCreationParameters(AdjustPrimitiveDimension(PlaneCreationWidth, 1), PlaneCreationDepth);
        }

        public void DecreasePlaneWidth()
        {
            SetPlaneCreationParameters(AdjustPrimitiveDimension(PlaneCreationWidth, -1), PlaneCreationDepth);
        }

        public void IncreasePlaneDepth()
        {
            SetPlaneCreationParameters(PlaneCreationWidth, AdjustPrimitiveDimension(PlaneCreationDepth, 1));
        }

        public void DecreasePlaneDepth()
        {
            SetPlaneCreationParameters(PlaneCreationWidth, AdjustPrimitiveDimension(PlaneCreationDepth, -1));
        }

        public void IncreaseWedgeWidth()
        {
            SetWedgeCreationParameters(AdjustPrimitiveDimension(WedgeCreationWidth, 1), WedgeCreationHeight, WedgeCreationDepth);
        }

        public void DecreaseWedgeWidth()
        {
            SetWedgeCreationParameters(AdjustPrimitiveDimension(WedgeCreationWidth, -1), WedgeCreationHeight, WedgeCreationDepth);
        }

        public void IncreaseWedgeHeight()
        {
            SetWedgeCreationParameters(WedgeCreationWidth, AdjustPrimitiveDimension(WedgeCreationHeight, 1), WedgeCreationDepth);
        }

        public void DecreaseWedgeHeight()
        {
            SetWedgeCreationParameters(WedgeCreationWidth, AdjustPrimitiveDimension(WedgeCreationHeight, -1), WedgeCreationDepth);
        }

        public void IncreaseWedgeDepth()
        {
            SetWedgeCreationParameters(WedgeCreationWidth, WedgeCreationHeight, AdjustPrimitiveDimension(WedgeCreationDepth, 1));
        }

        public void DecreaseWedgeDepth()
        {
            SetWedgeCreationParameters(WedgeCreationWidth, WedgeCreationHeight, AdjustPrimitiveDimension(WedgeCreationDepth, -1));
        }

        public void IncreaseCylinderRadius()
        {
            SetCylinderCreationParameters(AdjustPrimitiveDimension(CylinderCreationRadius, 1), CylinderCreationHeight, CylinderCreationSegments);
        }

        public void DecreaseCylinderRadius()
        {
            SetCylinderCreationParameters(AdjustPrimitiveDimension(CylinderCreationRadius, -1), CylinderCreationHeight, CylinderCreationSegments);
        }

        public void IncreaseCylinderHeight()
        {
            SetCylinderCreationParameters(CylinderCreationRadius, AdjustPrimitiveDimension(CylinderCreationHeight, 1), CylinderCreationSegments);
        }

        public void DecreaseCylinderHeight()
        {
            SetCylinderCreationParameters(CylinderCreationRadius, AdjustPrimitiveDimension(CylinderCreationHeight, -1), CylinderCreationSegments);
        }

        public void IncreaseCylinderSegments()
        {
            SetCylinderCreationParameters(CylinderCreationRadius, CylinderCreationHeight, CylinderCreationSegments + kCylinderSegmentStep);
        }

        public void DecreaseCylinderSegments()
        {
            SetCylinderCreationParameters(CylinderCreationRadius, CylinderCreationHeight, CylinderCreationSegments - kCylinderSegmentStep);
        }

        public void IncreaseConeBottomDiameter()
        {
            SetConeCreationParameters(AdjustPrimitiveDimension(ConeCreationBottomDiameter, 1), ConeCreationTopDiameter, ConeCreationHeight, ConeCreationSegments);
        }

        public void DecreaseConeBottomDiameter()
        {
            SetConeCreationParameters(AdjustPrimitiveDimension(ConeCreationBottomDiameter, -1), ConeCreationTopDiameter, ConeCreationHeight, ConeCreationSegments);
        }

        public void IncreaseConeTopDiameter()
        {
            SetConeCreationParameters(ConeCreationBottomDiameter, AdjustOptionalPrimitiveDimension(ConeCreationTopDiameter, 1), ConeCreationHeight, ConeCreationSegments);
        }

        public void DecreaseConeTopDiameter()
        {
            SetConeCreationParameters(ConeCreationBottomDiameter, AdjustOptionalPrimitiveDimension(ConeCreationTopDiameter, -1), ConeCreationHeight, ConeCreationSegments);
        }

        public void IncreaseConeHeight()
        {
            SetConeCreationParameters(ConeCreationBottomDiameter, ConeCreationTopDiameter, AdjustPrimitiveDimension(ConeCreationHeight, 1), ConeCreationSegments);
        }

        public void DecreaseConeHeight()
        {
            SetConeCreationParameters(ConeCreationBottomDiameter, ConeCreationTopDiameter, AdjustPrimitiveDimension(ConeCreationHeight, -1), ConeCreationSegments);
        }

        public void IncreaseConeSegments()
        {
            SetConeCreationParameters(ConeCreationBottomDiameter, ConeCreationTopDiameter, ConeCreationHeight, ConeCreationSegments + kConeSegmentStep);
        }

        public void DecreaseConeSegments()
        {
            SetConeCreationParameters(ConeCreationBottomDiameter, ConeCreationTopDiameter, ConeCreationHeight, ConeCreationSegments - kConeSegmentStep);
        }

        public void IncreaseArchWidth()
        {
            SetArchCreationWidthValue(AdjustPrimitiveDimension(ArchCreationWidth, 1));
        }

        public void DecreaseArchWidth()
        {
            SetArchCreationWidthValue(AdjustPrimitiveDimension(ArchCreationWidth, -1));
        }

        public void IncreaseArchHeight()
        {
            SetArchCreationHeightValue(AdjustPrimitiveDimension(ArchCreationHeight, 1));
        }

        public void DecreaseArchHeight()
        {
            SetArchCreationHeightValue(AdjustPrimitiveDimension(ArchCreationHeight, -1));
        }

        public void IncreaseArchDepth()
        {
            SetArchCreationDepthValue(AdjustPrimitiveDimension(ArchCreationDepth, 1));
        }

        public void DecreaseArchDepth()
        {
            SetArchCreationDepthValue(AdjustPrimitiveDimension(ArchCreationDepth, -1));
        }

        public void IncreaseArchOpeningWidth()
        {
            SetArchCreationOpeningWidthValue(AdjustPrimitiveDimension(ArchCreationOpeningWidth, 1));
        }

        public void DecreaseArchOpeningWidth()
        {
            SetArchCreationOpeningWidthValue(AdjustPrimitiveDimension(ArchCreationOpeningWidth, -1));
        }

        public void IncreaseArchOpeningHeight()
        {
            SetArchCreationOpeningHeightValue(AdjustPrimitiveDimension(ArchCreationOpeningHeight, 1));
        }

        public void DecreaseArchOpeningHeight()
        {
            SetArchCreationOpeningHeightValue(AdjustPrimitiveDimension(ArchCreationOpeningHeight, -1));
        }

        public void IncreaseArchCurveSegments()
        {
            SetArchCreationCurveSegmentsValue(ArchCreationCurveSegments + kArchCurveSegmentStep);
        }

        public void DecreaseArchCurveSegments()
        {
            SetArchCreationCurveSegmentsValue(ArchCreationCurveSegments - kArchCurveSegmentStep);
        }

        public void IncreaseStairsWidth()
        {
            SetStairsCreationParameters(AdjustPrimitiveDimension(StairsCreationWidth, 1), StairsCreationTreadDepth, StairsCreationSteps);
        }

        public void DecreaseStairsWidth()
        {
            SetStairsCreationParameters(AdjustPrimitiveDimension(StairsCreationWidth, -1), StairsCreationTreadDepth, StairsCreationSteps);
        }

        public void IncreaseStairsTreadDepth()
        {
            SetStairsCreationParameters(StairsCreationWidth, AdjustPrimitiveDimension(StairsCreationTreadDepth, 1), StairsCreationSteps);
        }

        public void DecreaseStairsTreadDepth()
        {
            SetStairsCreationParameters(StairsCreationWidth, AdjustPrimitiveDimension(StairsCreationTreadDepth, -1), StairsCreationSteps);
        }

        public void IncreaseStairsSteps()
        {
            SetStairsCreationParameters(StairsCreationWidth, StairsCreationTreadDepth, StairsCreationSteps + kStairCountStep);
        }

        public void DecreaseStairsSteps()
        {
            SetStairsCreationParameters(StairsCreationWidth, StairsCreationTreadDepth, StairsCreationSteps - kStairCountStep);
        }
    }
}