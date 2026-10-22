using System;
using PO2.Core.Lod;
using UnityEngine;

namespace PO2.Rendering.Lod
{
    internal readonly struct LodGenerationKey : IEquatable<LodGenerationKey>
    {
        internal LodGenerationKey(long worldEpoch, int objectId, Guid persistentId, ulong sourceRevision, ulong configurationRevision = 0, ulong previewSessionId = 0, ulong requestId = 0)
        {
            WorldEpoch = worldEpoch;
            ObjectId = objectId;
            PersistentId = persistentId;
            SourceRevision = sourceRevision;
            ConfigurationRevision = configurationRevision;
            PreviewSessionId = previewSessionId;
            RequestId = requestId;
        }

        internal long WorldEpoch { get; }
        internal int ObjectId { get; }
        internal Guid PersistentId { get; }
        internal ulong SourceRevision { get; }
        internal ulong ConfigurationRevision { get; }
        internal ulong PreviewSessionId { get; }
        internal ulong RequestId { get; }
        internal bool IsPreview => PreviewSessionId != 0;

        public bool Equals(LodGenerationKey other)
        {
            return WorldEpoch == other.WorldEpoch && ObjectId == other.ObjectId && PersistentId == other.PersistentId && SourceRevision == other.SourceRevision && ConfigurationRevision == other.ConfigurationRevision && PreviewSessionId == other.PreviewSessionId && RequestId == other.RequestId;
        }

        public override bool Equals(object obj)
        {
            return obj is LodGenerationKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldEpoch.GetHashCode();
                hash = hash * 397 ^ ObjectId;
                hash = hash * 397 ^ PersistentId.GetHashCode();
                hash = hash * 397 ^ SourceRevision.GetHashCode();
                hash = hash * 397 ^ ConfigurationRevision.GetHashCode();
                hash = hash * 397 ^ PreviewSessionId.GetHashCode();
                hash = hash * 397 ^ RequestId.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(LodGenerationKey left, LodGenerationKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(LodGenerationKey left, LodGenerationKey right)
        {
            return !left.Equals(right);
        }
    }

    internal sealed class LodMeshData
    {
        internal LodMeshData(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord3, int uv0Dimension, int[] triangles) : this(vertices, normals, tangents, uv0, vertexColors, null, texCoord3, uv0Dimension, triangles)
        {
        }

        internal LodMeshData(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int uv0Dimension, int[] triangles)
        {
            Validate(vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3, uv0Dimension, triangles);

            Vertices = vertices;
            Normals = normals;
            Tangents = tangents;
            UV0 = uv0;
            VertexColors = vertexColors;
            TexCoord1 = texCoord1;
            TexCoord3 = texCoord3;
            UV0Dimension = uv0Dimension;
            Triangles = triangles;

            CalculateBounds(vertices, out Vector3 boundsMin, out Vector3 boundsMax);
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
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
        internal Vector3 BoundsMin { get; }
        internal Vector3 BoundsMax { get; }

        internal int VertexCount => Vertices.Length;
        internal int TriangleCount => Triangles.Length / 3;

        internal long EstimatedBytes
        {
            get
            {
                long bytes = (long)Vertices.Length * 12L;
                bytes += Normals != null ? (long)Normals.Length * 12L : 0L;
                bytes += Tangents != null ? (long)Tangents.Length * 16L : 0L;
                bytes += UV0 != null ? (long)UV0.Length * 16L : 0L;
                bytes += VertexColors != null ? (long)VertexColors.Length * 4L : 0L;
                bytes += TexCoord1 != null ? (long)TexCoord1.Length * 16L : 0L;
                bytes += TexCoord3 != null ? (long)TexCoord3.Length * 16L : 0L;
                bytes += (long)Triangles.Length * sizeof(int);
                return bytes;
            }
        }

        internal LodMeshData WithTangents(Vector4[] tangents)
        {
            return new LodMeshData(Vertices, Normals, tangents, UV0, VertexColors, TexCoord1, TexCoord3, UV0Dimension, Triangles);
        }

        internal LodMeshData WithNormals(Vector3[] normals)
        {
            return new LodMeshData(Vertices, normals, Tangents, UV0, VertexColors, TexCoord1, TexCoord3, UV0Dimension, Triangles);
        }

        private static void Validate(Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int uv0Dimension, int[] triangles)
        {
            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }

            if (triangles == null)
            {
                throw new ArgumentNullException(nameof(triangles));
            }

            if (vertices.Length == 0)
            {
                throw new ArgumentException("LOD mesh data must contain at least one vertex.", nameof(vertices));
            }

            if (triangles.Length == 0 || triangles.Length % 3 != 0)
            {
                throw new ArgumentException("LOD mesh indices must contain complete triangles.", nameof(triangles));
            }

            if (uv0Dimension < 2 || uv0Dimension > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(uv0Dimension));
            }

            ValidateStreamLength(normals, vertices.Length, nameof(normals));
            ValidateStreamLength(tangents, vertices.Length, nameof(tangents));
            ValidateStreamLength(uv0, vertices.Length, nameof(uv0));
            ValidateStreamLength(vertexColors, vertices.Length, nameof(vertexColors));
            ValidateStreamLength(texCoord1, vertices.Length, nameof(texCoord1));
            ValidateStreamLength(texCoord3, vertices.Length, nameof(texCoord3));

            for (int i = 0; i < vertices.Length; ++i)
            {
                if (!IsFinite(vertices[i]))
                {
                    throw new ArgumentException("LOD mesh positions must be finite.", nameof(vertices));
                }

                if (normals != null && !IsFinite(normals[i]))
                {
                    throw new ArgumentException("LOD mesh normals must be finite.", nameof(normals));
                }

                if (tangents != null && !IsFinite(tangents[i]))
                {
                    throw new ArgumentException("LOD mesh tangents must be finite.", nameof(tangents));
                }

                if (uv0 != null && !IsFinite(uv0[i]))
                {
                    throw new ArgumentException("LOD mesh UV0 values must be finite.", nameof(uv0));
                }

                if (texCoord1 != null && !IsFinite(texCoord1[i]))
                {
                    throw new ArgumentException("LOD mesh TexCoord1 values must be finite.", nameof(texCoord1));
                }

                if (texCoord3 != null && !IsFinite(texCoord3[i]))
                {
                    throw new ArgumentException("LOD mesh TexCoord3 values must be finite.", nameof(texCoord3));
                }
            }

            for (int i = 0; i < triangles.Length; ++i)
            {
                if (triangles[i] < 0 || triangles[i] >= vertices.Length)
                {
                    throw new ArgumentException("LOD mesh indices must reference an existing vertex.", nameof(triangles));
                }
            }
        }

        private static void ValidateStreamLength<T>(T[] values, int vertexCount, string parameterName)
        {
            if (values != null && values.Length != vertexCount)
            {
                throw new ArgumentException("LOD vertex stream length must match the position stream.", parameterName);
            }
        }

        private static void CalculateBounds(Vector3[] vertices, out Vector3 minimum, out Vector3 maximum)
        {
            minimum = vertices[0];
            maximum = vertices[0];
            for (int i = 1; i < vertices.Length; ++i)
            {
                Vector3 vertex = vertices[i];
                minimum.x = Math.Min(minimum.x, vertex.x);
                minimum.y = Math.Min(minimum.y, vertex.y);
                minimum.z = Math.Min(minimum.z, vertex.z);
                maximum.x = Math.Max(maximum.x, vertex.x);
                maximum.y = Math.Max(maximum.y, vertex.y);
                maximum.z = Math.Max(maximum.z, vertex.z);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Vector4 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    internal sealed class LodAreaSource
    {
        internal LodAreaSource(int partIndex, int surfaceAreaId, int materialIndex, bool isolatedRenderer, bool requiresTangents, bool allowVertexCacheOptimization, LodMeshData mesh)
        {
            if (partIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

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
        internal LodMeshData Mesh { get; }
    }

    internal sealed class LodSourceSnapshot
    {
        internal LodSourceSnapshot(LodGenerationKey key, Vector3 objectScale, LodAreaSource[] areas)
        {
            if (!IsFinite(objectScale.x) || !IsFinite(objectScale.y) || !IsFinite(objectScale.z))
            {
                throw new ArgumentException("LOD object scale must be finite.", nameof(objectScale));
            }

            Key = key;
            ObjectScale = objectScale;
            Areas = areas ?? throw new ArgumentNullException(nameof(areas));

            for (int i = 0; i < Areas.Length; ++i)
            {
                if (Areas[i] == null)
                {
                    throw new ArgumentException("LOD source areas cannot contain null entries.", nameof(areas));
                }
            }
        }

        internal LodGenerationKey Key { get; }
        internal Vector3 ObjectScale { get; }
        internal LodAreaSource[] Areas { get; }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    internal enum LodSimplificationMethod
    {
        PassThrough = 0,
        AttributeAware = 1,
        Aggressive = 2,
        PositionFocused = 3,
    }

    internal sealed class LodLevelMeshResult
    {
        internal static readonly LodLevelMeshResult Disabled = new LodLevelMeshResult();

        private LodLevelMeshResult()
        {
        }

        internal LodLevelMeshResult(LodMeshData mesh, bool simplified, float relativeError, float absoluteError, LodSimplificationMethod method, int targetTriangleCount, int simplifierOutputTriangleCount, int prunedTriangleCount)
        {
            if (float.IsNaN(relativeError) || float.IsInfinity(relativeError) || relativeError < 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(relativeError));
            }

            if (float.IsNaN(absoluteError) || float.IsInfinity(absoluteError) || absoluteError < 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(absoluteError));
            }

            if (targetTriangleCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetTriangleCount));
            }

            if (simplifierOutputTriangleCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(simplifierOutputTriangleCount));
            }

            if (prunedTriangleCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(prunedTriangleCount));
            }

            Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
            Simplified = simplified;
            RelativeError = relativeError;
            AbsoluteError = absoluteError;
            Method = method;
            TargetTriangleCount = targetTriangleCount;
            SimplifierOutputTriangleCount = simplifierOutputTriangleCount;
            PrunedTriangleCount = prunedTriangleCount;
        }

        // Disabled levels have no candidate mesh. Enabled levels cover every area.
        internal LodMeshData Mesh { get; }
        internal bool Enabled => Mesh != null;
        internal bool Simplified { get; }
        internal float RelativeError { get; }
        internal float AbsoluteError { get; }
        internal LodSimplificationMethod Method { get; }

        internal bool SimplifierRan =>
            Method != LodSimplificationMethod.PassThrough;

        internal bool UsedAggressiveSimplification =>
            Method == LodSimplificationMethod.Aggressive;

        internal int TargetTriangleCount { get; }
        internal int SimplifierOutputTriangleCount { get; }
        internal int PrunedTriangleCount { get; }
        internal bool BudgetMet => Enabled && Mesh.TriangleCount <= TargetTriangleCount;
    }

    internal sealed class LodAreaResult
    {
        internal LodAreaResult(LodAreaSource source, LodLevelMeshResult lod1, LodLevelMeshResult lod2)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Lod1 = lod1 ?? throw new ArgumentNullException(nameof(lod1));
            Lod2 = lod2 ?? throw new ArgumentNullException(nameof(lod2));
        }

        internal LodAreaSource Source { get; }
        internal LodLevelMeshResult Lod1 { get; }
        internal LodLevelMeshResult Lod2 { get; }
    }

