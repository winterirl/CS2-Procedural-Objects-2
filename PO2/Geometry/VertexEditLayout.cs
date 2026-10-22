using System;
using System.Collections.Generic;
using PO2.History;
using PO2.History.Actions;

namespace PO2.Geometry
{
    // Membership and connectivity are immutable. A gesture exclusively leases
    // scratch arrays and always captures fresh starting values.
    internal sealed unsafe class VertexEditLayout
    {
        internal readonly int[] Selected, Affected, Records, SelectedNormals, SelectedCorners;
        internal readonly ulong Fingerprint;
        internal readonly bool Sparse;
        internal readonly long Bytes;

        internal sealed class Workspace
        {
            internal readonly UnityEngine.Vector3[] Sums;
            internal readonly byte[] Moved, Active;

            internal Workspace(int selected, int affected)
            {
                Sums = new UnityEngine.Vector3[affected];
                Moved = new byte[selected];
                Active = new byte[affected];
            }
        }

        private Workspace m_Workspace;
        private bool m_WorkspaceInUse;

        internal Workspace RentWorkspace()
        {
            if (m_WorkspaceInUse) throw new InvalidOperationException("The normal workspace already belongs to an edit.");
            if (m_Workspace == null) m_Workspace = new Workspace(Selected.Length, Affected.Length);
            m_WorkspaceInUse = true;
            return m_Workspace;
        }

        internal void ReturnWorkspace()
        {
            m_WorkspaceInUse = false;
        }

        private int m_Owners;
        private bool m_Released;
        private Action m_ReleaseReservation;

        internal static long Estimate(MeshData mesh, int selected) => checked(4096L + 13L * selected + 29L * mesh.Vertices.Length + 28L * (mesh.Triangles.Length / 3) + (selected <= 128 ? VertexEditPreparationKernels.CoverageBytes(mesh.Vertices.Length) + 256L * (Math.Min(mesh.Triangles.Length / 3, 4096) + Math.Min(mesh.Vertices.Length, 1024) + selected) : 0));

        internal VertexEditLayout(MeshData mesh, int[] selected, MeshPart part = null, Action releaseReservation = null, int[] suppliedAffected = null, HistoryLayoutCache layoutCache = null)
        {
            if (mesh == null || mesh.Vertices == null || mesh.Triangles == null || selected == null || mesh.Triangles.Length % 3 != 0 || (mesh.Normals != null && mesh.Normals.Length != mesh.Vertices.Length))
                throw new ArgumentException("Invalid normal layout streams.");
            Fingerprint = layoutCache == null ? GeometryHistory.Layout(mesh) : layoutCache.Layout(mesh);
            Selected = (int[])selected.Clone();
            foreach (int index in Selected)
            {
                if ((uint)index >= (uint)mesh.Vertices.Length)
                    throw new ArgumentException("Invalid or duplicate normal layout selection.");
            }

            if (suppliedAffected != null)
            {
                Affected = (int[])suppliedAffected.Clone();
                BuildGiven(mesh, Selected, Affected, out Records, out SelectedNormals, out SelectedCorners);
            }
            else if (mesh.Normals == null)
            {
                var checkedSelection = (int[])Selected.Clone();
                if (VertexEditPreparationKernels.SortUnique(checkedSelection, mesh.Vertices.Length) != Selected.Length)
                    throw new ArgumentException("Duplicate normal layout selection.");
                Affected = Records = SelectedNormals = SelectedCorners = Array.Empty<int>();
            }
            else if (Selected.Length <= 128 && TrySparse(mesh, part, Selected, out Affected, out Records, out SelectedNormals, out SelectedCorners))
            {
                Sparse = true;
            }
            else BuildDense(mesh, Selected, out Affected, out Records, out SelectedNormals, out SelectedCorners);

            Bytes = 256L + 4L * ((long)Selected.Length + Affected.Length + Records.Length + SelectedNormals.Length + SelectedCorners.Length);
            m_ReleaseReservation = releaseReservation;
        }

        internal void Retain()
        {
            if (m_Released) throw new ObjectDisposedException(nameof(VertexEditLayout));
            m_Owners++;
        }

