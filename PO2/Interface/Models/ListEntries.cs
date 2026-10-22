using Colossal.UI.Binding;

namespace PO2.Interface.Models
{
    internal sealed class ObjectListEntry : IJsonWritable
    {
        private readonly int m_ObjectId;
        private readonly string m_Name;

        internal ObjectListEntry(int objectId, string name)
        {
            m_ObjectId = objectId;
            m_Name = name ?? string.Empty;
        }

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("PO2.ObjectListEntry");
            writer.PropertyName("objectId");
            writer.Write(m_ObjectId);
            writer.PropertyName("name");
            writer.Write(m_Name);
            writer.TypeEnd();
        }
    }

    internal sealed class NamedIndex : IJsonWritable
    {
        private readonly int m_Index;
        private readonly string m_Name;

        internal NamedIndex(int index, string name)
        {
            m_Index = index;
            m_Name = name ?? string.Empty;
        }

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("PO2.NamedIndex");
            writer.PropertyName("index");
            writer.Write(m_Index);
            writer.PropertyName("name");
            writer.Write(m_Name);
            writer.TypeEnd();
        }
    }

    internal sealed class MaterialLibraryEntry : IJsonWritable
    {
        private readonly string m_Id;
        private readonly string m_DisplayName;
        private readonly string m_Category;
        private readonly int m_Source;

        internal MaterialLibraryEntry(string id, string displayName, string category, int source)
        {
            m_Id = id ?? string.Empty;
            m_DisplayName = displayName ?? string.Empty;
            m_Category = category ?? string.Empty;
            m_Source = source;
        }

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("PO2.MaterialLibraryEntry");
            writer.PropertyName("id");
            writer.Write(m_Id);
            writer.PropertyName("displayName");
            writer.Write(m_DisplayName);
            writer.PropertyName("category");
            writer.Write(m_Category);
            writer.PropertyName("source");
            writer.Write(m_Source);
            writer.TypeEnd();
        }
    }
}