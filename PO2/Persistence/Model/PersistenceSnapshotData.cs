using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Core;
using PO2.Core.Lod;
using PO2.Core.Deformation;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Persistence.Format;
using AssetHash128 = Colossal.Hash128;

namespace PO2.Persistence.Model
{
    // A detached copy gives the encoder one consistent view of each PO.
    internal sealed class PersistenceObjectData
    {
        public Guid PersistentId { get; }
        public string Name { get; }
        public bool IsLocked { get; }
        public bool ReceivesDecals { get; }
        public ObjectTransform Transform { get; }
        public Vector3 LocalPivot { get; }
        internal LodProfile LodProfile { get; }
        public PersistenceCustomOrientationData[] CustomOrientations { get; }
        public PersistencePartData[] Parts { get; }
        internal DeformationCage[] Deformations { get; set; } = Array.Empty<DeformationCage>();

        private PersistenceObjectData(Guid persistentId, string name, bool isLocked, bool receivesDecals, ObjectTransform transform, PersistenceCustomOrientationData[] customOrientations, PersistencePartData[] parts, Vector3 localPivot, LodProfile lodProfile)
        {
            PersistentId = persistentId;
            Name = name ?? string.Empty;
            IsLocked = isLocked;
            ReceivesDecals = receivesDecals;
            Transform = transform;
            LocalPivot = localPivot;
            LodProfile = lodProfile;
            CustomOrientations = customOrientations ?? Array.Empty<PersistenceCustomOrientationData>();
            Parts = parts ?? Array.Empty<PersistencePartData>();
        }

        internal static PersistenceObjectData FromDecoded(Guid persistentId, string name, bool isLocked, bool receivesDecals, ObjectTransform transform, PersistenceCustomOrientationData[] customOrientations, PersistencePartData[] parts, Vector3? localPivot = null, LodProfile lodProfile = null)
        {
            if (persistentId == Guid.Empty)
            {
                throw new ArgumentException("Persisted PO identity cannot be empty.", nameof(persistentId));
            }

            if (parts == null)
            {
                throw new ArgumentNullException(nameof(parts));
            }

            if (customOrientations == null)
            {
                throw new ArgumentNullException(nameof(customOrientations));
            }

            if (customOrientations.Length > SnapshotFormat.kMaxCustomOrientationsPerObject)
            {
                throw new ArgumentException($"Persisted PO contains more than " + $"{SnapshotFormat.kMaxCustomOrientationsPerObject} " + "custom orientations.", nameof(customOrientations));
            }

            for (int i = 0; i < customOrientations.Length; i++)
            {
                if (customOrientations[i] == null)
                {
                    throw new ArgumentException("Persisted custom orientation cannot be null.", nameof(customOrientations));
                }
            }

            Vector3 resolvedPivot = localPivot ?? Vector3.zero;
            if (!IsFinite(resolvedPivot))
            {
                throw new ArgumentException("Persisted object pivot must be finite.", nameof(localPivot));
            }

            return new PersistenceObjectData(persistentId, name, isLocked, receivesDecals, transform, customOrientations, parts, resolvedPivot, lodProfile);
        }

        internal static PersistenceObjectData Capture(ProceduralObject obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            // Part order is part of the PO and is kept exactly as it appears at runtime.
            PersistencePartData[] parts = new PersistencePartData[obj.Parts.Count];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = PersistencePartData.Capture(obj.Parts[i]);
            }

            PersistenceCustomOrientationData[] customOrientations = new PersistenceCustomOrientationData[obj.CustomOrientations.Count];
            for (int i = 0; i < customOrientations.Length; i++)
            {
                customOrientations[i] = PersistenceCustomOrientationData.Capture(obj.CustomOrientations[i]);
            }

            var data = new PersistenceObjectData(obj.PersistentId, obj.Name, obj.IsLocked, obj.ReceivesDecals, obj.Transform, customOrientations, parts, obj.LocalPivot, obj.LodProfile);
            data.Deformations = new DeformationCage[obj.Deformations.Count];
            for (int i = 0; i < data.Deformations.Length; i++) data.Deformations[i] = obj.Deformations[i].Copy(obj, false);
            return data;
        }

