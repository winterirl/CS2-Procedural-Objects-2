using System;
using UnityEngine;
using PO2.Geometry.Topology;

namespace PO2.Geometry.Editing
{
    // Topology edits build one of these off to the side, validate the whole thing,
    // and only then hand it back to the part.
    // This is intentionally a complete replacement instead of hella edits.
    // Half a loop cut would be a pretty great way to corrupt an object.
    public sealed class MeshMutationResult
    {
        // Editors know which original render records survive compaction.
        // Keep that map until history has built both replay directions.
        internal int[] HistoryOldToNew { get; private set; }

        internal void SetHistoryVertexMap(int sourceCount, int[] oldToNew)
        {
            var map = new int[sourceCount];
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < sourceCount; i++)
            {
                int target = oldToNew == null ? i : oldToNew[i];
                if (target < -1 || target >= Vertices.Length || (target >= 0 && !seen.Add(target)))
                    throw new ArgumentException("Invalid topology history vertex map.");
                map[i] = target;
            }

            HistoryOldToNew = map;
        }

        public Vector3[] Vertices { get; }
        public Vector3[] Normals { get; }
        public Vector4[] Tangents { get; }
        public Vector4[] UV0 { get; }
        public Color32[] VertexColors { get; }
        public Vector4[] TexCoord1 { get; }
        public Vector4[] TexCoord3 { get; }
        public int[] Triangles { get; }
        public StructuralFace[] StructuralFaces { get; }
        public int[] TriangleSurfaceAreaIds { get; }

        // Subdivision keeps the weld groups we already have. Position welding
        // here would quietly join unrelated vertices that happen to overlap.
        internal int[] RenderToLogical { get; private set; }
        internal int LogicalVertexCount { get; private set; }

        internal void SetLogicalVertexMap(int[] renderToLogical, int logicalVertexCount)
        {
            if (renderToLogical == null || renderToLogical.Length != Vertices.Length || logicalVertexCount < 0 || logicalVertexCount > Vertices.Length)
            {
                throw new ArgumentException("Invalid mutation logical vertex map.");
            }

            bool[] represented = new bool[logicalVertexCount];
            for (int i = 0; i < renderToLogical.Length; i++)
            {
                int logicalId = renderToLogical[i];
                if (logicalId < 0 || logicalId >= logicalVertexCount)
                {
                    throw new ArgumentException("Invalid mutation logical vertex id.");
                }

                represented[logicalId] = true;
            }

            for (int i = 0; i < represented.Length; i++)
            {
                if (!represented[i])
                {
                    throw new ArgumentException("Mutation logical vertices cannot be empty.");
                }
            }

            RenderToLogical = (int[])renderToLogical.Clone();
            LogicalVertexCount = logicalVertexCount;
        }

        public MeshMutationResult(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, int[] triangles, StructuralFace[] structuralFaces, int[] triangleSurfaceAreaIds) : this(vertices, normals, tangents, uv0, null, null, triangles, structuralFaces, triangleSurfaceAreaIds)
        {
        }

        public MeshMutationResult(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord3, int[] triangles, StructuralFace[] structuralFaces, int[] triangleSurfaceAreaIds) : this(vertices, normals, tangents, uv0, vertexColors, null, texCoord3, triangles, structuralFaces, triangleSurfaceAreaIds)
        {
        }

        public MeshMutationResult(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int[] triangles, StructuralFace[] structuralFaces, int[] triangleSurfaceAreaIds)
        {
            Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
            Normals = normals;
            Tangents = tangents;
            UV0 = uv0;
            VertexColors = vertexColors;
            TexCoord1 = texCoord1;
            TexCoord3 = texCoord3;
            Triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));
            StructuralFaces = structuralFaces ?? Array.Empty<StructuralFace>();
            TriangleSurfaceAreaIds = triangleSurfaceAreaIds ?? throw new ArgumentNullException(nameof(triangleSurfaceAreaIds));

            // Optional streams are allowed, but if they exist they still have
            // to describe the same vertices as the position stream.
            if (VertexColors != null && VertexColors.Length != Vertices.Length)
            {
                throw new ArgumentException("Vertex color count must match vertex count.", nameof(vertexColors));
            }

            if (TexCoord1 != null && TexCoord1.Length != Vertices.Length)
            {
                throw new ArgumentException("TexCoord1 count must match vertex count.", nameof(texCoord1));
            }

            if (TexCoord3 != null && TexCoord3.Length != Vertices.Length)
            {
                throw new ArgumentException("TexCoord3 count must match vertex count.", nameof(texCoord3));
            }

            if (TriangleSurfaceAreaIds.Length != Triangles.Length / 3)
            {
                throw new ArgumentException("Surface Area membership count must match triangle count.", nameof(triangleSurfaceAreaIds));
            }
        }
    }
}