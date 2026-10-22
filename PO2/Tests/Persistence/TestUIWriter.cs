using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Colossal.UI.Binding;

internal sealed class TestUIWriter : IJsonWriter, IDisposable {
    private readonly MemoryStream m_Stream = new MemoryStream();
    private readonly Utf8JsonWriter m_Writer;
    private readonly Stack<Container> m_Containers = new Stack<Container>();
    private readonly Dictionary<string, string[]> m_TypeProperties = new Dictionary<string, string[]>();
    public string debugName => "PO2 binding test";
    internal TestUIWriter() { m_Writer = new Utf8JsonWriter(m_Stream); }

    private enum ContainerKind { Type, Map, Array }

    private sealed class Container {
        internal ContainerKind Kind;
        internal string Name;
        internal long ExpectedValues;
        internal long Values;
        internal readonly List<string> Properties = new List<string>();
    }

    public void TypeBegin(string name) {
        Begin(ContainerKind.Type, name, 0);
        m_Writer.WriteStartObject();
    }

    public void TypeEnd() {
        Container container = End(ContainerKind.Type);
        // A named Cohtml type must keep the same fields in the same order.
        if (m_TypeProperties.TryGetValue(container.Name, out string[] properties)) {
            if (!properties.SequenceEqual(container.Properties))
                throw new InvalidOperationException("UI type changed fields: " + container.Name);
        }
        else m_TypeProperties.Add(container.Name, container.Properties.ToArray());
        m_Writer.WriteEndObject();
    }

    public void MapBegin(uint size) {
        Begin(ContainerKind.Map, null, size * 2L);
        m_Writer.WriteStartObject();
    }

    public void MapEnd() {
        End(ContainerKind.Map);
        m_Writer.WriteEndObject();
    }

    public void ArrayBegin(uint size) {
        Begin(ContainerKind.Array, null, size);
        m_Writer.WriteStartArray();
    }

    public void ArrayEnd() {
        End(ContainerKind.Array);
        m_Writer.WriteEndArray();
    }

    public void PropertyName(string name) {
        if (m_Containers.Count == 0 || m_Containers.Peek().Kind != ContainerKind.Type)
            throw new InvalidOperationException("PropertyName requires a named type. Write map keys as strings.");
        Container container = m_Containers.Peek();
        if (container.Values != container.Properties.Count)
            throw new InvalidOperationException("UI property is missing its value.");
        container.Properties.Add(name);
        m_Writer.WritePropertyName(name);
    }

    public void Write(string value) {
        if (m_Containers.Count > 0 && m_Containers.Peek().Kind == ContainerKind.Map &&
            m_Containers.Peek().Values % 2 == 0) {
            m_Containers.Peek().Values++;
            m_Writer.WritePropertyName(value);
            return;
        }
        ValueWritten();
        m_Writer.WriteStringValue(value);
    }

    public void WriteNull() { ValueWritten(); m_Writer.WriteNullValue(); }
    public void Write(bool value) { ValueWritten(); m_Writer.WriteBooleanValue(value); }
    public void Write(int value) { ValueWritten(); m_Writer.WriteNumberValue(value); }
    public void Write(uint value) { ValueWritten(); m_Writer.WriteNumberValue(value); }
    public void Write(long value) { ValueWritten(); m_Writer.WriteNumberValue(value); }
    public void Write(ulong value) { ValueWritten(); m_Writer.WriteNumberValue(value); }
    public void Write(float value) { ValueWritten(); m_Writer.WriteNumberValue(value); }
    public void Write(double value) { ValueWritten(); m_Writer.WriteNumberValue(value); }

    private void Begin(ContainerKind kind, string name, long expectedValues) {
        ValueWritten();
        m_Containers.Push(new Container { Kind = kind, Name = name, ExpectedValues = expectedValues });
    }

    private Container End(ContainerKind kind) {
        if (m_Containers.Count == 0 || m_Containers.Peek().Kind != kind)
            throw new InvalidOperationException("UI container end does not match its begin.");
        Container container = m_Containers.Pop();
        long expected = kind == ContainerKind.Type ? container.Properties.Count : container.ExpectedValues;
        if (container.Values != expected)
            throw new InvalidOperationException("UI container wrote the wrong number of values.");
        return container;
    }

    private void ValueWritten() {
        if (m_Containers.Count == 0) return;
        Container container = m_Containers.Peek();
        if (container.Kind == ContainerKind.Map && container.Values % 2 == 0)
            throw new InvalidOperationException("UI map needs a string key before its value.");
        if (container.Kind == ContainerKind.Type && container.Values == container.Properties.Count)
            throw new InvalidOperationException("UI type needs a property name before its value.");
        container.Values++;
    }

    internal JsonDocument Read() {
        if (m_Containers.Count != 0) throw new InvalidOperationException("UI container was not closed.");
        m_Writer.Flush();
        return JsonDocument.Parse(m_Stream.ToArray());
    }
    public void Dispose() { m_Writer.Dispose(); m_Stream.Dispose(); }
}
