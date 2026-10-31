// Frozen CPU-pass reference (320839c); do not update alongside the optimized code.
using System;
using System.Collections.Generic;

namespace PO2.Geometry.Topology
{
    // Topology for the faces and edges the user is supposed to interact with.
    // Faces and edges are values~ memberships live in shared packed buffers.
    public sealed partial class ReferenceStructuralTopology
    {
        private readonly StructuralFace[] m_Faces;
        private readonly StructuralEdge[] m_Edges;
        private readonly int[] m_FaceBoundaryOffsets;
        private readonly int[] m_FaceBoundaryEdgeIds;
        private readonly byte[] m_FaceBoundaryForward;
        private readonly int[] m_VertexIncidentEdgeOffsets;
        private readonly int[] m_VertexIncidentEdgeIds;

        public IReadOnlyList<StructuralFace> Faces => m_Faces;
        public IReadOnlyList<StructuralEdge> Edges => m_Edges;
        public int FaceCount => m_Faces.Length;
        public int StructuralEdgeCount => m_Edges.Length;

        public int QuadCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < m_Faces.Length; i++)
                {
                    if (m_Faces[i].IsQuad)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int TriangleCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < m_Faces.Length; i++)
                {
                    if (m_Faces[i].IsTriangle)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private ReferenceStructuralTopology(StructuralFace[] faces, StructuralEdge[] edges, int[] faceBoundaryOffsets, int[] faceBoundaryEdgeIds, byte[] faceBoundaryForward, int[] vertexIncidentEdgeOffsets, int[] vertexIncidentEdgeIds)
        {
            m_Faces = faces;
            m_Edges = edges;
            m_FaceBoundaryOffsets = faceBoundaryOffsets;
            m_FaceBoundaryEdgeIds = faceBoundaryEdgeIds;
            m_FaceBoundaryForward = faceBoundaryForward;
            m_VertexIncidentEdgeOffsets = vertexIncidentEdgeOffsets;
            m_VertexIncidentEdgeIds = vertexIncidentEdgeIds;
        }

        public static ReferenceStructuralTopology Build(MeshTopology meshTopology, int renderVertexCount, IReadOnlyList<StructuralFace> authoredFaces)
        {
            if (meshTopology == null)
            {
                throw new ArgumentNullException(nameof(meshTopology));
            }

            if (renderVertexCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(renderVertexCount));
            }

            if (authoredFaces == null || authoredFaces.Count == 0)
            {
                return Empty;
            }

            StructuralFace[] faces = new StructuralFace[authoredFaces.Count];
            int[] faceBoundaryOffsets = new int[authoredFaces.Count + 1];
            int boundaryCount = 0;

            for (int faceIndex = 0; faceIndex < authoredFaces.Count; faceIndex++)
            {
                StructuralFace face = authoredFaces[faceIndex];

                if (face.Id != faceIndex)
                {
                    throw new InvalidOperationException("Structural face ids must be contiguous and match their " + $"array position. Expected {faceIndex}, got {face.Id}.");
                }

                if (!face.IsTriangle && !face.IsQuad)
                {
                    throw new InvalidOperationException($"Structural face {faceIndex} must contain three or four corners.");
                }

                ValidateFaceRenderVertices(face, renderVertexCount);
                faces[faceIndex] = face;
                boundaryCount = checked(boundaryCount + face.CornerCount);
                faceBoundaryOffsets[faceIndex + 1] = boundaryCount;
            }

            StructuralBoundaryRecord[] records = new StructuralBoundaryRecord[boundaryCount];
            int recordIndex = 0;

            for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
            {
                StructuralFace face = faces[faceIndex];

                for (int boundaryIndex = 0; boundaryIndex < face.CornerCount; boundaryIndex++)
                {
                    int renderA = face.GetRenderVertexIndex(boundaryIndex);
                    int renderB = face.GetRenderVertexIndex((boundaryIndex + 1) % face.CornerCount);
                    int logicalA = meshTopology.GetLogicalVertexIdForRenderVertex(renderA);
                    int logicalB = meshTopology.GetLogicalVertexIdForRenderVertex(renderB);

                    if (logicalA == logicalB)
                    {
                        throw new InvalidOperationException($"Structural face {face.Id} boundary {boundaryIndex} " + "collapses to one logical vertex.");
                    }

                    StructuralEdgeKey key = new StructuralEdgeKey(logicalA, logicalB);
                    records[recordIndex++] = new StructuralBoundaryRecord(key, face.Id, boundaryIndex, logicalA == key.VertexA);
                }
            }

            Array.Sort(records, CompareBoundaryRecords);

            CountEdgeOutput(records, out int edgeCount, out int adjacentFaceCount);

            StructuralEdge[] edges = new StructuralEdge[edgeCount];
            int[] adjacentFaceIds = new int[adjacentFaceCount];
            int[] faceBoundaryEdgeIds = new int[boundaryCount];
            byte[] faceBoundaryForward = new byte[boundaryCount];

            PackEdges(records, faceBoundaryOffsets, edges, adjacentFaceIds, faceBoundaryEdgeIds, faceBoundaryForward);

            BuildIncidentEdges(meshTopology.LogicalVertexCount, edges, out int[] vertexIncidentEdgeOffsets, out int[] vertexIncidentEdgeIds);

            return new ReferenceStructuralTopology(faces, edges, faceBoundaryOffsets, faceBoundaryEdgeIds, faceBoundaryForward, vertexIncidentEdgeOffsets, vertexIncidentEdgeIds);
        }

