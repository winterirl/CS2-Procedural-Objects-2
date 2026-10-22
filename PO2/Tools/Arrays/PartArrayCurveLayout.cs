using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using PO2.Core;
using PO2.Localization;

namespace PO2.Tools.Arrays
{
    internal static class PartArrayCurveLayout
    {
        internal static bool TryCreate(PartArrayCurve path, PartArraySource source, PartArrayAlignment alignment, Quaternion offset, double gap, PartArrayMode mode, int copies, int capacity, int revision, CancellationToken token, out PartArrayPlacements result, out LocalizedText reason)
        {
            result = null;
            reason = LocalizedText.Empty;
            token.ThrowIfCancellationRequested();
            if (path == null || source == null || alignment < PartArrayAlignment.EndToEnd || alignment > PartArrayAlignment.FixedOrientation || mode < PartArrayMode.FixedGap || mode > PartArrayMode.FixedCount || capacity < 0 || capacity > ProceduralObject.kMaxPartCount || !PartArrayCurveMath.Finite(gap) || gap < 0 || !PartArrayCurveMath.TryRotation(offset, out offset))
            {
                reason = LocalizedText.For("PO2.PartArray.Curve.Error.InvalidSettings");
                return false;
            }

            if ((new ArrayPoint(path.Definition.Start) - new ArrayPoint(source.Start)).Length > path.Tolerance)
            {
                reason = LocalizedText.For("PO2.PartArray.Curve.Error.SourcePivotChanged");
                return false;
            }

            bool connected = alignment == PartArrayAlignment.EndToEnd;
            Quaternion referenceInverse = Quaternion.Inverse(source.ReferenceFrame);
            Quaternion sourceRotation = connected ? path.Frame(0) * referenceInverse : Quaternion.identity;
            var sourcePlacement = new PartArrayPlacement(source.Start, sourceRotation);
            if (mode == PartArrayMode.FixedCount)
            {
                int requested = Math.Max(1, copies), count = Math.Min(requested, capacity);
                double stride = count > 0 ? path.Length / count : 0;
                var placements = AlongPath(path, connected, referenceInverse, count, stride, token);
                result = new PartArrayPlacements(revision, sourcePlacement, placements, offset, requested > capacity, 0, stride, true);
                return true;
            }

            if (!connected)
            {
                source.Measure(offset, out double rear, out double front);
                // An offset pivot can sit beyond the front of its geometry.
                // Keep those pivots on the curve instead of clamping several
                // overhanging placements onto the same endpoint.
                double width = front - rear, stride = width + gap, available = path.Length - Math.Max(0, front);
                if (stride < .000001 || !PartArrayCurveMath.Finite(stride))
                {
                    reason = LocalizedText.For("PO2.PartArray.Error.ZeroSpacing");
                    return false;
                }

                double requested = Math.Max(0, Math.Floor((available + .00001) / stride));
                int count = (int)Math.Min(capacity, requested);
                double effective = mode == PartArrayMode.FitEndpoint && count > 0 ? Math.Max(gap, available / count - width) : gap;
                var placements = AlongPath(path, false, referenceInverse, count, width + effective, token);
                result = new PartArrayPlacements(revision, sourcePlacement, placements, offset, requested > capacity, effective, width + effective, false);
                return true;
            }

            if (source.Width < .000001)
            {
                reason = LocalizedText.For("PO2.PartArray.Curve.Error.FlatConnectedPart");
                return false;
            }

            try
            {
                var first = new ArrayPoint(source.Start) + new ArrayPoint(sourceRotation * source.Forward) * source.Front;
                // Keep the valid minimum-gap chain. Endpoint fitting can reuse it
                // as a whole-part fallback without walking the path again.
                var collected = new List<PartArrayPlacement>(Math.Min(capacity, 32));
                int requested = Walk(path, source, first, gap, capacity + 1, collected, referenceInverse, token, out var minimumEnd);
                // On a bend the rigid source front can miss the curve entirely
                // at a small exact gap. Fit Endpoint treats that gap as a lower
                // bound: start from the nearest feasible uniform gap instead.
                double feasibleGap = gap;
                if (mode == PartArrayMode.FitEndpoint && requested == 0 && gap > 0 && capacity > 0 && !path.TryChord(0, first, gap, token, out _, out _))
                {
                    feasibleGap = Math.Max(gap, path.MinimumGapToCurve(first, token));
                    if (feasibleGap > gap)
                        requested = Walk(path, source, first, feasibleGap, capacity + 1, collected, referenceInverse, token, out minimumEnd);
                    if (requested == 0) feasibleGap = gap;
                }

                int count = Math.Min(capacity, requested);
                double effective = feasibleGap;
                bool fallback = false;
                if (mode == PartArrayMode.FitEndpoint && count > 0)
                {
                    if (TryFitGap(path, source, first, referenceInverse, count, feasibleGap, requested == count ? minimumEnd : (ArrayPoint?)null, token, out effective))
                    {
                        if (effective != feasibleGap)
                        {
                            var fitted = new List<PartArrayPlacement>(count);
                            if (Walk(path, source, first, effective, count, fitted, referenceInverse, token, out _) == count)
                                collected = fitted;
                            else fallback = true;
                        }
                    }
                    else fallback = true;

                    if (fallback) effective = feasibleGap;
                }

                if (collected.Count > count) collected.RemoveAt(collected.Count - 1);
                result = new PartArrayPlacements(revision, sourcePlacement, collected.ToArray(), offset, requested > capacity, effective, 0, true, fallback);
                return true;
            }
            catch (InvalidOperationException e)
            {
                reason = LocalizedText.FromException(e);
                return false;
            }
        }

