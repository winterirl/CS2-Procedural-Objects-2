using System;
using System.IO;
using System.Linq;
using PO2.Core;
using PO2.Core.Deformation;
using PO2.Core.Lod;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using UnityEngine;

internal static partial class LoadingTests
{
    private static PersistenceObjectData Fixture(ushort schema)
    {
        var material=PersistenceMaterialData.FromDecoded(MaterialSourceKind.LibraryMaterial,"default",default,VanillaSpecialMaterialKind.None,TextureMappingMode.Original,new Vector2(1,2),13,new Vector2(3,4),0.1f,0.2f,0.3f);
        var mask=PersistenceColorMaskData.FromDecoded(ColorMaskMode.Override,true,Color.white,Color.white,Color.white,new Color(0.1f,0.2f,0.3f,0.4f),Color.white,Color.white);
        var flags=VertexAttributeFlags.VertexColor|VertexAttributeFlags.TexCoord3|VertexAttributeFlags.UV0FourComponent;
        if(schema>=SnapshotFormat.kTexCoord1ObjectSchemaVersion)flags|=VertexAttributeFlags.TexCoord1;
        var areas=new[]{PersistenceSurfaceAreaData.FromDecoded(0,flags,NativeWindowKind.None,material,mask,material,mask)};
        var vertices=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(0,0,0)};
        var part=PersistencePartData.FromDecoded("part",vertices,Enumerable.Repeat(Vector3.up,4).ToArray(),Enumerable.Repeat(new Vector4(1,0,0,1),4).ToArray(),Enumerable.Repeat(new Vector4(0.1f,0.2f,0.3f,0.4f),4).ToArray(),Enumerable.Repeat(new Color32(1,2,3,4),4).ToArray(),
            schema>=SnapshotFormat.kTexCoord1ObjectSchemaVersion?new Vector4[4]:null,new Vector4[4],new[]{0,1,2,3,1,2},new[]{0,1,2,3},4,
            new[]{new StructuralFace(0,0,1,2),new StructuralFace(1,3,1,2)},StructuralTopologySource.Authored,areas,new[]{0,0},1,new Vector3(2,3,4));
        var obj=PersistenceObjectData.FromDecoded(Guid.NewGuid(),"PO test",true,true,new ObjectTransform(new Vector3(10,20,30),new Quaternion(0,0,0,1),new Vector3(1,1,1)),new[]{PersistenceCustomOrientationData.FromDecoded("orientation",new Quaternion(0,0,0,1))},new[]{part,part},new Vector3(1,2,3),schema>=SnapshotFormat.kLodProfileObjectSchemaVersion?LodProfile.Default:null);
        return obj;
    }

    private static void Persistence()
    {
        for(ushort schema=SnapshotFormat.kLegacyObjectSchemaVersion;schema<=SnapshotFormat.kObjectSchemaVersion;schema++)
        {
            var fixture=Fixture(schema);
            foreach(var compression in new[]{SaveCompression.Compact,SaveCompression.Fast})
            {
                var options=new SnapshotEncodingOptions(compression,1);
                byte[] encoded=ObjectBlockWriter.WriteForSchema(fixture,schema,options);
                var current=ObjectBlockReader.Read(encoded,schema);
                var baseline=ReferenceObjectBlockReader.Read(encoded,schema);
                Check(ObjectBlockWriter.WriteForSchema(current,schema,options).SequenceEqual(ObjectBlockWriter.WriteForSchema(baseline,schema,options)),"baseline decoder equivalence schema "+schema);
                Check(current.Parts.Length==2&&!ReferenceEquals(current.Parts[0].Vertices,current.Parts[1].Vertices),"part payloads are independent");
                Check(current.Parts[1].UV0[2].w==0.4f&&current.Parts[1].VertexColors[3].b==3,"second-part segment offset and attributes");
                Same(current.Parts[1].RenderToLogical,new[]{0,1,2,3},"overlapping split IDs retained");
                encoded[encoded.Length-1]^=1;
                Throws<InvalidDataException>(()=>ObjectBlockReader.Read(encoded,schema));
            }
        }

        var save=Guid.NewGuid();var session=Guid.NewGuid();
        var objects=Enumerable.Range(0,8).Select(_=>Fixture(SnapshotFormat.kObjectSchemaVersion)).ToArray();
        var metadata=new SnapshotMetadata("city","autosave","map",null,SnapshotSaveKind.AutoSave,null);
        byte[] bytes=SnapshotWriter.Write(save,session,metadata,objects);
        var serial=SnapshotReader.Read(new MemoryStream(bytes));
        var parallel=SnapshotReader.Read(new MemoryStream(bytes),true,save,session);
        Check(parallel.Objects.Count==8&&parallel.Metadata.SaveKind==SnapshotSaveKind.AutoSave,"autosave container accepted");
        for(int i=0;i<8;i++)Check(ObjectBlockWriter.Write(serial.Objects[i]).SequenceEqual(ObjectBlockWriter.Write(parallel.Objects[i])),"serial and parallel ordered equality");
        // Exact identity rejection must happen before any object payload is read.
        using(var guarded=new HeaderOnlyStream(bytes))
            Check(!SnapshotReader.TryRead(guarded,out _,out _,true,Guid.NewGuid(),session)&&!guarded.ReadObject,"early exact identity rejection");
        using(var guarded=new HeaderOnlyStream(bytes))
            Check(!SnapshotReader.TryRead(guarded,out _,out _,true,save,Guid.NewGuid())&&!guarded.ReadObject,"early session identity rejection");
        long directory=(long)BitConverter.ToUInt64(bytes,56);
        int damagedIndex=3;
        int block=(int)BitConverter.ToUInt64(bytes,(int)directory+damagedIndex*SnapshotFormat.kDirectoryEntrySize+20);
        var damaged=(byte[])bytes.Clone();damaged[block+16]^=1;
        var recovered=SnapshotReader.Read(new MemoryStream(damaged),true);
        Check(recovered.Objects.Count==7&&recovered.SkippedObjects.Count==1&&recovered.SkippedObjects[0].DirectoryIndex==3,"one damaged object contained");
        Check(recovered.Objects[3].PersistentId==objects[4].PersistentId,"skip preserves order");
        var mismatch=(byte[])bytes.Clone();int footprint=(int)directory+2*SnapshotFormat.kDirectoryEntrySize+36;mismatch[footprint]^=1;FixChecksums(mismatch);
        Check(SnapshotReader.Read(new MemoryStream(mismatch),true).SkippedObjects.Single().DirectoryIndex==2,"decoded footprint checked");
        var unknown=(byte[])bytes.Clone();unknown[(int)directory+SnapshotFormat.kDirectoryEntrySize+18]=1;FixChecksums(unknown);
        Check(SnapshotReader.Read(new MemoryStream(unknown),true).SkippedObjects.Single().DirectoryIndex==1,"unknown flags contained");
        var badDirectory=(byte[])bytes.Clone();badDirectory[(int)directory]^=1;
        Check(!SnapshotReader.TryRead(badDirectory,out _,out _),"bad directory rejects container");
        var overlap=(byte[])bytes.Clone();Array.Copy(overlap,(int)directory+20,overlap,(int)directory+SnapshotFormat.kDirectoryEntrySize+20,8);FixChecksums(overlap);
        Check(!SnapshotReader.TryRead(overlap,out _,out _),"overlapping blocks rejected");

        var legacy=LegacyContainer(objects[0],save,session);
        Check(SnapshotReader.TryRead(legacy,out var legacyResult,out _)&&legacyResult.ObjectCount==1,"legacy container v1");
        var result=SnapshotReader.Read(new MemoryStream(bytes));
        var dto=result.Objects[0];var positions=dto.Parts[0].Vertices;var triangles=dto.Parts[0].Triangles;
        using(var owned=result.TakeObjectsForRestore())
        {
            Check(result.ObjectCount==8,"count survives consumption");
            Throws<InvalidOperationException>(()=>result.TakeObjectsForRestore());
            Throws<InvalidOperationException>(()=>{var ignored=result.Objects;});
            var transferred=owned.Take(0);Throws<InvalidOperationException>(()=>owned.Take(0));
            var obj=transferred.RestoreOwned(1);
            Check(ReferenceEquals(obj.Parts[0].Mesh.Vertices,positions)&&ReferenceEquals(obj.Parts[0].Mesh.Triangles,triangles),"owned geometry transferred");
            Check(!ReferenceEquals(obj.Parts[0].SurfaceAreas[0].Material,obj.Parts[1].SurfaceAreas[0].Material),"materials remain independent");
        }
        var reusable=ObjectBlockReader.Read(ObjectBlockWriter.Write(objects[0]),SnapshotFormat.kObjectSchemaVersion);
        var first=reusable.Restore(1);var second=reusable.Restore(2);
        first.Parts[0].Mesh.Vertices[0]=new Vector3(99,0,0);
        Check(second.Parts[0].Mesh.Vertices[0].x==0&&reusable.Parts[0].Vertices[0].x==0,"clipboard/history restore independence");
        var baselinePositions=(Vector3[])reusable.Parts[0].Vertices.Clone();
        var cage=new DeformationCage(Guid.NewGuid(),0,default,new Vector3[8],new[]{new DeformationBaseline(0,Guid.Empty,baselinePositions,(Vector3[])reusable.Parts[0].Normals.Clone(),(Vector4[])reusable.Parts[0].Tangents.Clone())});
        reusable.Deformations=new[]{cage};var deformed=reusable.RestoreOwned(3);
        Check(ReferenceEquals(deformed.Deformations[0].Parts[0].Positions,baselinePositions),"cage baseline transferred");
        Check(!ReferenceEquals(deformed.Parts[0].Mesh.Vertices,baselinePositions)&&deformed.Deformations[0].Matches(deformed),"cage remains separate and binds new part ID");
        Check(deformed.Deformations[0].Id==cage.Id,"cage identity preserved");
    }

    private static void FixChecksums(byte[] bytes)
    {
        int offset=(int)BitConverter.ToUInt64(bytes,56),length=(int)BitConverter.ToUInt64(bytes,64);
        Array.Copy(BitConverter.GetBytes(Crc32.Compute(bytes,offset,length)),0,bytes,72,4);
        Array.Copy(BitConverter.GetBytes(Crc32.Compute(bytes,0,76)),0,bytes,76,4);
    }

    private static byte[] LegacyContainer(PersistenceObjectData data,Guid save,Guid session)
    {
        byte[] block=ObjectBlockWriter.Write(data);
        using(var stream=new MemoryStream())
        {
            stream.Write(SnapshotFormat.s_Magic,0,8);
            SnapshotBinary.WriteUInt16(stream,SnapshotFormat.kLegacyContainerVersion);SnapshotBinary.WriteUInt16(stream,SnapshotFormat.kSnapshotSchemaVersion);
            SnapshotBinary.WriteUInt16(stream,80);SnapshotBinary.WriteUInt16(stream,0);SnapshotBinary.WriteGuid(stream,save);SnapshotBinary.WriteGuid(stream,session);
            SnapshotBinary.WriteUInt32(stream,1);SnapshotBinary.WriteUInt16(stream,52);SnapshotBinary.WriteUInt16(stream,0);SnapshotBinary.WriteUInt64(stream,80);SnapshotBinary.WriteUInt64(stream,52);
            SnapshotBinary.WriteUInt32(stream,0);SnapshotBinary.WriteUInt32(stream,0);
            SnapshotBinary.WriteGuid(stream,data.PersistentId);SnapshotBinary.WriteUInt16(stream,SnapshotFormat.kObjectSchemaVersion);SnapshotBinary.WriteUInt16(stream,0);
            SnapshotBinary.WriteUInt64(stream,132);SnapshotBinary.WriteUInt64(stream,(ulong)block.Length);SnapshotBinary.WriteUInt64(stream,(ulong)SnapshotObjectMetrics.ComputeDecodedFootprint(data,SnapshotFormat.kObjectSchemaVersion));
            SnapshotBinary.WriteUInt32(stream,Crc32.Compute(block));SnapshotBinary.WriteUInt32(stream,0);stream.Write(block,0,block.Length);
            var result=stream.ToArray();FixChecksums(result);return result;
        }
    }

    private sealed class HeaderOnlyStream : MemoryStream
    {
        private readonly long m_BlockStart;
        internal bool ReadObject;
        internal HeaderOnlyStream(byte[] bytes):base(bytes,false){m_BlockStart=(long)(BitConverter.ToUInt64(bytes,56)+BitConverter.ToUInt64(bytes,64));}
        public override int Read(byte[] buffer,int offset,int count)
        {
            if(Position>=m_BlockStart&&count>0){ReadObject=true;throw new Exception("Object read before identity rejection.");}
            return base.Read(buffer,offset,count);
        }
    }
}
