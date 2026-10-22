using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private static void SetUnityUV0(Mesh mesh, Vector4[] uvs, int dimension)
        {
            if (mesh == null)
            {
                return;
            }

            if (uvs == null || uvs.Length == 0)
            {
                mesh.SetUVs(0, new List<Vector2>());
                return;
            }

            if (dimension <= 2)
            {
                List<Vector2> uv2 = new List<Vector2>(uvs.Length);
                for (int i = 0; i < uvs.Length; i++)
                {
                    uv2.Add(new Vector2(uvs[i].x, uvs[i].y));
                }

                mesh.SetUVs(0, uv2);
                return;
            }

            mesh.SetUVs(0, new List<Vector4>(uvs));
        }

        private static void SetUnityTexCoord1(Mesh mesh, Vector4[] texCoord1)
        {
            if (mesh == null)
            {
                return;
            }

            if (texCoord1 == null || texCoord1.Length == 0)
            {
                if (mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                    mesh.SetUVs(1, new List<Vector4>());
                return;
            }

            mesh.SetUVs(1, new List<Vector4>(texCoord1));
        }

        private static void SetUnityTexCoord3(Mesh mesh, Vector4[] texCoord3)
        {
            if (mesh == null)
            {
                return;
            }

            if (texCoord3 == null || texCoord3.Length == 0)
            {
                mesh.SetUVs(3, new List<Vector4>());
                return;
            }

            mesh.SetUVs(3, new List<Vector4>(texCoord3));
        }
    }
}