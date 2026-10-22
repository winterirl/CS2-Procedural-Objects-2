namespace PO2.Tools.Snapping
{
    // Shared snapping preferences for the current tool session. They are not saved with POs.
    public sealed class SnapSettings
    {
        public const float kDefaultMoveIncrement = 1f;
        public const float kDefaultPrecisionMoveIncrement = 0.1f;
        public const float kDefaultRotationIncrement = 5f;
        public const float kDefaultPrecisionRotationIncrement = 1f;
        public const float kDefaultScaleIncrement = 0.1f;
        public const float kDefaultPrecisionScaleIncrement = 0.01f;

        public bool MagnetEnabled { get; private set; }
        public bool IncrementEnabled { get; private set; } = true;
        public bool POVerticesEnabled { get; private set; } = true;
        public bool POEdgesEnabled { get; private set; }
        public bool GameSurfacesEnabled { get; private set; } = true;

        public float MoveIncrement { get; private set; } = kDefaultMoveIncrement;

        public float PrecisionMoveIncrement { get; private set; } = kDefaultPrecisionMoveIncrement;

        public float RotationIncrement { get; private set; } = kDefaultRotationIncrement;

        public float PrecisionRotationIncrement { get; private set; } = kDefaultPrecisionRotationIncrement;

        public float ScaleIncrement { get; private set; } = kDefaultScaleIncrement;

        public float PrecisionScaleIncrement { get; private set; } = kDefaultPrecisionScaleIncrement;

        public bool IsSnappingActive(bool controlPressed)
        {
            return MagnetEnabled != controlPressed;
        }

        public bool IsIncrementSnappingActive(bool controlPressed)
        {
            return IncrementEnabled && IsSnappingActive(controlPressed);
        }

        public void ToggleMagnet()
        {
            MagnetEnabled = !MagnetEnabled;
        }

        public void ToggleIncrement()
        {
            IncrementEnabled = !IncrementEnabled;
        }

        public void TogglePOVertices()
        {
            POVerticesEnabled = !POVerticesEnabled;
        }

        public void TogglePOEdges()
        {
            POEdgesEnabled = !POEdgesEnabled;
        }

        public void ToggleGameSurfaces()
        {
            GameSurfacesEnabled = !GameSurfacesEnabled;
        }

        public bool SetMoveIncrement(float value)
        {
            if (!IsValidIncrement(value))
            {
                return false;
            }

            MoveIncrement = value;
            return true;
        }

        public bool SetPrecisionMoveIncrement(float value)
        {
            if (!IsValidIncrement(value))
            {
                return false;
            }

            PrecisionMoveIncrement = value;
            return true;
        }

        public bool SetRotationIncrement(float value)
        {
            if (!IsValidIncrement(value))
            {
                return false;
            }

            RotationIncrement = value;
            return true;
        }

        public bool SetPrecisionRotationIncrement(float value)
        {
            if (!IsValidIncrement(value))
            {
                return false;
            }

            PrecisionRotationIncrement = value;
            return true;
        }

        public bool SetScaleIncrement(float value)
        {
            if (!IsValidIncrement(value))
            {
                return false;
            }

            ScaleIncrement = value;
            return true;
        }

        public bool SetPrecisionScaleIncrement(float value)
        {
            if (!IsValidIncrement(value))
            {
                return false;
            }

            PrecisionScaleIncrement = value;
            return true;
        }

        public void ResetIncrements()
        {
            MoveIncrement = kDefaultMoveIncrement;
            PrecisionMoveIncrement = kDefaultPrecisionMoveIncrement;
            RotationIncrement = kDefaultRotationIncrement;
            PrecisionRotationIncrement = kDefaultPrecisionRotationIncrement;
            ScaleIncrement = kDefaultScaleIncrement;
            PrecisionScaleIncrement = kDefaultPrecisionScaleIncrement;
        }

        private static bool IsValidIncrement(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}