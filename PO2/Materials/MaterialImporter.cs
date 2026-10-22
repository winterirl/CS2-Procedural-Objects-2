using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PO2.Localization;

namespace PO2.Materials
{
    // Stages the complete material folder before moving it into the live library.
    public static class MaterialImporter
    {
        private const int kMinimumTextureDimension = 512;
        private const int kMaximumTextureDimension = 4096;
        private const string kSourceFolderName = "Source";
        private const string kManifestFileName = "material.json";

        private static readonly string[] s_BaseColorSuffixes =
        {
            "basecolor",
            "basecolour",
            "base_color",
            "base_colour",
            "albedo",
            "diffuse",
            "color",
            "colour"
        };

        private static readonly string[] s_NormalSuffixes =
        {
            "normal",
            "normalmap",
            "normal_map"
        };

        public static IReadOnlyList<MaterialImportCandidate> ScanDirectory(string sourceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Validation.SourceFolderRequired")).AsException(localizedMessage => new ArgumentException(localizedMessage, nameof(sourceDirectory)));
            }

            string fullDirectory = Path.GetFullPath(sourceDirectory.Trim());
            if (!Directory.Exists(fullDirectory))
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Import.Error.FolderMissing", ("path", fullDirectory))).AsException(localizedMessage => new DirectoryNotFoundException(localizedMessage));
            }

            string[] files = Directory.GetFiles(fullDirectory, "*.png", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            Dictionary<string, CandidateParts> groups = new Dictionary<string, CandidateParts>(StringComparer.OrdinalIgnoreCase);

            // Pair files by the stem before their conventional texture suffix.
            // Ambiguous pairs are skipped instead of guessing which image wins.
            for (int i = 0; i < files.Length; i++)
            {
                string filePath = files[i];
                string fileName = Path.GetFileNameWithoutExtension(filePath);

                if (TryStripTextureSuffix(fileName, s_BaseColorSuffixes, out string baseStem))
                {
                    CandidateParts parts = GetOrCreateParts(groups, baseStem);
                    if (parts.BaseColorPath == null)
                    {
                        parts.BaseColorPath = filePath;
                    }
                    else
                    {
                        parts.HasAmbiguousBaseColor = true;
                    }

                    continue;
                }

                if (TryStripTextureSuffix(fileName, s_NormalSuffixes, out string normalStem))
                {
                    CandidateParts parts = GetOrCreateParts(groups, normalStem);
                    if (parts.NormalPath == null)
                    {
                        parts.NormalPath = filePath;
                    }
                    else
                    {
                        parts.HasAmbiguousNormal = true;
                    }
                }
            }

            List<MaterialImportCandidate> candidates = new List<MaterialImportCandidate>();

            foreach (KeyValuePair<string, CandidateParts> pair in groups)
            {
                CandidateParts parts = pair.Value;
                if (parts.BaseColorPath == null || parts.HasAmbiguousBaseColor || parts.HasAmbiguousNormal)
                {
                    continue;
                }

                candidates.Add(new MaterialImportCandidate(BuildSuggestedDisplayName(pair.Key), parts.BaseColorPath, parts.NormalPath));
            }

            candidates.Sort((left, right) => string.Compare(left.SuggestedDisplayName, right.SuggestedDisplayName, StringComparison.OrdinalIgnoreCase));

            return candidates;
        }

