using PO2.Localization;

namespace PO2.Interface.Models
{
    internal enum MaterialImportMessageKind
    {
        None = 0,
        ScanResult = 1,
        Error = 2
    }

    internal readonly struct MaterialImportNotice
    {
        internal MaterialImportMessageKind Kind { get; }
        internal LocalizedText Text { get; }

        internal MaterialImportNotice(MaterialImportMessageKind kind, LocalizedText text)
        {
            Kind = kind;
            Text = text;
        }
    }
}