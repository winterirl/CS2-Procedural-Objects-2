using System;
using System.Threading;
using PO2.Materials;

namespace PO2.Geometry
{
    // One dense remap per part, reused for every area. Only touched entries reset.
    internal sealed class SurfaceAreaGeometryBuilder
    {
        private readonly MeshData m_Source;
        private readonly int[] m_Map, m_Touched;

        internal SurfaceAreaGeometryBuilder(MeshData source)
        {
            m_Source = source ?? throw new ArgumentNullException(nameof(source));
            m_Map = new int[source.Vertices.Length];
            m_Touched = new int[source.Vertices.Length];
        }

        internal void Build(SurfaceArea area, int[] triangleIds, out MeshData mesh, out int[] sourceIndices, CancellationToken token = default)
        {
            int count = 0;
            var triangles = new int[checked(triangleIds.Length * 3)];
            try
            {
                for (int i = 0; i < triangleIds.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    int offset = checked(triangleIds[i] * 3);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int source = m_Source.Triangles[offset + corner];
                        int local = m_Map[source] - 1;
                        if (local < 0)
                        {
                            local = count;
                            m_Touched[count++] = source;
                            m_Map[source] = local + 1;
                        }
                        triangles[i * 3 + corner] = local;
                    }
                }
                sourceIndices = new int[count];
                Array.Copy(m_Touched, sourceIndices, count);
                var flags = area.SourceVertexAttributes;
                mesh = new MeshData(Copy(m_Source.Vertices, count, token), Copy(m_Source.Normals, count, token),
                    Copy(m_Source.Tangents, count, token), Copy(m_Source.UV0, count, token),
                    (flags & VertexAttributeFlags.VertexColor) != 0 ? Copy(m_Source.VertexColors, count, token) : null,
                    (flags & VertexAttributeFlags.TexCoord1) != 0 ? Copy(m_Source.TexCoord1, count, token) : null,
                    (flags & VertexAttributeFlags.TexCoord3) != 0 ? Copy(m_Source.TexCoord3, count, token) : null, triangles);
            }
            finally
            {
                for (int i = 0; i < count; i++) m_Map[m_Touched[i]] = 0;
            }
        }

        private T[] Copy<T>(T[] stream, int count, CancellationToken token)
        {
            if (stream == null) return null;
            var result = new T[count];
            for (int i = 0; i < count; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                result[i] = stream[m_Touched[i]];
            }
            return result;
        }
    }
}
