using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Transforms;
using PO2.Localization;
using PO2.Materials;
using AssetHash128 = Colossal.Hash128;

namespace PO2.Core
{
    // Editable PO data: identity, transform, parts, orientations, and change versions.
    // Rendering resources and selection live in their own systems.
    public sealed partial class ProceduralObject
    {
        public const int kMaxCustomOrientationCount = 12;
        public const int kMaxPartCount = 4_096;

        private readonly List<MeshPart> m_Parts;
        private readonly List<CustomOrientation> m_CustomOrientations;

        private Bounds m_LocalBounds;
        private bool m_HasLocalBounds;
        private bool m_LocalBoundsDirty;

        // Id is a simple runtime lookup key and is regenerated on load.
        // PersistentId is the stable identity used for serialization / deserialization.
        // It was easier to just add the guid field than to heavily refactor Id way after the fact.
        public int Id { get; }
        public Guid PersistentId { get; }
        private string m_Name;
        internal event Action NameChanged;

        public string Name
        {
            get => m_Name;
            private set
            {
                if (m_Name == value) return;
                m_Name = value;
                NameChanged?.Invoke();
            }
        }

        public bool IsLocked { get; private set; }
        public bool ReceivesDecals { get; private set; }
        public ObjectTransform Transform { get; private set; }
        public Vector3 LocalPivot { get; private set; }
        public Vector3 WorldPivot => Transform.TransformPoint(LocalPivot);
        public IReadOnlyList<MeshPart> Parts => m_Parts;
        public IReadOnlyList<CustomOrientation> CustomOrientations => m_CustomOrientations;

