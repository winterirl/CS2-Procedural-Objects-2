using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PO2.Core;
using PO2.Geometry;
using PO2.Materials;
using PO2.Rendering;
using PO2.Rendering.Mapping;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        internal bool TryCaptureDeformationWork(ProceduralObject proceduralObject, int[] partIndices, out DeformationRendererWorkItem workItem)
        {
            workItem = null;

            if (proceduralObject == null || partIndices == null || partIndices.Length == 0 || !m_Instances.TryGetValue(proceduralObject.Id, out RenderInstance instance) || instance.Parts.Count != proceduralObject.Parts.Count)
            {
                return false;
            }

            var parts = new DeformationRendererPartWork[partIndices.Length];

            for (int targetIndex = 0; targetIndex < partIndices.Length; targetIndex++)
            {
                int partIndex = partIndices[targetIndex];
                if (partIndex < 0 || partIndex >= proceduralObject.Parts.Count)
                {
                    return false;
                }

                MeshPart sourcePart = proceduralObject.Parts[partIndex];
                RenderPart renderPart = instance.Parts[partIndex];

                // Generated mapping depends on geometry and keeps the safe
                // ordinary renderer rebuild. Original mapping can prepare its
                // source-index expansion entirely on the worker.
                bool supported = sourcePart.Mesh.Normals != null && renderPart.CombinedNormals.Length == renderPart.CombinedVertices.Length;
                for (int areaIndex = 0; supported && areaIndex < renderPart.Areas.Count; areaIndex++)
                {
                    RenderArea area = renderPart.Areas[areaIndex];
                    supported = area.GeneratedMapping == null && area.AreaMesh != null && area.SourceVertexIndices != null && area.SourceVertexIndices.Length == area.AreaMesh.Vertices.Length;
                }

                if (!supported)
                {
                    parts[targetIndex] = null;
                    continue;
                }

                var areas = new DeformationRendererAreaWork[renderPart.Areas.Count];
                for (int areaIndex = 0; areaIndex < renderPart.Areas.Count; areaIndex++)
                {
                    RenderArea area = renderPart.Areas[areaIndex];
                    areas[areaIndex] = new DeformationRendererAreaWork(area.SurfaceAreaId, area.SourceVertexIndices, area.IsolatedRenderer, area.CombinedVertexOffset, area.CombinedVertexCount);
                }

                parts[targetIndex] = new DeformationRendererPartWork(partIndex, sourcePart.TopologyVersion, sourcePart.SurfaceAreaMembershipVersion, sourcePart.PositionVersion, renderPart.CombinedVertices.Length, renderPart.UsesSourceTangents, areas);
            }

            workItem = new DeformationRendererWorkItem(proceduralObject.Id, parts);
            return true;
        }

        internal bool TryPublishDeformationPreparation(ProceduralObject proceduralObject, DeformationRendererPreparation preparation, out string failureReason)
        {
            failureReason = string.Empty;

            if (proceduralObject == null || preparation == null || !preparation.Success || preparation.ObjectId != proceduralObject.Id || preparation.Parts == null || !m_Instances.TryGetValue(proceduralObject.Id, out RenderInstance instance) || instance.Parts.Count != proceduralObject.Parts.Count)
            {
                failureReason = preparation?.FailureReason ?? "No renderer preparation is available.";
                return false;
            }

            for (int i = 0; i < preparation.Parts.Length; i++)
            {
                DeformationRendererPartPreparation partPreparation = preparation.Parts[i];
                if (partPreparation == null)
                {
                    continue;
                }

                if (!ValidateDeformationPreparation(proceduralObject, instance, partPreparation, out failureReason))
                {
                    return false;
                }
            }

            bool publishedAny = false;
            for (int i = 0; i < preparation.Parts.Length; i++)
            {
                DeformationRendererPartPreparation partPreparation = preparation.Parts[i];
                if (partPreparation == null)
                {
                    continue;
                }

                PublishDeformationPreparation(proceduralObject, instance, partPreparation);
                publishedAny = true;
            }

            if (!publishedAny)
            {
                failureReason = "The target uses geometry-derived texture mapping.";
            }

            return publishedAny;
        }

        private static bool ValidateDeformationPreparation(ProceduralObject proceduralObject, RenderInstance instance, DeformationRendererPartPreparation preparation, out string failureReason)
        {
            failureReason = string.Empty;
            int partIndex = preparation.PartIndex;
            if (partIndex < 0 || partIndex >= proceduralObject.Parts.Count)
            {
                failureReason = "The prepared renderer part no longer exists.";
                return false;
            }

            MeshPart sourcePart = proceduralObject.Parts[partIndex];
            RenderPart renderPart = instance.Parts[partIndex];
            if (sourcePart.TopologyVersion != preparation.TopologyVersion || sourcePart.SurfaceAreaMembershipVersion != preparation.SurfaceAreaMembershipVersion || sourcePart.PositionVersion != preparation.PositionVersion || renderPart.PositionVersion != preparation.PositionVersion || preparation.Areas == null || preparation.Areas.Length != renderPart.Areas.Count || preparation.CombinedNormals == null || preparation.CombinedNormals.Length != renderPart.CombinedNormals.Length || renderPart.UsesSourceTangents != (preparation.CombinedTangents != null) || (preparation.CombinedTangents != null && preparation.CombinedTangents.Length != renderPart.CombinedVertices.Length))
            {
                failureReason = "The prepared renderer layout no longer matches.";
                return false;
            }

            for (int areaIndex = 0; areaIndex < preparation.Areas.Length; areaIndex++)
            {
                DeformationRendererAreaPreparation areaPreparation = preparation.Areas[areaIndex];
                RenderArea renderArea = renderPart.Areas[areaIndex];
                if (areaPreparation == null || areaPreparation.SurfaceAreaId != renderArea.SurfaceAreaId || areaPreparation.Isolated != renderArea.IsolatedRenderer || !ReferenceEquals(areaPreparation.SourceVertexIndices, renderArea.SourceVertexIndices) || renderArea.GeneratedMapping != null || renderArea.AreaMesh == null || (renderArea.IsolatedRenderer && renderArea.IsolatedMesh == null) || (areaPreparation.Normals == null) != (renderArea.AreaMesh.Normals == null) || (areaPreparation.Normals != null && areaPreparation.Normals.Length != renderArea.AreaMesh.Vertices.Length) || (areaPreparation.Tangents == null) != (renderArea.AreaMesh.Tangents == null) || (areaPreparation.Tangents != null && areaPreparation.Tangents.Length != renderArea.AreaMesh.Vertices.Length))
                {
                    failureReason = "A prepared renderer Surface Area no longer matches.";
                    return false;
                }
            }

            return true;
        }

        private void PublishDeformationPreparation(ProceduralObject proceduralObject, RenderInstance instance, DeformationRendererPartPreparation preparation)
        {
            MeshPart sourcePart = proceduralObject.Parts[preparation.PartIndex];
            RenderPart renderPart = instance.Parts[preparation.PartIndex];

            for (int areaIndex = 0; areaIndex < preparation.Areas.Length; areaIndex++)
            {
                DeformationRendererAreaPreparation areaPreparation = preparation.Areas[areaIndex];
                RenderArea renderArea = renderPart.Areas[areaIndex];
                renderArea.AreaMesh.ReplaceDirectionStreams(areaPreparation.Normals, areaPreparation.Tangents);

                if (!renderArea.IsolatedRenderer)
                {
                    continue;
                }

                Mesh mesh = renderArea.IsolatedMesh;
                SetDirectionStream(mesh, areaPreparation.Normals, VertexAttribute.Normal, RenderVertexStreams.Role.Normal);

                if (sourcePart.TryGetSurfaceArea(renderArea.SurfaceAreaId, out SurfaceArea sourceArea) && RequiresTangentSpace(sourceArea.Material) && renderArea.AreaMesh.Vertices.Length > 0)
                {
                    if (renderArea.UsesSourceTangents && areaPreparation.Tangents != null)
                    {
                        SetDirectionStream(mesh, areaPreparation.Tangents, VertexAttribute.Tangent, RenderVertexStreams.Role.Tangent);
                    }
                    else
                    {
                        RenderVertexStreams.RecalculateTangents(mesh);
                        mesh.MarkModified();
                    }
                }
            }

            renderPart.CombinedNormals = preparation.CombinedNormals;
            if (preparation.CombinedTangents != null)
            {
                renderPart.CombinedTangents = preparation.CombinedTangents;
            }

            if (renderPart.CombinedVertices.Length > 0)
            {
                SetDirectionStream(renderPart.Mesh, preparation.CombinedNormals, VertexAttribute.Normal, RenderVertexStreams.Role.Normal);

                if (renderPart.HasTangentStream)
                {
                    if (preparation.CombinedTangents != null)
                    {
                        SetDirectionStream(renderPart.Mesh, preparation.CombinedTangents, VertexAttribute.Tangent, RenderVertexStreams.Role.Tangent);
                    }
                    else
                    {
                        RenderVertexStreams.RecalculateTangents(renderPart.Mesh);
                        renderPart.Mesh.MarkModified();
                        renderPart.CombinedTangents = renderPart.Mesh.tangents;
                    }
                }
            }

            renderPart.GeometryVersion = sourcePart.GeometryVersion;
            renderPart.PositionVersion = sourcePart.PositionVersion;
            renderPart.FullGeometryVersion = sourcePart.FullGeometryVersion;
            renderPart.DirectionVersion = sourcePart.DirectionVersion;
            renderPart.TopologyVersion = sourcePart.TopologyVersion;
            renderPart.SurfaceAreaMembershipVersion = sourcePart.SurfaceAreaMembershipVersion;
            PrepareGeometryUpdates(renderPart, sourcePart);
        }
    }
}