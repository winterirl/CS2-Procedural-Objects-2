using UnityEngine;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Transforms;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;

namespace PO2.Tools.Gizmos
{
    internal enum PartTransformKind
    {
        Translation,
        Rotation,
        Scale,
    }

    // Part transforms are baked into mesh streams.
    // This owns the one start snapshot shared by every preview frame and the exact Escape rollback.
    internal sealed class PartTransformDragState
    {
        private const float kAppliedTransformEpsilon = 0.000001f;
        private const float kMinimumRootDeterminantMagnitude = 0.000000000001f;

        private ProceduralObject m_Object;
        private MeshPart m_Part;
        private PartGeometrySnapshot m_Snapshot;
        private HistoryEdit m_History;
        private PartTransformAction m_HistoryAction;
        private Matrix4x4 m_ObjectLocalToWorld;
        private Matrix4x4 m_WorldToObjectLocal;
        private PartTransformKind m_TransformKind;
        private Vector3 m_StartLocalPivot;
        private bool m_HasAppliedTransform;
        private Matrix4x4 m_LastAppliedTransform;

        internal int ObjectId { get; private set; } = -1;
        internal int PartIndex { get; private set; } = -1;
        internal Vector3 StartPivotWorld { get; private set; }
        internal Vector3 CurrentPivotWorld { get; private set; }
        internal bool HasPreview { get; private set; }
        internal MeshPart Part => m_Part;

        internal bool IsCaptured =>
            m_Object != null && m_Part != null && m_Snapshot != null;

        internal bool TryBegin(ProceduralObject proceduralObject, int partIndex, Vector3 pivotWorld, PartTransformKind transformKind, ProceduralObjectSystem historySystem, out string failureReason)
        {
            if (historySystem.ActionHistory.IsBusy)
            {
                failureReason = "Finish the current history edit before starting a part transform.";
                return false;
            }

            Reset();
            failureReason = string.Empty;

            if (proceduralObject == null || partIndex < 0 || partIndex >= proceduralObject.Parts.Count)
            {
                failureReason = "The active part is not available.";
                return false;
            }

            if (!proceduralObject.CanEditGeometry(partIndex, out LocalizedText deformationReason))
            {
                failureReason = deformationReason.ToString();
                return false;
            }

            MeshPart part = proceduralObject.Parts[partIndex];

            if (!PartGeometryTransform.TryCaptureSnapshot(part, transformKind == PartTransformKind.Translation, out PartGeometrySnapshot snapshot, out failureReason))
            {
                return false;
            }

            ObjectTransform transform = proceduralObject.Transform;

            Matrix4x4 objectLocalToWorld = Matrix4x4.TRS(transform.Position, transform.Rotation, transform.Scale);

            if (!IsFinite(objectLocalToWorld) || Mathf.Abs(objectLocalToWorld.determinant) < kMinimumRootDeterminantMagnitude)
            {
                failureReason = "The PO root transform is singular or non-finite.";
                return false;
            }

            Matrix4x4 worldToObjectLocal = objectLocalToWorld.inverse;

            if (!IsFinite(worldToObjectLocal))
            {
                failureReason = "The PO root transform does not have a finite inverse.";
                return false;
            }

            Vector3 startPivotWorld = transform.TransformPoint(part.LocalPivot);
            if (!IsFinite(startPivotWorld) || !IsFinite(pivotWorld))
            {
                failureReason = "The active part pivot is non-finite.";
                return false;
            }

            m_HistoryAction = new PartTransformAction(proceduralObject, partIndex, snapshot, transformKind == PartTransformKind.Translation ? LocalizedText.For("PO2.History.Action.Part.Translate") : transformKind == PartTransformKind.Rotation ? LocalizedText.For("PO2.History.Action.Part.Rotate") : LocalizedText.For("PO2.History.Action.Part.Scale"));
            m_History = historySystem.BeginPartTransformHistory(m_HistoryAction);
            if (m_History == null)
            {
                m_HistoryAction.Dispose();
                m_HistoryAction = null;
                failureReason = "History is busy or cannot reserve this part edit.";
                return false;
            }

            m_Object = proceduralObject;
            m_Part = part;
            m_Snapshot = snapshot;
            m_ObjectLocalToWorld = objectLocalToWorld;
            m_WorldToObjectLocal = worldToObjectLocal;
            m_TransformKind = transformKind;
            m_StartLocalPivot = part.LocalPivot;
            ObjectId = proceduralObject.Id;
            PartIndex = partIndex;
            StartPivotWorld = startPivotWorld;
            CurrentPivotWorld = startPivotWorld;
            return true;
        }

