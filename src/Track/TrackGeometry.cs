using System;
using System.Collections.Generic;

namespace VintageRailroading.Track
{
    // ---------------------------------------------------------------------
    // Layer 0 — Track Geometry (pure math, NO Vintage Story dependency)
    //
    // This namespace must never reference Vintagestory.* so it stays unit-
    // testable in isolation and its correctness is independent of the game.
    // The math here was validated numerically against a reference prototype
    // (see tests/proto_geometry.py): straight-length exactness, arc-length
    // linearity, curve length > chord, endpoint-heading fidelity, and
    // <1% spacing deviation on curves at 64 samples.
    // ---------------------------------------------------------------------

    /// <summary>Minimal double-precision 3D vector. Game code will convert
    /// to/from Vec3d at the Layer 1 boundary; Layer 0 stays engine-agnostic.</summary>
    public readonly struct Vec3d
    {
        public readonly double X, Y, Z;
        public Vec3d(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3d operator *(Vec3d a, double s) => new Vec3d(a.X * s, a.Y * s, a.Z * s);
        public static Vec3d operator *(double s, Vec3d a) => a * s;

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public Vec3d Normalized()
        {
            double l = Length;
            return l < 1e-9 ? new Vec3d(0, 0, 0) : new Vec3d(X / l, Y / l, Z / l);
        }

        public double Dot(Vec3d o) => X * o.X + Y * o.Y + Z * o.Z;

        public Vec3d Cross(Vec3d o) =>
            new Vec3d(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);

        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }

    /// <summary>
    /// A track gauge as first-class registered data — never a magic number.
    /// WidthMeters is the distance between the inner faces of the two running
    /// rails (the centerline is gauge-independent; gauge only offsets the
    /// rails ±Width/2 at the rendering/Layer-1 stage).
    /// </summary>
    public sealed class TrackGauge
    {
        public string Id { get; }
        public string DisplayName { get; }
        public double WidthMeters { get; }

        public TrackGauge(string id, string displayName, double widthMeters)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("gauge id required");
            if (widthMeters <= 0) throw new ArgumentException("gauge width must be > 0");
            Id = id;
            DisplayName = displayName;
            WidthMeters = widthMeters;
        }

        // Real-world prototype gauges, so stock and rail spacing look right at
        // VS scale (1 block = 1 metre). Extend freely — this is just the
        // built-in set, not a closed enum.
        public static readonly TrackGauge Narrow   = new TrackGauge("narrow",   "Narrow (2ft)",      0.610);
        public static readonly TrackGauge Metre    = new TrackGauge("metre",    "Metre",             1.000);
        public static readonly TrackGauge Standard = new TrackGauge("standard", "Standard (4ft 8½)", 1.435);
        public static readonly TrackGauge Broad    = new TrackGauge("broad",    "Broad (5ft 6)",     1.676);

        public override string ToString() => $"{DisplayName} [{Id}] {WidthMeters}m";
    }

    /// <summary>
    /// One curve between two placement nodes, as a cubic Hermite spline.
    /// Endpoint tangents are explicit inputs — this is deliberate: in a track-
    /// laying mod the player controls the heading at each end (new track leaves
    /// the previous piece tangent to it), which maps exactly to Hermite's
    /// endpoint-tangent form. Catmull-Rom (tangents inferred from neighbours)
    /// would be wrong for incremental placement.
    ///
    /// Tangent MAGNITUDE controls curve tightness: longer tangents bow the
    /// curve out further before turning. As a rule of thumb, scaling each
    /// tangent to roughly the chord length gives a natural arc; Layer 1 will
    /// expose this as a "smoothness" knob.
    /// </summary>
    public sealed class HermiteCurve
    {
        public readonly Vec3d P0, M0, P1, M1;
        public readonly double Length;

        private readonly double[] _t;     // parameter samples 0..1
        private readonly double[] _cum;   // cumulative arc length at each sample

        public HermiteCurve(Vec3d p0, Vec3d startTangent, Vec3d p1, Vec3d endTangent, int samples = 64)
        {
            if (samples < 2) samples = 2;
            P0 = p0; M0 = startTangent; P1 = p1; M1 = endTangent;

            _t = new double[samples + 1];
            _cum = new double[samples + 1];

            Vec3d prev = PointAt(0.0);
            _t[0] = 0.0; _cum[0] = 0.0;
            for (int k = 1; k <= samples; k++)
            {
                double t = (double)k / samples;
                Vec3d cur = PointAt(t);
                _t[k] = t;
                _cum[k] = _cum[k - 1] + (cur - prev).Length;
                prev = cur;
            }
            Length = _cum[samples];
        }

        /// <summary>Raw position at spline parameter t in [0,1].</summary>
        public Vec3d PointAt(double t)
        {
            double t2 = t * t, t3 = t2 * t;
            double h00 = 2 * t3 - 3 * t2 + 1;
            double h10 = t3 - 2 * t2 + t;
            double h01 = -2 * t3 + 3 * t2;
            double h11 = t3 - t2;
            return P0 * h00 + M0 * h10 + P1 * h01 + M1 * h11;
        }

        /// <summary>Unit tangent (travel direction) at spline parameter t.</summary>
        public Vec3d TangentAt(double t)
        {
            double t2 = t * t;
            double d00 = 6 * t2 - 6 * t;
            double d10 = 3 * t2 - 4 * t + 1;
            double d01 = -6 * t2 + 6 * t;
            double d11 = 3 * t2 - 2 * t;
            return (P0 * d00 + M0 * d10 + P1 * d01 + M1 * d11).Normalized();
        }

        /// <summary>
        /// Invert arc length: given a distance in metres along the curve,
        /// return the spline parameter t. This is what makes a train move at a
        /// real speed — raw t is NOT linear in distance, so we binary-search
        /// the cumulative table and linearly interpolate between entries.
        /// </summary>
        public double ParamAtDistance(double s)
        {
            if (s <= 0.0) return 0.0;
            if (s >= Length) return 1.0;

            int lo = 0, hi = _cum.Length - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_cum[mid] < s) lo = mid; else hi = mid;
            }
            double seg = _cum[hi] - _cum[lo];
            double frac = seg < 1e-12 ? 0.0 : (s - _cum[lo]) / seg;
            return _t[lo] + frac * (_t[hi] - _t[lo]);
        }

        /// <summary>World position at a distance (metres) along the curve.</summary>
        public Vec3d PositionAtDistance(double s) => PointAt(ParamAtDistance(s));

        /// <summary>Unit travel direction at a distance (metres) along the curve.</summary>
        public Vec3d HeadingAtDistance(double s) => TangentAt(ParamAtDistance(s));
    }
}
