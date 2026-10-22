using System.IO;
using System.Text;
using Colossal.Json;

namespace PO2.Materials
{
    // One place for the on-disk manifest shape used by both initial import and later edits.
    // Keep schema/order defaults out of the UI services.
    internal static class UserMaterialWriter
    {
        public const int kCurrentSchemaVersion = 1;
        public const int kDefaultCatalogOrder = 1000;

        internal static MaterialManifest CreateManifest(string materialId, MaterialImportRequest request, string baseColorRelativePath, string normalRelativePath)
        {
            return new MaterialManifest
            {
                schemaVersion = kCurrentSchemaVersion,
                catalogOrder = kDefaultCatalogOrder,
                id = materialId,
                displayName = request.DisplayName,
                category = request.Category,
                textures = new MaterialManifestTextures
                {
                    baseColor = baseColorRelativePath,
                    normal = normalRelativePath
                },
                physicalSize = new MaterialManifestPhysicalSize
                {
                    widthMeters = request.PhysicalWidthMeters,
                    heightMeters = request.PhysicalHeightMeters
                },
                defaults = new MaterialManifestDefaults
                {
                    mappingMode = request.MappingMode.ToString(),
                    metallic = request.Metallic,
                    smoothness = request.Smoothness,
                    coat = request.CoatStrength,
                    normalMode = request.NormalMode.ToString()
                }
            };
        }

        internal static void WriteManifest(string manifestPath, MaterialManifest manifest)
        {
            string json = JSON.Dump(manifest, EncodeOptions.PreserveUnicode);

            // UTF-8 without a BOM keeps manifests conventional and portable while
            // PreserveUnicode keeps user-entered names readable.
            File.WriteAllText(manifestPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}