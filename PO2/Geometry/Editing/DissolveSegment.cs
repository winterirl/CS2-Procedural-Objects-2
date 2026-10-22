namespace PO2.Geometry.Editing
{
    // Caches the selected edge and its two quads so dissolve can match corners
    // without rediscovering them after the winding changes.
    public readonly struct DissolveSegment
    {
        public int StartLogicalVertexId { get; }
        public int EndLogicalVertexId { get; }
        public int StructuralEdgeId { get; }

        public int SideAQuadId { get; }
        public int SideABoundaryIndex { get; }
        public int SideAOuterStartLogicalVertexId { get; }
        public int SideAOuterEndLogicalVertexId { get; }

        public int SideBQuadId { get; }
        public int SideBBoundaryIndex { get; }
        public int SideBOuterStartLogicalVertexId { get; }
        public int SideBOuterEndLogicalVertexId { get; }

        internal DissolveSegment(int startLogicalVertexId, int endLogicalVertexId, int structuralEdgeId, int sideAQuadId, int sideABoundaryIndex, int sideAOuterStartLogicalVertexId, int sideAOuterEndLogicalVertexId, int sideBQuadId, int sideBBoundaryIndex, int sideBOuterStartLogicalVertexId, int sideBOuterEndLogicalVertexId)
        {
            StartLogicalVertexId = startLogicalVertexId;
            EndLogicalVertexId = endLogicalVertexId;
            StructuralEdgeId = structuralEdgeId;

            SideAQuadId = sideAQuadId;
            SideABoundaryIndex = sideABoundaryIndex;
            SideAOuterStartLogicalVertexId = sideAOuterStartLogicalVertexId;
            SideAOuterEndLogicalVertexId = sideAOuterEndLogicalVertexId;

            SideBQuadId = sideBQuadId;
            SideBBoundaryIndex = sideBBoundaryIndex;
            SideBOuterStartLogicalVertexId = sideBOuterStartLogicalVertexId;
            SideBOuterEndLogicalVertexId = sideBOuterEndLogicalVertexId;
        }
    }
}