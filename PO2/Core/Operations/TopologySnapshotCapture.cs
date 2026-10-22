using System;
using System.Threading;
using UnityEngine;
using PO2.Core;
using PO2.Geometry.Topology;
using PO2.History;
using PO2.Persistence.Model;

namespace PO2.Core.Operations
{
    internal sealed class TopologySnapshotCapture
    {
        private readonly ProceduralObject m_Object;
        private readonly int m_Index, m_LogicalCount, m_NextArea;
        private readonly string m_Name;
        private readonly Vector3? m_Pivot;
        private readonly StructuralTopologySource? m_SourceKind;
        private readonly BudgetedCopy.Stream<Vector3> m_Positions, m_Normals;
        private readonly BudgetedCopy.Stream<Vector4> m_Tangents, m_UV, m_TexCoord1, m_TexCoord3;
        private readonly BudgetedCopy.Stream<Color32> m_Colors;
        private readonly BudgetedCopy.Stream<int> m_Triangles, m_Mapping, m_Membership;
        private readonly BudgetedCopy.Stream<StructuralFace> m_Faces;
        private readonly BudgetedCopy.Stream<PersistenceSurfaceAreaData> m_Areas;
        internal readonly BudgetedCopy Copy = new BudgetedCopy();
        internal readonly TopologyTargetStamp Stamp;

        internal TopologySnapshotCapture(ProceduralObject obj, int index)
        {
            m_Object = obj;
            m_Index = index;
            Stamp = new TopologyTargetStamp(obj, index);
            var part = obj.Parts[index];
            var mesh = part.Mesh;
            var topology = part.Topology;
            System.Collections.Generic.IReadOnlyList<StructuralFace> faces = part.StructuralTopology.Faces;
            var membership = part.TriangleSurfaceAreaIds;
            var areas = part.SurfaceAreas;
            m_Name = part.Name;
            m_Pivot = part.LocalPivot;
            m_SourceKind = part.StructuralTopologySource;
            m_LogicalCount = topology.LogicalVertexCount;
            m_NextArea = part.NextSurfaceAreaId;
            m_Positions = Copy.Add(mesh.Vertices, 12);
            m_Normals = Copy.Add(mesh.Normals, 12);
            m_Tangents = Copy.Add(mesh.Tangents, 16);
            m_UV = Copy.Add(mesh.UV0, 16);
            m_Colors = Copy.Add(mesh.VertexColors, 4);
            m_TexCoord1 = Copy.Add(mesh.TexCoord1, 16);
            m_TexCoord3 = Copy.Add(mesh.TexCoord3, 16);
            m_Triangles = Copy.Add(mesh.Triangles, 4);
            m_Mapping = Copy.Add(topology.RenderVertexCount, 4, topology.GetLogicalVertexIdForRenderVertex);
            m_Membership = Copy.Add(membership.Count, 4, i => membership[i]);
            m_Faces = Copy.Add(faces.Count, 24, i => faces[i]);
            m_Areas = Copy.Add(areas.Count, 512, i => PersistenceSurfaceAreaData.Capture(areas[i]));
        }

        internal void Start(CancellationToken token) => Copy.Start(token);

        internal bool Poll(CancellationToken token, long deadlineTimestamp = 0) =>
            Copy.Poll(() => Stamp.Matches(m_Object, m_Index), token, deadlineTimestamp: deadlineTimestamp);

        internal PersistencePartData Finish()
        {
            if (!Copy.IsComplete || !Stamp.Matches(m_Object, m_Index)) throw new InvalidOperationException("Incomplete topology capture.");
            return PersistencePartData.FromDecoded(m_Name, m_Positions.Data, m_Normals?.Data, m_Tangents?.Data, m_UV?.Data, m_Colors?.Data, m_TexCoord1?.Data, m_TexCoord3?.Data, m_Triangles.Data, m_Mapping.Data, m_LogicalCount, m_Faces.Data, m_SourceKind, m_Areas.Data, m_Membership.Data, m_NextArea, m_Pivot);
        }
    }
}