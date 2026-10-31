using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Conversion;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.Materials;
using AssetHash128 = Colossal.Hash128;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Geometry
{
    // One independently rendered/editable part of a PO. 
    // It keeps the render mesh, both topology layers, and per-triangle Surface Area membership in one place 
    // this is so those pieces cant drift apart quietly w/o being noticed.
    public sealed partial class MeshPart
    {
        // Area 0 always exists as the default Surface Area.
        public const int kDefaultSurfaceAreaId = 0;

        private List<SurfaceArea> m_SurfaceAreas;
        private int[] m_TriangleSurfaceAreaIds;

        private int m_NextSurfaceAreaId;

        // Triangle -> structural face is derived cache data.
        // -1 means the triangle is raw geometry outside the structural layer.
        private int[] m_TriangleStructuralFaceIds;
        private StructuralFaceTriangles[] m_StructuralFaceTriangles;
        private TopologyEditCapabilities m_TopologyEditCapabilities;
        private uint m_TopologyEditCapabilitiesVersion;
        private bool m_HasTopologyEditCapabilities;

        public string Name { get; private set; }
        public Vector3 LocalPivot { get; private set; }

        public uint PivotVersion { get; private set; }

        // A cold history payload can replace this instance, but not this part's identity.
        // City saves deliberately omit it; normal clones start with a fresh ID.
        internal Guid PartHistoryId { get; private set; } = Guid.NewGuid();

        internal void RestoreHistoryIdentity(Guid id)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("History part identity cannot be empty.", nameof(id));
            }

            PartHistoryId = id;
        }

        public MeshData Mesh { get; private set; }
        public MeshTopology Topology { get; private set; }
        public StructuralTopology StructuralTopology { get; private set; }
        internal StructuralTopologySource StructuralTopologySource { get; private set; }

        public bool SupportsDestructiveTopologyEditing =>
            StructuralTopologySource == PO2.Geometry.Topology.StructuralTopologySource.Authored;

        internal TopologyEditCapabilities TopologyEditCapabilities
        {
            get
            {
                if (!m_HasTopologyEditCapabilities || m_TopologyEditCapabilitiesVersion != TopologyVersion)
                {
                    m_TopologyEditCapabilities = TopologyEditor.AnalyzeTopologyEditCapabilities(this);
                    m_TopologyEditCapabilitiesVersion = TopologyVersion;
                    m_HasTopologyEditCapabilities = true;
                }

                return m_TopologyEditCapabilities;
            }
        }

        public IReadOnlyList<SurfaceArea> SurfaceAreas =>
            m_SurfaceAreas;

        public IReadOnlyList<int> TriangleSurfaceAreaIds =>
            m_TriangleSurfaceAreaIds;

        internal int NextSurfaceAreaId => m_NextSurfaceAreaId;
        public uint SurfaceAreaMembershipVersion { get; private set; }

        public IReadOnlyList<int> TriangleStructuralFaceIds =>
            m_TriangleStructuralFaceIds;

        // Compatibility surface for callers that still address material state at the mesh-part level.
        // Surface Area editing uses explicit area APIs;
        // these accessors intentionally continue to target default Area 0.
        // this  is what we in the industry call technical debt, and I should've cleaned it up before moving on
        // but you know, once code is there and working you're stuck with it forever
        // ... >.>
        public MaterialData Material => PrimarySurfaceArea.Material;

        public uint MaterialSourceVersion =>
            PrimarySurfaceArea.MaterialSourceVersion;

        public uint MaterialPropertiesVersion =>
            PrimarySurfaceArea.MaterialPropertiesVersion;

        public uint MappingVersion => PrimarySurfaceArea.MappingVersion;

        public uint GeometryVersion { get; private set; }

        // Position-only previews let the renderer avoid touching every other
        // mesh stream during a translated part drag.
        public uint PositionVersion { get; private set; }
        public uint FullGeometryVersion { get; private set; }
        public uint DirectionVersion { get; private set; }
        public uint TopologyVersion { get; private set; }

        private SurfaceArea PrimarySurfaceArea =>
            m_SurfaceAreas[0];

        public MeshPart(string name, MeshData mesh, MaterialData material, StructuralFace[] structuralFaces = null) : this(name, mesh, material, new ColorMaskData(), null, structuralFaces)
        {
        }

        public MeshPart(string name, MeshData mesh, MaterialData material, ColorMaskData colorMask, SurfaceAreaOriginalState originalState, StructuralFace[] structuralFaces = null, NativeWindowKind nativeWindowKind = NativeWindowKind.None) : this(name, mesh, material, colorMask, originalState, structuralFaces, nativeWindowKind, null)
        {
        }

        // Converted parts use this overload so their native build can be timed
        // and safely fall back without changing the ordinary part path.
        internal MeshPart(string name, MeshData mesh, MaterialData material, ColorMaskData colorMask, SurfaceAreaOriginalState originalState, StructuralFace[] structuralFaces, NativeWindowKind nativeWindowKind, ConversionPartProfile conversionProfile, int[] convertedTriangleSurfaceAreaIds = null)
        {
            Name = NormalizeName(name);
            Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            // New parts infer logical vertices from overlapping positions.
            MeshTopology conversionTopology = null;
            string topologyFallbackReason = string.Empty;
            bool nativeTopologyBuilt = false;
            if (conversionProfile != null)
            {
                try
                {
                    nativeTopologyBuilt = NativeMeshTopologyBuilder.TryBuild(mesh, MeshTopology.kDefaultWeldEpsilon, out conversionTopology, out topologyFallbackReason);
                }
                catch (InvalidOperationException exception)
                {
                    topologyFallbackReason = $"Native topology validation failed: {exception.Message}";
                }
            }

            if (conversionProfile == null)
            {
                conversionTopology = MeshTopology.Build(mesh);
            }
            else if (!nativeTopologyBuilt)
            {
                conversionProfile.TopologyFallbackReason = topologyFallbackReason;

                conversionTopology = MeshTopology.Build(mesh);
            }

            Topology = conversionTopology;
            bool buildConvertedTopology = conversionProfile != null && structuralFaces == null;
            if (buildConvertedTopology)
            {
                ConvertedStructuralTopologyResult structuralResult = ConvertedStructuralTopologyBuilder.Build(mesh, Topology, convertedTriangleSurfaceAreaIds, conversionProfile);
                StructuralTopology = structuralResult.Topology;
                m_StructuralFaceTriangles = structuralResult.FaceTriangles;
                m_TriangleStructuralFaceIds = structuralResult.TriangleFaceIds;
                StructuralTopologySource = structuralResult.TopologySource;
            }
            else
            {
                StructuralTopology = StructuralTopology.Build(Topology, mesh.Vertices.Length, structuralFaces ?? Array.Empty<StructuralFace>());
                StructuralTopologySource = PO2.Geometry.Topology.StructuralTopologySource.Authored;
            }

            ColorMaskData initialColorMask = colorMask ?? new ColorMaskData();
            m_SurfaceAreas = new List<SurfaceArea>(1)
            {
                new SurfaceArea(kDefaultSurfaceAreaId, material, VertexAttributeFlags.None, initialColorMask, originalState, nativeWindowKind),
            };
            m_NextSurfaceAreaId = kDefaultSurfaceAreaId + 1;
            m_TriangleSurfaceAreaIds = CreateDefaultTriangleMembership(mesh.Triangles.Length / 3);

            if (!buildConvertedTopology)
            {
                RebuildStructuralFaceTriangleMap();
            }

            ValidateSurfaceAreaState();

            LocalPivot = CalculateDefaultLocalPivot();
        }

        public void MarkGeometryChanged()
        {
            GeometryVersion++;
            PositionVersion++;
            FullGeometryVersion++;
            DirectionVersion++;
        }

        internal void MarkPositionsChanged()
        {
            GeometryVersion++;
            PositionVersion++;
        }

        internal void MarkDirectionsChanged()
        {
            GeometryVersion++;
            DirectionVersion++;
        }

        internal void MarkPositionsAndDirectionsChanged()
        {
            GeometryVersion++;
            PositionVersion++;
            DirectionVersion++;
        }

        internal bool Rename(string name)
        {
            string normalized = NormalizeName(name);
            if (string.Equals(Name, normalized, StringComparison.Ordinal))
            {
                return false;
            }

            Name = normalized;
            return true;
        }

        internal bool SetLocalPivot(Vector3 localPivot)
        {
            if (!IsFinite(localPivot) || LocalPivot.Equals(localPivot))
            {
                return false;
            }

            LocalPivot = localPivot;
            PivotVersion++;
            return true;
        }

        public void ApplyTopologyMutation(MeshMutationResult mutation)
        {
            if (mutation == null)
            {
                throw new ArgumentNullException(nameof(mutation));
            }

            if (!SupportsDestructiveTopologyEditing)
            {
                throw new InvalidOperationException("Destructive topology editing is not enabled for " + "converted topology.");
            }

            // Build the entire replacement first.
            // A failed cut/dissolve/merge should leave the old part completely usable, not half updated.
            MeshData replacementMesh = new MeshData(mutation.Vertices, mutation.Normals, mutation.Tangents, mutation.UV0, mutation.VertexColors, mutation.TexCoord1, mutation.TexCoord3, mutation.Triangles);

            MeshTopology replacementTopology = mutation.RenderToLogical != null ? MeshTopology.Restore(replacementMesh, mutation.RenderToLogical, mutation.LogicalVertexCount) : MeshTopology.Build(replacementMesh);

            StructuralTopology replacementStructuralTopology = StructuralTopology.Build(replacementTopology, mutation.Vertices.Length, mutation.StructuralFaces);

            StructuralFaceTriangleResolver.Build(replacementStructuralTopology.Faces, replacementMesh.Triangles, out StructuralFaceTriangles[] replacementFaceTriangles, out int[] replacementTriangleFaceIds);

            ValidateSurfaceAreaState(replacementMesh, replacementStructuralTopology, replacementFaceTriangles, mutation.TriangleSurfaceAreaIds);

            int[] replacementMemberships = (int[])mutation.TriangleSurfaceAreaIds.Clone();

            // Everything above passed. This is the actual commit point.
            Mesh.ReplaceTopology(mutation.Vertices, mutation.Normals, mutation.Tangents, mutation.UV0, mutation.VertexColors, mutation.TexCoord1, mutation.TexCoord3, mutation.Triangles);

            Topology = replacementTopology;
            StructuralTopology = replacementStructuralTopology;
            StructuralTopologySource = PO2.Geometry.Topology.StructuralTopologySource.Authored;
            m_TriangleSurfaceAreaIds = replacementMemberships;
            m_StructuralFaceTriangles = replacementFaceTriangles;
            m_TriangleStructuralFaceIds = replacementTriangleFaceIds;
            PruneEmptySurfaceAreas();
            SurfaceAreaMembershipVersion++;

            TopologyVersion++;
            MarkGeometryChanged();
        }

        public int CreateSurfaceArea(MaterialData material)
        {
            return CreateSurfaceArea(material, VertexAttributeFlags.None);
        }

        public int CreateSurfaceArea(MaterialData material, VertexAttributeFlags sourceVertexAttributes)
        {
            return CreateSurfaceArea(material, sourceVertexAttributes, new ColorMaskData(), null);
        }

        public int CreateSurfaceArea(MaterialData material, VertexAttributeFlags sourceVertexAttributes, ColorMaskData colorMask, SurfaceAreaOriginalState originalState, NativeWindowKind nativeWindowKind = NativeWindowKind.None)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            // IDs are stable and not recycled when an area is removed.
            // Reusing one would make stale selection/persistence references point at a different material region.
            int id = m_NextSurfaceAreaId++;
            m_SurfaceAreas.Add(new SurfaceArea(id, material.Clone(), sourceVertexAttributes, colorMask, originalState, nativeWindowKind));
            return id;
        }

        public bool SetSurfaceAreaSourceVertexAttributes(int surfaceAreaId, VertexAttributeFlags attributes)
        {
            if (!TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area))
            {
                return false;
            }

            area.SetSourceVertexAttributes(attributes);
            return true;
        }

        public bool AssignTrianglesToSurfaceArea(int[] triangleIds, int surfaceAreaId)
        {
            return AssignTrianglesToSurfaceArea(triangleIds, surfaceAreaId, out string _);
        }

        public bool AssignTrianglesToSurfaceArea(int[] triangleIds, int surfaceAreaId, out string failureReason)
        {
            failureReason = string.Empty;

            if (triangleIds == null || triangleIds.Length == 0)
            {
                failureReason = "No triangles were supplied for Surface Area assignment.";
                return false;
            }

            if (!ContainsSurfaceAreaId(surfaceAreaId))
            {
                failureReason = $"Surface Area {surfaceAreaId} does not exist on mesh part '{Name}'.";
                return false;
            }

            // Work on a copy because a partial assignment is not useful.
            // This also lets validation reject splitting one structural quad across
            // two areas without touching the current membership array
            int[] replacement = (int[])m_TriangleSurfaceAreaIds.Clone();

            for (int i = 0; i < triangleIds.Length; i++)
            {
                int triangleId = triangleIds[i];
                if (triangleId < 0 || triangleId >= replacement.Length)
                {
                    failureReason = $"Triangle {triangleId} is outside mesh part '{Name}' " + $"triangle range 0..{replacement.Length - 1}.";
                    return false;
                }

                replacement[triangleId] = surfaceAreaId;
            }

            int[] previous = m_TriangleSurfaceAreaIds;
            m_TriangleSurfaceAreaIds = replacement;

            try
            {
                ValidateSurfaceAreaState();
            }
            catch (InvalidOperationException exception)
            {
                m_TriangleSurfaceAreaIds = previous;
                failureReason = exception.Message;
                return false;
            }

            PruneEmptySurfaceAreas();
            SurfaceAreaMembershipVersion++;
            return true;
        }

        public bool TryDeleteSurfaceArea(int surfaceAreaId, out int reassignedTriangleCount, out string failureReason)
        {
            reassignedTriangleCount = 0;
            failureReason = string.Empty;

            if (surfaceAreaId == kDefaultSurfaceAreaId)
            {
                failureReason = "The default Surface Area cannot be deleted.";
                return false;
            }

            if (!ContainsSurfaceAreaId(surfaceAreaId))
            {
                failureReason = $"Surface Area {surfaceAreaId} does not exist on mesh part '{Name}'.";
                return false;
            }

            List<int> triangleIds = new List<int>();
            for (int triangleId = 0; triangleId < m_TriangleSurfaceAreaIds.Length; triangleId++)
            {
                if (m_TriangleSurfaceAreaIds[triangleId] == surfaceAreaId)
                {
                    triangleIds.Add(triangleId);
                }
            }

            if (triangleIds.Count == 0)
            {
                failureReason = $"Surface Area {surfaceAreaId} has no assigned triangles.";
                return false;
            }

            int[] reassignedTriangles = triangleIds.ToArray();
            if (!AssignTrianglesToSurfaceArea(reassignedTriangles, kDefaultSurfaceAreaId, out failureReason))
            {
                return false;
            }

            reassignedTriangleCount = reassignedTriangles.Length;
            return true;
        }

        public bool TryGetTriangleStructuralFaceId(int triangleId, out int structuralFaceId)
        {
            structuralFaceId = -1;
            if (triangleId < 0 || triangleId >= m_TriangleStructuralFaceIds.Length)
            {
                return false;
            }

            structuralFaceId = m_TriangleStructuralFaceIds[triangleId];
            return structuralFaceId >= 0;
        }

        public bool TryGetStructuralFaceTriangles(int structuralFaceId, out StructuralFaceTriangles triangles)
        {
            triangles = default;
            if (structuralFaceId < 0 || structuralFaceId >= m_StructuralFaceTriangles.Length)
            {
                return false;
            }

            triangles = m_StructuralFaceTriangles[structuralFaceId];
            return triangles.Count > 0;
        }

        public int GetTriangleSurfaceAreaId(int triangleId)
        {
            if (triangleId < 0 || triangleId >= m_TriangleSurfaceAreaIds.Length)
            {
                return kDefaultSurfaceAreaId;
            }

            return m_TriangleSurfaceAreaIds[triangleId];
        }

        public bool TryCreateSurfaceAreaFromTriangles(int[] triangleIds, int sourceSurfaceAreaId, out int newSurfaceAreaId)
        {
            return TryCreateSurfaceAreaFromTriangles(triangleIds, sourceSurfaceAreaId, out newSurfaceAreaId, out string _);
        }

        public bool TryCreateSurfaceAreaFromTriangles(int[] triangleIds, int sourceSurfaceAreaId, out int newSurfaceAreaId, out string failureReason)
        {
            newSurfaceAreaId = -1;
            failureReason = string.Empty;

            if (triangleIds == null || triangleIds.Length == 0)
            {
                failureReason = "No triangles were supplied for Surface Area creation.";
                return false;
            }

            if (!TryGetSurfaceArea(sourceSurfaceAreaId, out SurfaceArea sourceArea))
            {
                failureReason = $"Source Surface Area {sourceSurfaceAreaId} does not exist " + $"on mesh part '{Name}'.";
                return false;
            }

            int candidateId = m_NextSurfaceAreaId;
            if (candidateId <= 0 || candidateId == int.MaxValue)
            {
                failureReason = "No more Surface Area IDs are available on this part.";
                return false;
            }

            // A new area starts as a copy of the source appearance.
            // This makes "new area from selection" visually neutral until the user changes its material.
            SurfaceArea candidate = new SurfaceArea(candidateId, sourceArea.Material.Clone(), sourceArea.SourceVertexAttributes, sourceArea.ColorMask.Clone(), sourceArea.OriginalState, sourceArea.NativeWindowKind);
            m_SurfaceAreas.Add(candidate);
            m_NextSurfaceAreaId++;

            if (!AssignTrianglesToSurfaceArea(triangleIds, candidateId, out failureReason))
            {
                m_SurfaceAreas.RemoveAt(m_SurfaceAreas.Count - 1);
                m_NextSurfaceAreaId = candidateId;
                return false;
            }

            newSurfaceAreaId = candidateId;
            return true;
        }

        public bool TryGetStructuralFaceSurfaceAreaId(int structuralFaceId, out int surfaceAreaId)
        {
            surfaceAreaId = kDefaultSurfaceAreaId;

            if (!TryGetStructuralFaceTriangles(structuralFaceId, out StructuralFaceTriangles triangles))
            {
                return false;
            }

            int triangleA = triangles.TriangleA;
            if (triangleA < 0 || triangleA >= m_TriangleSurfaceAreaIds.Length)
            {
                return false;
            }

            surfaceAreaId = m_TriangleSurfaceAreaIds[triangleA];

            if (triangles.Count == 2 && (triangles.TriangleB < 0 || triangles.TriangleB >= m_TriangleSurfaceAreaIds.Length || m_TriangleSurfaceAreaIds[triangles.TriangleB] != surfaceAreaId))
            {
                return false;
            }

            return ContainsSurfaceAreaId(surfaceAreaId);
        }

        public int GetSurfaceAreaTriangleCount(int surfaceAreaId)
        {
            int count = 0;
            for (int i = 0; i < m_TriangleSurfaceAreaIds.Length; i++)
            {
                if (m_TriangleSurfaceAreaIds[i] == surfaceAreaId)
                {
                    count++;
                }
            }

            return count;
        }

        public bool TryGetSurfaceArea(int surfaceAreaId, out SurfaceArea surfaceArea)
        {
            for (int i = 0; i < m_SurfaceAreas.Count; i++)
            {
                SurfaceArea candidate = m_SurfaceAreas[i];
                if (candidate.Id == surfaceAreaId)
                {
                    surfaceArea = candidate;
                    return true;
                }
            }

            surfaceArea = null;
            return false;
        }

        public bool SetSurfaceAreaColorMaskMode(int surfaceAreaId, ColorMaskMode mode)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetColorMaskMode(mode);
        }

        public bool SetSurfaceAreaColorMaskOverride(int surfaceAreaId, Color mask1, Color mask2, Color mask3)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetColorMaskOverride(mask1, mask2, mask3);
        }

        public bool RestoreSurfaceAreaOriginalAppearance(int surfaceAreaId)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.RestoreOriginalAppearance();
        }

        public bool RestoreAllSurfaceAreasOriginalAppearance()
        {
            bool changed = false;
            for (int i = 0; i < m_SurfaceAreas.Count; i++)
            {
                if (m_SurfaceAreas[i].RestoreOriginalAppearance())
                {
                    changed = true;
                }
            }

            return changed;
        }

        public bool SetSurfaceAreaMappingMode(int surfaceAreaId, TextureMappingMode mode)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetMappingMode(mode);
        }

        public bool SetSurfaceAreaTextureScale(int surfaceAreaId, Vector2 textureScale)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetTextureScale(textureScale);
        }

        public bool SetSurfaceAreaTextureRotation(int surfaceAreaId, float rotation)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetTextureRotation(rotation);
        }

        public bool SetSurfaceAreaTextureOffset(int surfaceAreaId, Vector2 offset)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetTextureOffset(offset);
        }

        public bool SetSurfaceAreaLibraryMaterial(int surfaceAreaId, string definitionId)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetLibraryMaterial(definitionId);
        }

        public int ReplaceLibraryMaterialReferencesWithDefault(string definitionId)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                return 0;
            }

            int replacementCount = 0;
            for (int i = 0; i < m_SurfaceAreas.Count; i++)
            {
                if (m_SurfaceAreas[i].ReplaceLibraryMaterialReferenceWithDefault(definitionId))
                {
                    replacementCount++;
                }
            }

            return replacementCount;
        }

        public int ValidateLibraryReferences()
        {
            int replacementCount = 0;
            for (int i = 0; i < m_SurfaceAreas.Count; i++)
            {
                if (m_SurfaceAreas[i].ValidateLibraryReference())
                {
                    replacementCount++;
                }
            }

            return replacementCount;
        }

        public bool SetSurfaceAreaMetallic(int surfaceAreaId, float value)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetMetallic(value);
        }

        public bool SetSurfaceAreaSmoothness(int surfaceAreaId, float value)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetSmoothness(value);
        }

        public bool SetSurfaceAreaCoatStrength(int surfaceAreaId, float value)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetCoatStrength(value);
        }

        public bool ResetSurfaceAreaMaterialProperties(int surfaceAreaId)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.ResetMaterialPropertiesToLibraryDefaults();
        }

        public bool SetSurfaceAreaSurfaceMaterial(int surfaceAreaId, AssetHash128 surfaceGuid)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetSurfaceMaterial(surfaceGuid);
        }

        public bool SetSurfaceAreaVanillaSpecialMaterial(int surfaceAreaId, VanillaSpecialMaterialKind kind)
        {
            return TryGetSurfaceArea(surfaceAreaId, out SurfaceArea area) && area.SetVanillaSpecialMaterial(kind);
        }

        public MeshPart Clone()
        {
            return CloneWithGeometry(Name, Mesh.Clone(), StructuralTopology.CloneFaces(), LocalPivot);
        }

        internal MeshPart CloneWithGeometry(string name, MeshData mesh, StructuralFace[] structuralFaces)
        {
            return CloneWithGeometry(name, mesh, structuralFaces, LocalPivot);
        }

        internal MeshPart CloneWithGeometry(string name, MeshData mesh, StructuralFace[] structuralFaces, Vector3 localPivot)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (mesh.Vertices.Length != Topology.RenderVertexCount)
            {
                throw new ArgumentException("Replacement geometry must preserve the render vertex count.", nameof(mesh));
            }

            List<SurfaceArea> clonedAreas = new List<SurfaceArea>(m_SurfaceAreas.Count);

            for (int i = 0; i < m_SurfaceAreas.Count; i++)
            {
                clonedAreas.Add(m_SurfaceAreas[i].Clone());
            }

            // Clone the current mapping exactly.
            // Rebuilding by position here would make duplicate change the editable topology of the copy.
            int[] renderToLogical = new int[Topology.RenderVertexCount];
            for (int i = 0; i < renderToLogical.Length; i++)
            {
                renderToLogical[i] = Topology.GetLogicalVertexIdForRenderVertex(i);
            }

            return Restore(name, mesh, renderToLogical, Topology.LogicalVertexCount, structuralFaces, clonedAreas, (int[])m_TriangleSurfaceAreaIds.Clone(), m_NextSurfaceAreaId, StructuralTopologySource, localPivot);
        }









        private void PruneEmptySurfaceAreas()
        {
            // Area 0 is permanent even when it has no triangles.
            // Other empty areas are just dead material state and can be removed.
            for (int i = m_SurfaceAreas.Count - 1; i >= 0; i--)
            {
                SurfaceArea area = m_SurfaceAreas[i];
                if (area.Id == kDefaultSurfaceAreaId || GetSurfaceAreaTriangleCount(area.Id) > 0)
                {
                    continue;
                }

                m_SurfaceAreas.RemoveAt(i);
            }
        }

        private int[] CreateDefaultTriangleMembership(int triangleCount)
        {
            int[] memberships = new int[triangleCount];
            for (int i = 0; i < memberships.Length; i++)
            {
                memberships[i] = kDefaultSurfaceAreaId;
            }

            return memberships;
        }














    }
}