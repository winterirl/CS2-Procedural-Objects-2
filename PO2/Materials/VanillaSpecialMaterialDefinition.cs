using AssetHash128 = Colossal.Hash128;

namespace PO2.Materials
{
    // Saved vanilla shader family. Template hashes and SurfaceAsset GUIDs
    // are resolved by the renderer, not stored as PO dependencies.
    public sealed class VanillaSpecialMaterialDefinition
    {
        public VanillaSpecialMaterialKind Kind { get; }
        public string DisplayName { get; }
        public string ShaderName { get; }
        public int MaterialTemplateHash { get; }
        public TextureMappingMode DefaultMappingMode { get; }
        public bool RequiresIsolatedRenderer { get; }
        public AssetHash128 CanonicalSurfaceGuid { get; }

        public VanillaSpecialMaterialDefinition(VanillaSpecialMaterialKind kind, string displayName, string shaderName, int materialTemplateHash, TextureMappingMode defaultMappingMode, bool requiresIsolatedRenderer, AssetHash128 canonicalSurfaceGuid)
        {
            Kind = kind;
            DisplayName = displayName;
            ShaderName = shaderName;
            MaterialTemplateHash = materialTemplateHash;
            DefaultMappingMode = defaultMappingMode;
            RequiresIsolatedRenderer = requiresIsolatedRenderer;
            CanonicalSurfaceGuid = canonicalSurfaceGuid;
        }
    }
}