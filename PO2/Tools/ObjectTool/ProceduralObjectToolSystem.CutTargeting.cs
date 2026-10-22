using Game;
using Game.Input;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.Tools.Gizmos;
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private void UpdateLoopCutPositioning(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool precision, bool center)
        {
            if (!m_LoopCut.IsPositioning || editedObject.Id != m_LoopCut.ObjectId)
            {
                return;
            }

            if (!IsLoopCutSourceCurrent(editedObject))
            {
                Mod.log.Warn("[PO Mesh Edit] Loop Cut source part changed while positioning; cancelling.");
                CancelMeshLoopCut();
                return;
            }

            // Topology may change from another command while this two-click tool is armed.
            // Never apply a cached loop to a different mesh revision.
            Vector3 worldEnd = m_LoopCut.WorldEdgeStart + m_LoopCut.WorldEdgeAxis * m_LoopCut.WorldEdgeLength;

            if (!TryGetLoopCutAxisParameter(camera, mousePosition, m_LoopCut.WorldEdgeStart, worldEnd, out float currentAxisParameter))
            {
                return;
            }

            if (!m_LoopCut.UpdatePositionInput(currentAxisParameter, precision, center))
            {
                return;
            }

            if (!CutPreview.Fill(editedObject, m_LoopCut.PartIndex, m_LoopCut.StructuralEdgeId, m_LoopCut.Loop, m_LoopCut.CutCount, m_LoopCut.CurrentT, m_LoopCut.PreviewLines))
            {
                Mod.log.Warn("[PO Mesh Edit] Loop Cut preview became invalid while positioning; cancelling.");
                CancelMeshLoopCut();
                return;
            }

            m_LoopCut.NotifyPreviewChanged();
        }

        private void UpdateSurfaceCutPositioning(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool precision, bool center)
        {
            if (!m_SurfaceCut.IsPositioning || editedObject.Id != m_SurfaceCut.ObjectId)
            {
                return;
            }

            if (!IsSurfaceCutSourceCurrent(editedObject))
            {
                Mod.log.Warn("[PO Mesh Edit] Surface Cut source part changed while positioning; cancelling.");
                CancelMeshSurfaceCut();
                return;
            }

            Vector3 worldEnd = m_SurfaceCut.WorldEdgeStart + m_SurfaceCut.WorldEdgeAxis * m_SurfaceCut.WorldEdgeLength;

            if (!TryGetLoopCutAxisParameter(camera, mousePosition, m_SurfaceCut.WorldEdgeStart, worldEnd, out float currentAxisParameter))
            {
                return;
            }

            if (!m_SurfaceCut.UpdatePositionInput(currentAxisParameter, precision, center))
            {
                return;
            }

            if (!CutPreview.Fill(editedObject, m_SurfaceCut.PartIndex, m_SurfaceCut.StructuralEdgeId, m_SurfaceCut.Loop, m_SurfaceCut.CutCount, m_SurfaceCut.CurrentT, m_SurfaceCut.PreviewLines))
            {
                Mod.log.Warn("[PO Mesh Edit] Surface Cut preview became invalid while positioning; cancelling.");
                CancelMeshSurfaceCut();
                return;
            }

            m_SurfaceCut.NotifyPreviewChanged();
        }

        private static bool TryGetLoopCutTargetWorldEdge(ProceduralObject editedObject, int partIndex, int structuralEdgeId, out Vector3 worldStart, out Vector3 worldEnd)
        {
            worldStart = Vector3.zero;
            worldEnd = Vector3.zero;

            if (editedObject == null || partIndex < 0 || partIndex >= editedObject.Parts.Count)
            {
                return false;
            }

            MeshPart part = editedObject.Parts[partIndex];
            StructuralTopology topology = part.StructuralTopology;

            if (structuralEdgeId < 0 || structuralEdgeId >= topology.StructuralEdgeCount)
            {
                return false;
            }

            StructuralEdge edge = topology.GetEdge(structuralEdgeId);
            Vertex logicalA = part.Topology.GetVertex(edge.LogicalVertexA);
            Vertex logicalB = part.Topology.GetVertex(edge.LogicalVertexB);

            worldStart = editedObject.Transform.TransformPoint(logicalA.GetPosition(part.Mesh));
            worldEnd = editedObject.Transform.TransformPoint(logicalB.GetPosition(part.Mesh));

            return (worldEnd - worldStart).sqrMagnitude > 0.000001f;
        }

        private static bool TryGetLoopCutAxisParameter(Camera camera, Vector2 mousePosition, Vector3 worldStart, Vector3 worldEnd, out float axisParameter)
        {
            axisParameter = 0f;

            Vector3 worldDelta = worldEnd - worldStart;
            float worldLength = worldDelta.magnitude;

            if (camera == null || worldLength <= 0.000001f)
            {
                return false;
            }

            Ray mouseRay = camera.ScreenPointToRay(mousePosition);

            if (GizmoMath.TryGetClosestRayLineParameter(mouseRay, worldStart, worldDelta / worldLength, out axisParameter))
            {
                return true;
            }

            // Nearly parallel rays make the 3D line solve unstable. Use an unclamped
            // screen-segment parameter so the drag still moves smoothly.
            Vector3 screenStart3 = camera.WorldToScreenPoint(worldStart);
            Vector3 screenEnd3 = camera.WorldToScreenPoint(worldEnd);

            if (screenStart3.z <= 0f || screenEnd3.z <= 0f)
            {
                return false;
            }

            Vector2 screenStart = new Vector2(screenStart3.x, screenStart3.y);
            Vector2 screenEnd = new Vector2(screenEnd3.x, screenEnd3.y);
            Vector2 screenDelta = screenEnd - screenStart;
            float screenLengthSquared = screenDelta.sqrMagnitude;

            if (screenLengthSquared <= 0.000001f)
            {
                return false;
            }

            float screenT = Vector2.Dot(mousePosition - screenStart, screenDelta) / screenLengthSquared;

            axisParameter = screenT * worldLength;
            return true;
        }

        private bool TryPickStructuralEdge(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, MeshCutKind cutKind, out StructuralEdgeRef edgeRef)
        {
            const float pickRadiusPixels = 12f;

            edgeRef = default;

            if (editedObject == null || camera == null)
            {
                return false;
            }

            float bestDistanceSquared = pickRadiusPixels * pickRadiusPixels;
            int bestPartIndex = -1;
            int bestEdgeId = -1;

            for (int partIndex = 0; partIndex < editedObject.Parts.Count; partIndex++)
            {
                MeshPart part = editedObject.Parts[partIndex];

                if (!IsMeshCutPartInCurrentScope(partIndex, part, cutKind))
                {
                    continue;
                }

                StructuralTopology topology = part.StructuralTopology;

                for (int edgeId = 0; edgeId < topology.StructuralEdgeCount; edgeId++)
                {
                    StructuralEdge edge = topology.GetEdge(edgeId);

                    Vertex logicalA = part.Topology.GetVertex(edge.LogicalVertexA);
                    Vertex logicalB = part.Topology.GetVertex(edge.LogicalVertexB);

                    Vector3 worldA = editedObject.Transform.TransformPoint(logicalA.GetPosition(part.Mesh));
                    Vector3 worldB = editedObject.Transform.TransformPoint(logicalB.GetPosition(part.Mesh));

                    Vector3 screenA3 = camera.WorldToScreenPoint(worldA);
                    Vector3 screenB3 = camera.WorldToScreenPoint(worldB);

                    if (screenA3.z <= 0f || screenB3.z <= 0f)
                    {
                        continue;
                    }

                    Vector2 screenA = new Vector2(screenA3.x, screenA3.y);
                    Vector2 screenB = new Vector2(screenB3.x, screenB3.y);

                    float distanceSquared = DistanceSquaredToScreenSegment(mousePosition, screenA, screenB);

                    if (distanceSquared >= bestDistanceSquared)
                    {
                        continue;
                    }

                    bestDistanceSquared = distanceSquared;
                    bestPartIndex = partIndex;
                    bestEdgeId = edgeId;
                }
            }

            if (bestPartIndex < 0 || bestEdgeId < 0)
            {
                return false;
            }

            edgeRef = new StructuralEdgeRef(bestPartIndex, bestEdgeId);
            return true;
        }

        private bool IsLoopCutSourceCurrent(ProceduralObject editedObject)
        {
            if (editedObject == null || m_LoopCut.PartIndex < 0 || m_LoopCut.PartIndex >= editedObject.Parts.Count)
            {
                return false;
            }

            MeshPart sourcePart = editedObject.Parts[m_LoopCut.PartIndex];

            return IsMeshCutPartInCurrentScope(m_LoopCut.PartIndex, sourcePart, MeshCutKind.Loop) && sourcePart.GeometryVersion == m_LoopCut.SourcePartGeometryVersion && sourcePart.TopologyVersion == m_LoopCut.SourcePartTopologyVersion;
        }

        private bool IsSurfaceCutSourceCurrent(ProceduralObject editedObject)
        {
            if (editedObject == null || m_SurfaceCut.PartIndex < 0 || m_SurfaceCut.PartIndex >= editedObject.Parts.Count)
            {
                return false;
            }

            MeshPart sourcePart = editedObject.Parts[m_SurfaceCut.PartIndex];

            return IsMeshCutPartInCurrentScope(m_SurfaceCut.PartIndex, sourcePart, MeshCutKind.Surface) && sourcePart.GeometryVersion == m_SurfaceCut.SourcePartGeometryVersion && sourcePart.TopologyVersion == m_SurfaceCut.SourcePartTopologyVersion;
        }

        private void UpdateLoopCutTargeting(ProceduralObject editedObject, Camera camera, Vector2 mousePosition)
        {
            if (!TryPickStructuralEdge(editedObject, camera, mousePosition, MeshCutKind.Loop, out StructuralEdgeRef edgeRef))
            {
                m_LoopCut.ClearTargetCandidate();
                return;
            }

            MeshPart part = editedObject.Parts[edgeRef.PartIndex];
            StructuralTopology topology = part.StructuralTopology;

            if (m_LoopCut.PartIndex == edgeRef.PartIndex && m_LoopCut.StructuralEdgeId == edgeRef.EdgeId && m_LoopCut.SourcePartGeometryVersion == part.GeometryVersion && m_LoopCut.SourcePartTopologyVersion == part.TopologyVersion)
            {
                return;
            }

            if (!TopologyEditor.TryBuildClosedStructuralLoop(topology, edgeRef.EdgeId, out StructuralLoop loop))
            {
                m_LoopCut.ClearTargetCandidate();
                return;
            }


            LoopCutPreviewLine[] previewLines = CutPreview.Build(editedObject, edgeRef.PartIndex, edgeRef.EdgeId, loop, m_LoopCut.CutCount, 0.5f);

            m_LoopCut.SetTargetCandidate(edgeRef.PartIndex, edgeRef.EdgeId, loop, part.GeometryVersion, part.TopologyVersion, previewLines);
        }

        private void UpdateSurfaceCutTargeting(ProceduralObject editedObject, Camera camera, Vector2 mousePosition)
        {
            if (!TryPickStructuralEdge(editedObject, camera, mousePosition, MeshCutKind.Surface, out StructuralEdgeRef edgeRef))
            {
                m_SurfaceCut.ClearTargetCandidate();
                return;
            }

            MeshPart part = editedObject.Parts[edgeRef.PartIndex];
            StructuralTopology topology = part.StructuralTopology;

            if (m_SurfaceCut.PartIndex == edgeRef.PartIndex && m_SurfaceCut.StructuralEdgeId == edgeRef.EdgeId && m_SurfaceCut.SourcePartGeometryVersion == part.GeometryVersion && m_SurfaceCut.SourcePartTopologyVersion == part.TopologyVersion)
            {
                return;
            }

            if (!TopologyEditor.TryBuildOpenStructuralStrip(topology, edgeRef.EdgeId, out StructuralLoop loop))
            {
                m_SurfaceCut.ClearTargetCandidate();
                return;
            }


            LoopCutPreviewLine[] previewLines = CutPreview.Build(editedObject, edgeRef.PartIndex, edgeRef.EdgeId, loop, m_SurfaceCut.CutCount, 0.5f);

            m_SurfaceCut.SetTargetCandidate(edgeRef.PartIndex, edgeRef.EdgeId, loop, part.GeometryVersion, part.TopologyVersion, previewLines);
        }

        private static float DistanceSquaredToScreenSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float lengthSquared = segment.sqrMagnitude;

            if (lengthSquared <= 0.000001f)
            {
                return (point - segmentStart).sqrMagnitude;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSquared);
            Vector2 closest = segmentStart + segment * t;
            return (point - closest).sqrMagnitude;
        }
    }
}