using System;
using UnityEngine;
using PO2.Tools.Gizmos;

namespace PO2.Rendering
{
    internal sealed class DeformationRendererAreaWork
    {
        internal readonly int SurfaceAreaId;
        internal readonly int[] SourceVertexIndices;
        internal readonly bool Isolated;
        internal readonly int CombinedVertexOffset;
        internal readonly int CombinedVertexCount;

        internal DeformationRendererAreaWork(int surfaceAreaId, int[] sourceVertexIndices, bool isolated, int combinedVertexOffset, int combinedVertexCount)
        {
            SurfaceAreaId = surfaceAreaId;
            SourceVertexIndices = sourceVertexIndices;
            Isolated = isolated;
            CombinedVertexOffset = combinedVertexOffset;
            CombinedVertexCount = combinedVertexCount;
        }
    }

    internal sealed class DeformationRendererPartWork
    {
        internal readonly int PartIndex;
        internal readonly uint TopologyVersion;
        internal readonly uint SurfaceAreaMembershipVersion;
        internal readonly uint PositionVersion;
        internal readonly int CombinedVertexCount;
        internal readonly bool UsesSourceTangents;
        internal readonly DeformationRendererAreaWork[] Areas;

        internal DeformationRendererPartWork(int partIndex, uint topologyVersion, uint surfaceAreaMembershipVersion, uint positionVersion, int combinedVertexCount, bool usesSourceTangents, DeformationRendererAreaWork[] areas)
        {
            PartIndex = partIndex;
            TopologyVersion = topologyVersion;
            SurfaceAreaMembershipVersion = surfaceAreaMembershipVersion;
            PositionVersion = positionVersion;
            CombinedVertexCount = combinedVertexCount;
            UsesSourceTangents = usesSourceTangents;
            Areas = areas;
        }
    }

    internal sealed class DeformationRendererWorkItem
    {
        internal readonly int ObjectId;
        internal readonly DeformationRendererPartWork[] Parts;

        internal DeformationRendererWorkItem(int objectId, DeformationRendererPartWork[] parts)
        {
            ObjectId = objectId;
            Parts = parts;
        }

        internal DeformationRendererPreparation Build(CageDeformationPartWorkItem[] partStreams)
        {
            if (Parts == null || partStreams == null || partStreams.Length != Parts.Length)
            {
                return DeformationRendererPreparation.Failed(ObjectId, "Renderer preparation inputs do not match.");
            }

            try
            {
                var results = new DeformationRendererPartPreparation[Parts.Length];

                for (int i = 0; i < Parts.Length; i++)
                {
                    DeformationRendererPartWork partWork = Parts[i];
                    if (partWork == null)
                    {
                        continue;
                    }

                    if (partStreams[i] == null || partStreams[i].PartIndex != partWork.PartIndex)
                    {
                        return DeformationRendererPreparation.Failed(ObjectId, "Renderer preparation part ordering changed.");
                    }

                    results[i] = BuildPart(partWork, partStreams[i]);
                }

                return DeformationRendererPreparation.Passed(ObjectId, results);
            }
            catch (Exception exception)
            {
                return DeformationRendererPreparation.Failed(ObjectId, "Renderer preparation failed: " + exception.Message);
            }
        }

