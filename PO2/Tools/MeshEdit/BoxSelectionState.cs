using UnityEngine;

namespace PO2.Tools.MeshEdit
{
    public enum BoxSelectionMode
    {
        Replace,
        Add,
        Remove,
    }

    // A mouse-down is still a click until it moves far enough. Keeping Pending
    // separate from Active stops tiny hand movement from eating normal clicks.
    public sealed class BoxSelectionState
    {
        private const float kDragThresholdPixels = 5f;
        private const float kDragThresholdSquared = kDragThresholdPixels * kDragThresholdPixels;

        public bool IsPending { get; private set; }
        public bool IsActive { get; private set; }
        public BoxSelectionMode Mode { get; private set; }
        public Vector2 StartScreenPosition { get; private set; }
        public Vector2 CurrentScreenPosition { get; private set; }

        public bool IsGestureActive => IsPending || IsActive;

        public void Begin(Vector2 screenPosition, BoxSelectionMode mode)
        {
            StartScreenPosition = screenPosition;
            CurrentScreenPosition = screenPosition;
            Mode = mode;
            IsPending = true;
            IsActive = false;
        }

        public bool Update(Vector2 screenPosition)
        {
            if (!IsGestureActive)
            {
                return false;
            }

            CurrentScreenPosition = screenPosition;

            // Squared distance because there is no reason to do a square root
            // every mouse move just to compare against five pixels.
            if (!IsActive && (CurrentScreenPosition - StartScreenPosition).sqrMagnitude >= kDragThresholdSquared)
            {
                IsPending = false;
                IsActive = true;
                return true;
            }

            return false;
        }

        public Rect GetScreenRect()
        {
            float xMin = Mathf.Min(StartScreenPosition.x, CurrentScreenPosition.x);
            float xMax = Mathf.Max(StartScreenPosition.x, CurrentScreenPosition.x);
            float yMin = Mathf.Min(StartScreenPosition.y, CurrentScreenPosition.y);
            float yMax = Mathf.Max(StartScreenPosition.y, CurrentScreenPosition.y);

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        public void Reset()
        {
            IsPending = false;
            IsActive = false;
            StartScreenPosition = Vector2.zero;
            CurrentScreenPosition = Vector2.zero;
        }
    }
}