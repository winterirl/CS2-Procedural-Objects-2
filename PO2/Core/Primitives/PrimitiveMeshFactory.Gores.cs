using System;
using UnityEngine;

namespace PO2.Core.Primitives {
    public static partial class PrimitiveMeshFactory {
        // Twelve narrow strips keep the bend within each unwrap small. Boundaries
        // follow existing columns, including segment counts that do not divide by 12.
        private sealed class PrimitiveGores {
            public readonly int Count;
            public readonly int Stride;
            public readonly int[] Left;
            public readonly float[] Centers;

            public PrimitiveGores(int segments) {
                Count = Math.Min(12, segments);
                Stride = segments + Count;
                Left = new int[segments];
                Centers = new float[segments];
                for (int gore = 0; gore < Count; gore++) {
                    int first = gore * segments / Count;
                    int end = (gore + 1) * segments / Count;
                    float center = (first + end) * 0.5f / segments;
                    for (int column = first; column < end; column++) {
                        Left[column] = column + gore;
                        Centers[column] = center;
                    }
                }
            }

            public Vector4 UV(int segment, float longitude, float ringRadius,
                float v, float circumference, float meridianLength) {
                float u = Centers[segment] + (longitude - Centers[segment]) * ringRadius;
                // XY is the full unwrap. ZW carries the same layout in meters for
                // Tile, so changing modes never sends these strips through a planar unwrap.
                return new Vector4(u, v, u * circumference, v * meridianLength);
            }
        }

        private static float[] DomeMeridianDistances(float radius, float height, int rows) {
            var distances = new float[rows + 1];
            // Integrate the ellipse arc, including very tall or shallow domes.
            // Several samples per ring keep the UV spacing independent of mesh detail.
            for (int row = 1; row <= rows; row++) {
                double start = (row - 1) * Math.PI * 0.5 / rows;
                double step = Math.PI * 0.5 / rows / 32;
                double length = 0;
                for (int i = 0; i <= 32; i++) {
                    double angle = start + i * step;
                    double x = radius * Math.Cos(angle);
                    double y = height * Math.Sin(angle);
                    int weight = i == 0 || i == 32 ? 1 : i % 2 == 0 ? 2 : 4;
                    length += weight * Math.Sqrt(x * x + y * y);
                }
                distances[row] = distances[row - 1] + (float)(length * step / 3);
            }
            return distances;
        }
    }
}
