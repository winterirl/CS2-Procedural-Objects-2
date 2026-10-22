using System.Threading;
using PO2.Geometry.Topology;
using PO2.Localization;
using PO2.Persistence.Model;

namespace PO2.Geometry.Editing
{
    public static partial class TopologyEditor
    {
        // This only reads a detached snapshot. Converted faces keep their existing
        // grouping; deleting a quad corner removes both triangles of that quad.
        internal static bool TryAnalyzeVertexDeletion(PersistencePartData source, int[] selected, CancellationToken cancellation, out VertexDeletePlan plan, out LocalizedText reason, TopologyEditIndex removalIndex = null)
        {
            plan = null;
            reason = LocalizedText.For("PO2.Edit.Selection.Required.VerticesInPart");
            if (source == null || selected == null || selected.Length == 0) return false;
            int vertexCount = source.Vertices.Length, triangleCount = source.Triangles.Length / 3;
            if (source.RenderToLogical.Length != vertexCount || source.Triangles.Length % 3 != 0 || source.TriangleSurfaceAreaIds.Length != triangleCount)
            {
                reason = LocalizedText.For("PO2.Edit.Validation.MeshLayout");
                return false;
            }

            var removeLogical = new bool[source.LogicalVertexCount];
            foreach (int id in selected)
            {
                if (id < 0 || id >= removeLogical.Length) return false;
                removeLogical[id] = true;
            }

            var removeRender = new bool[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                int logical = source.RenderToLogical[i];
                if (logical < 0 || logical >= removeLogical.Length)
                {
                    reason = LocalizedText.For("PO2.Edit.Validation.LogicalVertexMapping");
                    return false;
                }

                removeRender[i] = removeLogical[logical];
            }

            var removedTriangles = new bool[triangleCount];
            var usedBefore = new bool[vertexCount];
            for (int t = 0; t < triangleCount; t++)
            {
                if ((t & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                for (int c = 0; c < 3; c++)
                {
                    int index = source.Triangles[t * 3 + c];
                    if (index < 0 || index >= vertexCount)
                    {
                        reason = LocalizedText.For("PO2.Edit.Validation.TriangleIndex");
                        return false;
                    }

                    usedBefore[index] = true;
                    removedTriangles[t] |= removeRender[index];
                }
            }

            cancellation.ThrowIfCancellationRequested();
            var faceTriangles = removalIndex?.FaceTriangles;
            if (faceTriangles == null) StructuralFaceTriangleResolver.Build(source.StructuralFaces, source.Triangles, out faceTriangles, out _);
            var removedFaces = new bool[source.StructuralFaces.Length];
            int keptFaces = 0;
            for (int f = 0; f < removedFaces.Length; f++)
            {
                if ((f & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                var face = source.StructuralFaces[f];
                bool remove = false;
                for (int c = 0; c < face.CornerCount; c++)
                {
                    int index = face.GetRenderVertexIndex(c);
                    if (index < 0 || index >= vertexCount)
                    {
                        reason = LocalizedText.For("PO2.Edit.Validation.FaceCorner");
                        return false;
                    }

                    remove |= removeRender[index];
                }

                removedFaces[f] = remove;
                if (remove)
                {
                    removedTriangles[faceTriangles[f].TriangleA] = true;
                    if (face.IsQuad) removedTriangles[faceTriangles[f].TriangleB] = true;
                }
                else keptFaces++;
            }

            var usedAfter = new bool[vertexCount];
            int keptTriangles = 0;
            for (int t = 0; t < triangleCount; t++)
            {
                if ((t & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                if (removedTriangles[t]) continue;
                keptTriangles++;
                for (int c = 0; c < 3; c++) usedAfter[source.Triangles[t * 3 + c]] = true;
            }

            if (keptTriangles == 0)
            {
                reason = LocalizedText.For("PO2.Edit.DeleteVertices.Error.EmptyPart");
                return false;
            }

            var oldToNew = new int[vertexCount];
            int keptVertices = 0;
            for (int i = 0; i < vertexCount; i++)
            {
                // Only clean up vertices this operation strands. Unrelated loose
                // vertices still belong to the user and stay where they were.
                oldToNew[i] = !removeRender[i] && (usedAfter[i] || !usedBefore[i]) ? keptVertices++ : -1;
            }

            if (keptVertices == vertexCount && keptTriangles == triangleCount)
            {
                reason = LocalizedText.For("PO2.Edit.Selection.Error.VerticesUnavailable");
                return false;
            }

            plan = new VertexDeletePlan(source, removedTriangles, removedFaces, oldToNew, keptVertices, keptTriangles, keptFaces);
            reason = LocalizedText.Empty;
            return true;
        }

        internal static MeshMutationResult CreateVertexDeletionMutation(VertexDeletePlan plan, CancellationToken cancellation)
        {
            var source = plan.Source;
            int[] map = plan.OldToNew;
            var triangles = new int[checked(plan.TriangleCount * 3)];
            var memberships = new int[plan.TriangleCount];
            int nextTriangle = 0;
            for (int t = 0; t < plan.RemovedTriangles.Length; t++)
            {
                if ((t & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                if (plan.RemovedTriangles[t]) continue;
                for (int c = 0; c < 3; c++) triangles[nextTriangle * 3 + c] = map[source.Triangles[t * 3 + c]];
                memberships[nextTriangle++] = source.TriangleSurfaceAreaIds[t];
            }

            var faces = new StructuralFace[plan.FaceCount];
            int nextFace = 0;
            for (int f = 0; f < plan.RemovedFaces.Length; f++)
            {
                if ((f & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                if (plan.RemovedFaces[f]) continue;
                var face = source.StructuralFaces[f];
                int a = map[face.GetRenderVertexIndex(0)], b = map[face.GetRenderVertexIndex(1)], c = map[face.GetRenderVertexIndex(2)];
                faces[nextFace] = face.IsQuad ? new StructuralFace(nextFace, a, b, c, map[face.GetRenderVertexIndex(3)]) : new StructuralFace(nextFace, a, b, c);
                nextFace++;
            }

            var result = new MeshMutationResult(CompactRemovalStream(source.Vertices, plan, cancellation), CompactRemovalStream(source.Normals, plan, cancellation), CompactRemovalStream(source.Tangents, plan, cancellation), CompactRemovalStream(source.UV0, plan, cancellation), CompactRemovalStream(source.VertexColors, plan, cancellation), CompactRemovalStream(source.TexCoord1, plan, cancellation), CompactRemovalStream(source.TexCoord3, plan, cancellation), triangles, faces, memberships);
            SetRemovalVertexMaps(result, source, map, cancellation);
            return result;
        }
    }
}