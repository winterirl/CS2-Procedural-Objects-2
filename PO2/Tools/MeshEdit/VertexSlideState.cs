using UnityEngine;
using PO2.Core;
using PO2.Geometry.Topology;
using PO2.History;

namespace PO2.Tools.MeshEdit
{
    // Same snapshot idea as a normal vertex drag, but this operation is locked
    // to one structural edge and stores both ends in local and world space.
    public sealed class VertexSlideState
    {
        internal VertexHistoryEdit History { get; set; }

        public struct CapturedRenderVertex
        {
            public int PartIndex;
            public int RenderVertexIndex;
            public Vector3 StartLocalPosition;
        }

        private CapturedRenderVertex[] m_RenderVertices = new CapturedRenderVertex[8];

        public int ObjectId { get; set; }
        public VertexRef SourceVertex { get; set; }
        public VertexRef TargetVertex { get; set; }
        public Vector3 StartLocalPosition { get; set; }
        public Vector3 TargetLocalPosition { get; set; }
        public Vector3 WorldStartPosition { get; set; }
        public Vector3 WorldTargetPosition { get; set; }
        public Vector3 WorldAxis { get; set; }
        public float WorldEdgeLength { get; set; }
        public float StartAxisParameter { get; set; }
        public ObjectTransform StartObjectTransform { get; set; }
        public int RenderVertexCount { get; private set; }
        public float LastAppliedT { get; set; }
        public bool HasAppliedSlide { get; set; }

        public CapturedRenderVertex GetRenderVertex(int index)
        {
            return m_RenderVertices[index];
        }

        public void BeginCapture(int objectId, VertexRef sourceVertex, VertexRef targetVertex, Vector3 startLocalPosition, Vector3 targetLocalPosition, Vector3 worldStartPosition, Vector3 worldTargetPosition, ObjectTransform startObjectTransform)
        {
            ObjectId = objectId;
            SourceVertex = sourceVertex;
            TargetVertex = targetVertex;
            StartLocalPosition = startLocalPosition;
            TargetLocalPosition = targetLocalPosition;
            WorldStartPosition = worldStartPosition;
            WorldTargetPosition = worldTargetPosition;

            Vector3 worldDelta = worldTargetPosition - worldStartPosition;

            WorldEdgeLength = worldDelta.magnitude;
            // Zero length is left as an invalid state for the controller to
            // reject. Normalizing it here would just introduce NaN.
            WorldAxis = WorldEdgeLength > 0.000001f ? worldDelta / WorldEdgeLength : Vector3.zero;

            StartAxisParameter = 0f;
            StartObjectTransform = startObjectTransform;
            RenderVertexCount = 0;
            LastAppliedT = 0f;
            HasAppliedSlide = false;
        }

        public void AddRenderVertex(int partIndex, int renderVertexIndex, Vector3 startLocalPosition)
        {
            EnsureCapacity(RenderVertexCount + 1);

            m_RenderVertices[RenderVertexCount++] = new CapturedRenderVertex
            {
                PartIndex = partIndex,
                RenderVertexIndex = renderVertexIndex,
                StartLocalPosition = startLocalPosition,
            };
        }

        public void Reset()
        {
            History?.Dispose();
            History = null;
            ObjectId = 0;
            SourceVertex = default;
            TargetVertex = default;
            StartLocalPosition = Vector3.zero;
            TargetLocalPosition = Vector3.zero;
            WorldStartPosition = Vector3.zero;
            WorldTargetPosition = Vector3.zero;
            WorldAxis = Vector3.zero;
            WorldEdgeLength = 0f;
            StartAxisParameter = 0f;
            StartObjectTransform = default;
            RenderVertexCount = 0;
            LastAppliedT = 0f;
            HasAppliedSlide = false;
        }

        private void EnsureCapacity(int requiredCount)
        {
            if (m_RenderVertices.Length >= requiredCount)
            {
                return;
            }

            int newCapacity = m_RenderVertices.Length;

            while (newCapacity < requiredCount)
            {
                newCapacity *= 2;
            }

            System.Array.Resize(ref m_RenderVertices, newCapacity);
        }
    }
}