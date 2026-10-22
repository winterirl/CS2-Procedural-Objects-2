using System;
using PO2.Core.Lod;

namespace PO2.Core
{
    public sealed partial class ProceduralObject
    {
        // Null follows the shared defaults. Only applied settings belong here.
        internal LodProfile LodProfile { get; private set; }
        internal uint LodSettingsVersion { get; private set; }
        internal event Action<ProceduralObject> LodSettingsChanged;

        internal bool SetLodProfile(LodProfile profile)
        {
            if (Equals(LodProfile, profile)) return false;
            LodProfile = profile;
            unchecked
            {
                LodSettingsVersion++;
            }

            LodSettingsChanged?.Invoke(this);
            return true;
        }
    }
}