using System;
using System.IO;
using System.Security.Cryptography;
using PO2.Rendering.Lod;
using UnityEngine;

internal static partial class Program {
    private static string MeshFingerprint(LodMeshData mesh) {
        using (var stream = new MemoryStream()) {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) {
                WriteMeshFingerprint(writer, mesh);
            }
            return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        }
    }

    private static void WriteMeshFingerprint(BinaryWriter writer, LodMeshData mesh) {
        writer.Write(mesh.UV0Dimension);
        foreach (Array values in new Array[] {
            mesh.Vertices, mesh.Normals, mesh.Tangents, mesh.UV0,
            mesh.VertexColors, mesh.TexCoord1, mesh.TexCoord3, mesh.Triangles,
        }) {
            writer.Write(values?.Length ?? -1);
            if (values == null) { continue; }
            foreach (object value in values) {
                if (value is Vector3 v3) {
                    writer.Write(v3.x); writer.Write(v3.y); writer.Write(v3.z);
                }
                else if (value is Vector4 v4) {
                    writer.Write(v4.x); writer.Write(v4.y); writer.Write(v4.z); writer.Write(v4.w);
                }
                else if (value is Color32 c) {
                    writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a);
                }
                else { writer.Write((int)value); }
            }
        }
    }
}
