using System;

namespace PO2.Materials
{
    // Records optional vertex streams and their intended use.
    // The renderer also uses this to avoid binding a native shader to incomplete data.
    [Flags]
    public enum VertexAttributeFlags
    {
        None = 0,
        UV0FourComponent = 1 << 0,
        VertexColor = 1 << 1,
        TexCoord3 = 1 << 2,
        TexCoord1 = 1 << 3,

        // UV0.xy is an authored unwrap; UV0.zw stores its surface coordinates in meters.
        // This is separate from native window UV0 data and is never inferred from it.
        SurfaceUVCoordinates = 1 << 4,
    }

    // Capability checks belong here so UI and rendering use the same rule when
    // deciding whether converted geometry can support a native material.
    public static class VertexAttributeCapabilities
    {
        private const VertexAttributeFlags kWindowPayload = VertexAttributeFlags.UV0FourComponent | VertexAttributeFlags.VertexColor | VertexAttributeFlags.TexCoord3;

        // Native windows need UV0.xyzw, packed color metadata, and TexCoord3 orientation.
        // Keep these streams on conversion; generic window assignment needs to author them first.
        public static bool HasWindowVertexPayload(VertexAttributeFlags attributes)
        {
            return (attributes & kWindowPayload) == kWindowPayload;
        }
    }
}