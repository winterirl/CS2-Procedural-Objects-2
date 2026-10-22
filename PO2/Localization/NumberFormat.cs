using System;
using System.Globalization;

namespace PO2.Localization
{
    internal sealed class NumberFormat
    {
        internal string Locale { get; }
        internal string DecimalSeparator { get; }
        internal string GroupSeparator { get; }
        internal string NegativeSign { get; }
        internal string PositiveSign { get; }
        internal System.Collections.Generic.IReadOnlyList<int> GroupSizes { get; }

        internal NumberFormat(string locale)
        {
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(locale) ? "en-US" : locale);
            }
            catch (CultureNotFoundException)
            {
                culture = CultureInfo.GetCultureInfo("en-US");
            }

            Locale = culture.Name;
            NumberFormatInfo number = culture.NumberFormat;
            DecimalSeparator = number.NumberDecimalSeparator;
            GroupSeparator = number.NumberGroupSeparator;
            NegativeSign = number.NegativeSign;
            PositiveSign = number.PositiveSign;
            GroupSizes = Array.AsReadOnly(number.NumberGroupSizes);
        }
    }

    internal sealed class NumberFormatCache
    {
        private string m_Locale;
        private NumberFormat m_Format;

        internal NumberFormat Get(string locale)
        {
            // The game polls bindings every frame. Only rebuild when its language changes.
            if (m_Format == null || !string.Equals(locale, m_Locale, StringComparison.Ordinal))
            {
                m_Format = new NumberFormat(locale);
                m_Locale = locale;
            }

            return m_Format;
        }
    }
}