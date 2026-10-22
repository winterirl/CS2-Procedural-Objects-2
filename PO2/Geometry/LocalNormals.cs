using System;
using System.Collections.Generic;
using UnityEngine;

namespace PO2.Geometry
{
    internal static class LocalNormals
    {
        internal static int[] FindAffected(MeshData mesh, int[] moved)
        {
            if (mesh == null || mesh.Vertices == null || mesh.Triangles == null || moved == null)
                throw new ArgumentException("Missing normal-plan input.");
            if (mesh.Triangles.Length % 3 != 0) throw new InvalidOperationException("Incomplete triangle stream.");
            foreach (int index in moved)
                if ((uint)index >= (uint)mesh.Vertices.Length)
                    throw new ArgumentOutOfRangeException(nameof(moved));
            // A missing normal stream stays missing. Rendering already supports
            // that case; a position edit should not invent a new source stream.
            if (mesh.Normals == null) return Array.Empty<int>();
            if (mesh.Normals.Length != mesh.Vertices.Length) throw new InvalidOperationException("Mismatched normal stream.");
            var selected = new HashSet<int>(moved);
            var affected = new HashSet<int>(moved);
            foreach (int index in moved)
                if (index < 0 || index >= mesh.Vertices.Length)
                    throw new ArgumentOutOfRangeException(nameof(moved));
            var triangles = mesh.Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if ((uint)a >= (uint)mesh.Vertices.Length || (uint)b >= (uint)mesh.Vertices.Length || (uint)c >= (uint)mesh.Vertices.Length)
                    throw new InvalidOperationException("Invalid normal-plan triangle.");
                if (!selected.Contains(a) && !selected.Contains(b) && !selected.Contains(c)) continue;
                affected.Add(a);
                affected.Add(b);
                affected.Add(c);
            }

            var result = new int[affected.Count];
            affected.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        internal sealed unsafe class Plan : IDisposable
        {
            private readonly MeshData m_Mesh;
            private readonly Vector3[] m_Positions, m_Normals;
            private readonly int[] m_SourceTriangles, m_Selected, m_Affected, m_Records;
            private readonly int[] m_SelectedNormalSlots, m_SelectedTriangleSlots;
            private readonly Vector3[] m_Sums;
            private readonly byte[] m_Moved, m_Active;
            private bool m_MaskValid, m_Disposed;
            private int m_ActiveCount;
            private static long s_LiveBytes;
            internal long Bytes { get; }
            internal static long LiveBytes => System.Threading.Interlocked.Read(ref s_LiveBytes);
            internal int MaskBuilds { get; private set; }
            internal int MaskReuses { get; private set; }
            internal int EvaluatedTriangles { get; private set; }

            private VertexEditLayout m_Layout;

            internal Plan(MeshData mesh, int[] affected, int[] selected) : this(mesh, new VertexEditLayout(mesh, selected, suppliedAffected: affected))
            {
            }

            internal Plan(MeshData mesh, VertexEditLayout layout)
            {
                if (mesh == null || layout == null) throw new ArgumentException("Missing normal-plan input.");
                var workspace = layout.RentWorkspace();
                layout.Retain();
                m_Layout = layout;
                m_Mesh = mesh;
                m_Positions = mesh.Vertices;
                m_Normals = mesh.Normals;
                m_SourceTriangles = mesh.Triangles;
                m_Selected = layout.Selected;
                m_Affected = layout.Affected;
                m_Records = layout.Records;
                m_SelectedNormalSlots = layout.SelectedNormals;
                m_SelectedTriangleSlots = layout.SelectedCorners;
                m_Sums = workspace.Sums;
                m_Active = workspace.Active;
                m_Moved = workspace.Moved;
                // Every first update rebuilds its mask and overwrites every slot
                // before use. A previous gesture's values never become input.
                m_MaskValid = false;
                m_ActiveCount = 0;
                Bytes = layout.Bytes + 128L + m_Selected.Length + 13L * m_Affected.Length;
                System.Threading.Interlocked.Add(ref s_LiveBytes, Bytes);
            }

            internal bool Matches(MeshData mesh) => !m_Disposed && ReferenceEquals(mesh, m_Mesh) && ReferenceEquals(mesh.Vertices, m_Positions) && ReferenceEquals(mesh.Normals, m_Normals) && ReferenceEquals(mesh.Triangles, m_SourceTriangles);

            internal bool Recalculate(MeshData mesh, Vector3[] startPositions, Vector3[] startNormals)
            {
                if (!Matches(mesh) || startPositions == null || startPositions.Length != m_Selected.Length || startNormals == null || startNormals.Length != m_Affected.Length) return false;
                EvaluatedTriangles = 0;
                if (m_Normals == null)
                {
                    return true;
                }

                var c = new NormalUpdateKernels.Context
                {
                    SelectedCount = m_Selected.Length, AffectedCount = m_Affected.Length, VertexCount = m_Positions.Length,
                    RecordCount = m_Records.Length / 4, ActiveCount = m_ActiveCount, MaskChanged = m_MaskValid ? 0 : 1
                };
                fixed (Vector3* positions = m_Positions, normals = m_Normals, oldPositions = startPositions, oldNormals = startNormals, sums = m_Sums)
                fixed (int* selected = m_Selected, affected = m_Affected, triangles = m_SourceTriangles, records = m_Records, selectedNormals = m_SelectedNormalSlots, selectedCorners = m_SelectedTriangleSlots)
                fixed (byte* moved = m_Moved, active = m_Active)
                {
                    c.Positions = (NormalUpdateKernels.Point*)positions;
                    c.Normals = (NormalUpdateKernels.Point*)normals;
                    c.StartPositions = (NormalUpdateKernels.Point*)oldPositions;
                    c.StartNormals = (NormalUpdateKernels.Point*)oldNormals;
                    c.Sums = (NormalUpdateKernels.Point*)sums;
                    c.Selected = selected;
                    c.Affected = affected;
                    c.Triangles = triangles;
                    c.Records = records;
                    c.SelectedNormals = selectedNormals;
                    c.SelectedCorners = selectedCorners;
                    c.Moved = moved;
                    c.Active = active;
                    NormalUpdateKernels.ComparePositions(ref c);
                    if (c.Error == 0)
                    {
                        if (c.MaskChanged != 0)
                        {
                            NormalUpdateKernels.BuildNeighborhood(ref c);
                            m_ActiveCount = c.ActiveCount;
                            m_MaskValid = true;
                            MaskBuilds++;
                        }
                        else
                        {
                            MaskReuses++;
                        }

                        if (c.ActiveCount != 0) NormalUpdateKernels.SumTriangles(ref c);
                        if (c.Error == 0)
                        {
                            NormalUpdateKernels.FinishNormals(ref c);
                            if (c.Error == 0)
                            {
                                NormalUpdateKernels.PublishNormals(ref c);
                            }
                        }
                    }
                }

                EvaluatedTriangles = c.Evaluated;
                // A partial comparison must never leave a reusable old mask.
                if (c.Error != 0) m_MaskValid = false;
                return c.Error == 0;
            }

            public void Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                m_Layout.ReturnWorkspace();
                m_Layout.Release();
                m_Layout = null;
                System.Threading.Interlocked.Add(ref s_LiveBytes, -Bytes);
            }
        }
    }
}