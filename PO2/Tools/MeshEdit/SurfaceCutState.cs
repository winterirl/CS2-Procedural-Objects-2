using System;
using UnityEngine;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    public enum SurfaceCutPhase
    {
        None = 0,
        Targeting = 1,
        Positioning = 2,
    }

    // This is deliberately its own state even though it currently mirrors
    // LoopCutState. A Surface Cut targets an open strip and has different
    // validity rules, so sharing one mutable state would get confusing fast.
    public sealed class SurfaceCutState
    {
        private LoopCutPreviewLine[] m_PreviewLines = Array.Empty<LoopCutPreviewLine>();

        public SurfaceCutPhase Phase { get; private set; }
        public int ObjectId { get; private set; } = -1;
        public int PartIndex { get; private set; } = -1;
        public int StructuralEdgeId { get; private set; } = -1;
        public StructuralLoop Loop { get; private set; }
        public uint SourcePartGeometryVersion { get; private set; }
        public uint SourcePartTopologyVersion { get; private set; }
        public uint PreviewVersion { get; private set; }
        public float CurrentT { get; private set; } = 0.5f;
        public int CutCount { get; private set; } = 1;
        public Vector3 WorldEdgeStart { get; private set; }
        public Vector3 WorldEdgeAxis { get; private set; }
        public float WorldEdgeLength { get; private set; }

        private float m_LastAxisParameter;
        private bool m_CenterHeld;

        public bool IsActive => Phase != SurfaceCutPhase.None;
        public bool IsTargeting => Phase == SurfaceCutPhase.Targeting;
        public bool IsPositioning => Phase == SurfaceCutPhase.Positioning;

        public bool HasCandidate =>
            PartIndex >= 0 && StructuralEdgeId >= 0 && Loop != null;

        public LoopCutPreviewLine[] PreviewLines => m_PreviewLines;
        public int PreviewLineCount => m_PreviewLines.Length;

        public void Begin(int objectId)
        {
            ObjectId = objectId;
            Phase = SurfaceCutPhase.Targeting;
            PartIndex = -1;
            StructuralEdgeId = -1;
            Loop = null;
            SourcePartGeometryVersion = 0;
            SourcePartTopologyVersion = 0;
            CurrentT = 0.5f;
            CutCount = 1;
            WorldEdgeStart = Vector3.zero;
            WorldEdgeAxis = Vector3.zero;
            WorldEdgeLength = 0f;
            m_LastAxisParameter = 0f;
            m_CenterHeld = false;
            SetPreviewLines(Array.Empty<LoopCutPreviewLine>());
        }

        public void SetTargetCandidate(int partIndex, int structuralEdgeId, StructuralLoop loop, uint sourcePartGeometryVersion, uint sourcePartTopologyVersion, LoopCutPreviewLine[] previewLines)
        {
            if (!IsTargeting)
            {
                return;
            }

            // Candidate ids only mean anything for the mesh versions they came
            // from. A rebuild can recycle both part and edge ids.
            bool identityChanged = PartIndex != partIndex || StructuralEdgeId != structuralEdgeId || SourcePartGeometryVersion != sourcePartGeometryVersion || SourcePartTopologyVersion != sourcePartTopologyVersion;

            PartIndex = partIndex;
            StructuralEdgeId = structuralEdgeId;
            Loop = loop;
            SourcePartGeometryVersion = sourcePartGeometryVersion;
            SourcePartTopologyVersion = sourcePartTopologyVersion;

            if (identityChanged || !ReferenceEquals(m_PreviewLines, previewLines))
            {
                SetPreviewLines(previewLines);
            }
        }

        public void ClearTargetCandidate()
        {
            if (!IsTargeting || !HasCandidate)
            {
                return;
            }

            PartIndex = -1;
            StructuralEdgeId = -1;
            Loop = null;
            SourcePartGeometryVersion = 0;
            SourcePartTopologyVersion = 0;
            CurrentT = 0.5f;
            WorldEdgeStart = Vector3.zero;
            WorldEdgeAxis = Vector3.zero;
            WorldEdgeLength = 0f;
            m_LastAxisParameter = 0f;
            m_CenterHeld = false;
            SetPreviewLines(Array.Empty<LoopCutPreviewLine>());
        }

        public bool LockCandidate(Vector3 worldEdgeStart, Vector3 worldEdgeEnd, float startAxisParameter)
        {
            if (!IsTargeting || !HasCandidate)
            {
                return false;
            }

            Vector3 worldDelta = worldEdgeEnd - worldEdgeStart;
            float worldLength = worldDelta.magnitude;

            if (worldLength <= 0.000001f)
            {
                return false;
            }

            // Movement along the locked world edge becomes normalized t in
            // UpdatePositionInput. This makes sensitivity independent of size.
            Phase = SurfaceCutPhase.Positioning;
            CurrentT = 0.5f;
            WorldEdgeStart = worldEdgeStart;
            WorldEdgeAxis = worldDelta / worldLength;
            WorldEdgeLength = worldLength;
            m_LastAxisParameter = startAxisParameter;
            m_CenterHeld = false;
            PreviewVersion++;
            return true;
        }

        public bool UpdatePositionInput(float currentAxisParameter, bool precision, bool center)
        {
            if (!IsPositioning || WorldEdgeLength <= 0.000001f)
            {
                return false;
            }

            float rawDelta = currentAxisParameter - m_LastAxisParameter;
            m_LastAxisParameter = currentAxisParameter;

            if (center)
            {
                m_CenterHeld = true;

                if (Mathf.Abs(CurrentT - 0.5f) <= 0.000001f)
                {
                    return false;
                }

                CurrentT = 0.5f;
                return true;
            }

            if (m_CenterHeld)
            {
                // Rebase at the pointer position reached while Ctrl was held.
                // The first free frame therefore resumes from center without
                // jumping back to the pre-center mouse offset.
                m_CenterHeld = false;
                return false;
            }

            float sensitivity = precision ? 0.1f : 1f;
            float deltaT = rawDelta / WorldEdgeLength * sensitivity;
            float nextT = CutLayout.ClampCenter(CutCount, CurrentT + deltaT);

            if (Mathf.Abs(nextT - CurrentT) <= 0.000001f)
            {
                return false;
            }

            CurrentT = nextT;
            return true;
        }

        public bool ChangeCutCount(int delta)
        {
            if (!IsActive || !HasCandidate || delta == 0) return false;
            int nextCount = (int)Math.Max(1L, Math.Min(CutLayout.kMaxCuts, (long)CutCount + delta));
            if (nextCount == CutCount) return false;

            CutCount = nextCount;
            CurrentT = CutLayout.ClampCenter(CutCount, CurrentT);
            SetPreviewLines(new LoopCutPreviewLine[checked(Loop.StepCount * CutCount + 1)]);
            return true;
        }

        public void NotifyPreviewChanged()
        {
            PreviewVersion++;
        }

        public void Reset()
        {
            Phase = SurfaceCutPhase.None;
            ObjectId = -1;
            PartIndex = -1;
            StructuralEdgeId = -1;
            Loop = null;
            SourcePartGeometryVersion = 0;
            SourcePartTopologyVersion = 0;
            CurrentT = 0.5f;
            CutCount = 1;
            WorldEdgeStart = Vector3.zero;
            WorldEdgeAxis = Vector3.zero;
            WorldEdgeLength = 0f;
            m_LastAxisParameter = 0f;
            m_CenterHeld = false;
            SetPreviewLines(Array.Empty<LoopCutPreviewLine>());
        }

        private void SetPreviewLines(LoopCutPreviewLine[] previewLines)
        {
            m_PreviewLines = previewLines ?? Array.Empty<LoopCutPreviewLine>();
            PreviewVersion++;
        }
    }
}