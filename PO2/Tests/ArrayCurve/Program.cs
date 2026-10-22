using PO2.Core;
using System;
using System.Diagnostics;
using System.Threading;
using PO2;
using UnityEngine;
using PO2.Tools.Arrays;

// Only the model's part-count constant is needed by these pure placement tests.
namespace PO2.Core { internal static class ProceduralObject { internal const int kMaxPartCount = 4096; } }

internal static partial class Program {
    private static int s_Checks;
    private static readonly CancellationToken Token = CancellationToken.None;
    private static readonly ObjectTransform Identity = new ObjectTransform(Vector3.zero, Quaternion.identity, Vector3.one);
    private static void Check(bool value, string message) { s_Checks++; if (!value) throw new Exception(message); }
    private static void Near(double actual, double expected, string message, double tolerance = .001) =>
        Check(Math.Abs(actual - expected) <= tolerance, message + $" ({actual} != {expected})");
    private static void Near(Vector3 actual, Vector3 expected, string message, double tolerance = .001) =>
        Near(new ArrayPoint(actual - expected).Length, 0, message, tolerance);
    private static void Rotation(Quaternion actual, Quaternion expected, string message) {
        Near(actual * Vector3.forward, expected * Vector3.forward, message);
        Near(actual * Vector3.up, expected * Vector3.up, message);
    }
    private static void Main(string[] args) {
        Sampling(); Connections(); Spacing(); Transforms(); DifficultPaths(); InvalidAndCancellation(); ReuseAndParallelReads(); AdditionalCases(); WorkerTests(); MinimumGapTests();
        Console.WriteLine($"PASS: {s_Checks} curve sampling, connection, placement, transform and cancellation checks.");
    }
    private static PartArrayCurve Curve(PartArrayBezier definition, Vector3? up = null) {
        Check(PartArrayCurve.TryCreate(definition, up ?? Vector3.up, Token, out var curve, out var reason), "Curve build: " + reason);
        return curve;
    }
    private static PartArraySource Source(Vector3? pivot = null, ObjectTransform? owner = null, Vector3? direction = null, Vector3? size = null) {
        Check(PartArraySource.TryCreate(new Bounds(Vector3.zero, size ?? new Vector3(1, 2, 3)), pivot ?? Vector3.zero,
            owner ?? Identity, direction ?? Vector3.forward, Vector3.up, out var source, out var reason), "Source capture: " + reason);
        return source;
    }
    private static PartArrayPlacements Layout(PartArrayCurve curve, PartArraySource source,
        PartArrayAlignment alignment = PartArrayAlignment.EndToEnd, PartArrayMode mode = PartArrayMode.FixedGap,
        double gap = 0, int count = 12, int capacity = 4095, Quaternion? offset = null) {
        Check(PartArrayCurveLayout.TryCreate(curve, source, alignment, offset ?? Quaternion.identity, gap, mode,
            count, capacity, 7, Token, out var layout, out var reason), "Placement: " + reason);
        Check(layout.Revision == 7, "Placement carries request revision");
        return layout;
    }
    private static PartArrayBezier Bend => new PartArrayBezier(Vector3.zero, new Vector3(0, 0, 20),
        new Vector3(20, 0, 30), new Vector3(35, 0, 30));

