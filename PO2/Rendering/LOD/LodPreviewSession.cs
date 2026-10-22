using PO2.Core.Lod;

namespace PO2.Rendering.Lod
{
    internal enum LodPreviewStatus
    {
        Idle,
        Waiting,
        Capturing,
        Generating,
        Uploading,
        Ready,
        OutOfDate,
        Cancelled,
        Failed,
    }

    internal enum LodPreviewDisplay
    {
        Auto,
        Original,
        Lod1,
        Lod2,
    }

    internal sealed class LodPreviewSession
    {
        internal LodPreviewSession(int objectId, ulong sessionId, LodProfile profile, ulong configuration)
        {
            ObjectId = objectId;
            SessionId = sessionId;
            DraftProfile = profile;
            ConfigurationRevision = configuration;
            Display = LodPreviewDisplay.Original;
        }

        internal int ObjectId { get; }
        internal ulong SessionId { get; }
        internal LodProfile DraftProfile { get; set; }
        internal ulong ConfigurationRevision { get; set; }
        internal LodGenerationKey RequestKey { get; set; }
        internal LodPreviewStatus Status { get; set; }
        internal LodPreviewDisplay Display { get; set; }
        internal string FailureReason { get; set; } = string.Empty;
        internal long CapturedBytes { get; set; }
        internal long CaptureBytes { get; set; }
        internal int UploadedMeshes { get; set; }
        internal int UploadMeshes { get; set; }
        internal bool HasCompletedGeneration { get; set; }
        internal LodObjectDiagnostics Diagnostics { get; set; }
        internal LodAutomaticChain AutomaticChain { get; set; }

        internal bool CanDisplay(LodPreviewDisplay display)
        {
            if (display == LodPreviewDisplay.Auto || display == LodPreviewDisplay.Original)
            {
                return true;
            }

            if ((display != LodPreviewDisplay.Lod1 && display != LodPreviewDisplay.Lod2) || Status != LodPreviewStatus.Ready || !HasCompletedGeneration)
            {
                return false;
            }

            LodProfile profile = LodProfile.Resolve(DraftProfile);
            bool enabled = display == LodPreviewDisplay.Lod1 ? profile.Lod1.Enabled : profile.Lod2.Enabled;
            return enabled && (DraftProfile != null || AutomaticChain.HasGeneratedLevels);
        }

        internal bool IsBusy => Status == LodPreviewStatus.Waiting || Status == LodPreviewStatus.Capturing || Status == LodPreviewStatus.Generating || Status == LodPreviewStatus.Uploading;
    }
}