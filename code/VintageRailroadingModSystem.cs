using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageRailroading.Blocks;
using VintageRailroading.Entities;
using GVec = VintageRailroading.Track.Vec3d;
using TrackGauge = VintageRailroading.Track.TrackGauge;

namespace VintageRailroading
{
    /// <summary>
    /// Layer 1a harness. Proves the block + block-entity + persistence path.
    ///
    ///   /vrrnode   -> 1st call sets point A; 2nd call places a 'railnode' block
    ///                 at A and writes the finished curve into its BlockEntity.
    ///   /vrrgauge  -> cycle the gauge used for the next curve.
    ///
    /// The placed block entity redraws its curve from PERSISTED data every 2s,
    /// so after /reload (or save+reopen) the trail reappearing proves the curve
    /// survived serialization.
    /// </summary>
    public class VintageRailroadingModSystem : ModSystem
    {
        private GVec _pendingPos;
        private double _pendingYaw;
        private double _pendingPitch;
        private bool _hasPending;
        private TrackGauge _gauge = TrackGauge.Standard;
        // When false, /vrrnode places endpoints exactly where you stand and never
        // fuses onto a nearby existing node — lets you lay tracks close together.
        private bool _snapEnabled = true;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            // Register on both sides so client and server agree on the type.
            api.RegisterBlockEntityClass("VintageRailroadingRailNode", typeof(BlockEntityRailNode));
            api.RegisterEntity("EntityTrain", typeof(EntityTrain));
            api.RegisterEntity("EntityCargo", typeof(EntityCargo));
            api.RegisterEntityBehaviorClass("fuelstorage", typeof(VintageRailroading.Entities.EntityBehaviorFuelStorage));
            api.RegisterEntityBehaviorClass("fluidstorage", typeof(VintageRailroading.Entities.EntityBehaviorFluidStorage));
            api.RegisterEntityBehaviorClass("fuelconsumer", typeof(VintageRailroading.Entities.EntityBehaviorFuelConsumer));
            api.RegisterEntityBehaviorClass("genericstorage", typeof(VintageRailroading.Entities.EntityBehaviorGenericStorage));
            api.RegisterEntityBehaviorClass("freezer", typeof(VintageRailroading.Entities.EntityBehaviorFreezer));
            api.RegisterEntityBehaviorClass("skinnable", typeof(VintageRailroading.Entities.EntityBehaviorSkinnable));
            api.RegisterItemClass("ItemTrainPlacer", typeof(VintageRailroading.Items.ItemTrainPlacer));
            api.RegisterItemClass("ItemTrackLayer", typeof(VintageRailroading.Items.ItemTrackLayer));
            api.RegisterItemClass("ItemCouplingTool", typeof(VintageRailroading.Items.ItemCouplingTool));
            api.RegisterItemClass("ItemSkinTool", typeof(VintageRailroading.Items.ItemSkinTool));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.ChatCommands
                .Create("vrrnode")
                .WithDescription("Vintage Railroading: place a rail node curve between two points.")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .HandleWith(args => OnVrrNode(api, args));

            api.ChatCommands
                .Create("vrrgauge")
                .WithDescription("Vintage Railroading: cycle the test gauge.")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .HandleWith(OnVrrGauge);

            api.ChatCommands
                .Create("vrrsnap")
                .WithDescription("Vintage Railroading: toggle endpoint snapping. Off = lay tracks close together without auto-joining.")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .HandleWith(OnVrrSnap);

            api.ChatCommands
                .Create("vrrswitch")
                .WithDescription("Vintage Railroading: cycle the switch at the nearest junction (sets which branch trains take through it).")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .HandleWith(args => OnVrrSwitch(api, args));

            api.ChatCommands
                .Create("vrrdebug")
                .WithDescription("Vintage Railroading: toggle verbose debug logging to the server/client log.")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(OnVrrDebug);
        }

