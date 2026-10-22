using System;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    internal sealed class VertexPatchAction : HistoryCaptureAction
    {
        private readonly HistoryPartKey m_Key;
        private readonly ulong m_Layout;
        private readonly int[] m_Positions;
        private readonly int[] m_NormalIndices;
        private LocalNormals.Plan m_NormalPlan;
        private MeshPart m_PreviewPart;
        private MeshData m_PreviewMesh;
        private Vector3[] m_PreviewPositions;
        private int[] m_PreviewTriangles;
        private uint m_PreviewTopology;
        private bool m_PreviewFailed;
        private Vector3[] m_OldPositions;
        private Vector3[] m_OldNormals;
        private ulong m_Expected;
        private ulong m_OppositeHash;

        internal VertexPatchAction(ProceduralObject obj, int index, int[] positions, LocalizedText description, VertexEditLayout prepared = null, HistoryLayoutCache layoutCache = null) : base(description)
        {
            var part = obj.Parts[index];
            m_Key = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), part.PartHistoryId, index);
            var layout = prepared ?? new VertexEditLayout(part.Mesh, positions, part, layoutCache: layoutCache);
            m_Layout = layout.Fingerprint;
            m_Positions = layout.Selected;
            m_NormalIndices = layout.Affected;
            m_OppositeHash = VertexHistoryKernels.Capture(part.Mesh, m_Positions, m_NormalIndices, out m_OldPositions, out m_OldNormals).Hash;
            m_NormalPlan = part.Mesh.Normals == null ? null : new LocalNormals.Plan(part.Mesh, layout);
            m_PreviewMesh = part.Mesh;
            m_PreviewPositions = part.Mesh.Vertices;
            m_PreviewTriangles = part.Mesh.Triangles;
            m_PreviewPart = part;
            m_PreviewTopology = part.TopologyVersion;
        }

        public override HistoryFootprint Footprint
        {
            get
            {
                long bytes = 192 + 16L * (m_Positions.Length + m_NormalIndices.Length);
                return new HistoryFootprint(bytes, 0, 0, bytes);
            }
        }

        private MeshPart Resolve(HistoryContext context)
        {
            if (!context.TryResolve(m_Key, out var part, out _) || context.Layouts.Layout(part.Mesh) != m_Layout)
                throw new InvalidOperationException("Vertex history target or stream layout changed.");
            return part;
        }

        internal GeometryChange Change(MeshPart part, int index, uint before) =>
            new GeometryChange(part, index, before, m_Positions, m_NormalIndices);

        internal bool Recalculate(MeshPart part)
        {
            bool valid = !m_PreviewFailed && MatchesPreview(part);
            if (valid && m_NormalPlan != null) valid = m_NormalPlan.Recalculate(part.Mesh, m_OldPositions, m_OldNormals);
            if (!valid)
            {
                m_PreviewFailed = true;
            }

            return valid;
        }

        private bool MatchesPreview(MeshPart part)
        {
            if (part == null || !ReferenceEquals(part, m_PreviewPart) || part.TopologyVersion != m_PreviewTopology) return false;
            if (m_NormalPlan != null) return m_NormalPlan.Matches(part.Mesh);
            return ReferenceEquals(part.Mesh, m_PreviewMesh) && part.Mesh.Normals == null && ReferenceEquals(part.Mesh.Vertices, m_PreviewPositions) && ReferenceEquals(part.Mesh.Triangles, m_PreviewTriangles);
        }

        internal bool HasFiniteNormals(MeshPart part)
        {
            if (m_PreviewFailed || !MatchesPreview(part)) return false;
            foreach (int index in m_NormalIndices)
            {
                Vector3 value = part.Mesh.Normals[index];
                if (float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z)) return false;
            }

            return true;
        }

        internal override void Seal(HistoryContext context)
        {
            if (m_PreviewFailed) throw new InvalidOperationException("Vertex preview failed validation.");
            var part = Resolve(context);
            var mesh = part.Mesh;
            var current = VertexHistoryKernels.Scan(mesh, m_Positions, m_NormalIndices, m_OldPositions, m_OldNormals);
            ReleasePreview();
            if (current.SamePositions)
            {
                if (!current.SameNormals) Apply(context, part, m_OldPositions, m_OldNormals);
                HasChanges = false;
                return;
            }

            m_Expected = current.Hash;
            HasChanges = true;
        }

        internal override void RestoreCapture(HistoryContext context)
        {
            var part = Resolve(context);
            var current = VertexHistoryKernels.Scan(part.Mesh, m_Positions, m_NormalIndices, m_OldPositions, m_OldNormals, validate: false, hash: false);
            if (!current.SamePositions || !current.SameNormals) Apply(context, part, m_OldPositions, m_OldNormals);
        }

        public override HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            MeshPart part;
            ProceduralObject obj;
            int index;
            part = Resolve(context);
            if (!context.TryResolve(m_Key.Owner, out obj) || !context.TryResolve(m_Key, out var resolved, out index) || !ReferenceEquals(part, resolved)) throw new InvalidOperationException("Missing vertex history owner.");
            uint before = part.GeometryVersion;
            ulong currentHash;
            if (HistoryBoundaryKernels.CanSwap(part.Mesh, m_OldPositions, m_OldNormals))
            {
                // Scan validates the entire source before the first write. The
                // stored arrays belong only to this action, so replay can reuse them.
                var current = VertexHistoryKernels.Scan(part.Mesh, m_Positions, m_NormalIndices);
                if (current.Hash != m_Expected) return new HistoryResult(false, "Vertex history values no longer match.", true);
                HistoryBoundaryKernels.SwapValidated(part.Mesh, m_Positions, m_NormalIndices, m_OldPositions, m_OldNormals);
                currentHash = current.Hash;
            }
            else
            {
                // Shared source channels need both old values captured before
                // either channel changes. Preserve that unusual mesh layout.
                var current = VertexHistoryKernels.Capture(part.Mesh, m_Positions, m_NormalIndices, out var positions, out var normals, true);
                if (current.Hash != m_Expected) return new HistoryResult(false, "Vertex history values no longer match.", true);
                Write(part, m_OldPositions, m_OldNormals);
                m_OldPositions = positions;
                m_OldNormals = normals;
                currentHash = current.Hash;
            }

            m_Expected = m_OppositeHash;
            m_OppositeHash = currentHash;
            Publish(context, obj, part, index, before);
            return HistoryResult.Ok;
        }

        private void Apply(HistoryContext context, MeshPart part, Vector3[] positions, Vector3[] normals)
        {
            if (!context.TryResolve(m_Key.Owner, out var obj) || !context.TryResolve(m_Key, out _, out int index))
                throw new InvalidOperationException("Missing vertex history owner.");
            uint before = part.GeometryVersion;
            Write(part, positions, normals);
            Publish(context, obj, part, index, before);
        }

        private void Write(MeshPart part, Vector3[] positions, Vector3[] normals)
        {
            for (int i = 0; i < m_Positions.Length; i++) part.Mesh.Vertices[m_Positions[i]] = positions[i];
            for (int i = 0; i < m_NormalIndices.Length; i++) part.Mesh.Normals[m_NormalIndices[i]] = normals[i];
        }

        private void Publish(HistoryContext context, ProceduralObject obj, MeshPart part, int index, uint before)
        {
            part.MarkPositionsAndDirectionsChanged();
            obj.MarkDirty(ProceduralObjectDirtyFlags.Geometry);
            context.GeometryChanged(obj, Change(part, index, before));
        }

        private void ReleasePreview()
        {
            m_NormalPlan?.Dispose();
            m_NormalPlan = null;
            m_PreviewPart = null;
            m_PreviewMesh = null;
            m_PreviewPositions = null;
            m_PreviewTriangles = null;
        }

        public override void Dispose()
        {
            ReleasePreview();
            m_OldPositions = null;
            m_OldNormals = null;
        }
    }

    internal static class GeometryHistory
    {
        internal static ulong Layout(MeshData mesh) => VertexHistoryKernels.Layout(mesh);
    }
}