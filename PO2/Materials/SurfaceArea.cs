using UnityEngine;
using AssetHash128 = Colossal.Hash128;

namespace PO2.Materials
{
    // One material assignment across mesh triangles. Area zero always exists.
    public sealed partial class SurfaceArea
    {
        public int Id { get; }
        internal GeneratedMappingSeed MappingSeed { get; set; }
        public MaterialData Material { get; private set; }
        public ColorMaskData ColorMask { get; private set; }
        public SurfaceAreaOriginalState OriginalState { get; }
        public bool HasOriginalState => OriginalState != null;
        public VertexAttributeFlags SourceVertexAttributes { get; private set; }

        public bool HasWindowVertexPayload =>
            VertexAttributeCapabilities.HasWindowVertexPayload(SourceVertexAttributes);

        public NativeWindowKind NativeWindowKind { get; }

        // The renderer watches these independently.
        // A scalar edit should not make it rebuild generated UVs or resolve the material source again.
        public uint MaterialSourceVersion { get; private set; }
        public uint MaterialPropertiesVersion { get; private set; }
        public uint MappingVersion { get; private set; }
        public uint ColorMaskVersion { get; private set; }

        public SurfaceArea(int id, MaterialData material, VertexAttributeFlags sourceVertexAttributes = VertexAttributeFlags.None, ColorMaskData colorMask = null, SurfaceAreaOriginalState originalState = null, NativeWindowKind nativeWindowKind = NativeWindowKind.None)
        {
            Id = id;
            Material = material ?? throw new System.ArgumentNullException(nameof(material));
            SourceVertexAttributes = sourceVertexAttributes;
            // Surface Areas own their mutable appearance data.
            // Callers may reuse conversion snapshots without linking later edits between areas.
            ColorMask = (colorMask ?? new ColorMaskData()).Clone();
            OriginalState = originalState?.Clone();
            NativeWindowKind = nativeWindowKind;
        }

        public void SetSourceVertexAttributes(VertexAttributeFlags attributes)
        {
            SourceVertexAttributes = attributes;
        }

        public bool SetMappingMode(TextureMappingMode mode)
        {
            if (!Material.Mapping.SetMode(mode))
            {
                return false;
            }

            MappingVersion++;
            return true;
        }

        public bool SetTextureScale(Vector2 textureScale)
        {
            if (!Material.Mapping.SetTextureScale(textureScale))
            {
                return false;
            }

            MappingVersion++;
            return true;
        }

        public bool SetTextureRotation(float rotation)
        {
            if (!Material.Mapping.SetRotation(rotation))
            {
                return false;
            }

            MappingVersion++;
            return true;
        }

        public bool SetTextureOffset(Vector2 offset)
        {
            if (!Material.Mapping.SetOffset(offset))
            {
                return false;
            }

            MappingVersion++;
            return true;
        }

        public bool RefreshLibraryPhysicalSize(string definitionId)
        {
            if (string.IsNullOrWhiteSpace(definitionId) || Material.SourceKind != MaterialSourceKind.LibraryMaterial || !string.Equals(Material.DefinitionId, definitionId, System.StringComparison.Ordinal))
            {
                return false;
            }

            // Physical tile size lives in the shared definition instead of the Surface Area.
            // Bump the mapping version so already-rendered POs reproject immediately after a user material edit.
            MappingVersion++;
            return true;
        }

        public bool SetLibraryMaterial(string definitionId)
        {
            if (!Material.SetLibraryMaterial(definitionId))
            {
                return false;
            }

            // A new definition also installs new scalar defaults, so both
            // versions change even though this is one user action.
            MaterialSourceVersion++;
            MaterialPropertiesVersion++;
            return true;
        }

        public bool ReplaceLibraryMaterialReferenceWithDefault(string definitionId)
        {
            if (string.IsNullOrWhiteSpace(definitionId) || Material.SourceKind != MaterialSourceKind.LibraryMaterial || !string.Equals(Material.DefinitionId, definitionId, System.StringComparison.Ordinal))
            {
                return false;
            }

            ReplaceMaterialWithDefault();
            return true;
        }

        public bool ValidateLibraryReference()
        {
            // Missing user materials are recoverable save data.
            // Replace only the broken reference and leave SurfaceAsset/special sources untouched.
            if (Material.SourceKind != MaterialSourceKind.LibraryMaterial || MaterialLibrary.TryGet(Material.DefinitionId, out MaterialDefinition _))
            {
                return false;
            }

            ReplaceMaterialWithDefault();
            return true;
        }

        private void ReplaceMaterialWithDefault()
        {
            Material = MaterialData.CreateDefault();
            // Replacing the whole material also replaces its mapping instance.
            MaterialSourceVersion++;
            MaterialPropertiesVersion++;
            MappingVersion++;
        }

        public bool SetMetallic(float value)
        {
            if (!Material.SetMetallic(value))
            {
                return false;
            }

            MaterialPropertiesVersion++;
            return true;
        }

        public bool SetSmoothness(float value)
        {
            if (!Material.SetSmoothness(value))
            {
                return false;
            }

            MaterialPropertiesVersion++;
            return true;
        }

        public bool SetCoatStrength(float value)
        {
            if (!Material.SetCoatStrength(value))
            {
                return false;
            }

            MaterialPropertiesVersion++;
            return true;
        }

        public bool ResetMaterialPropertiesToLibraryDefaults()
        {
            if (!Material.ResetLibraryPropertiesToDefaults())
            {
                return false;
            }

            MaterialPropertiesVersion++;
            return true;
        }

        public bool SetSurfaceMaterial(AssetHash128 surfaceGuid)
        {
            if (!Material.SetSurfaceAsset(surfaceGuid))
            {
                return false;
            }

            MaterialSourceVersion++;
            return true;
        }

        public bool SetVanillaSpecialMaterial(VanillaSpecialMaterialKind kind)
        {
            if (!Material.SetVanillaSpecialMaterial(kind))
            {
                return false;
            }

            MaterialSourceVersion++;
            return true;
        }

        public bool SetColorMaskMode(ColorMaskMode mode)
        {
            if (!ColorMask.SetMode(mode))
            {
                return false;
            }

            ColorMaskVersion++;
            return true;
        }

        public bool SetColorMaskOverride(Color mask1, Color mask2, Color mask3)
        {
            if (!ColorMask.SetOverride(mask1, mask2, mask3))
            {
                return false;
            }

            ColorMaskVersion++;
            return true;
        }

        public bool RestoreOriginalAppearance()
        {
            if (OriginalState == null)
            {
                return false;
            }

            Material = OriginalState.CreateMaterialClone();
            ColorMask = OriginalState.CreateColorMaskClone();

            // Everything may have changed relative to the renderer's cached copy.
            MaterialSourceVersion++;
            MaterialPropertiesVersion++;
            MappingVersion++;
            ColorMaskVersion++;
            return true;
        }

        public SurfaceArea Clone()
        {
            // Version counters deliberately restart.
            // A cloned part gets a new enderer state rather than synchronizing against the source part.
            return new SurfaceArea(Id, Material.Clone(), SourceVertexAttributes, ColorMask.Clone(), OriginalState, NativeWindowKind) { MappingSeed = MappingSeed };
        }
    }
}