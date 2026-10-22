using Game;
using Game.Input;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private static bool TryPickSurface(ProceduralObject editedObject, int partIndex, Camera camera, Vector2 mousePosition, out SurfaceRef hoveredSurface)
        {
            hoveredSurface = default;
            if (editedObject == null || camera == null || partIndex < 0 || partIndex >= editedObject.Parts.Count)
            {
                return false;
            }

            Ray worldRay = camera.ScreenPointToRay(mousePosition);
            Vector3 localOrigin = editedObject.Transform.InverseTransformPoint(worldRay.origin);
            Vector3 localDirection = editedObject.Transform.InverseTransformVector(worldRay.direction);

            MeshPart part = editedObject.Parts[partIndex];
            Vector3[] vertices = part.Mesh.Vertices;
            int[] triangles = part.Mesh.Triangles;
            float bestT = float.PositiveInfinity;
            int bestTriangle = -1;

            for (int triangleId = 0; triangleId < triangles.Length / 3; triangleId++)
            {
                int offset = triangleId * 3;
                int i0 = triangles[offset];
                int i1 = triangles[offset + 1];
                int i2 = triangles[offset + 2];
                if (TryIntersectTriangle(localOrigin, localDirection, vertices[i0], vertices[i1], vertices[i2], out float t) && t < bestT)
                {
                    bestT = t;
                    bestTriangle = triangleId;
                }
            }

            if (bestTriangle < 0)
            {
                return false;
            }

            hoveredSurface = new SurfaceRef(partIndex, bestTriangle);
            return true;
        }

        private static SurfaceRef[] BuildSurfaceSelectionUnit(ProceduralObject editedObject, SurfaceRef hoveredSurface, bool drillIntoFace)
        {
            if (editedObject == null || hoveredSurface.PartIndex < 0 || hoveredSurface.PartIndex >= editedObject.Parts.Count)
            {
                return System.Array.Empty<SurfaceRef>();
            }

            MeshPart part = editedObject.Parts[hoveredSurface.PartIndex];
            int surfaceAreaId = part.GetTriangleSurfaceAreaId(hoveredSurface.TriangleId);

            // Custom Surface Areas are the default selection unit; Alt drills down to faces.
            // Area zero always uses individual faces.
            if (!drillIntoFace && surfaceAreaId > MeshPart.kDefaultSurfaceAreaId)
            {
                int triangleCount = part.Mesh.Triangles.Length / 3;
                int areaTriangleCount = 0;
                for (int triangleId = 0; triangleId < triangleCount; triangleId++)
                {
                    if (part.GetTriangleSurfaceAreaId(triangleId) == surfaceAreaId)
                    {
                        areaTriangleCount++;
                    }
                }

                SurfaceRef[] areaResult = new SurfaceRef[areaTriangleCount];
                int resultIndex = 0;
                for (int triangleId = 0; triangleId < triangleCount; triangleId++)
                {
                    if (part.GetTriangleSurfaceAreaId(triangleId) != surfaceAreaId)
                    {
                        continue;
                    }

                    areaResult[resultIndex++] = new SurfaceRef(hoveredSurface.PartIndex, triangleId);
                }

                return areaResult;
            }

            if (part.TryGetTriangleStructuralFaceId(hoveredSurface.TriangleId, out int faceId) && part.TryGetStructuralFaceTriangles(faceId, out StructuralFaceTriangles faceTriangles))
            {
                SurfaceRef[] faceResult = new SurfaceRef[faceTriangles.Count];
                for (int i = 0; i < faceTriangles.Count; i++)
                {
                    faceResult[i] = new SurfaceRef(hoveredSurface.PartIndex, faceTriangles[i]);
                }

                return faceResult;
            }

            return new[] { hoveredSurface };
        }

        private static bool TryIntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            const float epsilon = 0.000001f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < epsilon)
            {
                return false;
            }

            float inverse = 1f / determinant;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inverse;
            if (u < 0f || u > 1f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }

            t = Vector3.Dot(edge2, q) * inverse;
            return t > epsilon;
        }
    }
}