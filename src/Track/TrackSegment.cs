using System;
using System.Collections.Generic;

namespace VintageRailroading.Track
{
    /// <summary>
    /// A placed length of track: a gauge bound to a centerline curve, with a
    /// stable id for persistence and for trains to reference. The curve is the
    /// CENTERLINE; the two running rails are offset ±Gauge.WidthMeters/2 from it
    /// by Layer 1 when generating mesh. Gauge is stored here for validation
    /// (which stock may enter) and for that rail offset — the centerline math
    /// itself is gauge-independent.
    /// </summary>
    public sealed class TrackSegment
    {
        public long Id { get; }
        public TrackGauge Gauge { get; }
        public HermiteCurve Curve { get; }

        public double Length => Curve.Length;

        public TrackSegment(long id, TrackGauge gauge, HermiteCurve curve)
        {
            Gauge = gauge ?? throw new ArgumentNullException(nameof(gauge));
            Curve = curve ?? throw new ArgumentNullException(nameof(curve));
            Id = id;
        }

        public Vec3d PositionAtDistance(double s) => Curve.PositionAtDistance(s);
        public Vec3d HeadingAtDistance(double s) => Curve.HeadingAtDistance(s);

        /// <summary>Centerline offset to the two running rails at distance s,
        /// in world space. Returns (leftRail, rightRail). Uses world-up to find
        /// the lateral axis; superelevation (banking) is a later refinement.</summary>
        public (Vec3d left, Vec3d right) RailPointsAtDistance(double s)
        {
            Vec3d center = PositionAtDistance(s);
            Vec3d heading = HeadingAtDistance(s);
            Vec3d up = new Vec3d(0, 1, 0);
            Vec3d lateral = heading.Cross(up).Normalized(); // points to one side
            double half = Gauge.WidthMeters * 0.5;
            return (center + lateral * half, center - lateral * half);
        }
    }

    /// <summary>
    /// An ordered chain of connected segments a train can traverse as one
    /// continuous arc-length space. Distance 0 is the start of the first
    /// segment; TotalLength is the end of the last. Converting a global
    /// distance to (segment, localDistance) is what lets a multi-car consist
    /// span segment boundaries without special-casing the joins.
    ///
    /// All segments in a path must share a gauge — enforced at construction.
    /// </summary>
    public sealed class TrackPath
    {
        private readonly List<TrackSegment> _segments;
        private readonly double[] _startDist; // global start distance of each segment

        public TrackGauge Gauge { get; }
        public double TotalLength { get; }
        public IReadOnlyList<TrackSegment> Segments => _segments;

        public TrackPath(IEnumerable<TrackSegment> segments)
        {
            _segments = new List<TrackSegment>(segments ?? throw new ArgumentNullException(nameof(segments)));
            if (_segments.Count == 0) throw new ArgumentException("path needs at least one segment");

            Gauge = _segments[0].Gauge;
            _startDist = new double[_segments.Count];
            double acc = 0.0;
            for (int i = 0; i < _segments.Count; i++)
            {
                if (_segments[i].Gauge.Id != Gauge.Id)
                    throw new ArgumentException(
                        $"mixed gauge in path: '{_segments[i].Gauge.Id}' != '{Gauge.Id}' at index {i}");
                _startDist[i] = acc;
                acc += _segments[i].Length;
            }
            TotalLength = acc;
        }

        /// <summary>Map a global distance to the owning segment and the local
        /// distance within it.</summary>
        public (TrackSegment seg, double local) Locate(double globalDistance)
        {
            if (globalDistance <= 0.0) return (_segments[0], 0.0);
            if (globalDistance >= TotalLength)
            {
                var last = _segments[_segments.Count - 1];
                return (last, last.Length);
            }
            // binary search the segment whose start distance precedes globalDistance
            int lo = 0, hi = _segments.Count - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_startDist[mid] <= globalDistance) lo = mid; else hi = mid;
            }
            int idx = (_startDist[hi] <= globalDistance) ? hi : lo;
            return (_segments[idx], globalDistance - _startDist[idx]);
        }

        public Vec3d PositionAtDistance(double globalDistance)
        {
            var (seg, local) = Locate(globalDistance);
            return seg.PositionAtDistance(local);
        }

        public Vec3d HeadingAtDistance(double globalDistance)
        {
            var (seg, local) = Locate(globalDistance);
            return seg.HeadingAtDistance(local);
        }
    }
}