        private TextCommandResult OnVrrDebug(TextCommandCallingArgs args)
        {
            VrrDebug.Enabled = !VrrDebug.Enabled;
            return TextCommandResult.Success(VrrDebug.Enabled
                ? "VRR debug logging ON — diagnostic [vrr] lines now write to 'vrr-debug.log' in your VS Logs folder. /vrrdebug again to turn off."
                : "VRR debug logging OFF.");
        }

        private TextCommandResult OnVrrSnap(TextCommandCallingArgs args)
        {
            _snapEnabled = !_snapEnabled;
            return TextCommandResult.Success(_snapEnabled
                ? $"Snapping ON — endpoints within {VintageRailroading.Track.TrackNetwork.SnapDistance:0.##} blocks of an existing node will join it."
                : "Snapping OFF — endpoints are placed exactly where you stand and will NOT auto-join. Use this to lay tracks close together. /vrrsnap again to re-enable.");
        }

        private TextCommandResult OnVrrSwitch(ICoreServerAPI api, TextCommandCallingArgs args)
        {
            EntityPlayer? plr = args.Caller.Entity as EntityPlayer;
            if (plr == null) return TextCommandResult.Error("No player entity.");

            var network = api.ModLoader.GetModSystem<TrackNetworkManager>().Network;
            Vec3d foot = plr.Pos.XYZ;

            var node = network.NearestJunction(foot.X, foot.Y, foot.Z, maxDist: 4.0);
            if (node == null)
                return TextCommandResult.Success("No junction (3+ track ends) within 4 blocks. Stand on the junction node and retry.");

            long selected = network.CycleSwitch(node.Id);
            if (selected == 0)
                return TextCommandResult.Success($"Junction #{node.Id} has too few exits to switch.");

            // Persist + push to clients (clients also run NextSegment when rendering
            // train motion, so they need the updated SelectedExit).
            api.ModLoader.GetModSystem<TrackNetworkManager>()?.BroadcastNetwork();

            return TextCommandResult.Success(
                $"Junction #{node.Id}: through-route set to segment #{selected}. " +
                "Trains traversing this node will now take that branch.");
        }

        private TextCommandResult OnVrrGauge(TextCommandCallingArgs args)
        {
            _gauge = _gauge.Id switch
            {
                "narrow"   => TrackGauge.Metre,
                "metre"    => TrackGauge.Standard,
                "standard" => TrackGauge.Broad,
                _          => TrackGauge.Narrow,
            };
            return TextCommandResult.Success($"Next curve gauge: {_gauge}");
        }

        private TextCommandResult OnVrrNode(ICoreServerAPI api, TextCommandCallingArgs args)
        {
            EntityPlayer? plr = args.Caller.Entity as EntityPlayer;
            if (plr == null) return TextCommandResult.Error("No player entity.");

            // Foot position (ground level), NOT eye height — track sits on the ground.
            Vec3d foot = plr.Pos.XYZ;
            double yaw = plr.Pos.Yaw;
            double pitch = plr.Pos.Pitch;

            if (!_hasPending)
            {
                _pendingPos = new GVec(foot.X, foot.Y, foot.Z);
                _pendingYaw = yaw;
                _pendingPitch = pitch;
                _hasPending = true;
                return TextCommandResult.Success("Point A set. Move to the END (any height — grades are allowed), look the way track should leave, then /vrrnode again.");
            }
            _hasPending = false;

            var a = _pendingPos;
            var b = new GVec(foot.X, foot.Y, foot.Z);
            var result = LaySegment(api, a, _pendingYaw, b, yaw);
            return result.Ok
                ? TextCommandResult.Success(result.Message)
                : TextCommandResult.Success(result.Message); // user-facing info either way
        }

        /// <summary>Result of laying one track segment.</summary>
        public struct LaySegmentResult
        {
            public bool Ok;
            public long SegmentId;
            public string Message;
            // The resolved END node world position — callers (e.g. the item) can use
            // this as the next segment's start so chained track joins seamlessly.
            public GVec EndPos;
            public static LaySegmentResult Fail(string msg) => new LaySegmentResult { Ok = false, Message = msg };
        }

