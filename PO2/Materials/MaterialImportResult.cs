using PO2.Localization;

namespace PO2.Materials
{
    // Import failures are expected user-facing outcomes, so the importer returns
    // them instead of leaking filesystem/validation exceptions into UI bindings.
    public sealed class MaterialImportResult
    {
        public bool Success { get; }
        public string MaterialId { get; }
        public string MaterialDirectory { get; }
        public LocalizedText ErrorMessage { get; }

        private MaterialImportResult(bool success, string materialId, string materialDirectory, LocalizedText errorMessage)
        {
            Success = success;
            MaterialId = materialId;
            MaterialDirectory = materialDirectory;
            ErrorMessage = errorMessage;
        }

        public static MaterialImportResult Succeeded(string materialId, string materialDirectory)
        {
            return new MaterialImportResult(true, materialId, materialDirectory, LocalizedText.Empty);
        }

        public static MaterialImportResult Failed(LocalizedText errorMessage)
        {
            return new MaterialImportResult(false, null, null, errorMessage);
        }
    }
}