        private static DeformationRendererPartPreparation BuildPart(DeformationRendererPartWork work, CageDeformationPartWorkItem streams)
        {
            if (streams == null)
            {
                throw new InvalidOperationException("A renderer preparation stream set is missing.");
            }

            Vector3[] sourcePositions = streams.FinalPositions;
            Vector3[] sourceNormals = streams.FinalNormals;
            Vector4[] sourceTangents = streams.FinalTangents;
            var areas = new DeformationRendererAreaPreparation[work.Areas.Length];
            var combinedNormals = sourceNormals != null ? new Vector3[work.CombinedVertexCount] : null;
            var combinedTangents = work.UsesSourceTangents && sourceTangents != null ? new Vector4[work.CombinedVertexCount] : null;

            for (int areaIndex = 0; areaIndex < work.Areas.Length; areaIndex++)
            {
                DeformationRendererAreaWork areaWork = work.Areas[areaIndex];
                int count = areaWork.SourceVertexIndices.Length;
                var normals = sourceNormals != null ? new Vector3[count] : null;
                var tangents = sourceTangents != null ? new Vector4[count] : null;

                for (int vertexIndex = 0; vertexIndex < count; vertexIndex++)
                {
                    int sourceIndex = areaWork.SourceVertexIndices[vertexIndex];
                    if (sourceIndex < 0 || sourceIndex >= sourcePositions.Length)
                    {
                        throw new InvalidOperationException("A renderer source vertex index is invalid.");
                    }

                    if (normals != null)
                    {
                        normals[vertexIndex] = sourceNormals[sourceIndex];
                    }

                    if (tangents != null)
                    {
                        tangents[vertexIndex] = sourceTangents[sourceIndex];
                    }

                    if (areaWork.Isolated)
                    {
                        continue;
                    }

                    int combinedIndex = areaWork.CombinedVertexOffset + vertexIndex;
                    if (vertexIndex >= areaWork.CombinedVertexCount || combinedIndex < 0 || combinedIndex >= work.CombinedVertexCount)
                    {
                        throw new InvalidOperationException("A combined renderer vertex range is invalid.");
                    }

                    if (combinedNormals != null)
                    {
                        combinedNormals[combinedIndex] = normals[vertexIndex];
                    }

                    if (combinedTangents != null)
                    {
                        combinedTangents[combinedIndex] = tangents[vertexIndex];
                    }
                }

                areas[areaIndex] = new DeformationRendererAreaPreparation(areaWork.SurfaceAreaId, areaWork.SourceVertexIndices, areaWork.Isolated, normals, tangents);
            }

            return new DeformationRendererPartPreparation(work.PartIndex, work.TopologyVersion, work.SurfaceAreaMembershipVersion, work.PositionVersion, combinedNormals, combinedTangents, areas);
        }
    }

    internal sealed class DeformationRendererPreparation
    {
        internal readonly int ObjectId;
        internal readonly bool Success;
        internal readonly string FailureReason;
        internal readonly DeformationRendererPartPreparation[] Parts;

        private DeformationRendererPreparation(int objectId, bool success, string failureReason, DeformationRendererPartPreparation[] parts)
        {
            ObjectId = objectId;
            Success = success;
            FailureReason = failureReason ?? string.Empty;
            Parts = parts;
        }

        internal static DeformationRendererPreparation Passed(int objectId, DeformationRendererPartPreparation[] parts)
        {
            return new DeformationRendererPreparation(objectId, true, string.Empty, parts);
        }

        internal static DeformationRendererPreparation Failed(int objectId, string failureReason)
        {
            return new DeformationRendererPreparation(objectId, false, failureReason, null);
        }
    }

    internal sealed class DeformationRendererPartPreparation
    {
        internal readonly int PartIndex;
        internal readonly uint TopologyVersion;
        internal readonly uint SurfaceAreaMembershipVersion;
        internal readonly uint PositionVersion;
        internal readonly Vector3[] CombinedNormals;
        internal readonly Vector4[] CombinedTangents;
        internal readonly DeformationRendererAreaPreparation[] Areas;

        internal DeformationRendererPartPreparation(int partIndex, uint topologyVersion, uint surfaceAreaMembershipVersion, uint positionVersion, Vector3[] combinedNormals, Vector4[] combinedTangents, DeformationRendererAreaPreparation[] areas)
        {
            PartIndex = partIndex;
            TopologyVersion = topologyVersion;
            SurfaceAreaMembershipVersion = surfaceAreaMembershipVersion;
            PositionVersion = positionVersion;
            CombinedNormals = combinedNormals;
            CombinedTangents = combinedTangents;
            Areas = areas;
        }
    }

    internal sealed class DeformationRendererAreaPreparation
    {
        internal readonly int SurfaceAreaId;
        internal readonly int[] SourceVertexIndices;
        internal readonly bool Isolated;
        internal readonly Vector3[] Normals;
        internal readonly Vector4[] Tangents;

        internal DeformationRendererAreaPreparation(int surfaceAreaId, int[] sourceVertexIndices, bool isolated, Vector3[] normals, Vector4[] tangents)
        {
            SurfaceAreaId = surfaceAreaId;
            SourceVertexIndices = sourceVertexIndices;
            Isolated = isolated;
            Normals = normals;
            Tangents = tangents;
        }
    }
}