using System;

namespace PO2.Core
{
    // Renderer change flags are not saved.
    // Topology also implies geometry because a new vertex/index layout needs new mesh data.
    [Flags]
    public enum ProceduralObjectDirtyFlags
    {
        None = 0,
        Transform = 1 << 0,
        Geometry = 1 << 1,
        Topology = 1 << 2,
        Material = 1 << 3,
        All = Transform | Geometry | Topology | Material,
    }
}