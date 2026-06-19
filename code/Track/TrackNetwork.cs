using System;
using System.Collections.Generic;
using ProtoBuf;

namespace VintageRailroading.Track
{
    // ---------------------------------------------------------------------
    // Layer 2a — Track Network (topology). Serializable via VS SerializerUtil
    // (ProtoBuf). Holds nodes + segments for the whole world; persisted once to
    // the savegame, not per block. Geometry (HermiteCurve) is rebuilt from the
    // stored endpoints/tangents on demand.
    //
    // Snapping + junction logic validated in tests/proto_network.py.
    // ---------------------------------------------------------------------

    /// <summary>A connection point. A node with 3+ connections is a junction.</summary>
    [ProtoContract]
    public class TrackNode
    {
        [ProtoMember(1)] public long Id;
        [ProtoMember(2)] public double X;
        [ProtoMember(3)] public double Y;
        [ProtoMember(4)] public double Z;
        // segmentId -> which end ("start"/"end") connects here
        [ProtoMember(5)] public List<NodeConnection> Connections = new List<NodeConnection>();

        // Junction/switch state: the SegmentId a train should take when traversing
        // THROUGH this node (i.e. the through-route the switch is set to). 0 = unset,
        // meaning NextSegment falls back to the smoothest exit. Only meaningful when
        // this node has 3+ connections; ignored for plain end-to-end joins.
        [ProtoMember(6)] public long SelectedExit;

        public TrackNode() { }
        public TrackNode(long id, double x, double y, double z) { Id = id; X = x; Y = y; Z = z; }
    }

    [ProtoContract]
    public class NodeConnection
    {
        [ProtoMember(1)] public long SegmentId;
        [ProtoMember(2)] public bool IsStart; // true = this node is the segment's start
        public NodeConnection() { }
        public NodeConnection(long segId, bool isStart) { SegmentId = segId; IsStart = isStart; }
    }

    /// <summary>A segment between two nodes, with its curve tangents + gauge.</summary>
    [ProtoContract]
    public class TrackSegmentData
    {
        [ProtoMember(1)] public long Id;
        [ProtoMember(2)] public long StartNodeId;
        [ProtoMember(3)] public long EndNodeId;
        // start tangent
        [ProtoMember(4)] public double TsX;
        [ProtoMember(5)] public double TsY;
        [ProtoMember(6)] public double TsZ;
        // end tangent
        [ProtoMember(7)] public double TeX;
        [ProtoMember(8)] public double TeY;
        [ProtoMember(9)] public double TeZ;
        [ProtoMember(10)] public string GaugeId = "standard";
        public TrackSegmentData() { }
    }

    /// <summary>The whole world's track graph. The root serialized object.
    /// Uses Lists (not Dictionaries) for robust protobuf-net serialization;
    /// runtime lookup dictionaries are rebuilt from the lists after load.</summary>
    [ProtoContract]
    public class TrackNetwork
    {
        [ProtoMember(1)] public List<TrackNode> NodeList = new List<TrackNode>();
        [ProtoMember(2)] public List<TrackSegmentData> SegmentList = new List<TrackSegmentData>();
        [ProtoMember(3)] public long NextNodeId = 1;
        [ProtoMember(4)] public long NextSegmentId = 1;

        // Not serialized — rebuilt from the lists.
        private Dictionary<long, TrackNode> _nodeIdx = null!;
        private Dictionary<long, TrackSegmentData> _segIdx = null!;

        // Auto-join radius. Endpoints within this many blocks of an existing node
        // fuse onto it. Kept tight (0.75) so deliberately-parallel/close tracks stay
        // separate; raising it makes joining easier but collapses nearby tracks.
        public const double SnapDistance = 0.75;

        /// <summary>Rebuild runtime lookup after deserialization (call once on load).</summary>
        public void BuildIndex()
        {
            _nodeIdx = new Dictionary<long, TrackNode>();
            foreach (var n in NodeList) _nodeIdx[n.Id] = n;
            _segIdx = new Dictionary<long, TrackSegmentData>();
            foreach (var s in SegmentList) _segIdx[s.Id] = s;
        }

        private void EnsureIndex()
        {
            if (_nodeIdx == null || _segIdx == null) BuildIndex();
        }

        public IReadOnlyList<TrackSegmentData> Segments => SegmentList;
        public IReadOnlyList<TrackNode> Nodes => NodeList;

