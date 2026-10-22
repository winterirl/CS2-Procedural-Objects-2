using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.Localization;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private bool m_HasMeshSelectionAvailability;
        private int m_MeshSelectionAvailabilityObjectId = -1;
        private int m_MeshSelectionAvailabilitySelectionVersion = -1;
        private int m_MeshSelectionAvailabilityVertexCount = -1;
        private int m_MeshSelectionAvailabilityEdgeCount = -1;
        private uint m_MeshSelectionAvailabilityGeometryVersion;
        private uint m_MeshSelectionAvailabilityPartVersion;
        private uint m_MeshSelectionAvailabilityDeformationVersion;
        private uint m_MeshSelectionAvailabilitySurfaceAreaStamp;
        private MeshSelectionMode m_MeshSelectionAvailabilitySelectionMode;
        private bool m_CanDissolveSelectedLoop;
        private bool m_CanDissolveSelectedSurfaceCut;
        private bool m_CanMergeSelectedVertices;

        public bool MeshEditRequiresBake
        {
            get
            {
                if (!TryGetMeshEditObject(out var obj) || m_MeshEditController == null) return false;
                foreach (var cage in obj.Deformations)
                {
                    if (cage.PartIndex < 0 || m_MeshEditController.IsPartInCurrentScope(cage.PartIndex)) return true;
                }

                return false;
            }
        }

        public bool CanMeshLoopCut =>
            GetMeshCutScopeAvailability(MeshCutKind.Loop).IsAvailable;

        public bool CanMeshSurfaceCut =>
            GetMeshCutScopeAvailability(MeshCutKind.Surface).IsAvailable;

        public bool CanDissolveSelectedLoop
        {
            get
            {
                RefreshMeshSelectionAvailability();
                return m_CanDissolveSelectedLoop;
            }
        }

        public bool CanDissolveSelectedSurfaceCut
        {
            get
            {
                RefreshMeshSelectionAvailability();
                return m_CanDissolveSelectedSurfaceCut;
            }
        }

        public bool CanMergeSelectedVertices
        {
            get
            {
                RefreshMeshSelectionAvailability();
                return m_CanMergeSelectedVertices;
            }
        }

        private void RefreshMeshSelectionAvailability()
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null || MeshEditRequiresBake)
            {
                m_HasMeshSelectionAvailability = false;
                m_CanDissolveSelectedLoop = false;
                m_CanDissolveSelectedSurfaceCut = false;
                m_CanMergeSelectedVertices = false;
                return;
            }

            int selectionVersion = m_MeshEditController.SelectionVersion;
            int vertexCount = m_MeshEditController.SelectionCount;
            int edgeCount = m_MeshEditController.EdgeSelectionCount;
            MeshSelectionMode selectionMode = m_MeshEditController.SelectionMode;
            uint surfaceAreaStamp = GetSurfaceAreaMembershipStamp(editedObject);

            if (m_HasMeshSelectionAvailability && m_MeshSelectionAvailabilityDeformationVersion == editedObject.DeformationVersion && m_MeshSelectionAvailabilityObjectId == editedObject.Id && m_MeshSelectionAvailabilitySelectionVersion == selectionVersion && m_MeshSelectionAvailabilityVertexCount == vertexCount && m_MeshSelectionAvailabilityEdgeCount == edgeCount && m_MeshSelectionAvailabilityGeometryVersion == editedObject.GeometryVersion && m_MeshSelectionAvailabilityPartVersion == editedObject.PartVersion && m_MeshSelectionAvailabilitySurfaceAreaStamp == surfaceAreaStamp && m_MeshSelectionAvailabilitySelectionMode == selectionMode)
            {
                return;
            }

            m_HasMeshSelectionAvailability = true;
            m_MeshSelectionAvailabilityDeformationVersion = editedObject.DeformationVersion;
            m_MeshSelectionAvailabilityObjectId = editedObject.Id;
            m_MeshSelectionAvailabilitySelectionVersion = selectionVersion;
            m_MeshSelectionAvailabilityVertexCount = vertexCount;
            m_MeshSelectionAvailabilityEdgeCount = edgeCount;
            m_MeshSelectionAvailabilityGeometryVersion = editedObject.GeometryVersion;
            m_MeshSelectionAvailabilityPartVersion = editedObject.PartVersion;
            m_MeshSelectionAvailabilitySurfaceAreaStamp = surfaceAreaStamp;
            m_MeshSelectionAvailabilitySelectionMode = selectionMode;
            m_CanDissolveSelectedLoop = false;
            m_CanDissolveSelectedSurfaceCut = false;
            m_CanMergeSelectedVertices = false;

            int firstPart = m_MeshEditController.FirstSelectedPart;
            if (firstPart >= 0 && firstPart < editedObject.Parts.Count && !editedObject.Parts[firstPart].SupportsDestructiveTopologyEditing) return;

            if (selectionMode == MeshSelectionMode.Edge)
            {
                if (TryGetDissolveLoopVerticesFromEdgeSelection(editedObject, out int loopPartIndex, out int[] loopVertexIds) && editedObject.CanEditGeometry(loopPartIndex, out _))
                {
                    m_CanDissolveSelectedLoop = TopologyEditor.TryAnalyzeLoopDissolve(editedObject.Parts[loopPartIndex], loopVertexIds, out LoopDissolvePlan _);
                }

                if (TryGetDissolveSurfaceCutVerticesFromEdgeSelection(editedObject, out int cutPartIndex, out int[] cutVertexIds) && editedObject.CanEditGeometry(cutPartIndex, out _))
                {
                    m_CanDissolveSelectedSurfaceCut = TopologyEditor.TryAnalyzeSurfaceCutDissolve(editedObject.Parts[cutPartIndex], cutVertexIds, out SurfaceCutDissolvePlan _);
                }

                return;
            }

            if (selectionMode != MeshSelectionMode.Vertex || !TryGetSinglePartVertexSelection(editedObject, out int partIndex, out int[] logicalVertexIds))
            {
                return;
            }

            MeshPart part = editedObject.Parts[partIndex];

            if (logicalVertexIds.Length >= 2)
            {
                m_CanMergeSelectedVertices = TopologyEditor.TryAnalyzeVertexMerge(part, logicalVertexIds, out VertexMergePlan _);
            }

            if (logicalVertexIds.Length >= 3)
            {
                m_CanDissolveSelectedLoop = TopologyEditor.TryAnalyzeLoopDissolve(part, logicalVertexIds, out LoopDissolvePlan _);
            }

            if (logicalVertexIds.Length >= 2)
            {
                m_CanDissolveSelectedSurfaceCut = TopologyEditor.TryAnalyzeSurfaceCutDissolve(part, logicalVertexIds, out SurfaceCutDissolvePlan _);
            }
        }

        private bool TryGetSinglePartVertexSelection(ProceduralObject editedObject, out int partIndex, out int[] logicalVertexIds)
        {
            partIndex = -1;
            logicalVertexIds = System.Array.Empty<int>();
            if (MeshEditRequiresBake) return false;

            VertexRef[] selection = m_MeshEditController.GetSelectionSnapshot();

            if (selection.Length == 0)
            {
                return false;
            }

            partIndex = selection[0].PartIndex;
            if (partIndex < 0 || partIndex >= editedObject.Parts.Count)
            {
                return false;
            }

            if (!editedObject.CanEditGeometry(partIndex, out _)) return false;

            logicalVertexIds = new int[selection.Length];

            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i].PartIndex != partIndex)
                {
                    partIndex = -1;
                    logicalVertexIds = System.Array.Empty<int>();
                    return false;
                }

                logicalVertexIds[i] = selection[i].VertexId;
            }

            return true;
        }

        private static uint GetSurfaceAreaMembershipStamp(ProceduralObject editedObject)
        {
            unchecked
            {
                uint stamp = 2166136261u;

                for (int partIndex = 0; partIndex < editedObject.Parts.Count; partIndex++)
                {
                    stamp ^= editedObject.Parts[partIndex].SurfaceAreaMembershipVersion;
                    stamp *= 16777619u;
                }

                return stamp;
            }
        }

        private MeshCutScopeAvailability GetMeshCutScopeAvailability(MeshCutKind cutKind)
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null)
            {
                return MeshCutScopeAvailability.Blocked(GetMeshCutUnavailableReason(TopologyEditBlockReason.NoPartInScope));
            }

            if (MeshEditRequiresBake)
                return MeshCutScopeAvailability.Blocked("Bake deformation to edit this mesh.");

            bool sawPart = false;
            bool sawConvertedTopology = false;
            bool sawIncompleteTopology = false;
            bool sawAuthoredTopologyWithoutStrip = false;

            for (int partIndex = 0; partIndex < editedObject.Parts.Count; partIndex++)
            {
                if (!m_MeshEditController.IsPartInCurrentScope(partIndex))
                {
                    continue;
                }

                if (!editedObject.CanEditGeometry(partIndex, out LocalizedText deformationReason))
                    return MeshCutScopeAvailability.Blocked(deformationReason.ToString());

                sawPart = true;
                TopologyEditCapabilities capabilities = editedObject.Parts[partIndex].TopologyEditCapabilities;

                if (IsMeshCutAvailable(capabilities, cutKind))
                {
                    return MeshCutScopeAvailability.s_Available;
                }

                TopologyEditBlockReason reason = GetMeshCutBlockReason(capabilities, cutKind);

                switch (reason)
                {
                    case TopologyEditBlockReason.ConvertedTopology:
                        sawConvertedTopology = true;
                        break;

                    case TopologyEditBlockReason.IncompleteStructuralTopology:
                        sawIncompleteTopology = true;
                        break;

                    case TopologyEditBlockReason.NoClosedQuadStrip:
                    case TopologyEditBlockReason.NoOpenQuadStrip:
                        sawAuthoredTopologyWithoutStrip = true;
                        break;
                }
            }

            TopologyEditBlockReason scopeReason;
            if (!sawPart)
            {
                scopeReason = TopologyEditBlockReason.NoPartInScope;
            }
            else if (sawAuthoredTopologyWithoutStrip)
            {
                scopeReason = cutKind == MeshCutKind.Loop ? TopologyEditBlockReason.NoClosedQuadStrip : TopologyEditBlockReason.NoOpenQuadStrip;
            }
            else if (sawIncompleteTopology)
            {
                scopeReason = TopologyEditBlockReason.IncompleteStructuralTopology;
            }
            else if (sawConvertedTopology)
            {
                scopeReason = TopologyEditBlockReason.ConvertedTopology;
            }
            else
            {
                scopeReason = TopologyEditBlockReason.NoPartInScope;
            }

            return MeshCutScopeAvailability.Blocked(GetMeshCutUnavailableReason(scopeReason));
        }

        private bool IsMeshCutPartInCurrentScope(int partIndex, MeshPart part, MeshCutKind cutKind)
        {
            if (m_MeshEditController == null || !m_MeshEditController.IsPartInCurrentScope(partIndex) || part == null)
            {
                return false;
            }

            return IsMeshCutAvailable(part.TopologyEditCapabilities, cutKind);
        }

        private static bool IsMeshCutAvailable(TopologyEditCapabilities capabilities, MeshCutKind cutKind)
        {
            return cutKind == MeshCutKind.Loop ? capabilities.CanLoopCut : capabilities.CanSurfaceCut;
        }

        private static TopologyEditBlockReason GetMeshCutBlockReason(TopologyEditCapabilities capabilities, MeshCutKind cutKind)
        {
            return cutKind == MeshCutKind.Loop ? capabilities.LoopCutBlockReason : capabilities.SurfaceCutBlockReason;
        }

        private static string GetMeshCutUnavailableReason(TopologyEditBlockReason reason)
        {
            switch (reason)
            {
                case TopologyEditBlockReason.ConvertedTopology:
                    return "Topology changes are unavailable for " + "converted geometry.";

                case TopologyEditBlockReason.IncompleteStructuralTopology:
                    return "This geometry does not have complete " + "structural face coverage for topology changes.";

                case TopologyEditBlockReason.NoClosedQuadStrip:
                    return "Loop Cut needs a closed strip of quads in " + "the current part scope.";

                case TopologyEditBlockReason.NoOpenQuadStrip:
                    return "Surface Cut needs an open strip of quads in " + "the current part scope.";

                default:
                    return "No mesh part is available in the current " + "part scope.";
            }
        }

        private readonly struct MeshCutScopeAvailability
        {
            public static readonly MeshCutScopeAvailability s_Available = new MeshCutScopeAvailability(true, string.Empty);

            public bool IsAvailable { get; }
            public string UnavailableReason { get; }

            private MeshCutScopeAvailability(bool isAvailable, string unavailableReason)
            {
                IsAvailable = isAvailable;
                UnavailableReason = unavailableReason ?? string.Empty;
            }

            public static MeshCutScopeAvailability Blocked(string reason)
            {
                return new MeshCutScopeAvailability(false, reason);
            }
        }
    }
}