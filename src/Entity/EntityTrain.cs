using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageRailroading.Track;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// A train that rides the track NETWORK. Real state is 1D:
    /// (segment id, distance along it) + speed. Each server tick it advances
    /// distance, projects onto the segment's Hermite curve, and sets Pos.
    ///
    /// Mounting: extends EntityBoat to inherit its proven seat/IMountable machinery
    /// (the generic creaturecarrier->EntityBehaviorSeatable path NREs in CreateSeat
    /// for a bare EntityAgent; EntityBoat provides what CreateSeat needs). We do NOT
    /// add EntityBoat's water physics behaviors, so no floating; our OnGameTick
    /// override drives position along the spline instead.
    /// </summary>
    public class EntityTrain : EntityBoat
    {
        public long SegmentId
        {
            get => WatchedAttributes.GetLong("vrrSegId", 0);
            set => WatchedAttributes.SetLong("vrrSegId", value);
        }
        public double Distance
        {
            get => WatchedAttributes.GetDouble("vrrDist", 0);
            set => WatchedAttributes.SetDouble("vrrDist", value);
        }
        public double Speed // metres/sec along the track
        {
            get => WatchedAttributes.GetDouble("vrrSpeed", 0);
            set => WatchedAttributes.SetDouble("vrrSpeed", value);
        }

        /// <summary>Entity id of the car this one is coupled BEHIND (its leader). 0 =
        /// not coupled (this car is a leader / standalone, reads throttle normally).
        /// A coupled car ignores throttle and instead tracks `CouplingGap` metres behind
        /// its leader along the same path each tick.</summary>
        public long LeaderEntityId
        {
            get => WatchedAttributes.GetLong("vrrLeader", 0);
            set => WatchedAttributes.SetLong("vrrLeader", value);
        }
        /// <summary>Arc-length gap (metres) a coupled car keeps behind its leader.</summary>
        public double CouplingGap
        {
            get => WatchedAttributes.GetDouble("vrrGap", 6.0);
            set => WatchedAttributes.SetDouble("vrrGap", value);
        }

        // Per-type top speed (m/s), read from the entity JSON's `attributes.maxSpeed`
        // so each train type (loco, coal cart, etc.) can differ WITHOUT a new class.
        // Defaults to 6.0 so the original locomotive is unchanged. Cached after init.
        private double _maxSpeed = 6.0;
        private double MaxSpeed => _maxSpeed;

        private TrackNetwork _network;
        private TrackSegment _geom;
        private long _geomForSegId = -1;
        private float _diagAccum;

        // CLIENT-SIDE DEAD RECKONING: the server advances Distance once per tick (~15 Hz);
        // the client renders at frame rate (60+ Hz). To move smoothly between the sparse
        // synced positions, the client keeps its OWN local distance and integrates it from
        // the synced Speed every frame, softly correcting toward the authoritative synced
        // Distance whenever a fresh server value arrives. _clientDist is that local value;
        // _clientDistInit tracks whether we've seeded it yet.
        private double _clientDist;
        private long _clientSegId = -1;
        private bool _clientInit;

        public override void Initialize(EntityProperties properties, ICoreAPI api, long inChunkIndex3d)
        {
            requirePosesOnServer = true;

            // Per-type tuning from entity JSON `attributes` (no new class needed).
            if (properties?.Attributes != null)
            {
                _maxSpeed = properties.Attributes["maxSpeed"].AsDouble(6.0);
            }

            // Log behavior codes only (no JSON braces — they break the logger's
            // format string, which caused the earlier "Couldn't write to log file"
            // FormatException). Use the {0} overload so any stray braces are safe.
            try
            {
                var sb = new System.Text.StringBuilder();
                var bcfg = properties?.Server?.BehaviorsAsJsonObj;
                if (bcfg != null)
                {
                    foreach (var b in bcfg) { sb.Append(b["code"].AsString("?")); sb.Append(' '); }
                }
                api.Logger.Notification("[vintagerailroading] Initialize start. server behaviors: {0}", sb.ToString());
            }
            catch (System.Exception ex)
            {
                api.Logger.Notification("[vintagerailroading] behavior dump failed: {0} {1}", ex.GetType().Name, ex.Message);
            }

            api.Logger.Notification("[vintagerailroading] calling base.Initialize (EntityBoat)...");
            base.Initialize(properties, api, inChunkIndex3d);
            api.Logger.Notification("[vintagerailroading] base.Initialize returned OK.");

            var mgr = api.ModLoader.GetModSystem<TrackNetworkManager>();
            _network = mgr?.Network;
            api.Logger.Notification("[vintagerailroading] Initialize complete. network={0}", _network != null ? "OK" : "NULL");
        }

        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vintagestory.API.MathTools.Vec3d hitPosition, EnumInteractMode mode)
        {
            // WRENCH PICKUP: if the player right-clicks the train holding a wrench,
            // pick it up back into the inventory instead of mounting. Server-side only
            // (it mutates the world + inventory). We match any held item whose code
            // contains "wrench" so it works with Vintage Engineering's wrench or any
            // modded one without hard-depending on a specific mod being loaded.
            if (World.Side == EnumAppSide.Server && mode == EnumInteractMode.Interact
                && IsWrench(itemslot) && TryPickup(byEntity))
            {
                return; // handled; do NOT fall through to mounting
            }

            // FUEL STORAGE: empty-hand right-click opens the cart's fuel-only storage
            // (if this entity type has the behavior). Runs on BOTH sides: server marks
            // the inventory open for sync, client opens the dialog. Takes priority over
            // mounting so you can load the cart without sitting in it.
            if (mode == EnumInteractMode.Interact && (itemslot == null || itemslot.Empty))
            {
                var storage = GetBehavior<VintageRailroading.Entities.EntityBehaviorFuelStorage>();
                if (storage != null)
                {
                    var plr = (byEntity as EntityPlayer)?.Player;
                    if (plr != null && storage.OpenFor(plr))
                    {
                        return; // handled; do not fall through to mounting
                    }
                }
            }

            World.Logger.Notification("[vrr] {0} OnInteract by={1} mode={2} hitY={3:0.0}",
                World.Side, byEntity?.GetType().Name, mode, hitPosition?.Y);
            base.OnInteract(byEntity, itemslot, hitPosition, mode);
            // Report mount state right after base handled it.
            var mnt = GetInterface<IMountable>();
            World.Logger.Notification("[vrr] {0} OnInteract post: anyMounted={1}",
                World.Side, mnt != null && mnt.AnyMounted());
        }

        private static bool IsWrench(ItemSlot slot)
        {
            if (slot == null || slot.Empty) return false;
            string path = slot.Itemstack?.Collectible?.Code?.Path;
            return path != null && path.Contains("wrench");
        }

        /// <summary>Pick the train up into the player's inventory: first EJECT any
        /// riders (so nobody is left stuck in a seat on a despawned entity), then give
        /// back a 'trainplacer' item and despawn the train. Returns true if handled.</summary>
        private bool TryPickup(EntityAgent byEntity)
        {
            // ORDER MATTERS: unmount every passenger BEFORE Die(). If we despawn while a
            // player is still seated, their MountedOn points at a seat on a now-dead
            // entity and they get stuck. Unmounting first clears that reference cleanly.
            foreach (var mountable in GetInterfaces<IMountable>())
            {
                if (mountable?.Seats == null) continue;
                foreach (var seat in mountable.Seats)
                {
                    var passenger = seat?.Passenger;
                    if (passenger == null) continue;
                    // Ask the rider to leave. EntityAgent.TryUnmount() (no args) clears
                    // MountedOn and restores normal player control/camera. Cast guards
                    // against Passenger being typed as the base Entity.
                    if (passenger is EntityAgent agent) agent.TryUnmount();
                }
            }

            var placer = World.GetItem(new AssetLocation("vintagerailroading:trainplacer"));
            if (placer == null)
            {
                Msg(byEntity, "trainplacer item missing — cannot pick up.");
                return false;
            }

            var stack = new ItemStack(placer);
            bool gave = false;
            if (byEntity is EntityPlayer ep && ep.Player is IServerPlayer sp)
            {
                gave = sp.InventoryManager.TryGiveItemstack(stack);
            }
            if (!gave)
            {
                // No room — drop it at the train's position so it isn't lost.
                World.SpawnItemEntity(stack, Pos.XYZ);
            }

            Msg(byEntity, "Train picked up.");
            // If this car has fuel storage, drop its contents so cargo isn't lost.
            GetBehavior<VintageRailroading.Entities.EntityBehaviorFuelStorage>()?.DropContents();
            // Now safe to remove the train from the world (no riders left attached).
            Die(EnumDespawnReason.Removed);
            return true;
        }

        private void Msg(EntityAgent byEntity, string text)
        {
            if (byEntity is EntityPlayer ep && ep.Player is IServerPlayer sp)
                sp.SendMessage(Vintagestory.API.Config.GlobalConstants.GeneralChatGroup, text, EnumChatType.Notification);
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);

            bool isServer = World.Side == EnumAppSide.Server;
            string side = isServer ? "SRV" : "CLI";

            _diagAccum += dt;
            bool logNow = _diagAccum >= 0.5f;
            if (logNow) _diagAccum = 0f;

            void Log(string msg)
            {
                if (logNow) World.Logger.Notification("[vrr] " + side + " " + msg);
            }

            bool coupled = false;

            // ============================ SERVER (authoritative) ============================
            // Reads throttle / coupling, advances Distance along the network, and is the
            // single source of truth that syncs (SegmentId, Distance, Speed) to clients.
            if (isServer)
            {
                if (LeaderEntityId != 0 && _network != null)
                {
                    var leader = World.GetEntityById(LeaderEntityId) as EntityTrain;
                    if (leader == null)
                    {
                        LeaderEntityId = 0;
                        Speed = 0;
                    }
                    else
                    {
                        coupled = true;
                        var res = _network.Offset(
                            leader.SegmentId, leader.Distance, -CouplingGap,
                            SegLength, out bool _);
                        SegmentId = res.segId;
                        Distance = res.distance;
                        _geomForSegId = -1;        // segment may have changed
                        Speed = leader.Speed;      // for pitch/animation only
                    }
                }

                if (!coupled)
                {
                    var controls = GetControllingControls();
                    double target = 0;
                    if (controls != null)
                    {
                        if (controls.Forward) target = MaxSpeed;
                        else if (controls.Backward) target = -MaxSpeed;
                    }
                    Speed += (target - Speed) * Math.Min(1.0, dt * 2.0);
                    if (Math.Abs(Speed) < 0.05) Speed = 0;
                }

                EnsureGeometry();
                if (_geom == null)
                {
                    Log($"SRV geom=NULL segId={SegmentId} — cannot advance this tick");
                    return;
                }

                if (!coupled && Speed != 0)
                {
                    AdvanceServerDistance(dt, Log);
                }

                ApplyPoseFromDistance(Distance, Log);
                CarryRiders(Log);
                return;
            }

            // ============================ CLIENT (smooth render) ============================
            // The server only updates Distance ~15x/sec; we render at frame rate. So the
            // client keeps its OWN distance (_clientDist) and integrates it from the synced
            // Speed EVERY frame, then softly corrects toward the authoritative synced
            // Distance whenever it arrives. This removes the per-tick stepping.
            EnsureGeometry();
            if (_geom == null)
            {
                if (logNow)
                    World.Logger.Notification($"[vrr] CLI geom=NULL segId={SegmentId} dist={Distance:0.0} — cannot render");
                return;
            }

            // (Re)seed local distance when uninitialised or when the server moved us to a
            // different segment (traversal / coupling jump): snap, don't interpolate across.
            if (!_clientInit || _clientSegId != SegmentId)
            {
                _clientDist = Distance;
                _clientSegId = SegmentId;
                _clientInit = true;
            }
            else
            {
                // Dead reckon this frame from the synced speed.
                _clientDist += Speed * dt;

                // Soft-correct toward the authoritative synced Distance so we never drift.
                // Lerp factor scales with dt (~8/sec) for a smooth catch-up; snap if the
                // gap is large (a teleport / desync) to avoid a long visible slide.
                double err = Distance - _clientDist;
                if (Math.Abs(err) > 4.0) _clientDist = Distance;
                else _clientDist += err * Math.Min(1.0, dt * 8.0);

                // Clamp to the current segment so we never sample off the curve; the
                // server handles the actual segment-to-segment traversal.
                if (_clientDist < 0) _clientDist = 0;
                if (_clientDist > _geom.Length) _clientDist = _geom.Length;
            }

            if (logNow)
                World.Logger.Notification($"[vrr] CLI render segId={SegmentId} cdist={_clientDist:0.0} sdist={Distance:0.0} spd={Speed:0.0}");

            ApplyPoseFromDistance(_clientDist, Log);
            CarryRiders(Log);
        }

        /// <summary>Server: advance Distance by Speed*dt, crossing node boundaries onto
        /// connected segments (or stopping at a true endpoint).</summary>
        private void AdvanceServerDistance(float dt, Action<string> Log)
        {
            double newDist = Distance + Speed * dt;

            if (newDist >= _geom.Length)
            {
                double overshoot = newDist - _geom.Length;
                var next = _network?.NextSegment(SegmentId, leavingEnd: true);
                if (next.HasValue)
                {
                    var info = next.Value;
                    SegmentId = info.nextSegmentId;
                    _geomForSegId = -1;
                    EnsureGeometry();
                    double nlen = _geom?.Length ?? 0;
                    Distance = info.entryDistanceFraction < 0.5 ? overshoot : Math.Max(0, nlen - overshoot);
                    Speed = Math.Abs(Speed) * info.dirSign;
                    Log($"TRAVERSE end -> seg {SegmentId} dist={Distance:0.0} dir={info.dirSign}");
                }
                else { Distance = _geom.Length; Speed = 0; Log("endpoint (far) -> stop"); }
            }
            else if (newDist <= 0)
            {
                double overshoot = -newDist;
                var next = _network?.NextSegment(SegmentId, leavingEnd: false);
                if (next.HasValue)
                {
                    var info = next.Value;
                    SegmentId = info.nextSegmentId;
                    _geomForSegId = -1;
                    EnsureGeometry();
                    double nlen = _geom?.Length ?? 0;
                    Distance = info.entryDistanceFraction < 0.5 ? overshoot : Math.Max(0, nlen - overshoot);
                    Speed = Math.Abs(Speed) * (info.entryDistanceFraction < 0.5 ? +1 : -1);
                    Log($"TRAVERSE start -> seg {SegmentId} dist={Distance:0.0}");
                }
                else { Distance = 0; Speed = 0; Log("endpoint (near) -> stop"); }
            }
            else
            {
                Distance = newDist;
            }
        }

        /// <summary>Project a track distance onto the current segment's curve and set
        /// Pos (position, yaw, grade pitch). Zeroes motion so inherited boat physics
        /// can't shove us off the spline.</summary>
        private void ApplyPoseFromDistance(double dist, Action<string> Log)
        {
            if (_geom == null) return;
            var p = _geom.PositionAtDistance(dist);
            var h = _geom.HeadingAtDistance(dist);
            Pos.SetPos(p.X, p.Y, p.Z);
            Pos.Yaw = (float)Math.Atan2(h.X, h.Z);
            Pos.Pitch = PitchFromHeading(h);
            Pos.Motion.Set(0, 0, 0);
        }

        /// <summary>Carry any seated riders to their seat position. Runs on both sides,
        /// unconditionally, so a rider is never left behind (see bug #7).</summary>
        private void CarryRiders(Action<string> Log)
        {
            foreach (var mountable in GetInterfaces<IMountable>())
            {
                if (mountable == null || !mountable.AnyMounted()) continue;
                foreach (var seat in mountable.Seats)
                {
                    var passenger = seat?.Passenger;
                    var sp = seat?.SeatPosition;
                    if (passenger != null && sp != null)
                    {
                        passenger.Pos.SetPos(sp.X, sp.Y, sp.Z);
                    }
                }
            }
        }

        // Returns the controlling rider's controls, or null if nobody is driving.
        // IMPORTANT: GetInterface<IMountable>() returns the FIRST IMountable, which is
        // EntityBoat's own (empty) mount — NOT the creaturecarrier seat the player is
        // actually in. We must search ALL mountables for one that AnyMounted().
        private EntityControls GetControllingControls()
        {
            foreach (var mountable in GetInterfaces<IMountable>())
            {
                if (mountable != null && mountable.AnyMounted())
                {
                    var cc = mountable.ControllingControls;
                    // Patch Forward from the rider entity if the seat object lacks it.
                    var controller = mountable.Controller;
                    if (cc != null && controller is EntityAgent agent && agent.Controls != null && agent.Controls.Forward)
                    {
                        cc.Forward = true;
                    }
                    if (cc != null) return cc;
                }
            }
            return null;
        }

        private void EnsureGeometry()
        {
            if (_network == null)
            {
                // Network may not be populated yet (esp. client-side before the
                // TrackNetworkManager has synced). Try to (re)acquire it.
                _network = Api?.ModLoader?.GetModSystem<TrackNetworkManager>()?.Network;
                if (_network == null) return;
            }
            if (_geomForSegId == SegmentId && _geom != null) return;
            var built = _network.BuildGeometry(SegmentId);
            if (built != null)
            {
                _geom = built;
                _geomForSegId = SegmentId;   // cache only on SUCCESS
            }
            else
            {
                // BuildGeometry failed (e.g. client network not synced yet, or this
                // segment not received). Do NOT cache the failure — leave _geom as-is
                // and retry next tick so it self-heals once the network arrives.
                _geom = null;
            }
        }

        /// <summary>Place the train onto a segment (server). Network passed in
        /// because this runs before Initialize sets _network.</summary>
        public void PlaceOnSegment(TrackNetwork network, long segmentId, double distance)
        {
            _network = network;
            SegmentId = segmentId;
            Distance = distance;
            _geomForSegId = -1;
            EnsureGeometry();
            if (_geom != null)
            {
                var p = _geom.PositionAtDistance(distance);
                var h = _geom.HeadingAtDistance(distance);
                Pos.X = p.X; Pos.Y = p.Y; Pos.Z = p.Z;
                Pos.Yaw = (float)Math.Atan2(h.X, h.Z);
                Pos.Pitch = PitchFromHeading(h);
            }
        }

        /// <summary>Pitch (radians) for the loco model from the track heading, so it
        /// noses up/down on grades. atan2(rise, run) of the heading vector. The SIGN
        /// here is the convention to verify in-game: if the loco tilts the wrong way on
        /// a slope (noses down while climbing), negate this. Yaw already orients the
        /// model; this only adds the vertical lean.</summary>
        /// <summary>Arc length of a network segment id, for the coupling walker. Builds
        /// lightweight geometry on demand; returns 0 if the segment is missing.</summary>
        private double SegLength(long segId)
        {
            if (_network == null) return 0;
            var g = _network.BuildGeometry(segId);
            return g?.Length ?? 0;
        }

        private static float PitchFromHeading(VintageRailroading.Track.Vec3d h)
        {
            double horiz = Math.Sqrt(h.X * h.X + h.Z * h.Z);
            return (float)Math.Atan2(h.Y, horiz);
        }
    }
}