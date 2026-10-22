using System;
using UnityEngine;

namespace PO2.Geometry
{
    /// The actual render mesh data for one PO part. These arrays are PO-local and are the source data used to build Unity meshes.
    public sealed class MeshData
    {
        // "Vertex" means render vertex here.
        // Having three different things all called vertex is stupid I know. But like, it's true.
        // One logical/editable point may have several render vertices because normals, UV seams, materials,
        // etc. still need separate values at the same position.
        public Vector3[] Vertices { get; private set; }
        public Vector3[] Normals { get; private set; }
        public Vector4[] Tangents { get; private set; }
        public Vector4[] UV0 { get; private set; }
        public Color32[] VertexColors { get; private set; }
        public Vector4[] TexCoord1 { get; private set; }
        public Vector4[] TexCoord3 { get; private set; }
        public int[] Triangles { get; private set; }

        public MeshData(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, int[] triangles) : this(vertices, normals, tangents, uv0, null, null, triangles)
        {
        }

        public MeshData(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord3, int[] triangles) : this(vertices, normals, tangents, uv0, vertexColors, null, texCoord3, triangles)
        {
        }

        public MeshData(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int[] triangles)
        {
            ValidateTopologyArrays(vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3, triangles);

            Vertices = vertices;
            Normals = normals;
            Tangents = tangents;
            UV0 = uv0;
            VertexColors = vertexColors;
            TexCoord1 = texCoord1;
            TexCoord3 = texCoord3;
            Triangles = triangles;
        }

        public void ReplaceTopology(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, int[] triangles)
        {
            ReplaceTopology(vertices, normals, tangents, uv0, null, null, triangles);
        }

        public void ReplaceTopology(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord3, int[] triangles)
        {
            ReplaceTopology(vertices, normals, tangents, uv0, vertexColors, null, texCoord3, triangles);
        }

        public void ReplaceTopology(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int[] triangles)
        {
            ValidateTopologyArrays(vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3, triangles);

            Vertices = vertices;
            Normals = normals;
            Tangents = tangents;
            UV0 = uv0;
            VertexColors = vertexColors;
            TexCoord1 = texCoord1;
            TexCoord3 = texCoord3;
            Triangles = triangles;
        }

        // A rollback keeps the old array references until publication succeeds.
        // Neither wrapper may edit those arrays while the swap is pending.
        internal MeshData CaptureTopologyReference() => (MeshData)MemberwiseClone();

        internal void AdoptPreparedTopology(MeshData prepared)
        {
            if (prepared == null) throw new ArgumentNullException(nameof(prepared));
            Vertices = prepared.Vertices;
            Normals = prepared.Normals;
            Tangents = prepared.Tangents;
            UV0 = prepared.UV0;
            VertexColors = prepared.VertexColors;
            TexCoord1 = prepared.TexCoord1;
            TexCoord3 = prepared.TexCoord3;
            Triangles = prepared.Triangles;
        }

        internal void ReplaceDeformationStreams(Vector3[] vertices, Vector3[] normals, Vector4[] tangents)
        {
            if (vertices == null || vertices.Length != Vertices.Length || (normals == null) != (Normals == null) || (normals != null && normals.Length != vertices.Length) || (tangents == null) != (Tangents == null) || (tangents != null && tangents.Length != vertices.Length))
            {
                throw new ArgumentException("Replacement deformation streams do not match the mesh layout.");
            }

            Vertices = vertices;
            Normals = normals;
            Tangents = tangents;
        }

        internal void ReplaceDirectionStreams(Vector3[] normals, Vector4[] tangents)
        {
            if ((normals == null) != (Normals == null) || (normals != null && normals.Length != Vertices.Length) || (tangents == null) != (Tangents == null) || (tangents != null && tangents.Length != Vertices.Length))
            {
                throw new ArgumentException("Replacement direction streams do not match the mesh layout.");
            }

            Normals = normals;
            Tangents = tangents;
        }

        private static void ValidateTopologyArrays(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int[] triangles)
        {
            // Keep all of this validation here.
            // Constructors, persistence restore, and topology edits all eventually come through this class,
            // so an invalid stream should fail before it reaches unity.
            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }

            if (triangles == null)
            {
                throw new ArgumentNullException(nameof(triangles));
            }

            if (triangles.Length % 3 != 0)
            {
                throw new ArgumentException("Triangle index array length must be divisible by three.", nameof(triangles));
            }

            if (normals != null && normals.Length != vertices.Length)
            {
                throw new ArgumentException("Normal count must match vertex count.", nameof(normals));
            }

            if (tangents != null && tangents.Length != vertices.Length)
            {
                throw new ArgumentException("Tangent count must match vertex count.", nameof(tangents));
            }

            if (uv0 != null && uv0.Length != vertices.Length)
            {
                throw new ArgumentException("UV0 count must match vertex count.", nameof(uv0));
            }

            if (vertexColors != null && vertexColors.Length != vertices.Length)
            {
                throw new ArgumentException("Vertex color count must match vertex count.", nameof(vertexColors));
            }

            if (texCoord1 != null && texCoord1.Length != vertices.Length)
            {
                throw new ArgumentException("TexCoord1 count must match vertex count.", nameof(texCoord1));
            }

