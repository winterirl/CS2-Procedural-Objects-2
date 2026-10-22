using System;
using UnityEngine;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Geometry.Transforms
{
    // Shared geometry math for part transforms, primitive insertion, and Combine.
    // Live transforms re-use a start snapshot while clone paths
    // can also repair winding for reflected coordinate spaces.
    internal static class PartGeometryTransform
    {
        private const float kAffineTolerance = 0.00001f;
        private const float kMinimumDeterminantMagnitude = 0.000000000001f;
        private const float kMinimumDirectionMagnitudeSquared = 0.000000000001f;

        internal static bool TryGetLogicalBounds(MeshPart part, out Bounds bounds)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            bool hasBounds = false;
            bounds = default;

            for (int vertexId = 0; vertexId < part.Topology.LogicalVertexCount; vertexId++)
            {
                Vector3 position = part.Topology.GetVertex(vertexId).GetPosition(part.Mesh);

                if (!hasBounds)
                {
                    bounds = new Bounds(position, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(position);
                }
            }

            return hasBounds;
        }

        internal static bool TryGetLogicalCentroid(MeshPart part, out Vector3 centroid)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            int logicalVertexCount = part.Topology.LogicalVertexCount;

            if (logicalVertexCount <= 0)
            {
                centroid = Vector3.zero;
                return false;
            }

            double x = 0.0;
            double y = 0.0;
            double z = 0.0;

            for (int vertexId = 0; vertexId < logicalVertexCount; vertexId++)
            {
                Vector3 position = part.Topology.GetVertex(vertexId).GetPosition(part.Mesh);

                if (!IsFinite(position))
                {
                    centroid = Vector3.zero;
                    return false;
                }

                x += position.x;
                y += position.y;
                z += position.z;
            }

            double inverseCount = 1.0 / logicalVertexCount;
            centroid = new Vector3((float)(x * inverseCount), (float)(y * inverseCount), (float)(z * inverseCount));

            return IsFinite(centroid);
        }

        internal static bool TryCaptureSnapshot(MeshPart part, bool positionsOnly, out PartGeometrySnapshot snapshot, out string failureReason)
        {
            return PartGeometrySnapshot.TryCapture(part, positionsOnly, out snapshot, out failureReason);
        }

        internal static bool TryCloneTransformed(MeshPart source, Matrix4x4 destinationFromSource, string destinationName, out MeshPart result, out string failureReason)
        {
            result = null;
            failureReason = string.Empty;

            if (source == null)
            {
                failureReason = "The source mesh part is null.";
                return false;
            }

            if (!TryCreateTransformInfo(destinationFromSource, allowReflection: true, out TransformInfo transform, out failureReason))
            {
                return false;
            }

            MeshData sourceMesh = source.Mesh;
            Vector3[] vertices = new Vector3[sourceMesh.Vertices.Length];

            Vector3[] normals = sourceMesh.Normals != null ? new Vector3[sourceMesh.Normals.Length] : null;

            Vector4[] tangents = sourceMesh.Tangents != null ? new Vector4[sourceMesh.Tangents.Length] : null;

            if (!TryTransformStreams(sourceMesh.Vertices, sourceMesh.Normals, sourceMesh.Tangents, vertices, normals, tangents, transform, out failureReason))
            {
                return false;
            }

            if (!TryCreateTransformedWinding(source, transform.ReversesHandedness, out int[] triangles, out StructuralFace[] structuralFaces, out failureReason))
            {
                return false;
            }

            try
            {
                MeshData transformedMesh = new MeshData(vertices, normals, tangents, CloneOrNull(sourceMesh.UV0), CloneOrNull(sourceMesh.VertexColors), CloneOrNull(sourceMesh.TexCoord1), CloneOrNull(sourceMesh.TexCoord3), triangles);

                result = source.CloneWithGeometry(destinationName, transformedMesh, structuralFaces, destinationFromSource.MultiplyPoint3x4(source.LocalPivot));
            }
            catch (Exception exception)
            {
                failureReason = $"The transformed mesh part is invalid: {exception.Message}";
                result = null;
                return false;
            }

            return true;
        }

        internal static bool TryGetMirrorNormalMatrix(Matrix4x4 matrix, out Matrix4x4 normal, out string reason)
        {
            normal = default;
            if (!TryCreateTransformInfo(matrix, true, out var transform, out reason)) return false;
            if (!transform.ReversesHandedness)
            {
                reason = "The mirror transform must reverse handedness.";
                return false;
            }

            normal = transform.NormalMatrix;
            return true;
        }

        internal static bool TryHistoryTransform(Vector3[] positions, Vector3[] normals, Vector4[] tangents, Vector3[] outputPositions, Vector3[] outputNormals, Vector4[] outputTangents, Matrix4x4 matrix, bool positionsOnly, out string failureReason)
        {
            if (positionsOnly)
            {
                if (!TryGetTranslation(matrix, out var translation, out failureReason)) return false;
                return TryTranslatePositions(positions, outputPositions, translation, out failureReason);
            }

            if (!TryCreateTransformInfo(matrix, false, out var transform, out failureReason)) return false;
            return TryTransformStreams(positions, normals, tangents, outputPositions, outputNormals, outputTangents, transform, out failureReason);
        }

        internal static bool TryApplyFromSnapshot(MeshPart destination, PartGeometrySnapshot snapshot, Matrix4x4 destinationFromStart, out string failureReason)
        {
            failureReason = string.Empty;

            if (snapshot == null)
            {
                failureReason = "The part transform snapshot is null.";
                return false;
            }

            if (!snapshot.MatchesDestination(destination, out failureReason))
            {
                return false;
            }

            if (snapshot.PositionsOnly)
            {
                if (!TryGetTranslation(destinationFromStart, out Vector3 translation, out failureReason) || !TryTranslatePositions(snapshot.Positions, snapshot.WorkPositions, translation, out failureReason))
                {
                    return false;
                }

                return snapshot.TryPublishWorkStreams(destination, out failureReason);
            }

            // Part gizmos do not mirror. Reflections are supported by the
            // transformed-clone path where triangle and face winding can be
            // replaced as one validated result.
            if (!TryCreateTransformInfo(destinationFromStart, allowReflection: false, out TransformInfo transform, out failureReason))
            {
                return false;
            }

            if (!TryTransformStreams(snapshot.Positions, snapshot.Normals, snapshot.Tangents, snapshot.WorkPositions, snapshot.WorkNormals, snapshot.WorkTangents, transform, out failureReason))
            {
                return false;
            }

            return snapshot.TryPublishWorkStreams(destination, out failureReason);
        }


        private static bool TryGetTranslation(Matrix4x4 matrix, out Vector3 translation, out string failureReason)
        {
            translation = Vector3.zero;
            failureReason = string.Empty;

            if (!IsFinite(matrix))
            {
                failureReason = "The part translation contains a non-finite value.";
                return false;
            }

            if (Mathf.Abs(matrix.m00 - 1f) > kAffineTolerance || Mathf.Abs(matrix.m11 - 1f) > kAffineTolerance || Mathf.Abs(matrix.m22 - 1f) > kAffineTolerance || Mathf.Abs(matrix.m01) > kAffineTolerance || Mathf.Abs(matrix.m02) > kAffineTolerance || Mathf.Abs(matrix.m10) > kAffineTolerance || Mathf.Abs(matrix.m12) > kAffineTolerance || Mathf.Abs(matrix.m20) > kAffineTolerance || Mathf.Abs(matrix.m21) > kAffineTolerance || Mathf.Abs(matrix.m30) > kAffineTolerance || Mathf.Abs(matrix.m31) > kAffineTolerance || Mathf.Abs(matrix.m32) > kAffineTolerance || Mathf.Abs(matrix.m33 - 1f) > kAffineTolerance)
            {
                failureReason = "A position-only part preview must be a translation.";
                return false;
            }

            translation = new Vector3(matrix.m03, matrix.m13, matrix.m23);

            if (!IsFinite(translation))
            {
                failureReason = "The part translation produced a non-finite offset.";
                return false;
            }

            return true;
        }

        private static bool TryTranslatePositions(Vector3[] source, Vector3[] destination, Vector3 translation, out string failureReason)
        {
            failureReason = string.Empty;

            if (source == null || destination == null || source.Length != destination.Length)
            {
                failureReason = "The part translation streams have mismatched lengths.";
                return false;
            }

            for (int i = 0; i < source.Length; i++)
            {
                Vector3 position = source[i] + translation;
                if (!IsFinite(position))
                {
                    failureReason = $"Render vertex {i} produced a non-finite position.";
                    return false;
                }

                destination[i] = position;
            }

            return true;
        }


        private static bool TryTransformStreams(Vector3[] sourcePositions, Vector3[] sourceNormals, Vector4[] sourceTangents, Vector3[] destinationPositions, Vector3[] destinationNormals, Vector4[] destinationTangents, TransformInfo transform, out string failureReason)
        {
            failureReason = string.Empty;

            if (sourcePositions == null || destinationPositions == null || sourcePositions.Length != destinationPositions.Length || !StreamsMatch(sourcePositions, sourceNormals) || !StreamsMatch(sourcePositions, sourceTangents) || !StreamsMatch(destinationPositions, destinationNormals, sourceNormals != null) || !StreamsMatch(destinationPositions, destinationTangents, sourceTangents != null))
            {
                failureReason = "The part transform streams have mismatched lengths.";
                return false;
            }

            if (!TryTransformPositions(sourcePositions, destinationPositions, transform.PositionMatrix, out failureReason))
            {
                return false;
            }

            return TryTransformDirections(sourcePositions, sourceNormals, sourceTangents, destinationNormals, destinationTangents, transform, out failureReason);
        }

        private static bool TryTransformDirections(Vector3[] sourcePositions, Vector3[] sourceNormals, Vector4[] sourceTangents, Vector3[] destinationNormals, Vector4[] destinationTangents, TransformInfo transform, out string failureReason)
        {
            failureReason = string.Empty;

            if (sourcePositions == null || !StreamsMatch(sourcePositions, sourceNormals) || !StreamsMatch(sourcePositions, sourceTangents) || !StreamsMatch(sourcePositions, destinationNormals, sourceNormals != null) || !StreamsMatch(sourcePositions, destinationTangents, sourceTangents != null))
            {
                failureReason = "The part direction streams have mismatched lengths.";
                return false;
            }

            if (sourceNormals != null)
            {
                for (int i = 0; i < sourceNormals.Length; i++)
                {
                    Vector3 sourceNormal = sourceNormals[i];
                    if (!IsFinite(sourceNormal))
                    {
                        failureReason = $"Render vertex {i} has a non-finite normal.";
                        return false;
                    }

                    if (!TryTransformDirection(transform.NormalMatrix, sourceNormal, out Vector3 transformedNormal))
                    {
                        failureReason = $"Render vertex {i} produced an invalid normal.";
                        return false;
                    }

                    destinationNormals[i] = transformedNormal;
                }
            }

            if (sourceTangents == null)
            {
                return true;
            }

            for (int i = 0; i < sourceTangents.Length; i++)
            {
                Vector4 sourceTangent = sourceTangents[i];
                if (!IsFinite(sourceTangent))
                {
                    failureReason = $"Render vertex {i} has a non-finite tangent.";
                    return false;
                }

                Vector3 tangentDirection = new Vector3(sourceTangent.x, sourceTangent.y, sourceTangent.z);

                Vector3 transformedTangent = transform.PositionMatrix.MultiplyVector(tangentDirection);

                if (!IsFinite(transformedTangent))
                {
                    failureReason = $"Render vertex {i} produced a non-finite tangent.";
                    return false;
                }

                Vector3 transformedNormal = sourceNormals != null ? destinationNormals[i] : Vector3.zero;

                if (!TryNormalizeTangent(transformedTangent, transformedNormal, out transformedTangent))
                {
                    failureReason = $"Render vertex {i} produced an invalid tangent.";
                    return false;
                }

                float handedness = sourceTangent.w * transform.HandednessMultiplier;

                if (!IsFinite(handedness))
                {
                    failureReason = $"Render vertex {i} produced invalid tangent handedness.";
                    return false;
                }

                destinationTangents[i] = new Vector4(transformedTangent.x, transformedTangent.y, transformedTangent.z, handedness);
            }

            return true;
        }

        private static bool TryTransformPositions(Vector3[] sourcePositions, Vector3[] destinationPositions, Matrix4x4 matrix, out string failureReason)
        {
            failureReason = string.Empty;

            if (sourcePositions == null || destinationPositions == null || sourcePositions.Length != destinationPositions.Length)
            {
                failureReason = "The part position streams have mismatched lengths.";
                return false;
            }

            for (int i = 0; i < sourcePositions.Length; i++)
            {
                Vector3 sourcePosition = sourcePositions[i];
                if (!IsFinite(sourcePosition))
                {
                    failureReason = $"Render vertex {i} has a non-finite position.";
                    return false;
                }

                Vector3 transformedPosition = matrix.MultiplyPoint3x4(sourcePosition);

                if (!IsFinite(transformedPosition))
                {
                    failureReason = $"Render vertex {i} produced a non-finite position.";
                    return false;
                }

                destinationPositions[i] = transformedPosition;
            }

            return true;
        }

        private static bool TryCreateTransformInfo(Matrix4x4 matrix, bool allowReflection, out TransformInfo transform, out string failureReason)
        {
            transform = default;
            failureReason = string.Empty;

            if (!IsFinite(matrix))
            {
                failureReason = "The part transform contains a non-finite value.";
                return false;
            }

            if (Mathf.Abs(matrix.m30) > kAffineTolerance || Mathf.Abs(matrix.m31) > kAffineTolerance || Mathf.Abs(matrix.m32) > kAffineTolerance || Mathf.Abs(matrix.m33 - 1f) > kAffineTolerance)
            {
                failureReason = "The part transform is not affine.";
                return false;
            }

            float determinant = matrix.determinant;
            if (!IsFinite(determinant) || Mathf.Abs(determinant) < kMinimumDeterminantMagnitude)
            {
                failureReason = "The part transform is singular.";
                return false;
            }

            bool reversesHandedness = determinant < 0f;
            if (reversesHandedness && !allowReflection)
            {
                failureReason = "Live part transforms cannot mirror mesh geometry.";
                return false;
            }

            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            if (!IsFinite(normalMatrix))
            {
                failureReason = "The part transform does not have a finite inverse.";
                return false;
            }

            transform = new TransformInfo(matrix, normalMatrix, reversesHandedness);

            return true;
        }

        private static bool TryCreateTransformedWinding(MeshPart source, bool reverseWinding, out int[] triangles, out StructuralFace[] structuralFaces, out string failureReason)
        {
            failureReason = string.Empty;
            triangles = (int[])source.Mesh.Triangles.Clone();
            structuralFaces = source.StructuralTopology.CloneFaces();

            if (!reverseWinding)
            {
                return true;
            }

            for (int triangleOffset = 0; triangleOffset + 2 < triangles.Length; triangleOffset += 3)
            {
                int swap = triangles[triangleOffset + 1];
                triangles[triangleOffset + 1] = triangles[triangleOffset + 2];
                triangles[triangleOffset + 2] = swap;
            }

            for (int faceIndex = 0; faceIndex < source.StructuralTopology.FaceCount; faceIndex++)
            {
                StructuralFace sourceFace = source.StructuralTopology.GetFace(faceIndex);

                structuralFaces[faceIndex] = ReverseFaceWinding(sourceFace);

                if (!sourceFace.IsQuad)
                {
                    continue;
                }

                if (!source.TryGetStructuralFaceTriangles(sourceFace.Id, out StructuralFaceTriangles faceTriangles) || faceTriangles.Count != 2)
                {
                    failureReason = $"Structural quad {sourceFace.Id} does not match " + "the source triangle stream.";
                    return false;
                }

                // Reversing a quad swaps its two render triangles. Swap the mapped records too;
                // they need not be consecutive in the source index buffer.
                SwapTriangleTriples(triangles, faceTriangles.TriangleA * 3, faceTriangles.TriangleB * 3);
            }

            return true;
        }

        private static StructuralFace ReverseFaceWinding(StructuralFace face)
        {
            int vertex0 = face.GetRenderVertexIndex(0);

            if (face.IsTriangle)
            {
                return new StructuralFace(face.Id, vertex0, face.GetRenderVertexIndex(2), face.GetRenderVertexIndex(1));
            }

            int vertex1 = face.GetRenderVertexIndex(3);
            int vertex2 = face.GetRenderVertexIndex(2);
            int vertex3 = face.GetRenderVertexIndex(1);

            return new StructuralFace(face.Id, vertex0, vertex1, vertex2, vertex3);
        }

        private static void SwapTriangleTriples(int[] triangles, int leftOffset, int rightOffset)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                int swap = triangles[leftOffset + corner];
                triangles[leftOffset + corner] = triangles[rightOffset + corner];
                triangles[rightOffset + corner] = swap;
            }
        }

        private static bool TryTransformDirection(Matrix4x4 matrix, Vector3 source, out Vector3 result)
        {
            if (source.sqrMagnitude < kMinimumDirectionMagnitudeSquared)
            {
                result = Vector3.zero;
                return true;
            }

            result = matrix.MultiplyVector(source);
            if (!IsFinite(result) || result.sqrMagnitude < kMinimumDirectionMagnitudeSquared)
            {
                return false;
            }

            result.Normalize();
            return IsFinite(result);
        }

        private static bool TryNormalizeTangent(Vector3 tangent, Vector3 normal, out Vector3 result)
        {
            if (tangent.sqrMagnitude < kMinimumDirectionMagnitudeSquared)
            {
                result = Vector3.zero;
                return true;
            }

            result = tangent;

            if (normal.sqrMagnitude >= kMinimumDirectionMagnitudeSquared)
            {
                result -= normal * Vector3.Dot(normal, result);

                if (result.sqrMagnitude < kMinimumDirectionMagnitudeSquared)
                {
                    result = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.999f ? Vector3.up : Vector3.right);
                }
            }

            if (!IsFinite(result) || result.sqrMagnitude < kMinimumDirectionMagnitudeSquared)
            {
                return false;
            }

            result.Normalize();
            return IsFinite(result);
        }

        private static bool StreamsMatch<TSource, TStream>(TSource[] positions, TStream[] stream)
        {
            return stream == null || stream.Length == positions.Length;
        }

        private static bool StreamsMatch<TSource, TStream>(TSource[] positions, TStream[] stream, bool required)
        {
            if (!required)
            {
                return stream == null;
            }

            return stream != null && stream.Length == positions.Length;
        }

        private static Vector4[] CloneOrNull(Vector4[] source)
        {
            return source != null ? (Vector4[])source.Clone() : null;
        }

        private static Color32[] CloneOrNull(Color32[] source)
        {
            return source != null ? (Color32[])source.Clone() : null;
        }

        private static bool IsFinite(Matrix4x4 matrix)
        {
            return IsFinite(matrix.m00) && IsFinite(matrix.m01) && IsFinite(matrix.m02) && IsFinite(matrix.m03) && IsFinite(matrix.m10) && IsFinite(matrix.m11) && IsFinite(matrix.m12) && IsFinite(matrix.m13) && IsFinite(matrix.m20) && IsFinite(matrix.m21) && IsFinite(matrix.m22) && IsFinite(matrix.m23) && IsFinite(matrix.m30) && IsFinite(matrix.m31) && IsFinite(matrix.m32) && IsFinite(matrix.m33);
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

        private readonly struct TransformInfo
        {
            internal Matrix4x4 PositionMatrix { get; }
            internal Matrix4x4 NormalMatrix { get; }
            internal bool ReversesHandedness { get; }

            internal float HandednessMultiplier =>
                ReversesHandedness ? -1f : 1f;

            internal TransformInfo(Matrix4x4 positionMatrix, Matrix4x4 normalMatrix, bool reversesHandedness)
            {
                PositionMatrix = positionMatrix;
                NormalMatrix = normalMatrix;
                ReversesHandedness = reversesHandedness;
            }
        }
    }
}