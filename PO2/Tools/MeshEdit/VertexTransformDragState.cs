using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.Tools.Gizmos;
using PO2.Tools.Snapping;

namespace PO2.Tools.MeshEdit
{
    // A drag edits the live mesh for immediate feedback, so cancel needs an
    // exact snapshot of every render vertex touched. Logical vertices are not
    // enough here because UV seams can give one logical vertex several copies.
    public sealed class VertexTransformDragState
    {
        internal MeshTranslationSnapVertex[] SnapVertices;
        internal int SnapVertexCount;
        internal VertexHistoryEdit History { get; set; }

        public struct CapturedRenderVertex
        {
            public int PartIndex;
            public int RenderVertexIndex;
            public Vector3 StartLocalPosition;
        }

        private CapturedRenderVertex[] m_RenderVertices = new CapturedRenderVertex[16];

        private MeshPart[] m_CapturedParts = new MeshPart[4];
        private uint[] m_TopologyVersions = new uint[4];
        private int m_SelectionVersion;
        private System.Guid m_PersistentId;
        private Vector3[] m_TransformedPositions = new Vector3[16];
        private Matrix4x4 m_LastLocalTransform;
        private bool m_HasLocalTransform;

        private int[] m_AffectedParts = new int[4];

        public int ObjectId { get; set; }
        public GizmoHandle Handle { get; set; }
        public TransformFrame Frame { get; set; }
        internal TranslationConstraint Constraint { get; private set; }
        public Vector3 StartPivot { get; set; }
        internal Vector3 LocalPivot { get; set; }
        public float StartAxisParameter { get; set; }
        public Plane DragPlane { get; set; }
        public Vector3 StartMouseHit { get; set; }
        public float GizmoSize { get; set; }
        public ObjectTransform StartObjectTransform { get; set; }

        public int LogicalVertexCount { get; set; }
        public int RenderVertexCount { get; private set; }
        public int AffectedPartCount { get; private set; }

        public Vector3 LastAppliedLocalDelta { get; set; }
        public bool HasAppliedDelta { get; set; }

        public CapturedRenderVertex GetRenderVertex(int index)
        {
            return m_RenderVertices[index];
        }

        public int GetAffectedPartIndex(int index)
        {
            return m_AffectedParts[index];
        }

        internal Dictionary<int, int[]> CreateHistoryIndices()
        {
            int maximumPart = -1;
            for (int i = 0; i < AffectedPartCount; i++) maximumPart = Math.Max(maximumPart, m_AffectedParts[i]);
            // Part indices come from the active object. Reject invalid inputs
            // before sizing the compact table used by both capture passes.
            if (maximumPart >= 4096) throw new ArgumentOutOfRangeException("PartIndex");
            var cursors = new int[maximumPart + 1];
            var maximumVertex = new int[maximumPart + 1];
            var buffers = new int[maximumPart + 1][];
            for (int i = 0; i < RenderVertexCount; i++)
            {
                var entry = m_RenderVertices[i];
                if (entry.PartIndex < 0 || entry.RenderVertexIndex < 0) throw new ArgumentOutOfRangeException("Capture index");
                cursors[entry.PartIndex]++;
                maximumVertex[entry.PartIndex] = Math.Max(maximumVertex[entry.PartIndex], entry.RenderVertexIndex);
            }

            // These arrays belong to history and never alias mutable drag data.
            var result = new Dictionary<int, int[]>(AffectedPartCount);
            for (int i = 0; i < AffectedPartCount; i++)
            {
                int part = m_AffectedParts[i];
                buffers[part] = new int[cursors[part]];
                cursors[part] = 0;
            }

            for (int i = 0; i < RenderVertexCount; i++)
            {
                var entry = m_RenderVertices[i];
                buffers[entry.PartIndex][cursors[entry.PartIndex]++] = entry.RenderVertexIndex;
            }

            for (int i = 0; i < AffectedPartCount; i++)
            {
                int part = m_AffectedParts[i];
                int[] indices = buffers[part];
                int unique = VertexEditPreparationKernels.SortUnique(indices, checked(maximumVertex[part] + 1));
                if (unique != indices.Length) Array.Resize(ref indices, unique);
                result.Add(part, indices);
            }

            return result;
        }

        internal void BeginCapture(int objectId, GizmoHandle handle, TransformFrame frame, TranslationConstraint constraint, Vector3 startPivot, float startAxisParameter, Plane dragPlane, Vector3 startMouseHit, float gizmoSize, ObjectTransform startObjectTransform, int logicalVertexCount)
        {
            ObjectId = objectId;
            Handle = handle;
            Frame = frame;
            Constraint = constraint;
            StartPivot = startPivot;
            StartAxisParameter = startAxisParameter;
            DragPlane = dragPlane;
            StartMouseHit = startMouseHit;
            GizmoSize = gizmoSize;
            StartObjectTransform = startObjectTransform;
            LogicalVertexCount = logicalVertexCount;

            RenderVertexCount = 0;
            AffectedPartCount = 0;
            LastAppliedLocalDelta = Vector3.zero;
            HasAppliedDelta = false;
            m_HasLocalTransform = false;
        }

