using System;
using UnityEngine;
using PO2.Core;

namespace PO2.Geometry.Editing
{
    internal readonly struct FaceRegionBoundaryEdge
    {
        public int SourceFaceId { get; }
        public int SourceBoundaryIndex { get; }
        public int StructuralEdgeId { get; }
        public int LogicalVertexA { get; }
        public int LogicalVertexB { get; }
        public int SourceRenderVertexA { get; }
        public int SourceRenderVertexB { get; }
        public int PayloadRenderVertexA { get; }
        public int PayloadRenderVertexB { get; }
        public Vector3 LocalPositionA { get; }
        public Vector3 LocalPositionB { get; }
        public float Length { get; }

        public FaceRegionBoundaryEdge(int sourceFaceId, int sourceBoundaryIndex, int structuralEdgeId, int logicalVertexA, int logicalVertexB, int sourceRenderVertexA, int sourceRenderVertexB, int payloadRenderVertexA, int payloadRenderVertexB, Vector3 localPositionA, Vector3 localPositionB, float length)
        {
            SourceFaceId = sourceFaceId;
            SourceBoundaryIndex = sourceBoundaryIndex;
            StructuralEdgeId = structuralEdgeId;
            LogicalVertexA = logicalVertexA;
            LogicalVertexB = logicalVertexB;
            SourceRenderVertexA = sourceRenderVertexA;
            SourceRenderVertexB = sourceRenderVertexB;
            PayloadRenderVertexA = payloadRenderVertexA;
            PayloadRenderVertexB = payloadRenderVertexB;
            LocalPositionA = localPositionA;
            LocalPositionB = localPositionB;
            Length = length;
        }
    }

    internal sealed class FaceRegionBoundaryLoop
    {
        private readonly FaceRegionBoundaryEdge[] m_Edges;

        public FaceRegionBoundaryEdge[] Edges => m_Edges;
        public int EdgeCount => m_Edges.Length;
        public float TotalLength { get; }

        public FaceRegionBoundaryLoop(FaceRegionBoundaryEdge[] edges, float totalLength)
        {
            m_Edges = edges ?? Array.Empty<FaceRegionBoundaryEdge>();
            TotalLength = totalLength;
        }
    }

    internal readonly struct FaceRegionPreviewEdge
    {
        public int LogicalVertexA { get; }
        public int LogicalVertexB { get; }
        public Vector3 LocalPositionA { get; }
        public Vector3 LocalPositionB { get; }

        public FaceRegionPreviewEdge(int logicalVertexA, int logicalVertexB, Vector3 localPositionA, Vector3 localPositionB)
        {
            LogicalVertexA = logicalVertexA;
            LogicalVertexB = logicalVertexB;
            LocalPositionA = localPositionA;
            LocalPositionB = localPositionB;
        }
    }

    internal sealed class FaceRegionPlan
    {
        private readonly int[] m_SelectedFaceIds;
        private readonly bool[] m_SelectedFaces;
        private readonly FaceRegionBoundaryLoop[] m_BoundaryLoops;
        private readonly FaceRegionPreviewEdge[] m_SelectedEdges;

        public int[] SelectedFaceIds => m_SelectedFaceIds;
        public bool[] SelectedFaces => m_SelectedFaces;

        public FaceRegionBoundaryLoop[] BoundaryLoops =>
            m_BoundaryLoops;

        public FaceRegionPreviewEdge[] SelectedEdges =>
            m_SelectedEdges;

        public ObjectTransform SourceTransform { get; }
        public Vector3 WorldNormal { get; }
        public Vector3 WorldPivot { get; }
        public Vector3 LocalNormalPerWorldMeter { get; }
        public float WorldSpan { get; }
        public int BoundaryEdgeCount { get; }

        public FaceRegionPlan(int[] selectedFaceIds, bool[] selectedFaces, FaceRegionBoundaryLoop[] boundaryLoops, FaceRegionPreviewEdge[] selectedEdges, ObjectTransform sourceTransform, Vector3 worldNormal, Vector3 worldPivot, Vector3 localNormalPerWorldMeter, float worldSpan, int boundaryEdgeCount)
        {
            m_SelectedFaceIds = selectedFaceIds ?? Array.Empty<int>();
            m_SelectedFaces = selectedFaces ?? Array.Empty<bool>();
            m_BoundaryLoops = boundaryLoops ?? Array.Empty<FaceRegionBoundaryLoop>();
            m_SelectedEdges = selectedEdges ?? Array.Empty<FaceRegionPreviewEdge>();
            SourceTransform = sourceTransform;
            WorldNormal = worldNormal;
            WorldPivot = worldPivot;
            LocalNormalPerWorldMeter = localNormalPerWorldMeter;
            WorldSpan = worldSpan;
            BoundaryEdgeCount = boundaryEdgeCount;
        }
    }
}