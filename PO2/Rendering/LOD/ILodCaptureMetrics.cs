namespace PO2.Rendering.Lod
{
    internal interface ILodCaptureMetrics
    {
        double AllocationMilliseconds { get; }
        double MaximumAllocationMilliseconds { get; }
    }
}