    internal readonly struct LodGenerationStatistics
    {
        internal LodGenerationStatistics(int sourceVertices, int sourceTriangles, int lod1Vertices, int lod1Triangles, int lod2Vertices, int lod2Triangles, int lod1TargetTriangles, int lod2TargetTriangles, int simplifiedAreaLevels, int passThroughAreaLevels, int attemptedAreaLevels, int limitedAreaLevels, int budgetMissedAreaLevels, int aggressiveAreaLevels, int prunedTriangles, int lod1SimplifierOutputTriangles, int lod2SimplifierOutputTriangles, long additionalResultBytes, double generationMilliseconds)
        {
            SourceVertices = sourceVertices;
            SourceTriangles = sourceTriangles;
            Lod1Vertices = lod1Vertices;
            Lod1Triangles = lod1Triangles;
            Lod2Vertices = lod2Vertices;
            Lod2Triangles = lod2Triangles;
            Lod1TargetTriangles = lod1TargetTriangles;
            Lod2TargetTriangles = lod2TargetTriangles;
            SimplifiedAreaLevels = simplifiedAreaLevels;
            PassThroughAreaLevels = passThroughAreaLevels;
            AttemptedAreaLevels = attemptedAreaLevels;
            LimitedAreaLevels = limitedAreaLevels;
            BudgetMissedAreaLevels = budgetMissedAreaLevels;
            AggressiveAreaLevels = aggressiveAreaLevels;
            PrunedTriangles = prunedTriangles;
            Lod1SimplifierOutputTriangles = lod1SimplifierOutputTriangles;
            Lod2SimplifierOutputTriangles = lod2SimplifierOutputTriangles;
            AdditionalResultBytes = additionalResultBytes;
            GenerationMilliseconds = generationMilliseconds;
        }

        internal int SourceVertices { get; }
        internal int SourceTriangles { get; }
        internal int Lod1Vertices { get; }
        internal int Lod1Triangles { get; }
        internal int Lod2Vertices { get; }
        internal int Lod2Triangles { get; }
        internal int Lod1TargetTriangles { get; }
        internal int Lod2TargetTriangles { get; }

        internal bool Lod1BudgetMet =>
            Lod1Triangles <= Lod1TargetTriangles;

        internal bool Lod2BudgetMet =>
            Lod2Triangles <= Lod2TargetTriangles;

        internal int SimplifiedAreaLevels { get; }
        internal int PassThroughAreaLevels { get; }
        internal int AttemptedAreaLevels { get; }
        internal int LimitedAreaLevels { get; }
        internal int BudgetMissedAreaLevels { get; }
        internal int AggressiveAreaLevels { get; }
        internal int PrunedTriangles { get; }
        internal int Lod1SimplifierOutputTriangles { get; }
        internal int Lod2SimplifierOutputTriangles { get; }
        internal long AdditionalResultBytes { get; }
        internal double GenerationMilliseconds { get; }
    }