        internal ProceduralObject Restore(int runtimeId) => RestoreCore(runtimeId, false);

        // Only a consumed snapshot may donate its arrays to the live model.
        internal ProceduralObject RestoreOwned(int runtimeId, MeshPart[] preparedParts = null) => RestoreCore(runtimeId, true, preparedParts);

        private ProceduralObject RestoreCore(int runtimeId, bool owned, MeshPart[] preparedParts = null)
        {
            if (PersistentId == Guid.Empty)
            {
                throw new InvalidOperationException("Persisted PO identity cannot be empty.");
            }

            List<MeshPart> parts = new List<MeshPart>(Parts.Length);
            for (int i = 0; i < Parts.Length; i++)
            {
                parts.Add(preparedParts != null ? preparedParts[i] : owned ? Parts[i].RestoreOwned() : Parts[i].Restore());
            }

            List<CustomOrientation> customOrientations = new List<CustomOrientation>(CustomOrientations.Length);
            for (int i = 0; i < CustomOrientations.Length; i++)
            {
                customOrientations.Add(CustomOrientations[i].Restore());
            }

            // Runtime IDs are intentionally new after every load.
            // PersistentId is the identity that survives across saves.
            ProceduralObject obj = new ProceduralObject(runtimeId, PersistentId, Name, Transform, parts, customOrientations, localPivot: LocalPivot);
            obj.SetLocked(IsLocked);
            obj.SetReceivesDecals(ReceivesDecals);
            obj.SetLodProfile(LodProfile);
            foreach (var cage in Deformations) obj.SetDeformation(cage.PartIndex, owned ? cage.RestoreOwned(obj) : cage.Copy(obj, false));
            return obj;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }

    internal sealed class PersistenceCustomOrientationData
    {
        public string Name { get; }
        public Quaternion LocalRotation { get; }

        private PersistenceCustomOrientationData(string name, Quaternion localRotation)
        {
            Name = name;
            LocalRotation = localRotation;
        }

        internal static PersistenceCustomOrientationData FromDecoded(string name, Quaternion localRotation)
        {
            if (!CustomOrientation.TryCreate(name, localRotation, out CustomOrientation validated))
            {
                throw new ArgumentException("Persisted custom orientation has an invalid name or rotation.");
            }

            return new PersistenceCustomOrientationData(validated.Name, validated.LocalRotation);
        }

        internal static PersistenceCustomOrientationData Capture(CustomOrientation orientation)
        {
            if (orientation == null)
            {
                throw new ArgumentNullException(nameof(orientation));
            }

            return FromDecoded(orientation.Name, orientation.LocalRotation);
        }

        internal CustomOrientation Restore()
        {
            if (!CustomOrientation.TryCreate(Name, LocalRotation, out CustomOrientation orientation))
            {
                throw new InvalidOperationException("Persisted custom orientation could not be restored.");
            }

            return orientation;
        }
    }

    internal sealed class PersistencePartData
    {
        // These are the pieces that cannot safely be regenerated from positions.
        // In particular, render/logical membership and structural faces preserve edit topology across save/load.
        public string Name { get; }
        public Vector3[] Vertices { get; }
        public Vector3[] Normals { get; }
        public Vector4[] Tangents { get; }
        public Vector4[] UV0 { get; }
        public Color32[] VertexColors { get; }
        public Vector4[] TexCoord1 { get; }
        public Vector4[] TexCoord3 { get; }
        public int[] Triangles { get; }
        public int[] RenderToLogical { get; }
        public int LogicalVertexCount { get; }
        public StructuralFace[] StructuralFaces { get; }
        public StructuralTopologySource? StructuralTopologySource { get; }
        public Vector3? LocalPivot { get; }
        public PersistenceSurfaceAreaData[] SurfaceAreas { get; }
        public int[] TriangleSurfaceAreaIds { get; }
        public int NextSurfaceAreaId { get; }

