using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Persistence;
using PO2.Persistence.Format;
using UnityEngine;

internal static partial class LoadingTests
{
    private static int s_Checks;
    private static void Check(bool condition,string message) {s_Checks++;if(!condition)throw new Exception(message);}
    private static void Throws<T>(Action action) where T:Exception {try {action();}catch(T){s_Checks++;return;}throw new Exception("Expected "+typeof(T).Name);}
    private static MeshData Mesh(int vertices,int[] triangles)=>new MeshData(new Vector3[vertices],null,null,null,null,null,null,triangles);
    private static void Same(System.Collections.Generic.IEnumerable<int> actual,System.Collections.Generic.IEnumerable<int> expected,string label)=>Check(actual.SequenceEqual(expected),label);
    public static void Main()
    {
        string fixtureOutput = Environment.GetEnvironmentVariable("PO2_COMPACT_WRITE_FIXTURE");
        if (fixtureOutput != null) { WriteCompactFixture(fixtureOutput); return; }
        string compatibilityInput = Environment.GetEnvironmentVariable("PO2_COMPACT_COMPAT_INPUT");
        if (compatibilityInput != null) { CompactReaderCompatibility(compatibilityInput, Environment.GetEnvironmentVariable("PO2_COMPACT_EXPECT_FALLBACK") == "1"); return; }
        if (Environment.GetEnvironmentVariable("PO2_COMPACT_BENCHMARK") == "1") { CompactTopologyBenchmark(); return; }
        if (Environment.GetEnvironmentVariable("PO2_PREPARED_BENCHMARK") == "1") { PreparedTopologyBenchmark(); return; }
        if (Environment.GetEnvironmentVariable("PO2_TOPOLOGY_RESTORE_BENCHMARK") == "1") { RestoreSortBenchmark(); return; }
        CompactTopologyChecks();
        CompactFixtureChecks();
        PreparedFixtureCompatibility();
        PreparedAdditionalChecks();
        PreparedMappingChecks();
        PreparedTopologyChecks();
        RestoreSortChecks(); DetachedBatchChecks(); SnapshotModelRestoreChecks(); StructuralRestoreChecks(); QueueChecks(); SchedulingIntegrationChecks(); EncodingPipelineChecks(); CageCodecChecks(); FileStoreChecks(); CodecCompatibility(); Compatibility(); Topology(); Faces(); Codecs(); Areas(); Batches(); Gate(); Persistence(); RenderPreparation();
        Console.WriteLine($"PASS: {s_Checks} loading algorithm checks. CPU worker default: {LoadingBatch.WorkerCount}.");
    }
    private static void Topology()
    {
        var random=new Random(17031);
        for(int trial=0;trial<100;trial++)
        {
            int logical=1+random.Next(60), renders=logical+random.Next(60);
            int[] mapping=Enumerable.Range(0,renders).Select(i=>i<logical?i:random.Next(logical)).ToArray();
            // Coincident positions with deliberately distinct logical IDs, plus
            // welded noncoincident positions, duplicate/collapsed triangles.
            int[] triangles=Enumerable.Range(0,3*random.Next(200)).Select(_=>random.Next(renders)).ToArray();
            var mesh=Mesh(renders,triangles);
            for(int i=0;i<renders;i++)mesh.Vertices[i]=new Vector3(i%5,i%3,0);
            var expected=ReferenceMeshTopology.Restore(mesh,mapping,logical);
            var actual=MeshTopology.Restore(mesh,mapping,logical);
            Check(actual.LogicalVertexCount==expected.LogicalVertexCount&&actual.LogicalEdgeCount==expected.LogicalEdgeCount,"counts");
            for(int i=0;i<logical;i++)
            {
                var a=actual.GetVertex(i);var b=expected.GetVertex(i);
                Same(a.RenderVertexIndices,b.RenderVertexIndices,"render membership");Same(a.TriangleIndices,b.TriangleIndices,"triangle membership");Same(a.AdjacentVertexIds,b.AdjacentVertexIds,"adjacent vertices");
            }
            for(int i=0;i<actual.LogicalEdgeCount;i++)
            {
                var a=actual.GetEdge(i);var b=expected.GetEdge(i);
                Check(a.Id==b.Id&&a.VertexA==b.VertexA&&a.VertexB==b.VertexB,"edge IDs/endpoints");Same(a.TriangleIndices,b.TriangleIndices,"edge membership");
                Check(actual.TryGetEdge(a.VertexB,a.VertexA,out var found)&&found.Id==i,"binary edge lookup");
            }
            int original=mapping[0]; mapping[0]=logical;
            Check(actual.GetLogicalVertexIdForRenderVertex(0)==original,"reusable mapping copy"); mapping[0]=original;
        }
        Check(MeshTopology.Restore(Mesh(0,Array.Empty<int>()),Array.Empty<int>(),0).LogicalVertexCount==0,"empty mesh");
        Throws<InvalidOperationException>(()=>MeshTopology.Restore(Mesh(2,new[]{0,1,2}),new[]{0,1},2));
        Throws<InvalidOperationException>(()=>MeshTopology.Restore(Mesh(2,Array.Empty<int>()),new[]{0,0},2));
        Throws<InvalidOperationException>(()=>MeshTopology.Restore(Mesh(2,Array.Empty<int>()),new[]{0,2},2));
        Throws<InvalidOperationException>(()=>MeshTopology.Restore(Mesh(2,new[]{0}),new[]{0,1},2));
        var owned=new[]{0,1}; var adopted=MeshTopology.Restore(Mesh(2,Array.Empty<int>()),owned,2,true);
        var field=typeof(MeshTopology).GetField("m_RenderVertexToLogicalVertex",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        Check(ReferenceEquals(field.GetValue(adopted),owned),"owned mapping transferred");
    }
    private static void Faces()
    {
        var triangles=new[]{0,1,2,0,1,2,2,3,0};
        var faces=new[]{new StructuralFace(0,0,1,2),new StructuralFace(1,0,1,2),new StructuralFace(2,2,3,0)};
        StructuralFaceTriangleResolver.Build(faces,triangles,out var forward,out var reverse);
        Same(reverse,new[]{0,1,2},"identity duplicate triangles");
        var quad=new[]{new StructuralFace(0,0,1,2,3)};
        StructuralFaceTriangleResolver.Build(quad,new[]{0,2,3,0,1,2},out forward,out reverse);
        Same(reverse,new[]{0,0},"reordered quad fallback");
        Throws<InvalidOperationException>(()=>StructuralFaceTriangleResolver.Build(faces,new[]{0,2,1,0,1,2,2,3,0},out _,out _));
    }
    private static EncodedStreamView View(SnapshotStreamSemantic semantic,SnapshotCodec codec,int count,int decoded,byte[] payload)
    {
        var padded=Enumerable.Repeat((byte)0xff,payload.Length+37).ToArray();Array.Copy(payload,0,padded,17,payload.Length);
        return new EncodedStreamView(semantic,codec,count,decoded,new ArraySegment<byte>(padded,17,payload.Length));
    }
    private static void Codecs()
    {
        Same(SnapshotBinary.DecodeDeltaZigZagVarInt((byte[])null,0),Array.Empty<int>(),"legacy empty delta API");
        Same(SnapshotBinary.DecodeRleVarUInt((byte[])null,0),Array.Empty<int>(),"legacy empty RLE API");
        var random=new Random(10);
        int[] values=Enumerable.Range(0,2000).Select(i=>i%5==0?random.Next(250):7).ToArray();
        foreach(var codec in new[]{SnapshotCodec.UnsignedFixed8,SnapshotCodec.UnsignedFixed16,SnapshotCodec.UnsignedFixed32,SnapshotCodec.DeltaZigZagVarInt,SnapshotCodec.DeltaZigZagVarIntDeflate,SnapshotCodec.RleVarUInt,SnapshotCodec.RleVarUIntDeflate})
        {
            byte[] payload;
            if(codec==SnapshotCodec.DeltaZigZagVarInt||codec==SnapshotCodec.DeltaZigZagVarIntDeflate)payload=SnapshotBinary.EncodeDeltaZigZagVarInt(values);
            else if(codec==SnapshotCodec.RleVarUInt||codec==SnapshotCodec.RleVarUIntDeflate)payload=SnapshotBinary.EncodeRleVarUInt(values);
            else payload=SnapshotBinary.EncodeUnsignedFixed(values,codec);
            if(codec==SnapshotCodec.DeltaZigZagVarIntDeflate||codec==SnapshotCodec.RleVarUIntDeflate)payload=SnapshotBinary.Deflate(payload);
            Same(ObjectBlockCodec.DecodeInteger(View(SnapshotStreamSemantic.Triangles,codec,values.Length,values.Length*4,payload)),values,"slice integer "+codec);
            if(codec==SnapshotCodec.UnsignedFixed8)Throws<InvalidDataException>(()=>ObjectBlockCodec.DecodeInteger(View(SnapshotStreamSemantic.Triangles,codec,values.Length,values.Length*4,payload.Concat(new byte[]{1}).ToArray())));
        }
        int[] bits={0,unchecked((int)0x80000000),0x3f800000,0x7fc12345,0x7f800000,unchecked((int)0xff800000)};
        byte[] raw=bits.SelectMany(BitConverter.GetBytes).ToArray();
        foreach(var codec in new[]{SnapshotCodec.Raw,SnapshotCodec.Deflate,SnapshotCodec.ByteShuffleDeflate})
        {
            byte[] payload=codec==SnapshotCodec.Raw?raw:SnapshotBinary.Deflate(codec==SnapshotCodec.ByteShuffleDeflate?SnapshotBinary.ShuffleFloatBytes(raw):raw);
            var result=ObjectBlockCodec.DecodeVector3(View(SnapshotStreamSemantic.Positions,codec,2,raw.Length,payload));
            int[] actual=result.SelectMany(v=>new[]{v.x,v.y,v.z}).Select(BitConverter.SingleToInt32Bits).ToArray();Same(actual,bits,"float bits "+codec);
        }
        var color=ObjectBlockCodec.DecodeColors(View(SnapshotStreamSemantic.VertexColors,SnapshotCodec.UniformColor32,5,20,new byte[]{1,2,3,4}));
        Check(color.Length==5&&color[4].a==4,"uniform color slice");
        var empty=View(SnapshotStreamSemantic.Positions,SnapshotCodec.Raw,0,0,Array.Empty<byte>());
        Check(ObjectBlockCodec.DecodeVector3(empty).Length==0,"empty slice");
        Throws<InvalidDataException>(()=>ObjectBlockCodec.DecodeInteger(View(SnapshotStreamSemantic.Triangles,SnapshotCodec.RleVarUInt,1,4,new byte[]{1,0})));
        using(var body=new MemoryStream(new byte[100],16,80,false))Check(body.Position==0&&body.Length==80,"body view origin");
    }
    private static void Areas()
    {
        var source=Mesh(5,new[]{3,1,4,0,1,2,4,1,3});
        source.Vertices=Enumerable.Range(0,5).Select(i=>new Vector3(i,0,0)).ToArray();
        source.VertexColors=Enumerable.Range(0,5).Select(i=>new Color32((byte)i,0,0,255)).ToArray();
        var builder=new SurfaceAreaGeometryBuilder(source);var area=new SurfaceArea{SourceVertexAttributes=VertexAttributeFlags.VertexColor};
        builder.Build(area,new[]{0,2},out var mesh,out var map);
        Same(map,new[]{3,1,4},"first encounter order");Same(mesh.Triangles,new[]{0,1,2,2,1,0},"scattered area triangles");Check(mesh.VertexColors[0].r==3,"area attributes");
        builder.Build(new SurfaceArea(),new[]{1},out mesh,out map);
        Same(map,new[]{0,1,2},"remap reset");Check(mesh.VertexColors==null,"unflagged channel remains absent");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();Throws<OperationCanceledException>(()=>builder.Build(area,new[]{0},out _,out _,cancel.Token));}
        builder.Build(area,new[]{0},out mesh,out map);Same(map,new[]{3,1,4},"cancel cleanup");
        Check(!ReferenceEquals(mesh.Vertices,source.Vertices),"runtime area independent of source");
    }
    private static void Batches()
    {
        int active=0,peak=0,finished=0,released=0,consumed=0;
        LoadingBatch.Run(12,i=>
        {
            int value=Interlocked.Increment(ref active),old;
            do {old=peak;if(old>=value)break;}while(Interlocked.CompareExchange(ref peak,value,old)!=old);
            Thread.Sleep((i%2==0)?8:1);Interlocked.Decrement(ref active);Interlocked.Increment(ref finished);return i;
        },(i,v)=>{Check(i==consumed++&&v==i,"ordered consumption");},_=>released++,workers:4);
        Check(peak<=4&&finished==12&&released==12,"bounded concurrent work");
        finished=0;released=0;
        Throws<InvalidOperationException>(()=>LoadingBatch.Run(4,i=>{Thread.Sleep(5);Interlocked.Increment(ref finished);return i;},(i,v)=>throw new InvalidOperationException(),_=>released++,workers:4));
        Check(finished==4&&released==4,"consumer failure drains and releases");
        finished=0;released=0;
        Throws<InvalidOperationException>(()=>LoadingBatch.Run(4,i=>{if(i==1)throw new InvalidOperationException();Interlocked.Increment(ref finished);return i;},(i,v)=>{},_=>released++,workers:4));
        Check(finished>=1&&finished<=3&&released==finished,"worker failure drains and releases started work");
        finished=0;released=0;
        Throws<InvalidOperationException>(()=>LoadingBatch.Run(4,i=>{Thread.Sleep(5);Interlocked.Increment(ref finished);return i;},(i,v)=>{},v=>{released++;if(v==0)throw new InvalidOperationException();},workers:4));
        Check(finished==4&&released==4,"release failure still drains all work");
    }
    private static void Gate()
    {
        var gate=new SnapshotAccessGate();Check(gate.TryResume(gate.Revision),"resume");var lease=gate.TryEnter(gate.Revision);
        using(var started=new ManualResetEventSlim())
        {
            var task=Task.Run(()=>{started.Set();gate.SuspendAndDrain();});started.Wait();
            Check(!task.Wait(10),"drain waits for lease");lease.Dispose();Check(task.Wait(2000),"lease wakes drain");
            lease.Dispose();Check(!gate.IsActive&&gate.TryEnter(gate.Revision)==null,"drain remains suspended and lease idempotent");
        }
    }
}
