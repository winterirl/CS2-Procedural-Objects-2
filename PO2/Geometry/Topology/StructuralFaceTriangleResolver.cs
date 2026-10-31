using System;
using System.Collections.Generic;

namespace PO2.Geometry.Topology
{
    internal static class StructuralFaceTriangleResolver
    {
        internal static bool HasDuplicateTriangles(int[] triangles, System.Threading.CancellationToken token)
        {
            var seen = new HashSet<OrientedTriangleKey>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                if (!seen.Add(new OrientedTriangleKey(triangles[i], triangles[i + 1], triangles[i + 2]))) return true;
            }

            return false;
        }

        public static void Build(IReadOnlyList<StructuralFace> faces, int[] triangles, out StructuralFaceTriangles[] faceTriangles, out int[] triangleFaceIds)
        {
            if (faces == null)
            {
                throw new ArgumentNullException(nameof(faces));
            }

            if (triangles == null)
            {
                throw new ArgumentNullException(nameof(triangles));
            }

            if (triangles.Length % 3 != 0)
            {
                throw new InvalidOperationException("The structural face map requires a triangle index buffer.");
            }

            int triangleCount = triangles.Length / 3;
            faceTriangles = new StructuralFaceTriangles[faces.Count];
            triangleFaceIds = new int[triangleCount];
            if (TryBuildSequentialMap(faces, triangles, faceTriangles, triangleFaceIds)) return;

            for (int triangleId = 0; triangleId < triangleFaceIds.Length; triangleId++)
            {
                triangleFaceIds[triangleId] = -1;
            }
            if (faces.Count == 0) return;

            // One linked chain per oriented triangle key avoids a List<int> allocation
            // for every unique triangle, while still handling exact duplicate records in stable triangle order.
            Dictionary<OrientedTriangleKey, int> availableHeads = new Dictionary<OrientedTriangleKey, int>(triangleCount);
            int[] nextTriangleIds = new int[triangleCount];

            for (int triangleId = triangleCount - 1; triangleId >= 0; triangleId--)
            {
                int offset = triangleId * 3;
                OrientedTriangleKey key = new OrientedTriangleKey(triangles[offset], triangles[offset + 1], triangles[offset + 2]);
                int next = availableHeads.TryGetValue(key, out int currentHead) ? currentHead : -1;
                nextTriangleIds[triangleId] = next;
                availableHeads[key] = triangleId;
            }

            for (int faceId = 0; faceId < faces.Count; faceId++)
            {
                StructuralFace face = faces[faceId];
                if (face.Id != faceId || (!face.IsTriangle && !face.IsQuad))
                {
                    throw new InvalidOperationException($"Structural face {faceId} is not a valid dense face record.");
                }

                OrientedTriangleKey firstKey = new OrientedTriangleKey(face.GetRenderVertexIndex(0), face.GetRenderVertexIndex(1), face.GetRenderVertexIndex(2));

                if (!TryTakeTriangle(availableHeads, nextTriangleIds, firstKey, out int firstTriangleId))
                {
                    throw new InvalidOperationException($"Structural face {face.Id} does not match the triangle stream.");
                }

                if (face.IsTriangle)
                {
                    faceTriangles[faceId] = new StructuralFaceTriangles(firstTriangleId, -1, 1);
                    triangleFaceIds[firstTriangleId] = faceId;
                    continue;
                }

                OrientedTriangleKey secondKey = new OrientedTriangleKey(face.GetRenderVertexIndex(0), face.GetRenderVertexIndex(2), face.GetRenderVertexIndex(3));

                if (!TryTakeTriangle(availableHeads, nextTriangleIds, secondKey, out int secondTriangleId))
                {
                    ReturnTriangle(availableHeads, nextTriangleIds, firstKey, firstTriangleId);
                    throw new InvalidOperationException($"Structural quad {face.Id} does not match both triangles in the stream.");
                }

                faceTriangles[faceId] = new StructuralFaceTriangles(firstTriangleId, secondTriangleId, 2);
                triangleFaceIds[firstTriangleId] = faceId;
                triangleFaceIds[secondTriangleId] = faceId;
            }
        }