        public void AddRenderVertex(int partIndex, int renderVertexIndex, Vector3 startLocalPosition)
        {
            EnsureRenderVertexCapacity(RenderVertexCount + 1);

            m_RenderVertices[RenderVertexCount++] = new CapturedRenderVertex
            {
                PartIndex = partIndex,
                RenderVertexIndex = renderVertexIndex,
                StartLocalPosition = startLocalPosition,
            };

            // Parts are deduplicated separately so normals/tangents only get
            // rebuilt once per part after a drag update.
            AddAffectedPart(partIndex);
        }

        internal void CaptureTargets(ProceduralObject obj, int selectionVersion)
        {
            m_SelectionVersion = selectionVersion;
            m_PersistentId = obj.PersistentId;
            for (int i = 0; i < AffectedPartCount; i++)
            {
                MeshPart part = obj.Parts[m_AffectedParts[i]];
                m_CapturedParts[i] = part;
                m_TopologyVersions[i] = part.TopologyVersion;
            }
        }

        internal bool MatchesTargets(ProceduralObject obj, int selectionVersion)
        {
            if (obj == null || obj.Id != ObjectId || obj.PersistentId != m_PersistentId || obj.IsLocked || !obj.Transform.Position.Equals(StartObjectTransform.Position) || !obj.Transform.Scale.Equals(StartObjectTransform.Scale) || obj.Transform.Rotation.x != StartObjectTransform.Rotation.x || obj.Transform.Rotation.y != StartObjectTransform.Rotation.y || obj.Transform.Rotation.z != StartObjectTransform.Rotation.z || obj.Transform.Rotation.w != StartObjectTransform.Rotation.w || selectionVersion != m_SelectionVersion)
            {
                return false;
            }

            for (int i = 0; i < AffectedPartCount; i++)
            {
                int partIndex = m_AffectedParts[i];
                if (partIndex < 0 || partIndex >= obj.Parts.Count || obj.Parts[partIndex] != m_CapturedParts[i] || obj.Parts[partIndex].TopologyVersion != m_TopologyVersions[i])
                {
                    return false;
                }
            }

            return true;
        }

        internal bool TryPrepareTransform(Matrix4x4 localFromStart, bool neutral, out bool changed)
        {
            changed = false;
            if (m_HasLocalTransform && SelectionTransformMath.SameMatrix(m_LastLocalTransform, localFromStart))
            {
                return true;
            }

            for (int i = 0; i < RenderVertexCount; i++)
            {
                Vector3 start = m_RenderVertices[i].StartLocalPosition;
                Vector3 position = neutral ? start : LocalPivot + localFromStart.MultiplyVector(start - LocalPivot);
                if (!SelectionTransformMath.IsFinite(position))
                {
                    return false;
                }

                if (!position.Equals(m_HasLocalTransform ? m_TransformedPositions[i] : start))
                {
                    changed = true;
                }

                m_TransformedPositions[i] = position;
            }

            m_LastLocalTransform = localFromStart;
            m_HasLocalTransform = true;
            return true;
        }

        internal Vector3 GetTransformedPosition(int index)
        {
            return m_TransformedPositions[index];
        }

        public void Reset()
        {
            SnapVertices = null;
            SnapVertexCount = 0;
            History?.Dispose();
            History = null;
            System.Array.Clear(m_CapturedParts, 0, AffectedPartCount);
            ObjectId = 0;
            Handle = GizmoHandle.None;
            Frame = default;
            Constraint = default;
            StartPivot = Vector3.zero;
            LocalPivot = Vector3.zero;
            StartAxisParameter = 0f;
            DragPlane = default;
            StartMouseHit = Vector3.zero;
            GizmoSize = 0f;
            StartObjectTransform = default;
            LogicalVertexCount = 0;
            RenderVertexCount = 0;
            AffectedPartCount = 0;
            LastAppliedLocalDelta = Vector3.zero;
            HasAppliedDelta = false;
            m_HasLocalTransform = false;
        }

        private void AddAffectedPart(int partIndex)
        {
            for (int i = 0; i < AffectedPartCount; i++)
            {
                if (m_AffectedParts[i] == partIndex)
                {
                    return;
                }
            }

            EnsureAffectedPartCapacity(AffectedPartCount + 1);

            m_AffectedParts[AffectedPartCount++] = partIndex;
        }

        private void EnsureRenderVertexCapacity(int requiredCount)
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
            System.Array.Resize(ref m_TransformedPositions, newCapacity);
        }

        private void EnsureAffectedPartCapacity(int requiredCount)
        {
            if (m_AffectedParts.Length >= requiredCount)
            {
                return;
            }

            int newCapacity = m_AffectedParts.Length;

            while (newCapacity < requiredCount)
            {
                newCapacity *= 2;
            }

            System.Array.Resize(ref m_AffectedParts, newCapacity);
            System.Array.Resize(ref m_CapturedParts, newCapacity);
            System.Array.Resize(ref m_TopologyVersions, newCapacity);
        }
    }
}