        /// <summary>Find nearest node within snap distance, or create a new one.</summary>
        public TrackNode FindOrCreateNode(double x, double y, double z, double snap = SnapDistance)
        {
            EnsureIndex();
            TrackNode? best = null;
            double bestD = snap;
            foreach (var n in NodeList)
            {
                double d = Math.Sqrt((n.X - x) * (n.X - x) + (n.Y - y) * (n.Y - y) + (n.Z - z) * (n.Z - z));
                if (d <= bestD) { best = n; bestD = d; }
            }
            if (best != null) return best;

            var node = new TrackNode(NextNodeId++, x, y, z);
            NodeList.Add(node);
            _nodeIdx[node.Id] = node;
            return node;
        }

        public bool WasSnapped(double x, double y, double z, double snap = SnapDistance)
        {
            foreach (var n in NodeList)
            {
                double d = Math.Sqrt((n.X - x) * (n.X - x) + (n.Y - y) * (n.Y - y) + (n.Z - z) * (n.Z - z));
                if (d <= snap) return true;
            }
            return false;
        }

        /// <summary>Create a segment between two world points, snapping endpoints
        /// to existing nodes where possible.</summary>
        public TrackSegmentData AddSegment(
            double sx, double sy, double sz, double tsx, double tsy, double tsz,
            double ex, double ey, double ez, double tex, double tey, double tez,
            string gaugeId, double snap = SnapDistance)
        {
            EnsureIndex();
            var sNode = FindOrCreateNode(sx, sy, sz, snap);
            var eNode = FindOrCreateNode(ex, ey, ez, snap);

            var seg = new TrackSegmentData
            {
                Id = NextSegmentId++,
                StartNodeId = sNode.Id,
                EndNodeId = eNode.Id,
                TsX = tsx, TsY = tsy, TsZ = tsz,
                TeX = tex, TeY = tey, TeZ = tez,
                GaugeId = gaugeId
            };
            SegmentList.Add(seg);
            _segIdx[seg.Id] = seg;
            sNode.Connections.Add(new NodeConnection(seg.Id, true));
            eNode.Connections.Add(new NodeConnection(seg.Id, false));
            return seg;
        }

        /// <summary>Heading of an existing segment AT the given node, expressed as a
        /// "continue forward" direction that points AWAY from the node (i.e. the
        /// direction a new segment leaving this node should head to stay tangent).
        /// Null if no connections.
        ///
        /// Both endpoint tangents are stored pointing along the segment's travel
        /// direction (start->end). At the segment's END node that travel direction
        /// already points away from the node, so it is returned as-is. At the
        /// segment's START node the travel direction points INTO the segment (toward
        /// the node's far end), so it is negated to face away from the node. This
        /// makes the returned vector's meaning independent of which end the caller
        /// snapped to — callers must NOT apply their own negation.</summary>
        public (double x, double y, double z)? InheritedTangentAt(long nodeId)
        {
            EnsureIndex();
            if (!_nodeIdx.TryGetValue(nodeId, out var node) || node.Connections.Count == 0)
                return null;
            var c = node.Connections[0];
            if (!_segIdx.TryGetValue(c.SegmentId, out var seg)) return null;
            if (c.IsStart) return (-seg.TsX, -seg.TsY, -seg.TsZ);
            return (seg.TeX, seg.TeY, seg.TeZ);
        }

        /// <summary>
        /// Node traversal: a train running off one end of a segment continues onto a
        /// connected segment at the shared node. Given the current segment and which
        /// end it is leaving (leavingEnd = true: leaving via the EndNode; false: via
        /// the StartNode), returns the next segment to ride, the distance along it to
        /// enter at, and the speed-direction sign (+1 forward, -1 backward) so motion
        /// continues smoothly. Returns null if the node is an endpoint (no other
        /// segment) or a junction with no straight-through choice.
        /// </summary>
        public (long nextSegmentId, double entryDistanceFraction, int dirSign)? NextSegment(long currentSegmentId, bool leavingEnd)
        {
            EnsureIndex();
            var seg = GetSegment(currentSegmentId);
            if (seg == null) return null;

            long nodeId = leavingEnd ? seg.EndNodeId : seg.StartNodeId;
            var node = GetNode(nodeId);
            if (node == null) return null;

            // Gather all candidate exits (every connection except the one we arrived on).
            var candidates = new List<NodeConnection>();
            foreach (var c in node.Connections)
            {
                if (c.SegmentId == currentSegmentId) continue;
                if (GetSegment(c.SegmentId) == null) continue;
                candidates.Add(c);
            }

            if (candidates.Count == 0) return null;            // true endpoint -> stop
            NodeConnection chosen;
            if (candidates.Count == 1)
            {
                chosen = candidates[0];                         // plain end-to-end join
            }
            else
            {
                // Junction: honour the switch setting if it points at a valid exit,
                // else fall back to the smoothest (best-aligned) branch.
                NodeConnection? picked = null;
                if (node.SelectedExit != 0)
                {
                    foreach (var c in candidates)
                        if (c.SegmentId == node.SelectedExit) { picked = c; break; }
                }
                chosen = picked ?? SmoothestExit(node, seg, leavingEnd, candidates);
            }

            // If this node is the NEXT segment's start (chosen.IsStart), the train
            // enters at distance 0 going forward (+1). If it's the next segment's end,
            // it enters at the far end going backward (-1).
            if (chosen.IsStart) return (chosen.SegmentId, 0.0, +1);
            return (chosen.SegmentId, 1.0, -1); // 1.0 = fraction of length; caller scales
        }

