using System;
using UnityEngine;

namespace PO2.Rendering.Lod
{
    // Capture detaches arrays on the main thread. Validation runs on the worker
    // so a large mesh cannot stall the rendering update.
    internal sealed class LodCapturedMeshData
    {
        internal LodCapturedMeshData(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord3, int uv0Dimension, int[] triangles) : this(vertices, normals, tangents, uv0, vertexColors, null, texCoord3, uv0Dimension, triangles)
        {
        }

        internal LodCapturedMeshData(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int uv0Dimension, int[] triangles)
        {
            Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
            Normals = normals;
            Tangents = tangents;
            UV0 = uv0;
            VertexColors = vertexColors;
            TexCoord1 = texCoord1;
            TexCoord3 = texCoord3;
            UV0Dimension = uv0Dimension;
            Triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));
        }

        internal Vector3[] Vertices { get; }
        internal Vector3[] Normals { get; }
        internal Vector4[] Tangents { get; }
        internal Vector4[] UV0 { get; }
        internal Color32[] VertexColors { get; }
        internal Vector4[] TexCoord1 { get; }
        internal Vector4[] TexCoord3 { get; }
        internal int UV0Dimension { get; }
        internal int[] Triangles { get; }

        internal LodMeshData Materialize()
        {
            return new LodMeshData(Vertices, Normals, Tangents, UV0, VertexColors, TexCoord1, TexCoord3, UV0Dimension, Triangles);
        }
    }

    internal sealed class LodCapturedAreaSource
    {
        internal LodCapturedAreaSource(int partIndex, int surfaceAreaId, int materialIndex, bool isolatedRenderer, bool requiresTangents, bool allowVertexCacheOptimization, LodCapturedMeshData mesh)
        {
            PartIndex = partIndex;
            SurfaceAreaId = surfaceAreaId;
            MaterialIndex = materialIndex;
            IsolatedRenderer = isolatedRenderer;
            RequiresTangents = requiresTangents;
            AllowVertexCacheOptimization = allowVertexCacheOptimization;
            Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        }

        internal int PartIndex { get; }
        internal int SurfaceAreaId { get; }
        internal int MaterialIndex { get; }
        internal bool IsolatedRenderer { get; }
        internal bool RequiresTangents { get; }
        internal bool AllowVertexCacheOptimization { get; }
        internal LodCapturedMeshData Mesh { get; }

        internal LodAreaSource Materialize()
        {
            return new LodAreaSource(PartIndex, SurfaceAreaId, MaterialIndex, IsolatedRenderer, RequiresTangents, AllowVertexCacheOptimization, Mesh.Materialize());
        }
    }

    internal sealed class LodCapturedSource
    {
        internal LodCapturedSource(LodGenerationKey key, Vector3 objectScale, LodCapturedAreaSource[] areas)
        {
            Key = key;
            ObjectScale = objectScale;
            Areas = areas ?? throw new ArgumentNullException(nameof(areas));
        }

        internal LodGenerationKey Key { get; }
        internal Vector3 ObjectScale { get; }
        internal LodCapturedAreaSource[] Areas { get; }

        internal LodSourceSnapshot Materialize()
        {
            LodAreaSource[] areas = new LodAreaSource[Areas.Length];
            for (int i = 0; i < areas.Length; ++i)
            {
                areas[i] = Areas[i].Materialize();
            }

            return new LodSourceSnapshot(Key, ObjectScale, areas);
        }
    }

    // Each capture step runs on the rendering thread and respects its copy budget.
    // Implementations must not retain Unity objects after construction.
    internal interface ILodSourceCapture
    {
        LodGenerationKey Key { get; }
        long TotalBytes { get; }
        long CopiedBytes { get; }
        bool IsComplete { get; }

        void CopyNext(int maximumBytes);
        LodCapturedSource Complete();
    }
}