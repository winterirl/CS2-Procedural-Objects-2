// CPU layout and mapping tests. Engine upload remains a facade.
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using PO2.Geometry;
using PO2.Materials;
using PO2.Persistence.Model;
namespace UnityEngine {public struct Bounds { } }
namespace PO2.Conversion
{
    internal struct ConversionBoundsSnapshot {internal void Encapsulate(Vector3 value){}internal Bounds ToBounds()=>default;}
}
namespace PO2.Core.Operations
{
    internal enum TopologyRenderCaptureMode {Restore,Mirror,Array}
    internal interface ITopologyRenderWork {ITopologyRenderData Build(MeshPart part,PersistencePartData before,int[] oldToNew,ITopologyAppearanceCheck appearance,CancellationToken token);}
    internal interface IArrayRenderWork {void PrepareArray(MeshPart source,Matrix4x4 transform,CancellationToken token);ITopologyRenderData BuildArray(MeshPart part,PersistencePartData before,Matrix4x4 transform,CancellationToken token);}
    internal interface ITopologyRenderData { }
    internal interface ITopologyAppearanceCheck {void Validate(PersistencePartData before,int[] oldToNew,ITopologyRenderData data,CancellationToken token);}
    internal class TopologyReplayProof {internal Vector3 Scale;internal Dictionary<int,Vector2> RestoredTangents=new Dictionary<int,Vector2>();}
    internal static class ArrayKernels
    {
        internal static MeshData CopyTransformed(MeshData mesh,Matrix4x4 transform,CancellationToken token)=>throw new NotSupportedException();
        internal static Vector4[] CopyTangents(MeshData mesh,Matrix4x4 transform,CancellationToken token)=>throw new NotSupportedException();
    }
}
namespace PO2.Rendering
{
    internal class RenderGeometryPreparation : IDisposable
    {
        internal long Bytes=>0;
        public void Dispose(){}
        internal static RenderGeometryPreparation TryBuild(int count,int[][] maps,Vector3[] main,Vector3[][] isolated,CancellationToken token)=>null;
    }
    internal static class RenderVertexStreams
    {
        internal struct Layout { }
        internal static Layout Describe(Vector3[] p,Vector3[] n,Vector4[] t,Vector4[] uv,Color32[] c,Vector4[] tex1,Vector4[] tex3,int dim,bool tangents)=>default;
        internal static byte[] PackAuxiliary(Layout layout,int count,Vector4[] uv,Color32[] c,Vector4[] tex1,Vector4[] tex3,CancellationToken token)=>Array.Empty<byte>();
    }
    internal static class TopologyRendererValidation
    {
        internal static bool SameTangent(Vector4 a,Vector4 b)=>a.Equals(b);
        internal static void ValidateRestoredArea(MeshData a,MeshData b,CancellationToken token)=>throw new NotSupportedException();
    }
}
