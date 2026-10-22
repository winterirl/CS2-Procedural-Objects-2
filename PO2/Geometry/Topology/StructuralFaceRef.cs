using System;

namespace PO2.Geometry.Topology
{
    // Structural face ids restart for each part, so keep the part index in the
    // reference just like vertex and structural-edge selection do.
    public readonly struct StructuralFaceRef : IEquatable<StructuralFaceRef>
    {
        public int PartIndex { get; }
        public int FaceId { get; }

        public StructuralFaceRef(int partIndex, int faceId)
        {
            PartIndex = partIndex;
            FaceId = faceId;
        }

        public bool Equals(StructuralFaceRef other)
        {
            return PartIndex == other.PartIndex && FaceId == other.FaceId;
        }

        public override bool Equals(object obj)
        {
            return obj is StructuralFaceRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (PartIndex * 397) ^ FaceId;
            }
        }

        public static bool operator ==(StructuralFaceRef left, StructuralFaceRef right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StructuralFaceRef left, StructuralFaceRef right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"Part {PartIndex}, Structural Face {FaceId}";
        }
    }
}