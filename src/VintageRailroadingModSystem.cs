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
            api.RegisterEntityBehaviorClass("woodstorage", typeof(VintageRailroading.Entities.EntityBehaviorWoodStorage));
            api.RegisterItemClass("ItemTrainPlacer", typeof(VintageRailroading.Items.ItemTrainPlacer));
            api.RegisterItemClass("ItemTrackLayer", typeof(VintageRailroading.Items.ItemTrackLayer));
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
                .Create("vrrcouple")
                .WithDescription("Vintage Railroading: couple the two nearest trains (the rear one follows the front one).")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .HandleWith(args => OnVrrCouple(api, args));

            api.ChatCommands
                .Create("vrruncouple")
                .WithDescription("Vintage Railroading: uncouple the nearest train from its leader.")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .HandleWith(args => OnVrrUncouple(api, args));
        }

        private TextCommandResult OnVrrCouple(ICoreServerAPI api, TextCommandCallingArgs args)
        {
            var plr = args.Caller.Entity as EntityPlayer;
            if (plr == null) return TextCommandResult.Error("No player entity.");

            // Find the two nearest rail vehicles (trains OR cargo cars) to the player.
            var found = api.World.GetEntitiesAround(plr.Pos.XYZ, 12f, 12f,
                e => e is VintageRailroading.Entities.IRailVehicle && e.Alive);
            if (found == null || found.Length < 2)
                return TextCommandResult.Success("Need two rail vehicles within 12 blocks to couple. Found " + (found?.Length ?? 0) + ".");

            // Sort by distance to player; nearest two.
            System.Array.Sort(found, (a, b) =>
                a.Pos.XYZ.SquareDistanceTo(plr.Pos.XYZ).CompareTo(b.Pos.XYZ.SquareDistanceTo(plr.Pos.XYZ)));
            // Keep the Entity refs (for EntityId / Pos) and the IRailVehicle view (for the
            // coupling fields). Every IRailVehicle is an Entity, so both are the same object.
            var e0 = found[0]; var e1 = found[1];
            var t0 = e0 as VintageRailroading.Entities.IRailVehicle;
            var t1 = e1 as VintageRailroading.Entities.IRailVehicle;
            if (t0 == null || t1 == null) return TextCommandResult.Success("Could not resolve two rail vehicles.");

            // Decide leader/follower: the one further ALONG the track (greater combined
            // seg+dist ordering is ambiguous across segments), so we use a simple rule —
            // the train the OTHER is behind becomes leader. Here we just make the nearer
            // train the follower of the farther one; the player can re-run if reversed.
            var leader = t1; var leaderEnt = e1;
            var follower = t0; var followerEnt = e0;
            follower.LeaderEntityId = leaderEnt.EntityId;
            // Set the gap from their current spacing so they don't jump on couple.
            double gap = followerEnt.Pos.XYZ.DistanceTo(leaderEnt.Pos.XYZ);
            if (gap < 1.0 || gap > 20.0) gap = 6.0;
            follower.CouplingGap = gap;

            return TextCommandResult.Success(
                $"Coupled #{followerEnt.EntityId} behind #{leaderEnt.EntityId} at gap {gap:0.0}m. " +
                "Drive the leader and the follower will trail it. /vrruncouple to detach.");
        }

        private TextCommandResult OnVrrUncouple(ICoreServerAPI api, TextCommandCallingArgs args)
        {
            var plr = args.Caller.Entity as EntityPlayer;
            if (plr == null) return TextCommandResult.Error("No player entity.");

            var found = api.World.GetEntitiesAround(plr.Pos.XYZ, 12f, 12f,
                e => e is VintageRailroading.Entities.IRailVehicle && e.Alive
                     && ((VintageRailroading.Entities.IRailVehicle)e).LeaderEntityId != 0);
            if (found == null || found.Length == 0)
                return TextCommandResult.Success("No coupled rail vehicle within 12 blocks.");

            System.Array.Sort(found, (a, b) =>
                a.Pos.XYZ.SquareDistanceTo(plr.Pos.XYZ).CompareTo(b.Pos.XYZ.SquareDistanceTo(plr.Pos.XYZ)));
            var tEnt = found[0];
            var t = tEnt as VintageRailroading.Entities.IRailVehicle;
            if (t == null) return TextCommandResult.Success("Could not resolve a rail vehicle.");
            long was = t.LeaderEntityId;
            t.LeaderEntityId = 0;
            t.Speed = 0;
            return TextCommandResult.Success($"Uncoupled #{tEnt.EntityId} from leader #{was}.");
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
            EntityPlayer plr = args.Caller.Entity as EntityPlayer;
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
            EntityPlayer plr = args.Caller.Entity as EntityPlayer;
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

            // Grade as a fraction (rise/run). Clamp absurd slopes so the track stays
            // ride-able; railroads rarely exceed ~4%, but we allow up to 100% (45°).
            double grade = dy / horiz;
            const double MaxGrade = 1.0;
            bool clamped = false;
            if (grade > MaxGrade) { grade = MaxGrade; clamped = true; }
            else if (grade < -MaxGrade) { grade = -MaxGrade; clamped = true; }

            // Default tangents: horizontal direction from each end's look-yaw, with a
            // vertical component matching the chord's grade so the slope is smooth and
            // consistent across the whole segment (no mid-curve hump/dip).
            double tanScale = chord * 0.4;
            Vec3d dirA = new Vec3d(0,0,0).Ahead(1.0, 0, yawA);
            Vec3d dirB = new Vec3d(0,0,0).Ahead(1.0, 0, yawB);
            double hmA = Math.Sqrt(dirA.X * dirA.X + dirA.Z * dirA.Z);
            double hmB = Math.Sqrt(dirB.X * dirB.X + dirB.Z * dirB.Z);
            var m0 = new GVec(dirA.X*tanScale, grade*hmA*tanScale, dirA.Z*tanScale);
            var m1 = new GVec(dirB.X*tanScale, grade*hmB*tanScale, dirB.Z*tanScale);

            // Effective snap radius: 0 when snapping is toggled off.
            double snap = _snapEnabled ? VintageRailroading.Track.TrackNetwork.SnapDistance : 0.0;

            // SNAPPING: if an endpoint is within snap distance of an existing node,
            // snap to it AND inherit that node's tangent so the join is smooth.
            bool snappedStart = network.WasSnapped(a.X, a.Y, a.Z, snap);
            bool snappedEnd   = network.WasSnapped(b.X, b.Y, b.Z, snap);

            if (snappedStart)
            {
                var sn = network.FindOrCreateNode(a.X, a.Y, a.Z, snap);
                a = new GVec(sn.X, sn.Y, sn.Z);
                var inh = network.InheritedTangentAt(sn.Id);
                if (inh.HasValue)
                {
                    var t = Normalize(inh.Value);
                    m0 = new GVec(t.x * tanScale, t.y * tanScale, t.z * tanScale);
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
                    m1 = new GVec(t.x * tanScale, t.y * tanScale, t.z * tanScale);
                }
            }

            var segData = network.AddSegment(
                a.X, a.Y, a.Z, m0.X, m0.Y, m0.Z,
                b.X, b.Y, b.Z, m1.X, m1.Y, m1.Z,
                _gauge.Id, snap);

            var pos = new BlockPos((int)Math.Floor(a.X), (int)Math.Floor(a.Y), (int)Math.Floor(a.Z));
            Block block = api.World.GetBlock(new AssetLocation("vintagerailroading:railnode"));
            if (block == null)
                return LaySegmentResult.Fail("railnode block not found — is the asset loaded?");

            api.World.BlockAccessor.SetBlock(block.BlockId, pos);
            var be = api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityRailNode;
            if (be == null)
                return LaySegmentResult.Fail("Block entity did not attach — check entityClass registration.");

            be.SetCurve(a, m0, b, m1, _gauge.Id);
            api.ModLoader.GetModSystem<TrackNetworkManager>()?.BroadcastNetwork();

            string snapInfo = (snappedStart || snappedEnd)
                ? $" SNAPPED(start={snappedStart}, end={snappedEnd})"
                : " (new track, no snap)";
            double gradePct = grade * 100.0;
            string gradeInfo = Math.Abs(dy) < 0.05
                ? " grade=flat"
                : $" grade={gradePct:+0.#;-0.#}% (rise {dy:+0.0;-0.0}m)" + (clamped ? " [CLAMPED to 100%]" : "");

            return new LaySegmentResult
            {
                Ok = true,
                SegmentId = segData.Id,
                EndPos = b,
                Message = $"Segment #{segData.Id} placed. gauge={_gauge.Id}, chord={chord:0.0}m,{gradeInfo}, " +
                          $"nodes={network.Nodes.Count}, segments={network.Segments.Count}.{snapInfo}"
            };
        }

        private static (double x, double y, double z) Normalize((double x, double y, double z) v)
        {
            double len = Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            if (len < 1e-9) return (0, 0, 0);
            return (v.x / len, v.y / len, v.z / len);
        }

    }
}