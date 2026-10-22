using System;
using System.IO;
using Colossal.Json;
using PO2.Localization;

namespace PO2.Materials
{
    // Mutations for existing user materials. These use staging/backup files so a
    // failed reload can be rolled back without losing the last valid material.
    internal static class UserMaterialService
    {
        internal static void CleanupDeleteStaging()
        {
            try
            {
                string userMaterialRoot = GetUserMaterialRoot();
                string stagingRoot = GetDeleteStagingRoot(userMaterialRoot);
                if (!Directory.Exists(stagingRoot))
                {
                    return;
                }

                int removedCount = 0;
                // Anything left here came from a delete that had already removed
                // the material from the active library before shutdown/crash.
                string[] entries = Directory.GetFileSystemEntries(stagingRoot);
                for (int i = 0; i < entries.Length; i++)
                {
                    string entry = entries[i];
                    try
                    {
                        if (Directory.Exists(entry))
                        {
                            Directory.Delete(entry, true);
                            removedCount++;
                        }
                        else if (File.Exists(entry))
                        {
                            File.Delete(entry);
                            removedCount++;
                        }
                    }
                    catch (Exception cleanupException)
                    {
                        Mod.log.Warn($"[PO Material Library] Could not clean stale material delete staging entry '{Path.GetFileName(entry)}': {cleanupException.Message}");
                    }
                }

                TryDeleteEmptyDirectory(stagingRoot);
                if (removedCount > 0)
                {
                    Mod.log.Info($"[PO Material Library] Cleaned {removedCount} stale material delete staging entr{(removedCount == 1 ? "y" : "ies")}.");
                }
            }
            catch (Exception exception)
            {
                Mod.log.Warn($"[PO Material Library] Material delete staging cleanup failed: {exception.Message}");
            }
        }