        private PersistencePartData(string name, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord3, int[] triangles, int[] renderToLogical, int logicalVertexCount, StructuralFace[] structuralFaces, StructuralTopologySource? structuralTopologySource, PersistenceSurfaceAreaData[] surfaceAreas, int[] triangleSurfaceAreaIds, int nextSurfaceAreaId, Vector3? localPivot) : this(name, vertices, normals, tangents, uv0, vertexColors, null, texCoord3, triangles, renderToLogical, logicalVertexCount, structuralFaces, structuralTopologySource, surfaceAreas, triangleSurfaceAreaIds, nextSurfaceAreaId, localPivot)
        {
        }

        private PersistencePartData(string name, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int[] triangles, int[] renderToLogical, int logicalVertexCount, StructuralFace[] structuralFaces, StructuralTopologySource? structuralTopologySource, PersistenceSurfaceAreaData[] surfaceAreas, int[] triangleSurfaceAreaIds, int nextSurfaceAreaId, Vector3? localPivot)
        {
            Name = name ?? string.Empty;
            Vertices = vertices;
            Normals = normals;
            Tangents = tangents;
            UV0 = uv0;
            VertexColors = vertexColors;
            TexCoord1 = texCoord1;
            TexCoord3 = texCoord3;
            Triangles = triangles;
            RenderToLogical = renderToLogical;
            LogicalVertexCount = logicalVertexCount;
            StructuralFaces = structuralFaces;
            StructuralTopologySource = structuralTopologySource;
            LocalPivot = localPivot;
            SurfaceAreas = surfaceAreas;
            TriangleSurfaceAreaIds = triangleSurfaceAreaIds;
            NextSurfaceAreaId = nextSurfaceAreaId;
        }

        internal static PersistencePartData FromDecoded(string name, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord3, int[] triangles, int[] renderToLogical, int logicalVertexCount, StructuralFace[] structuralFaces, StructuralTopologySource? structuralTopologySource, PersistenceSurfaceAreaData[] surfaceAreas, int[] triangleSurfaceAreaIds, int nextSurfaceAreaId, Vector3? localPivot = null)
        {
            return FromDecoded(name, vertices, normals, tangents, uv0, vertexColors, null, texCoord3, triangles, renderToLogical, logicalVertexCount, structuralFaces, structuralTopologySource, surfaceAreas, triangleSurfaceAreaIds, nextSurfaceAreaId, localPivot);
        }

        internal static PersistencePartData FromDecoded(string name, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, Vector4[] uv0, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int[] triangles, int[] renderToLogical, int logicalVertexCount, StructuralFace[] structuralFaces, StructuralTopologySource? structuralTopologySource, PersistenceSurfaceAreaData[] surfaceAreas, int[] triangleSurfaceAreaIds, int nextSurfaceAreaId, Vector3? localPivot = null)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            if (renderToLogical == null) throw new ArgumentNullException(nameof(renderToLogical));
            if (structuralFaces == null) throw new ArgumentNullException(nameof(structuralFaces));
            if (surfaceAreas == null) throw new ArgumentNullException(nameof(surfaceAreas));
            if (triangleSurfaceAreaIds == null)
            {
                throw new ArgumentNullException(nameof(triangleSurfaceAreaIds));
            }

