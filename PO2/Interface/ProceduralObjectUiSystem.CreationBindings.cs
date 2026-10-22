using Colossal.UI.Binding;
using PO2.Core.Primitives;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private void RegisterCreationStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "placingPrimitiveType", () => m_ProceduralObjectToolSystem.ActivePlacementPrimitiveType));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedCreationPrimitiveType", () => m_ProceduralObjectToolSystem.SelectedCreationPrimitiveType));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "sphereCreationRadius", () => m_ProceduralObjectToolSystem.SphereCreationRadius));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "sphereCreationSegments", () => m_ProceduralObjectToolSystem.SphereCreationSegments));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "sphereCreationVerticalSegments", () => m_ProceduralObjectToolSystem.SphereCreationVerticalSegments));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "domeCreationRadius", () => m_ProceduralObjectToolSystem.DomeCreationRadius));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "domeCreationHeight", () => m_ProceduralObjectToolSystem.DomeCreationHeight));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "domeCreationSegments", () => m_ProceduralObjectToolSystem.DomeCreationSegments));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "domeCreationVerticalSegments", () => m_ProceduralObjectToolSystem.DomeCreationVerticalSegments));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "boxCreationWidth", () => m_ProceduralObjectToolSystem.BoxCreationWidth));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "boxCreationHeight", () => m_ProceduralObjectToolSystem.BoxCreationHeight));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "boxCreationDepth", () => m_ProceduralObjectToolSystem.BoxCreationDepth));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "planeCreationWidth", () => m_ProceduralObjectToolSystem.PlaneCreationWidth));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "planeCreationDepth", () => m_ProceduralObjectToolSystem.PlaneCreationDepth));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "wedgeCreationWidth", () => m_ProceduralObjectToolSystem.WedgeCreationWidth));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "wedgeCreationHeight", () => m_ProceduralObjectToolSystem.WedgeCreationHeight));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "wedgeCreationDepth", () => m_ProceduralObjectToolSystem.WedgeCreationDepth));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "cylinderCreationRadius", () => m_ProceduralObjectToolSystem.CylinderCreationRadius));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "cylinderCreationHeight", () => m_ProceduralObjectToolSystem.CylinderCreationHeight));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "cylinderCreationSegments", () => m_ProceduralObjectToolSystem.CylinderCreationSegments));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "coneCreationBottomDiameter", () => m_ProceduralObjectToolSystem.ConeCreationBottomDiameter));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "coneCreationTopDiameter", () => m_ProceduralObjectToolSystem.ConeCreationTopDiameter));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "coneCreationHeight", () => m_ProceduralObjectToolSystem.ConeCreationHeight));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "coneCreationSegments", () => m_ProceduralObjectToolSystem.ConeCreationSegments));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "archCreationWidth", () => m_ProceduralObjectToolSystem.ArchCreationWidth));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "archCreationHeight", () => m_ProceduralObjectToolSystem.ArchCreationHeight));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "archCreationDepth", () => m_ProceduralObjectToolSystem.ArchCreationDepth));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "archCreationOpeningWidth", () => m_ProceduralObjectToolSystem.ArchCreationOpeningWidth));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "archCreationOpeningHeight", () => m_ProceduralObjectToolSystem.ArchCreationOpeningHeight));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "archCreationCurveSegments", () => m_ProceduralObjectToolSystem.ArchCreationCurveSegments));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "stairsCreationWidth", () => m_ProceduralObjectToolSystem.StairsCreationWidth));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "stairsCreationTreadDepth", () => m_ProceduralObjectToolSystem.StairsCreationTreadDepth));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "stairsCreationSteps", () => m_ProceduralObjectToolSystem.StairsCreationSteps));
        }

        private void RegisterRoundPrimitiveActions()
        {
            AddBinding(CommandBinding("placeSphere", GuardHistory(() => m_ProceduralObjectToolSystem.BeginPrimitivePlacement(PrimitiveKind.Sphere))));

            AddBinding(new TriggerBinding(kGroup, "increaseSphereRadius", GuardHistory(m_ProceduralObjectToolSystem.IncreaseSphereRadius)));

            AddBinding(new TriggerBinding(kGroup, "decreaseSphereRadius", GuardHistory(m_ProceduralObjectToolSystem.DecreaseSphereRadius)));

            AddBinding(new TriggerBinding<float>(kGroup, "setSphereRadius", GuardHistory<float>(m_ProceduralObjectToolSystem.SetSphereCreationRadius)));

            AddBinding(new TriggerBinding(kGroup, "increaseSphereSegments", GuardHistory(m_ProceduralObjectToolSystem.IncreaseSphereSegments)));

            AddBinding(new TriggerBinding(kGroup, "decreaseSphereSegments", GuardHistory(m_ProceduralObjectToolSystem.DecreaseSphereSegments)));

            AddBinding(new TriggerBinding<int>(kGroup, "setSphereSegments", GuardHistory<int>(m_ProceduralObjectToolSystem.SetSphereCreationSegments)));

            AddBinding(new TriggerBinding(kGroup, "increaseSphereVerticalSegments", GuardHistory(m_ProceduralObjectToolSystem.IncreaseSphereVerticalSegments)));

            AddBinding(new TriggerBinding(kGroup, "decreaseSphereVerticalSegments", GuardHistory(m_ProceduralObjectToolSystem.DecreaseSphereVerticalSegments)));

            AddBinding(new TriggerBinding<int>(kGroup, "setSphereVerticalSegments", GuardHistory<int>(m_ProceduralObjectToolSystem.SetSphereCreationVerticalSegments)));

            AddBinding(CommandBinding("placeDome", GuardHistory(() => m_ProceduralObjectToolSystem.BeginPrimitivePlacement(PrimitiveKind.Dome))));

            AddBinding(new TriggerBinding(kGroup, "increaseDomeRadius", GuardHistory(m_ProceduralObjectToolSystem.IncreaseDomeRadius)));

            AddBinding(new TriggerBinding(kGroup, "decreaseDomeRadius", GuardHistory(m_ProceduralObjectToolSystem.DecreaseDomeRadius)));

            AddBinding(new TriggerBinding<float>(kGroup, "setDomeRadius", GuardHistory<float>(m_ProceduralObjectToolSystem.SetDomeCreationRadius)));

            AddBinding(new TriggerBinding(kGroup, "increaseDomeHeight", GuardHistory(m_ProceduralObjectToolSystem.IncreaseDomeHeight)));

            AddBinding(new TriggerBinding(kGroup, "decreaseDomeHeight", GuardHistory(m_ProceduralObjectToolSystem.DecreaseDomeHeight)));

            AddBinding(new TriggerBinding<float>(kGroup, "setDomeHeight", GuardHistory<float>(m_ProceduralObjectToolSystem.SetDomeCreationHeight)));

            AddBinding(new TriggerBinding(kGroup, "increaseDomeSegments", GuardHistory(m_ProceduralObjectToolSystem.IncreaseDomeSegments)));

            AddBinding(new TriggerBinding(kGroup, "decreaseDomeSegments", GuardHistory(m_ProceduralObjectToolSystem.DecreaseDomeSegments)));

            AddBinding(new TriggerBinding<int>(kGroup, "setDomeSegments", GuardHistory<int>(m_ProceduralObjectToolSystem.SetDomeCreationSegments)));

            AddBinding(new TriggerBinding(kGroup, "increaseDomeVerticalSegments", GuardHistory(m_ProceduralObjectToolSystem.IncreaseDomeVerticalSegments)));

            AddBinding(new TriggerBinding(kGroup, "decreaseDomeVerticalSegments", GuardHistory(m_ProceduralObjectToolSystem.DecreaseDomeVerticalSegments)));

            AddBinding(new TriggerBinding<int>(kGroup, "setDomeVerticalSegments", GuardHistory<int>(m_ProceduralObjectToolSystem.SetDomeCreationVerticalSegments)));
        }

        private void RegisterPrimitivePlacementBindings()
        {
            AddBinding(CommandBinding("placeBox", GuardHistory(() => m_ProceduralObjectToolSystem.BeginPrimitivePlacement(PrimitiveKind.Box))));

            AddBinding(CommandBinding("placePlane", GuardHistory(() => m_ProceduralObjectToolSystem.BeginPrimitivePlacement(PrimitiveKind.Plane))));

            AddBinding(CommandBinding("placeWedge", GuardHistory(() => m_ProceduralObjectToolSystem.BeginPrimitivePlacement(PrimitiveKind.Wedge))));

            AddBinding(CommandBinding("placeCylinder", GuardHistory(() => m_ProceduralObjectToolSystem.BeginPrimitivePlacement(PrimitiveKind.Cylinder))));

            AddBinding(CommandBinding("placeCone", GuardHistory(() => m_ProceduralObjectToolSystem.BeginPrimitivePlacement(PrimitiveKind.Cone))));

            AddBinding(CommandBinding("placeArch", GuardHistory(() => m_ProceduralObjectToolSystem.BeginPrimitivePlacement(PrimitiveKind.Arch))));

            AddBinding(CommandBinding("placeStairs", GuardHistory(() => m_ProceduralObjectToolSystem.BeginPrimitivePlacement(PrimitiveKind.Stairs))));
        }

        private void RegisterPrimitiveParameterBindings()
        {
            AddBinding(new TriggerBinding(kGroup, "increaseBoxWidth", GuardHistory(m_ProceduralObjectToolSystem.IncreaseBoxWidth)));

            AddBinding(new TriggerBinding(kGroup, "decreaseBoxWidth", GuardHistory(m_ProceduralObjectToolSystem.DecreaseBoxWidth)));

            AddBinding(new TriggerBinding(kGroup, "increaseBoxHeight", GuardHistory(m_ProceduralObjectToolSystem.IncreaseBoxHeight)));

            AddBinding(new TriggerBinding(kGroup, "decreaseBoxHeight", GuardHistory(m_ProceduralObjectToolSystem.DecreaseBoxHeight)));

            AddBinding(new TriggerBinding(kGroup, "increaseBoxDepth", GuardHistory(m_ProceduralObjectToolSystem.IncreaseBoxDepth)));

            AddBinding(new TriggerBinding(kGroup, "decreaseBoxDepth", GuardHistory(m_ProceduralObjectToolSystem.DecreaseBoxDepth)));

            AddBinding(new TriggerBinding(kGroup, "increasePlaneWidth", GuardHistory(m_ProceduralObjectToolSystem.IncreasePlaneWidth)));

            AddBinding(new TriggerBinding(kGroup, "decreasePlaneWidth", GuardHistory(m_ProceduralObjectToolSystem.DecreasePlaneWidth)));

            AddBinding(new TriggerBinding(kGroup, "increasePlaneDepth", GuardHistory(m_ProceduralObjectToolSystem.IncreasePlaneDepth)));

            AddBinding(new TriggerBinding(kGroup, "decreasePlaneDepth", GuardHistory(m_ProceduralObjectToolSystem.DecreasePlaneDepth)));

            AddBinding(new TriggerBinding(kGroup, "increaseWedgeWidth", GuardHistory(m_ProceduralObjectToolSystem.IncreaseWedgeWidth)));

            AddBinding(new TriggerBinding(kGroup, "decreaseWedgeWidth", GuardHistory(m_ProceduralObjectToolSystem.DecreaseWedgeWidth)));

            AddBinding(new TriggerBinding(kGroup, "increaseWedgeHeight", GuardHistory(m_ProceduralObjectToolSystem.IncreaseWedgeHeight)));

            AddBinding(new TriggerBinding(kGroup, "decreaseWedgeHeight", GuardHistory(m_ProceduralObjectToolSystem.DecreaseWedgeHeight)));

            AddBinding(new TriggerBinding(kGroup, "increaseWedgeDepth", GuardHistory(m_ProceduralObjectToolSystem.IncreaseWedgeDepth)));

            AddBinding(new TriggerBinding(kGroup, "decreaseWedgeDepth", GuardHistory(m_ProceduralObjectToolSystem.DecreaseWedgeDepth)));

            AddBinding(new TriggerBinding(kGroup, "increaseCylinderRadius", GuardHistory(m_ProceduralObjectToolSystem.IncreaseCylinderRadius)));

            AddBinding(new TriggerBinding(kGroup, "decreaseCylinderRadius", GuardHistory(m_ProceduralObjectToolSystem.DecreaseCylinderRadius)));

            AddBinding(new TriggerBinding(kGroup, "increaseCylinderHeight", GuardHistory(m_ProceduralObjectToolSystem.IncreaseCylinderHeight)));

            AddBinding(new TriggerBinding(kGroup, "decreaseCylinderHeight", GuardHistory(m_ProceduralObjectToolSystem.DecreaseCylinderHeight)));

            AddBinding(new TriggerBinding(kGroup, "increaseCylinderSegments", GuardHistory(m_ProceduralObjectToolSystem.IncreaseCylinderSegments)));

            AddBinding(new TriggerBinding(kGroup, "decreaseCylinderSegments", GuardHistory(m_ProceduralObjectToolSystem.DecreaseCylinderSegments)));

            AddBinding(new TriggerBinding(kGroup, "increaseConeBottomDiameter", GuardHistory(m_ProceduralObjectToolSystem.IncreaseConeBottomDiameter)));

            AddBinding(new TriggerBinding(kGroup, "decreaseConeBottomDiameter", GuardHistory(m_ProceduralObjectToolSystem.DecreaseConeBottomDiameter)));

            AddBinding(new TriggerBinding(kGroup, "increaseConeTopDiameter", GuardHistory(m_ProceduralObjectToolSystem.IncreaseConeTopDiameter)));

            AddBinding(new TriggerBinding(kGroup, "decreaseConeTopDiameter", GuardHistory(m_ProceduralObjectToolSystem.DecreaseConeTopDiameter)));

            AddBinding(new TriggerBinding(kGroup, "increaseConeHeight", GuardHistory(m_ProceduralObjectToolSystem.IncreaseConeHeight)));

            AddBinding(new TriggerBinding(kGroup, "decreaseConeHeight", GuardHistory(m_ProceduralObjectToolSystem.DecreaseConeHeight)));

            AddBinding(new TriggerBinding(kGroup, "increaseConeSegments", GuardHistory(m_ProceduralObjectToolSystem.IncreaseConeSegments)));

            AddBinding(new TriggerBinding(kGroup, "decreaseConeSegments", GuardHistory(m_ProceduralObjectToolSystem.DecreaseConeSegments)));

            AddBinding(new TriggerBinding(kGroup, "increaseArchWidth", GuardHistory(m_ProceduralObjectToolSystem.IncreaseArchWidth)));

            AddBinding(new TriggerBinding(kGroup, "decreaseArchWidth", GuardHistory(m_ProceduralObjectToolSystem.DecreaseArchWidth)));

            AddBinding(new TriggerBinding(kGroup, "increaseArchHeight", GuardHistory(m_ProceduralObjectToolSystem.IncreaseArchHeight)));

            AddBinding(new TriggerBinding(kGroup, "decreaseArchHeight", GuardHistory(m_ProceduralObjectToolSystem.DecreaseArchHeight)));

            AddBinding(new TriggerBinding(kGroup, "increaseArchDepth", GuardHistory(m_ProceduralObjectToolSystem.IncreaseArchDepth)));

            AddBinding(new TriggerBinding(kGroup, "decreaseArchDepth", GuardHistory(m_ProceduralObjectToolSystem.DecreaseArchDepth)));

            AddBinding(new TriggerBinding(kGroup, "increaseArchOpeningWidth", GuardHistory(m_ProceduralObjectToolSystem.IncreaseArchOpeningWidth)));

            AddBinding(new TriggerBinding(kGroup, "decreaseArchOpeningWidth", GuardHistory(m_ProceduralObjectToolSystem.DecreaseArchOpeningWidth)));

            AddBinding(new TriggerBinding(kGroup, "increaseArchOpeningHeight", GuardHistory(m_ProceduralObjectToolSystem.IncreaseArchOpeningHeight)));

            AddBinding(new TriggerBinding(kGroup, "decreaseArchOpeningHeight", GuardHistory(m_ProceduralObjectToolSystem.DecreaseArchOpeningHeight)));

            AddBinding(new TriggerBinding(kGroup, "increaseArchCurveSegments", GuardHistory(m_ProceduralObjectToolSystem.IncreaseArchCurveSegments)));

            AddBinding(new TriggerBinding(kGroup, "decreaseArchCurveSegments", GuardHistory(m_ProceduralObjectToolSystem.DecreaseArchCurveSegments)));

            AddBinding(new TriggerBinding(kGroup, "increaseStairsWidth", GuardHistory(m_ProceduralObjectToolSystem.IncreaseStairsWidth)));

            AddBinding(new TriggerBinding(kGroup, "decreaseStairsWidth", GuardHistory(m_ProceduralObjectToolSystem.DecreaseStairsWidth)));

            AddBinding(new TriggerBinding(kGroup, "increaseStairsTreadDepth", GuardHistory(m_ProceduralObjectToolSystem.IncreaseStairsTreadDepth)));

            AddBinding(new TriggerBinding(kGroup, "decreaseStairsTreadDepth", GuardHistory(m_ProceduralObjectToolSystem.DecreaseStairsTreadDepth)));

            AddBinding(new TriggerBinding(kGroup, "increaseStairsSteps", GuardHistory(m_ProceduralObjectToolSystem.IncreaseStairsSteps)));

            AddBinding(new TriggerBinding(kGroup, "decreaseStairsSteps", GuardHistory(m_ProceduralObjectToolSystem.DecreaseStairsSteps)));

            AddBinding(new TriggerBinding<float>(kGroup, "setBoxWidth", GuardHistory<float>(m_ProceduralObjectToolSystem.SetBoxCreationWidth)));

            AddBinding(new TriggerBinding<float>(kGroup, "setBoxHeight", GuardHistory<float>(m_ProceduralObjectToolSystem.SetBoxCreationHeight)));

            AddBinding(new TriggerBinding<float>(kGroup, "setBoxDepth", GuardHistory<float>(m_ProceduralObjectToolSystem.SetBoxCreationDepth)));

            AddBinding(new TriggerBinding<float>(kGroup, "setPlaneWidth", GuardHistory<float>(m_ProceduralObjectToolSystem.SetPlaneCreationWidth)));

            AddBinding(new TriggerBinding<float>(kGroup, "setPlaneDepth", GuardHistory<float>(m_ProceduralObjectToolSystem.SetPlaneCreationDepth)));

            AddBinding(new TriggerBinding<float>(kGroup, "setWedgeWidth", GuardHistory<float>(m_ProceduralObjectToolSystem.SetWedgeCreationWidth)));

            AddBinding(new TriggerBinding<float>(kGroup, "setWedgeHeight", GuardHistory<float>(m_ProceduralObjectToolSystem.SetWedgeCreationHeight)));

            AddBinding(new TriggerBinding<float>(kGroup, "setWedgeDepth", GuardHistory<float>(m_ProceduralObjectToolSystem.SetWedgeCreationDepth)));

            AddBinding(new TriggerBinding<float>(kGroup, "setCylinderRadius", GuardHistory<float>(m_ProceduralObjectToolSystem.SetCylinderCreationRadius)));

            AddBinding(new TriggerBinding<float>(kGroup, "setCylinderHeight", GuardHistory<float>(m_ProceduralObjectToolSystem.SetCylinderCreationHeight)));

            AddBinding(new TriggerBinding<int>(kGroup, "setCylinderSegments", GuardHistory<int>(m_ProceduralObjectToolSystem.SetCylinderCreationSegments)));

            AddBinding(new TriggerBinding<float>(kGroup, "setConeBottomDiameter", GuardHistory<float>(m_ProceduralObjectToolSystem.SetConeCreationBottomDiameter)));

            AddBinding(new TriggerBinding<float>(kGroup, "setConeTopDiameter", GuardHistory<float>(m_ProceduralObjectToolSystem.SetConeCreationTopDiameter)));

            AddBinding(new TriggerBinding<float>(kGroup, "setConeHeight", GuardHistory<float>(m_ProceduralObjectToolSystem.SetConeCreationHeight)));

            AddBinding(new TriggerBinding<int>(kGroup, "setConeSegments", GuardHistory<int>(m_ProceduralObjectToolSystem.SetConeCreationSegments)));

            AddBinding(new TriggerBinding<float>(kGroup, "setArchWidth", GuardHistory<float>(m_ProceduralObjectToolSystem.SetArchCreationWidth)));

            AddBinding(new TriggerBinding<float>(kGroup, "setArchHeight", GuardHistory<float>(m_ProceduralObjectToolSystem.SetArchCreationHeight)));

            AddBinding(new TriggerBinding<float>(kGroup, "setArchDepth", GuardHistory<float>(m_ProceduralObjectToolSystem.SetArchCreationDepth)));

            AddBinding(new TriggerBinding<float>(kGroup, "setArchOpeningWidth", GuardHistory<float>(m_ProceduralObjectToolSystem.SetArchCreationOpeningWidth)));

            AddBinding(new TriggerBinding<float>(kGroup, "setArchOpeningHeight", GuardHistory<float>(m_ProceduralObjectToolSystem.SetArchCreationOpeningHeight)));

            AddBinding(new TriggerBinding<int>(kGroup, "setArchCurveSegments", GuardHistory<int>(m_ProceduralObjectToolSystem.SetArchCreationCurveSegments)));

            AddBinding(new TriggerBinding<float>(kGroup, "setStairsWidth", GuardHistory<float>(m_ProceduralObjectToolSystem.SetStairsCreationWidth)));

            AddBinding(new TriggerBinding<float>(kGroup, "setStairsTreadDepth", GuardHistory<float>(m_ProceduralObjectToolSystem.SetStairsCreationTreadDepth)));

            AddBinding(new TriggerBinding<int>(kGroup, "setStairsSteps", GuardHistory<int>(m_ProceduralObjectToolSystem.SetStairsCreationSteps)));
        }
    }
}