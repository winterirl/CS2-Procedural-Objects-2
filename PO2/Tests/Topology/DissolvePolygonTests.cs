using System;
using UnityEngine;

internal static partial class Program {
    private static void DissolvePolygonLimits() {
        foreach(int corners in new[]{5,16,128,256}) {
            var positions=new Vector3[corners+1];var triangles=new int[corners*3];
            for(int i=0;i<corners;i++) {
                double angle=i*Math.PI*2/corners;positions[i]=new Vector3((float)Math.Cos(angle),(float)Math.Sin(angle),0);
                triangles[i*3]=i;triangles[i*3+1]=(i+1)%corners;triangles[i*3+2]=corners;
            }
            var part=DissolveFixture(positions,triangles);var plan=DissolvePlan(part,corners);
            var result=DissolveResult(plan);
            Check(result.Mesh.Triangles.Length==(corners-2)*3,"Near-limit polygon keeps exact boundary count");
        }
    }
}