    internal sealed class LodGenerationResult
    {
        internal LodGenerationResult(LodGenerationKey key, LodAreaResult[] areas, LodGenerationStatistics statistics, float lod1MaximumRelativeError, float lod2MaximumRelativeError, float lod1MaximumAbsoluteError, float lod2MaximumAbsoluteError, LodProfile profile, LodAutomaticChain automaticChain)
        {
            Key = key;
            Areas = areas ?? throw new ArgumentNullException(nameof(areas));
            Statistics = statistics;
            Lod1MaximumRelativeError = lod1MaximumRelativeError;
            Lod2MaximumRelativeError = lod2MaximumRelativeError;
            Lod1MaximumAbsoluteError = lod1MaximumAbsoluteError;
            Lod2MaximumAbsoluteError = lod2MaximumAbsoluteError;
            Profile = profile;
            AutomaticChain = automaticChain;
        }

        internal LodGenerationKey Key { get; }
        internal LodAreaResult[] Areas { get; }
        internal LodGenerationStatistics Statistics { get; }
        internal float Lod1MaximumRelativeError { get; }
        internal float Lod2MaximumRelativeError { get; }
        internal float Lod1MaximumAbsoluteError { get; }
        internal float Lod2MaximumAbsoluteError { get; }
        internal LodProfile Profile { get; }
        internal LodAutomaticChain AutomaticChain { get; }
        internal bool WorthPublishing => AutomaticChain.HasGeneratedLevels;
    }

    internal enum LodGenerationStatus
    {
        Completed = 0,
        Cancelled = 1,
        Failed = 2,
    }

    internal sealed class LodGenerationAttempt
    {
        private LodGenerationAttempt(LodGenerationStatus status, LodGenerationResult result, string failureReason)
        {
            Status = status;
            Result = result;
            FailureReason = failureReason ?? string.Empty;
        }

        internal LodGenerationStatus Status { get; }
        internal LodGenerationResult Result { get; }
        internal string FailureReason { get; }

        internal static LodGenerationAttempt Completed(LodGenerationResult result)
        {
            return new LodGenerationAttempt(LodGenerationStatus.Completed, result, string.Empty);
        }

        internal static LodGenerationAttempt Cancelled()
        {
            return new LodGenerationAttempt(LodGenerationStatus.Cancelled, null, string.Empty);
        }

        internal static LodGenerationAttempt Failed(string reason)
        {
            return new LodGenerationAttempt(LodGenerationStatus.Failed, null, reason);
        }
    }
}