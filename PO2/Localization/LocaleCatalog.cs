using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Colossal.Json;

namespace PO2.Localization
{
    internal static class LocaleCatalog
    {
        private static readonly Regex s_Token = new Regex(@"\{([A-Za-z][A-Za-z0-9_]*)\}");

        internal static Dictionary<string, string> ReadEnglish(string json)
        {
            var entries = ReadObject(json);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                string text = ReadText(entry.Value);
                if (!entry.Key.StartsWith("PO2.", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(text) || !TryTokens(text, out _))
                    throw new FormatException("Invalid English locale entry: " + entry.Key);
                result.Add(entry.Key, text);
            }

            if (result.Count == 0) throw new FormatException("The English locale is empty.");
            return result;
        }

        internal static Dictionary<string, string> Overlay(Dictionary<string, string> english, string json, Action<string> warn)
        {
            var result = new Dictionary<string, string>(english, StringComparer.Ordinal);
            if (json == null) return result;
            IEnumerable<KeyValuePair<string, Variant>> entries;
            try
            {
                entries = ReadObject(json);
            }
            catch (Exception exception)
            {
                warn?.Invoke("Could not read translation; using English. " + exception.Message);
                return result;
            }

            foreach (var entry in entries)
            {
                if (!english.TryGetValue(entry.Key, out string fallback))
                {
                    warn?.Invoke("Unknown translation key: " + entry.Key);
                    continue;
                }

                string text = ReadText(entry.Value);
                if (text == null)
                {
                    warn?.Invoke("Translation must be a string: " + entry.Key);
                    continue;
                }

                // An unfinished entry behaves just like an omitted entry.
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (!TryTokens(text, out var translatedTokens) || !TryTokens(fallback, out var englishTokens) || !englishTokens.SetEquals(translatedTokens))
                {
                    warn?.Invoke("Translation placeholders do not match English: " + entry.Key);
                    continue;
                }

                result[entry.Key] = text;
            }

            return result;
        }

        private static IEnumerable<KeyValuePair<string, Variant>> ReadObject(string json)
        {
            return JSON.Load(json) as ProxyObject ?? throw new FormatException("Expected a JSON object of text entries.");
        }

        private static string ReadText(Variant value) => value is ProxyString ? value.ToString(CultureInfo.InvariantCulture) : null;

        private static bool TryTokens(string text, out HashSet<string> tokens)
        {
            tokens = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in s_Token.Matches(text)) tokens.Add(match.Groups[1].Value);
            string remaining = s_Token.Replace(text, string.Empty);
            return remaining.IndexOf('{') < 0 && remaining.IndexOf('}') < 0;
        }
    }
}