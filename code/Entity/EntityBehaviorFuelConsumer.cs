using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// Makes a locomotive CONSUME fuel to run. Kept deliberately separate from fuel
    /// STORAGE (EntityBehaviorFuelStorage) and from the fuel SOURCE readers (IFuelSource),
    /// so the same consumer will work unchanged when liquid fuel is added later — it just
    /// asks its sources for burn-seconds without knowing solid vs. liquid.
    ///
    /// Model: a small "firebox" buffer measured in BURN-SECONDS. Each server tick, if the
    /// train is trying to move, the buffer drains by dt * burnRateWhileMoving. When the
    /// buffer falls below one tick's worth, the consumer pulls one unit of fuel from an
    /// available source (refilling the buffer by that fuel's burn-seconds).
    ///
    /// Fuel sources: per the design, ONLY coupled coal carts (fuelstorage behaviors found
    /// by walking the coupling chain that leads to THIS locomotive). A lone locomotive has
    /// no tender and therefore no fuel — it can coast but never accelerate.
    ///
    /// Throttle gate: HasPower is true when the buffer has burn-time left. EntityTrain
    /// checks HasPower before allowing a nonzero throttle target, so out of fuel = coast
    /// (existing speed decays normally) but no acceleration.
    ///
    /// JSON (locomotive, server side is enough; harmless on client):
    ///   { "code": "fuelconsumer", "burnRatePerSecond": 1.0, "fireboxSeconds": 0 }
    ///   - burnRatePerSecond: how many burn-seconds are spent per real second while moving.
    ///   - fireboxSeconds: optional starting buffer (default 0 = must load fuel first).
    /// </summary>
    public class EntityBehaviorFuelConsumer : EntityBehavior
    {
        private double _burnRate = 1.0;     // burn-seconds spent per real second while moving
        private double _buffer;             // burn-seconds remaining in the firebox

        public EntityBehaviorFuelConsumer(Entity entity) : base(entity) { }

        public override string PropertyName() => "fuelconsumer";

        /// <summary>True when there is burn-time available, so the train may accelerate.</summary>
        public bool HasPower => _buffer > 0.0001;

        /// <summary>Burn-seconds currently in the firebox (for HUD/debug).</summary>
        public double FireboxSeconds => _buffer;

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            _burnRate = attributes?["burnRatePerSecond"].AsDouble(1.0) ?? 1.0;
            if (_burnRate <= 0) _burnRate = 1.0;

            // Restore a persisted buffer if present, else use the JSON starting value.
            double start = attributes?["fireboxSeconds"].AsDouble(0.0) ?? 0.0;
            _buffer = entity.WatchedAttributes.GetDouble("vrrFirebox", start);
        }

        /// <summary>
        /// Called from EntityTrain's server tick. `wantsToMove` is true when the driver is
        /// holding a throttle. Drains the firebox while moving and refills from coupled coal
        /// carts. Server-side only (the train guards this).
        /// </summary>
        public void TickConsume(float dt, bool wantsToMove)
        {
            if (entity.Api.Side != EnumAppSide.Server) return;

            // Drain only when actually trying to move (idle doesn't burn).
            if (wantsToMove && _buffer > 0)
            {
                _buffer -= dt * _burnRate;
                if (_buffer < 0) _buffer = 0;
            }

            // Refill: if the buffer is low and the driver wants to move, try to pull one
            // unit of fuel from any available source.
            if (wantsToMove && _buffer <= dt * _burnRate)
            {
                var srcs = GatherSources();
                VrrDebug.Log(entity.Api, "FuelConsumer: wantsToMove buffer={0:0.00} sources={1}", _buffer, srcs.Count);
                foreach (var src in srcs)
                {
                    bool hf = src.HasFuel;
                    VrrDebug.Log(entity.Api, "FuelConsumer: source hasFuel={0}", hf);
                    if (!hf) continue;
                    double secs = src.DrawBurnSeconds();
                    VrrDebug.Log(entity.Api, "FuelConsumer: drew {0:0.0}s of burn", secs);
                    if (secs > 0) { _buffer += secs; break; } // one unit per top-up
                }
            }

            // Persist so the firebox survives save/reload.
            entity.WatchedAttributes.SetDouble("vrrFirebox", _buffer);
        }

        /// <summary>
        /// Build the list of fuel sources feeding THIS locomotive: every coupled coal cart
        /// (an entity carrying a fuelstorage whose coupling chain leads back to us).
        ///
        /// We walk the consist by following LeaderEntityId links: any rail vehicle whose
        /// chain of leaders reaches this entity is "in our consist behind us." For each such
        /// vehicle that has a FuelStorage, we add a SolidFuelSource.
        /// </summary>
        private List<IFuelSource> GatherSources()
        {
            var sources = new List<IFuelSource>();
            var world = entity.World;
            if (world == null) return sources;

            // Scan nearby rail vehicles and keep those whose leader-chain reaches us.
            // (Consists are short and local, so a small radius scan is cheap and avoids
            // maintaining a separate consist registry for now.)
            // VERIFY: IWorldAccessor.GetEntitiesAround(Vec3d, float horRange, float vertRange,
            // ActionConsumable<Entity>) — same overload the old coupling commands used.
            var near = world.GetEntitiesAround(entity.ServerPos.XYZ, 64f, 64f,
                e => e is IRailVehicle && e.Alive && e != entity);
            if (near == null) return sources;

            foreach (var e in near)
            {
                if (!ChainLeadsToMe(e as IRailVehicle, e)) continue;
                var fs = e.GetBehavior<EntityBehaviorFuelStorage>();
                if (fs != null) sources.Add(new SolidFuelSource(fs));
            }
            return sources;
        }

        /// <summary>
        /// True if following `rv`'s LeaderEntityId chain reaches this locomotive's entity id
        /// within a sane number of hops (guards against cycles / broken links).
        /// </summary>
        private bool ChainLeadsToMe(IRailVehicle rv, Entity startEnt)
        {
            if (rv == null) return false;
            var world = entity.World;
            long myId = entity.EntityId;

            var cur = rv;
            int hops = 0;
            while (cur != null && hops++ < 32)
            {
                long leaderId = cur.LeaderEntityId;
                if (leaderId == 0) return false;       // reached an uncoupled head that isn't us
                if (leaderId == myId) return true;     // chain leads to this locomotive
                var leaderEnt = world.GetEntityById(leaderId);
                cur = leaderEnt as IRailVehicle;
            }
            return false;
        }
    }
}
