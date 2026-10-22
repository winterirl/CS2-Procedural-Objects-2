using System;
using UnityEngine;
using PO2.Tools.MeshEdit;

namespace PO2.Geometry.Editing
{
    internal sealed class FaceInsetLoop
    {
        private readonly Vector2[] m_OuterPoints;

        public FaceRegionBoundaryLoop RegionLoop { get; }
        public Vector2[] OuterPoints => m_OuterPoints;
        public float SignedArea { get; }

        public FaceInsetLoop(FaceRegionBoundaryLoop regionLoop, Vector2[] outerPoints, float signedArea)
        {
            RegionLoop = regionLoop ?? throw new ArgumentNullException(nameof(regionLoop));
            m_OuterPoints = outerPoints ?? Array.Empty<Vector2>();
            SignedArea = signedArea;
        }
    }

    internal sealed class FaceInsetPlan
    {
        private readonly FaceInsetLoop[] m_Loops;

        public FaceRegionPlan RegionPlan { get; }
        public FaceInsetLoop[] Loops => m_Loops;
        public Vector3 WorldAxisU { get; }
        public Vector3 WorldAxisV { get; }
        public int LogicalVertexCount { get; }
        public int OuterLoopIndex { get; }
        public float WorldTolerance { get; }

        public FaceInsetPlan(FaceRegionPlan regionPlan, FaceInsetLoop[] loops, Vector3 worldAxisU, Vector3 worldAxisV, int logicalVertexCount, int outerLoopIndex, float worldTolerance)
        {
            RegionPlan = regionPlan ?? throw new ArgumentNullException(nameof(regionPlan));
            m_Loops = loops ?? Array.Empty<FaceInsetLoop>();
            WorldAxisU = worldAxisU;
            WorldAxisV = worldAxisV;
            LogicalVertexCount = logicalVertexCount;
            OuterLoopIndex = outerLoopIndex;
            WorldTolerance = worldTolerance;
        }

        public Vector2 ProjectWorldPoint(Vector3 worldPoint)
        {
            Vector3 offset = worldPoint - RegionPlan.WorldPivot;
            return new Vector2(Vector3.Dot(offset, WorldAxisU), Vector3.Dot(offset, WorldAxisV));
        }

        public Vector3 ExpandWorldPoint(Vector2 planePoint)
        {
            return RegionPlan.WorldPivot + WorldAxisU * planePoint.x + WorldAxisV * planePoint.y;
        }
    }

    internal sealed class FaceInsetEvaluation
    {
        private readonly bool[] m_MovedLogicalVertices;
        private readonly Vector2[] m_InnerPlanePositions;
        private readonly Vector3[] m_InnerLocalPositions;
        private readonly Vector2[][] m_InnerLoops;
        private readonly LoopCutPreviewLine[] m_PreviewLines;

        public float Amount { get; }
        public bool CanCommit { get; }
        public bool[] MovedLogicalVertices => m_MovedLogicalVertices;
        public Vector2[] InnerPlanePositions => m_InnerPlanePositions;
        public Vector3[] InnerLocalPositions => m_InnerLocalPositions;
        public Vector2[][] InnerLoops => m_InnerLoops;
        public LoopCutPreviewLine[] PreviewLines => m_PreviewLines;

        public FaceInsetEvaluation(float amount, bool canCommit, bool[] movedLogicalVertices, Vector2[] innerPlanePositions, Vector3[] innerLocalPositions, Vector2[][] innerLoops, LoopCutPreviewLine[] previewLines)
        {
            Amount = amount;
            CanCommit = canCommit;
            m_MovedLogicalVertices = movedLogicalVertices ?? Array.Empty<bool>();
            m_InnerPlanePositions = innerPlanePositions ?? Array.Empty<Vector2>();
            m_InnerLocalPositions = innerLocalPositions ?? Array.Empty<Vector3>();
            m_InnerLoops = innerLoops ?? Array.Empty<Vector2[]>();
            m_PreviewLines = previewLines ?? Array.Empty<LoopCutPreviewLine>();
        }
    }

    internal sealed class FaceInsetMutation
    {
        public MeshMutationResult MeshMutation { get; }
        public int[] InnerRenderVertexIndices { get; }
        public int[] InnerBoundaryRenderVertexPairs { get; }

        public FaceInsetMutation(MeshMutationResult meshMutation, int[] innerRenderVertexIndices, int[] innerBoundaryRenderVertexPairs)
        {
            MeshMutation = meshMutation ?? throw new ArgumentNullException(nameof(meshMutation));
            InnerRenderVertexIndices = innerRenderVertexIndices ?? Array.Empty<int>();
            InnerBoundaryRenderVertexPairs = innerBoundaryRenderVertexPairs ?? Array.Empty<int>();
        }
    }
}