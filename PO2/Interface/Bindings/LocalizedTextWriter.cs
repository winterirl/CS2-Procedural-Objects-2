using Colossal.UI.Binding;
using PO2.Localization;

namespace PO2.Interface.Bindings
{
    internal sealed class LocalizedTextWriter : IWriter<LocalizedText>
    {
        internal static readonly LocalizedTextWriter s_Instance = new LocalizedTextWriter();

        public void Write(IJsonWriter writer, LocalizedText text)
        {
            if (text.IsEmpty)
            {
                writer.WriteNull();
                return;
            }

            // Message fields vary, so use maps instead of a named type with fixed fields.
            writer.MapBegin(text.Key == null ? 1u : 2u);
            if (text.Lines != null)
            {
                writer.Write("lines");
                writer.ArrayBegin((uint)text.Lines.Count);
                foreach (var line in text.Lines) Write(writer, line);
                writer.ArrayEnd();
            }
            else if (text.Key == null)
            {
                writer.Write("literal");
                writer.Write(text.Literal);
            }
            else
            {
                writer.Write("key");
                writer.Write(text.Key);
                writer.Write("args");
                writer.MapBegin((uint)(text.Arguments?.Count ?? 0));
                if (text.Arguments != null)
                    foreach (var argument in text.Arguments)
                    {
                        writer.Write(argument.Name);
                        switch (argument.Value)
                        {
                            case LocalizedText nested: Write(writer, nested); break;
                            case LocalizedNumber number:
                                writer.TypeBegin("PO2.TextNumber");
                                writer.PropertyName("number");
                                writer.Write(number.Value);
                                writer.PropertyName("digits");
                                writer.Write(number.Digits);
                                writer.TypeEnd();
                                break;
                            case string value: writer.Write(value); break;
                            case int value: writer.Write(value); break;
                            case long value: writer.Write(value); break;
                            case float value: writer.Write(value); break;
                            case double value: writer.Write(value); break;
                        }
                    }

                writer.MapEnd();
            }

            writer.MapEnd();
        }
    }
}