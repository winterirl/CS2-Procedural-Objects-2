using UnityEngine;
using PO2.Core;
using PO2.Core.Deformation;

namespace PO2.Tools.Gizmos
{
    internal static class CageBounds
    {
        internal static bool TryFit(ProceduralObject obj, int partIndex, TransformFrame frame, out Matrix4x4 reference)
        {
            reference = Matrix4x4.identity;
            if (obj == null || partIndex < -1 || partIndex >= obj.Parts.Count) return false;
            Matrix4x4 root = Matrix4x4.TRS(Vector3.zero, obj.Transform.Rotation, obj.Transform.Scale);
            if (!DeformationRecipe.Finite(root) || root.determinant == 0f) return false;
            Matrix4x4 axes = Matrix4x4.identity;
            DeformationRecipe.Column(ref axes, 0, frame.XAxis);
            DeformationRecipe.Column(ref axes, 1, frame.YAxis);
            DeformationRecipe.Column(ref axes, 2, frame.ZAxis);
            Matrix4x4 projectedFromLocal = axes.transpose * root;
            Vector3 minimum = Vector3.zero, maximum = Vector3.zero;
            bool any = false;
            int first = partIndex < 0 ? 0 : partIndex, end = partIndex < 0 ? obj.Parts.Count : partIndex + 1;
            for (int part = first; part < end; part++)
            {
                foreach (Vector3 vertex in obj.Parts[part].Mesh.Vertices)
                {
                    Vector3 p = projectedFromLocal.MultiplyVector(vertex);
                    if (!DeformationRecipe.Finite(p)) return false;
                    if (!any)
                    {
                        minimum = maximum = p;
                        any = true;
                    }
                    else
                    {
                        minimum = new Vector3(Mathf.Min(minimum.x, p.x), Mathf.Min(minimum.y, p.y), Mathf.Min(minimum.z, p.z));
                        maximum = new Vector3(Mathf.Max(maximum.x, p.x), Mathf.Max(maximum.y, p.y), Mathf.Max(maximum.z, p.z));
                    }
                }
            }

            if (!any) return false;
            Vector3 size = maximum - minimum;
            float span = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (span < 0.0001f) return false;
            float padding = Mathf.Max(0.001f, span * 0.001f);
            Vector3 padded = new Vector3(Mathf.Max(size.x, padding), Mathf.Max(size.y, padding), Mathf.Max(size.z, padding));
            minimum -= (padded - size) * 0.5f;
            reference = projectedFromLocal.inverse * Matrix4x4.TRS(minimum, Quaternion.identity, padded);
            return DeformationRecipe.TryCreate(reference, DeformationCage.ReferenceCorners(reference), out _, out _);
        }
    }
}