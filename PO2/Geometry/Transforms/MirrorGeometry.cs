using System.Threading;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Localization;
using PO2.Persistence.Model;

namespace PO2.Geometry.Transforms
{
    internal static class MirrorGeometry
    {
        // Reflect in world directions, but do the math near the local origin.
        // Large city coordinates should not affect the result.
        internal static Matrix4x4 CreateLocalReflection(ObjectTransform transform, Vector3 localPivot, Vector3 worldAxis)
        {
            Vector3 n = worldAxis.normalized;
            Matrix4x4 reflection = Matrix4x4.identity;
            reflection.m00 -= 2f * n.x * n.x;
            reflection.m01 = -2f * n.x * n.y;
            reflection.m02 = -2f * n.x * n.z;
            reflection.m10 = -2f * n.y * n.x;
            reflection.m11 -= 2f * n.y * n.y;
            reflection.m12 = -2f * n.y * n.z;
            reflection.m20 = -2f * n.z * n.x;
            reflection.m21 = -2f * n.z * n.y;
            reflection.m22 -= 2f * n.z * n.z;

            Matrix4x4 basis = Matrix4x4.TRS(Vector3.zero, transform.Rotation, transform.Scale);
            Matrix4x4 local = basis.inverse * reflection * basis;
            Vector3 offset = localPivot - local.MultiplyVector(localPivot);
            local.m03 = offset.x;
            local.m13 = offset.y;
            local.m23 = offset.z;
            return local;
        }

        internal static PersistencePartData TransformCapture(PersistencePartData source, Matrix4x4 reflection, bool preservePivot, CancellationToken token, StructuralFaceTriangles[] cachedFaces = null)
        {
            token.ThrowIfCancellationRequested();
            var positions = new Vector3[source.Vertices.Length];
            var normals = source.Normals == null ? null : new Vector3[source.Normals.Length];
            var tangents = source.Tangents == null ? null : new Vector4[source.Tangents.Length];
            var triangles = new int[source.Triangles.Length];
            MirrorKernels.Transform(source, reflection, positions, normals, tangents, triangles, token);

            // Triangle records for a quad need to swap places as well as reverse
            // winding. Keep the existing diagonal and nonconsecutive triangle IDs.
            bool hasQuads = false;
            foreach (var face in source.StructuralFaces)
                if (face.IsQuad)
                {
                    hasQuads = true;
                    break;
                }

            StructuralFaceTriangles[] faceTriangles = cachedFaces;
            if (hasQuads && faceTriangles == null) StructuralFaceTriangleResolver.Build(source.StructuralFaces, source.Triangles, out faceTriangles, out _);
            var faces = new StructuralFace[source.StructuralFaces.Length];
            for (int i = 0; i < faces.Length; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                var face = source.StructuralFaces[i];
                int a = face.GetRenderVertexIndex(0), b = face.GetRenderVertexIndex(1), c = face.GetRenderVertexIndex(2);
                faces[i] = face.IsQuad ? new StructuralFace(face.Id, a, face.GetRenderVertexIndex(3), c, b) : new StructuralFace(face.Id, a, c, b);
                if (!face.IsQuad) continue;
                int left = faceTriangles[i].TriangleA * 3, right = faceTriangles[i].TriangleB * 3;
                for (int corner = 0; corner < 3; corner++)
                {
                    int swap = triangles[left + corner];
                    triangles[left + corner] = triangles[right + corner];
                    triangles[right + corner] = swap;
                }
            }

            Vector3? pivot = source.LocalPivot;
            if (!preservePivot && pivot.HasValue) pivot = reflection.MultiplyPoint3x4(pivot.Value);
            // The capture is detached from live geometry. Unchanged streams are
            // shared during preparation; all capture reads finish before publication.
            return PersistencePartData.FromDecoded(source.Name, positions, normals, tangents, source.UV0, source.VertexColors, source.TexCoord1, source.TexCoord3, triangles, source.RenderToLogical, source.LogicalVertexCount, faces, source.StructuralTopologySource, source.SurfaceAreas, source.TriangleSurfaceAreaIds, source.NextSurfaceAreaId, pivot);
        }

        internal static bool TryPrepare(ProceduralObject obj, int partIndex, Vector3 worldAxis, out MeshPart[] parts, out string reason)
        {
            parts = null;
            reason = string.Empty;
            if (!TryValidate(obj, partIndex, worldAxis, out LocalizedText validation))
            {
                reason = validation.ToString();
                return false;
            }

            Vector3 pivot = partIndex < 0 ? obj.LocalPivot : obj.Parts[partIndex].LocalPivot;
            Matrix4x4 reflection = CreateLocalReflection(obj.Transform, pivot, worldAxis);
            int first = partIndex < 0 ? 0 : partIndex;
            var prepared = new MeshPart[partIndex < 0 ? obj.Parts.Count : 1];
            for (int i = 0; i < prepared.Length; i++)
            {
                var source = obj.Parts[first + i];
                if (!PartGeometryTransform.TryCloneTransformed(source, reflection, source.Name, out prepared[i], out reason)) return false;
                // The target's pivot lies on the plane. Keep it exactly unchanged.
                if (partIndex >= 0) prepared[i].SetLocalPivot(source.LocalPivot);
            }

            parts = prepared;
            return true;
        }

        internal static bool TryValidate(ProceduralObject obj, int partIndex, Vector3 worldAxis, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            if (obj == null || obj.IsLocked || partIndex < -1 || partIndex >= obj.Parts.Count || obj.Parts.Count == 0)
            {
                reason = LocalizedText.For("PO2.Object.Mirror.Error.TargetUnavailable");
                return false;
            }

            if (!obj.CanEditGeometry(partIndex, out reason)) return false;
            float length = worldAxis.sqrMagnitude;
            if (float.IsNaN(length) || float.IsInfinity(length) || length < 0.000001f)
            {
                reason = LocalizedText.For("PO2.Object.Mirror.Error.InvalidAxis");
                return false;
            }

            return true;
        }
    }
}