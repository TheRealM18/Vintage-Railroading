using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using VintageRailroading.Track;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// Common contract for anything that rides the track network — locomotives
    /// (EntityTrain) and seatless cargo cars (EntityCargo). Consumers (the couple/uncouple
    /// commands, the placer, the coupling follower logic) work against THIS interface so a
    /// cargo car is accepted anywhere a train is, without duplicating "is EntityTrain ||
    /// is EntityCargo" checks and double casts everywhere.
    ///
    /// Every member here is already implemented by both classes via their WatchedAttributes
    /// properties and PlaceOnSegment method, so the only change to those classes is adding
    /// ": IRailVehicle" to the declaration — no new bodies needed.
    ///
    /// Note: implementers also derive from Entity, so consumers still have access to
    /// EntityId, Pos, Alive, World, etc. To use those, keep a reference typed as the
    /// concrete Entity OR cast the IRailVehicle back to Entity (every implementer is one).
    /// </summary>
    public interface IRailVehicle
    {
        /// <summary>Which track segment the vehicle is currently on.</summary>
        long SegmentId { get; set; }

        /// <summary>Metres travelled along the current segment.</summary>
        double Distance { get; set; }

        /// <summary>Metres/second along the track (for movement and/or pitch animation).</summary>
        double Speed { get; set; }

        /// <summary>EntityId of the leader this vehicle follows (0 = not coupled).</summary>
        long LeaderEntityId { get; set; }

        /// <summary>Arc-length gap (metres) kept behind the leader when coupled.</summary>
        double CouplingGap { get; set; }

        /// <summary>Place the vehicle onto a network segment at the given distance.</summary>
        void PlaceOnSegment(TrackNetwork network, long segmentId, double distance);
    }

    /// <summary>
    /// Helpers shared by the rail-vehicle entity classes.
    /// </summary>
    public static class RailVehicleHelper
    {
        // Fallback derivation: entity code -> placer item code, used only when an entity has
        // no stamped "vrrPlacerCode" (e.g. spawned via creative or before the stamp existed).
        //
        // This is convention-based rather than a hardcoded case list: the placer item for any
        // car "<code>" is "<code>placer". So a new car type needs NO C# change here — just an
        // entities/<code>.json and an itemtypes placer whose code is "<code>placer" with
        // attributes.entityCode = "<code>". ResolvePlacerItem still validates the result
        // against the registry and falls back to the train placer if it does not exist, so an
        // unconventional name simply degrades gracefully instead of erroring.
        private static string PlacerForEntityCode(string entityCode)
        {
            if (string.IsNullOrEmpty(entityCode)) return "trainplacer";
            // Avoid producing "trainplacerplacer" etc. if the code already ends in "placer".
            if (entityCode.EndsWith("placer")) return entityCode;
            return entityCode + "placer";
        }

        /// <summary>
        /// Resolve the placer item that should be returned when this vehicle is picked up.
        /// Prefers the exact "vrrPlacerCode" stamped at placement; otherwise derives it from
        /// the entity's own code; finally falls back to the train placer. Returns null only
        /// if even the train placer item is missing from the registry.
        /// </summary>
        public static Item ResolvePlacerItem(Vintagestory.API.Common.Entities.Entity entity)
        {
            var world = entity?.World;
            if (world == null) return null;

            // 1) exact stamp from when it was placed
            string code = entity.WatchedAttributes?.GetString("vrrPlacerCode", null);

            // 2) derive from the entity's code (e.g. "coalcart" -> "coalcartplacer")
            if (string.IsNullOrEmpty(code))
            {
                string entityCode = entity.Code?.Path ?? "train";
                code = PlacerForEntityCode(entityCode);
            }

            var item = world.GetItem(new AssetLocation("vintagerailroading:" + code));
            // 3) last resort: the train placer, so pickup never silently fails
            if (item == null)
                item = world.GetItem(new AssetLocation("vintagerailroading:trainplacer"));
            return item;
        }
    }
}