        // Converted faces already map back to exact render triangles.
        // Reuse the endpoint-sorted logical edges and their triangle spans so a large
        // inferred mesh does not need another global boundary-record sort.
        internal static ReferenceStructuralTopology BuildFromConvertedFaces(MeshTopology meshTopology, int[] triangles, StructuralFace[] faces, StructuralFaceTriangles[] faceTriangles, int[] triangleFaceIds, bool[] suppressedLogicalEdges)
        {
            if (meshTopology == null)
            {
                throw new ArgumentNullException(nameof(meshTopology));
            }

            if (triangles == null)
            {
                throw new ArgumentNullException(nameof(triangles));
            }

            if (faces == null)
            {
                throw new ArgumentNullException(nameof(faces));
            }

            if (faceTriangles == null)
            {
                throw new ArgumentNullException(nameof(faceTriangles));
            }

            if (triangleFaceIds == null)
            {
                throw new ArgumentNullException(nameof(triangleFaceIds));
            }

            if (suppressedLogicalEdges == null)
            {
                throw new ArgumentNullException(nameof(suppressedLogicalEdges));
            }

            if (triangles.Length % 3 != 0 || triangleFaceIds.Length != triangles.Length / 3 || faceTriangles.Length != faces.Length || suppressedLogicalEdges.Length != meshTopology.LogicalEdgeCount)
            {
                throw new InvalidOperationException("Converted structural face inputs have inconsistent counts.");
            }

            ValidateConvertedFaces(triangles, faces, faceTriangles, triangleFaceIds);

            if (faces.Length == 0)
            {
                return Empty;
            }

            int logicalEdgeCount = meshTopology.LogicalEdgeCount;
            int structuralEdgeCount = 0;
            int adjacentFaceCount = 0;

            for (int logicalEdgeId = 0; logicalEdgeId < logicalEdgeCount; logicalEdgeId++)
            {
                LogicalEdge logicalEdge = meshTopology.GetEdge(logicalEdgeId);
                int mappedCount = CountMappedFaces(logicalEdge.TriangleIndices, triangleFaceIds);
                if (suppressedLogicalEdges[logicalEdgeId])
                {
                    ValidateSuppressedLogicalEdge(meshTopology, logicalEdge, faces, faceTriangles, triangleFaceIds);
                    continue;
                }

                if (mappedCount == 0)
                {
                    continue;
                }

                structuralEdgeCount++;
                adjacentFaceCount = checked(adjacentFaceCount + mappedCount);
            }

            StructuralEdge[] edges = new StructuralEdge[structuralEdgeCount];
            int[] adjacentFaceIds = new int[adjacentFaceCount];
            int[] logicalToStructuralEdge = new int[logicalEdgeCount];
            for (int i = 0; i < logicalToStructuralEdge.Length; i++)
            {
                logicalToStructuralEdge[i] = -1;
            }

            int structuralEdgeId = 0;
            int adjacentFaceCursor = 0;
            for (int logicalEdgeId = 0; logicalEdgeId < logicalEdgeCount; logicalEdgeId++)
            {
                LogicalEdge logicalEdge = meshTopology.GetEdge(logicalEdgeId);
                if (suppressedLogicalEdges[logicalEdgeId])
                {
                    continue;
                }

                int mappedCount = CountMappedFaces(logicalEdge.TriangleIndices, triangleFaceIds);
                if (mappedCount == 0)
                {
                    continue;
                }

                int adjacentStart = adjacentFaceCursor;
                for (int i = 0; i < logicalEdge.TriangleIndices.Count; i++)
                {
                    int triangleId = logicalEdge.TriangleIndices[i];
                    int faceId = triangleFaceIds[triangleId];
                    if (faceId < 0 || ContainsMappedFaceBefore(logicalEdge.TriangleIndices, triangleFaceIds, i, faceId))
                    {
                        continue;
                    }

                    adjacentFaceIds[adjacentFaceCursor++] = faceId;
                }

                if (adjacentFaceCursor - adjacentStart != mappedCount)
                {
                    throw new InvalidOperationException("Converted structural edge adjacency count changed while packing.");
                }

                Array.Sort(adjacentFaceIds, adjacentStart, mappedCount);

                edges[structuralEdgeId] = new StructuralEdge(structuralEdgeId, logicalEdge.VertexA, logicalEdge.VertexB, adjacentFaceIds, adjacentStart, mappedCount);
                logicalToStructuralEdge[logicalEdgeId] = structuralEdgeId;
                structuralEdgeId++;
            }

            if (structuralEdgeId != structuralEdgeCount || adjacentFaceCursor != adjacentFaceIds.Length)
            {
                throw new InvalidOperationException("Converted structural edge packing produced inconsistent counts.");
            }

            int[] faceBoundaryOffsets = new int[faces.Length + 1];
            for (int faceId = 0; faceId < faces.Length; faceId++)
            {
                faceBoundaryOffsets[faceId + 1] = checked(faceBoundaryOffsets[faceId] + faces[faceId].CornerCount);
            }

            int[] faceBoundaryEdgeIds = new int[faceBoundaryOffsets[faces.Length]];
            byte[] faceBoundaryForward = new byte[faceBoundaryEdgeIds.Length];
            for (int i = 0; i < faceBoundaryEdgeIds.Length; i++)
            {
                faceBoundaryEdgeIds[i] = -1;
            }

            for (int logicalEdgeId = 0; logicalEdgeId < logicalEdgeCount; logicalEdgeId++)
            {
                int packedEdgeId = logicalToStructuralEdge[logicalEdgeId];
                if (packedEdgeId < 0)
                {
                    continue;
                }

                LogicalEdge logicalEdge = meshTopology.GetEdge(logicalEdgeId);
                for (int i = 0; i < logicalEdge.TriangleIndices.Count; i++)
                {
                    int triangleId = logicalEdge.TriangleIndices[i];
                    int faceId = triangleFaceIds[triangleId];
                    if (faceId < 0 || ContainsMappedFaceBefore(logicalEdge.TriangleIndices, triangleFaceIds, i, faceId))
                    {
                        continue;
                    }

                    int boundaryIndex = FindFaceBoundary(meshTopology, faces[faceId], logicalEdge.VertexA, logicalEdge.VertexB, out bool forward);
                    if (boundaryIndex < 0)
                    {
                        throw new InvalidOperationException($"Converted structural face {faceId} does not contain " + $"logical edge {logicalEdgeId}.");
                    }

                    int flatBoundaryIndex = faceBoundaryOffsets[faceId] + boundaryIndex;
                    if (faceBoundaryEdgeIds[flatBoundaryIndex] >= 0 && faceBoundaryEdgeIds[flatBoundaryIndex] != packedEdgeId)
                    {
                        throw new InvalidOperationException($"Converted structural face {faceId} boundary " + $"{boundaryIndex} maps to more than one edge.");
                    }

                    faceBoundaryEdgeIds[flatBoundaryIndex] = packedEdgeId;
                    faceBoundaryForward[flatBoundaryIndex] = forward ? (byte)1 : (byte)0;
                }
            }

            for (int i = 0; i < faceBoundaryEdgeIds.Length; i++)
            {
                if (faceBoundaryEdgeIds[i] < 0)
                {
                    throw new InvalidOperationException($"Converted structural face boundary {i} has no edge.");
                }
            }

            BuildIncidentEdges(meshTopology.LogicalVertexCount, edges, out int[] vertexIncidentEdgeOffsets, out int[] vertexIncidentEdgeIds);

            return new ReferenceStructuralTopology(faces, edges, faceBoundaryOffsets, faceBoundaryEdgeIds, faceBoundaryForward, vertexIncidentEdgeOffsets, vertexIncidentEdgeIds);
        }

