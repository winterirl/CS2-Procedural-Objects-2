using System;
using System.Linq;
using System.Threading;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Materials;
using PO2.Persistence.Format;
using PO2.Rendering;
using UnityEngine;

internal static partial class LoadingTests
{
    private static void RenderPreparation()
    {
        var obj=Fixture(SnapshotFormat.kObjectSchemaVersion).Restore(1);
        var part=obj.Parts[0];
        // Three scattered areas, one empty. Include an isolated material,
        // missing channels and four-component UVs in multiple combinations.
        part.Mesh.Triangles=new[]{0,1,2,3,1,2,2,1,3,0,2,1,3,2,1};
        part.TriangleSurfaceAreaIds=new[]{7,0,7,12,0};
        var original=part.SurfaceAreas[0];
        part.SurfaceAreas.Add(new SurfaceArea(7,original.Material,VertexAttributeFlags.None,original.ColorMask,null,default));
        part.SurfaceAreas.Add(new SurfaceArea(12,original.Material,VertexAttributeFlags.VertexColor|VertexAttributeFlags.TexCoord1,original.ColorMask,null,default));
        part.SurfaceAreas.Add(new SurfaceArea(20,original.Material,VertexAttributeFlags.None,original.ColorMask,null,default));
        for(int scenario=0;scenario<8;scenario++)
        {
            part.Mesh.Normals=(scenario&1)==0?new Vector3[4]:null;
            part.Mesh.Tangents=(scenario&2)==0?new Vector4[4]:null;
            var work=new TopologyRendererWork{CaptureMode=TopologyRenderCaptureMode.Restore};
            var reference=new ReferenceRendererWork{CaptureMode=TopologyRenderCaptureMode.Restore};
            foreach(var area in part.SurfaceAreas)
            {
                var settings=new TopologyRenderAreaWork{Id=area.Id,Isolated=area.Id==12&&(scenario&4)==0,RequiresTangents=true,TileSize=Vector2.one};
                work.Areas.Add(area.Id,settings);reference.Areas.Add(area.Id,settings);
            }
            using(var actual=(TopologyRendererPreparation)work.Build(part,null,null,null,CancellationToken.None))
            using(var expected=(TopologyRendererPreparation)reference.Build(part,null,null,null,CancellationToken.None))
            {
                EqualMesh(actual.Combined,expected.Combined,"combined streams");
                Check(actual.Areas.Length==3&&actual.Areas.Length==expected.Areas.Length,"empty area retained only in model");
                Check(actual.RecalculateCombinedTangents==expected.RecalculateCombinedTangents&&actual.UV0Dimension==expected.UV0Dimension,"combined tangent and UV requirements");
                Same(actual.Upload.Indices,expected.Upload.Indices,"upload indices");
                Check(ReferenceEquals(actual.Upload.Indices,actual.Combined.Triangles),"upload reuses validated index storage");
                for(int i=0;i<actual.Areas.Length;i++)
                {
                    var a=actual.Areas[i];var b=expected.Areas[i];EqualMesh(a.Source,b.Source,"area streams");
                    Same(a.SourceVertexIndices,b.SourceVertexIndices,"area vertex order");Same(a.SourceTriangleIds,b.SourceTriangleIds,"area triangle order");
                    Check(a.CombinedOffset==b.CombinedOffset&&a.MaterialIndex==b.MaterialIndex&&a.RecalculateTangents==b.RecalculateTangents,"area slots and tangent fallback");
                }
            }
        }
    }
    private static void EqualMesh(MeshData a,MeshData b,string label)
    {
        EqualStream(a.Vertices,b.Vertices,label);EqualStream(a.Normals,b.Normals,label);EqualStream(a.Tangents,b.Tangents,label);EqualStream(a.UV0,b.UV0,label);
        EqualStream(a.VertexColors,b.VertexColors,label);EqualStream(a.TexCoord1,b.TexCoord1,label);EqualStream(a.TexCoord3,b.TexCoord3,label);Same(a.Triangles,b.Triangles,label);
    }
    private static void EqualStream<T>(T[] a,T[] b,string label)=>Check(a==null?b==null:b!=null&&a.SequenceEqual(b),label);
}
