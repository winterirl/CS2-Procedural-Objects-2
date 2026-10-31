// Standalone algorithm tests. NativeArray owns real pinned buffers, but these
// facades do not test Unity API behavior or Burst compilation.
using System;
using System.Runtime.InteropServices;
namespace UnityEngine
{
    public partial struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) {this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero => default;
        public static Vector3 up => new Vector3(0,1,0);
        public Vector3 normalized { get { float length=(float)Math.Sqrt(sqrMagnitude); return length > 1e-5f ? this * (1f/length) : zero; } }
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public partial struct Vector4
    {
        public static Vector4 zero=>default;
        public float x,y,z,w;
        public Vector4(float x,float y,float z,float w) {this.x=x;this.y=y;this.z=z;this.w=w;}
    }
    public struct Color32
    {
        public byte r,g,b,a;
        public Color32(byte r,byte g,byte b,byte a) {this.r=r;this.g=g;this.b=b;this.a=a;}
    }
    public static partial class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); public static float Abs(float v)=>Math.Abs(v); public static int RoundToInt(float v)=>(int)Math.Round(v); }
}
namespace Unity.Mathematics
{
    public struct uint4 { public uint x,y,z,w; public uint4(uint x,uint y,uint z,uint w) {this.x=x;this.y=y;this.z=z;this.w=w;} }
    public struct float3
    {
        public float x,y,z;
        public float3(float x,float y,float z) {this.x=x;this.y=y;this.z=z;}
        public static float3 operator *(float3 a,float b)=>new float3(a.x*b,a.y*b,a.z*b);
        public static float3 operator -(float3 a,float3 b)=>new float3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public struct int3
    {
        public int x,y,z;
        public int3(int x,int y,int z) {this.x=x;this.y=y;this.z=z;}
        public static int3 operator +(int3 a,int3 b)=>new int3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static bool operator ==(int3 a,int3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        public static bool operator !=(int3 a,int3 b)=>!(a==b);
        public override bool Equals(object o)=>o is int3 v && this==v;
        public override int GetHashCode()=>HashCode.Combine(x,y,z);
    }
    public static class math
    {
        public static float floor(float v)=>(float)Math.Floor(v);
        public static int min(int a,int b)=>Math.Min(a,b);
        public static int max(int a,int b)=>Math.Max(a,b);
        public static bool all(bool v)=>v;
    }
}
namespace Unity.Burst
{
    [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method)]
    public sealed class BurstCompileAttribute : Attribute { public bool CompileSynchronously; public FloatMode FloatMode; public FloatPrecision FloatPrecision; }
    public enum FloatMode { Strict }
    public enum FloatPrecision { Standard }
    public struct FunctionPointer<T> { public T Invoke; }
    public static class BurstCompiler
    {
        public static bool IsEnabled => false;
        public static FunctionPointer<T> CompileFunctionPointer<T>(T kernel) => throw new NotSupportedException();
    }
}
namespace Unity.Collections
{
    internal static class NativeAllocationChecks
    {
        internal static int Active, Created;
        [ThreadStatic] internal static int FailAfter;
        internal static void BeforeCreate()
        {
            if (FailAfter > 0 && --FailAfter == 0) throw new OutOfMemoryException("Injected native allocation failure.");
        }
    }
    public enum Allocator { Persistent }
    public enum NativeArrayOptions { UninitializedMemory, ClearMemory }
    public struct NativeArray<T> : IDisposable where T : struct
    {
        private sealed class Storage
        {
            internal T[] Values;
            internal GCHandle Pin;
            internal bool Disposed;
            internal Storage(int count)
            {
                NativeAllocationChecks.BeforeCreate();
                Values=new T[count]; Pin=GCHandle.Alloc(Values,GCHandleType.Pinned);
                System.Threading.Interlocked.Increment(ref NativeAllocationChecks.Active);
                System.Threading.Interlocked.Increment(ref NativeAllocationChecks.Created);
            }
        }
        private Storage m_Storage;
        public NativeArray(int count,Allocator allocator,NativeArrayOptions options) {m_Storage=new Storage(count);}
        public int Length=>m_Storage?.Values.Length??0;
        public bool IsCreated=>m_Storage!=null&&!m_Storage.Disposed;
        public T this[int index] { get=>m_Storage.Values[index]; set=>m_Storage.Values[index]=value; }
        internal IntPtr Pointer=>m_Storage==null?IntPtr.Zero:m_Storage.Pin.AddrOfPinnedObject();
        public void Dispose() {if(IsCreated) {m_Storage.Pin.Free();m_Storage.Disposed=true;System.Threading.Interlocked.Decrement(ref NativeAllocationChecks.Active);}}
    }
}
namespace Unity.Collections.LowLevel.Unsafe
{
    public static unsafe class NativeArrayUnsafeUtility
    {
        public static void* GetUnsafePtr<T>(Unity.Collections.NativeArray<T> a) where T:struct => a.Pointer.ToPointer();
        public static void* GetUnsafeReadOnlyPtr<T>(Unity.Collections.NativeArray<T> a) where T:struct => a.Pointer.ToPointer();
    }
    public static unsafe class UnsafeUtility { public static void MemCpy(void* target,void* source,long count)=>Buffer.MemoryCopy(source,target,count,count); }
}
namespace Colossal
{
    public struct Hash128 {public Hash128(uint x,uint y,uint z,uint w) {value=new Unity.Mathematics.uint4(x,y,z,w);}public Unity.Mathematics.uint4 value; public Hash128(Unity.Mathematics.uint4 value) {this.value=value;} }
}

namespace PO2.Conversion { }
namespace PO2.Materials
{

}
namespace PO2.Geometry
{
    public class MeshData
    {
        public void RecalculateTangents()=>throw new NotSupportedException("Unity tangent calculation is outside this harness.");
        public MeshData(UnityEngine.Vector3[] vertices,UnityEngine.Vector3[] normals,UnityEngine.Vector4[] tangents,UnityEngine.Vector4[] uv,int[] triangles):this(vertices,normals,tangents,uv,null,null,null,triangles){}
        public UnityEngine.Vector3[] Vertices,Normals;
        public UnityEngine.Vector4[] Tangents,UV0,TexCoord1,TexCoord3;
        public UnityEngine.Color32[] VertexColors;
        public int[] Triangles;
        public MeshData(UnityEngine.Vector3[] vertices,UnityEngine.Vector3[] normals,UnityEngine.Vector4[] tangents,UnityEngine.Vector4[] uv,UnityEngine.Color32[] colors,UnityEngine.Vector4[] tex1,UnityEngine.Vector4[] tex3,int[] triangles)
        {Vertices=vertices;Normals=normals;Tangents=tangents;UV0=uv;VertexColors=colors;TexCoord1=tex1;TexCoord3=tex3;Triangles=triangles;}
    }
}