            if (logicalVertexCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalVertexCount));
            }

            if (nextSurfaceAreaId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nextSurfaceAreaId));
            }

            if (localPivot.HasValue && !IsFinite(localPivot.Value))
            {
                throw new ArgumentException("Persisted part pivot must be finite.", nameof(localPivot));
            }

            return new PersistencePartData(name, vertices, normals, tangents, uv0, vertexColors, texCoord1, texCoord3, triangles, renderToLogical, logicalVertexCount, structuralFaces, structuralTopologySource, surfaceAreas, triangleSurfaceAreaIds, nextSurfaceAreaId, localPivot);
        }

        internal static PersistencePartData Capture(MeshPart part)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            MeshData mesh = part.Mesh;
            // Render vertices may be split for UVs/normals while still referring
            // to one editable vertex, so this mapping has to be saved explicitly.
            int[] renderToLogical = new int[part.Topology.RenderVertexCount];
            for (int i = 0; i < renderToLogical.Length; i++)
            {
                renderToLogical[i] = part.Topology.GetLogicalVertexIdForRenderVertex(i);
            }

            PersistenceSurfaceAreaData[] areas = new PersistenceSurfaceAreaData[part.SurfaceAreas.Count];
            for (int i = 0; i < areas.Length; i++)
            {
                areas[i] = PersistenceSurfaceAreaData.Capture(part.SurfaceAreas[i]);
            }

            int[] memberships = new int[part.TriangleSurfaceAreaIds.Count];
            for (int i = 0; i < memberships.Length; i++)
            {
                memberships[i] = part.TriangleSurfaceAreaIds[i];
            }

            // Clone every array. Saving must not observe later edits to the live mesh while encoding is in progress.
            return new PersistencePartData(part.Name, (Vector3[])mesh.Vertices.Clone(), mesh.Normals != null ? (Vector3[])mesh.Normals.Clone() : null, mesh.Tangents != null ? (Vector4[])mesh.Tangents.Clone() : null, mesh.UV0 != null ? (Vector4[])mesh.UV0.Clone() : null, mesh.VertexColors != null ? (Color32[])mesh.VertexColors.Clone() : null, mesh.TexCoord1 != null ? (Vector4[])mesh.TexCoord1.Clone() : null, mesh.TexCoord3 != null ? (Vector4[])mesh.TexCoord3.Clone() : null, (int[])mesh.Triangles.Clone(), renderToLogical, part.Topology.LogicalVertexCount, part.StructuralTopology.CloneFaces(), part.StructuralTopologySource, areas, memberships, part.NextSurfaceAreaId, part.LocalPivot);
        }

        internal MeshPart Restore() => RestoreCore(true);

        // Only detached operation buffers can be handed over. Callers finish all
        // reads before publishing the part, then release their capture references.
        internal MeshPart RestoreOwned(ValidatedPartTopology prepared = null) => RestoreCore(false, prepared);

        private static T[] RestoreStream<T>(T[] values, bool clone) =>
            values == null || !clone ? values : (T[])values.Clone();

        private MeshPart RestoreCore(bool clone, ValidatedPartTopology prepared = null)
        {
            if (Vertices == null || Triangles == null || RenderToLogical == null || StructuralFaces == null || SurfaceAreas == null || TriangleSurfaceAreaIds == null)
            {
                throw new InvalidOperationException($"Persisted mesh part '{Name}' is incomplete.");
            }

            if (prepared != null && (clone || !ReferenceEquals(prepared.Source, this))) throw new InvalidOperationException("Prepared topology belongs to a different snapshot part.");

            MeshData mesh = new MeshData(RestoreStream(Vertices, clone), RestoreStream(Normals, clone), RestoreStream(Tangents, clone), RestoreStream(UV0, clone), RestoreStream(VertexColors, clone), RestoreStream(TexCoord1, clone), RestoreStream(TexCoord3, clone), RestoreStream(Triangles, clone));

            List<SurfaceArea> areas = new List<SurfaceArea>(SurfaceAreas.Length);
            for (int i = 0; i < SurfaceAreas.Length; i++)
            {
                areas.Add(SurfaceAreas[i].Restore());
            }

            // StructuralFace is a value record. Owned loads can also donate the
            // containing array; reusable history/clipboard restores keep a copy.
            StructuralFace[] faces = clone ? (StructuralFace[])StructuralFaces.Clone() : StructuralFaces;

            // City loading can donate validated prepared adjacency. Reusable DTO
            // restores still rebuild independently from their saved topology.
            return MeshPart.Restore(Name, mesh, RenderToLogical, LogicalVertexCount, faces, areas, RestoreStream(TriangleSurfaceAreaIds, clone), NextSurfaceAreaId, StructuralTopologySource, LocalPivot, takeMappingOwnership: !clone, prepared: prepared);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }

    internal sealed class PersistenceSurfaceAreaData
    {
        // Original appearance is stored beside the current appearance.
        // It is still needed after load for converted assets' restore button.
        public int Id { get; }
        internal GeneratedMappingSeed MappingSeed { get; }
        public VertexAttributeFlags SourceVertexAttributes { get; }
        public NativeWindowKind NativeWindowKind { get; }
        public PersistenceMaterialData Material { get; }
        public PersistenceColorMaskData ColorMask { get; }
        public PersistenceMaterialData OriginalMaterial { get; }
        public PersistenceColorMaskData OriginalColorMask { get; }
        public bool HasOriginalState => OriginalMaterial != null;

        private PersistenceSurfaceAreaData(int id, VertexAttributeFlags sourceVertexAttributes, NativeWindowKind nativeWindowKind, PersistenceMaterialData material, PersistenceColorMaskData colorMask, PersistenceMaterialData originalMaterial, PersistenceColorMaskData originalColorMask, GeneratedMappingSeed mappingSeed = null)
        {
            Id = id;
            SourceVertexAttributes = sourceVertexAttributes;
            NativeWindowKind = nativeWindowKind;
            Material = material;
            ColorMask = colorMask;
            OriginalMaterial = originalMaterial;
            OriginalColorMask = originalColorMask;
            MappingSeed = mappingSeed;
        }

        internal static PersistenceSurfaceAreaData FromDecoded(int id, VertexAttributeFlags sourceVertexAttributes, NativeWindowKind nativeWindowKind, PersistenceMaterialData material, PersistenceColorMaskData colorMask, PersistenceMaterialData originalMaterial, PersistenceColorMaskData originalColorMask, GeneratedMappingSeed mappingSeed = null)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (colorMask == null) throw new ArgumentNullException(nameof(colorMask));
            // Original state is one unit. Half of it would make restore behavior
            // depend on made-up defaults.
            if ((originalMaterial == null) != (originalColorMask == null))
            {
                throw new ArgumentException("Original material and color mask must either both be present or both be absent.");
            }

            return new PersistenceSurfaceAreaData(id, sourceVertexAttributes, nativeWindowKind, material, colorMask, originalMaterial, originalColorMask, mappingSeed);
        }

        internal static PersistenceSurfaceAreaData Capture(SurfaceArea area)
        {
            if (area == null)
            {
                throw new ArgumentNullException(nameof(area));
            }

            PersistenceMaterialData originalMaterial = null;
            PersistenceColorMaskData originalColorMask = null;
            if (area.OriginalState != null)
            {
                originalMaterial = PersistenceMaterialData.Capture(area.OriginalState.CreateMaterialClone());
                originalColorMask = PersistenceColorMaskData.Capture(area.OriginalState.CreateColorMaskClone());
            }

            return new PersistenceSurfaceAreaData(area.Id, area.SourceVertexAttributes, area.NativeWindowKind, PersistenceMaterialData.Capture(area.Material), PersistenceColorMaskData.Capture(area.ColorMask), originalMaterial, originalColorMask, area.MappingSeed);
        }

        internal SurfaceArea Restore()
        {
            if (Material == null || ColorMask == null || (HasOriginalState && OriginalColorMask == null))
            {
                throw new InvalidOperationException($"Persisted Surface Area {Id} is incomplete.");
            }

            // Construct a fresh snapshot so the restored area's current material
            // never shares mutable state with its original copy.
            SurfaceAreaOriginalState originalState = null;
            if (HasOriginalState)
            {
                originalState = new SurfaceAreaOriginalState(OriginalMaterial.Restore(), OriginalColorMask.Restore());
            }

            return new SurfaceArea(Id, Material.Restore(), SourceVertexAttributes, ColorMask.Restore(), originalState, NativeWindowKind) { MappingSeed = MappingSeed };
        }
    }

    internal sealed class PersistenceMaterialData
    {
        // Saves all three possible material identities in one fixed record shape.
        // SourceKind decides which identity is actually used.
        public MaterialSourceKind SourceKind { get; }
        public string DefinitionId { get; }
        public AssetHash128 SurfaceGuid { get; }
        public VanillaSpecialMaterialKind VanillaSpecialKind { get; }
        public TextureMappingMode MappingMode { get; }
        public Vector2 TextureScale { get; }
        public float Rotation { get; }
        public Vector2 Offset { get; }
        public float Metallic { get; }
        public float Smoothness { get; }
        public float CoatStrength { get; }

        private PersistenceMaterialData(MaterialSourceKind sourceKind, string definitionId, AssetHash128 surfaceGuid, VanillaSpecialMaterialKind vanillaSpecialKind, TextureMappingMode mappingMode, Vector2 textureScale, float rotation, Vector2 offset, float metallic, float smoothness, float coatStrength)
        {
            SourceKind = sourceKind;
            DefinitionId = definitionId;
            SurfaceGuid = surfaceGuid;
            VanillaSpecialKind = vanillaSpecialKind;
            MappingMode = mappingMode;
            TextureScale = textureScale;
            Rotation = rotation;
            Offset = offset;
            Metallic = metallic;
            Smoothness = smoothness;
            CoatStrength = coatStrength;
        }

        internal static PersistenceMaterialData FromDecoded(MaterialSourceKind sourceKind, string definitionId, AssetHash128 surfaceGuid, VanillaSpecialMaterialKind vanillaSpecialKind, TextureMappingMode mappingMode, Vector2 textureScale, float rotation, Vector2 offset, float metallic, float smoothness, float coatStrength)
        {
            // Reject impossible source/payload combinations before they can turn into ambiguous runtime material state.
            SnapshotValidation.ValidateMaterialPayload(sourceKind, definitionId, vanillaSpecialKind);

            return new PersistenceMaterialData(sourceKind, definitionId, surfaceGuid, vanillaSpecialKind, mappingMode, textureScale, rotation, offset, metallic, smoothness, coatStrength);
        }

        internal static PersistenceMaterialData Capture(MaterialData material)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            return new PersistenceMaterialData(material.SourceKind, material.DefinitionId, material.SurfaceGuid, material.VanillaSpecialKind, material.Mapping.Mode, material.Mapping.TextureScale, material.Mapping.Rotation, material.Mapping.Offset, material.Metallic, material.Smoothness, material.CoatStrength);
        }

        internal MaterialData Restore()
        {
            return MaterialData.Restore(SourceKind, DefinitionId, SurfaceGuid, VanillaSpecialKind, new TextureMappingData(MappingMode, TextureScale, Rotation, Offset), Metallic, Smoothness, CoatStrength);
        }
    }

    internal sealed class PersistenceColorMaskData
    {
        // Source and override colors are both kept even though only one mode is active.
        // Switching modes after load should not lose either set.
        public ColorMaskMode Mode { get; }
        public bool HasSourceMask { get; }
        public Color SourceMask1 { get; }
        public Color SourceMask2 { get; }
        public Color SourceMask3 { get; }
        public Color OverrideMask1 { get; }
        public Color OverrideMask2 { get; }
        public Color OverrideMask3 { get; }

        private PersistenceColorMaskData(ColorMaskMode mode, bool hasSourceMask, Color sourceMask1, Color sourceMask2, Color sourceMask3, Color overrideMask1, Color overrideMask2, Color overrideMask3)
        {
            Mode = mode;
            HasSourceMask = hasSourceMask;
            SourceMask1 = sourceMask1;
            SourceMask2 = sourceMask2;
            SourceMask3 = sourceMask3;
            OverrideMask1 = overrideMask1;
            OverrideMask2 = overrideMask2;
            OverrideMask3 = overrideMask3;
        }

        internal static PersistenceColorMaskData FromDecoded(ColorMaskMode mode, bool hasSourceMask, Color sourceMask1, Color sourceMask2, Color sourceMask3, Color overrideMask1, Color overrideMask2, Color overrideMask3)
        {
            if (mode == ColorMaskMode.Source && !hasSourceMask)
            {
                throw new ArgumentException("Source color-mask mode requires a source mask.", nameof(hasSourceMask));
            }

            return new PersistenceColorMaskData(mode, hasSourceMask, sourceMask1, sourceMask2, sourceMask3, overrideMask1, overrideMask2, overrideMask3);
        }

        internal static PersistenceColorMaskData Capture(ColorMaskData mask)
        {
            if (mask == null)
            {
                throw new ArgumentNullException(nameof(mask));
            }

            return new PersistenceColorMaskData(mask.Mode, mask.HasSourceMask, mask.SourceMask1, mask.SourceMask2, mask.SourceMask3, mask.OverrideMask1, mask.OverrideMask2, mask.OverrideMask3);
        }

        internal ColorMaskData Restore()
        {
            return ColorMaskData.Restore(Mode, HasSourceMask, SourceMask1, SourceMask2, SourceMask3, OverrideMask1, OverrideMask2, OverrideMask3);
        }
    }
}