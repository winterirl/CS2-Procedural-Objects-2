using System;
using UnityEngine;
using PO2.Core;
using PO2.Tools.Arrays;
using PO2.Tools.Gizmos;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private PartArrayMode m_ArrayMode;
        private int m_ArrayCopies = 1;
        private bool m_ArrayCopiesInitialized;
        private float m_ArrayPathAngle;
        private TransformFrame m_ArrayPathFrame;

        public int ArrayMode => (int)m_ArrayMode;
        public int ArrayCapacity => ArrayActive ? Math.Max(0, ProceduralObject.kMaxPartCount - m_ArrayObject.Parts.Count) : 0;
        public int ArrayCopies => Math.Min(m_ArrayCopies, ArrayCapacity);
        public float ArraySpacing => m_ArrayLayoutValid ? (float)(ArrayIsCurve ? m_ArrayPlacements?.Spacing ?? 0 : m_ArrayLayout.Stride) : 0;
        public float ArrayPathAngle => m_ArrayPathAngle;
        public float ArrayPathLength => ArrayActive ? PartArrayPathMath.Length(m_ArrayEndpoint - m_ArrayStart) : 0;
        private bool CanEditArrayPath => ArrayActive && !ArrayBusy && !ArrayDragging;

        private void InitializeArrayPath()
        {
            m_ArrayPathFrame = ResolveActiveTransformFrame(m_ArrayObject);
            m_ArrayPathAngle = 0;
            m_ArrayPlane = new Plane(m_ArrayPathFrame.YAxis, m_ArrayStart);
        }

        private void SyncArrayPathFrame()
        {
            // A drag owns its starting frame until release, including rotation drags.
            if (!CanEditArrayPath) return;
            TransformFrame frame = ResolveActiveTransformFrame(m_ArrayObject);
            if (PartArrayPathMath.SameFrame(frame, m_ArrayPathFrame)) return;
            Vector3 previousHeading = PartArrayPathMath.Heading(m_ArrayPathFrame, m_ArrayPathAngle);
            float fallback = PartArrayPathMath.Angle(previousHeading, frame, m_ArrayPathAngle);
            m_ArrayPathAngle = PartArrayPathMath.Angle(m_ArrayEndpoint - m_ArrayStart, frame, fallback);
            m_ArrayPathFrame = frame;
            m_ArrayPlane = new Plane(frame.YAxis, m_ArrayStart);
            ClearGizmoHover();
        }

        public void SetArrayMode(int value)
        {
            if (!CanEditArrayPath || value < (int)PartArrayMode.FixedGap || value > (int)PartArrayMode.FixedCount) return;
            if ((PartArrayMode)value == PartArrayMode.FixedCount && !m_ArrayCopiesInitialized)
            {
                m_ArrayCopies = Math.Max(1, ArrayCount);
                m_ArrayCopiesInitialized = true;
            }

            m_ArrayMode = (PartArrayMode)value;
            RefreshArrayLayout();
        }

        public void SetArrayCopies(int value)
        {
            if (!CanEditArrayPath) return;
            m_ArrayCopies = Math.Max(1, Math.Min(ArrayCapacity, value));
            m_ArrayCopiesInitialized = true;
            RefreshArrayLayout();
        }

        public void SetArrayPathAngle(float value)
        {
            if (!CanEditArrayPath || !PartArrayLayout.Finite(value)) return;
            SyncArrayPathFrame();
            m_ArrayPathAngle = PartArrayPathMath.NormalizeAngle(value);
            SetArrayEndpoint(m_ArrayStart + PartArrayPathMath.WithAngle(m_ArrayEndpoint - m_ArrayStart, m_ArrayPathFrame, m_ArrayPathAngle));
            FinishNumericArrayEndpoint();
        }

        public void SetArrayPathLength(float value)
        {
            if (!CanEditArrayPath || !PartArrayLayout.Finite(value) || value < 0) return;
            SyncArrayPathFrame();
            SetArrayEndpoint(m_ArrayStart + PartArrayPathMath.WithLength(m_ArrayEndpoint - m_ArrayStart, m_ArrayPathFrame, m_ArrayPathAngle, value));
            FinishNumericArrayEndpoint();
        }

        private void FinishNumericArrayEndpoint()
        {
            if (!m_ArrayDrawing || ArrayPathLength < 0.00001f) return;
            FinishArrayGesture();
            m_ArrayDrawing = false;
            m_ArraySceneReady = false;
            if (ArrayIsCurve) RefreshArrayLayout();
            ClearGizmoHover();
        }
    }
}