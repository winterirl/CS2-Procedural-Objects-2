namespace PO2.Geometry.Editing
{
    internal enum TopologyEditBlockReason
    {
        None = 0,
        ConvertedTopology = 1,
        IncompleteStructuralTopology = 2,
        NoClosedQuadStrip = 3,
        NoOpenQuadStrip = 4,
        NoPartInScope = 5,
    }

    internal readonly struct TopologyEditCapabilities
    {
        public bool CanLoopCut { get; }
        public bool CanSurfaceCut { get; }
        public bool CanEditStructuralFaces { get; }
        public TopologyEditBlockReason LoopCutBlockReason { get; }
        public TopologyEditBlockReason SurfaceCutBlockReason { get; }
        public TopologyEditBlockReason FaceEditBlockReason { get; }

        public TopologyEditCapabilities(bool canLoopCut, bool canSurfaceCut, bool canEditStructuralFaces, TopologyEditBlockReason loopCutBlockReason, TopologyEditBlockReason surfaceCutBlockReason, TopologyEditBlockReason faceEditBlockReason)
        {
            CanLoopCut = canLoopCut;
            CanSurfaceCut = canSurfaceCut;
            CanEditStructuralFaces = canEditStructuralFaces;
            LoopCutBlockReason = canLoopCut ? TopologyEditBlockReason.None : loopCutBlockReason;
            SurfaceCutBlockReason = canSurfaceCut ? TopologyEditBlockReason.None : surfaceCutBlockReason;
            FaceEditBlockReason = canEditStructuralFaces ? TopologyEditBlockReason.None : faceEditBlockReason;
        }

        public static TopologyEditCapabilities Blocked(TopologyEditBlockReason reason)
        {
            return new TopologyEditCapabilities(false, false, false, reason, reason, reason);
        }
    }

    internal enum MeshCutKind
    {
        Loop = 0,
        Surface = 1,
    }
}