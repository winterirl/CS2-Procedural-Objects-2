using System;
using System.Threading;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Persistence.Model;

namespace PO2.Core.Operations
{
    // Captured on the owner thread. These topology buffers are replaced but never edited in place so
    // a worker can keep reading them until its work drains.
    internal sealed class MirrorTopologySnapshot
    {
        internal readonly MeshTopology Logical;
        internal readonly StructuralTopology Structural;
        internal readonly StructuralFaceTriangles[] FaceTriangles;
        internal readonly int[] TriangleFaces;
        private readonly StructuralTopologySource m_Kind;
        private PersistencePartData m_Source;
        private int[] m_TriangleMap;
        internal bool UsesFallback { get; private set; }

        internal MirrorTopologySnapshot(MeshTopology logical, StructuralTopology structural, StructuralFaceTriangles[] faceTriangles, int[] triangleFaces, StructuralTopologySource kind)
        {
            Logical = logical;
            Structural = structural;
            FaceTriangles = faceTriangles;
            TriangleFaces = triangleFaces;
            m_Kind = kind;
        }

        internal void Prepare(PersistencePartData source, CancellationToken token)
        {
            if (m_Source != null) throw new InvalidOperationException("Mirror topology was already prepared.");
            if (source.StructuralTopologySource != m_Kind || source.Vertices.Length != Logical.RenderVertexCount || source.LogicalVertexCount != Logical.LogicalVertexCount || source.RenderToLogical.Length != Logical.RenderVertexCount || source.StructuralFaces.Length != Structural.FaceCount || FaceTriangles.Length != Structural.FaceCount || source.Triangles.Length != 3L * TriangleFaces.Length)
                throw new InvalidOperationException("The captured mirror topology no longer matches the mesh.");
            for (int i = 0; i < source.RenderToLogical.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                if (source.RenderToLogical[i] != Logical.GetLogicalVertexIdForRenderVertex(i))
                    throw new InvalidOperationException("The captured mirror vertex groups changed.");
            }

            bool quads = false;
            for (int i = 0; i < source.StructuralFaces.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                var face = source.StructuralFaces[i];
                var original = Structural.GetFace(i);
                var map = FaceTriangles[i];
                if (face.Id != i || original.Id != i || face.CornerCount != original.CornerCount || map.Count != face.CornerCount - 2)
                    throw new InvalidOperationException("The captured mirror face changed.");
                for (int j = 0; j < face.CornerCount; j++)
                    if (face.GetRenderVertexIndex(j) != original.GetRenderVertexIndex(j))
                        throw new InvalidOperationException("The captured mirror face corners changed.");
                ValidateTriangle(source.Triangles, map.TriangleA, i, face.GetRenderVertexIndex(0), face.GetRenderVertexIndex(1), face.GetRenderVertexIndex(2));
                if (!face.IsQuad) continue;
                quads = true;
                ValidateTriangle(source.Triangles, map.TriangleB, i, face.GetRenderVertexIndex(0), face.GetRenderVertexIndex(2), face.GetRenderVertexIndex(3));
                if (map.TriangleA == map.TriangleB) throw new InvalidOperationException("A mirror quad references the same triangle twice.");
            }

            // Swapping quad records can change which identical triangle the ordinary resolver chooses.
            // Keep its full restore for that case.
            UsesFallback = quads && StructuralFaceTriangleResolver.HasDuplicateTriangles(source.Triangles, token);
            if (quads && !UsesFallback)
            {
                m_TriangleMap = new int[TriangleFaces.Length];
                for (int i = 0; i < m_TriangleMap.Length; i++)
                {
                    if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                    m_TriangleMap[i] = i;
                }

                for (int i = 0; i < FaceTriangles.Length; i++)
                    if (source.StructuralFaces[i].IsQuad)
                    {
                        if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                        var pair = FaceTriangles[i];
                        m_TriangleMap[pair.TriangleA] = pair.TriangleB;
                        m_TriangleMap[pair.TriangleB] = pair.TriangleA;
                    }
            }

            token.ThrowIfCancellationRequested();
            m_Source = source;
        }

        private void ValidateTriangle(int[] indices, int triangle, int face, int a, int b, int c)
        {
            if (triangle < 0 || triangle >= TriangleFaces.Length || TriangleFaces[triangle] != face)
                throw new InvalidOperationException("Invalid cached mirror face mapping.");
            int at = triangle * 3, x = indices[at], y = indices[at + 1], z = indices[at + 2];
            if (!(x == a && y == b && z == c || x == b && y == c && z == a || x == c && y == a && z == b))
                throw new InvalidOperationException("The cached mirror face does not match its triangle.");
        }

        internal MeshPart CreateCandidate(PersistencePartData target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var source = m_Source ?? throw new InvalidOperationException("Mirror topology has not been prepared.");
            if (UsesFallback) return target.RestoreOwned();
            if (target.StructuralTopologySource != m_Kind || target.Vertices.Length != source.Vertices.Length || target.Triangles.Length != source.Triangles.Length || target.LogicalVertexCount != source.LogicalVertexCount || !ReferenceEquals(target.RenderToLogical, source.RenderToLogical) || !ReferenceEquals(target.TriangleSurfaceAreaIds, source.TriangleSurfaceAreaIds))
                throw new InvalidOperationException("Mirror changed the mesh layout or Surface Area membership.");
            for (int i = 0; i < TriangleFaces.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                int at = i * 3, from = (m_TriangleMap == null ? i : m_TriangleMap[i]) * 3;
                if (target.Triangles[at] != source.Triangles[from] || target.Triangles[at + 1] != source.Triangles[from + 2] || target.Triangles[at + 2] != source.Triangles[from + 1])
                    throw new InvalidOperationException("Mirror triangle order does not match its topology.");
            }

            var logical = Logical.RemapMirrorTriangles(m_TriangleMap, token);
            var structural = Structural.ReverseMirrorFaces(target.StructuralFaces, token);
            return MeshPart.CreateMirrorCandidate(target, logical, structural, FaceTriangles, TriangleFaces, token);
        }
    }
}