using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PO2.Localization
{
    // Keep the message untranslated until the UI draws it. Workers and history
    // entries can carry this without reading the game's language or UI state.
    public readonly struct LocalizedText : IEquatable<LocalizedText>
    {
        internal string Key { get; }
        internal IReadOnlyList<(string Name, object Value)> Arguments { get; }
        internal string Literal { get; }
        internal IReadOnlyList<LocalizedText> Lines { get; }
        internal bool IsEmpty => Key == null && Literal == null && Lines == null;
        internal static LocalizedText Empty => default;

        private LocalizedText(string key, (string Name, object Value)[] arguments, string literal, LocalizedText[] lines)
        {
            Key = key;
            Arguments = arguments == null ? null : Array.AsReadOnly(arguments);
            Literal = literal;
            Lines = lines == null ? null : Array.AsReadOnly(lines);
        }

        internal static LocalizedText For(string key, params (string Name, object Value)[] arguments)
        {
            if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("PO2.", StringComparison.Ordinal))
                throw new ArgumentException("A PO2 locale key is required.", nameof(key));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            if (arguments.Length == 0) return new LocalizedText(key, null, null, null);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var argument in arguments)
            {
                if (string.IsNullOrWhiteSpace(argument.Name) || !names.Add(argument.Name))
                    throw new ArgumentException("Message argument names must be unique.", nameof(arguments));
                if (!(argument.Value is string || argument.Value is LocalizedText || argument.Value is LocalizedNumber || argument.Value is int || argument.Value is long || argument.Value is float || argument.Value is double))
                    throw new ArgumentException("Message arguments must be text, numbers or nested messages.", nameof(arguments));
            }

            return new LocalizedText(key, ((string, object)[])arguments.Clone(), null, null);
        }

        internal static LocalizedNumber Number(double value, int digits) => new LocalizedNumber(value, digits);

        // File paths and exception details are diagnostic data, never lookup keys.
        internal static LocalizedText Diagnostic(string text) => string.IsNullOrWhiteSpace(text) ? Empty : new LocalizedText(null, null, text, null);

        internal static LocalizedText FromException(Exception exception) => exception is LocalizedTextException localized ? localized.Text : exception.Data["PO2.LocalizedText"] is LocalizedText attached ? attached : For("PO2.Runtime.Error.Unexpected", ("details", exception.Message));

        internal TException AsException<TException>(Func<string, TException> create) where TException : Exception
        {
            var exception = create(ToString());
            exception.Data["PO2.LocalizedText"] = this;
            return exception;
        }

        internal static LocalizedText Join(IEnumerable<LocalizedText> messages)
        {
            var lines = messages.Where(message => !message.IsEmpty).ToArray();
            return lines.Length == 0 ? Empty : lines.Length == 1 ? lines[0] : new LocalizedText(null, null, null, lines);
        }

        public bool Equals(LocalizedText other) => Key == other.Key && Literal == other.Literal && SequenceEqual(Arguments, other.Arguments) && SequenceEqual(Lines, other.Lines);

        private static bool SequenceEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
                if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                    return false;
            return true;
        }

        public override bool Equals(object other) => other is LocalizedText text && Equals(text);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Key?.GetHashCode() ?? Literal?.GetHashCode() ?? 0;
                if (Arguments != null)
                    foreach (var argument in Arguments)
                        hash = hash * 31 + argument.GetHashCode();
                if (Lines != null)
                    foreach (var line in Lines)
                        hash = hash * 31 + line.GetHashCode();
                return hash;
            }
        }

        // Logs use stable keys and literal arguments, independent of the UI language.
        public override string ToString()
        {
            if (Lines != null) return string.Join("\n", Lines.Select(line => line.ToString()));
            if (Key == null) return Literal ?? string.Empty;
            return Arguments == null ? Key : Key + " (" + string.Join(", ", Arguments.Select(argument => argument.Name + "=" + Convert.ToString(argument.Value, CultureInfo.InvariantCulture))) + ")";
        }
    }

    internal readonly struct LocalizedNumber
    {
        internal double Value { get; }
        internal int Digits { get; }

        internal LocalizedNumber(double value, int digits)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (digits < 0 || digits > 20) throw new ArgumentOutOfRangeException(nameof(digits));
            Value = value;
            Digits = digits;
        }

        public override string ToString() => Value.ToString("F" + Digits, CultureInfo.InvariantCulture);
    }

    internal sealed class LocalizedTextException : InvalidOperationException
    {
        internal LocalizedText Text { get; }

        internal LocalizedTextException(LocalizedText text) : base(text.ToString())
        {
            Text = text;
        }
    }
}