namespace PO2.Geometry.Transforms
{
    internal static class PivotSelectionMath
    {
        internal static bool UsesSelection(int selectedCount, int scopedCount)
        {
            return selectedCount > 0 && scopedCount > 0 && selectedCount < scopedCount;
        }
    }
}