        private static PartArrayPlacement[] AlongPath(PartArrayCurve path, bool follow, Quaternion referenceInverse, int count, double stride, CancellationToken token)
        {
            var placements = new PartArrayPlacement[count];
            int cursor = 0;
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                var point = path.AtDistance(stride * (i + 1), ref cursor, out double parameter);
                Quaternion rotation = follow ? path.Frame(parameter) * referenceInverse : Quaternion.identity;
                placements[i] = new PartArrayPlacement(point.Vector, rotation);
            }

            return placements;
        }

        private static int Walk(PartArrayCurve path, PartArraySource source, ArrayPoint first, double gap, int limit, List<PartArrayPlacement> output, Quaternion referenceInverse, CancellationToken token, out ArrayPoint last)
        {
            last = first;
            double parameter = 0;
            for (int i = 0; i < limit; i++)
            {
                token.ThrowIfCancellationRequested();
                ArrayPoint rear = last;
                if (gap > 0 && !path.TryChord(parameter, last, gap, token, out parameter, out rear)) return i;
                double rearParameter = parameter;
                if (!path.TryChord(parameter, rear, source.Width, token, out double next, out ArrayPoint front)) return i;
                if (next <= parameter + 1e-14) throw new LocalizedTextException(LocalizedText.For("PO2.PartArray.Curve.Error.NoProgress"));
                if (output != null)
                {
                    var direction = (front - rear) * (1 / (front - rear).Length);
                    var pivot = rear - direction * source.Rear;
                    Quaternion rotation = path.ChordFrame((rearParameter + next) * .5, direction.Vector) * referenceInverse;
                    output.Add(new PartArrayPlacement(pivot.Vector, rotation));
                }

                parameter = next;
                last = front;
            }

            return limit;
        }

        private static bool TryFitGap(PartArrayCurve path, PartArraySource source, ArrayPoint first, Quaternion referenceInverse, int count, double minimum, ArrayPoint? minimumEnd, CancellationToken token, out double gap)
        {
            try
            {
                return FitGap(path, source, first, referenceInverse, count, minimum, minimumEnd, token, out gap);
            }
            catch (InvalidOperationException)
            {
                gap = minimum;
                return false;
            }
        }

        private static bool FitGap(PartArrayCurve path, PartArraySource source, ArrayPoint first, Quaternion referenceInverse, int count, double minimum, ArrayPoint? minimumEnd, CancellationToken token, out double gap)
        {
            gap = minimum;
            var end = new ArrayPoint(path.Definition.End);
            double tolerance = Math.Max(path.Tolerance * 4, .00001);
            ArrayPoint last;
            if (minimumEnd.HasValue) last = minimumEnd.Value;
            else Walk(path, source, first, minimum, count, null, referenceInverse, token, out last);
            if ((last - end).Length <= tolerance) return true;
            double low = minimum, high = Math.Max(minimum + 1, path.Length + Math.Abs(source.Front) + source.Width);
            // Feasible gaps form a continuous branch on ordinary open curves.
            // Folded curves can jump between branches, so convergence alone is
            // not success: the final anchor must actually reach the endpoint.
            for (int i = 0; i < 48; i++)
            {
                token.ThrowIfCancellationRequested();
                double middle = (low + high) * .5;
                int found = Walk(path, source, first, middle, count, null, referenceInverse, token, out last);
                if (found == count)
                {
                    low = middle;
                    gap = middle;
                    if ((last - end).Length <= tolerance) return true;
                }
                else high = middle;

                if (high - low < Math.Min(path.Tolerance * .01, 1e-8)) break;
            }

            Walk(path, source, first, gap, count, null, referenceInverse, token, out last);
            return (last - end).Length <= tolerance;
        }
    }
}