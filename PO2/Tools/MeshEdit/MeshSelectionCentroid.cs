using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Tools.MeshEdit
{
    // Selection membership is stable during a drag. Only its positions change.
    // This owner never retains pointers or reads live mesh arrays on a worker.
    internal sealed class MeshSelectionCentroid : IDisposable
    {
        internal const long kMaximumBytes = 8L * 1024 * 1024;

        internal enum Fallback
        {
            None,
            Budget,
            InvalidRepresentative,
            NonFinite
        }

        private struct Part
        {
            internal MeshPart Source;
            internal Guid Identity;
            internal MeshData Mesh;
            internal MeshTopology Topology;
            internal Vector3[] Positions;
            internal uint TopologyVersion, GeometryVersion;
            internal int Start, Count;
        }

        private ProceduralObject m_Object;
        private Guid m_Identity;
        private uint m_PartVersion;
        private VertexSelection m_Selection;
        private int m_Version, m_SelectedCount, m_Mode, m_Scope, m_ActivePart;
        private Part[] m_Parts;
        private int[] m_Indices;
        private Vector3 m_Local;
        private bool m_HasLocal;
        internal long Bytes { get; private set; }
        internal int LogicalCount { get; private set; }
        internal Fallback LastFallback { get; private set; }
        internal int Builds { get; private set; }
        internal int Reductions { get; private set; }

        internal bool TryGet(ProceduralObject obj, VertexSelection selection, int version, MeshSelectionMode mode, MeshEditVertexScope scope, int activePart, out Vector3 local, out Vector3 world)
        {
            local = world = Vector3.zero;
            LastFallback = Fallback.None;
            if (obj == null || selection == null || selection.Count == 0)
            {
                Dispose();
                return false;
            }

            bool geometryChanged;
            if (!Matches(obj, selection, version, (int)mode, (int)scope, activePart, out geometryChanged))
            {
                Dispose(); // Release the old cache before counting the replacement peak.
                if (!Build(obj, selection, version, (int)mode, (int)scope, activePart))
                    return CheckedFallback(obj, selection, out local, out world);
                geometryChanged = true;
            }
            else
            {
            }

            if (geometryChanged || !m_HasLocal)
            {
                var sum = new MeshCentroidKernels.Sum();
                for (int i = 0; i < m_Parts.Length; i++)
                {
                    var part = m_Parts[i];
                    MeshCentroidKernels.Accumulate(part.Positions, m_Indices, part.Start, part.Count, ref sum);
                    m_Parts[i].GeometryVersion = part.Source.GeometryVersion;
                }

                Reductions++;
                m_HasLocal = LogicalCount != 0 && sum.Error == 0;
                if (!m_HasLocal)
                {
                    LastFallback = sum.Error != 0 ? Fallback.NonFinite : Fallback.None;
                    return false;
                }

                m_Local = new Vector3((float)(sum.X / LogicalCount), (float)(sum.Y / LogicalCount), (float)(sum.Z / LogicalCount));
            }

            local = m_Local;
            world = obj.Transform.TransformPoint(local);
            if (!Finite(world))
            {
                local = world = Vector3.zero;
                LastFallback = Fallback.NonFinite;
                return false;
            }

            return true;
        }

        // Box/topology adoption already calculated a revision-checked local
        // center. Reuse it and prime membership at adoption, before mouse-down.
        internal bool TryPrime(ProceduralObject obj, VertexSelection selection, int version, MeshSelectionMode mode, MeshEditVertexScope scope, int activePart, Vector3 local, int count)
        {
            Dispose();
            LastFallback = Fallback.None;
            if (obj == null || selection == null || count == 0 || !Finite(local)) return false;
            if (!Build(obj, selection, version, (int)mode, (int)scope, activePart))
            {
                return false;
            }

            if (LogicalCount != count)
            {
                Dispose();
                return false;
            }

            for (int i = 0; i < m_Parts.Length; i++) m_Parts[i].GeometryVersion = m_Parts[i].Source.GeometryVersion;
            m_Local = local;
            m_HasLocal = true;
            return true;
        }

        private bool Matches(ProceduralObject obj, VertexSelection selection, int version, int mode, int scope, int activePart, out bool geometryChanged)
        {
            geometryChanged = false;
            if (!ReferenceEquals(m_Object, obj) || m_Identity != obj.PersistentId || m_PartVersion != obj.PartVersion || !ReferenceEquals(m_Selection, selection) || m_Version != version || m_SelectedCount != selection.Count || m_Mode != mode || m_Scope != scope || m_ActivePart != activePart || m_Parts == null || m_Parts.Length != obj.Parts.Count) return false;
            for (int i = 0; i < m_Parts.Length; i++)
            {
                var cached = m_Parts[i];
                var part = obj.Parts[i];
                if (!ReferenceEquals(cached.Source, part) || cached.Identity != part.PartHistoryId || !ReferenceEquals(cached.Mesh, part.Mesh) || !ReferenceEquals(cached.Topology, part.Topology) || cached.TopologyVersion != part.TopologyVersion || !ReferenceEquals(cached.Positions, part.Mesh.Vertices)) return false;
                if (cached.GeometryVersion != part.GeometryVersion) geometryChanged = true;
            }

            return true;
        }

        private bool Build(ProceduralObject obj, VertexSelection selection, int version, int mode, int scope, int activePart)
        {
            // Includes object/array headers and the temporary per-part cursors.
            long peak = 512L + obj.Parts.Count * 128L + selection.Count * 4L;
            if (peak > kMaximumBytes)
            {
                LastFallback = Fallback.Budget;
                return false;
            }

            m_Parts = new Part[obj.Parts.Count];
            m_Indices = new int[selection.Count];
            var next = new int[m_Parts.Length];
            for (int i = 0; i < m_Parts.Length; i++)
            {
                var part = obj.Parts[i];
                m_Parts[i] = new Part
                {
                    Source = part, Identity = part.PartHistoryId, Mesh = part.Mesh, Topology = part.Topology,
                    Positions = part.Mesh.Vertices, TopologyVersion = part.TopologyVersion
                };
            }

            foreach (var vertex in selection.Vertices)
            {
                if (!ValidLogical(obj, vertex)) continue;
                m_Parts[vertex.PartIndex].Count++;
            }

            int count = 0;
            for (int i = 0; i < m_Parts.Length; i++)
            {
                next[i] = m_Parts[i].Start = count;
                count += m_Parts[i].Count;
            }

            foreach (var vertex in selection.Vertices)
            {
                if (!ValidLogical(obj, vertex)) continue;
                var part = obj.Parts[vertex.PartIndex];
                var render = part.Topology.GetVertex(vertex.VertexId).RenderVertexIndices;
                if (render.Count == 0 || (uint)render[0] >= (uint)part.Mesh.Vertices.Length)
                {
                    Dispose();
                    LastFallback = Fallback.InvalidRepresentative;
                    return false;
                }

                m_Indices[next[vertex.PartIndex]++] = render[0];
            }

            m_Object = obj;
            m_Identity = obj.PersistentId;
            m_PartVersion = obj.PartVersion;
            m_Selection = selection;
            m_Version = version;
            m_SelectedCount = selection.Count;
            m_Mode = mode;
            m_Scope = scope;
            m_ActivePart = activePart;
            LogicalCount = count;
            Bytes = peak;
            Builds++;
            return true;
        }

        private static bool ValidLogical(ProceduralObject obj, VertexRef vertex) =>
            (uint)vertex.PartIndex < (uint)obj.Parts.Count && (uint)vertex.VertexId < (uint)obj.Parts[vertex.PartIndex].Topology.LogicalVertexCount;

        private static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));

        private bool CheckedFallback(ProceduralObject obj, VertexSelection selection, out Vector3 local, out Vector3 world)
        {
            local = world = Vector3.zero;

            if (LastFallback != Fallback.Budget) return false;
            // Exceptional cap misses retain the checked logical-vertex walk.
            double x = 0, y = 0, z = 0;
            int count = 0;
            foreach (var vertex in selection.Vertices)
            {
                if (!ValidLogical(obj, vertex)) continue;
                var part = obj.Parts[vertex.PartIndex];
                var render = part.Topology.GetVertex(vertex.VertexId).RenderVertexIndices;
                if (render.Count == 0 || (uint)render[0] >= (uint)part.Mesh.Vertices.Length)
                {
                    LastFallback = Fallback.InvalidRepresentative;
                    return false;
                }

                var value = part.Mesh.Vertices[render[0]];
                if (!Finite(value))
                {
                    LastFallback = Fallback.NonFinite;
                    return false;
                }

                x += value.x;
                y += value.y;
                z += value.z;
                count++;
            }

            if (count == 0) return false;
            local = new Vector3((float)(x / count), (float)(y / count), (float)(z / count));
            world = obj.Transform.TransformPoint(local);
            if (!Finite(world))
            {
                local = world = Vector3.zero;
                LastFallback = Fallback.NonFinite;
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            m_Object = null;
            m_Selection = null;
            m_Parts = null;
            m_Indices = null;
            m_HasLocal = false;
            m_Local = Vector3.zero;
            LogicalCount = 0;
            Bytes = 0;
        }
    }

    [BurstCompile]
    internal static unsafe class MeshCentroidKernels
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Sum
        {
            internal double X, Y, Z;
            internal int Error;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(Vector3* positions, int* indices, int count, ref Sum sum);

        private static Kernel s_Reduce = Reduce;
        private static bool s_Initialized;
        internal static bool UsesBurst { get; private set; }
        internal static string Failure { get; private set; } = string.Empty;

        internal static void Initialize()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            try
            {
                if (BurstCompiler.IsEnabled)
                {
                    s_Reduce = BurstCompiler.CompileFunctionPointer<Kernel>(Reduce).Invoke;
                    UsesBurst = true;
                }
                else Failure = "Burst disabled; centroid uses its managed entry point.";
            }
            catch (Exception error)
            {
                Failure = "Centroid Burst initialization failed: " + error.Message;
            }
        }

        internal static void Accumulate(Vector3[] positions, int[] indices, int start, int count, ref Sum sum)
        {
            if (count == 0) return;
            fixed (Vector3* p = positions)
            fixed (int* at = indices)
                s_Reduce(p, at + start, count, ref sum);
        }

        private static bool Finite(float value) => (*(uint*)&value & 0x7f800000u) != 0x7f800000u;

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void Reduce(Vector3* positions, int* indices, int count, ref Sum sum)
        {
            for (int i = 0; i < count; i++)
            {
                var value = positions[indices[i]];
                if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z))
                {
                    sum.Error = 1;
                    return;
                }

                sum.X += value.x;
                sum.Y += value.y;
                sum.Z += value.z;
            }
        }
    }
}