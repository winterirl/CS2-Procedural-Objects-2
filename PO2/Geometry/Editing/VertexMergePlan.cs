using System;
using System.Collections.Generic;
using UnityEngine;

namespace PO2.Geometry.Editing
{
    // Collapsing selected vertices can leave a face alone, turn a quad into a
    // triangle, or eat the face entirely. The analysis pass spells that out
    // per face before we touch any vertex streams.
    public enum VertexMergeFaceResult
    {
        Unchanged,
        Quad,
        Triangle,
        Removed,
    }

    public readonly struct VertexMergeCornerPlan
    {
        // SourceCornerIndex is ignored for the synthetic merged corner.
        public bool IsMergedCorner { get; }
        public int SourceCornerIndex { get; }

        public VertexMergeCornerPlan(bool isMergedCorner, int sourceCornerIndex)
        {
            IsMergedCorner = isMergedCorner;
            SourceCornerIndex = sourceCornerIndex;
        }
    }

    public sealed class VertexMergeFacePlan
    {
        private readonly VertexMergeCornerPlan[] m_ResultCorners;
        private readonly int[] m_MergedSourceCornerIndices;

        public int SourceFaceId { get; }
        public VertexMergeFaceResult Result { get; }

        public IReadOnlyList<VertexMergeCornerPlan> ResultCorners =>
            m_ResultCorners;

        public IReadOnlyList<int> MergedSourceCornerIndices =>
            m_MergedSourceCornerIndices;

        public int ResultCornerCount => m_ResultCorners.Length;

        public VertexMergeFacePlan(int sourceFaceId, VertexMergeFaceResult result, VertexMergeCornerPlan[] resultCorners, int[] mergedSourceCornerIndices)
        {
            SourceFaceId = sourceFaceId;
            Result = result;
            m_ResultCorners = resultCorners ?? Array.Empty<VertexMergeCornerPlan>();
            m_MergedSourceCornerIndices = mergedSourceCornerIndices ?? Array.Empty<int>();
        }
    }

    public sealed class VertexMergePlan
    {
        // This also doubles as useful preview/debug information.
        // If a merge is rejected none of this gets applied to the actual mesh.
        private readonly int[] m_SelectedLogicalVertexIds;
        private readonly VertexMergeFacePlan[] m_Faces;

        public IReadOnlyList<int> SelectedLogicalVertexIds =>
            m_SelectedLogicalVertexIds;

        public Vector3 TargetPosition { get; }
        public IReadOnlyList<VertexMergeFacePlan> Faces => m_Faces;
        public int ResultFaceCount { get; }
        public int ResultQuadCount { get; }
        public int ResultTriangleCount { get; }
        public int RemovedFaceCount { get; }
        public int AffectedFaceCount { get; }

        public VertexMergePlan(int[] selectedLogicalVertexIds, Vector3 targetPosition, VertexMergeFacePlan[] faces, int resultFaceCount, int resultQuadCount, int resultTriangleCount, int removedFaceCount, int affectedFaceCount)
        {
            m_SelectedLogicalVertexIds = selectedLogicalVertexIds ?? Array.Empty<int>();
            TargetPosition = targetPosition;
            m_Faces = faces ?? Array.Empty<VertexMergeFacePlan>();
            ResultFaceCount = resultFaceCount;
            ResultQuadCount = resultQuadCount;
            ResultTriangleCount = resultTriangleCount;
            RemovedFaceCount = removedFaceCount;
            AffectedFaceCount = affectedFaceCount;
        }
    }
}