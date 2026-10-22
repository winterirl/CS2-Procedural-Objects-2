using UnityEngine;

namespace PO2.Tools.Gizmos
{
    // Rebase when Shift or snap settings change during a drag so the gizmo does not jump.
    public struct GizmoDragModifierState
    {
        private const float kPrecisionSensitivity = 0.1f;
        private const float kChangeEpsilon = 0.000001f;

        private bool m_Initialized;
        private bool m_OutputPrecision;
        private bool m_SnapEnabled;
        private bool m_HasIncrementState;

        private float m_NormalIncrement;
        private float m_PrecisionIncrement;

        private float m_LastRawInput;
        private float m_EffectiveInput;
        private float m_LastUnsnappedValue;
        private float m_SegmentBaseValue;
        private float m_SegmentUnsnappedDelta;
        private float m_LastResolvedValue;

        public void Begin(bool precision, bool snapEnabled)
        {
            m_Initialized = true;
            m_OutputPrecision = precision;
            m_SnapEnabled = snapEnabled;
            m_HasIncrementState = false;
            m_NormalIncrement = 0f;
            m_PrecisionIncrement = 0f;
            m_LastRawInput = 0f;
            m_EffectiveInput = 0f;
            m_LastUnsnappedValue = 0f;
            m_SegmentBaseValue = 0f;
            m_SegmentUnsnappedDelta = 0f;
            m_LastResolvedValue = 0f;
        }

        public float UpdateEffectiveInput(float rawInput, bool precision, bool cyclicDegrees = false)
        {
            if (!m_Initialized)
            {
                Begin(precision, false);
            }

            float rawDelta = cyclicDegrees ? Mathf.DeltaAngle(m_LastRawInput, rawInput) : rawInput - m_LastRawInput;

            m_LastRawInput = rawInput;

            m_EffectiveInput += rawDelta * (precision ? kPrecisionSensitivity : 1f);

            return m_EffectiveInput;
        }

        public bool Resolve(float unsnappedValue, bool precision, bool snapEnabled, float normalIncrement, float precisionIncrement, out float resolvedValue)
        {
            if (!m_Initialized)
            {
                Begin(precision, snapEnabled);
            }

            float previousResolved = m_LastResolvedValue;
            float unsnappedDelta = unsnappedValue - m_LastUnsnappedValue;

            bool incrementsChanged = m_HasIncrementState && (snapEnabled || m_SnapEnabled) && (Mathf.Abs(normalIncrement - m_NormalIncrement) > kChangeEpsilon || Mathf.Abs(precisionIncrement - m_PrecisionIncrement) > kChangeEpsilon);

            bool modeChanged = precision != m_OutputPrecision || snapEnabled != m_SnapEnabled || incrementsChanged;

            // Start a new segment from the last visible result.
            // Only movement after the setting change uses the new precision/snap rules.
            if (modeChanged)
            {
                m_SegmentBaseValue = m_LastResolvedValue;
                m_SegmentUnsnappedDelta = 0f;
                m_OutputPrecision = precision;
                m_SnapEnabled = snapEnabled;
            }

            m_HasIncrementState = true;
            m_NormalIncrement = normalIncrement;
            m_PrecisionIncrement = precisionIncrement;

            m_SegmentUnsnappedDelta += unsnappedDelta;
            m_LastUnsnappedValue = unsnappedValue;

            float segmentValue = m_SegmentUnsnappedDelta;

            if (snapEnabled)
            {
                float increment = precision ? precisionIncrement : normalIncrement;

                if (increment > 0f)
                {
                    segmentValue = Mathf.Round(segmentValue / increment) * increment;
                }
            }

            resolvedValue = m_SegmentBaseValue + segmentValue;

            m_LastResolvedValue = resolvedValue;

            return Mathf.Abs(resolvedValue - previousResolved) > kChangeEpsilon;
        }

        public bool ResolveLinear(float rawInput, bool precision, bool snapEnabled, float normalIncrement, float precisionIncrement, out float resolvedValue, bool cyclicDegrees = false)
        {
            float effectiveInput = UpdateEffectiveInput(rawInput, precision, cyclicDegrees);

            return Resolve(effectiveInput, precision, snapEnabled, normalIncrement, precisionIncrement, out resolvedValue);
        }

        public void Reset()
        {
            this = default;
        }
    }
}