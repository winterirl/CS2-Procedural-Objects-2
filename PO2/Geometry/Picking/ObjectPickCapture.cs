using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace PO2.Geometry.Picking
{
    // Copy in small chunks across updates before starting a detached build.
    // The renderer abandons this capture if source versions change meanwhile.
    internal sealed class ObjectPickCapture
    {
        private const int kCopyChunk = 16384;
        internal readonly ObjectPickCacheState Target;
        internal readonly uint Revision;
        private Vector3[] m_SourceVertices;
        private int[] m_SourceTriangles;
        private IReadOnlyList<int> m_SourceMembership;
        private HashSet<int> m_Areas;
        private Vector3[] m_Vertices;
        private int[] m_Triangles;
        private int[] m_Membership;
        private readonly TrianglePickCache m_RefitSource;
        private int m_Stage;
        private int m_Offset;

        internal ObjectPickCapture(ObjectPickCacheState target, Vector3[] vertices, int[] triangles, IReadOnlyList<int> membership, HashSet<int> areas)
        {
            Target = target;
            Revision = target.Revision;
            m_SourceVertices = vertices;
            m_SourceTriangles = triangles;
            m_SourceMembership = membership;
            m_Areas = areas;
            m_RefitSource = target.RefitSource;
        }

        internal bool Step(long deadlineTimestamp = 0)
        {
            if (Target.Revision != Revision) throw new InvalidOperationException("Picking source changed during capture.");
            if (deadlineTimestamp == 0) deadlineTimestamp = Stopwatch.GetTimestamp() + Math.Max(1, Stopwatch.Frequency / 1000);
            if (Stopwatch.GetTimestamp() >= deadlineTimestamp) return m_Stage == 6;
            do
            {
                switch (m_Stage)
                {
                    case 0:
                        m_Vertices = new Vector3[m_SourceVertices.Length];
                        m_Stage = 1;
                        break;
                    case 1:
                        int vertices = Math.Min(kCopyChunk, m_Vertices.Length - m_Offset);
                        Array.Copy(m_SourceVertices, m_Offset, m_Vertices, m_Offset, vertices);
                        m_Offset += vertices;
                        if (m_Offset == m_Vertices.Length)
                        {
                            m_Offset = 0;
                            m_Stage = m_RefitSource != null ? 6 : 2;
                        }

                        break;
                    case 2:
                        m_Triangles = new int[m_SourceTriangles.Length];
                        m_Stage = 3;
                        break;
                    case 3:
                        int indices = Math.Min(kCopyChunk, m_Triangles.Length - m_Offset);
                        Array.Copy(m_SourceTriangles, m_Offset, m_Triangles, m_Offset, indices);
                        m_Offset += indices;
                        if (m_Offset == m_Triangles.Length)
                        {
                            m_Offset = 0;
                            m_Stage = 4;
                        }

                        break;
                    case 4:
                        m_Membership = new int[m_SourceMembership.Count];
                        m_Stage = 5;
                        break;
                    case 5:
                        int end = Math.Min(m_Offset + kCopyChunk, m_Membership.Length);
                        for (; m_Offset < end; m_Offset++) m_Membership[m_Offset] = m_SourceMembership[m_Offset];
                        if (m_Offset == m_Membership.Length) m_Stage = 6;
                        break;
                    case 6:
                        return true;
                }
            } while (Stopwatch.GetTimestamp() < deadlineTimestamp);

            return m_Stage == 6;
        }

        internal Func<TrianglePickCache> CreateBuild()
        {
            if (m_Stage != 6) throw new InvalidOperationException("Picking capture is incomplete.");
            Vector3[] vertices = m_Vertices;
            int[] triangles = m_Triangles;
            int[] membership = m_Membership;
            HashSet<int> areas = m_Areas;
            TrianglePickCache previous = m_RefitSource;
            m_SourceVertices = null;
            m_SourceTriangles = null;
            m_SourceMembership = null;
            m_Areas = null;
            return () =>
            {
                if (previous != null) return previous.RefitCopy(vertices);
                if (membership.Length != triangles.Length / 3)
                {
                    throw new ArgumentException("Picking surface membership does not match the mesh.");
                }

                int count = 0;
                for (int i = 0; i < membership.Length; i++)
                {
                    if (!areas.Contains(membership[i])) continue;
                    triangles[count++] = triangles[i * 3];
                    triangles[count++] = triangles[i * 3 + 1];
                    triangles[count++] = triangles[i * 3 + 2];
                }

                if (count != triangles.Length) Array.Resize(ref triangles, count);
                return new TrianglePickCache(vertices, triangles);
            };
        }
    }
}