using System;
using UnityEngine;
using PO2.Geometry;

namespace PO2.Core.Operations
{
    // Exact start streams and reusable preview buffers for one baked part transform.
    // The topology has to remain unchanged for the snapshot's whole lifetime.
    internal sealed class PartGeometrySnapshot
    {
        private readonly MeshPart m_Part;
        private readonly uint m_TopologyVersion;
        private readonly bool m_PositionsOnly;

        private readonly Vector3[] m_Positions;
        private readonly Vector3[] m_Normals;
        private readonly Vector4[] m_Tangents;

        private readonly Vector3[] m_WorkPositions;
        private readonly Vector3[] m_WorkNormals;
        private readonly Vector4[] m_WorkTangents;

        internal MeshPart Part => m_Part;
        internal Vector3[] Positions => m_Positions;
        internal Vector3[] Normals => m_Normals;
        internal Vector4[] Tangents => m_Tangents;
        internal Vector3[] WorkPositions => m_WorkPositions;
        internal Vector3[] WorkNormals => m_WorkNormals;
        internal Vector4[] WorkTangents => m_WorkTangents;
        internal bool PositionsOnly => m_PositionsOnly;

        private PartGeometrySnapshot(MeshPart part, Vector3[] positions, Vector3[] normals, Vector4[] tangents, bool positionsOnly)
        {
            m_Part = part;
            m_TopologyVersion = part.TopologyVersion;
            m_PositionsOnly = positionsOnly;

            m_Positions = positions;
            m_Normals = normals;
            m_Tangents = tangents;

            m_WorkPositions = new Vector3[positions.Length];
            m_WorkNormals = normals != null ? new Vector3[normals.Length] : null;
            m_WorkTangents = tangents != null ? new Vector4[tangents.Length] : null;
        }

        internal static bool TryCapture(MeshPart part, bool positionsOnly, out PartGeometrySnapshot snapshot, out string failureReason)
        {
            snapshot = null;
            failureReason = string.Empty;

            if (part == null)
            {
                failureReason = "The mesh part is null.";
                return false;
            }

            MeshData mesh = part.Mesh;
            if (mesh == null || mesh.Vertices == null)
            {
                failureReason = "The mesh part has no vertex stream.";
                return false;
            }

            if ((mesh.Normals != null && mesh.Normals.Length != mesh.Vertices.Length) || (mesh.Tangents != null && mesh.Tangents.Length != mesh.Vertices.Length))
            {
                failureReason = "The mesh part has mismatched transform streams.";
                return false;
            }

            snapshot = new PartGeometrySnapshot(part, (Vector3[])mesh.Vertices.Clone(), !positionsOnly && mesh.Normals != null ? (Vector3[])mesh.Normals.Clone() : null, !positionsOnly && mesh.Tangents != null ? (Vector4[])mesh.Tangents.Clone() : null, positionsOnly);

            return true;
        }

        internal bool MatchesDestination(MeshPart part, out string failureReason)
        {
            failureReason = string.Empty;

            if (!ReferenceEquals(m_Part, part))
            {
                failureReason = "The transform snapshot belongs to a different mesh part.";
                return false;
            }

            if (part.TopologyVersion != m_TopologyVersion)
            {
                failureReason = "The mesh part topology changed during the transform.";
                return false;
            }

            MeshData mesh = part.Mesh;
            if (mesh == null || mesh.Vertices == null || mesh.Vertices.Length != m_Positions.Length || (!m_PositionsOnly && (!StreamsMatch(mesh.Normals, m_Normals) || !StreamsMatch(mesh.Tangents, m_Tangents))))
            {
                failureReason = "The mesh part streams changed during the transform.";
                return false;
            }

            return true;
        }

        internal bool TryPublishWorkStreams(MeshPart part, out string failureReason)
        {
            if (!MatchesDestination(part, out failureReason))
            {
                return false;
            }

            Array.Copy(m_WorkPositions, part.Mesh.Vertices, m_WorkPositions.Length);

            if (m_WorkNormals != null)
            {
                Array.Copy(m_WorkNormals, part.Mesh.Normals, m_WorkNormals.Length);
            }

            if (m_WorkTangents != null)
            {
                Array.Copy(m_WorkTangents, part.Mesh.Tangents, m_WorkTangents.Length);
            }

            return true;
        }

        internal bool TryPublishWorkPositions(MeshPart part, out string failureReason)
        {
            if (!MatchesDestination(part, out failureReason))
            {
                return false;
            }

            Array.Copy(m_WorkPositions, part.Mesh.Vertices, m_WorkPositions.Length);

            return true;
        }


        internal bool TryAdoptWorkStreams(MeshPart part, out string failureReason)
        {
            if (!MatchesDestination(part, out failureReason))
            {
                return false;
            }

            try
            {
                part.Mesh.ReplaceDeformationStreams(m_WorkPositions, m_WorkNormals, m_WorkTangents);
                return true;
            }
            catch (Exception exception)
            {
                failureReason = "The completed deformation streams could not be adopted: " + exception.Message;
                return false;
            }
        }

        internal bool TryRestore(MeshPart part, out string failureReason)
        {
            if (!MatchesDestination(part, out failureReason))
            {
                return false;
            }

            Array.Copy(m_Positions, part.Mesh.Vertices, m_Positions.Length);

            if (m_Normals != null)
            {
                Array.Copy(m_Normals, part.Mesh.Normals, m_Normals.Length);
            }

            if (m_Tangents != null)
            {
                Array.Copy(m_Tangents, part.Mesh.Tangents, m_Tangents.Length);
            }

            return true;
        }

        private static bool StreamsMatch<T>(T[] current, T[] captured)
        {
            if (current == null || captured == null)
            {
                return current == null && captured == null;
            }

            return current.Length == captured.Length;
        }
    }
}