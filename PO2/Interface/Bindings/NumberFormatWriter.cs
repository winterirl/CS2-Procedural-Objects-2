using Colossal.UI.Binding;
using PO2.Localization;

namespace PO2.Interface.Bindings
{
    internal sealed class NumberFormatWriter : IWriter<NumberFormat>
    {
        internal static readonly NumberFormatWriter s_Instance = new NumberFormatWriter();

        public void Write(IJsonWriter writer, NumberFormat format)
        {
            writer.TypeBegin("PO2.NumberFormat");
            writer.PropertyName("locale");
            writer.Write(format.Locale);
            writer.PropertyName("decimalSeparator");
            writer.Write(format.DecimalSeparator);
            writer.PropertyName("groupSeparator");
            writer.Write(format.GroupSeparator);
            writer.PropertyName("negativeSign");
            writer.Write(format.NegativeSign);
            writer.PropertyName("positiveSign");
            writer.Write(format.PositiveSign);
            writer.PropertyName("groupSizes");
            writer.ArrayBegin((uint)format.GroupSizes.Count);
            foreach (int size in format.GroupSizes) writer.Write(size);
            writer.ArrayEnd();
            writer.TypeEnd();
        }
    }
}