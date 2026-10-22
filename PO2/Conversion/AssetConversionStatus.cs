using PO2.Localization;

namespace PO2.Conversion
{
    public enum AssetConversionPhase
    {
        Inactive = 0,
        Ready = 1,
        Running = 2,
        Cancelling = 3,
        Failed = 4
    }

    internal sealed class AssetConversionStatus
    {
        private bool m_Failed;
        internal LocalizedText Message { get; private set; } = LocalizedText.Empty;

        internal AssetConversionPhase GetPhase(bool active, bool busy, bool abandoned)
        {
            if (!active) return AssetConversionPhase.Inactive;
            if (busy) return abandoned ? AssetConversionPhase.Cancelling : AssetConversionPhase.Running;
            return m_Failed ? AssetConversionPhase.Failed : AssetConversionPhase.Ready;
        }

        internal void SetMessage(LocalizedText message)
        {
            m_Failed = false;
            Message = message;
        }

        internal void Fail(LocalizedText message)
        {
            m_Failed = true;
            Message = message;
        }

        internal void Clear() => SetMessage(LocalizedText.Empty);
    }
}