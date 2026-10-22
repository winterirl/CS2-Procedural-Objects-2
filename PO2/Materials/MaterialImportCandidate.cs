using System;

namespace PO2.Materials
{
    // A file pair discovered by ScanDirectory, before the user supplies any of
    // the metadata needed to make it a library material.
    public sealed class MaterialImportCandidate
    {
        public string SuggestedDisplayName { get; }
        public string BaseColorPath { get; }
        public string NormalPath { get; }

        public MaterialImportCandidate(string suggestedDisplayName, string baseColorPath, string normalPath)
        {
            SuggestedDisplayName = suggestedDisplayName ?? string.Empty;
            BaseColorPath = baseColorPath ?? throw new ArgumentNullException(nameof(baseColorPath));
            NormalPath = normalPath;
        }
    }
}