        private static bool TryBuildSequentialMap(IReadOnlyList<StructuralFace> faces,
            int[] triangles, StructuralFaceTriangles[] faceTriangles, int[] triangleFaceIds)
        {
            int triangleCount = triangleFaceIds.Length;
            int cursor = 0;
            // Prove the whole stream before writing anything. A partial match
            // must leave the ordinary duplicate-aware resolver a clean slate.
            for (int i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                if (face.Id != i || (!face.IsTriangle && !face.IsQuad)) return false;
                int count = face.IsQuad ? 2 : 1;
                if (count > triangleCount - cursor) return false;
                int offset = cursor * 3;
                int a = face.GetRenderVertexIndex(0);
                int b = face.GetRenderVertexIndex(1);
                int c = face.GetRenderVertexIndex(2);
                if (triangles[offset] != a || triangles[offset + 1] != b || triangles[offset + 2] != c) return false;
                if (face.IsQuad && (triangles[offset + 3] != a || triangles[offset + 4] != c
                    || triangles[offset + 5] != face.GetRenderVertexIndex(3))) return false;
                cursor += count;
            }
            if (cursor != triangleCount) return false;

            cursor = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                bool quad = faces[i].IsQuad;
                faceTriangles[i] = new StructuralFaceTriangles(cursor, quad ? cursor + 1 : -1, quad ? 2 : 1);
                triangleFaceIds[cursor++] = i;
                if (quad) triangleFaceIds[cursor++] = i;
            }
            return true;
        }

        private static bool TryTakeTriangle(Dictionary<OrientedTriangleKey, int> availableHeads, int[] nextTriangleIds, OrientedTriangleKey key, out int triangleId)
        {
            if (!availableHeads.TryGetValue(key, out triangleId) || triangleId < 0)
            {
                triangleId = -1;
                return false;
            }

            availableHeads[key] = nextTriangleIds[triangleId];
            return true;
        }

        private static void ReturnTriangle(Dictionary<OrientedTriangleKey, int> availableHeads, int[] nextTriangleIds, OrientedTriangleKey key, int triangleId)
        {
            int currentHead = availableHeads.TryGetValue(key, out int head) ? head : -1;
            nextTriangleIds[triangleId] = currentHead;
            availableHeads[key] = triangleId;
        }

        private readonly struct OrientedTriangleKey : IEquatable<OrientedTriangleKey>
        {
            public int Vertex0 { get; }
            public int Vertex1 { get; }
            public int Vertex2 { get; }

            public OrientedTriangleKey(int vertex0, int vertex1, int vertex2)
            {
                int best0 = vertex0;
                int best1 = vertex1;
                int best2 = vertex2;

                if (IsLess(vertex1, vertex2, vertex0, best0, best1, best2))
                {
                    best0 = vertex1;
                    best1 = vertex2;
                    best2 = vertex0;
                }

                if (IsLess(vertex2, vertex0, vertex1, best0, best1, best2))
                {
                    best0 = vertex2;
                    best1 = vertex0;
                    best2 = vertex1;
                }

                Vertex0 = best0;
                Vertex1 = best1;
                Vertex2 = best2;
            }

            public bool Equals(OrientedTriangleKey other)
            {
                return Vertex0 == other.Vertex0 && Vertex1 == other.Vertex1 && Vertex2 == other.Vertex2;
            }

            public override bool Equals(object obj)
            {
                return obj is OrientedTriangleKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Vertex0;
                    hash = (hash * 397) ^ Vertex1;
                    hash = (hash * 397) ^ Vertex2;
                    return hash;
                }
            }

            private static bool IsLess(int left0, int left1, int left2, int right0, int right1, int right2)
            {
                if (left0 != right0)
                {
                    return left0 < right0;
                }

                if (left1 != right1)
                {
                    return left1 < right1;
                }

                return left2 < right2;
            }
        }
    }
}