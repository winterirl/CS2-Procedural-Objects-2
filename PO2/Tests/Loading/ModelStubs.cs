// Minimal runtime facades for the real persistence DTOs and format codecs.
// MeshPart restoration and validation are linked from production; renderer APIs remain facades.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Core.Deformation;
using PO2.Core.Lod;
using PO2.Materials;
namespace UnityEngine
{
    public partial struct Vector2
    {
        public float x,y;
        public Vector2(float x,float y){this.x=x;this.y=y;}
        public static Vector2 zero=>default;
        public static Vector2 one=>new Vector2(1,1);
    }
    public struct Quaternion {public float x,y,z,w; public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;} }
    public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}public static Color white=>new Color(1,1,1,1);}

}
namespace PO2.Core
{
    public struct ObjectTransform
    {
        public Vector3 Position,Scale;public Quaternion Rotation;
        public ObjectTransform(Vector3 p,Quaternion r,Vector3 s){Position=p;Rotation=r;Scale=s;}
    }
    public class CustomOrientation
    {
        public string Name;public Quaternion LocalRotation;
        public static bool TryCreate(string name,Quaternion rotation,out CustomOrientation result){result=new CustomOrientation{Name=name,LocalRotation=rotation};return !string.IsNullOrEmpty(name);}
    }
    public class ProceduralObject
    {
        public const int kMaxPartCount=4096;
        public int Id;public Guid PersistentId;public string Name;public ObjectTransform Transform;public Vector3 LocalPivot;
        public bool IsLocked,ReceivesDecals;
        internal LodProfile LodProfile;
        internal List<DeformationCage> Deformations=new List<DeformationCage>();
        public List<MeshPart> Parts;public List<CustomOrientation> CustomOrientations;
        public ProceduralObject(int id,Guid guid,string name,ObjectTransform transform,List<MeshPart> parts,List<CustomOrientation> orientations,Vector3 localPivot=default)
        {Id=id;PersistentId=guid;Name=name;Transform=transform;Parts=parts;CustomOrientations=orientations;LocalPivot=localPivot;}
        public void SetLocked(bool value)=>IsLocked=value;
        public void SetReceivesDecals(bool value)=>ReceivesDecals=value;
        internal void SetLodProfile(LodProfile value)=>LodProfile=value;
        internal void SetDeformation(int index,DeformationCage value){if(!value.Matches(this))throw new InvalidOperationException();Deformations.Add(value);}
    }
}
namespace PO2.Core.Operations {internal class PartGeometrySnapshot {internal Vector3[] Positions,Normals;internal Vector4[] Tangents;} }
namespace PO2.Geometry
{
    public sealed partial class MeshPart
    {
        public const int kDefaultSurfaceAreaId = 0;
        public Guid PartHistoryId=Guid.NewGuid();public string Name;public MeshData Mesh;
        public MeshTopology Topology;public StructuralTopology StructuralTopology;
        internal StructuralTopologySource StructuralTopologySource;
        internal uint TopologyVersion;
        private List<SurfaceArea> m_SurfaceAreas;
        private int[] m_TriangleSurfaceAreaIds;
        private int m_NextSurfaceAreaId;
        private StructuralFaceTriangles[] m_StructuralFaceTriangles;
        private int[] m_TriangleStructuralFaceIds;
        public List<SurfaceArea> SurfaceAreas { get => m_SurfaceAreas; set => m_SurfaceAreas=value; }
        public IReadOnlyList<int> TriangleSurfaceAreaIds { get => m_TriangleSurfaceAreaIds; set => m_TriangleSurfaceAreaIds=(int[])value; }
        public int NextSurfaceAreaId { get => m_NextSurfaceAreaId; set => m_NextSurfaceAreaId=value; }
        public Vector3 LocalPivot;
        public MeshPart() { }
    }

}
namespace PO2.Materials
{
    public class TextureMappingData
    {
        public TextureMappingMode Mode;public Vector2 TextureScale,Offset;public float Rotation;
        public TextureMappingData(TextureMappingMode mode,Vector2 scale,float rotation,Vector2 offset){Mode=mode;TextureScale=scale;Rotation=rotation;Offset=offset;}
    }
    public class MaterialData
    {
        public MaterialSourceKind SourceKind;public string DefinitionId;public Colossal.Hash128 SurfaceGuid;public VanillaSpecialMaterialKind VanillaSpecialKind;
        public TextureMappingData Mapping;public float Metallic,Smoothness,CoatStrength;
        internal static MaterialData Restore(MaterialSourceKind kind,string id,Colossal.Hash128 guid,VanillaSpecialMaterialKind special,TextureMappingData mapping,float metal,float smooth,float coat)=>new MaterialData{SourceKind=kind,DefinitionId=id,SurfaceGuid=guid,VanillaSpecialKind=special,Mapping=mapping,Metallic=metal,Smoothness=smooth,CoatStrength=coat};
    }
    public class ColorMaskData
    {
        public ColorMaskMode Mode;public bool HasSourceMask;public Color SourceMask1,SourceMask2,SourceMask3,OverrideMask1,OverrideMask2,OverrideMask3;
        internal static ColorMaskData Restore(ColorMaskMode mode,bool source,Color a,Color b,Color c,Color d,Color e,Color f)=>new ColorMaskData{Mode=mode,HasSourceMask=source,SourceMask1=a,SourceMask2=b,SourceMask3=c,OverrideMask1=d,OverrideMask2=e,OverrideMask3=f};
    }
    public class SurfaceAreaOriginalState
    {
        private MaterialData m_Material;private ColorMaskData m_Mask;
        public SurfaceAreaOriginalState(MaterialData material,ColorMaskData mask){m_Material=material;m_Mask=mask;}
        public MaterialData CreateMaterialClone()=>m_Material;public ColorMaskData CreateColorMaskClone()=>m_Mask;
    }
    public class SurfaceArea
    {
        public int Id;public MaterialData Material;public ColorMaskData ColorMask;public VertexAttributeFlags SourceVertexAttributes;
        public SurfaceAreaOriginalState OriginalState;public NativeWindowKind NativeWindowKind;internal GeneratedMappingSeed MappingSeed;
        public SurfaceArea(){}
        public SurfaceArea(int id,MaterialData material,VertexAttributeFlags flags,ColorMaskData mask,SurfaceAreaOriginalState original,NativeWindowKind window){Id=id;Material=material;SourceVertexAttributes=flags;ColorMask=mask;OriginalState=original;NativeWindowKind=window;}
    }
}

namespace PO2.Conversion
{
    internal sealed class ConversionPartProfile { internal string StructuralTopologyFallbackReason; }
}