        /// <summary>Of several junction exits, pick the one whose direction best
        /// continues the train's incoming heading (straightest through-route). This
        /// is the default when no switch is set and keeps motion smooth across the
        /// node seam.</summary>
        private NodeConnection SmoothestExit(TrackNode node, TrackSegmentData arriving, bool leavingEnd, List<NodeConnection> candidates)
        {
            // Direction the train is travelling AS IT ARRIVES at the node. Leaving via
            // the segment's end means we travelled along its end tangent (forward);
            // leaving via its start means we travelled against its start tangent.
            var inDir = leavingEnd
                ? Norm(arriving.TeX, arriving.TeY, arriving.TeZ)
                : Norm(-arriving.TsX, -arriving.TsY, -arriving.TsZ);

            NodeConnection best = candidates[0];
            double bestDot = double.NegativeInfinity;
            foreach (var c in candidates)
            {
                var nxt = GetSegment(c.SegmentId);
                if (nxt == null) continue;
                // Direction the train would travel as it LEAVES the node onto nxt.
                // Entering at nxt's start -> travel along its start tangent (forward).
                // Entering at nxt's end -> travel against its end tangent (backward).
                var outDir = c.IsStart
                    ? Norm(nxt.TsX, nxt.TsY, nxt.TsZ)
                    : Norm(-nxt.TeX, -nxt.TeY, -nxt.TeZ);
                double dot = inDir.x * outDir.x + inDir.y * outDir.y + inDir.z * outDir.z;
                if (dot > bestDot) { bestDot = dot; best = c; }
            }
            return best;
        }

        private static (double x, double y, double z) Norm(double x, double y, double z)
        {
            double l = Math.Sqrt(x * x + y * y + z * z);
            if (l < 1e-9) return (0, 0, 0);
            return (x / l, y / l, z / l);
        }

        /// <summary>True if the node is a junction (3+ connected segment ends).</summary>
        public bool IsJunction(long nodeId)
        {
            var n = GetNode(nodeId);
            return n != null && n.Connections.Count >= 3;
        }

        /// <summary>Set a junction's through-route to a specific exit segment. Pass 0
        /// to clear (revert to smoothest-exit default). Returns false if invalid.</summary>
        public bool SetSwitch(long nodeId, long exitSegmentId)
        {
            var n = GetNode(nodeId);
            if (n == null) return false;
            if (exitSegmentId != 0)
            {
                bool ok = false;
                foreach (var c in n.Connections) if (c.SegmentId == exitSegmentId) { ok = true; break; }
                if (!ok) return false;
            }
            n.SelectedExit = exitSegmentId;
            return true;
        }

        /// <summary>Cycle a junction's switch to its next exit branch (the segment ids
        /// connected to it, in ascending order). Returns the newly selected segment id,
        /// or 0 if the node has fewer than 2 exits.</summary>
        public long CycleSwitch(long nodeId)
        {
            var n = GetNode(nodeId);
            if (n == null) return 0;
            var ids = new List<long>();
            foreach (var c in n.Connections) ids.Add(c.SegmentId);
            ids.Sort();
            if (ids.Count < 2) return 0;

            int cur = ids.IndexOf(n.SelectedExit); // -1 if unset
            long next = ids[(cur + 1) % ids.Count];
            n.SelectedExit = next;
            return next;
        }

        /// <summary>Nearest junction node to a world point within maxDist, or null.</summary>
        public TrackNode? NearestJunction(double x, double y, double z, double maxDist)
        {
            EnsureIndex();
            TrackNode? best = null;
            double bestD = maxDist;
            foreach (var n in NodeList)
            {
                if (n.Connections.Count < 3) continue;
                double d = Math.Sqrt((n.X - x) * (n.X - x) + (n.Y - y) * (n.Y - y) + (n.Z - z) * (n.Z - z));
                if (d <= bestD) { best = n; bestD = d; }
            }
            return best;
        }

