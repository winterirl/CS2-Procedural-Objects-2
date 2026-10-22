using System;
using System.Diagnostics;
using PO2;
using UnityEngine;
using PO2.Geometry.Picking;

internal static partial class Program {
    private static int s_Checks;
    private static void Check(bool condition, string name) {
        s_Checks++;
        if (!condition) { throw new Exception(name); }
    }
    internal static void Grid(int width, out Vector3[] vertices, out int[] indices) {
        vertices = new Vector3[(width + 1) * (width + 1)];
        indices = new int[width * width * 6];
        for (int y = 0; y <= width; y++) {
            for (int x = 0; x <= width; x++) { vertices[y * (width + 1) + x] = new Vector3(x, y, 10); }
        }
        for (int y = 0; y < width; y++) {
            for (int x = 0; x < width; x++) {
                int a = y * (width + 1) + x;
                int offset = (y * width + x) * 6;
                indices[offset] = a; indices[offset + 1] = a + 1; indices[offset + 2] = a + width + 2;
                indices[offset + 3] = a; indices[offset + 4] = a + width + 2; indices[offset + 5] = a + width + 1;
            }
        }
    }
    private static void Main(string[] args) {
        ClipboardModeTests();
        if (Array.IndexOf(args, "--clipboard") >= 0) { Console.WriteLine("PASS: Copy/Paste input and mode checks."); return; }
        GeometryCases();
        IntegrationCases();
        DeleteModeCases();
        WorkerCases();
        SchedulingCases();
        foreach (int width in new[] { 1, 4, 6, 32, 388 }) {
            Grid(width, out Vector3[] vertices, out int[] indices);
            TrianglePickCache cache = new TrianglePickCache(vertices, indices);
            for (int i = 0; i < 32; i++) {
                Vector3 origin = new Vector3((i * 0.723f) % width, (i * 0.319f) % width, 0);
                bool hit = cache.TryPick(origin, new Vector3(0, 0, 1), out int triangleId, out float distance);
                Check(hit && Math.Abs(distance - 10) < 0.0001f, "grid hit");
                Check(triangleId >= 0 && triangleId < indices.Length / 3, "triangle id");
            }
            Vector3[] moved = (Vector3[])vertices.Clone();
            for (int i = 0; i < moved.Length; i++) { moved[i].z += 5; }
            TrianglePickCache refit = cache.RefitCopy(moved);
            Check(refit.TryPick(new Vector3(0.2f, 0.3f, 0), new Vector3(0, 0, 1), out int _, out float d) && Math.Abs(d - 15) < 0.001f, "refit");
            Check(cache.TryPick(new Vector3(0.2f, 0.3f, 0), new Vector3(0, 0, 1), out int _, out d) && Math.Abs(d - 10) < 0.001f, "refit leaves original untouched");
        }
        Console.WriteLine($"PASS: {s_Checks} picking checks.");
    }
}
