using MeshData = PO2.Geometry.MeshData;
using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Game.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Picking;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Rendering.Mapping;
using AssetHash128 = Colossal.Hash128;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private sealed class RenderInstance
        {
            public int ObjectId;
            public ProceduralObject Source;
            public GameObject Root;
            public readonly List<RenderPart> Parts = new List<RenderPart>();
            public bool Hovered;
            public bool DeleteHovered;
        }

        private abstract class MaterialVariantEntry
        {
            public string BaseName;
            public Material DecalsDisabledMaterial;
            public Material DecalsEnabledMaterial;
            public int DecalsDisabledRefCount;
            public int DecalsEnabledRefCount;

            public int TotalRefCount =>
                DecalsDisabledRefCount + DecalsEnabledRefCount;

            public Material GetMaterial(bool receivesDecals)
            {
                return receivesDecals ? DecalsEnabledMaterial : DecalsDisabledMaterial;
            }

            public Material GetAnyMaterial()
            {
                return DecalsDisabledMaterial != null ? DecalsDisabledMaterial : DecalsEnabledMaterial;
            }

            public void SetMaterial(bool receivesDecals, Material material)
            {
                if (receivesDecals)
                {
                    DecalsEnabledMaterial = material;
                }
                else
                {
                    DecalsDisabledMaterial = material;
                }
            }

            public void AddReference(bool receivesDecals)
            {
                if (receivesDecals)
                {
                    DecalsEnabledRefCount++;
                }
                else
                {
                    DecalsDisabledRefCount++;
                }
            }

            public bool ReleaseReference(Material material, bool receivesDecals)
            {
                if (GetMaterial(receivesDecals) != material)
                {
                    return false;
                }

                if (receivesDecals)
                {
                    DecalsEnabledRefCount = Mathf.Max(0, DecalsEnabledRefCount - 1);
                }
                else
                {
                    DecalsDisabledRefCount = Mathf.Max(0, DecalsDisabledRefCount - 1);
                }

                return true;
            }
        }

        private sealed class SurfaceMaterialEntry : MaterialVariantEntry
        {
            public SurfaceAsset Surface;
        }

        private sealed class SpecialMaterialEntry : MaterialVariantEntry
        {
        }

        private sealed class LibraryMaterialEntry : MaterialVariantEntry
        {
        }

        private sealed class TextureResourceEntry
        {
            public Texture2D Texture;
            public int RefCount;
        }

        private sealed class RenderPart
        {
            public bool DeleteHovered;
            public MeshPart PickSource;
            public RenderGeometryState GeometryUpdates;
            public readonly ObjectPickCacheState PickCache = new ObjectPickCacheState();
            public GameObject GameObject;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public bool ReceivesDecals;
            public uint GeometryVersion;
            public uint PositionVersion;
            public uint FullGeometryVersion;
            public uint DirectionVersion;
            public uint TopologyVersion;
            public uint SurfaceAreaMembershipVersion;
            public Vector3 MappingScale;
            public Vector3[] CombinedVertices = Array.Empty<Vector3>();
            public Vector3[] CombinedNormals = Array.Empty<Vector3>();
            public Vector4[] CombinedUVs = Array.Empty<Vector4>();
            public Vector4[] CombinedTangents = Array.Empty<Vector4>();
            public Color32[] CombinedColors = Array.Empty<Color32>();
            public Vector4[] CombinedTexCoord1 = Array.Empty<Vector4>();
            public Vector4[] CombinedTexCoord3 = Array.Empty<Vector4>();
            public int CombinedUV0Dimension = 2;
            public bool HasTangentStream;
            public bool UsesSourceTangents;
            public bool HasMainRendererGeometry;
            public readonly List<RenderArea> Areas = new List<RenderArea>();
        }

        private sealed class SurfaceAreaMeshBuildResult
        {
            public MeshData Mesh;
            public int[] SourceVertexIndices;
        }

        private sealed class RenderArea
        {
            public int SurfaceAreaId;
            public int MaterialIndex;
            public Material Material;
            public bool ReceivesDecals;
            public uint MaterialSourceVersion;
            public uint MaterialPropertiesVersion;
            public uint MappingVersion;
            public uint ColorMaskVersion;
            public ColorMaskMode ColorMaskMode;
            public bool HasSourceColorMask;
            public Color SourceColorMask1;
            public Color SourceColorMask2;
            public Color SourceColorMask3;
            public Color OverrideColorMask1;
            public Color OverrideColorMask2;
            public Color OverrideColorMask3;
            public MaterialSourceKind MaterialSourceKind;
            public string DefinitionId;
            public AssetHash128 SurfaceGuid;
            public VanillaSpecialMaterialKind VanillaSpecialKind;
            public AssetHash128 ResolvedSurfaceGuid;
            public float Metallic;
            public float Smoothness;
            public float CoatStrength;
            public TextureMappingMode MappingMode;
            public MeshData AreaMesh;
            public int[] SourceVertexIndices;
            public MeshTopology AreaTopology;
            public GeneratedMapping GeneratedMapping;
            public int CombinedVertexOffset;
            public int CombinedVertexCount;
            public bool IsolatedRenderer;
            public bool UsesSourceTangents;
            public int UV0Dimension = 2;
            public GameObject IsolatedGameObject;
            public Mesh IsolatedMesh;
            public MeshRenderer IsolatedRendererComponent;
            public MaterialPropertyBlock PropertyBlock;
        }
    }
}