        public static ReferenceStructuralTopology Empty { get; } = new ReferenceStructuralTopology(Array.Empty<StructuralFace>(), Array.Empty<StructuralEdge>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<byte>(), Array.Empty<int>(), Array.Empty<int>());

        public StructuralFace GetFace(int faceId)
        {
            if (faceId < 0 || faceId >= m_Faces.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(faceId));
            }

            return m_Faces[faceId];
        }

        public bool TryGetQuad(int faceId, out StructuralFace quad)
        {
            quad = default;
            if (faceId < 0 || faceId >= m_Faces.Length || !m_Faces[faceId].IsQuad)
            {
                return false;
            }

            quad = m_Faces[faceId];
            return true;
        }

        public StructuralEdge GetEdge(int edgeId)
        {
            if (edgeId < 0 || edgeId >= m_Edges.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(edgeId));
            }

            return m_Edges[edgeId];
        }

        public int GetBoundaryEdgeId(int faceId, int boundaryIndex)
        {
            int flatIndex = GetBoundaryFlatIndex(faceId, boundaryIndex);
            return m_FaceBoundaryEdgeIds[flatIndex];
        }

        public int GetOppositeBoundaryEdgeId(int faceId, int boundaryIndex)
        {
            StructuralFace face = GetFace(faceId);
            if (!face.IsQuad)
            {
                throw new InvalidOperationException("Only structural quads have an opposite boundary edge.");
            }

            return GetBoundaryEdgeId(faceId, (boundaryIndex + 2) % 4);
        }

        public bool IsBoundaryForward(int faceId, int boundaryIndex)
        {
            int flatIndex = GetBoundaryFlatIndex(faceId, boundaryIndex);
            return m_FaceBoundaryForward[flatIndex] != 0;
        }

        public bool TryGetBoundaryIndex(int faceId, int structuralEdgeId, out int boundaryIndex)
        {
            boundaryIndex = -1;
            if (faceId < 0 || faceId >= m_Faces.Length || structuralEdgeId < 0 || structuralEdgeId >= m_Edges.Length)
            {
                return false;
            }

            int start = m_FaceBoundaryOffsets[faceId];
            int end = m_FaceBoundaryOffsets[faceId + 1];
            for (int i = start; i < end; i++)
            {
                if (m_FaceBoundaryEdgeIds[i] == structuralEdgeId)
                {
                    boundaryIndex = i - start;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetOtherAdjacentFace(int structuralEdgeId, int currentFaceId, out int otherFaceId)
        {
            otherFaceId = -1;
            if (structuralEdgeId < 0 || structuralEdgeId >= m_Edges.Length)
            {
                return false;
            }

            StructuralEdge edge = m_Edges[structuralEdgeId];
            if (!edge.IsManifold || edge.AdjacentFaceCount != 2)
            {
                return false;
            }

            if (edge.AdjacentFaceIds[0] == currentFaceId)
            {
                otherFaceId = edge.AdjacentFaceIds[1];
                return true;
            }

            if (edge.AdjacentFaceIds[1] == currentFaceId)
            {
                otherFaceId = edge.AdjacentFaceIds[0];
                return true;
            }

            return false;
        }

        public bool TryGetEdgeByLogicalVertices(int logicalVertexA, int logicalVertexB, out StructuralEdge edge)
        {
            edge = default;
            if (logicalVertexA < 0 || logicalVertexB < 0 || logicalVertexA == logicalVertexB)
            {
                return false;
            }

            int vertexA = Math.Min(logicalVertexA, logicalVertexB);
            int vertexB = Math.Max(logicalVertexA, logicalVertexB);
            int low = 0;
            int high = m_Edges.Length - 1;

            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                StructuralEdge candidate = m_Edges[middle];
                int comparison = candidate.LogicalVertexA.CompareTo(vertexA);
                if (comparison == 0)
                {
                    comparison = candidate.LogicalVertexB.CompareTo(vertexB);
                }

                if (comparison == 0)
                {
                    edge = candidate;
                    return true;
                }

                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return false;
        }

        public IndexSpan GetIncidentEdgeIds(int logicalVertexId)
        {
            if (logicalVertexId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalVertexId));
            }

            if (m_VertexIncidentEdgeOffsets.Length == 0)
            {
                return new IndexSpan(Array.Empty<int>());
            }

            if (logicalVertexId >= m_VertexIncidentEdgeOffsets.Length - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalVertexId));
            }

            int start = m_VertexIncidentEdgeOffsets[logicalVertexId];
            int end = m_VertexIncidentEdgeOffsets[logicalVertexId + 1];
            return new IndexSpan(m_VertexIncidentEdgeIds, start, end - start);
        }

        public StructuralFace[] CloneFaces()
        {
            return (StructuralFace[])m_Faces.Clone();
        }

        private int GetBoundaryFlatIndex(int faceId, int boundaryIndex)
        {
            if (faceId < 0 || faceId >= m_Faces.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(faceId));
            }

            int start = m_FaceBoundaryOffsets[faceId];
            int count = m_FaceBoundaryOffsets[faceId + 1] - start;
            if (boundaryIndex < 0 || boundaryIndex >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(boundaryIndex));
            }

            return start + boundaryIndex;
        }

        private static void ValidateFaceRenderVertices(StructuralFace face, int renderVertexCount)
        {
            for (int cornerIndex = 0; cornerIndex < face.CornerCount; cornerIndex++)
            {
                int renderIndex = face.GetRenderVertexIndex(cornerIndex);
                if (renderIndex < 0 || renderIndex >= renderVertexCount)
                {
                    throw new InvalidOperationException($"Structural face {face.Id} references render vertex " + $"{renderIndex}, but the mesh has {renderVertexCount} vertices.");
                }
            }
        }

        private static void ValidateConvertedFaces(int[] triangles, StructuralFace[] faces, StructuralFaceTriangles[] faceTriangles, int[] triangleFaceIds)
        {
            bool[] claimedTriangles = new bool[triangleFaceIds.Length];
            for (int faceId = 0; faceId < faces.Length; faceId++)
            {
                StructuralFace face = faces[faceId];
                StructuralFaceTriangles mapping = faceTriangles[faceId];
                if (face.Id != faceId || (!face.IsTriangle && !face.IsQuad) || mapping.Count != (face.IsQuad ? 2 : 1) || mapping.TriangleA < 0 || mapping.TriangleA >= triangleFaceIds.Length || triangleFaceIds[mapping.TriangleA] != faceId || claimedTriangles[mapping.TriangleA])
                {
                    throw new InvalidOperationException($"Converted structural face {faceId} has an invalid triangle mapping.");
                }

                if (!MatchesOrientedTriangle(triangles, mapping.TriangleA, face.GetRenderVertexIndex(0), face.GetRenderVertexIndex(1), face.GetRenderVertexIndex(2)))
                {
                    throw new InvalidOperationException($"Converted structural face {faceId} does not preserve " + "its first render triangle winding.");
                }

                claimedTriangles[mapping.TriangleA] = true;
                if (!face.IsQuad)
                {
                    continue;
                }

                if (mapping.TriangleB < 0 || mapping.TriangleB >= triangleFaceIds.Length || mapping.TriangleB == mapping.TriangleA || triangleFaceIds[mapping.TriangleB] != faceId || claimedTriangles[mapping.TriangleB] || !MatchesOrientedTriangle(triangles, mapping.TriangleB, face.GetRenderVertexIndex(0), face.GetRenderVertexIndex(2), face.GetRenderVertexIndex(3)))
                {
                    throw new InvalidOperationException($"Converted structural quad {faceId} has an invalid " + "second triangle mapping.");
                }

                claimedTriangles[mapping.TriangleB] = true;
            }

            for (int triangleId = 0; triangleId < triangleFaceIds.Length; triangleId++)
            {
                int faceId = triangleFaceIds[triangleId];
                if (faceId < -1 || faceId >= faces.Length || (faceId >= 0 && !claimedTriangles[triangleId]))
                {
                    throw new InvalidOperationException($"Converted triangle {triangleId} has an invalid structural face mapping.");
                }
            }
        }

        private static int CountMappedFaces(IndexSpan triangleIds, int[] triangleFaceIds)
        {
            int count = 0;
            for (int i = 0; i < triangleIds.Count; i++)
            {
                int triangleId = triangleIds[i];
                if (triangleId < 0 || triangleId >= triangleFaceIds.Length)
                {
                    throw new InvalidOperationException($"Logical edge references invalid triangle {triangleId}.");
                }

                int faceId = triangleFaceIds[triangleId];
                if (faceId < 0 || ContainsMappedFaceBefore(triangleIds, triangleFaceIds, i, faceId))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private static bool ContainsMappedFaceBefore(IndexSpan triangleIds, int[] triangleFaceIds, int exclusiveEnd, int faceId)
        {
            for (int i = 0; i < exclusiveEnd; i++)
            {
                int triangleId = triangleIds[i];
                if (triangleId >= 0 && triangleId < triangleFaceIds.Length && triangleFaceIds[triangleId] == faceId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateSuppressedLogicalEdge(MeshTopology meshTopology, LogicalEdge logicalEdge, StructuralFace[] faces, StructuralFaceTriangles[] faceTriangles, int[] triangleFaceIds)
        {
            if (logicalEdge.TriangleIndices.Count != 2)
            {
                throw new InvalidOperationException($"Suppressed logical edge {logicalEdge.Id} is not shared by two triangles.");
            }

            int triangleA = logicalEdge.TriangleIndices[0];
            int triangleB = logicalEdge.TriangleIndices[1];
            if (triangleA < 0 || triangleA >= triangleFaceIds.Length || triangleB < 0 || triangleB >= triangleFaceIds.Length)
            {
                throw new InvalidOperationException($"Suppressed logical edge {logicalEdge.Id} has invalid triangle adjacency.");
            }

            int faceId = triangleFaceIds[triangleA];
            if (faceId < 0 || faceId != triangleFaceIds[triangleB] || faceId >= faces.Length || !faces[faceId].IsQuad)
            {
                throw new InvalidOperationException($"Suppressed logical edge {logicalEdge.Id} is not one quad diagonal.");
            }

            StructuralFaceTriangles mapping = faceTriangles[faceId];
            if (mapping.Count != 2 || !((mapping.TriangleA == triangleA && mapping.TriangleB == triangleB) || (mapping.TriangleA == triangleB && mapping.TriangleB == triangleA)))
            {
                throw new InvalidOperationException($"Suppressed logical edge {logicalEdge.Id} does not match its quad triangles.");
            }

            StructuralFace face = faces[faceId];
            int logical0 = meshTopology.GetLogicalVertexIdForRenderVertex(face.GetRenderVertexIndex(0));
            int logical2 = meshTopology.GetLogicalVertexIdForRenderVertex(face.GetRenderVertexIndex(2));
            if (Math.Min(logical0, logical2) != logicalEdge.VertexA || Math.Max(logical0, logical2) != logicalEdge.VertexB)
            {
                throw new InvalidOperationException($"Suppressed logical edge {logicalEdge.Id} is not the quad's render diagonal.");
            }
        }

        private static bool MatchesOrientedTriangle(int[] triangles, int triangleId, int vertex0, int vertex1, int vertex2)
        {
            int offset = checked(triangleId * 3);
            int triangle0 = triangles[offset];
            int triangle1 = triangles[offset + 1];
            int triangle2 = triangles[offset + 2];
            return (triangle0 == vertex0 && triangle1 == vertex1 && triangle2 == vertex2) || (triangle0 == vertex1 && triangle1 == vertex2 && triangle2 == vertex0) || (triangle0 == vertex2 && triangle1 == vertex0 && triangle2 == vertex1);
        }

        private static int FindFaceBoundary(MeshTopology meshTopology, StructuralFace face, int logicalVertexA, int logicalVertexB, out bool forward)
        {
            forward = false;
            for (int boundaryIndex = 0; boundaryIndex < face.CornerCount; boundaryIndex++)
            {
                int renderA = face.GetRenderVertexIndex(boundaryIndex);
                int renderB = face.GetRenderVertexIndex((boundaryIndex + 1) % face.CornerCount);
                int boundaryLogicalA = meshTopology.GetLogicalVertexIdForRenderVertex(renderA);
                int boundaryLogicalB = meshTopology.GetLogicalVertexIdForRenderVertex(renderB);

                if (Math.Min(boundaryLogicalA, boundaryLogicalB) != logicalVertexA || Math.Max(boundaryLogicalA, boundaryLogicalB) != logicalVertexB)
                {
                    continue;
                }

                forward = boundaryLogicalA == logicalVertexA;
                return boundaryIndex;
            }

            return -1;
        }

        private static int CompareBoundaryRecords(StructuralBoundaryRecord left, StructuralBoundaryRecord right)
        {
            int comparison = left.Key.VertexA.CompareTo(right.Key.VertexA);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Key.VertexB.CompareTo(right.Key.VertexB);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.FaceId.CompareTo(right.FaceId);
            return comparison != 0 ? comparison : left.BoundaryIndex.CompareTo(right.BoundaryIndex);
        }

        private static void CountEdgeOutput(StructuralBoundaryRecord[] records, out int edgeCount, out int adjacentFaceCount)
        {
            edgeCount = 0;
            adjacentFaceCount = 0;
            int recordIndex = 0;

            while (recordIndex < records.Length)
            {
                StructuralEdgeKey key = records[recordIndex].Key;
                edgeCount++;
                int lastFaceId = -1;

                while (recordIndex < records.Length && records[recordIndex].Key.Equals(key))
                {
                    int faceId = records[recordIndex].FaceId;
                    if (faceId != lastFaceId)
                    {
                        adjacentFaceCount++;
                        lastFaceId = faceId;
                    }

                    recordIndex++;
                }
            }
        }

        private static void PackEdges(StructuralBoundaryRecord[] records, int[] faceBoundaryOffsets, StructuralEdge[] edges, int[] adjacentFaceIds, int[] faceBoundaryEdgeIds, byte[] faceBoundaryForward)
        {
            int recordIndex = 0;
            int edgeIndex = 0;
            int adjacentFaceIndex = 0;

            while (recordIndex < records.Length)
            {
                StructuralEdgeKey key = records[recordIndex].Key;
                int adjacentStart = adjacentFaceIndex;
                int lastFaceId = -1;

                while (recordIndex < records.Length && records[recordIndex].Key.Equals(key))
                {
                    StructuralBoundaryRecord record = records[recordIndex++];
                    int flatBoundaryIndex = faceBoundaryOffsets[record.FaceId] + record.BoundaryIndex;

                    faceBoundaryEdgeIds[flatBoundaryIndex] = edgeIndex;
                    faceBoundaryForward[flatBoundaryIndex] = record.Forward ? (byte)1 : (byte)0;

                    if (record.FaceId != lastFaceId)
                    {
                        adjacentFaceIds[adjacentFaceIndex++] = record.FaceId;
                        lastFaceId = record.FaceId;
                    }
                }

                edges[edgeIndex] = new StructuralEdge(edgeIndex, key.VertexA, key.VertexB, adjacentFaceIds, adjacentStart, adjacentFaceIndex - adjacentStart);
                edgeIndex++;
            }
        }

        private static void BuildIncidentEdges(int logicalVertexCount, StructuralEdge[] edges, out int[] incidentOffsets, out int[] incidentEdgeIds)
        {
            incidentOffsets = new int[logicalVertexCount + 1];
            for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
            {
                StructuralEdge edge = edges[edgeIndex];
                incidentOffsets[edge.LogicalVertexA + 1]++;
                incidentOffsets[edge.LogicalVertexB + 1]++;
            }

            for (int vertexId = 0; vertexId < logicalVertexCount; vertexId++)
            {
                incidentOffsets[vertexId + 1] = checked(incidentOffsets[vertexId] + incidentOffsets[vertexId + 1]);
            }

            incidentEdgeIds = new int[incidentOffsets[logicalVertexCount]];
            int[] cursors = new int[logicalVertexCount];
            Array.Copy(incidentOffsets, cursors, logicalVertexCount);

            for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
            {
                StructuralEdge edge = edges[edgeIndex];
                incidentEdgeIds[cursors[edge.LogicalVertexA]++] = edgeIndex;
                incidentEdgeIds[cursors[edge.LogicalVertexB]++] = edgeIndex;
            }
        }

        private readonly struct StructuralBoundaryRecord
        {
            public StructuralEdgeKey Key { get; }
            public int FaceId { get; }
            public int BoundaryIndex { get; }
            public bool Forward { get; }

            public StructuralBoundaryRecord(StructuralEdgeKey key, int faceId, int boundaryIndex, bool forward)
            {
                Key = key;
                FaceId = faceId;
                BoundaryIndex = boundaryIndex;
                Forward = forward;
            }
        }

        private readonly struct StructuralEdgeKey : IEquatable<StructuralEdgeKey>
        {
            public int VertexA { get; }
            public int VertexB { get; }

            public StructuralEdgeKey(int vertexA, int vertexB)
            {
                VertexA = Math.Min(vertexA, vertexB);
                VertexB = Math.Max(vertexA, vertexB);
            }

            public bool Equals(StructuralEdgeKey other)
            {
                return VertexA == other.VertexA && VertexB == other.VertexB;
            }

            public override bool Equals(object obj)
            {
                return obj is StructuralEdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (VertexA * 397) ^ VertexB;
                }
            }
        }
    }
}