        internal bool Matches(ProceduralObject proceduralObject)
        {
            return IsCaptured && ReferenceEquals(m_Object, proceduralObject) && ObjectId == proceduralObject.Id && PartIndex >= 0 && PartIndex < proceduralObject.Parts.Count && ReferenceEquals(proceduralObject.Parts[PartIndex], m_Part);
        }

        internal bool TryApplyWorldTransform(ProceduralObject proceduralObject, Matrix4x4 worldFromStart, out bool changed, out string failureReason)
        {
            changed = false;
            failureReason = string.Empty;

            if (!Matches(proceduralObject))
            {
                failureReason = "The active part changed during the transform.";
                return false;
            }

            Matrix4x4 objectLocalFromStart = m_WorldToObjectLocal * worldFromStart * m_ObjectLocalToWorld;

            if (!IsFinite(objectLocalFromStart))
            {
                failureReason = "The part transform produced a non-finite preview matrix.";
                return false;
            }

            Vector3 nextLocalPivot = objectLocalFromStart.MultiplyPoint3x4(m_StartLocalPivot);
            Vector3 nextWorldPivot = m_ObjectLocalToWorld.MultiplyPoint3x4(nextLocalPivot);
            if (!IsFinite(nextLocalPivot) || !IsFinite(nextWorldPivot))
            {
                failureReason = "The part transform produced a non-finite pivot.";
                return false;
            }

            // The matrix is the actual preview recipe. A single scalar could
            // not distinguish two plane translations with equal magnitude.
            if (m_HasAppliedTransform && ApproximatelyEqual(objectLocalFromStart, m_LastAppliedTransform))
            {
                return true;
            }

            if (!PartGeometryTransform.TryApplyFromSnapshot(m_Part, m_Snapshot, objectLocalFromStart, out failureReason))
            {
                return false;
            }

            if (!m_Part.SetLocalPivot(nextLocalPivot) && !m_Part.LocalPivot.Equals(nextLocalPivot))
            {
                failureReason = "The part pivot could not follow the transform.";
                return false;
            }

            if (!proceduralObject.TryMarkPartGeometryPreviewChanged(PartIndex, m_Part, m_TransformKind == PartTransformKind.Translation))
            {
                failureReason = "The active part changed before the preview was published.";
                return false;
            }

            m_HistoryAction.SetRecipe(objectLocalFromStart);
            m_HasAppliedTransform = true;
            m_LastAppliedTransform = objectLocalFromStart;
            HasPreview = true;
            CurrentPivotWorld = nextWorldPivot;
            changed = true;
            return true;
        }

        internal bool TryComplete(ProceduralObject proceduralObject, out string failureReason)
        {
            failureReason = string.Empty;

            if (!HasPreview)
            {
                return m_History != null && m_History.Complete();
            }

            if (!Matches(proceduralObject) || !proceduralObject.TryCommitPartGeometryChange(PartIndex, m_Part))
            {
                failureReason = "The active part changed before the transform completed.";
                return false;
            }

            if (m_History == null || !m_History.Complete())
            {
                failureReason = "The part transform could not be recorded and was restored.";
                return false;
            }

            return true;
        }

        internal bool TryCancel(ProceduralObject proceduralObject, out string failureReason)
        {
            failureReason = string.Empty;

            if (!HasPreview)
            {
                return true;
            }

            if (!Matches(proceduralObject))
            {
                failureReason = "The active part changed before it could be restored.";
                return false;
            }

            m_History?.Dispose();
            if (m_History == null || !m_History.RestoreSucceeded)
            {
                failureReason = "The part transform capture could not be restored.";
                return false;
            }

            CurrentPivotWorld = StartPivotWorld;
            return true;
        }

        internal void Reset()
        {
            m_History?.Dispose();
            m_History = null;
            m_HistoryAction = null;
            m_Object = null;
            m_Part = null;
            m_Snapshot = null;
            m_ObjectLocalToWorld = default;
            m_WorldToObjectLocal = default;
            m_TransformKind = default;
            m_StartLocalPivot = Vector3.zero;
            m_HasAppliedTransform = false;
            m_LastAppliedTransform = default;
            ObjectId = -1;
            PartIndex = -1;
            StartPivotWorld = Vector3.zero;
            CurrentPivotWorld = Vector3.zero;
            HasPreview = false;
        }

        private static bool ApproximatelyEqual(Matrix4x4 left, Matrix4x4 right)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    float leftValue = left[row, column];
                    float rightValue = right[row, column];

                    if (Mathf.Abs(leftValue - rightValue) > kAppliedTransformEpsilon)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsFinite(Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    float value = matrix[row, column];
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}