            if (texCoord3 != null && texCoord3.Length != vertices.Length)
            {
                throw new ArgumentException("TexCoord3 count must match vertex count.", nameof(texCoord3));
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                int renderVertexIndex = triangles[i];
                if (renderVertexIndex < 0 || renderVertexIndex >= vertices.Length)
                {
                    throw new ArgumentException($"Triangle index {renderVertexIndex} is outside the vertex array.", nameof(triangles));
                }
            }
        }

        public MeshData Clone()
        {
            // A duplicate/edit snapshot needs its own arrays.
            // Sharing any of these would mean editing one PO could quietly edit another one.
            return new MeshData((Vector3[])Vertices.Clone(), Normals != null ? (Vector3[])Normals.Clone() : null, Tangents != null ? (Vector4[])Tangents.Clone() : null, UV0 != null ? (Vector4[])UV0.Clone() : null, VertexColors != null ? (Color32[])VertexColors.Clone() : null, TexCoord1 != null ? (Vector4[])TexCoord1.Clone() : null, TexCoord3 != null ? (Vector4[])TexCoord3.Clone() : null, (int[])Triangles.Clone());
        }

        public void RecalculateNormals()
        {
            if (Normals == null || Normals.Length != Vertices.Length)
            {
                Normals = new Vector3[Vertices.Length];
            }
            else
            {
                Array.Clear(Normals, 0, Normals.Length);
            }

            Vector3[] normals = Normals;

            // Add the non-normalized face normal to each corner.
            // This naturally gives larger triangles more weight, which is fine for these generated/editable meshes
            // and matches the usual Unity approach.
            for (int i = 0; i + 2 < Triangles.Length; i += 3)
            {
                int a = Triangles[i];
                int b = Triangles[i + 1];
                int c = Triangles[i + 2];

                Vector3 edgeAB = Vertices[b] - Vertices[a];
                Vector3 edgeAC = Vertices[c] - Vertices[a];
                Vector3 faceNormal = Vector3.Cross(edgeAB, edgeAC);

                normals[a] += faceNormal;
                normals[b] += faceNormal;
                normals[c] += faceNormal;
            }

            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = normals[i].sqrMagnitude > 0f ? normals[i].normalized : Vector3.up;
            }
        }

        public void RecalculateTangents()
        {
            // Tangents are meaningless without both UVs and normals.
            // Null is a valid stream state here~
            // the renderer can handle meshes that do not have enough source data to generate tangents.
            if (UV0 == null || UV0.Length != Vertices.Length || Normals == null || Normals.Length != Vertices.Length)
            {
                Tangents = null;
                return;
            }

            Vector3[] tan1 = new Vector3[Vertices.Length];
            Vector3[] tan2 = new Vector3[Vertices.Length];

            for (int i = 0; i + 2 < Triangles.Length; i += 3)
            {
                int i1 = Triangles[i];
                int i2 = Triangles[i + 1];
                int i3 = Triangles[i + 2];

                Vector3 v1 = Vertices[i1];
                Vector3 v2 = Vertices[i2];
                Vector3 v3 = Vertices[i3];

                Vector2 w1 = new Vector2(UV0[i1].x, UV0[i1].y);
                Vector2 w2 = new Vector2(UV0[i2].x, UV0[i2].y);
                Vector2 w3 = new Vector2(UV0[i3].x, UV0[i3].y);

                float x1 = v2.x - v1.x;
                float x2 = v3.x - v1.x;
                float y1 = v2.y - v1.y;
                float y2 = v3.y - v1.y;
                float z1 = v2.z - v1.z;
                float z2 = v3.z - v1.z;

                float s1 = w2.x - w1.x;
                float s2 = w3.x - w1.x;
                float t1 = w2.y - w1.y;
                float t2 = w3.y - w1.y;

                float denominator = s1 * t2 - s2 * t1;
                // Degenerate UV triangles do not define a tangent direction.
                // Skip them!
                // definitely dont want to be introducing infinity/NaN into the rest of the mesh.
                if (Mathf.Abs(denominator) < 0.00000001f)
                {
                    continue;
                }

                float r = 1f / denominator;
                Vector3 sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
                Vector3 tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);

                tan1[i1] += sdir;
                tan1[i2] += sdir;
                tan1[i3] += sdir;

                tan2[i1] += tdir;
                tan2[i2] += tdir;
                tan2[i3] += tdir;
            }

            if (Tangents == null || Tangents.Length != Vertices.Length)
            {
                Tangents = new Vector4[Vertices.Length];
            }

            for (int i = 0; i < Vertices.Length; i++)
            {
                Vector3 normal = Normals[i];
                Vector3 tangent = tan1[i];

                Vector3.OrthoNormalize(ref normal, ref tangent);

                // A vertex can still end up with no usable accumulated tangent especially around degenerate UVs.
                // Pick any stable perpendicular direction so normal mapping does not get a zero vector.
                if (tangent.sqrMagnitude < 0.00000001f)
                {
                    tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.999f ? Vector3.up : Vector3.right).normalized;
                }

                float handedness = Vector3.Dot(Vector3.Cross(normal, tangent), tan2[i]) < 0f ? -1f : 1f;

                Tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, handedness);
            }
        }
    }
}