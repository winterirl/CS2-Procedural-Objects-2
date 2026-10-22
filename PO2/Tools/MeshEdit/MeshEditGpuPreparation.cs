using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Tools.MeshEdit
{
    // Main-thread stamp plus bounded copies of mutable mesh streams. Workers may
    // read only the copied streams and the immutable, constructed topology objects.
    internal sealed class MeshEditGpuSource
    {
        internal sealed class Part
        {
            internal MeshPart Identity;
            internal MeshData MeshIdentity;
            internal MeshTopology Topology;
            internal StructuralTopology StructuralTopology;
            internal uint GeometryVersion, TopologyVersion;
            internal Vector3[] LiveVertices, LiveNormals;
            internal int[] LiveTriangles;
            internal BudgetedCopy.Stream<Vector3> Positions, Directions;
            internal BudgetedCopy.Stream<int> Indices;
            internal Vector3[] Vertices => Positions.Data;
            internal Vector3[] Normals => Directions?.Data;
            internal int[] Triangles => Indices?.Data;
        }

        internal readonly ProceduralObject Object;
        internal readonly Guid PersistentId;
        internal readonly MeshSelectionMode Mode;
        internal readonly MeshEditVisibilityMode Visibility;
        internal bool NeedsOcclusion => Visibility == MeshEditVisibilityMode.Solid;
        internal readonly Part[] Parts;
        internal readonly BudgetedCopy Copy = new BudgetedCopy();
        internal readonly long CandidateCount, TriangleCount;

        internal MeshEditGpuSource(ProceduralObject obj, MeshSelectionMode mode, MeshEditVisibilityMode visibility = MeshEditVisibilityMode.Solid)
        {
            Object = obj;
            PersistentId = obj.PersistentId;
            Mode = mode;
            Visibility = visibility;
            Parts = new Part[obj.Parts.Count];
            for (int i = 0; i < Parts.Length; i++)
            {
                var part = obj.Parts[i];
                var mesh = part.Mesh;
                var p = Parts[i] = new Part
                {
                    Identity = part, MeshIdentity = mesh, Topology = part.Topology, StructuralTopology = part.StructuralTopology,
                    GeometryVersion = part.GeometryVersion, TopologyVersion = part.TopologyVersion,
                    LiveVertices = mesh.Vertices, LiveNormals = mesh.Normals, LiveTriangles = mesh.Triangles,
                };
                p.Positions = Copy.Add(p.LiveVertices, 12);
                if (NeedsOcclusion)
                {
                    p.Directions = Copy.Add(p.LiveNormals, 12);
                    p.Indices = Copy.Add(p.LiveTriangles, 4);
                    TriangleCount += p.LiveTriangles.Length / 3;
                }

                CandidateCount += mode == MeshSelectionMode.Vertex ? p.Topology.LogicalVertexCount : p.StructuralTopology.StructuralEdgeCount;
            }

            if (CandidateCount > 1048576)
                throw new InvalidOperationException("Mesh marker preparation exceeds the marker count limit.");
        }

        // Owner thread only. Transforms/cameras/scope do not change local geometry;
        // their latest values are used after publication without restarting a build.
        internal bool Matches(ProceduralObject obj, MeshSelectionMode mode)
        {
            if (!ReferenceEquals(obj, Object) || obj.PersistentId != PersistentId || mode != Mode || obj.Parts.Count != Parts.Length) return false;
            for (int i = 0; i < Parts.Length; i++)
            {
                var p = Parts[i];
                var live = obj.Parts[i];
                if (!ReferenceEquals(p.Identity, live) || p.GeometryVersion != live.GeometryVersion || p.TopologyVersion != live.TopologyVersion || !ReferenceEquals(p.MeshIdentity, live.Mesh) || !ReferenceEquals(p.Topology, live.Topology) || !ReferenceEquals(p.StructuralTopology, live.StructuralTopology) || !ReferenceEquals(p.LiveVertices, live.Mesh.Vertices) || !ReferenceEquals(p.LiveNormals, live.Mesh.Normals) || !ReferenceEquals(p.LiveTriangles, live.Mesh.Triangles)) return false;
            }

            return true;
        }
    }

    internal sealed class MeshEditGpuPreparation
    {
        internal readonly MeshEditGpuSource Source;
        private readonly CancellationTokenSource m_Cancellation = new CancellationTokenSource();
        private readonly MeshEditGpuGeometry m_Tree;
        private Task<MeshEditGpuGeometry> m_Worker;
        internal Task Completion => (Task)m_Worker ?? Source.Copy.Allocation;
        internal bool ReusedTree => m_Tree != null;

        internal MeshEditGpuPreparation(ProceduralObject obj, MeshSelectionMode mode, MeshEditGpuGeometry reuse, MeshEditVisibilityMode visibility = MeshEditVisibilityMode.Solid)
        {
            Source = new MeshEditGpuSource(obj, mode, visibility);
            // Only retain the shared tree, not the previous mode's candidates/maps.
            // The session detaches the old generation before starting this worker.
            if (Source.NeedsOcclusion && reuse != null)
                m_Tree = new MeshEditGpuGeometry
                {
                    Nodes = reuse.Nodes, Triangles = reuse.Triangles, Parents = reuse.Parents,
                    TriangleLeaves = reuse.TriangleLeaves, TriangleSources = reuse.TriangleSources,
                    OrthoLength = reuse.OrthoLength, TreeSerial = reuse.TreeSerial,
                };
            Source.Copy.Start(m_Cancellation.Token);
        }

        internal MeshEditGpuGeometry Advance()
        {
            if (m_Worker == null)
            {
                if (!Source.Copy.Poll(() => Source.Matches(Source.Object, Source.Mode), m_Cancellation.Token)) return null;
                var token = m_Cancellation.Token;
                m_Worker = Task.Run(() => { return MeshEditGpuGeometry.Build(Source, m_Tree, token); }, token);
                return null;
            }

            return m_Worker.IsCompleted ? m_Worker.GetAwaiter().GetResult() : null;
        }

        internal void Cancel()
        {
            m_Cancellation.Cancel();
        }

        internal void Release()
        {
            // Called only after completion; observe faults even on abandoned work.
            if (Completion.IsFaulted)
            {
                var observed = Completion.Exception;
            }

            m_Cancellation.Dispose();
        }
    }
}