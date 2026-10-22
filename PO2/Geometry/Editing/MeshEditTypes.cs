using System;

namespace PO2.Geometry.Editing
{
    // These are UI/tool modes, not properties of the mesh itself.
    // Nothing in the saved object should care which one happened to be active.
    public enum MeshSelectionMode
    {
        Vertex,
        Edge,
        SurfaceArea,
    }

    public enum MeshEditVisibilityMode
    {
        Solid,
        XRay,
    }

    public enum MeshEditVertexScope
    {
        CurrentPart,
        AllParts,
    }

    public enum VertexAlignmentAxis
    {
        None = -1,
        X = 0,
        Y = 1,
        Z = 2,
    }

    [Flags]
    public enum MeshEditCacheDirtyFlags
    {
        None = 0,
        Geometry = 1 << 0,
        Transform = 1 << 1,
        Camera = 1 << 2,
        Viewport = 1 << 3,
        Selection = 1 << 4,
        Hover = 1 << 5,

        // Projection and visual are the useful groups.
        // Selection changes do not require projecting every vertex again
        // That is the whole reason this is more than one dirty bool.
        Projection = Geometry | Transform | Camera | Viewport,
        Visual = Selection | Hover,
        All = Projection | Visual,
    }
}