        internal void Release()
        {
            if (m_Owners <= 0) throw new InvalidOperationException("Normal layout ownership was released twice.");
            if (--m_Owners != 0) return;
            m_Released = true;
            var release = m_ReleaseReservation;
            m_ReleaseReservation = null;
            release?.Invoke();
        }

        private static void BuildDense(MeshData mesh, int[] selected, out int[] affected, out int[] records, out int[] selectedNormals, out int[] selectedCorners)
        {
            var selectedMap = new int[mesh.Vertices.Length];
            var affectedMap = new int[mesh.Vertices.Length];
            var c = new VertexEditPreparationKernels.Context
            {
                VertexCount = mesh.Vertices.Length,
                SelectedCount = selected.Length, TriangleCount = mesh.Triangles.Length
            };
            fixed (int* s = selected, sm = selectedMap, am = affectedMap, t = mesh.Triangles)
            {
                c.Selected = s;
                c.SelectedMap = sm;
                c.AffectedMap = am;
                c.Triangles = t;
                c.Operation = 0;
                VertexEditPreparationKernels.Execute(ref c);
                affected = new int[c.AffectedCount];
                fixed (int* a = affected)
                {
                    c.Affected = a;
                    c.Operation = 1;
                    VertexEditPreparationKernels.Execute(ref c);
                    records = new int[checked(c.RecordCount * 4)];
                    selectedNormals = new int[selected.Length];
                    selectedCorners = new int[checked(c.RecordCount * 3)];
                    fixed (int* r = records, sn = selectedNormals, sc = selectedCorners)
                    {
                        c.Records = r;
                        c.SelectedNormals = sn;
                        c.SelectedCorners = sc;
                        c.Operation = 2;
                        VertexEditPreparationKernels.Execute(ref c);
                    }
                }
            }
        }

        private static bool TrySparse(MeshData mesh, MeshPart part, int[] selected, out int[] affected, out int[] records, out int[] selectedNormals, out int[] selectedCorners)
        {
            affected = records = selectedNormals = selectedCorners = null;
            if (part?.Topology == null || part.Topology.RenderVertexCount != mesh.Vertices.Length || selected.Length == 0) return SparseFallback(SparseFailure.Unavailable);
            // Limit incidence expansion before allocating another large set.
            const int maximumCandidates = 4096;
            var candidates = new HashSet<int>();
            var moved = new HashSet<int>(selected);
            if (moved.Count != selected.Length) throw new ArgumentException("Duplicate normal layout selection.");
            var normals = new HashSet<int>(selected);
            if (!Gather(part, selected, candidates, maximumCandidates)) return false;
            foreach (int id in candidates)
            {
                int offset = id * 3;
                int a = mesh.Triangles[offset], b = mesh.Triangles[offset + 1], c = mesh.Triangles[offset + 2];
                if (!moved.Contains(a) && !moved.Contains(b) && !moved.Contains(c)) continue;
                normals.Add(a);
                normals.Add(b);
                normals.Add(c);
                if (normals.Count > 1024) return SparseFallback(SparseFailure.NormalLimit);
            }

            affected = new int[normals.Count];
            normals.CopyTo(affected);
            Array.Sort(affected);
            candidates.Clear();
            if (!Gather(part, affected, candidates, maximumCandidates)) return false;
            var triangles = new int[candidates.Count];
            candidates.CopyTo(triangles);
            Array.Sort(triangles);
            var found = new List<int>();
            var corners = new List<int>();
            selectedNormals = new int[selected.Length];
            for (int i = 0; i < selected.Length; i++) selectedNormals[i] = Array.BinarySearch(affected, selected[i]);
            var selectedSlots = new Dictionary<int, int>(selected.Length);
            for (int i = 0; i < selected.Length; i++) selectedSlots.Add(selected[i], i);
            foreach (int id in triangles)
            {
                int offset = id * 3;
                int a = mesh.Triangles[offset], b = mesh.Triangles[offset + 1], c = mesh.Triangles[offset + 2];
                int sa = Array.BinarySearch(affected, a), sb = Array.BinarySearch(affected, b), sc = Array.BinarySearch(affected, c);
                if (sa < 0 && sb < 0 && sc < 0) continue;
                found.Add(offset);
                found.Add(sa < 0 ? -1 : sa);
                found.Add(sb < 0 ? -1 : sb);
                found.Add(sc < 0 ? -1 : sc);
                corners.Add(selectedSlots.TryGetValue(a, out int va) ? va : -1);
                corners.Add(selectedSlots.TryGetValue(b, out int vb) ? vb : -1);
                corners.Add(selectedSlots.TryGetValue(c, out int vc) ? vc : -1);
            }

            records = found.ToArray();
            selectedCorners = corners.ToArray();
            if (!VertexEditPreparationKernels.Covers(mesh, selected, affected, records)) return SparseFallback(SparseFailure.Coverage);
            return true;
        }

