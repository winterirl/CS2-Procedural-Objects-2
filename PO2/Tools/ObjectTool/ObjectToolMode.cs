namespace PO2.Tools.ObjectTool
{
    // State machine owned by ProceduralObjectToolSystem. 
    // Selection, placement, conversion, object transforms, and mesh editing 
    // are mutually exclusive so one input event cant mutate two contexts.
    public enum ObjectToolMode
    {
        Select = 0,
        PlacePrimitive = 1,
        PlacePrimitivePart = 2,
        CombineSource = 3,
        ConvertAsset = 4,
        Transform = 5,
        MeshEdit = 6,
        Texturing = 7,
        Delete = 8,
        Copy = 9,
        Paste = 10,
        Lod = 11,
    }
}