        /// <summary>
        /// Walk along the connected track from (segId, distance) by a SIGNED arc-length
        /// offset (metres). Positive = in the +distance direction of the current segment;
        /// negative = the -distance direction. Crosses node boundaries using the same
        /// traversal rules as a moving train (NextSegment), so a follower car stays glued
        /// to the path around curves and through junctions. Clamps at true endpoints.
        ///
        /// Returns the resulting (segId, distance). `geomLength` is a callback that
        /// returns a segment's arc length (the caller already builds geometry, so we let
        /// it supply lengths rather than rebuilding here). hitEnd is set true if we ran
        /// into a dead-end before consuming the whole offset.
        /// </summary>
        public (long segId, double distance) Offset(
            long segId, double distance, double offset,
            System.Func<long, double> geomLength, out bool hitEnd)
        {
            hitEnd = false;
            EnsureIndex();

            double len = geomLength(segId);
            if (len <= 0) { hitEnd = true; return (segId, distance); }

            // Direction we're stepping along the CURRENT segment: +1 toward the end.
            int dir = offset >= 0 ? +1 : -1;
            double remaining = System.Math.Abs(offset);

            // Guard against pathological loops (e.g. a tiny ring) — cap the hops.
            int safety = 0;
            const int MaxHops = 256;

            while (remaining > 1e-9 && safety++ < MaxHops)
            {
                len = geomLength(segId);
                if (len <= 0) { hitEnd = true; break; }

                // How far to the relevant end of this segment in our step direction.
                double room = dir > 0 ? (len - distance) : distance;

                if (remaining <= room)
                {
                    distance += dir * remaining;
                    remaining = 0;
                    break;
                }

                // Consume the rest of this segment and cross the node.
                remaining -= room;
                bool leavingEnd = dir > 0;            // ran off the end vs the start
                var next = NextSegment(segId, leavingEnd);
                if (!next.HasValue)
                {
                    // Dead end: clamp here.
                    distance = dir > 0 ? len : 0;
                    hitEnd = true;
                    break;
                }

                var info = next.Value;
                segId = info.nextSegmentId;
                double nlen = geomLength(segId);
                // entryDistanceFraction ~0 => entered at new seg's start, continue toward
                // its end (dir +1, distance 0). ~1 => entered at the end (dir -1, dist len).
                if (info.entryDistanceFraction < 0.5)
                {
                    distance = 0;
                    dir = +1;
                }
                else
                {
                    distance = nlen;
                    dir = -1;
                }
            }

            // Clamp for safety.
            double finalLen = geomLength(segId);
            if (distance < 0) distance = 0;
            if (finalLen > 0 && distance > finalLen) distance = finalLen;
            return (segId, distance);
        }

        public TrackNode? GetNode(long id)
        {
            EnsureIndex();
            return _nodeIdx.TryGetValue(id, out var n) ? n : null;
        }

        public TrackSegmentData? GetSegment(long id)
        {
            EnsureIndex();
            return _segIdx.TryGetValue(id, out var s) ? s : null;
        }

        /// <summary>Build runtime curve geometry (Layer 0 TrackSegment) for a stored
        /// segment id, so a train can sample positions/headings along it. Returns
        /// null if the segment or its nodes are missing.</summary>
        public TrackSegment? BuildGeometry(long segmentId)
        {
            var seg = GetSegment(segmentId);
            if (seg == null) return null;
            var sn = GetNode(seg.StartNodeId);
            var en = GetNode(seg.EndNodeId);
            if (sn == null || en == null) return null;

            var gauge = GaugeFromId(seg.GaugeId);
            var curve = new HermiteCurve(
                new Vec3d(sn.X, sn.Y, sn.Z),
                new Vec3d(seg.TsX, seg.TsY, seg.TsZ),
                new Vec3d(en.X, en.Y, en.Z),
                new Vec3d(seg.TeX, seg.TeY, seg.TeZ));
            return new TrackSegment(seg.Id, gauge, curve);
        }

        private static TrackGauge GaugeFromId(string id)
        {
            switch (id)
            {
                case "narrow": return TrackGauge.Narrow;
                case "metre":  return TrackGauge.Metre;
                case "broad":  return TrackGauge.Broad;
                default:        return TrackGauge.Standard;
            }
        }
    }
}