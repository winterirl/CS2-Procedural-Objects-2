using System;

namespace PO2.Geometry.Topology
{
    // Exact render triangles owned by one structural face.
    // Triangle IDs do not need to be consecutive in the mesh index stream.
    public readonly struct StructuralFaceTriangles
    {
        public int TriangleA { get; }
        public int TriangleB { get; }
        public int Count { get; }

        public int this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return index == 0 ? TriangleA : TriangleB;
            }
        }

        internal StructuralFaceTriangles(int triangleA, int triangleB, int count)
        {
            if (triangleA < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(triangleA));
            }

            if (count != 1 && count != 2)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count == 2 && (triangleB < 0 || triangleB == triangleA))
            {
                throw new ArgumentOutOfRangeException(nameof(triangleB));
            }

            TriangleA = triangleA;
            TriangleB = count == 2 ? triangleB : -1;
            Count = count;
        }
    }
}