        public bool HasOriginalAppearance
        {
            get
            {
                for (int partIndex = 0; partIndex < m_Parts.Count; partIndex++)
                {
                    IReadOnlyList<SurfaceArea> areas = m_Parts[partIndex].SurfaceAreas;

                    for (int areaIndex = 0; areaIndex < areas.Count; areaIndex++)
                    {
                        if (areas[areaIndex].HasOriginalState)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        // DirtyFlags tell the object renderer what needs to be synchronized
        // GeometryVersion is observed by editing/render caches that need a cheap way to invalidate geometry-derived data.
        public ProceduralObjectDirtyFlags DirtyFlags { get; private set; }
        public uint GeometryVersion { get; private set; }
        public uint PartVersion { get; private set; }

        // Changing a custom orientation does not invalidate geometry or renderer caches.
        public uint CustomOrientationVersion { get; private set; }
        public uint PivotVersion { get; private set; }

        public ProceduralObject(int id, string name, ObjectTransform transform, IEnumerable<MeshPart> parts) : this(id, Guid.NewGuid(), // New POs begin a new persistence lineage
            name, transform, parts, null)
        {
        }

        internal ProceduralObject(int id, Guid persistentId, string name, ObjectTransform transform, IEnumerable<MeshPart> parts) : this(id, persistentId, name, transform, parts, null)
        {
        }

        internal ProceduralObject(int id, Guid persistentId, string name, ObjectTransform transform, IEnumerable<MeshPart> parts, IEnumerable<CustomOrientation> customOrientations, Bounds? initialLocalBounds = null, Vector3? localPivot = null)
        {
            if (persistentId == Guid.Empty)
            {
                throw new ArgumentException("Persistent PO identity cannot be empty.", nameof(persistentId));
            }

            Id = id;
            PersistentId = persistentId;
            if (parts == null)
            {
                throw new ArgumentNullException(nameof(parts));
            }

            List<MeshPart> materializedParts = new List<MeshPart>(parts);
            if (materializedParts.Count == 0 || materializedParts.Count > kMaxPartCount)
            {
                throw new ArgumentException($"A PO must contain between 1 and {kMaxPartCount} mesh parts.", nameof(parts));
            }

            for (int i = 0; i < materializedParts.Count; i++)
            {
                if (materializedParts[i] == null)
                {
                    throw new ArgumentException("A PO mesh part cannot be null.", nameof(parts));
                }
            }

            Name = NormalizeName(name);
            Transform = transform;
            LocalPivot = localPivot ?? Vector3.zero;
            if (!IsFinite(LocalPivot))
            {
                throw new ArgumentException("A PO pivot must be finite.", nameof(localPivot));
            }

            m_Parts = materializedParts;
            m_CustomOrientations = new List<CustomOrientation>(kMaxCustomOrientationCount);

            if (customOrientations != null)
            {
                foreach (CustomOrientation orientation in customOrientations)
                {
                    if (m_CustomOrientations.Count >= kMaxCustomOrientationCount)
                    {
                        throw new ArgumentException($"A PO cannot contain more than {kMaxCustomOrientationCount} custom orientations.", nameof(customOrientations));
                    }

                    if (orientation == null)
                    {
                        throw new ArgumentException("A PO custom orientation cannot be null.", nameof(customOrientations));
                    }

                    m_CustomOrientations.Add(orientation.Clone());
                }
            }

            if (initialLocalBounds.HasValue)
            {
                m_LocalBounds = initialLocalBounds.Value;
                m_HasLocalBounds = true;
                m_LocalBoundsDirty = false;
            }
            else
            {
                RecalculateLocalBounds();
            }

            // A newly constructed PO has no matching runtime renderer yet, including objects reconstructed from saves.
            DirtyFlags = ProceduralObjectDirtyFlags.All;
        }

        public bool Rename(string name)
        {
            string normalized = name == null ? string.Empty : name.Trim();

            if (string.IsNullOrEmpty(normalized) || normalized == Name)
            {
                return false;
            }

            Name = normalized;
            return true;
        }

        public bool TryAddPart(MeshPart part, out int partIndex, out string failureReason)
        {
            if (part == null)
            {
                partIndex = -1;
                failureReason = "The mesh part cannot be null.";
                return false;
            }

            return TryAddParts(new[] { part }, out partIndex, out failureReason);
        }

        public bool TryAddParts(IReadOnlyList<MeshPart> parts, out int firstPartIndex, out string failureReason)
        {
            firstPartIndex = -1;
            failureReason = string.Empty;
            if (!CanEditGeometry(-1, out LocalizedText deformationReason))
            {
                failureReason = deformationReason.ToString();
                return false;
            }

            if (parts == null || parts.Count == 0)
            {
                failureReason = "At least one mesh part is required.";
                return false;
            }

            if (parts.Count > kMaxPartCount - m_Parts.Count)
            {
                failureReason = $"A PO cannot contain more than {kMaxPartCount} mesh parts.";
                return false;
            }

            HashSet<MeshPart> incoming = new HashSet<MeshPart>();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] uniqueNames = new string[parts.Count];

            for (int i = 0; i < m_Parts.Count; i++)
            {
                names.Add(m_Parts[i].Name);
            }

            for (int i = 0; i < parts.Count; i++)
            {
                MeshPart part = parts[i];
                if (part == null)
                {
                    failureReason = "A mesh part cannot be null.";
                    return false;
                }

                if (m_Parts.Contains(part) || !incoming.Add(part))
                {
                    failureReason = "A mesh part instance cannot be added more than once.";
                    return false;
                }

                uniqueNames[i] = GetUniquePartName(part.Name, names);
                names.Add(uniqueNames[i]);
            }

            firstPartIndex = m_Parts.Count;
            for (int i = 0; i < parts.Count; i++)
            {
                parts[i].Rename(uniqueNames[i]);
                m_Parts.Add(parts[i]);
            }

            PartVersion++;
            MarkDirty(ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool TryRemovePart(int partIndex, out MeshPart removedPart, out string failureReason)
        {
            removedPart = null;
            failureReason = string.Empty;
            if (!CanEditGeometry(-1, out LocalizedText deformationReason))
            {
                failureReason = deformationReason.ToString();
                return false;
            }

            if (m_Parts.Count <= 1)
            {
                failureReason = "A PO must retain at least one mesh part.";
                return false;
            }

            if (!TryGetPart(partIndex, out removedPart))
            {
                failureReason = $"Mesh part {partIndex} does not exist.";
                return false;
            }

            m_Parts.RemoveAt(partIndex);
            PartVersion++;
            MarkDirty(ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public PartRenameOutcome RenamePart(int partIndex, string name, out string finalName, out string failureReason)
        {
            finalName = string.Empty;
            failureReason = string.Empty;
            if (!TryGetPart(partIndex, out MeshPart part))
            {
                failureReason = $"Mesh part {partIndex} does not exist.";
                return PartRenameOutcome.Failed;
            }

            finalName = GetUniquePartName(name, partIndex);
            if (!part.Rename(finalName))
            {
                return PartRenameOutcome.Unchanged;
            }

            PartVersion++;
            return PartRenameOutcome.Changed;
        }

        public bool SetLocked(bool locked)
        {
            if (IsLocked == locked)
            {
                return false;
            }

            IsLocked = locked;
            return true;
        }

        public bool SetReceivesDecals(bool receivesDecals)
        {
            if (ReceivesDecals == receivesDecals)
            {
                return false;
            }

            ReceivesDecals = receivesDecals;
            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool TryAddCustomOrientation(string name, Quaternion localRotation, out int orientationIndex)
        {
            orientationIndex = -1;

            if (m_CustomOrientations.Count >= kMaxCustomOrientationCount || !CustomOrientation.TryCreate(name, localRotation, out CustomOrientation orientation))
            {
                return false;
            }

            orientationIndex = m_CustomOrientations.Count;
            m_CustomOrientations.Add(orientation);
            CustomOrientationVersion++;
            return true;
        }

        public bool TryGetCustomOrientation(int orientationIndex, out CustomOrientation orientation)
        {
            if (orientationIndex < 0 || orientationIndex >= m_CustomOrientations.Count)
            {
                orientation = null;
                return false;
            }

            orientation = m_CustomOrientations[orientationIndex];
            return true;
        }

        public bool RenameCustomOrientation(int orientationIndex, string name)
        {
            if (!TryGetCustomOrientation(orientationIndex, out CustomOrientation orientation) || !orientation.TryRename(name))
            {
                return false;
            }

            CustomOrientationVersion++;
            return true;
        }

        public bool RemoveCustomOrientation(int orientationIndex)
        {
            if (orientationIndex < 0 || orientationIndex >= m_CustomOrientations.Count)
            {
                return false;
            }

            m_CustomOrientations.RemoveAt(orientationIndex);
            CustomOrientationVersion++;
            return true;
        }

        public string GetNextCustomOrientationName()
        {
            for (int suffix = 1; suffix <= kMaxCustomOrientationCount; suffix++)
            {
                string candidate = $"Custom {suffix}";
                bool inUse = false;

                for (int i = 0; i < m_CustomOrientations.Count; i++)
                {
                    if (string.Equals(m_CustomOrientations[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        inUse = true;
                        break;
                    }
                }

                if (!inUse)
                {
                    return candidate;
                }
            }

            return $"Custom {m_CustomOrientations.Count + 1}";
        }

        public ProceduralObject Clone(int newId, string newName, Vector3 newPosition)
        {
            List<MeshPart> clonedParts = new List<MeshPart>(m_Parts.Count);

            for (int i = 0; i < m_Parts.Count; i++)
            {
                clonedParts.Add(m_Parts[i].Clone());
            }

            ObjectTransform clonedTransform = Transform;
            clonedTransform.Position = newPosition;

            // A duplicate starts a new persistence lineage but carries the
            // source object's detached orientation definitions with it.
            ProceduralObject clone = new ProceduralObject(newId, Guid.NewGuid(), newName, clonedTransform, clonedParts, m_CustomOrientations, localPivot: LocalPivot);
            clone.SetReceivesDecals(ReceivesDecals);
            clone.SetLodProfile(LodProfile);
            CopyDeformationsTo(clone);
            return clone;
        }

        public void Translate(Vector3 delta)
        {
            SetPosition(Transform.Position + delta);
        }

        public void SetPosition(Vector3 position)
        {
            ObjectTransform transform = Transform;
            transform.Position = position;
            SetTransform(transform);
        }

        public void Rotate(Quaternion delta)
        {
            SetRotation(delta * Transform.Rotation);
        }

        public void SetRotation(Quaternion rotation)
        {
            ObjectTransform transform = Transform;
            transform.Rotation = rotation;
            SetTransform(transform);
        }

        public void SetScale(Vector3 scale)
        {
            ObjectTransform transform = Transform;
            transform.Scale = scale;
            SetTransform(transform);
        }

        internal void SetTransform(ObjectTransform transform)
        {
            Transform = transform;
            MarkDirty(ProceduralObjectDirtyFlags.Transform);
        }

        public bool SetLocalPivot(Vector3 localPivot)
        {
            if (!IsFinite(localPivot) || LocalPivot.Equals(localPivot))
            {
                return false;
            }

            LocalPivot = localPivot;
            PivotVersion++;
            return true;
        }

        public bool TrySetWorldPivot(Vector3 worldPivot)
        {
            if (!IsFinite(worldPivot))
            {
                return false;
            }

            Vector3 localPivot = Transform.InverseTransformPoint(worldPivot);
            return SetLocalPivot(localPivot);
        }

        internal bool TrySetRotationAroundPivot(Quaternion rotation)
        {
            if (!PivotTransformMath.TryCreateTransformKeepingPivot(Transform, LocalPivot, rotation, Transform.Scale, out ObjectTransform result))
            {
                return false;
            }

            SetTransform(result);
            return true;
        }

        internal bool TrySetScaleAroundPivot(Vector3 scale)
        {
            if (!PivotTransformMath.TryCreateTransformKeepingPivot(Transform, LocalPivot, Transform.Rotation, scale, out ObjectTransform result))
            {
                return false;
            }

            SetTransform(result);
            return true;
        }

        public bool SetSurfaceAreaColorMaskMode(int partIndex, int surfaceAreaId, ColorMaskMode mode)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaColorMaskMode(surfaceAreaId, mode))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool SetSurfaceAreaColorMaskOverride(int partIndex, int surfaceAreaId, Color mask1, Color mask2, Color mask3)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaColorMaskOverride(surfaceAreaId, mask1, mask2, mask3))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool RestoreSurfaceAreaOriginalAppearance(int partIndex, int surfaceAreaId)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.RestoreSurfaceAreaOriginalAppearance(surfaceAreaId))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool RestorePartOriginalAppearance(int partIndex)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.RestoreAllSurfaceAreasOriginalAppearance())
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool RestoreOriginalAppearance()
        {
            bool changed = false;
            for (int i = 0; i < m_Parts.Count; i++)
            {
                if (m_Parts[i].RestoreAllSurfaceAreasOriginalAppearance())
                {
                    changed = true;
                }
            }

            if (changed)
            {
                MarkDirty(ProceduralObjectDirtyFlags.Material);
            }

            return changed;
        }

        public bool SetSurfaceAreaMappingMode(int partIndex, int surfaceAreaId, TextureMappingMode mode)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaMappingMode(surfaceAreaId, mode))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool SetSurfaceAreaTextureScale(int partIndex, int surfaceAreaId, Vector2 textureScale)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaTextureScale(surfaceAreaId, textureScale))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool SetSurfaceAreaTextureRotation(int partIndex, int surfaceAreaId, float rotation)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaTextureRotation(surfaceAreaId, rotation))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool SetSurfaceAreaTextureOffset(int partIndex, int surfaceAreaId, Vector2 offset)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaTextureOffset(surfaceAreaId, offset))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool SetSurfaceAreaMetallic(int partIndex, int surfaceAreaId, float value)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaMetallic(surfaceAreaId, value))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool SetSurfaceAreaSmoothness(int partIndex, int surfaceAreaId, float value)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaSmoothness(surfaceAreaId, value))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool SetSurfaceAreaCoatStrength(int partIndex, int surfaceAreaId, float value)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaCoatStrength(surfaceAreaId, value))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool ResetSurfaceAreaMaterialProperties(int partIndex, int surfaceAreaId)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.ResetSurfaceAreaMaterialProperties(surfaceAreaId))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool SetSurfaceAreaSurfaceMaterial(int partIndex, int surfaceAreaId, AssetHash128 surfaceGuid)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaSurfaceMaterial(surfaceAreaId, surfaceGuid))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool SetSurfaceAreaLibraryMaterial(int partIndex, int surfaceAreaId, string definitionId)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaLibraryMaterial(surfaceAreaId, definitionId))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public int ReplaceLibraryMaterialReferencesWithDefault(string definitionId)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                return 0;
            }

            int replacementCount = 0;
            for (int i = 0; i < m_Parts.Count; i++)
            {
                replacementCount += m_Parts[i].ReplaceLibraryMaterialReferencesWithDefault(definitionId);
            }

            if (replacementCount > 0)
            {
                MarkDirty(ProceduralObjectDirtyFlags.Material);
            }

            return replacementCount;
        }

        public int ValidateLibraryReferences()
        {
            int replacementCount = 0;
            for (int i = 0; i < m_Parts.Count; i++)
            {
                replacementCount += m_Parts[i].ValidateLibraryReferences();
            }

            if (replacementCount > 0)
            {
                MarkDirty(ProceduralObjectDirtyFlags.Material);
            }

            return replacementCount;
        }

        public bool SetSurfaceAreaVanillaSpecialMaterial(int partIndex, int surfaceAreaId, VanillaSpecialMaterialKind kind)
        {
            if (!TryGetPart(partIndex, out MeshPart part) || !part.SetSurfaceAreaVanillaSpecialMaterial(surfaceAreaId, kind))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool AssignTrianglesToSurfaceArea(int partIndex, int[] triangleIds, int surfaceAreaId)
        {
            return AssignTrianglesToSurfaceArea(partIndex, triangleIds, surfaceAreaId, out string _);
        }

        public bool AssignTrianglesToSurfaceArea(int partIndex, int[] triangleIds, int surfaceAreaId, out string failureReason)
        {
            failureReason = string.Empty;
            if (!TryGetPart(partIndex, out MeshPart part))
            {
                failureReason = $"Mesh part {partIndex} does not exist.";
                return false;
            }

            if (!part.AssignTrianglesToSurfaceArea(triangleIds, surfaceAreaId, out failureReason))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool TryDeleteSurfaceArea(int partIndex, int surfaceAreaId, out int reassignedTriangleCount, out string failureReason)
        {
            reassignedTriangleCount = 0;
            failureReason = string.Empty;

            if (!TryGetPart(partIndex, out MeshPart part))
            {
                failureReason = $"Mesh part {partIndex} does not exist.";
                return false;
            }

            if (!part.TryDeleteSurfaceArea(surfaceAreaId, out reassignedTriangleCount, out failureReason))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool TryCreateSurfaceAreaFromTriangles(int partIndex, int[] triangleIds, int sourceSurfaceAreaId, out int newSurfaceAreaId)
        {
            return TryCreateSurfaceAreaFromTriangles(partIndex, triangleIds, sourceSurfaceAreaId, out newSurfaceAreaId, out string _);
        }

        public bool TryCreateSurfaceAreaFromTriangles(int partIndex, int[] triangleIds, int sourceSurfaceAreaId, out int newSurfaceAreaId, out string failureReason)
        {
            newSurfaceAreaId = -1;
            failureReason = string.Empty;
            if (!TryGetPart(partIndex, out MeshPart part))
            {
                failureReason = $"Mesh part {partIndex} does not exist.";
                return false;
            }

            if (!part.TryCreateSurfaceAreaFromTriangles(triangleIds, sourceSurfaceAreaId, out newSurfaceAreaId, out failureReason))
            {
                return false;
            }

            MarkDirty(ProceduralObjectDirtyFlags.Material);
            return true;
        }

        public bool TryGetLocalBounds(out Bounds bounds)
        {
            if (m_LocalBoundsDirty)
            {
                RecalculateLocalBounds();
            }

            bounds = m_LocalBounds;
            return m_HasLocalBounds;
        }

        public void RecalculateLocalBounds()
        {
            // Bounds come from source vertices across every part, NOT from renderer bounds.
            // This keeps picking, duplication offsets, and loading sepaerate from unity object availability
            bool hasBounds = false;
            Bounds bounds = default;

            foreach (MeshPart part in m_Parts)
            {
                Vector3[] vertices = part.Mesh.Vertices;

                for (int i = 0; i < vertices.Length; i++)
                {
                    if (!hasBounds)
                    {
                        bounds = new Bounds(vertices[i], Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(vertices[i]);
                    }
                }
            }

            m_LocalBounds = bounds;
            m_HasLocalBounds = hasBounds;
            m_LocalBoundsDirty = false;
        }

        private static string NormalizeName(string name)
        {
            string normalized = name == null ? string.Empty : name.Trim();

            return string.IsNullOrEmpty(normalized) ? "Procedural Object" : normalized;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        public string GetUniquePartName(string name, int excludedPartIndex = -1)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < m_Parts.Count; i++)
            {
                if (i != excludedPartIndex)
                {
                    names.Add(m_Parts[i].Name);
                }
            }

            return GetUniquePartName(name, names);
        }

        internal static string GetUniquePartName(string name, ISet<string> names)
        {
            string normalized = MeshPart.NormalizeName(name);
            if (!names.Contains(normalized))
            {
                return normalized;
            }

            for (int suffix = 2; suffix < int.MaxValue; suffix++)
            {
                string candidate = $"{normalized} {suffix}";
                if (!names.Contains(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Could not generate a unique mesh part name.");
        }

        internal bool TryRollbackAddedParts(int firstPartIndex, IReadOnlyList<MeshPart> expectedParts)
        {
            if (expectedParts == null || expectedParts.Count == 0 || firstPartIndex < 1 || firstPartIndex + expectedParts.Count != m_Parts.Count)
            {
                return false;
            }

            for (int i = 0; i < expectedParts.Count; i++)
            {
                if (!ReferenceEquals(m_Parts[firstPartIndex + i], expectedParts[i]))
                {
                    return false;
                }
            }

            m_Parts.RemoveRange(firstPartIndex, expectedParts.Count);
            PartVersion++;
            MarkDirty(ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
            return true;
        }

        private bool TryGetPart(int partIndex, out MeshPart part)
        {
            if (partIndex >= 0 && partIndex < m_Parts.Count)
            {
                part = m_Parts[partIndex];
                return true;
            }

            part = null;
            return false;
        }

        public void MarkDirty(ProceduralObjectDirtyFlags flags)
        {
            // Topology changes always change the mesh layout even if callers only name the more specific category.
            // Propagating Geometry here keeps it centralized isntead of relying on every editor.
            if ((flags & ProceduralObjectDirtyFlags.Topology) != 0)
            {
                flags |= ProceduralObjectDirtyFlags.Geometry;
            }

            if ((flags & ProceduralObjectDirtyFlags.Geometry) != 0)
            {
                // Bounds and projection/visibility caches are derived from geometry
                // Version rollover is fine ~ we're looking for change, not any particular order
                GeometryVersion++;
                m_LocalBoundsDirty = true;
            }

            DirtyFlags |= flags;
        }

        internal bool TryMarkPartGeometryPreviewChanged(int partIndex, MeshPart expectedPart, bool positionsOnly = false)
        {
            if (!IsPartAt(partIndex, expectedPart))
            {
                return false;
            }

            // Live part drags need the renderer's fast stream refresh, but the
            // aggregate bounds and snap index still describe the start state
            // until the drag commits.
            if (positionsOnly)
            {
                expectedPart.MarkPositionsChanged();
            }
            else
            {
                expectedPart.MarkGeometryChanged();
            }

            DirtyFlags |= ProceduralObjectDirtyFlags.Geometry;
            return true;
        }

        internal bool TryCommitPartGeometryChange(int partIndex, MeshPart expectedPart)
        {
            if (!IsPartAt(partIndex, expectedPart))
            {
                return false;
            }

            // The last preview already advanced the part stream version.
            // Commit only advances the object-level geometry once so bounds
            // and the persistent snap index rebuild from the finished mesh.
            MarkDirty(ProceduralObjectDirtyFlags.Geometry);
            return true;
        }

        internal bool TryMarkPartGeometryDirectionsChanged(int partIndex, MeshPart expectedPart)
        {
            if (!IsPartAt(partIndex, expectedPart))
            {
                return false;
            }

            expectedPart.MarkDirectionsChanged();
            DirtyFlags |= ProceduralObjectDirtyFlags.Geometry;
            return true;
        }

        internal bool TryMarkGeometryPreviewChanged(IReadOnlyList<MeshPart> expectedParts, bool positionsOnly)
        {
            if (!PartsMatch(expectedParts))
            {
                return false;
            }

            for (int partIndex = 0; partIndex < expectedParts.Count; partIndex++)
            {
                if (positionsOnly)
                {
                    expectedParts[partIndex].MarkPositionsChanged();
                }
                else
                {
                    expectedParts[partIndex].MarkGeometryChanged();
                }
            }

            DirtyFlags |= ProceduralObjectDirtyFlags.Geometry;
            return true;
        }

        internal bool TryMarkGeometryDirectionsChanged(IReadOnlyList<MeshPart> expectedParts)
        {
            if (!PartsMatch(expectedParts))
            {
                return false;
            }

            for (int partIndex = 0; partIndex < expectedParts.Count; partIndex++)
            {
                expectedParts[partIndex].MarkDirectionsChanged();
            }

            DirtyFlags |= ProceduralObjectDirtyFlags.Geometry;
            return true;
        }

        internal bool TryCommitGeometryChange(IReadOnlyList<MeshPart> expectedParts)
        {
            if (!PartsMatch(expectedParts))
            {
                return false;
            }

            // All part streams already carry their final versions. Advance
            // aggregate geometry once so bounds and snap caches rebuild once.
            MarkDirty(ProceduralObjectDirtyFlags.Geometry);
            return true;
        }

        private bool PartsMatch(IReadOnlyList<MeshPart> expectedParts)
        {
            if (expectedParts == null || expectedParts.Count != m_Parts.Count)
            {
                return false;
            }

            for (int partIndex = 0; partIndex < expectedParts.Count; partIndex++)
            {
                if (!ReferenceEquals(m_Parts[partIndex], expectedParts[partIndex]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsPartAt(int partIndex, MeshPart expectedPart)
        {
            return expectedPart != null && partIndex >= 0 && partIndex < m_Parts.Count && ReferenceEquals(m_Parts[partIndex], expectedPart);
        }

        public void ClearDirty(ProceduralObjectDirtyFlags flags)
        {
            DirtyFlags &= ~flags;
        }
    }
}