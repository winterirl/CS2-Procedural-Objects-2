using System;

namespace PO2.Geometry.Topology
{
    // Surface selection stores triangle IDs. Commands expand these to whole
    // structural faces or Surface Areas when needed.
    public readonly struct SurfaceRef : IEquatable<SurfaceRef>
    {
        public int PartIndex { get; }
        public int TriangleId { get; }

        public SurfaceRef(int partIndex, int triangleId)
        {
            PartIndex = partIndex;
            TriangleId = triangleId;
        }

        public bool Equals(SurfaceRef other)
        {
            return PartIndex == other.PartIndex && TriangleId == other.TriangleId;
        }

        public override bool Equals(object obj)
        {
            return obj is SurfaceRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (PartIndex * 397) ^ TriangleId;
            }
        }

        public static bool operator ==(SurfaceRef left, SurfaceRef right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SurfaceRef left, SurfaceRef right)
        {
            return !left.Equals(right);
        }
    }
}