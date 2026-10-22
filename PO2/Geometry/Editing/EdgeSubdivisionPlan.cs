using PO2.Core.Operations;
using PO2.Geometry.Topology;
using PO2.Persistence.Model;

namespace PO2.Geometry.Editing
{
    internal readonly struct SubdivisionEdge
    {
        internal readonly int Id, A, B;

        internal SubdivisionEdge(int id, int a, int b)
        {
            Id = id;
            A = a;
            B = b;
        }

        internal ulong Key => TopologyEditIndex.Key(A, B);
    }

    internal sealed class EdgeSubdivisionPlan
    {
        internal sealed class Region
        {
            internal int FaceId, TriangleA, TriangleB, FirstTriangle, Area, Mask;
            internal int SecondFaceId = -1;
            internal int[] Corners;
            internal bool QuadTemplate;
            internal int OutputStart, OutputCount, FaceStart, FaceCount;
            internal Region CopyForBuild() => (Region)MemberwiseClone();
        }

        internal readonly TopologyEditIndex Index;
        internal readonly SubdivisionEdge[] Edges;
        internal readonly Region[] Regions;
        internal readonly int AddedRenderVertexCount, ResultFaceCount, ResultTriangleCount;
        internal readonly TopologySize Size;
        internal PersistencePartData Source => Index.Source;
        internal bool Converted => Source.StructuralTopologySource != PO2.Geometry.Topology.StructuralTopologySource.Authored;

        internal EdgeSubdivisionPlan(TopologyEditIndex index, SubdivisionEdge[] edges, Region[] regions, int addedVertices, int centers, int faces, int triangles)
        {
            Index = index;
            Edges = edges;
            Regions = regions;
            AddedRenderVertexCount = addedVertices;
            ResultFaceCount = faces;
            ResultTriangleCount = triangles;
            Size = TopologySize.ForOutput(Source, checked(Source.Vertices.Length + addedVertices), checked(Source.LogicalVertexCount + edges.Length + centers), checked(triangles * 3), faces);
        }
    }

    internal readonly struct SubdivisionTriangleSource
    {
        internal readonly int Triangle, QuadTriangle;

        internal SubdivisionTriangleSource(int triangle, int quadTriangle = -1)
        {
            Triangle = triangle;
            QuadTriangle = quadTriangle;
        }
    }

    internal readonly struct SubdividedEdge
    {
        internal readonly int LogicalA, LogicalB, LogicalMidpoint;

        internal SubdividedEdge(int a, int b, int midpoint)
        {
            LogicalA = a;
            LogicalB = b;
            LogicalMidpoint = midpoint;
        }
    }
}