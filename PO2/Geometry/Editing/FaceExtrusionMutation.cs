using System;

namespace PO2.Geometry.Editing
{
    internal sealed class FaceExtrusionMutation
    {
        public MeshMutationResult MeshMutation { get; }
        public int[] CapRenderVertexIndices { get; }
        public int[] CapBoundaryRenderVertexPairs { get; }

        public FaceExtrusionMutation(MeshMutationResult meshMutation, int[] capRenderVertexIndices, int[] capBoundaryRenderVertexPairs)
        {
            MeshMutation = meshMutation ?? throw new ArgumentNullException(nameof(meshMutation));
            CapRenderVertexIndices = capRenderVertexIndices ?? Array.Empty<int>();
            CapBoundaryRenderVertexPairs = capBoundaryRenderVertexPairs ?? Array.Empty<int>();
        }
    }
}