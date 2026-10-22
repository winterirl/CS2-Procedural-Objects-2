using System;
using PO2.Core.Lod;

namespace PO2.Rendering.Lod
{
    internal sealed class LodGenerationRequest
    {
        internal LodGenerationRequest(LodSourceSnapshot source, LodProfile profile = null)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Profile = profile;
            LodProfile effective = LodProfile.Resolve(profile);
            Lod1 = effective.Lod1;
            Lod2 = effective.Lod2;
        }

        internal LodSourceSnapshot Source { get; }
        internal LodProfile Profile { get; }
        internal LodLevelSettings Lod1 { get; }
        internal LodLevelSettings Lod2 { get; }
        internal bool UsesInheritedDefaults => Profile == null;
    }
}