    private static void Sampling() {
        foreach (var definition in new[] {
            PartArrayBezier.Straight(Vector3.zero, Vector3.forward * 30),
            new PartArrayBezier(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.forward * 30),
            new PartArrayBezier(Vector3.zero, Vector3.zero, Vector3.forward * 30, Vector3.forward * 30) }) {
            var path = Curve(definition); int cursor = 0;
            Check(path.SampleCount == 2, "Straight geometry needs only one cached interval");
            Near(path.Length, 30, "Straight length", 1e-9);
            foreach (double distance in new[] { 0, .00001, .02, .4, 1, 5, 10, 23.4, 29.999, 30.0 }) {
                var point = path.AtDistance(distance, ref cursor, out var t);
                Near(point.Vector, Vector3.forward * (float)distance, "Distance sampling works with collapsed or uneven handles", 1e-5);
                Near(path.Point(t).Vector, point.Vector, "Distance result lies on Bezier", 1e-7);
            }
        }
        foreach (var definition in new[] { Bend, new PartArrayBezier(Vector3.zero,
            new Vector3(-30, 15, 20), new Vector3(20, -9, -5), new Vector3(10, 3, 40)) }) {
            var path = Curve(definition); int cursor = 0;
            Near(path.Length, ReferenceLength(definition, 1), "Length matches independent integration", .00002);
            for (int i = 0; i <= 30; i++) {
                double distance = path.Length * i / 30;
                var point = path.AtDistance(distance, ref cursor, out var t);
                Near(ReferenceLength(definition, t), distance, "Distance lookup matches independent integration", .00003);
                Check(PartArrayLayout.Finite(point.Vector), "Finite sampled point");
            }
            Near(path.SamplePosition(0), definition.Start, "Overlay starts at source");
            Near(path.SamplePosition(path.SampleCount - 1), definition.End, "Overlay ends at endpoint");
        }
    }
    // Independent Simpson integration, rather than the cache's quadrature.
    private static double ReferenceLength(PartArrayBezier p, double end) {
        const int steps = 4000;
        double result = 0, h = end / steps;
        for (int i = 0; i <= steps; i++) {
            double t = i * h, u = 1 - t;
            double x = 3 * u * u * ((double)p.Control1.x - p.Start.x) + 6 * u * t * ((double)p.Control2.x - p.Control1.x) + 3 * t * t * ((double)p.End.x - p.Control2.x);
            double y = 3 * u * u * ((double)p.Control1.y - p.Start.y) + 6 * u * t * ((double)p.Control2.y - p.Control1.y) + 3 * t * t * ((double)p.End.y - p.Control2.y);
            double z = 3 * u * u * ((double)p.Control1.z - p.Start.z) + 6 * u * t * ((double)p.Control2.z - p.Control1.z) + 3 * t * t * ((double)p.End.z - p.Control2.z);
            result += Math.Sqrt(x * x + y * y + z * z) * (i == 0 || i == steps ? 1 : i % 2 == 0 ? 2 : 4);
        }
        return result * h / 3;
    }
    private static void Connections() {
        var source = Source(); var path = Curve(Bend);
        foreach (double gap in new[] { 0, .05, 1.0, 5.0 }) {
            var result = Layout(path, source, gap: gap);
            Check(result.Count > 2, "Bend fits multiple segments");
            Vector3 last = result.Source.Pivot + result.Source.Rotation * source.Forward * (float)source.Front;
            for (int i = 0; i < result.Count; i++) {
                var pose = result[i];
                Vector3 rear = pose.Pivot + pose.Rotation * source.Forward * (float)source.Rear;
                Vector3 front = pose.Pivot + pose.Rotation * source.Forward * (float)source.Front;
                Near(Vector3.Distance(last, rear), gap, "Adjacent end gap", .00003);
                Near(Vector3.Distance(rear, front), source.Width, "Rigid segment length", .00001);
                Near(pose.Rotation * Vector3.up, Vector3.up, "Planar fence remains upright");
                last = front;
            }
        }
        var connected = Layout(path, source); var turn = Quaternion.AngleAxis(90, Vector3.up);
        var turned = connected.WithRotationOffset(turn, 8);
        Check(turned.Revision == 8 && turned.Count == connected.Count, "Offset replaces revision without refitting count");
        for (int i = 0; i < turned.Count; i++) {
            Near(turned[i].Pivot, connected[i].Pivot, "Relative rotation keeps fitted pivots");
            Near(turned[i].Rotation * Vector3.forward, connected[i].Rotation * Vector3.right, "90 degrees is relative at each segment");
        }
        Rotation(turned.Source.Rotation, connected.Source.Rotation * turn, "Source receives the same relative offset");
        Near(turned.Source.Pivot, source.Start, "Source pivot remains fixed");
        Check(Vector3.Distance(connected[0].Rotation * Vector3.forward, connected[connected.Count - 1].Rotation * Vector3.forward) > .5,
            "A connected bend has individual rotations");
    }
    private static void Spacing() {
        var source = Source();
        foreach (float length in new[] { 1f, 4.5f, 10.2f, 31.2f, 100f }) foreach (double gap in new[] { 0, .1, 2.0 }) {
            var path = Curve(PartArrayBezier.Straight(Vector3.zero, Vector3.forward * length));
            foreach (var alignment in new[] { PartArrayAlignment.EndToEnd, PartArrayAlignment.FixedOrientation })
                foreach (var mode in new[] { PartArrayMode.FixedGap, PartArrayMode.FitEndpoint, PartArrayMode.FixedCount }) {
                    var layout = Layout(path, source, alignment, mode, gap, 7);
                    Check(PartArrayLayout.TryCreate(new Bounds(Vector3.zero, new Vector3(1, 2, 3)), Vector3.zero, Identity,
                        Vector3.forward * length, Quaternion.identity, (float)gap, mode, 7, 4095, out var line, out _), "Existing line layout");
                    Check(layout.Count == line.Count, "Straight curve and line count match");
                    Near(layout.Gap, line.Gap, "Straight effective gap matches", .001);
                    for (int i = 0; i < layout.Count; i++) Near(layout[i].Pivot, line.Offset(i), "Straight placements match line", .001);
                }
        }
        var bent = Curve(Bend);
        foreach (var alignment in new[] { PartArrayAlignment.EndToEnd, PartArrayAlignment.FixedOrientation }) {
            foreach (int capacity in new[] { 0, 3, 4095 }) {
                var count = Layout(bent, source, alignment, PartArrayMode.FixedCount, count: 4095, capacity: capacity);
                Check(count.Count == capacity && count.CapacityLimited == (capacity < 4095), "Count respects part capacity");
                if (capacity > 0) Near(count[capacity - 1].Pivot, Bend.End, "Fixed count ends at endpoint");
            }
            var fixedCount = Layout(bent, source, alignment, PartArrayMode.FixedCount, count: 23);
            var rotated = fixedCount.WithRotationOffset(Quaternion.AngleAxis(76, Vector3.right), 22);
            for (int i = 0; i < fixedCount.Count; i++) {
                Near(rotated[i].Pivot, fixedCount[i].Pivot, "Fixed count never refits on rotation");
                if (alignment == PartArrayAlignment.FixedOrientation) Rotation(rotated[i].Rotation, rotated.Source.Rotation, "Fixed orientation is identical everywhere");
            }
        }
        foreach (double gap in new[] { 0, .1, 1.0 }) foreach (int capacity in new[] { 3, 4095 }) {
            var fit = Layout(bent, source, mode: PartArrayMode.FitEndpoint, gap: gap, capacity: capacity);
            Check(fit.Gap >= gap && fit.Count > 0, "Fit respects minimum gap");
            var last = fit[fit.Count - 1];
            Near(last.Pivot + last.Rotation * source.Forward * (float)source.Front, Bend.End, "Fitted end reaches endpoint", .0005);
        }
    }
}
