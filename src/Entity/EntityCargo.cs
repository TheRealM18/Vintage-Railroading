using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageRailroading.Track;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// A SEATLESS cargo car (e.g. log car, coal cart) that rides the track NETWORK.
    ///
    /// This is deliberately NOT EntityTrain and NOT EntityBoat. EntityTrain extended
    /// EntityBoat purely to borrow a working CreateSeat for the creaturecarrier seat
    /// system; that seat machinery is exactly what kept hard-failing. A cargo car has
    /// no driver and no passengers, so it needs none of it. EntityCargo therefore:
    ///   * extends EntityAgent directly (no boat, no water physics, no buoyancy),
    ///   * carries NO creaturecarrier / seatable behavior in its JSON,
    ///   * never calls CreateSeat, never carries riders, never reads a throttle.
    ///
    /// Movement model is the same 1D state as EntityTrain — (SegmentId, Distance, Speed)
    /// projected onto the segment's Hermite curve each tick — but a cargo car only ever
    /// MOVES by following a leader through the coupling system. With no leader it simply
    /// sits still on its segment (it has no throttle of its own). Couple it behind a
    /// locomotive with /vrrcouple and it tracks CouplingGap metres behind, around curves,
    /// grades, and junctions, exactly like a coupled EntityTrain does.
    ///
    /// Storage: add a wood/fuel storage behavior in the JSON (e.g.
    ///   { "code": "woodstorage", "quantitySlots": 16 }
    /// ) and empty-hand right-click opens it. Because there is no seat to compete with,
    /// no interaction-priority juggling is needed: the only thing right-click does is
    /// open storage (or pick up with a wrench).
    /// </summary>
    public class EntityCargo : EntityAgent, IRailVehicle
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
        public double Speed // metres/sec along the track (for pitch/animation only)
        {
            get => WatchedAttributes.GetDouble("vrrSpeed", 0);
            set => WatchedAttributes.SetDouble("vrrSpeed", value);
        }

        /// <summary>Entity id of the car this one is coupled BEHIND (its leader). 0 =
        /// not coupled. A cargo car with no leader does not move (no throttle).</summary>
        public long LeaderEntityId
        {
            get => WatchedAttributes.GetLong("vrrLeader", 0);
            set => WatchedAttributes.SetLong("vrrLeader", value);
        }
        /// <summary>Arc-length gap (metres) this car keeps behind its leader.</summary>
        public double CouplingGap
        {
            get => WatchedAttributes.GetDouble("vrrGap", 6.0);
            set => WatchedAttributes.SetDouble("vrrGap", value);
        }

        private TrackNetwork _network;
        private TrackSegment _geom;
        private long _geomForSegId = -1;
        private float _diagAccum;

        // Client render-frame hook. interpolateposition (re-enabled in the JSON) smooths
        // the SYNCED transform between server ticks, but it lerps linearly in 3D and would
        // chord across curves. We neutralise that by re-projecting Pos onto the spline on
        // every render frame, AFTER interpolation has run — so the curve is always the
        // final word and corners are never cut. _capi/_renderReg manage that callback.
        private Vintagestory.API.Client.ICoreClientAPI _capi;
        private bool _renderReg;

        // CLIENT-SIDE DEAD RECKONING — identical scheme to EntityTrain: integrate the
        // synced Speed locally each frame and soft-correct toward the synced Distance.
        private double _clientDist;
        private long _clientSegId = -1;
        private bool _clientInit;

        public override void Initialize(EntityProperties properties, ICoreAPI api, long inChunkIndex3d)
        {
            requirePosesOnServer = true;
            base.Initialize(properties, api, inChunkIndex3d);

            var mgr = api.ModLoader.GetModSystem<TrackNetworkManager>();
            _network = mgr?.Network;
            api.Logger.Notification("[vintagerailroading] EntityCargo init. network={0}",
                _network != null ? "OK" : "NULL");

            // CLIENT: drive the on-curve pose every RENDER frame (not just every game
            // tick), so motion is perfectly smooth and always sits on the spline even
            // while interpolateposition is smoothing the synced transform underneath.
            if (api.Side == EnumAppSide.Client)
            {
                _capi = api as Vintagestory.API.Client.ICoreClientAPI;
                if (_capi != null && !_renderReg)
                {
                    // VERIFY: enum member name. In 1.22 the per-frame render stage for
                    // entities is EnumRenderStage.Before (runs before the frame is drawn).
                    // If this overload/enum differs in your API, this is the line to tweak.
                    _capi.Event.RegisterRenderer(
                        new CargoRenderUpdater(this), Vintagestory.API.Client.EnumRenderStage.Before,
                        "vrrcargo");
                    _renderReg = true;
                }
            }
        }

        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vintagestory.API.MathTools.Vec3d hitPosition, EnumInteractMode mode)
        {
            // WRENCH PICKUP: right-click with a wrench picks the car up. Server-side only.
            if (World.Side == EnumAppSide.Server && mode == EnumInteractMode.Interact
                && IsWrench(itemslot) && TryPickup(byEntity))
            {
                return;
            }

            // STORAGE: empty-hand right-click opens whichever storage behavior this cargo
            // car has (wood or fuel). No seat exists on a cargo car, so there is nothing
            // to compete with — opening storage is simply the default action.
            if (mode == EnumInteractMode.Interact && (itemslot == null || itemslot.Empty))
            {
                var plr = (byEntity as EntityPlayer)?.Player;
                if (plr != null)
                {
                    var wood = GetBehavior<EntityBehaviorWoodStorage>();
                    if (wood != null && wood.OpenFor(plr)) return;

                    var fuel = GetBehavior<EntityBehaviorFuelStorage>();
                    if (fuel != null && fuel.OpenFor(plr)) return;
                }
            }

            base.OnInteract(byEntity, itemslot, hitPosition, mode);
        }

        private static bool IsWrench(ItemSlot slot)
        {
            if (slot == null || slot.Empty) return false;
            string path = slot.Itemstack?.Collectible?.Code?.Path;
            return path != null && path.Contains("wrench");
        }

        /// <summary>Pick the cargo car up into the player's inventory. No riders to eject
        /// (cargo cars are seatless), so this is simpler than EntityTrain's pickup: drop
        /// stored cargo, give back a placer item, despawn.</summary>
        private bool TryPickup(EntityAgent byEntity)
        {
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
                World.SpawnItemEntity(stack, Pos.XYZ);
            }

            Msg(byEntity, "Cargo car picked up.");
            // Drop any stored contents so cargo isn't lost on pickup.
            GetBehavior<EntityBehaviorWoodStorage>()?.DropContents();
            GetBehavior<EntityBehaviorFuelStorage>()?.DropContents();

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
                if (logNow) World.Logger.Notification("[vrr] " + side + " CARGO " + msg);
            }

            // ============================ SERVER (authoritative) ============================
            // A cargo car has no throttle. It only moves by FOLLOWING a leader through the
            // coupling system. With no leader it holds position (Speed forced to 0).
            if (isServer)
            {
                if (LeaderEntityId != 0 && _network != null)
                {
                    // Leader may be a locomotive (EntityTrain) or another cargo car
                    // (EntityCargo) in a longer consist. Both expose SegmentId/Distance/Speed,
                    // so resolve the leader by either type.
                    var leaderEnt = World.GetEntityById(LeaderEntityId);
                    if (TryGetTrackState(leaderEnt, out long lSeg, out double lDist, out double lSpeed))
                    {
                        var res = _network.Offset(lSeg, lDist, -CouplingGap, SegLength, out bool _);
                        SegmentId = res.segId;
                        Distance = res.distance;
                        _geomForSegId = -1;      // segment may have changed
                        Speed = lSpeed;          // for pitch/animation only
                    }
                    else
                    {
                        // Leader gone — uncouple and stop.
                        LeaderEntityId = 0;
                        Speed = 0;
                    }
                }
                else
                {
                    Speed = 0; // standalone cargo car: never self-propels
                }

                EnsureGeometry();
                if (_geom == null)
                {
                    Log($"geom=NULL segId={SegmentId} — cannot pose this tick");
                    return;
                }

                ApplyPoseFromDistance(Distance, Log);
                return;
            }

            // ============================ CLIENT (smooth render) ============================
            // The heavy lifting now happens per RENDER frame in ClientAdvanceAndPose (driven
            // by CargoRenderUpdater). We still call it here so the pose is correct even on
            // ticks, but the render hook is what makes motion frame-smooth.
            ClientAdvanceAndPose(dt, logNow);
        }

        /// <summary>Client: integrate _clientDist from the synced Speed, soft-correct toward
        /// the synced Distance, and project onto the spline — then write Pos. Called every
        /// render frame (CargoRenderUpdater) AND every client game tick. Re-projecting here,
        /// after interpolateposition has smoothed the synced transform, keeps the car exactly
        /// on the curve so interpolation never chords across corners.</summary>
        public void ClientAdvanceAndPose(float dt, bool logNow)
        {
            if (World == null || World.Side != EnumAppSide.Client) return;

            EnsureGeometry();
            if (_geom == null)
            {
                if (logNow)
                    World.Logger.Notification($"[vrr] CLI CARGO geom=NULL segId={SegmentId} dist={Distance:0.0}");
                return;
            }

            if (!_clientInit || _clientSegId != SegmentId)
            {
                _clientDist = Distance;
                _clientSegId = SegmentId;
                _clientInit = true;
            }
            else
            {
                _clientDist += Speed * dt;
                double err = Distance - _clientDist;
                if (Math.Abs(err) > 4.0) _clientDist = Distance;
                else _clientDist += err * Math.Min(1.0, dt * 8.0);

                if (_clientDist < 0) _clientDist = 0;
                if (_clientDist > _geom.Length) _clientDist = _geom.Length;
            }

            ApplyPoseFromDistance(_clientDist, null);
        }

        /// <summary>Read (SegmentId, Distance, Speed) from a leader entity. Any rail
        /// vehicle (EntityTrain or EntityCargo, both IRailVehicle) is a valid leader.
        /// Returns false if the entity is null or not a rail vehicle.</summary>
        private static bool TryGetTrackState(Entity ent, out long segId, out double dist, out double speed)
        {
            segId = 0; dist = 0; speed = 0;
            if (ent is IRailVehicle rv)
            {
                segId = rv.SegmentId; dist = rv.Distance; speed = rv.Speed;
                return true;
            }
            return false;
        }

        /// <summary>Project a track distance onto the current segment's curve and set Pos
        /// (position, yaw, grade pitch). Zeroes motion so physics can't drift us off-rail.</summary>
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

        private void EnsureGeometry()
        {
            if (_network == null)
            {
                _network = Api?.ModLoader?.GetModSystem<TrackNetworkManager>()?.Network;
                if (_network == null) return;
            }
            if (_geomForSegId == SegmentId && _geom != null) return;
            var built = _network.BuildGeometry(SegmentId);
            if (built != null)
            {
                _geom = built;
                _geomForSegId = SegmentId;
            }
            else
            {
                _geom = null; // don't cache failure; retry next tick (network may sync late)
            }
        }

        /// <summary>Place the cargo car onto a segment (server). Network passed in because
        /// this runs before Initialize sets _network.</summary>
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

        /// <summary>Arc length of a network segment id, for the coupling walker.</summary>
        private double SegLength(long segId)
        {
            if (_network == null) return 0;
            var g = _network.BuildGeometry(segId);
            return g?.Length ?? 0;
        }

        /// <summary>Pitch (radians) from the track heading so the car noses up/down on
        /// grades. Same convention as EntityTrain — if tilts are backwards, negate.</summary>
        private static float PitchFromHeading(VintageRailroading.Track.Vec3d h)
        {
            double horiz = Math.Sqrt(h.X * h.X + h.Z * h.Z);
            return (float)Math.Atan2(h.Y, horiz);
        }

        // The render updater is unregistered when the entity leaves the world. The
        // CargoRenderUpdater's Dispose handles the actual UnregisterRenderer, but we null
        // our cached api so any late frame is a no-op.
        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            _capi = null;
            base.OnEntityDespawn(despawn);
        }
    }

    /// <summary>
    /// Tiny per-frame driver that re-applies the cargo car's on-curve pose EVERY render
    /// frame, after interpolateposition has smoothed the synced transform. This is what
    /// makes the car move smoothly at full frame rate while staying exactly on the spline
    /// (interpolation alone would chord across curves). Registered in EntityCargo.Initialize
    /// on the client only.
    /// </summary>
    public class CargoRenderUpdater : Vintagestory.API.Client.IRenderer
    {
        private EntityCargo _cargo;

        // VERIFY: IRenderer requires RenderOrder and RenderRange. 0.0 / 999 are safe
        // generic values; adjust if your build complains about missing members.
        public double RenderOrder => 0.0;
        public int RenderRange => 999;

        public CargoRenderUpdater(EntityCargo cargo) { _cargo = cargo; }

        public void OnRenderFrame(float deltaTime, Vintagestory.API.Client.EnumRenderStage stage)
        {
            // If the entity is gone, stop touching it.
            if (_cargo == null || _cargo.Alive == false) return;
            _cargo.ClientAdvanceAndPose(deltaTime, false);
        }

        public void Dispose() { _cargo = null; }
    }
}