        private enum SparseFailure
        {
            Unavailable,
            CandidateLimit,
            NormalLimit,
            Coverage
        }

        private static bool SparseFallback(SparseFailure reason)
        {
            return false;
        }

        private static bool Gather(MeshPart part, int[] vertices, HashSet<int> result, int maximum)
        {
            var logical = new HashSet<int>();
            try
            {
                foreach (int vertex in vertices)
                {
                    if ((uint)vertex >= (uint)part.Mesh.Vertices.Length) return SparseFallback(SparseFailure.Unavailable);
                    int id = part.Topology.GetLogicalVertexIdForRenderVertex(vertex);
                    if ((uint)id >= (uint)part.Topology.LogicalVertexCount) return SparseFallback(SparseFailure.Unavailable);
                    if (!logical.Add(id)) continue;
                    var triangles = part.Topology.GetVertex(id).TriangleIndices;
                    for (int i = 0; i < triangles.Count; i++)
                    {
                        int triangle = triangles[i];
                        if ((uint)triangle >= (uint)(part.Mesh.Triangles.Length / 3)) return SparseFallback(SparseFailure.Unavailable);
                        result.Add(triangle);
                        if (result.Count > maximum) return SparseFallback(SparseFailure.CandidateLimit);
                    }
                }

                return true;
            }
            catch (NotSupportedException)
            {
                return SparseFallback(SparseFailure.Unavailable);
            }
        }

        // Compatibility/fallback for callers with an explicit normal footprint.
        // The editor's ordinary cold capture uses the indexed builder above.
        private static void BuildGiven(MeshData mesh, int[] selected, int[] affected, out int[] records, out int[] selectedNormals, out int[] selectedCorners)
        {
            if (mesh.Normals == null && affected.Length != 0) throw new ArgumentException("Missing normal stream.");
            var slots = new Dictionary<int, int>(affected.Length);
            for (int i = 0; i < affected.Length; i++)
            {
                if ((uint)affected[i] >= (uint)mesh.Vertices.Length) throw new ArgumentOutOfRangeException(nameof(affected));
                slots.Add(affected[i], i);
            }

            var selection = new Dictionary<int, int>(selected.Length);
            selectedNormals = new int[selected.Length];
            for (int i = 0; i < selected.Length; i++)
            {
                selection.Add(selected[i], i);
                selectedNormals[i] = slots.TryGetValue(selected[i], out int slot) ? slot : -1;
            }

            var found = new List<int>();
            var corners = new List<int>();
            for (int i = 0; i < mesh.Triangles.Length && affected.Length != 0; i += 3)
            {
                int a = mesh.Triangles[i], b = mesh.Triangles[i + 1], c = mesh.Triangles[i + 2];
                if ((uint)a >= (uint)mesh.Vertices.Length || (uint)b >= (uint)mesh.Vertices.Length || (uint)c >= (uint)mesh.Vertices.Length)
                    throw new InvalidOperationException("Invalid normal-plan triangle.");
                bool hasA = slots.TryGetValue(a, out int sa), hasB = slots.TryGetValue(b, out int sb), hasC = slots.TryGetValue(c, out int sc);
                if (!hasA && !hasB && !hasC) continue;
                found.Add(i);
                found.Add(hasA ? sa : -1);
                found.Add(hasB ? sb : -1);
                found.Add(hasC ? sc : -1);
                corners.Add(selection.TryGetValue(a, out int va) ? va : -1);
                corners.Add(selection.TryGetValue(b, out int vb) ? vb : -1);
                corners.Add(selection.TryGetValue(c, out int vc) ? vc : -1);
            }

            records = found.ToArray();
            selectedCorners = corners.ToArray();
        }
    }
}