        internal static UserMaterialDeleteResult DeleteDefinition(string materialId)
        {
            string manifestPath = null;
            string materialDirectory = null;
            string stagingRoot = null;
            string stagedDirectory = null;
            bool movedToStaging = false;

            try
            {
                if (!MaterialLibrary.TryGet(materialId, out MaterialDefinition definition) || definition == null || definition.LibrarySource != MaterialLibrarySource.User)
                {
                    throw (LocalizedText.For("PO2.Materials.Delete.Error.BundledMaterial")).AsException(localizedMessage => new InvalidOperationException(localizedMessage));
                }

                if (!MaterialLibrary.TryGetUserManifestPath(materialId, out manifestPath) || string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                {
                    throw (LocalizedText.For("PO2.Materials.Error.ManifestMissing")).AsException(localizedMessage => new FileNotFoundException(localizedMessage, manifestPath));
                }

                string userMaterialRoot = GetUserMaterialRoot();
                manifestPath = Path.GetFullPath(manifestPath);
                EnsurePathInsideRoot(manifestPath, userMaterialRoot, LocalizedText.For("PO2.MaterialEditor.Validation.Field.Manifest"));

                materialDirectory = Path.GetDirectoryName(manifestPath);
                if (string.IsNullOrWhiteSpace(materialDirectory))
                {
                    throw (LocalizedText.For("PO2.Materials.Error.DirectoryUnresolved")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
                }

                materialDirectory = Path.GetFullPath(materialDirectory);
                EnsureDirectoryInsideRoot(materialDirectory, userMaterialRoot);

                MaterialManifest manifest = LoadManifest(manifestPath);
                if (!string.Equals(manifest.id, materialId, StringComparison.Ordinal))
                {
                    throw (LocalizedText.For("PO2.Materials.Error.ManifestIdMismatch")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
                }

                string displayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? materialId : definition.DisplayName;

                stagingRoot = GetDeleteStagingRoot(userMaterialRoot);
                Directory.CreateDirectory(stagingRoot);

                string directoryName = Path.GetFileName(materialDirectory);
                string token = Guid.NewGuid().ToString("N");
                stagedDirectory = Path.Combine(stagingRoot, $"{directoryName}-{token}");

                if (Directory.Exists(stagedDirectory) || File.Exists(stagedDirectory))
                {
                    throw (LocalizedText.For("PO2.Materials.Delete.Error.StagingExists")).AsException(localizedMessage => new IOException(localizedMessage));
                }

                // Moving outside the scanned root removes it from the next reload
                // while keeping the entire folder available for rollback.
                Directory.Move(materialDirectory, stagedDirectory);
                movedToStaging = true;

                MaterialLibrary.Reload();
                if (MaterialLibrary.TryGet(materialId, out _))
                {
                    throw (LocalizedText.For("PO2.Materials.Delete.Error.StillLoaded")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
                }

                try
                {
                    // Once reload proves the ID is gone physical deletion is cleanup only.
                    // A failure here is handled on the next startup.
                    Directory.Delete(stagedDirectory, true);
                    movedToStaging = false;
                    stagedDirectory = null;
                    TryDeleteEmptyDirectory(stagingRoot);
                }
                catch (Exception cleanupException)
                {
                    Mod.log.Warn($"[PO Material Library] Deleted user material '{displayName}' ({materialId}), " + $"but its quarantined files could not be removed immediately: {cleanupException.Message}");
                }

                Mod.log.Info($"[PO Material Library] Deleted user material '{displayName}' ({materialId}).");

                return UserMaterialDeleteResult.Succeeded(displayName);
            }
            catch (Exception exception)
            {
                // Put the directory back before reloading so the in-memory catalog
                // and disk return to the same pre-delete state.
                if (movedToStaging && !string.IsNullOrWhiteSpace(stagedDirectory) && Directory.Exists(stagedDirectory) && !string.IsNullOrWhiteSpace(materialDirectory))
                {
                    try
                    {
                        if (Directory.Exists(materialDirectory))
                        {
                            throw (LocalizedText.For("PO2.Materials.Delete.Error.RollbackDestinationExists")).AsException(localizedMessage => new IOException(localizedMessage));
                        }

                        Directory.Move(stagedDirectory, materialDirectory);
                        movedToStaging = false;
                        stagedDirectory = null;
                    }
                    catch (Exception rollbackException)
                    {
                        Mod.log.Error($"[PO Material Library] User material delete rollback failed: {rollbackException.Message}");
                    }
                }

                LocalizedText failureMessage = LocalizedText.FromException(exception);
                try
                {
                    MaterialLibrary.Reload();
                }
                catch (Exception reloadException)
                {
                    failureMessage = LocalizedText.For("PO2.Materials.Delete.Error.RecoveryReload", ("reason", failureMessage), ("reloadReason", LocalizedText.FromException(reloadException)));
                    Mod.log.Error($"[PO Material Library] User material delete recovery reload failed: {reloadException.Message}");
                }

                TryDeleteEmptyDirectory(stagingRoot);

                Mod.log.Error($"[PO Material Library] User material delete failed: {failureMessage}");
                return UserMaterialDeleteResult.Failed(failureMessage);
            }
        }

        internal static UserMaterialUpdateResult UpdateDefinition(string materialId, MaterialAuthoringState authoringState)
        {
            if (authoringState == null)
            {
                throw new ArgumentNullException(nameof(authoringState));
            }

            string manifestPath = null;
            string temporaryPath = null;
            string backupPath = null;
            bool originalMoved = false;
            bool replacementMoved = false;

            try
            {
                if (!MaterialLibrary.TryGet(materialId, out MaterialDefinition definition) || definition == null || definition.LibrarySource != MaterialLibrarySource.User)
                {
                    throw (LocalizedText.For("PO2.MaterialEditor.Edit.Error.BundledMaterial")).AsException(localizedMessage => new InvalidOperationException(localizedMessage));
                }

                if (!MaterialLibrary.TryGetUserManifestPath(materialId, out manifestPath) || string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                {
                    throw (LocalizedText.For("PO2.Materials.Error.ManifestMissing")).AsException(localizedMessage => new FileNotFoundException(localizedMessage, manifestPath));
                }

                string displayName = RequireText(authoringState.DisplayName, LocalizedText.For("PO2.MaterialEditor.Validation.Field.Name"));
                string category = RequireText(authoringState.Category, LocalizedText.For("PO2.MaterialEditor.Validation.Field.Category"));
                ValidateAuthoringState(authoringState);

                displayName = BuildUniqueDisplayName(displayName, materialId);

                MaterialManifest manifest = LoadManifest(manifestPath);
                if (!string.Equals(manifest.id, materialId, StringComparison.Ordinal))
                {
                    throw (LocalizedText.For("PO2.Materials.Error.ManifestIdMismatch")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
                }

                manifest.displayName = displayName;
                manifest.category = category;
                if (manifest.physicalSize == null)
                {
                    manifest.physicalSize = new MaterialManifestPhysicalSize();
                }

                manifest.physicalSize.widthMeters = authoringState.PhysicalWidthMeters;
                manifest.physicalSize.heightMeters = authoringState.PhysicalHeightMeters;
                if (manifest.defaults == null)
                {
                    manifest.defaults = new MaterialManifestDefaults();
                }

                manifest.defaults.mappingMode = authoringState.MappingMode.ToString();
                manifest.defaults.metallic = authoringState.Metallic;
                manifest.defaults.smoothness = authoringState.Smoothness;
                manifest.defaults.coat = authoringState.CoatStrength;
                manifest.defaults.normalMode = definition.NormalMode.ToString();

                // Texture paths and the stable ID stay untouched.
                // Editing only replaces metadata in material.json.
                string directory = Path.GetDirectoryName(manifestPath);
                string token = Guid.NewGuid().ToString("N");
                temporaryPath = Path.Combine(directory, $".material-edit-{token}.json");
                backupPath = Path.Combine(directory, $".material-edit-{token}.bak");

                UserMaterialWriter.WriteManifest(temporaryPath, manifest);
                ValidateWrittenManifest(temporaryPath, materialId);

                // Keep the original manifest beside the replacement until the
                // new file has survived a full catalog reload.
                File.Move(manifestPath, backupPath);
                originalMoved = true;
                File.Move(temporaryPath, manifestPath);
                replacementMoved = true;
                temporaryPath = null;

                MaterialLibrary.Reload();
                if (!MaterialLibrary.TryGet(materialId, out MaterialDefinition updated) || updated == null || updated.LibrarySource != MaterialLibrarySource.User)
                {
                    throw (LocalizedText.For("PO2.MaterialEditor.Edit.Error.LibraryRejected")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
                }

                if (!string.Equals(updated.DisplayName, displayName, StringComparison.Ordinal) || !string.Equals(updated.Category, category, StringComparison.Ordinal))
                {
                    throw (LocalizedText.For("PO2.MaterialEditor.Edit.Error.ReloadMismatch")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
                }

                TryDeleteFile(backupPath);
                backupPath = null;

                Mod.log.Info($"[PO Material Library] Updated user material '{displayName}' ({materialId}).");

                return UserMaterialUpdateResult.Succeeded(displayName);
            }
            catch (Exception exception)
            {
                try
                {
                    // Remove the replacement before restoring the backup.
                    // File.Move will not overwrite an existing manifest on these runtimes.
                    if (replacementMoved && !string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
                    {
                        File.Delete(manifestPath);
                    }

                    if (originalMoved && !string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                    {
                        File.Move(backupPath, manifestPath);
                        backupPath = null;
                    }
                }
                catch (Exception rollbackException)
                {
                    Mod.log.Error($"[PO Material Library] User material edit rollback failed: {rollbackException.Message}");
                }

                TryDeleteFile(temporaryPath);
                TryDeleteFile(backupPath);
                MaterialLibrary.Reload();

                Mod.log.Error($"[PO Material Library] User material edit failed: {exception.Message}");
                return UserMaterialUpdateResult.Failed(LocalizedText.FromException(exception));
            }
        }

        private static string GetUserMaterialRoot()
        {
            if (string.IsNullOrWhiteSpace(Mod.UserMaterialRootPath))
            {
                throw (LocalizedText.For("PO2.Materials.Error.StorageUnavailable")).AsException(localizedMessage => new InvalidOperationException(localizedMessage));
            }

            return Path.GetFullPath(Mod.UserMaterialRootPath);
        }

        private static string GetDeleteStagingRoot(string userMaterialRoot)
        {
            DirectoryInfo materialRootInfo = new DirectoryInfo(userMaterialRoot);
            DirectoryInfo parent = materialRootInfo.Parent;
            if (parent == null)
            {
                throw (LocalizedText.For("PO2.Materials.Delete.Error.StagingUnresolved")).AsException(localizedMessage => new InvalidOperationException(localizedMessage));
            }

            string stagingRoot = Path.GetFullPath(Path.Combine(parent.FullName, ".MaterialDeleteStaging"));

            // Staging must not be below the scanned library root or the supposedly
            // deleted manifest would simply be loaded from its new location.
            if (IsPathInsideRoot(stagingRoot, userMaterialRoot))
            {
                throw (LocalizedText.For("PO2.Materials.Delete.Error.StagingInsideLibrary")).AsException(localizedMessage => new InvalidOperationException(localizedMessage));
            }

            return stagingRoot;
        }

        private static void EnsureDirectoryInsideRoot(string directory, string root)
        {
            string fullDirectory = Path.GetFullPath(directory);
            string fullRoot = Path.GetFullPath(root);

            // Deleting the root itself would remove every user material, so an
            // exact root match is rejected separately from an escaped path.
            if (string.Equals(TrimTrailingSeparators(fullDirectory), TrimTrailingSeparators(fullRoot), StringComparison.OrdinalIgnoreCase) || !IsPathInsideRoot(fullDirectory, fullRoot))
            {
                throw (LocalizedText.For("PO2.Materials.Error.UnsafeDirectory")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }
        }

        private static void EnsurePathInsideRoot(string path, string root, LocalizedText label)
        {
            if (!IsPathInsideRoot(path, root))
            {
                throw (LocalizedText.For("PO2.Materials.Error.PathOutsideLibrary", ("label", label))).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }
        }

        private static bool IsPathInsideRoot(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = AppendDirectorySeparator(Path.GetFullPath(root));
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string AppendDirectorySeparator(string path)
        {
            string trimmed = TrimTrailingSeparators(path);
            return trimmed + Path.DirectorySeparatorChar;
        }

        private static string TrimTrailingSeparators(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void TryDeleteEmptyDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
                {
                    Directory.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup only
            }
        }

        private static MaterialManifest LoadManifest(string path)
        {
            string json = File.ReadAllText(path);
            MaterialManifest manifest = JSON.MakeInto<MaterialManifest>(JSON.Load(json));
            if (manifest == null)
            {
                throw (LocalizedText.For("PO2.Materials.Error.ManifestUnreadable")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }

            return manifest;
        }

        private static void ValidateWrittenManifest(string path, string materialId)
        {
            // This deliberately checks the fields an edit must never damage. The
            // normal library reload performs the complete schema validation.
            MaterialManifest manifest = LoadManifest(path);
            if (manifest.schemaVersion != UserMaterialWriter.kCurrentSchemaVersion)
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Edit.Error.SchemaUnsupported", ("version", manifest.schemaVersion))).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }

            if (!string.Equals(manifest.id, materialId, StringComparison.Ordinal))
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Edit.Error.MaterialIdChanged")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }

            if (manifest.textures == null || string.IsNullOrWhiteSpace(manifest.textures.baseColor))
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Edit.Error.BaseColorChanged")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }
        }

        private static void ValidateAuthoringState(MaterialAuthoringState state)
        {
            if (state.PhysicalWidthMeters < 0.1f || state.PhysicalWidthMeters > 1000f || state.PhysicalHeightMeters < 0.1f || state.PhysicalHeightMeters > 1000f)
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Validation.PhysicalSize")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }

            if (state.MappingMode != TextureMappingMode.Repeat && state.MappingMode != TextureMappingMode.Stretch)
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Edit.Validation.MappingMode")).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }
        }

        private static string BuildUniqueDisplayName(string requestedDisplayName, string materialId)
        {
            if (!CatalogContainsDisplayName(requestedDisplayName, materialId))
            {
                return requestedDisplayName;
            }

            int suffix = 1;
            while (true)
            {
                string candidate = $"{requestedDisplayName} ({suffix})";
                if (!CatalogContainsDisplayName(candidate, materialId))
                {
                    return candidate;
                }

                suffix++;
            }
        }

        private static bool CatalogContainsDisplayName(string displayName, string excludedMaterialId)
        {
            int count = MaterialLibrary.CatalogCount;
            for (int i = 0; i < count; i++)
            {
                MaterialDefinition definition = MaterialLibrary.GetCatalogMaterial(i);
                if (definition == null || string.Equals(definition.Id, excludedMaterialId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(definition.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string RequireText(string value, LocalizedText label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw (LocalizedText.For("PO2.MaterialEditor.Validation.RequiredField", ("label", label))).AsException(localizedMessage => new InvalidDataException(localizedMessage));
            }

            return value.Trim();
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup only
            }
        }
    }

    internal sealed class UserMaterialUpdateResult
    {
        public bool Success { get; }
        public string DisplayName { get; }
        public LocalizedText ErrorMessage { get; }

        private UserMaterialUpdateResult(bool success, string displayName, LocalizedText errorMessage)
        {
            Success = success;
            DisplayName = displayName ?? string.Empty;
            ErrorMessage = errorMessage;
        }

        internal static UserMaterialUpdateResult Succeeded(string displayName)
        {
            return new UserMaterialUpdateResult(true, displayName, LocalizedText.Empty);
        }

        internal static UserMaterialUpdateResult Failed(LocalizedText errorMessage)
        {
            return new UserMaterialUpdateResult(false, string.Empty, errorMessage);
        }
    }

    internal sealed class UserMaterialDeleteResult
    {
        public bool Success { get; }
        public string DisplayName { get; }
        public LocalizedText ErrorMessage { get; }

        private UserMaterialDeleteResult(bool success, string displayName, LocalizedText errorMessage)
        {
            Success = success;
            DisplayName = displayName ?? string.Empty;
            ErrorMessage = errorMessage;
        }

        internal static UserMaterialDeleteResult Succeeded(string displayName)
        {
            return new UserMaterialDeleteResult(true, displayName, LocalizedText.Empty);
        }

        internal static UserMaterialDeleteResult Failed(LocalizedText errorMessage)
        {
            return new UserMaterialDeleteResult(false, string.Empty, errorMessage);
        }
    }
}