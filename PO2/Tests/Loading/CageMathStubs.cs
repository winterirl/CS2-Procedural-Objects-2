// Test math uses System.Numerics for inversion. This exercises the real cage
// codec and recipe, but does not claim to reproduce Unity's native math exactly.
using System;
namespace UnityEngine
{
    public struct Matrix4x4
    {
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;
        public static Matrix4x4 identity=>new Matrix4x4 {m00=1,m11=1,m22=1,m33=1};
        public float this[int row,int column]
        {
            get { switch(row*4+column) { case 0: return m00; case 1: return m01; case 2: return m02; case 3: return m03; case 4: return m10; case 5: return m11; case 6: return m12; case 7: return m13; case 8: return m20; case 9: return m21; case 10: return m22; case 11: return m23; case 12: return m30; case 13: return m31; case 14: return m32; case 15: return m33; default: throw new IndexOutOfRangeException(); } }
            set { switch(row*4+column) { case 0: m00=value; break; case 1: m01=value; break; case 2: m02=value; break; case 3: m03=value; break; case 4: m10=value; break; case 5: m11=value; break; case 6: m12=value; break; case 7: m13=value; break; case 8: m20=value; break; case 9: m21=value; break; case 10: m22=value; break; case 11: m23=value; break; case 12: m30=value; break; case 13: m31=value; break; case 14: m32=value; break; case 15: m33=value; break; default: throw new IndexOutOfRangeException(); } }
        }
        private System.Numerics.Matrix4x4 Numerics=>new System.Numerics.Matrix4x4(m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33);
        private static Matrix4x4 From(System.Numerics.Matrix4x4 v)=>new Matrix4x4 { m00=v.M11, m01=v.M12, m02=v.M13, m03=v.M14, m10=v.M21, m11=v.M22, m12=v.M23, m13=v.M24, m20=v.M31, m21=v.M32, m22=v.M33, m23=v.M34, m30=v.M41, m31=v.M42, m32=v.M43, m33=v.M44 };
        public float determinant=>Numerics.GetDeterminant();
        public Matrix4x4 inverse { get { return System.Numerics.Matrix4x4.Invert(Numerics,out var value)?From(value):default; } }
        public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)=>From(a.Numerics*b.Numerics);
        public Vector3 MultiplyPoint3x4(Vector3 v)=>new Vector3(m00*v.x+m01*v.y+m02*v.z+m03,m10*v.x+m11*v.y+m12*v.z+m13,m20*v.x+m21*v.y+m22*v.z+m23);
        public Vector3 MultiplyVector(Vector3 v)=>new Vector3(m00*v.x+m01*v.y+m02*v.z,m10*v.x+m11*v.y+m12*v.z,m20*v.x+m21*v.y+m22*v.z);
    }
}
namespace PO2.Localization
{
    internal readonly struct LocalizedText
    {
        private readonly string m_Text;
        private LocalizedText(string text) { m_Text=text; }
        internal static LocalizedText Empty=>new LocalizedText("");
        internal static LocalizedText For(string key)=>new LocalizedText(key);
        public override string ToString()=>m_Text;
    }
}
