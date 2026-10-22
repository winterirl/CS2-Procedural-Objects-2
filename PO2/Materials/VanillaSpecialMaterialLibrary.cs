using System.Collections.Generic;
using AssetHash128 = Colossal.Hash128;

namespace PO2.Materials
{
    public static class VanillaSpecialMaterialLibrary
    {
        // These GUIDs are renderer-resolution details only.
        // Procedural objects own the semantic VanillaSpecial kind rather
        // than a dependency on whichever built-in SurfaceAsset currently supplies that material family.
        // That would could do weird things.

        private static readonly VanillaSpecialMaterialDefinition[] s_Definitions =
        {
            new VanillaSpecialMaterialDefinition(VanillaSpecialMaterialKind.Glass, "Glass", "BH/GlsShader", 11, TextureMappingMode.Original, true, new AssetHash128("558f7c98e483ae79267aa15774f22bbd")),
            new VanillaSpecialMaterialDefinition(VanillaSpecialMaterialKind.Grass, "Grass", "BH/GraShader", 12, TextureMappingMode.Original, false, default),
            new VanillaSpecialMaterialDefinition(VanillaSpecialMaterialKind.Water, "Decorative Water", "BH/WatShader", 28, TextureMappingMode.Original, false, new AssetHash128("655d3b6931f3b733b804953263bccfc3")),
        }; // Both guids above are base-game assets, no DLC required

        private static readonly Dictionary<VanillaSpecialMaterialKind, VanillaSpecialMaterialDefinition> s_DefinitionMap = BuildDefinitionMap();

        public static int Count => s_Definitions.Length;

        public static bool TryGet(VanillaSpecialMaterialKind kind, out VanillaSpecialMaterialDefinition definition)
        {
            if (kind == VanillaSpecialMaterialKind.None)
            {
                definition = null;
                return false;
            }

            return s_DefinitionMap.TryGetValue(kind, out definition);
        }

        public static VanillaSpecialMaterialDefinition Get(int index)
        {
            if (index < 0 || index >= s_Definitions.Length)
            {
                return null;
            }

            return s_Definitions[index];
        }

        public static bool TryResolveSurfaceGuid(VanillaSpecialMaterialKind kind, out AssetHash128 surfaceGuid)
        {
            // Keeping this lookup here means saved POs depend on the stable enum,
            // not on a game asset guid that may change between game versions.
            if (!TryGet(kind, out VanillaSpecialMaterialDefinition definition) || !definition.CanonicalSurfaceGuid.isValid)
            {
                surfaceGuid = default;
                return false;
            }

            surfaceGuid = definition.CanonicalSurfaceGuid;
            return true;
        }

        private static Dictionary<VanillaSpecialMaterialKind, VanillaSpecialMaterialDefinition> BuildDefinitionMap()
        {
            Dictionary<VanillaSpecialMaterialKind, VanillaSpecialMaterialDefinition> definitions = new Dictionary<VanillaSpecialMaterialKind, VanillaSpecialMaterialDefinition>();

            for (int i = 0; i < s_Definitions.Length; i++)
            {
                VanillaSpecialMaterialDefinition definition = s_Definitions[i];

                definitions.Add(definition.Kind, definition);
            }

            return definitions;
        }
    }
}