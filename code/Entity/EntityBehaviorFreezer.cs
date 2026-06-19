using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// FREEZER / FOOD car power behavior. While POWERED, perishable cargo in this car's
    /// generic storage is kept cold so it spoils far more slowly (ideally not at all);
    /// while UNPOWERED it spoils at the normal rate.
    ///
    /// Power model mirrors EntityBehaviorFuelConsumer: a small buffer measured in burn-
    /// seconds drains each tick and is topped up by pulling fuel from any coupled coal car
    /// found by walking the coupling chain that leads to THIS car (same IFuelSource path the
    /// locomotive uses). A freezer with no coupled tender simply runs out and stops cooling.
    ///
    /// Preservation works by periodically "freshening" the transitionable state on stored
    /// stacks: each powered tick we hold their spoil/transition timers back so fresh-hours
    /// accrue slowly. This is deliberately simple and self-contained — it does not require
    /// changes to the storage behavior beyond being able to read its inventory.
    ///
    /// JSON (server side is sufficient; harmless on client):
    ///   { "code": "freezer", "burnRatePerSecond": 0.25, "preserveFactor": 0.0 }
    ///   - burnRatePerSecond: burn-seconds spent per real second while running.
    ///   - preserveFactor: 0 = freeze spoilage completely while powered; 1 = no effect.
    /// </summary>
    public class EntityBehaviorFreezer : EntityBehavior
    {
        private double _burnRate = 0.25;     // burn-seconds spent per real second
        private double _buffer;              // burn-seconds remaining
        private float  _preserveFactor = 0f; // 0 = full preservation, 1 = none
        private float  _accum;               // throttle preservation pass to ~1/sec

        public EntityBehaviorFreezer(Entity entity) : base(entity) { }

        public override string PropertyName() => "freezer";

        /// <summary>True when the freezer currently has power to cool.</summary>
        public bool Powered => _buffer > 0.0001;

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            _burnRate = attributes?["burnRatePerSecond"].AsDouble(0.25) ?? 0.25;
            if (_burnRate <= 0) _burnRate = 0.25;
            _preserveFactor = (float)(attributes?["preserveFactor"].AsDouble(0.0) ?? 0.0);
            if (_preserveFactor < 0f) _preserveFactor = 0f;
            if (_preserveFactor > 1f) _preserveFactor = 1f;

            _buffer = entity.WatchedAttributes.GetDouble("vrrFreezer", 0.0);
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (entity.Api.Side != EnumAppSide.Server) return;

            // Drain the buffer while running, refilling from a coupled coal car as needed.
            _buffer -= dt * _burnRate;
            if (_buffer < 0) _buffer = 0;

            if (_buffer <= dt * _burnRate)
            {
                foreach (var src in GatherSources())
                {
                    if (!src.HasFuel) continue;
                    double secs = src.DrawBurnSeconds();
                    if (secs > 0) { _buffer += secs; break; }
                }
            }

            entity.WatchedAttributes.SetDouble("vrrFreezer", _buffer);

            // Preservation pass ~once per second to keep it cheap.
            _accum += dt;
            if (_accum >= 1f)
            {
                if (Powered) PreserveContents(_accum);
                _accum = 0f;
            }
        }

        /// <summary>
        /// Hold back spoilage on stored perishables while powered. We read the storage
        /// behavior's inventory and, for each stack carrying transition state, push the
        /// "fresh hours" transition timer forward so it ages more slowly. With
        /// preserveFactor 0 the timer is fully reset (no spoilage); higher values let some
        /// spoilage through proportionally.
        /// </summary>
        private void PreserveContents(float seconds)
        {
            var storage = entity.GetBehavior<EntityBehaviorGenericStorage>();
            var inv = storage?.Inventory;
            if (inv == null) return;

            foreach (var slot in inv)
            {
                var stack = slot?.Itemstack;
                if (stack?.Attributes == null) continue;

                // Vanilla perishables store their transition progress under "transitionstate"
                // with a "freshHours" array. We nudge each transition's accumulated fresh
                // time DOWN by (1 - preserveFactor) of what just elapsed, so the goods stay
                // fresh. If the structure is absent the stack simply isn't perishable.
                var tstate = stack.Attributes.GetTreeAttribute("transitionstate");
                if (tstate == null) continue;

                var freshArr = (tstate["freshHours"] as FloatArrayAttribute)?.value;
                var transArr = (tstate["transitionedHours"] as FloatArrayAttribute)?.value;
                if (transArr == null) continue;

                double gameHoursElapsed = seconds / 3600.0 * entity.World.Calendar.SpeedOfTime;
                float hold = (float)(gameHoursElapsed * (1.0 - _preserveFactor));
                for (int i = 0; i < transArr.Length; i++)
                {
                    transArr[i] -= hold;
                    if (transArr[i] < 0) transArr[i] = 0;
                }
                slot?.MarkDirty();
            }
        }

        // ── Coupling-chain fuel sourcing (same approach as the fuel consumer) ──────

        private List<IFuelSource> GatherSources()
        {
            var sources = new List<IFuelSource>();
            var world = entity.World;
            if (world == null) return sources;

            var near = world.GetEntitiesAround(entity.Pos.XYZ, 64f, 64f,
                e => e is IRailVehicle && e.Alive && e != entity);
            if (near == null) return sources;

            foreach (var e in near)
            {
                if (!ChainConnectsToMe(e as IRailVehicle)) continue;
                var fs = e.GetBehavior<EntityBehaviorFuelStorage>();
                if (fs != null) sources.Add(new SolidFuelSource(fs));
            }
            return sources;
        }

        /// <summary>
        /// True if `rv` is in the same consist as this car — i.e. following the leader chain
        /// from either end reaches this entity. Unlike the locomotive (which only pulls from
        /// cars BEHIND it), a freezer should accept a tender anywhere in the same consist, so
        /// we also accept the case where THIS car's chain leads to `rv`.
        /// </summary>
        private bool ChainConnectsToMe(IRailVehicle? rv)
        {
            if (rv == null) return false;
            var world = entity.World;
            long myId = entity.EntityId;

            // rv's chain -> me
            var cur = rv;
            int hops = 0;
            while (cur != null && hops++ < 32)
            {
                long leaderId = cur.LeaderEntityId;
                if (leaderId == 0) break;
                if (leaderId == myId) return true;
                cur = world.GetEntityById(leaderId) as IRailVehicle;
            }

            // me -> rv (this car follows the tender)
            var meRv = entity as IRailVehicle;
            cur = meRv;
            hops = 0;
            long rvId = (rv as Entity)?.EntityId ?? 0;
            while (cur != null && hops++ < 32)
            {
                long leaderId = cur.LeaderEntityId;
                if (leaderId == 0) break;
                if (leaderId == rvId) return true;
                cur = world.GetEntityById(leaderId) as IRailVehicle;
            }
            return false;
        }
    }
}
