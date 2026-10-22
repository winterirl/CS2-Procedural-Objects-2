namespace PO2.Tools.ObjectTool
{
    // The innermost pointer-driven interaction currently owning input. 
    // React consumes this directly instead of reconstructing state from many booleans.
    public enum NestedInteraction
    {
        None = 0,
        GizmoDrag = 1,
        BoxSelection = 2,
        PrimitivePlacement = 3,
        PrimitivePartPlacement = 4,
        AssetConversionTarget = 5,
        CombineSourceTarget = 6,
        VertexAlignmentTarget = 7,
        VertexSlideTarget = 8,
        VertexSlideDrag = 9,
        LoopCutTarget = 10,
        LoopCutPosition = 11,
        SurfaceCutTarget = 12,
        SurfaceCutPosition = 13,
        OrientationTarget = 14,
        FaceOperationSelection = 15,
        PartArray = 16,
    }
}