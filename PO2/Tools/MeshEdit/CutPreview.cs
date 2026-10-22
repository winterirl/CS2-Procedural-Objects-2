using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    public static class CutPreview
    {
        public static LoopCutPreviewLine[] Build(ProceduralObject editedObject, int partIndex, int structuralEdgeId, StructuralLoop loop, int cutCount, float t)
        {
            if (loop == null || !CutLayout.IsValid(cutCount, t))
            {
                return System.Array.Empty<LoopCutPreviewLine>();
            }

            LoopCutPreviewLine[] lines = new LoopCutPreviewLine[checked(loop.StepCount * cutCount + 1)];

            return Fill(editedObject, partIndex, structuralEdgeId, loop, cutCount, t, lines) ? lines : System.Array.Empty<LoopCutPreviewLine>();
        }

        public static bool Fill(ProceduralObject editedObject, int partIndex, int structuralEdgeId, StructuralLoop loop, int cutCount, float t, LoopCutPreviewLine[] lines)
        {
            if (editedObject == null || loop == null || !CutLayout.IsValid(cutCount, t) || lines == null || lines.Length != checked(loop.StepCount * cutCount + 1) || partIndex < 0 || partIndex >= editedObject.Parts.Count)
            {
                return false;
            }

            MeshPart part = editedObject.Parts[partIndex];
            StructuralTopology topology = part.StructuralTopology;
            MeshData mesh = part.Mesh;

            if (structuralEdgeId < 0 || structuralEdgeId >= topology.StructuralEdgeCount)
            {
                return false;
            }

            for (int stepIndex = 0; stepIndex < loop.StepCount; stepIndex++)
            {
                StructuralLoopStep step = loop.Steps[stepIndex];

                if (step.QuadId < 0 || step.QuadId >= topology.FaceCount || !topology.TryGetQuad(step.QuadId, out StructuralFace quad))
                {
                    return false;
                }

                int i0 = step.EntryBoundaryIndex;
                int i1 = (i0 + 1) % 4;
                int i2 = (i0 + 2) % 4;
                int i3 = (i0 + 3) % 4;

                for (int cutIndex = 0; cutIndex < cutCount; cutIndex++)
                {
                    float fraction = CutLayout.GetFraction(cutCount, cutIndex, t);
                    float localT = step.ReverseParameter ? 1f - fraction : fraction;

                    Vector3 localA = Vector3.Lerp(mesh.Vertices[quad.GetRenderVertexIndex(i0)], mesh.Vertices[quad.GetRenderVertexIndex(i1)], localT);
                    Vector3 localB = Vector3.Lerp(mesh.Vertices[quad.GetRenderVertexIndex(i3)], mesh.Vertices[quad.GetRenderVertexIndex(i2)], localT);

                    lines[stepIndex * cutCount + cutIndex] = new LoopCutPreviewLine(editedObject.Transform.TransformPoint(localA), editedObject.Transform.TransformPoint(localB), isTargetEdge: false);
                }
            }

            StructuralEdge targetEdge = topology.GetEdge(structuralEdgeId);
            Vertex targetA = part.Topology.GetVertex(targetEdge.LogicalVertexA);
            Vertex targetB = part.Topology.GetVertex(targetEdge.LogicalVertexB);

            lines[loop.StepCount * cutCount] = new LoopCutPreviewLine(editedObject.Transform.TransformPoint(targetA.GetPosition(mesh)), editedObject.Transform.TransformPoint(targetB.GetPosition(mesh)), isTargetEdge: true);

            return true;
        }
    }
}