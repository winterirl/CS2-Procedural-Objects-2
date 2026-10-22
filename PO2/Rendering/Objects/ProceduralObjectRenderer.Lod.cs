using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Materials;
using PO2.Rendering.Lod;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        internal ILodSourceCapture BeginLodSourceCapture(ProceduralObject proceduralObject, LodGenerationKey key)
        {
            if (proceduralObject == null || proceduralObject.Id != key.ObjectId || proceduralObject.PersistentId != key.PersistentId || !m_Instances.TryGetValue(proceduralObject.Id, out RenderInstance instance) || instance.Parts.Count != proceduralObject.Parts.Count)
            {
                return null;
            }

            List<LodAreaCapture> areas = new List<LodAreaCapture>();
            for (int partIndex = 0; partIndex < instance.Parts.Count; ++partIndex)
            {
                RenderPart renderPart = instance.Parts[partIndex];
                MeshPart sourcePart = proceduralObject.Parts[partIndex];

                for (int areaIndex = 0; areaIndex < renderPart.Areas.Count; ++areaIndex)
                {
                    RenderArea renderArea = renderPart.Areas[areaIndex];
                    if (!sourcePart.TryGetSurfaceArea(renderArea.SurfaceAreaId, out SurfaceArea sourceArea))
                    {
                        return null;
                    }

                    LodAreaCapture capture = CreateLodAreaCapture(partIndex, renderArea, sourceArea);
                    if (capture == null)
                    {
                        return null;
                    }

                    areas.Add(capture);
                }
            }

            if (areas.Count == 0)
            {
                return null;
            }

            return new LodSourceCapture(key, proceduralObject.Transform.Scale, areas.ToArray());
        }

        private static LodAreaCapture CreateLodAreaCapture(int partIndex, RenderArea renderArea, SurfaceArea sourceArea)
        {
            Vector3[] vertices;
            Vector3[] normals;
            Vector4[] tangents;
            Vector4[] uv0;
            Vector2[] generatedUv0;
            Color32[] colors;
            Vector4[] texCoord1;
            Vector4[] texCoord3;
            int[] triangles;
            int uv0Dimension;

            if (renderArea.GeneratedMapping != null)
            {
                vertices = renderArea.GeneratedMapping.Vertices;
                normals = renderArea.GeneratedMapping.Normals;
                tangents = null;
                uv0 = null;
                generatedUv0 = renderArea.GeneratedMapping.UVs;
                colors = null;
                texCoord1 = null;
                texCoord3 = null;
                triangles = renderArea.GeneratedMapping.Triangles;
                uv0Dimension = 2;
            }
            else if (renderArea.AreaMesh != null)
            {
                vertices = renderArea.AreaMesh.Vertices;
                normals = renderArea.AreaMesh.Normals;
                tangents = renderArea.AreaMesh.Tangents;
                uv0 = renderArea.AreaMesh.UV0;
                generatedUv0 = null;
                colors = renderArea.AreaMesh.VertexColors;
                texCoord1 = renderArea.AreaMesh.TexCoord1;
                texCoord3 = renderArea.AreaMesh.TexCoord3;
                triangles = renderArea.AreaMesh.Triangles;
                uv0Dimension = renderArea.UV0Dimension;
            }
            else
            {
                return null;
            }

            if (!LodAreaCapture.HasValidShape(vertices, normals, tangents, uv0, generatedUv0, colors, texCoord1, texCoord3, uv0Dimension, triangles))
            {
                return null;
            }

            bool explicitlyOpaque = renderArea.Material != null && renderArea.Material.HasProperty(kSurfaceTypeProperty) && renderArea.Material.GetFloat(kSurfaceTypeProperty) < 0.5f;
            bool allowVertexCacheOptimization = !renderArea.IsolatedRenderer && explicitlyOpaque;

            return new LodAreaCapture(partIndex, renderArea.SurfaceAreaId, renderArea.MaterialIndex, renderArea.IsolatedRenderer, RequiresTangentSpace(sourceArea.Material), allowVertexCacheOptimization, vertices, normals, tangents, uv0, generatedUv0, colors, texCoord1, texCoord3, uv0Dimension, triangles);
        }

        private sealed class LodSourceCapture : ILodSourceCapture, ILodCaptureMetrics
        {
            private readonly Vector3 m_ObjectScale;
            private readonly LodAreaCapture[] m_Areas;
            private readonly List<LodCopyOperation> m_Operations = new List<LodCopyOperation>();
            private int m_OperationIndex;

            internal LodSourceCapture(LodGenerationKey key, Vector3 objectScale, LodAreaCapture[] areas)
            {
                Key = key;
                m_ObjectScale = objectScale;
                m_Areas = areas;

                long totalBytes = 0;
                for (int i = 0; i < m_Areas.Length; ++i)
                {
                    m_Areas[i].AppendOperations(m_Operations, ref totalBytes);
                }

                TotalBytes = totalBytes;
            }

            public LodGenerationKey Key { get; }
            public long TotalBytes { get; }
            public long CopiedBytes { get; private set; }
            public double AllocationMilliseconds { get; private set; }
            public double MaximumAllocationMilliseconds { get; private set; }

            public bool IsComplete =>
                m_OperationIndex >= m_Operations.Count;

            public void CopyNext(int maximumBytes)
            {
                if (maximumBytes < 16)
                {
                    throw new ArgumentOutOfRangeException(nameof(maximumBytes));
                }

                int remainingBudget = maximumBytes;
                while (m_OperationIndex < m_Operations.Count && remainingBudget > 0)
                {
                    LodCopyOperation operation = m_Operations[m_OperationIndex];
                    double beforeAllocation = operation.AllocationMilliseconds;
                    int copied = operation.CopyNext(remainingBudget);
                    double allocated = operation.AllocationMilliseconds - beforeAllocation;
                    AllocationMilliseconds += allocated;
                    MaximumAllocationMilliseconds = Math.Max(MaximumAllocationMilliseconds, allocated);
                    if (copied > 0)
                    {
                        remainingBudget -= copied;
                        CopiedBytes += copied;
                    }

                    if (operation.IsComplete)
                    {
                        m_OperationIndex++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public LodCapturedSource Complete()
            {
                if (!IsComplete)
                {
                    throw new InvalidOperationException("LOD source capture is not complete.");
                }

                LodCapturedAreaSource[] areas = new LodCapturedAreaSource[m_Areas.Length];
                for (int i = 0; i < areas.Length; ++i)
                {
                    areas[i] = m_Areas[i].Complete();
                }

                return new LodCapturedSource(Key, m_ObjectScale, areas);
            }
        }

        private sealed class LodAreaCapture
        {
            private readonly int m_PartIndex;
            private readonly int m_SurfaceAreaId;
            private readonly int m_MaterialIndex;
            private readonly bool m_IsolatedRenderer;
            private readonly bool m_RequiresTangents;
            private readonly bool m_AllowVertexCacheOptimization;
            private readonly int m_Uv0Dimension;

            private Vector3[] m_Vertices;
            private Vector3[] m_Normals;
            private Vector4[] m_Tangents;
            private Vector4[] m_Uv0;
            private Color32[] m_Colors;
            private Vector4[] m_TexCoord1;
            private Vector4[] m_TexCoord3;
            private int[] m_Triangles;
            private readonly Vector2[] m_GeneratedUv0Source;

            internal LodAreaCapture(int partIndex, int surfaceAreaId, int materialIndex, bool isolatedRenderer, bool requiresTangents, bool allowVertexCacheOptimization, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Vector2[] generatedUv0, Color32[] colors, Vector4[] texCoord1, Vector4[] texCoord3, int uv0Dimension, int[] triangles)
            {
                m_PartIndex = partIndex;
                m_SurfaceAreaId = surfaceAreaId;
                m_MaterialIndex = materialIndex;
                m_IsolatedRenderer = isolatedRenderer;
                m_RequiresTangents = requiresTangents;
                m_AllowVertexCacheOptimization = allowVertexCacheOptimization;
                m_Uv0Dimension = uv0Dimension;

                m_GeneratedUv0Source = generatedUv0;

                VertexSource = vertices;
                NormalSource = normals;
                TangentSource = tangents;
                Uv0Source = uv0;
                ColorSource = colors;
                TexCoord1Source = texCoord1;
                TexCoord3Source = texCoord3;
                TriangleSource = triangles;
            }

            private Vector3[] VertexSource { get; }
            private Vector3[] NormalSource { get; }
            private Vector4[] TangentSource { get; }
            private Vector4[] Uv0Source { get; }
            private Color32[] ColorSource { get; }
            private Vector4[] TexCoord1Source { get; }
            private Vector4[] TexCoord3Source { get; }
            private int[] TriangleSource { get; }

            internal static bool HasValidShape(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Vector2[] generatedUv0, Color32[] colors, Vector4[] texCoord1, Vector4[] texCoord3, int uv0Dimension, int[] triangles)
            {
                if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length == 0 || triangles.Length % 3 != 0 || uv0Dimension < 2 || uv0Dimension > 4 || (uv0 != null && generatedUv0 != null))
                {
                    return false;
                }

                int vertexCount = vertices.Length;
                return HasLength(normals, vertexCount) && HasLength(tangents, vertexCount) && HasLength(uv0, vertexCount) && HasLength(generatedUv0, vertexCount) && HasLength(colors, vertexCount) && (HasLength(texCoord1, vertexCount) && HasLength(texCoord3, vertexCount));
            }

            internal void AppendOperations(List<LodCopyOperation> operations, ref long totalBytes)
            {
                AddCopy(operations, VertexSource, value => m_Vertices = value, 12, ref totalBytes);
                AddCopy(operations, NormalSource, value => m_Normals = value, 12, ref totalBytes);
                AddCopy(operations, TangentSource, value => m_Tangents = value, 16, ref totalBytes);

                if (m_GeneratedUv0Source != null)
                {
                    LodCopyOperation operation = new LodUvCopyOperation(m_GeneratedUv0Source, value => m_Uv0 = value);
                    operations.Add(operation);
                    totalBytes = checked(totalBytes + operation.TotalBytes);
                }
                else
                {
                    AddCopy(operations, Uv0Source, value => m_Uv0 = value, 16, ref totalBytes);
                }

                AddCopy(operations, ColorSource, value => m_Colors = value, 4, ref totalBytes);
                AddCopy(operations, TexCoord1Source, value => m_TexCoord1 = value, 16, ref totalBytes);
                AddCopy(operations, TexCoord3Source, value => m_TexCoord3 = value, 16, ref totalBytes);
                AddCopy(operations, TriangleSource, value => m_Triangles = value, 4, ref totalBytes);
            }

            internal LodCapturedAreaSource Complete()
            {
                if (m_Vertices == null || m_Triangles == null)
                {
                    throw new InvalidOperationException("LOD area capture is incomplete.");
                }

                return new LodCapturedAreaSource(m_PartIndex, m_SurfaceAreaId, m_MaterialIndex, m_IsolatedRenderer, m_RequiresTangents, m_AllowVertexCacheOptimization, new LodCapturedMeshData(m_Vertices, m_Normals, m_Tangents, m_Uv0, m_Colors, m_TexCoord1, m_TexCoord3, m_Uv0Dimension, m_Triangles));
            }

            private static void AddCopy<T>(List<LodCopyOperation> operations, T[] source, Action<T[]> setDestination, int elementBytes, ref long totalBytes)
            {
                if (source == null)
                {
                    return;
                }

                LodCopyOperation operation = new LodArrayCopyOperation<T>(source, setDestination, elementBytes);
                operations.Add(operation);
                totalBytes = checked(totalBytes + operation.TotalBytes);
            }

            private static bool HasLength(Array values, int expected)
            {
                return values == null || values.Length == expected;
            }
        }

        private abstract class LodCopyOperation
        {
            internal double AllocationMilliseconds { get; set; }
            internal abstract long TotalBytes { get; }
            internal abstract bool IsComplete { get; }
            internal abstract int CopyNext(int maximumBytes);
        }

        private sealed class LodArrayCopyOperation<T> : LodCopyOperation
        {
            private readonly T[] m_Source;
            private readonly Action<T[]> m_SetDestination;
            private readonly int m_ElementBytes;
            private T[] m_Destination;
            private int m_Index;

            internal LodArrayCopyOperation(T[] source, Action<T[]> setDestination, int elementBytes)
            {
                m_Source = source;
                m_SetDestination = setDestination;
                m_ElementBytes = elementBytes;
            }

            internal override long TotalBytes =>
                (long)m_Source.Length * m_ElementBytes;

            internal override bool IsComplete =>
                m_Index >= m_Source.Length;

            internal override int CopyNext(int maximumBytes)
            {
                int remaining = m_Source.Length - m_Index;
                if (remaining <= 0)
                {
                    return 0;
                }

                int count = Math.Min(remaining, maximumBytes / m_ElementBytes);
                if (count <= 0)
                {
                    return 0;
                }

                if (m_Destination == null)
                {
                    long started = Stopwatch.GetTimestamp();
                    m_Destination = new T[m_Source.Length];
                    AllocationMilliseconds += ElapsedMilliseconds(started);
                    m_SetDestination(m_Destination);
                }

                Array.Copy(m_Source, m_Index, m_Destination, m_Index, count);
                m_Index += count;
                return count * m_ElementBytes;
            }
        }

        private sealed class LodUvCopyOperation : LodCopyOperation
        {
            private readonly Vector2[] m_Source;
            private readonly Action<Vector4[]> m_SetDestination;
            private Vector4[] m_Destination;
            private int m_Index;

            internal LodUvCopyOperation(Vector2[] source, Action<Vector4[]> setDestination)
            {
                m_Source = source;
                m_SetDestination = setDestination;
            }

            internal override long TotalBytes =>
                (long)m_Source.Length * 16L;

            internal override bool IsComplete =>
                m_Index >= m_Source.Length;

            internal override int CopyNext(int maximumBytes)
            {
                int remaining = m_Source.Length - m_Index;
                int count = Math.Min(remaining, maximumBytes / 16);
                if (count <= 0)
                {
                    return 0;
                }

                if (m_Destination == null)
                {
                    long started = Stopwatch.GetTimestamp();
                    m_Destination = new Vector4[m_Source.Length];
                    AllocationMilliseconds += ElapsedMilliseconds(started);
                    m_SetDestination(m_Destination);
                }

                int end = m_Index + count;
                for (; m_Index < end; ++m_Index)
                {
                    Vector2 value = m_Source[m_Index];
                    m_Destination[m_Index] = new Vector4(value.x, value.y, 0.0f, 0.0f);
                }

                return count * 16;
            }
        }
    }
}