using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PO2.Materials
{
    // Central catalog for bundled and user-authored generic materials.
    // Runtime material data stores IDs and resolves the heavier definition through here.
    public static class MaterialLibrary
    {
        public const string kPrototypeNoisyGray = "prototype-noisy-gray";

        public static string DefaultMaterialId => kPrototypeNoisyGray;

        public const string kUserMaterialIdPrefix = "po2-user/";

        private const int kCurrentManifestSchemaVersion = 1;
        private const string kMaterialLibraryFolderName = "MaterialLibrary";
        private const string kMaterialManifestFileName = "material.json";

        private static readonly object s_LoadLock = new object();

        // The prototype lives in code so there is always a valid fallback even
        // when the on-disk catalog is unavailable or contains bad manifests.
        private static readonly MaterialDefinition s_Prototype = new MaterialDefinition(kPrototypeNoisyGray, "Prototype Material", "Diagnostic", MaterialShaderKind.PrototypeUnlit, MaterialLibrarySource.Bundled, null, null, null, null, true, Vector2.one, TextureMappingMode.Repeat, 0f, 0f, 0f, MaterialNormalMode.None);

        private static MaterialDefinition[] s_Catalog = Array.Empty<MaterialDefinition>();

        private static Dictionary<string, MaterialDefinition> s_Definitions = BuildPrototypeDefinitionMap();

        private static Dictionary<string, string> s_UserManifestPaths = new Dictionary<string, string>(StringComparer.Ordinal);

        private static string[] s_Categories = Array.Empty<string>();
        private static bool s_Loaded;

        public static int CatalogCount
        {
            get
            {
                EnsureLoaded();
                return s_Catalog.Length;
            }
        }

        public static IReadOnlyList<string> Categories
        {
            get
            {
                EnsureLoaded();
                return s_Categories;
            }
        }

        public static bool TryGet(string id, out MaterialDefinition definition)
        {
            EnsureLoaded();

            if (string.IsNullOrWhiteSpace(id))
            {
                definition = null;
                return false;
            }

            return s_Definitions.TryGetValue(id, out definition);
        }


        internal static bool TryGetUserManifestPath(string id, out string manifestPath)
        {
            EnsureLoaded();

            if (string.IsNullOrWhiteSpace(id))
            {
                manifestPath = null;
                return false;
            }

            return s_UserManifestPaths.TryGetValue(id, out manifestPath);
        }

        public static MaterialDefinition GetCatalogMaterial(int index)
        {
            EnsureLoaded();

            if (s_Catalog.Length == 0)
            {
                return null;
            }

            // Catalog browsing is cyclic in the UI, including when moving back from index zero.
            // Update: browsing ui is now also a scrollable list
            int wrapped = index % s_Catalog.Length;
            if (wrapped < 0)
            {
                wrapped += s_Catalog.Length;
            }

            return s_Catalog[wrapped];
        }

        public static int IndexOfCatalogMaterial(string id)
        {
            EnsureLoaded();

            if (string.IsNullOrWhiteSpace(id))
            {
                return -1;
            }

            for (int i = 0; i < s_Catalog.Length; i++)
            {
                if (string.Equals(s_Catalog[i].Id, id, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        public static void Reload()
        {
            lock (s_LoadLock)
            {
                // Clear the loaded flag first so a failed/deferred reload cannot
                // leave callers believing the previous catalog is current.
                s_Loaded = false;

                if (!CanLoadCatalog())
                {
                    ResetCatalog();
                    return;
                }

                LoadCatalog(Mod.ModRootPath, Mod.UserMaterialRootPath);
                s_Loaded = true;
            }
        }

        private static void EnsureLoaded()
        {
            if (s_Loaded)
            {
                return;
            }

            lock (s_LoadLock)
            {
                // Another caller may have completed the load while this one was waiting for the lock.
                if (s_Loaded)
                {
                    return;
                }

                if (!CanLoadCatalog())
                {
                    return;
                }

                LoadCatalog(Mod.ModRootPath, Mod.UserMaterialRootPath);
                s_Loaded = true;
            }
        }

        private static bool CanLoadCatalog()
        {
            if (string.IsNullOrWhiteSpace(Mod.ModRootPath))
            {
                Mod.log.Warn("[PO Material Library] Mod root path is not available yet; " + "material catalog loading is deferred.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(Mod.UserMaterialRootPath))
            {
                Mod.log.Warn("[PO Material Library] User material root path is not available yet; " + "material catalog loading is deferred.");
                return false;
            }

            return true;
        }

        private static void LoadCatalog(string modRootPath, string userMaterialRootPath)
        {
            string bundledLibraryRoot = Path.Combine(modRootPath, kMaterialLibraryFolderName);

            Dictionary<string, MaterialDefinition> definitions = BuildPrototypeDefinitionMap();
            List<LoadedMaterialDefinition> loaded = new List<LoadedMaterialDefinition>();

            int bundledCount = LoadLibraryRoot(bundledLibraryRoot, MaterialLibrarySource.Bundled, definitions, loaded, required: true);

            // Bundled definitions are intentionally loaded first.
            // Reject duplicate user IDs later so they cannot replace shipped materials.
            int userCount = 0;
            if (EnsureUserLibraryRoot(userMaterialRootPath))
            {
                userCount = LoadLibraryRoot(userMaterialRootPath, MaterialLibrarySource.User, definitions, loaded, required: false);
            }

            loaded.Sort(CompareLoadedMaterials);

            MaterialDefinition[] catalog = new MaterialDefinition[loaded.Count];
            for (int i = 0; i < loaded.Count; i++)
            {
                catalog[i] = loaded[i].Definition;
            }

            Dictionary<string, string> userManifestPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            // Only user manifests are exposed to edit/delete services.
            // Bundled files are read-only even if their layout happens to be identical.
            for (int i = 0; i < loaded.Count; i++)
            {
                LoadedMaterialDefinition material = loaded[i];
                if (material.Definition.LibrarySource == MaterialLibrarySource.User)
                {
                    userManifestPaths[material.Definition.Id] = material.ManifestPath;
                }
            }

            s_Catalog = catalog;
            s_Definitions = definitions;
            s_UserManifestPaths = userManifestPaths;
            s_Categories = BuildCategoryList(catalog);

            Mod.log.Info($"[PO Material Library] Loaded {bundledCount} bundled and {userCount} user " + $"generic material definitions across {s_Categories.Length} categories.");
        }

        private static bool EnsureUserLibraryRoot(string userMaterialRootPath)
        {
            try
            {
                Directory.CreateDirectory(userMaterialRootPath);
                return true;
            }
            catch (Exception exception)
            {
                Mod.log.Error($"[PO Material Library] User material library is unavailable at " + $"'{userMaterialRootPath}': {exception.Message}. Bundled materials " + "will remain available.");
                return false;
            }
        }

        private static int LoadLibraryRoot(string libraryRoot, MaterialLibrarySource source, Dictionary<string, MaterialDefinition> definitions, List<LoadedMaterialDefinition> loaded, bool required)
        {
            if (!Directory.Exists(libraryRoot))
            {
                if (required)
                {
                    Mod.log.Error($"[PO Material Library] Bundled material library folder was not found: " + $"'{libraryRoot}'.");
                }

                return 0;
            }

            string[] manifestPaths;
            try
            {
                manifestPaths = Directory.GetFiles(libraryRoot, kMaterialManifestFileName, SearchOption.AllDirectories);
            }
            catch (Exception exception)
            {
                Mod.log.Error($"[PO Material Library] Could not enumerate {source.ToString().ToLowerInvariant()} " + $"material library '{libraryRoot}': {exception.Message}");
                return 0;
            }

            Array.Sort(manifestPaths, StringComparer.OrdinalIgnoreCase);

            // Stable enumeration matters when two bad manifests claim the same id
            // the same earlier definition wins on every load.
            int acceptedCount = 0;

            for (int i = 0; i < manifestPaths.Length; i++)
            {
                string manifestPath = manifestPaths[i];

                try
                {
                    // A broken material is isolated to its own manifest.
                    // The rest of the library remains usable and reports its own errors.
                    LoadedMaterialDefinition material = LoadManifest(manifestPath, source);

                    if (definitions.ContainsKey(material.Definition.Id))
                    {
                        Mod.log.Error($"[PO Material Library] Duplicate material id " + $"'{material.Definition.Id}' in '{manifestPath}'. " + "Bundled definitions and earlier valid definitions take precedence; " + "the duplicate was skipped.");
                        continue;
                    }

                    definitions.Add(material.Definition.Id, material.Definition);
                    loaded.Add(material);
                    acceptedCount++;
                }
                catch (Exception exception)
                {
                    Mod.log.Error($"[PO Material Library] Failed to load {source.ToString().ToLowerInvariant()} " + $"manifest '{manifestPath}': {exception.Message}");
                }
            }

            return acceptedCount;
        }

        private static LoadedMaterialDefinition LoadManifest(string manifestPath, MaterialLibrarySource source)
        {
            string json = File.ReadAllText(manifestPath);
            MaterialManifest manifest = Colossal.Json.JSON.MakeInto<MaterialManifest>(Colossal.Json.JSON.Load(json));

            if (manifest == null)
            {
                throw new InvalidDataException("Manifest JSON could not be parsed.");
            }

            if (manifest.schemaVersion != kCurrentManifestSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported schemaVersion {manifest.schemaVersion}; " + $"expected {kCurrentManifestSchemaVersion}.");
            }

            string id = RequireText(manifest.id, "id");
            string displayName = RequireText(manifest.displayName, "displayName");
            string category = RequireText(manifest.category, "category");

            if (source == MaterialLibrarySource.User && !id.StartsWith(kUserMaterialIdPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"User material id '{id}' must begin with '{kUserMaterialIdPrefix}'.");
            }

            if (manifest.textures == null)
            {
                throw new InvalidDataException("Missing textures object.");
            }

            string baseColorPath = ResolveManifestTexturePath(manifestPath, RequireText(manifest.textures.baseColor, "textures.baseColor"));
            string normalPath = ResolveOptionalManifestTexturePath(manifestPath, manifest.textures.normal);

            bool hasPhysicalTileSize = manifest.physicalSize != null;
            // Old/minimal manifests may omit physical size.
            // The definition keeps that distinction instead of pretending the fallback is set by the user.
            Vector2 tileSizeMeters = Vector2.one;
            if (hasPhysicalTileSize)
            {
                if (manifest.physicalSize.widthMeters <= 0f || manifest.physicalSize.heightMeters <= 0f)
                {
                    throw new InvalidDataException("physicalSize widthMeters and heightMeters must be greater than zero.");
                }

                tileSizeMeters = new Vector2(manifest.physicalSize.widthMeters, manifest.physicalSize.heightMeters);
            }

            MaterialManifestDefaults defaults = manifest.defaults;
            TextureMappingMode mappingMode = TextureMappingMode.Repeat;
            float metallic = 0f;
            float smoothness = 0f;
            float coatStrength = 0f;
            MaterialNormalMode normalMode = string.IsNullOrWhiteSpace(normalPath) ? MaterialNormalMode.None : MaterialNormalMode.TangentSpace;

            if (defaults != null)
            {
                mappingMode = ParseMappingMode(defaults.mappingMode);
                metallic = defaults.metallic;
                smoothness = defaults.smoothness;
                coatStrength = defaults.coat;
                normalMode = ParseNormalMode(defaults.normalMode, normalPath);
            }

            MaterialDefinition definition = new MaterialDefinition(id, displayName, category, MaterialShaderKind.DefaultObject, source, baseColorPath, normalPath, null, null, hasPhysicalTileSize, tileSizeMeters, mappingMode, metallic, smoothness, coatStrength, normalMode);

            return new LoadedMaterialDefinition(definition, manifest.catalogOrder, manifestPath);
        }

        private static TextureMappingMode ParseMappingMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TextureMappingMode.Repeat;
            }

            if (Enum.TryParse(value.Trim(), true, out TextureMappingMode mappingMode))
            {
                return mappingMode;
            }

            throw new InvalidDataException($"Unknown defaults.mappingMode '{value}'.");
        }

        private static MaterialNormalMode ParseNormalMode(string value, string normalPath)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.IsNullOrWhiteSpace(normalPath) ? MaterialNormalMode.None : MaterialNormalMode.TangentSpace;
            }

            if (Enum.TryParse(value.Trim(), true, out MaterialNormalMode normalMode))
            {
                if (normalMode != MaterialNormalMode.None && string.IsNullOrWhiteSpace(normalPath))
                {
                    throw new InvalidDataException("defaults.normalMode requires textures.normal.");
                }

                return normalMode;
            }

            throw new InvalidDataException($"Unknown defaults.normalMode '{value}'.");
        }

        private static string ResolveManifestTexturePath(string manifestPath, string manifestRelativePath)
        {
            string manifestDirectory = Path.GetDirectoryName(manifestPath);
            string fullManifestDirectory = Path.GetFullPath(manifestDirectory);
            string fullPath = Path.GetFullPath(Path.Combine(fullManifestDirectory, manifestRelativePath.Replace('/', Path.DirectorySeparatorChar)));

            // Material folders are self-contained.
            // Besides being safer, this is what makes copying or deleting one folder predictable.
            if (!IsPathInsideRoot(fullPath, fullManifestDirectory))
            {
                throw new InvalidDataException($"Texture path '{manifestRelativePath}' escapes its material directory.");
            }

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Declared texture '{manifestRelativePath}' was not found.", fullPath);
            }

            return fullPath;
        }

        private static string ResolveOptionalManifestTexturePath(string manifestPath, string manifestRelativePath)
        {
            if (string.IsNullOrWhiteSpace(manifestRelativePath))
            {
                return null;
            }

            return ResolveManifestTexturePath(manifestPath, manifestRelativePath.Trim());
        }

        private static bool IsPathInsideRoot(string fullPath, string fullRoot)
        {
            string normalizedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string RequireText(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"Required field '{fieldName}' is missing or blank.");
            }

            return value.Trim();
        }

        private static int CompareLoadedMaterials(LoadedMaterialDefinition left, LoadedMaterialDefinition right)
        {
            // Source is first so bundled materials stay together ahead of user imports. 
            // Manifest order and name handle presentation within each.
            int sourceComparison = left.Definition.LibrarySource.CompareTo(right.Definition.LibrarySource);
            if (sourceComparison != 0)
            {
                return sourceComparison;
            }

            int orderComparison = left.CatalogOrder.CompareTo(right.CatalogOrder);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            int nameComparison = string.Compare(left.Definition.DisplayName, right.Definition.DisplayName, StringComparison.OrdinalIgnoreCase);
            if (nameComparison != 0)
            {
                return nameComparison;
            }

            return string.Compare(left.ManifestPath, right.ManifestPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string[] BuildCategoryList(MaterialDefinition[] catalog)
        {
            List<string> categories = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < catalog.Length; i++)
            {
                string category = catalog[i].Category;
                if (string.IsNullOrWhiteSpace(category) || !seen.Add(category))
                {
                    continue;
                }

                categories.Add(category);
            }

            categories.Sort(StringComparer.OrdinalIgnoreCase);
            return categories.ToArray();
        }

        private static Dictionary<string, MaterialDefinition> BuildPrototypeDefinitionMap()
        {
            return new Dictionary<string, MaterialDefinition>(StringComparer.Ordinal)
            {
                { s_Prototype.Id, s_Prototype },
            };
        }

        private static void ResetCatalog()
        {
            s_Catalog = Array.Empty<MaterialDefinition>();
            s_Definitions = BuildPrototypeDefinitionMap();
            s_UserManifestPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            s_Categories = Array.Empty<string>();
        }

        private readonly struct LoadedMaterialDefinition
        {
            public MaterialDefinition Definition { get; }
            public int CatalogOrder { get; }
            public string ManifestPath { get; }

            public LoadedMaterialDefinition(MaterialDefinition definition, int catalogOrder, string manifestPath)
            {
                Definition = definition;
                CatalogOrder = catalogOrder;
                ManifestPath = manifestPath;
            }
        }
    }
}