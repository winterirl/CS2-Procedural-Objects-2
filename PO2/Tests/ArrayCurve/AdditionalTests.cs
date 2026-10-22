using System;
using PO2;
using UnityEngine;
using PO2.Tools.Arrays;

internal static partial class Program {
    private static void AdditionalCases() {
        var random = new Random(71423); var source = Source();
        for (int c = 0; c < 64; c++) {
            Func<float> n = () => (float)(random.NextDouble() * 2 - 1);
            var definition = new PartArrayBezier(Vector3.zero, new Vector3(n() * 12, n() * 8, 20),
                new Vector3(n() * 25, n() * 16, 40), new Vector3(n() * 35, n() * 20, 65));
            Check(PartArrayCurve.TryCreate(definition, source.ReferenceFrame * Vector3.up, Token,
                out var curve, out var reason, referenceForward: source.Forward), "Seeded 3D curve: " + reason);
            double gap = c % 3 == 0 ? 0 : .2;
            var poses = Layout(curve, source, gap: gap);
            Vector3 last = poses.Source.Pivot + poses.Source.Rotation * source.Forward * (float)source.Front;
            for (int i = 0; i < poses.Count; i++) {
                var pose = poses[i];
                Vector3 rear = pose.Pivot + pose.Rotation * source.Forward * (float)source.Rear;
                Vector3 front = pose.Pivot + pose.Rotation * source.Forward * (float)source.Front;
                Near(Vector3.Distance(last, rear), gap, "Seeded 3D connection gap", .00006);
                Near(Vector3.Distance(front, rear), source.Width, "Seeded 3D rigid length", .00003);
                last = front;
            }
            var original = Layout(curve, source, PartArrayAlignment.FixedOrientation, PartArrayMode.FixedCount, count: 19);
            Quaternion offset = Quaternion.AngleAxis(35, new Vector3(1, 2, 3));
            var rotated = original.WithRotationOffset(offset, 15);
            for (int i = 0; i < rotated.Count; i++) {
                Near(rotated[i].Pivot, original[i].Pivot, "Seeded fixed rotation does not change pivot");
                Rotation(rotated[i].Rotation, offset, "Seeded fixed orientation stays global");
            }
        }
        var path = Curve(Bend);
        foreach (int capacity in new[] { -1, int.MaxValue }) Check(!PartArrayCurveLayout.TryCreate(path, source,
            PartArrayAlignment.EndToEnd, Quaternion.identity, 0, PartArrayMode.FixedCount, 100, capacity, 1,
            Token, out _, out _), "Invalid capacity cannot allocate an unbounded result");
        Check(!PartArrayCurve.TryCreate(Bend, Vector3.up, Token, out _, out var detailReason, 1e-20) && !detailReason.IsEmpty,
            "Unresolvable detail stops at a fixed subdivision bound");
        Check(!PartArrayCurveLayout.TryCreate(path, Source(pivot: Vector3.one), PartArrayAlignment.EndToEnd,
            Quaternion.identity, 0, PartArrayMode.FixedGap, 10, 100, 1, Token, out _, out _), "Stale source start is rejected");
        var constant = Layout(path, source, PartArrayAlignment.FixedOrientation);
        bool guarded = false;
        try { constant.WithRotationOffset(Quaternion.identity, 3); } catch (InvalidOperationException) { guarded = true; }
        Check(guarded, "Fixed-orientation gap mode cannot skip rotated footprint calculation");
        var lots = Layout(path, source, mode: PartArrayMode.FixedCount, count: 4095);
        var changed = lots.WithRotationOffset(Quaternion.identity, 3);
        Check(changed.Count == lots.Count, "Rotation reuse retains full placement count");
        for (int i = 0; i < lots.Count; i++) {
            Check(changed[i].Pivot.Equals(lots[i].Pivot) && changed[i].Rotation.Equals(lots[i].Rotation),
                "Identity rotation preserves each fitted placement");
        }
    }
}
