namespace PO2.Materials
{
    // Source preserves the color baked into converted geometry.
    // Override uses the PO value instead
    // Disabled leaves the material unmasked.
    public enum ColorMaskMode
    {
        Disabled,
        Source,
        Override,
    }
}