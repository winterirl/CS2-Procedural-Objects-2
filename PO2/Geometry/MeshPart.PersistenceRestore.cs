using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Conversion;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Persistence.PreparedTopology;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Geometry
{
    public sealed partial class MeshPart
    {
        internal PartTopologyCapture CapturePersistenceTopology() => new PartTopologyCapture(this, m_StructuralFaceTriangles, m_TriangleStructuralFaceIds);

        private MeshPart(string name, MeshData mesh, StructuralFace[] structuralFaces, List<SurfaceArea> surfaceAreas, int[] triangleSurfaceAreaIds, int nextSurfaceAreaId, MeshTopology restoredTopology = null, StructuralTopologySource? topologySource = null, Vector3? localPivot = null, ValidatedPartTopology prepared = null)
        {
            Name = NormalizeName(name);
            Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
            // restoredTopology is supplied by persistence/clone paths.
            // Falling back to Build() is only correct for new data that has never had an explicit render-to-logical vertex mapping.
            Topology = restoredTopology ?? MeshTopology.Build(mesh);
            StructuralFace[] restoredFaces = structuralFaces ?? Array.Empty<StructuralFace>();
            // Object schema v3 stores this source explicitly. Schema v2 data
            // still uses the old triangle-only signature so converted POs
            // can be upgraded after their original ECS entities are gone.
            StructuralTopologySource = topologySource ?? DetectRestoredStructuralTopologySource(mesh, Topology, restoredFaces);

            bool upgradeLegacyConvertedTopology = !topologySource.HasValue && StructuralTopologySource == StructuralTopologySource.ConvertedTriangleBaseline;
            bool hasPreparedStructuralMap = false;
            if (prepared != null)
            {
                StructuralTopology = StructuralTopology.RestorePacked(prepared.Data, restoredFaces);
                if (prepared.CanonicalMaps)
                {
                    var packed = prepared.Data;
                    m_StructuralFaceTriangles = new StructuralFaceTriangles[restoredFaces.Length];
                    for (int i = 0; i < restoredFaces.Length; i++)
                        m_StructuralFaceTriangles[i] = new StructuralFaceTriangles(packed[20][i], packed[21][i], restoredFaces[i].IsQuad ? 2 : 1);
                    m_TriangleStructuralFaceIds = packed[22];
                    hasPreparedStructuralMap = true;
                }
            }
            else if (upgradeLegacyConvertedTopology)
            {
                ConvertedStructuralTopologyResult structuralResult = ConvertedStructuralTopologyBuilder.Build(mesh, Topology, triangleSurfaceAreaIds, null);
                StructuralTopology = structuralResult.Topology;
                m_StructuralFaceTriangles = structuralResult.FaceTriangles;
                m_TriangleStructuralFaceIds = structuralResult.TriangleFaceIds;
                StructuralTopologySource = structuralResult.TopologySource;
                hasPreparedStructuralMap = true;
            }
            else if (StructuralTopologySource != PO2.Geometry.Topology.StructuralTopologySource.Authored)
            {
                ConvertedStructuralTopologyResult structuralResult = ConvertedStructuralTopologyBuilder.RestorePersisted(mesh, Topology, restoredFaces, StructuralTopologySource);
                StructuralTopology = structuralResult.Topology;
                m_StructuralFaceTriangles = structuralResult.FaceTriangles;
                m_TriangleStructuralFaceIds = structuralResult.TriangleFaceIds;
                hasPreparedStructuralMap = true;
            }
            else
            {
                StructuralTopology = StructuralTopology.Build(Topology, mesh.Vertices.Length, restoredFaces);
            }

            m_SurfaceAreas = surfaceAreas ?? throw new ArgumentNullException(nameof(surfaceAreas));
            m_TriangleSurfaceAreaIds = triangleSurfaceAreaIds ?? throw new ArgumentNullException(nameof(triangleSurfaceAreaIds));
            m_NextSurfaceAreaId = nextSurfaceAreaId;

            ValidateRestoredSurfaceAreaIdentity();
            if (!hasPreparedStructuralMap)
            {
                RebuildStructuralFaceTriangleMap();
            }

            ValidateSurfaceAreaState();
            LocalPivot = localPivot ?? CalculateDefaultLocalPivot();
            if (!IsFinite(LocalPivot))
            {
                throw new ArgumentException("A mesh part pivot must be finite.", nameof(localPivot));
            }
        }

        internal static MeshPart Restore(string name, MeshData mesh, int[] renderToLogical, int logicalVertexCount, StructuralFace[] structuralFaces, List<SurfaceArea> surfaceAreas, int[] triangleSurfaceAreaIds, int nextSurfaceAreaId, StructuralTopologySource? topologySource = null, Vector3? localPivot = null, bool takeMappingOwnership = false, ValidatedPartTopology prepared = null)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            // Don't infer welds here.
            // The saved mapping is part of the editable mesh state, even when two groups currently share a position.
            MeshTopology topology = prepared == null ? MeshTopology.Restore(mesh, renderToLogical, logicalVertexCount, takeMappingOwnership) : MeshTopology.RestorePacked(prepared.Data, renderToLogical);

            return new MeshPart(name, mesh, structuralFaces, surfaceAreas, triangleSurfaceAreaIds, nextSurfaceAreaId, topology, topologySource, localPivot, prepared);
        }

        private Vector3 CalculateDefaultLocalPivot()
        {
            int logicalVertexCount = Topology.LogicalVertexCount;
            if (logicalVertexCount <= 0)
            {
                return Vector3.zero;
            }

            double x = 0.0;
            double y = 0.0;
            double z = 0.0;

            for (int vertexId = 0; vertexId < logicalVertexCount; vertexId++)
            {
                Vector3 position = Topology.GetVertex(vertexId).GetPosition(Mesh);

                if (!IsFinite(position))
                {
                    return Vector3.zero;
                }

                x += position.x;
                y += position.y;
                z += position.z;
            }

            double inverseCount = 1.0 / logicalVertexCount;
            Vector3 pivot = new Vector3((float)(x * inverseCount), (float)(y * inverseCount), (float)(z * inverseCount));

            return IsFinite(pivot) ? pivot : Vector3.zero;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        internal static string NormalizeName(string name)
        {
            string normalized = name == null ? string.Empty : name.Trim();

            return string.IsNullOrEmpty(normalized) ? "Part" : normalized;
        }

        private void ValidateRestoredSurfaceAreaIdentity()
        {
            bool hasDefault = false;
            int maxId = -1;
            HashSet<int> ids = new HashSet<int>();

            for (int i = 0; i < m_SurfaceAreas.Count; i++)
            {
                SurfaceArea area = m_SurfaceAreas[i];
                if (area == null)
                {
                    throw new InvalidOperationException($"Mesh part '{Name}' contains a null Surface Area.");
                }

                if (area.Id < 0 || !ids.Add(area.Id))
                {
                    throw new InvalidOperationException($"Mesh part '{Name}' contains invalid or duplicate " + $"Surface Area ID {area.Id}.");
                }

                hasDefault |= area.Id == kDefaultSurfaceAreaId;
                maxId = Math.Max(maxId, area.Id);
            }

            if (!hasDefault)
            {
                throw new InvalidOperationException($"Mesh part '{Name}' is missing default Surface Area 0.");
            }

            // next ID must remain above every historical live ID.
            // It is saved specifically so deleting the highest area does not cause reuse.
            if (m_NextSurfaceAreaId <= maxId)
            {
                throw new InvalidOperationException($"Mesh part '{Name}' has next Surface Area ID " + $"{m_NextSurfaceAreaId}, which is not greater than " + $"existing maximum ID {maxId}.");
            }
        }

        private void ValidateSurfaceAreaState()
        {
            ValidateSurfaceAreaState(Mesh, StructuralTopology, m_StructuralFaceTriangles, m_TriangleSurfaceAreaIds);
        }

        private void ValidateSurfaceAreaState(MeshData mesh, StructuralTopology structuralTopology, StructuralFaceTriangles[] structuralFaceTriangles, int[] triangleSurfaceAreaIds)
        {
            if (m_SurfaceAreas.Count == 0)
            {
                throw new InvalidOperationException($"Mesh part '{Name}' must contain at least one Surface Area.");
            }

            if (mesh == null || structuralTopology == null || triangleSurfaceAreaIds == null)
            {
                throw new InvalidOperationException($"Mesh part '{Name}' has incomplete Surface Area state.");
            }

            int triangleCount = mesh.Triangles.Length / 3;
            if (triangleSurfaceAreaIds.Length != triangleCount)
            {
                throw new InvalidOperationException($"Mesh part '{Name}' has {triangleCount} triangles but " + $"{triangleSurfaceAreaIds.Length} Surface Area memberships.");
            }

            for (int i = 0; i < triangleSurfaceAreaIds.Length; i++)
            {
                if (!ContainsSurfaceAreaId(triangleSurfaceAreaIds[i]))
                {
                    throw new InvalidOperationException($"Mesh part '{Name}' triangle {i} references missing " + $"Surface Area {triangleSurfaceAreaIds[i]}.");
                }
            }

            ValidateStructuralFaceSurfaceAreas(structuralTopology, structuralFaceTriangles, triangleSurfaceAreaIds);
        }

        private bool ContainsSurfaceAreaId(int surfaceAreaId)
        {
            for (int i = 0; i < m_SurfaceAreas.Count; i++)
            {
                if (m_SurfaceAreas[i].Id == surfaceAreaId)
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildStructuralFaceTriangleMap()
        {
            StructuralFaceTriangleResolver.Build(StructuralTopology.Faces, Mesh.Triangles, out m_StructuralFaceTriangles, out m_TriangleStructuralFaceIds);
        }

        private static StructuralTopologySource DetectRestoredStructuralTopologySource(MeshData mesh, MeshTopology topology, StructuralFace[] structuralFaces)
        {
            if (mesh == null || topology == null || structuralFaces == null)
            {
                return PO2.Geometry.Topology.StructuralTopologySource.Authored;
            }

            // Snapshots previously saved converted parts with no structural faces at all.
            // Every built-in primitive has authored faces,
            // so a non-empty triangle stream with this old signature
            // can be upgraded safely instead of forcing the user to reconvert
            // an asset whose original ECS entity may already be gone.
            // TODO: Probs safe to remove at this point, I'm the only one it'd affect.
            if (structuralFaces.Length == 0)
            {
                return mesh.Triangles.Length > 0 ? StructuralTopologySource.ConvertedTriangleBaseline : PO2.Geometry.Topology.StructuralTopologySource.Authored;
            }

            int faceCursor = 0;
            int triangleCount = mesh.Triangles.Length / 3;
            for (int triangleId = 0; triangleId < triangleCount; triangleId++)
            {
                if (!IsTriangleStructurallyUsable(mesh, topology, triangleId))
                {
                    continue;
                }

                if (faceCursor >= structuralFaces.Length)
                {
                    return PO2.Geometry.Topology.StructuralTopologySource.Authored;
                }

                StructuralFace face = structuralFaces[faceCursor];
                int triangleOffset = triangleId * 3;
                if (face.Id != faceCursor || !face.IsTriangle || face.GetRenderVertexIndex(0) != mesh.Triangles[triangleOffset] || face.GetRenderVertexIndex(1) != mesh.Triangles[triangleOffset + 1] || face.GetRenderVertexIndex(2) != mesh.Triangles[triangleOffset + 2])
                {
                    return PO2.Geometry.Topology.StructuralTopologySource.Authored;
                }

                faceCursor++;
            }

            return faceCursor == structuralFaces.Length ? StructuralTopologySource.ConvertedTriangleBaseline : PO2.Geometry.Topology.StructuralTopologySource.Authored;
        }

        private static bool IsTriangleStructurallyUsable(MeshData mesh, MeshTopology topology, int triangleId)
        {
            int triangleOffset = checked(triangleId * 3);
            int renderA = mesh.Triangles[triangleOffset];
            int renderB = mesh.Triangles[triangleOffset + 1];
            int renderC = mesh.Triangles[triangleOffset + 2];
            if (renderA == renderB || renderA == renderC || renderB == renderC)
            {
                return false;
            }

            int logicalA = topology.GetLogicalVertexIdForRenderVertex(renderA);
            int logicalB = topology.GetLogicalVertexIdForRenderVertex(renderB);
            int logicalC = topology.GetLogicalVertexIdForRenderVertex(renderC);
            return logicalA != logicalB && logicalA != logicalC && logicalB != logicalC;
        }

        private void ValidateStructuralFaceSurfaceAreas(StructuralTopology structuralTopology, StructuralFaceTriangles[] structuralFaceTriangles, int[] triangleSurfaceAreaIds)
        {
            if (structuralTopology.FaceCount == 0)
            {
                return;
            }

            if (structuralFaceTriangles == null || structuralFaceTriangles.Length != structuralTopology.FaceCount)
            {
                throw new InvalidOperationException($"Mesh part '{Name}' has incomplete structural face mappings.");
            }

            for (int faceIndex = 0; faceIndex < structuralTopology.FaceCount; faceIndex++)
            {
                StructuralFace face = structuralTopology.GetFace(faceIndex);
                StructuralFaceTriangles mappedTriangles = structuralFaceTriangles[faceIndex];
                ValidateFaceSurfaceAreas(Name, face, mappedTriangles.TriangleA, mappedTriangles.TriangleB, mappedTriangles.Count, triangleSurfaceAreaIds);
            }
        }

        internal static void ValidatePreparedSurfaceAreas(PO2.Persistence.Model.PersistencePartData source, PackedTopologyData data)
        {
            for (int i = 0; i < source.StructuralFaces.Length; i++)
            {
                var face = source.StructuralFaces[i];
                ValidateFaceSurfaceAreas(source.Name, face, data[20][i], data[21][i], face.IsQuad ? 2 : 1, source.TriangleSurfaceAreaIds);
            }
        }

        internal static void ValidateResolvedSurfaceAreas(PO2.Persistence.Model.PersistencePartData source, StructuralFaceTriangles[] maps)
        {
            for (int i = 0; i < source.StructuralFaces.Length; i++)
                ValidateFaceSurfaceAreas(source.Name, source.StructuralFaces[i], maps[i].TriangleA, maps[i].TriangleB, maps[i].Count, source.TriangleSurfaceAreaIds);
        }

        private static void ValidateFaceSurfaceAreas(string name, StructuralFace face, int triangleA, int triangleB, int count, int[] memberships)
        {
            if (count != (face.IsQuad ? 2 : 1) || triangleA < 0 || triangleA >= memberships.Length)
                throw new InvalidOperationException($"Mesh part '{name}' structural face {face.Id} does not have a valid triangle mapping.");
            // A structural quad is one selectable/material unit.
            if (face.IsQuad && (triangleB < 0 || triangleB >= memberships.Length || memberships[triangleA] != memberships[triangleB]))
                throw new InvalidOperationException($"Mesh part '{name}' structural face {face.Id} spans multiple Surface Areas.");
        }
    }
}
