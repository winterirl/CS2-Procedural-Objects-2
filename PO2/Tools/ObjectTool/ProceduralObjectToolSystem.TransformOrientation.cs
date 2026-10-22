using Game;
using Game.Input;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Tools.Gizmos;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private TransformOrientation m_TransformOrientation = TransformOrientation.Local;

        private TransformOrientation m_LastStandardTransformOrientation = TransformOrientation.Local;

        private int m_ActiveCustomOrientationObjectId = -1;
        private int m_ActiveCustomOrientationIndex = -1;
        private CustomOrientation m_ActiveCustomOrientation;

        public TransformOrientation TransformOrientation =>
            m_TransformOrientation;

        public int ActiveCustomOrientationIndex =>
            m_ActiveCustomOrientationIndex;

        private TransformFrame ResolveActiveTransformFrame(ProceduralObject proceduralObject)
        {
            if (m_TransformOrientation == TransformOrientation.Custom)
            {
                if (TryResolveActiveCustomOrientation(proceduralObject, out CustomOrientation orientation))
                {
                    return TransformFrame.FromObjectRelativeRotation(proceduralObject.Transform.Rotation, orientation.LocalRotation);
                }

                return ResolveStandardTransformFrame(proceduralObject, m_LastStandardTransformOrientation);
            }

            return ResolveStandardTransformFrame(proceduralObject, m_TransformOrientation);
        }

        private static TransformFrame ResolveStandardTransformFrame(ProceduralObject proceduralObject, TransformOrientation orientation)
        {
            if (proceduralObject == null || orientation == TransformOrientation.Global)
            {
                return TransformFrame.Global;
            }

            return TransformFrame.FromUprightHeading(proceduralObject.Transform.Rotation);
        }

        private static TransformFrame ResolveScaleFrame(ProceduralObject proceduralObject)
        {
            return proceduralObject == null ? TransformFrame.Global : TransformFrame.FromObjectRotation(proceduralObject.Transform.Rotation);
        }

        public void SetGlobalTransformOrientation()
        {
            SetTransformOrientation(TransformOrientation.Global);
        }

        public void SetLocalTransformOrientation()
        {
            SetTransformOrientation(TransformOrientation.Local);
        }

        public void SetCustomTransformOrientation(int orientationIndex)
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || !selected.TryGetCustomOrientation(orientationIndex, out CustomOrientation orientation))
            {
                Mod.log.Warn($"[PO Gizmo] Cannot select custom orientation " + $"{orientationIndex}; it is not available on " + "the selected object.");
                return;
            }

            if (m_TransformOrientation == TransformOrientation.Custom && m_ActiveCustomOrientationObjectId == selected.Id && ReferenceEquals(m_ActiveCustomOrientation, orientation))
            {
                m_ActiveCustomOrientationIndex = orientationIndex;
                return;
            }

            CancelGizmoDrag(restoreTransform: true);
            ClearGizmoHover();

            m_TransformOrientation = TransformOrientation.Custom;
            m_ActiveCustomOrientationObjectId = selected.Id;
            m_ActiveCustomOrientationIndex = orientationIndex;
            m_ActiveCustomOrientation = orientation;
        }

        public void RenameActiveCustomOrientation(string name)
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || selected.IsLocked || m_TransformOrientation != TransformOrientation.Custom || !TryResolveActiveCustomOrientation(selected, out CustomOrientation orientation))
            {
                Mod.log.Warn("[PO Gizmo] Cannot rename a custom orientation " + "because no editable custom orientation is active.");
                return;
            }

            string previousName = orientation.Name;
            if (string.Equals(previousName, name?.Trim(), System.StringComparison.Ordinal))
            {
                return;
            }

            using (var history = m_ProceduralObjectSystem.BeginObjectHistory(selected, ObjectValueKind.Orientations, LocalizedText.For("PO2.History.Action.CustomAxis.Rename")))
            {
                if (history == null) return;
                if (!selected.RenameCustomOrientation(m_ActiveCustomOrientationIndex, name))
                {
                    Mod.log.Warn("[PO Gizmo] Custom orientation names must contain " + $"between 1 and {CustomOrientation.kMaxNameUtf8Bytes} UTF-8 bytes.");
                    return;
                }

                if (!history.Complete()) return;
            }
        }

        public void DeleteActiveCustomOrientation()
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || selected.IsLocked || m_TransformOrientation != TransformOrientation.Custom || !TryResolveActiveCustomOrientation(selected, out CustomOrientation orientation))
            {
                Mod.log.Warn("[PO Gizmo] Cannot delete a custom orientation " + "because no editable custom orientation is active.");
                return;
            }

            int deletedIndex = m_ActiveCustomOrientationIndex;

            using (var history = m_ProceduralObjectSystem.BeginObjectHistory(selected, ObjectValueKind.Orientations, LocalizedText.For("PO2.History.Action.CustomAxis.Delete")))
            {
                if (history == null) return;
                if (!selected.RemoveCustomOrientation(deletedIndex))
                {
                    Mod.log.Warn($"[PO Gizmo] Failed to delete custom orientation " + $"{deletedIndex} from object {selected.Id}.");
                    return;
                }

                if (!history.Complete()) return;
            }

            SetTransformOrientation(m_LastStandardTransformOrientation);
        }

        private void SetTransformOrientation(TransformOrientation orientation)
        {
            if (orientation != TransformOrientation.Global && orientation != TransformOrientation.Local)
            {
                Mod.log.Warn($"[PO Gizmo] Transform orientation {orientation} is not implemented.");
                return;
            }

            if (m_TransformOrientation == orientation)
            {
                m_LastStandardTransformOrientation = orientation;
                ClearActiveCustomTransformOrientation();
                return;
            }

            CancelGizmoDrag(restoreTransform: true);
            ClearGizmoHover();
            m_TransformOrientation = orientation;
            m_LastStandardTransformOrientation = orientation;
            ClearActiveCustomTransformOrientation();
        }

        private bool TryBuildCustomOrientationRotation(ProceduralObject editedObject, StructuralEdgeRef edgeRef, out Quaternion localRotation, out TransformAxis matchedAxis)
        {
            localRotation = Quaternion.identity;
            matchedAxis = TransformAxis.X;

            if (!TryGetStructuralEdgeWorldDirection(editedObject, edgeRef, out Vector3 edgeDirection) || !CustomOrientation.TryNormalizeRotation(editedObject.Transform.Rotation, out Quaternion objectRotation))
            {
                return false;
            }

            TransformFrame localFrame = TransformFrame.FromUprightHeading(objectRotation);

            float matchedDot = Vector3.Dot(edgeDirection, localFrame.XAxis);
            float bestAbsoluteDot = Mathf.Abs(matchedDot);

            float yDot = Vector3.Dot(edgeDirection, localFrame.YAxis);
            float yAbsoluteDot = Mathf.Abs(yDot);
            if (yAbsoluteDot > bestAbsoluteDot)
            {
                matchedAxis = TransformAxis.Y;
                matchedDot = yDot;
                bestAbsoluteDot = yAbsoluteDot;
            }

            float zDot = Vector3.Dot(edgeDirection, localFrame.ZAxis);
            float zAbsoluteDot = Mathf.Abs(zDot);
            if (zAbsoluteDot > bestAbsoluteDot)
            {
                matchedAxis = TransformAxis.Z;
                matchedDot = zDot;
            }

            if (matchedDot < 0f)
            {
                edgeDirection = -edgeDirection;
            }

            Quaternion alignment = Quaternion.FromToRotation(localFrame.GetAxis(matchedAxis), edgeDirection);
            Quaternion worldRotation = alignment * localFrame.ToRotation();
            Quaternion relativeRotation = Quaternion.Inverse(objectRotation) * worldRotation;

            return CustomOrientation.TryNormalizeRotation(relativeRotation, out localRotation);
        }

        private static bool TryGetStructuralEdgeWorldDirection(ProceduralObject editedObject, StructuralEdgeRef edgeRef, out Vector3 direction)
        {
            direction = Vector3.zero;

            if (editedObject == null || edgeRef.PartIndex < 0 || edgeRef.PartIndex >= editedObject.Parts.Count)
            {
                return false;
            }

            MeshPart part = editedObject.Parts[edgeRef.PartIndex];
            StructuralTopology structuralTopology = part.StructuralTopology;

            if (edgeRef.EdgeId < 0 || edgeRef.EdgeId >= structuralTopology.StructuralEdgeCount)
            {
                return false;
            }

            StructuralEdge edge = structuralTopology.GetEdge(edgeRef.EdgeId);
            Vertex logicalA = part.Topology.GetVertex(edge.LogicalVertexA);
            Vertex logicalB = part.Topology.GetVertex(edge.LogicalVertexB);

            Vector3 worldA = editedObject.Transform.TransformPoint(logicalA.GetPosition(part.Mesh));
            Vector3 worldB = editedObject.Transform.TransformPoint(logicalB.GetPosition(part.Mesh));
            Vector3 delta = worldB - worldA;
            float magnitudeSquared = delta.sqrMagnitude;

            if (!IsFinite(delta.x) || !IsFinite(delta.y) || !IsFinite(delta.z) || !IsFinite(magnitudeSquared) || magnitudeSquared < 0.00000001f)
            {
                return false;
            }

            direction = delta / Mathf.Sqrt(magnitudeSquared);
            return true;
        }

        private bool TryResolveActiveCustomOrientation(ProceduralObject proceduralObject, out CustomOrientation orientation)
        {
            orientation = null;

            if (proceduralObject == null || proceduralObject.Id != m_ActiveCustomOrientationObjectId || m_ActiveCustomOrientation == null)
            {
                return false;
            }

            for (int i = 0; i < proceduralObject.CustomOrientations.Count; i++)
            {
                if (!ReferenceEquals(proceduralObject.CustomOrientations[i], m_ActiveCustomOrientation))
                {
                    continue;
                }

                m_ActiveCustomOrientationIndex = i;
                orientation = m_ActiveCustomOrientation;
                return true;
            }

            return false;
        }

        private void SynchronizeCustomTransformOrientationTarget()
        {
            if (m_TransformOrientation != TransformOrientation.Custom || TryResolveActiveCustomOrientation(m_ProceduralObjectSystem.SelectedObject, out CustomOrientation _))
            {
                return;
            }

            CancelGizmoDrag(restoreTransform: true);
            ClearGizmoHover();
            m_TransformOrientation = m_LastStandardTransformOrientation;
            ClearActiveCustomTransformOrientation();
        }

        private void ClearActiveCustomTransformOrientation()
        {
            m_ActiveCustomOrientationObjectId = -1;
            m_ActiveCustomOrientationIndex = -1;
            m_ActiveCustomOrientation = null;
        }
    }
}