using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using PO2;
using PO2.Interface.Bindings;
using PO2.Localization;

internal static partial class Program {
    private static void NumberFormatTests() {
        var originalCulture = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var cache = new NumberFormatCache();
            foreach (string locale in new[] { "en-US", "de-DE", "fr-FR", "ru-RU", "hi-IN", "ja-JP", "zh-HANS" }) {
                var format = cache.Get(locale);
                var expected = CultureInfo.GetCultureInfo(locale).NumberFormat;
                Check(format.Locale == CultureInfo.GetCultureInfo(locale).Name, "Descriptor uses the selected game locale");
                Check(format.DecimalSeparator == expected.NumberDecimalSeparator && format.GroupSeparator == expected.NumberGroupSeparator,
                    "Separators come from the selected culture");
                Check(format.GroupSizes.SequenceEqual(expected.NumberGroupSizes), "Group sizes come from the selected culture");
                Check(format.NegativeSign == expected.NegativeSign && format.PositiveSign == expected.PositiveSign, "Culture signs are retained");
                Check(ReferenceEquals(format, cache.Get(locale)), "Unchanged game locale reuses its binding payload");
                using var writer = new TestUIWriter();
                NumberFormatWriter.s_Instance.Write(writer, format);
                using var json = writer.Read();
                var root = json.RootElement;
                Check(root.GetProperty("locale").GetString() == format.Locale, "Binding identifies its locale");
                Check(root.GetProperty("decimalSeparator").GetString() == expected.NumberDecimalSeparator &&
                    root.GetProperty("groupSeparator").GetString() == expected.NumberGroupSeparator, "Binding preserves Unicode separators");
                Check(root.GetProperty("negativeSign").GetString() == expected.NegativeSign &&
                    root.GetProperty("positiveSign").GetString() == expected.PositiveSign, "Binding preserves culture signs");
                Check(root.GetProperty("groupSizes").EnumerateArray().Select(item => item.GetInt32()).SequenceEqual(expected.NumberGroupSizes),
                    "Binding group sizes are JSON numbers");
                Check(CultureInfo.CurrentCulture == CultureInfo.InvariantCulture, "Selected locale never changes global culture");
            }
            var english = cache.Get("en-US");
            var german = cache.Get("de-DE");
            Check(!ReferenceEquals(english, german) && german.DecimalSeparator == ",", "Language switch produces a new binding value");
            foreach (string locale in new[] { null, "", " ", "!" }) {
                var fallback = cache.Get(locale);
                Check(fallback.Locale == "en-US" && fallback.DecimalSeparator == ".", "Unavailable culture falls back to English conventions");
                Check(ReferenceEquals(fallback, cache.Get(locale)), "Fallback is also cached");
            }

            var text = LocalizedText.For("PO2.SaveManager.DeleteResult.Succeeded", ("count", 2), ("size", LocalizedText.Number(1.5, 2)));
            var same = LocalizedText.For("PO2.SaveManager.DeleteResult.Succeeded", ("count", 2), ("size", LocalizedText.Number(1.5, 2)));
            Check(text.Equals(same) && text.GetHashCode() == same.GetHashCode(), "Fixed precision arguments retain structural equality");
            Check(!text.Equals(LocalizedText.For(text.Key, ("count", 2), ("size", LocalizedText.Number(1.5, 1)))), "Precision changes update the binding");
            Check(!text.Equals(LocalizedText.For(text.Key, ("count", 2), ("size", LocalizedText.Number(1.6, 2)))), "Value changes update the binding");
            foreach (string locale in new[] { "en-US", "fr-FR", "de-DE" }) {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
                using var payload = WriteText(text);
                var size = payload.RootElement.GetProperty("args").GetProperty("size");
                Check(size.GetProperty("number").ValueKind == JsonValueKind.Number && size.GetProperty("number").GetDouble() == 1.5,
                    "Fixed precision value stays a JSON number in every culture");
                Check(size.GetProperty("digits").GetInt32() == 2, "Precision travels separately from the value");
            }
            foreach (var invalid in new[] { (double.NaN, 2), (double.PositiveInfinity, 2), (double.NegativeInfinity, 2), (1d, -1), (1d, 21) }) {
                bool rejected = false;
                try { LocalizedText.Number(invalid.Item1, invalid.Item2); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "Invalid fixed precision arguments are rejected before binding");
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }
}
