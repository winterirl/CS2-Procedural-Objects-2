using System;
using System.Collections.Generic;
using System.Threading;
using PO2.Core.Operations;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Persistence.Model;

namespace PO2.Geometry
{
    public sealed partial class MeshPart
    {
        internal MirrorTopologySnapshot CaptureMirrorTopology() => new MirrorTopologySnapshot(Topology, StructuralTopology, m_StructuralFaceTriangles, m_TriangleStructuralFaceIds, StructuralTopologySource);

        internal static MeshPart CreateMirrorCandidate(PersistencePartData data, MeshTopology logical, StructuralTopology structural, StructuralFaceTriangles[] faceTriangles, int[] triangleFaces, CancellationToken token) =>
            new MeshPart(data, logical, structural, faceTriangles, triangleFaces, token);

        private MeshPart(PersistencePartData data, MeshTopology logical, StructuralTopology structural, StructuralFaceTriangles[] faceTriangles, int[] triangleFaces, CancellationToken token)
        {
            Name = NormalizeName(data.Name);
            Mesh = new MeshData(data.Vertices, data.Normals, data.Tangents, data.UV0, data.VertexColors, data.TexCoord1, data.TexCoord3, data.Triangles);
            Topology = logical;
            StructuralTopology = structural;
            StructuralTopologySource = data.StructuralTopologySource.Value;
            m_StructuralFaceTriangles = faceTriangles;
            m_TriangleStructuralFaceIds = triangleFaces;
            m_SurfaceAreas = new List<SurfaceArea>(data.SurfaceAreas.Length);
            foreach (var area in data.SurfaceAreas)
            {
                token.ThrowIfCancellationRequested();
                m_SurfaceAreas.Add(area.Restore());
            }

            m_TriangleSurfaceAreaIds = data.TriangleSurfaceAreaIds;
            m_NextSurfaceAreaId = data.NextSurfaceAreaId;
            ValidateRestoredSurfaceAreaIdentity();
            ValidateSurfaceAreaState();
            if (!data.LocalPivot.HasValue || !IsFinite(data.LocalPivot.Value)) throw new InvalidOperationException("Invalid mirror pivot.");
            LocalPivot = data.LocalPivot.Value;
            token.ThrowIfCancellationRequested();
        }
    }
}