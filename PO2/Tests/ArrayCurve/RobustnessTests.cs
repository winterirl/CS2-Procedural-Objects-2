using System;
using System.Diagnostics;
using System.Threading;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Tools.Arrays;

internal static partial class Program {
    private static void Transforms() {
        foreach (var scale in new[] { Vector3.one, new Vector3(2, .5f, 3), new Vector3(-2, 3, .5f) }) {
            var owner = new ObjectTransform(new Vector3(11000, 130, -8000), Quaternion.AngleAxis(37, new Vector3(1, 2, 3)), scale);
            Vector3 pivot = new Vector3(.25f, .2f, -.5f);
            Vector3 forward = owner.Rotation * Vector3.forward;
            var source = Source(pivot, owner, forward); var s = source.Start;
            var path = Curve(new PartArrayBezier(s, s + forward * 30, s + forward * 40 + Vector3.right * 15, s + forward * 50 + Vector3.right * 30));
            var poses = Layout(path, source);
            for (int i = 0; i < poses.Count; i++) {
                var pose = poses[i]; var matrix = pose.LocalTransform(owner, pivot);
                Near(owner.TransformPoint(matrix.MultiplyPoint3x4(pivot)), pose.Pivot, "Baked pivot matches preview", .002);
                foreach (var vertex in new[] { Vector3.zero, Vector3.one, new Vector3(-1, 2, 3) }) {
                    var expected = pose.Pivot + pose.Rotation * owner.TransformVector(vertex - pivot);
                    Near(owner.TransformPoint(matrix.MultiplyPoint3x4(vertex)), expected, "World and local transforms agree", .003);
                    var preview = Matrix4x4.TRS(pose.PreviewPosition(owner, source.Start), pose.Rotation * owner.Rotation, owner.Scale).MultiplyPoint3x4(vertex);
                    Near(preview, expected, "Shared-mesh preview matches world placement", .005);
                }
                Near(matrix.determinant, 1, "Rigid world placement does not reverse winding", .00001);
            }
        }
    }
    private static void DifficultPaths() {
        foreach (float degrees in new[] { 89f, 90f, 91f }) {
            Quaternion expected = Quaternion.AngleAxis(-degrees, Vector3.right);
            Vector3 forward = expected * Vector3.forward;
            Check(PartArrayCurve.TryCreate(PartArrayBezier.Straight(Vector3.zero, forward * 30), Vector3.up,
                Token, out var tilt, out _, referenceForward: Vector3.forward), "Curve carries the captured starting frame");
            Rotation(tilt.Frame(0), expected, "Editing through vertical does not flip source roll");
        }
        var folded = Curve(new PartArrayBezier(Vector3.zero, Vector3.forward * 30, Vector3.forward * 30, Vector3.zero));
        Check(folded.TryChord(0, new ArrayPoint(Vector3.zero), 5, Token, out double t, out var point), "Chord search finds exit before a return");
        Check(t < .5, "Search chooses first forward branch"); Near(point.Length, 5, "Folded chord length", .00001);
        var source = Source();
        foreach (var p in new[] {
            new PartArrayBezier(Vector3.zero, Vector3.up * 20, new Vector3(4, 20, 0), new Vector3(12, 20, 10)),
            new PartArrayBezier(Vector3.zero, new Vector3(30, 8, 20), new Vector3(-30, -8, 20), new Vector3(0, 0, 40)),
            new PartArrayBezier(Vector3.zero, Vector3.zero, Vector3.forward * 20, Vector3.forward * 20),
            new PartArrayBezier(Vector3.zero, Vector3.forward * 30, Vector3.forward * 30, Vector3.zero) }) {
            var path = Curve(p);
            var layout = Layout(path, source, mode: PartArrayMode.FixedCount, count: 200);
            for (int i = 0; i < layout.Count; i++) {
                Vector3 f = layout[i].Rotation * Vector3.forward, u = layout[i].Rotation * Vector3.up;
                Near(f.magnitude, 1, "Frame forward unit", .00001); Near(u.magnitude, 1, "Frame up unit", .00001);
                Near(Vector3.Dot(f, u), 0, "Frame is orthogonal", .00001);
                Check(PartArrayLayout.Finite(layout[i].Pivot), "Difficult path has finite poses");
            }
        }
        var high = Curve(new PartArrayBezier(Vector3.zero, Vector3.up * 30, new Vector3(0, 30, 30), Vector3.forward * 30));
        Check(Layout(high, source).Count > 0, "Vertical bend can connect segments");
        var zeroWidth = Source(size: Vector3.zero);
        Check(Layout(Curve(Bend), zeroWidth, mode: PartArrayMode.FixedCount).Count == 12, "Fixed count supports zero width");
        Check(!PartArrayCurveLayout.TryCreate(Curve(Bend), zeroWidth, PartArrayAlignment.EndToEnd, Quaternion.identity,
            0, PartArrayMode.FixedGap, 4, 20, 1, Token, out _, out _), "Zero width cannot start an infinite chain");
        var outside = Source(pivot: Vector3.forward * 20);
        var straight = Curve(PartArrayBezier.Straight(outside.Start, outside.Start + Vector3.forward * 30));
        var fixedPoses = Layout(straight, outside, PartArrayAlignment.FixedOrientation);
        for (int i = 0; i < fixedPoses.Count; i++) {
            Check(fixedPoses[i].Pivot.z <= straight.Definition.End.z, "Offset pivots remain on the path");
            if (i > 0) Check(fixedPoses[i].Pivot.z > fixedPoses[i - 1].Pivot.z, "Offset pivots do not pile up at the endpoint");
        }
    }
    private static void InvalidAndCancellation() {
        Check(!PartArrayCurve.TryCreate(new PartArrayBezier(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero),
            Vector3.up, Token, out _, out _), "Collapsed curve rejected");
        Check(!PartArrayCurve.TryCreate(new PartArrayBezier(Vector3.zero, new Vector3(float.NaN, 0, 0), Vector3.one, Vector3.one),
            Vector3.up, Token, out _, out _), "NaN controls rejected");
        Check(!PartArrayCurve.TryCreate(Bend, Vector3.up, Token, out _, out _, double.NaN), "NaN tolerance rejected");
        var source = Source(); var path = Curve(Bend);
        foreach (double gap in new[] { -1, double.NaN, double.PositiveInfinity })
            Check(!PartArrayCurveLayout.TryCreate(path, source, PartArrayAlignment.EndToEnd, Quaternion.identity,
                gap, PartArrayMode.FixedGap, 5, 10, 1, Token, out _, out _), "Invalid gap rejected");
        Check(!PartArrayCurveLayout.TryCreate(path, source, PartArrayAlignment.EndToEnd, default, 0,
            PartArrayMode.FixedGap, 1, 10, 1, Token, out _, out _), "Invalid rotation rejected");
        var badOwner = Identity; badOwner.Scale = Vector3.zero;
        Check(!PartArraySource.TryCreate(new Bounds(Vector3.zero, Vector3.one), Vector3.zero, badOwner,
            Vector3.forward, Vector3.up, out _, out _), "Zero owner scale rejected");
        using (var cancel = new CancellationTokenSource()) {
            cancel.Cancel(); bool caught = false;
            try { PartArrayCurve.TryCreate(Bend, Vector3.up, cancel.Token, out _, out _); } catch (OperationCanceledException) { caught = true; }
            Check(caught, "Cache build respects cancellation"); caught = false;
            try { PartArrayCurveLayout.TryCreate(path, source, PartArrayAlignment.EndToEnd, Quaternion.identity,
                0, PartArrayMode.FixedGap, 3, 10, 1, cancel.Token, out _, out _); } catch (OperationCanceledException) { caught = true; }
            Check(caught, "Placement respects cancellation");
        }
    }
    private static void ReuseAndParallelReads() {
        var source = Source(); var path = Curve(Bend); var result = Layout(path, source);
        var tasks = new System.Threading.Tasks.Task[4];
        for (int j = 0; j < tasks.Length; j++) tasks[j] = System.Threading.Tasks.Task.Run(() => {
            for (int i = 0; i < 50; i++) {
                if (!PartArrayCurveLayout.TryCreate(path, source, PartArrayAlignment.EndToEnd, Quaternion.identity,
                    0, PartArrayMode.FixedGap, 5, 4095, i, Token, out var other, out var reason)) throw new Exception(reason.ToString());
                if (other.Count != result.Count) throw new Exception("Shared cache changed placement count");
                for (int n = 0; n < result.Count; n++) {
                    if (!other[n].Pivot.Equals(result[n].Pivot) || !other[n].Rotation.Equals(result[n].Rotation))
                        throw new Exception("Shared cache changed placement values");
                }
            }
        });
        System.Threading.Tasks.Task.WaitAll(tasks);
    }
}
