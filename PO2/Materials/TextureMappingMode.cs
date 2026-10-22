namespace PO2.Materials
{
    public enum TextureMappingMode
    {
        // Keep authored mesh UVs, including the extra native window channels.
        Original,

        // Generate tiling using the material's physical dimensions.
        Repeat,

        // Fit one texture copy to each generated surface chart.
        Stretch,
    }
}