        /// <summary>
        /// Lay ONE track segment from a to b, with each end's tangent derived from the
        /// given look-yaw and the chord grade. Handles snapping, tangent inheritance,
        /// network registration, render-anchor block placement, and client broadcast.
        /// Shared by the /vrrnode command and the track-layer item so both behave
        /// identically. Server-side only.
        /// </summary>
        public LaySegmentResult LaySegment(ICoreServerAPI api, GVec a, double yawA, GVec b, double yawB)
        {
            var network = api.ModLoader.GetModSystem<TrackNetworkManager>().Network;

            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double horiz = Math.Sqrt(dx * dx + dz * dz);   // ground-plane run
            double chord = Math.Sqrt(dx * dx + dy * dy + dz * dz); // true 3D length
            if (horiz < 0.5)
                return LaySegmentResult.Fail("Points too close horizontally — move apart and retry.");

            // HORIZONTAL (yaw/left-right) turn-rate limit: how sharply the track can
            // bend in the XZ-plane, in radians of heading change per horizontal meter.
            const double MaxYawRate = 0.03; // radians per meter (~1.7°/m)
            double deltaYaw = AngleDiff(yawA, yawB);
            double minHorizForThisTurn = Math.Abs(deltaYaw) / MaxYawRate;
            if (horiz < minHorizForThisTurn)
                return LaySegmentResult.Fail(
                    $"Curve too sharp for this distance — need at least {minHorizForThisTurn:0.0}m horizontal " +
                    $"for a {deltaYaw * 180.0 / Math.PI:0.#}° turn (max yaw rate {MaxYawRate} rad/m).");

            // HARD CAP on total heading change for a single segment, regardless of
            // distance. Prevents U-turn/hairpin reversals even if the player is
            // willing to place the segment over a very long distance — those should
            // be built as multiple segments with intermediate nodes instead.
            const double MaxTotalTurn = Math.PI * 0.75; // 135°
            if (Math.Abs(deltaYaw) > MaxTotalTurn)
                return LaySegmentResult.Fail(
                    $"Turn too sharp overall — {deltaYaw * 180.0 / Math.PI:0.#}° exceeds the " +
                    $"{MaxTotalTurn * 180.0 / Math.PI:0.#}° max per segment, regardless of distance. " +
                    $"Break this into multiple segments with intermediate nodes.");

            // chordYaw is the straight-line heading a -> b in the XZ-plane. Since
            // every segment is now laid straight along its chord, chordYaw IS this
            // segment's heading at both ends. It is used below for the junction
            // smoothness check at snapped joints.
            double chordYaw = Math.Atan2(dx, dz);

            // VERTICAL (incline/decline) grade limit: rise/run, independent of yaw.
            // NOTE: endpoints a and b are fixed where the player stands, so the actual
            // slope is dy/horiz no matter what — this clamp is ADVISORY: it flags an
            // over-steep placement in the result message ("[CLAMPED to 10%]") so the
            // player knows to place the end at a gentler height. It does not bend the
            // geometry (that would require moving an endpoint).
            // VERTICAL (incline/decline) grade limit: rise/run, independent of yaw.
            // ENFORCED (not advisory): if the player places the end too high/low for the
            // horizontal run, we REJECT the segment and tell them the minimum run needed,
            // exactly like the yaw-rate check above. This is what actually keeps slopes
            // gentle — the old advisory clamp left the geometry as steep as placed and only
            // printed a warning. Lowered to 5% so up/down grades are noticeably less steep.
            double grade = dy / horiz;
            const double MaxGrade = 0.05; // 5% — gentler than the old 10%
            if (Math.Abs(grade) > MaxGrade)
            {
                double minRun = Math.Abs(dy) / MaxGrade;
                return LaySegmentResult.Fail(
                    $"Slope too steep — {grade * 100.0:+0.#;-0.#}% grade over {horiz:0.0}m. " +
                    $"For a {dy:+0.0;-0.0}m rise you need at least {minRun:0.0}m of horizontal run " +
                    $"(max grade {MaxGrade * 100.0:0}%). Move the end point further away or less high/low.");
            }

            // STRAIGHT-SEGMENT TANGENTS: both endpoint tangents are set to the chord
            // direction (a -> b). This makes the Hermite curve a straight line between
            // the two points — no bow, no overshoot, no possibility of a loop, and no
            // dependence on inherited-tangent sign conventions. Curves are produced by
            // laying many short straight segments that each step the heading slightly
            // (the per-meter yaw-rate limit above governs how sharply that can happen).
            //
            // Tangent magnitude for a Hermite straight line should equal the chord
            // length so the parameterization stays even; using chord keeps arc-length
            // sampling well-behaved.
            double chordDirX = chord > 1e-9 ? dx / chord : 0.0;
            double chordDirY = chord > 1e-9 ? dy / chord : 0.0;
            double chordDirZ = chord > 1e-9 ? dz / chord : 0.0;
            double tanScale = chord;
            var m0 = new GVec(chordDirX * tanScale, chordDirY * tanScale, chordDirZ * tanScale);
            var m1 = new GVec(chordDirX * tanScale, chordDirY * tanScale, chordDirZ * tanScale);

            // BANKING / SUPERELEVATION: any yaw change tilts the rail cross-section
            // toward the inside of the curve, proportional to how sharp the turn is
            // relative to the max allowed yaw rate, capped at MaxBankAngle.
            const double MaxBankAngle = 0.12; // radians (~6.9°), max rail tilt
            double yawUsage = Math.Min(Math.Abs(deltaYaw) / horiz / MaxYawRate, 1.0); // 0..1
            double bankFraction = yawUsage;
            double bankAngle = Math.Sign(deltaYaw) * bankFraction * MaxBankAngle;

            // Effective snap radius: 0 when snapping is toggled off.
            double snap = _snapEnabled ? VintageRailroading.Track.TrackNetwork.SnapDistance : 0.0;

            // SNAPPING: if an endpoint is within snap distance of an existing node,
            // snap to it AND inherit that node's tangent so the join is smooth.
            bool snappedStart = network.WasSnapped(a.X, a.Y, a.Z, snap);
            bool snappedEnd   = network.WasSnapped(b.X, b.Y, b.Z, snap);

            // JUNCTION SMOOTHNESS: because every segment is laid straight along its
            // chord (m0 = m1 = chord direction), this segment's heading at BOTH ends
            // is simply chordYaw. At a snapped joint we compare the existing track's
            // "continue forward" direction against chordYaw and reject if the kink is
            // too sharp — this is purely a guard against starting a new straight piece
            // that bends hard away from the track it joins. We do NOT rewrite m0/m1:
            // the segment stays straight. Any curve is built by laying several short
            // straight segments that each step the heading within the yaw-rate limit.
            //
            // InheritedTangentAt returns the "continue forward" direction pointing
            // AWAY from the node, already normalized for which end of the existing
            // segment this node was (see TrackNetwork.cs), so no negation is needed:
            // Atan2(t.x, t.z) matches the chordYaw/Ahead convention.
            const double MaxJunctionAngle = 0.3; // radians (~17°)

            if (snappedStart)
            {
                var sn = network.FindOrCreateNode(a.X, a.Y, a.Z, snap);
                a = new GVec(sn.X, sn.Y, sn.Z);
                var inh = network.InheritedTangentAt(sn.Id);
                if (inh.HasValue)
                {
                    var t = Normalize(inh.Value);
                    double inheritedYaw = Math.Atan2(t.x, t.z);
                    double junctionAngle = Math.Abs(AngleDiff(inheritedYaw, chordYaw));
                    if (junctionAngle > MaxJunctionAngle)
                        return LaySegmentResult.Fail(
                            $"Sharp bend at start node — heading changes by {junctionAngle * 180.0 / Math.PI:0.#}° " +
                            $"at the junction (max {MaxJunctionAngle * 180.0 / Math.PI:0.#}°). " +
                            $"Aim closer to the existing track's direction before laying this segment.");
                    // Accepted — segment stays straight, m0 already = chord direction.
                }
            }
            if (snappedEnd)
            {
                var en = network.FindOrCreateNode(b.X, b.Y, b.Z, snap);
                b = new GVec(en.X, en.Y, en.Z);
                var inh = network.InheritedTangentAt(en.Id);
                if (inh.HasValue)
                {
                    var t = Normalize(inh.Value);
                    double inheritedYaw = Math.Atan2(t.x, t.z);
                    // At the END node we are arriving, so the existing track's
                    // away-from-node direction is opposite our direction of travel;
                    // compare against the reverse of chordYaw.
                    double arrivalYaw = chordYaw + Math.PI;
                    double junctionAngle = Math.Abs(AngleDiff(inheritedYaw, arrivalYaw));
                    if (junctionAngle > MaxJunctionAngle)
                        return LaySegmentResult.Fail(
                            $"Sharp bend at end node — heading changes by {junctionAngle * 180.0 / Math.PI:0.#}° " +
                            $"at the junction (max {MaxJunctionAngle * 180.0 / Math.PI:0.#}°). " +
                            $"Aim closer to the existing track's direction before laying this segment.");
                    // Accepted — segment stays straight, m1 already = chord direction.
                }
            }

            var segData = network.AddSegment(
                a.X, a.Y, a.Z, m0.X, m0.Y, m0.Z,
                b.X, b.Y, b.Z, m1.X, m1.Y, m1.Z,
                _gauge.Id, snap);

            var pos = new BlockPos((int)Math.Floor(a.X), (int)Math.Floor(a.Y), (int)Math.Floor(a.Z));
            Block? block = api.World.GetBlock(new AssetLocation("vintagerailroading:railnode"));
            if (block == null)
                return LaySegmentResult.Fail("railnode block not found — is the asset loaded?");

            api.World.BlockAccessor.SetBlock(block.BlockId, pos);
            var be = api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityRailNode;
            if (be == null)
                return LaySegmentResult.Fail("Block entity did not attach — check entityClass registration.");

            be.SetCurve(a, m0, b, m1, _gauge.Id);
            be.SetBankAngle(bankAngle);
            api.ModLoader.GetModSystem<TrackNetworkManager>()?.BroadcastNetwork();

            string snapInfo = (snappedStart || snappedEnd)
                ? $" SNAPPED(start={snappedStart}, end={snappedEnd})"
                : " (new track, no snap)";
            double gradePct = grade * 100.0;
            string gradeInfo = Math.Abs(dy) < 0.05
                ? " grade=flat"
                : $" grade={gradePct:+0.#;-0.#}% (rise {dy:+0.0;-0.0}m)";
            string bankInfo = Math.Abs(bankAngle) < 0.001
                ? " bank=flat"
                : $" bank={bankAngle * 180.0 / Math.PI:+0.0;-0.0}° ({(bankAngle > 0 ? "left" : "right")} side raised)";

            return new LaySegmentResult
            {
                Ok = true,
                SegmentId = segData.Id,
                EndPos = b,
                Message = $"Segment #{segData.Id} placed. gauge={_gauge.Id}, chord={chord:0.0}m,{gradeInfo},{bankInfo}, " +
                        $"turn={deltaYaw * 180.0 / Math.PI:0.#}° over {horiz:0.0}m, " +
                        $"nodes={network.Nodes.Count}, segments={network.Segments.Count}.{snapInfo}"
            };
        }

        private static double AngleDiff(double yawA, double yawB)
        {
            double diff = (yawB - yawA) % (2 * Math.PI);
            if (diff > Math.PI) diff -= 2 * Math.PI;
            else if (diff < -Math.PI) diff += 2 * Math.PI;
            return diff;
        }

        private static (double x, double y, double z) Normalize((double x, double y, double z) v)
        {
            double len = Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            if (len < 1e-9) return (0, 0, 0);
            return (v.x / len, v.y / len, v.z / len);
        }

    }
}