        public static MaterialImportResult Import(MaterialImportRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string stagingDirectory = null;
            string finalDirectory = null;
            bool committed = false;

            try
            {
                // Display names are for people and may be adjusted for uniqueness.
                // The GUID-backed ID below is the stable identity saved on POs.
                string requestedDisplayName = RequireText(request.DisplayName, LocalizedText.For("PO2.MaterialEditor.Validation.Field.Name"));
                string displayName = BuildUniqueDisplayName(requestedDisplayName);
                string category = RequireText(request.Category, LocalizedText.For("PO2.MaterialEditor.Validation.Field.Category"));
                ValidateImportCreationRequest(request);

                ImportedTextureInfo baseColor = ValidateTexture(request.BaseColorPath, LocalizedText.For("PO2.MaterialEditor.Validation.Field.BaseColor"));
                ImportedTextureInfo normal = null;
                if (!string.IsNullOrWhiteSpace(request.NormalPath))
                {
                    normal = ValidateTexture(request.NormalPath, LocalizedText.For("PO2.MaterialEditor.Validation.Field.Normal"));

                    if (string.Equals(baseColor.FullPath, normal.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw (LocalizedText.For("PO2.MaterialEditor.Validation.DistinctTextures")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
                    }
                }

                string userRoot = RequireUserMaterialRoot();
                Directory.CreateDirectory(userRoot);

                Guid materialGuid = Guid.NewGuid();
                string guidText = materialGuid.ToString("D").ToLowerInvariant();
                string materialId = MaterialLibrary.kUserMaterialIdPrefix + guidText;
                string readableAssetName = BuildPascalCaseAssetName(displayName);
                string folderAssetName = readableAssetName;
                string folderGuid = materialGuid.ToString("N").ToLowerInvariant();

                stagingDirectory = Path.Combine(userRoot, $".import-{materialGuid:N}");
                finalDirectory = Path.Combine(userRoot, $"{folderAssetName}_{folderGuid}");

                if (Directory.Exists(stagingDirectory) || Directory.Exists(finalDirectory))
                {
                    throw (LocalizedText.For("PO2.MaterialEditor.Import.Error.DestinationExists")).AsException(localizedMessage => new IOException(localizedMessage));
                }

                string sourceDirectory = Path.Combine(stagingDirectory, kSourceFolderName);
                Directory.CreateDirectory(sourceDirectory);

                // Keep private copies of imported textures. A PO material must
                // not break because the user later moves the original PNGs.
                string baseColorFileName = $"{readableAssetName}_BaseColor.png";
                string normalFileName = normal == null ? null : $"{readableAssetName}_Normal.png";

                File.Copy(baseColor.FullPath, Path.Combine(sourceDirectory, baseColorFileName), overwrite: false);

                if (normal != null)
                {
                    File.Copy(normal.FullPath, Path.Combine(sourceDirectory, normalFileName), overwrite: false);
                }

                MaterialImportRequest resolvedRequest = new MaterialImportRequest(displayName, category, request.BaseColorPath, request.NormalPath, request.PhysicalWidthMeters, request.PhysicalHeightMeters, request.MappingMode, request.Metallic, request.Smoothness, request.CoatStrength, request.NormalMode);

                MaterialManifest manifest = UserMaterialWriter.CreateManifest(materialId, resolvedRequest, $"{kSourceFolderName}/{baseColorFileName}", normalFileName == null ? null : $"{kSourceFolderName}/{normalFileName}");

                UserMaterialWriter.WriteManifest(Path.Combine(stagingDirectory, kManifestFileName), manifest);

                // Directory.Move is the commit point. The library can only ever
                // discover the old state or the complete new material folder.
                Directory.Move(stagingDirectory, finalDirectory);
                stagingDirectory = null;
                committed = true;

                MaterialLibrary.Reload();
                // Reload is also validation. A manifest that was written but
                // rejected by the catalogue is not a successful import.
                if (!MaterialLibrary.TryGet(materialId, out MaterialDefinition loadedDefinition) || loadedDefinition.LibrarySource != MaterialLibrarySource.User)
                {
                    throw (LocalizedText.For("PO2.MaterialEditor.Import.Error.LibraryRejected")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
                }

                Mod.log.Info($"[PO Material Import] Imported '{displayName}' as '{materialId}' " + $"to '{finalDirectory}'. BaseColor={baseColor.Width}x{baseColor.Height}, " + $"Normal={(normal == null ? "None" : $"{normal.Width}x{normal.Height}")}.");

                return MaterialImportResult.Succeeded(materialId, finalDirectory);
            }
            catch (Exception exception)
            {
                // Before commit, only staging needs cleanup.
                // After commit, remove the final folder too so a failed import is not found next boot.
                if (!string.IsNullOrWhiteSpace(stagingDirectory))
                {
                    TryDeleteDirectory(stagingDirectory);
                }

                if (committed && !string.IsNullOrWhiteSpace(finalDirectory))
                {
                    TryDeleteDirectory(finalDirectory);
                    MaterialLibrary.Reload();
                }

                Mod.log.Error($"[PO Material Import] Import failed: {exception.Message}");
                return MaterialImportResult.Failed(LocalizedText.FromException(exception));
            }
        }

        private static string BuildUniqueDisplayName(string requestedDisplayName)
        {
            if (!CatalogContainsDisplayName(requestedDisplayName))
            {
                return requestedDisplayName;
            }

            int suffix = 1;
            while (true)
            {
                string candidate = $"{requestedDisplayName} ({suffix})";
                if (!CatalogContainsDisplayName(candidate))
                {
                    return candidate;
                }

                suffix++;
            }
        }

        private static bool CatalogContainsDisplayName(string displayName)
        {
            int count = MaterialLibrary.CatalogCount;
            for (int i = 0; i < count; i++)
            {
                MaterialDefinition definition = MaterialLibrary.GetCatalogMaterial(i);

                if (definition != null && string.Equals(definition.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateImportCreationRequest(MaterialImportRequest request)
        {
            if (request.PhysicalWidthMeters < 0.1f || request.PhysicalWidthMeters > 1000f || float.IsNaN(request.PhysicalWidthMeters) || float.IsInfinity(request.PhysicalWidthMeters) || request.PhysicalHeightMeters < 0.1f || request.PhysicalHeightMeters > 1000f || float.IsNaN(request.PhysicalHeightMeters) || float.IsInfinity(request.PhysicalHeightMeters))
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Validation.PhysicalSize")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }

            if (request.MappingMode != TextureMappingMode.Repeat && request.MappingMode != TextureMappingMode.Stretch)
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Import.Validation.MappingMode")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }

            ValidateScalar(request.Metallic, LocalizedText.For("PO2.MaterialEditor.Validation.Field.Metallic"));
            ValidateScalar(request.Smoothness, LocalizedText.For("PO2.MaterialEditor.Validation.Field.Smoothness"));
            ValidateScalar(request.CoatStrength, LocalizedText.For("PO2.MaterialEditor.Validation.Field.Coat"));

            bool hasNormal = !string.IsNullOrWhiteSpace(request.NormalPath);
            // Normal mode is derived from whether a normal texture exists for
            // now. Keeping both fields consistent avoids an invalid manifest.
            MaterialNormalMode expectedNormalMode = hasNormal ? MaterialNormalMode.TangentSpace : MaterialNormalMode.None;

            if (request.NormalMode != expectedNormalMode)
            {
                throw (hasNormal ? LocalizedText.For("PO2.MaterialEditor.Import.Validation.NormalModeWithTexture") : LocalizedText.For("PO2.MaterialEditor.Import.Validation.NormalModeWithoutTexture")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }
        }

        private static void ValidateScalar(float value, LocalizedText label)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Validation.ScalarRange", ("label", label))).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }
        }

        private static ImportedTextureInfo ValidateTexture(string path, LocalizedText roleName)
        {
            string fullPath = RequirePngFile(path, roleName);
            byte[] bytes = File.ReadAllBytes(fullPath);

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);

            try
            {
                // LoadImage is used as the PNG decoder as well as the dimension check.
                // Extension alone is not enough to accept the file. Idiots renaming jpgs, etc...
                if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: false))
                {
                    throw (LocalizedText.For("PO2.MaterialEditor.Validation.TextureDecode", ("role", roleName))).AsException(localizedMessage => new InvalidDataException(localizedMessage));
                }

                if (texture.width < kMinimumTextureDimension || texture.height < kMinimumTextureDimension || texture.width > kMaximumTextureDimension || texture.height > kMaximumTextureDimension)
                {
                    throw (LocalizedText.For("PO2.MaterialEditor.Validation.TextureDimensions", ("role", roleName), ("width", texture.width), ("height", texture.height), ("minimum", kMinimumTextureDimension), ("maximum", kMaximumTextureDimension))).AsException(localizedMessage => new InvalidDataException(localizedMessage));
                }

                return new ImportedTextureInfo(fullPath, texture.width, texture.height);
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        private static string RequirePngFile(string path, LocalizedText roleName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Validation.TexturePathRequired", ("role", roleName))).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }

            string fullPath = Path.GetFullPath(path.Trim());
            if (!File.Exists(fullPath))
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Validation.TextureMissing", ("role", roleName))).AsException(localizedMessage => new FileNotFoundException(localizedMessage, fullPath));
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Validation.TextureFormat", ("role", roleName))).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }

            return fullPath;
        }

        private static string RequireUserMaterialRoot()
        {
            if (string.IsNullOrWhiteSpace(Mod.UserMaterialRootPath))
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Import.Error.StorageUnavailable")).AsException(localizedMessage => new InvalidOperationException(localizedMessage));
            }

            return Path.GetFullPath(Mod.UserMaterialRootPath);
        }

        private static CandidateParts GetOrCreateParts(Dictionary<string, CandidateParts> groups, string stem)
        {
            if (!groups.TryGetValue(stem, out CandidateParts parts))
            {
                parts = new CandidateParts();
                groups.Add(stem, parts);
            }

            return parts;
        }

        private static bool TryStripTextureSuffix(string fileName, string[] suffixes, out string stem)
        {
            // The separator is optional so common names like BrickBaseColor and
            // Brick_BaseColor both scan as the same candidate stem.
            for (int i = 0; i < suffixes.Length; i++)
            {
                string suffix = suffixes[i];
                if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int stemLength = fileName.Length - suffix.Length;
                if (stemLength <= 0)
                {
                    continue;
                }

                string candidate = fileName.Substring(0, stemLength).TrimEnd('_', '-', ' ', '.');
                if (candidate.Length == 0)
                {
                    continue;
                }

                stem = candidate;
                return true;
            }

            stem = null;
            return false;
        }

        private static string BuildSuggestedDisplayName(string stem)
        {
            if (string.IsNullOrWhiteSpace(stem))
            {
                return "Imported Material";
            }

            StringBuilder builder = new StringBuilder(stem.Length + 8);
            char previous = '\0';

            for (int i = 0; i < stem.Length; i++)
            {
                char current = stem[i];
                if (current == '_' || current == '-' || current == '.')
                {
                    AppendSingleSpace(builder);
                    previous = current;
                    continue;
                }

                if (i > 0 && char.IsUpper(current) && char.IsLower(previous))
                {
                    AppendSingleSpace(builder);
                }

                builder.Append(current);
                previous = current;
            }

            string displayName = builder.ToString().Trim();
            return displayName.Length == 0 ? "Imported Material" : displayName;
        }

        private static string BuildPascalCaseAssetName(string displayName)
        {
            StringBuilder builder = new StringBuilder(displayName.Length);
            bool uppercaseNext = true;

            for (int i = 0; i < displayName.Length; i++)
            {
                char current = displayName[i];
                if (!char.IsLetterOrDigit(current))
                {
                    uppercaseNext = true;
                    continue;
                }

                if (uppercaseNext && char.IsLetter(current))
                {
                    builder.Append(char.ToUpperInvariant(current));
                }
                else
                {
                    builder.Append(current);
                }

                uppercaseNext = false;
            }

            if (builder.Length == 0)
            {
                return "ImportedMaterial";
            }

            if (char.IsDigit(builder[0]))
            {
                builder.Insert(0, "Material");
            }

            return builder.ToString();
        }

        private static string RequireText(string value, LocalizedText label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Validation.RequiredField", ("label", label))).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }

            return value.Trim();
        }

        private static void AppendSingleSpace(StringBuilder builder)
        {
            if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
            {
                builder.Append(' ');
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception cleanupException)
            {
                Mod.log.Warn($"[PO Material Import] Could not clean up '{path}': " + cleanupException.Message);
            }
        }

        private sealed class CandidateParts
        {
            public string BaseColorPath;
            public string NormalPath;
            public bool HasAmbiguousBaseColor;
            public bool HasAmbiguousNormal;
        }

        private sealed class ImportedTextureInfo
        {
            public string FullPath { get; }
            public int Width { get; }
            public int Height { get; }

            public ImportedTextureInfo(string fullPath, int width, int height)
            {
                FullPath = fullPath;
                Width = width;
                Height = height;
            }
        }
    }
}