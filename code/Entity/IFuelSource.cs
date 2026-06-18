using Vintagestory.API.Common;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// Abstract source of burn-time for the fuel consumer. The consumer doesn't care WHERE
    /// fuel comes from — only how many seconds of burn it can withdraw. Today the only
    /// implementation is <see cref="SolidFuelSource"/> (coal/charcoal/etc. via
    /// CombustibleProps on a FuelStorage). When liquid fuel is added later, a
    /// LiquidFuelSource implementing this same interface (litres × energy-per-litre →
    /// seconds, drawn from a fluidstorage) drops straight in with NO change to the consumer.
    /// </summary>
    public interface IFuelSource
    {
        /// <summary>True if this source currently has any fuel to give.</summary>
        bool HasFuel { get; }

        /// <summary>
        /// Consume up to one "unit" of fuel and return how many SECONDS of burn it yields.
        /// Returns 0 if empty. The consumer calls this only when its buffer needs topping
        /// up, so a "unit" is one item / one discrete draw — the buffer smooths it out.
        /// </summary>
        double DrawBurnSeconds();
    }

    /// <summary>
    /// Solid-fuel source: pulls one fuel item from a FuelStorage inventory and converts its
    /// CombustibleProps.BurnDuration into burn-seconds. This is the coal-cart reader.
    /// </summary>
    public class SolidFuelSource : IFuelSource
    {
        private readonly EntityBehaviorFuelStorage _storage;

        public SolidFuelSource(EntityBehaviorFuelStorage storage)
        {
            _storage = storage;
        }

        public bool HasFuel
        {
            get
            {
                var inv = _storage?.Inventory;
                if (inv == null) return false;
                var api = inv.Api;
                foreach (var slot in inv)
                {
                    var st = slot?.Itemstack;
                    if (st == null) continue;
                    var cp = st.Collectible?.CombustibleProps;
                    bool isFuel = ItemSlotFuelOnly.IsFuel(st);
                    VrrDebug.Log(api, "SolidFuelSource: slot item={0} combustibleProps={1} burnDuration={2} isFuel={3}",
                        st.Collectible?.Code, cp != null, cp != null ? cp.BurnDuration : 0f, isFuel);
                    if (isFuel) return true;
                }
                return false;
            }
        }

        public double DrawBurnSeconds()
        {
            var inv = _storage?.Inventory;
            if (inv == null) return 0;

            foreach (var slot in inv)
            {
                var stack = slot?.Itemstack;
                if (stack == null || !ItemSlotFuelOnly.IsFuel(stack)) continue;

                // BurnDuration is per ITEM, in seconds. Take exactly one item.
                double seconds = stack.Collectible.CombustibleProps.BurnDuration;
                slot.TakeOut(1);
                slot.MarkDirty();
                return seconds;
            }
            return 0;
        }
    }
}
