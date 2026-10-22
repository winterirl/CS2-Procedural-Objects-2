using System;
using System.Collections.Generic;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;

namespace PO2.Tools.MeshEdit
{
    // One current selection, plus any layout still leased by an active edit.
    // Timeline entries retain only their immutable index arrays and values.
    internal sealed class MeshVertexEditPreparation : IDisposable
    {
        internal const long kPartLimit = 32L * 1024 * 1024;
        internal const long kControllerLimit = 64L * 1024 * 1024;

        private sealed class Budget
        {
            internal long Bytes, Peak;
            internal readonly long Limit;

            internal Budget(long limit)
            {
                Limit = limit;
            }

            internal bool Reserve(long bytes)
            {
                if (bytes < 0 || bytes > Limit - Bytes) return false;
                Bytes += bytes;
                Peak = Math.Max(Peak, Bytes);
                return true;
            }

            internal void Release(long bytes)
            {
                Bytes -= bytes;
                if (Bytes < 0) throw new InvalidOperationException("Edit preparation reservation released twice.");
            }
        }

        private sealed class Part
        {
            internal MeshPart Source;
            internal MeshData Mesh;
            internal object Topology, Positions, Normals, Tangents, Triangles;
            internal Guid Identity;
            internal uint Version;
            internal VertexEditLayout Layout;
            internal bool Matches(MeshPart part) => ReferenceEquals(Source, part) && part.PartHistoryId == Identity && part.TopologyVersion == Version && ReferenceEquals(part.Topology, Topology) && ReferenceEquals(part.Mesh, Mesh) && ReferenceEquals(part.Mesh.Vertices, Positions) && ReferenceEquals(part.Mesh.Normals, Normals) && ReferenceEquals(part.Mesh.Tangents, Tangents) && ReferenceEquals(part.Mesh.Triangles, Triangles);
        }

        private readonly Budget m_Budget;
        private readonly Dictionary<int, Part> m_Parts = new Dictionary<int, Part>();
        private bool m_HasSelection;
        private ProceduralObject m_Object;
        private object m_Selection;
        private Guid m_Identity;
        private uint m_PartVersion;
        private int m_Version, m_Mode, m_Scope, m_ActivePart, m_Count, m_PartCount;
        internal long ReservedBytes => m_Budget.Bytes;
        internal long PeakBytes => m_Budget.Peak;

        internal MeshVertexEditPreparation(long limit = kControllerLimit)
        {
            m_Budget = new Budget(limit);
        }

        // The caller consumes this inside the immediate history capture factory.
        // It keeps layouts alive if the controller drops its cache during capture.
        internal sealed class Acquisition : IDisposable
        {
            internal readonly Dictionary<int, VertexEditLayout> Layouts = new Dictionary<int, VertexEditLayout>();

            internal void Add(int index, VertexEditLayout layout)
            {
                layout.Retain();
                try
                {
                    Layouts.Add(index, layout);
                }
                catch
                {
                    layout.Release();
                    throw;
                }
            }

            public void Dispose()
            {
                foreach (var layout in Layouts.Values) layout.Release();
                Layouts.Clear();
            }
        }

        internal Acquisition Acquire(ProceduralObject obj, object selection, int version, int mode, int scope, int activePart, int count, Func<Dictionary<int, int[]>> createIndices, HistoryLayoutCache layoutCache = null)
        {
            var acquired = new Acquisition();
            try
            {
                bool valid;
                valid = ReferenceEquals(obj, m_Object) && obj != null && obj.PersistentId == m_Identity && obj.PartVersion == m_PartVersion && obj.Parts.Count == m_PartCount && ReferenceEquals(selection, m_Selection) && version == m_Version && mode == m_Mode && scope == m_Scope && activePart == m_ActivePart && count == m_Count && m_HasSelection;
                if (valid)
                    foreach (var pair in m_Parts)
                    {
                        if ((uint)pair.Key >= (uint)obj.Parts.Count || !pair.Value.Matches(obj.Parts[pair.Key]) || pair.Value.Layout == null || (layoutCache == null ? GeometryHistory.Layout(obj.Parts[pair.Key].Mesh) : layoutCache.Layout(obj.Parts[pair.Key].Mesh)) != pair.Value.Layout.Fingerprint)
                        {
                            valid = false;
                            break;
                        }
                    }

                if (valid)
                {
                    foreach (var pair in m_Parts) acquired.Add(pair.Key, pair.Value.Layout);

                    return acquired;
                }

                Dispose();
                Dictionary<int, int[]> indices;
                indices = createIndices();
                Remember(obj, selection, version, mode, scope, activePart, count, indices);
                foreach (var pair in indices) acquired.Add(pair.Key, BuildLayout(pair.Key, pair.Value, layoutCache));
                return acquired;
            }
            catch
            {
                acquired.Dispose();
                Dispose();
                throw;
            }
        }

        private void Remember(ProceduralObject obj, object selection, int version, int mode, int scope, int activePart, int count, Dictionary<int, int[]> indices)
        {
            m_Object = obj;
            m_Identity = obj.PersistentId;
            m_PartVersion = obj.PartVersion;
            m_PartCount = obj.Parts.Count;
            m_Selection = selection;
            m_Version = version;
            m_Mode = mode;
            m_Scope = scope;
            m_ActivePart = activePart;
            m_Count = count;
            m_HasSelection = true;
            foreach (var pair in indices)
            {
                if (!obj.CanEditGeometry(pair.Key, out LocalizedText reason)) throw new LocalizedTextException(reason);
                var part = obj.Parts[pair.Key];
                m_Parts.Add(pair.Key, new Part
                {
                    Source = part, Identity = part.PartHistoryId, Version = part.TopologyVersion,
                    Mesh = part.Mesh, Topology = part.Topology, Positions = part.Mesh.Vertices, Normals = part.Mesh.Normals,
                    Tangents = part.Mesh.Tangents, Triangles = part.Mesh.Triangles
                });
            }
        }

        private VertexEditLayout BuildLayout(int index, int[] indices, HistoryLayoutCache layoutCache)
        {
            var part = m_Parts[index];
            long bytes = VertexEditLayout.Estimate(part.Mesh, indices.Length);
            if (bytes > kPartLimit || !m_Budget.Reserve(bytes))
            {
                // The ordinary uncached action remains available when reuse cannot fit.
                return new VertexEditLayout(part.Mesh, indices, part.Source, layoutCache: layoutCache);
            }

            var budget = m_Budget;
            VertexEditLayout layout;
            try
            {
                layout = new VertexEditLayout(part.Mesh, indices, part.Source, () => budget.Release(bytes), layoutCache: layoutCache);
            }
            catch
            {
                budget.Release(bytes);
                throw;
            }

            layout.Retain();
            part.Layout = layout;
            return layout;
        }

        public void Dispose()
        {
            foreach (var part in m_Parts.Values) part.Layout?.Release();
            m_Parts.Clear();
            m_HasSelection = false;
            m_Object = null;
            m_Selection = null;
        }
    }
}