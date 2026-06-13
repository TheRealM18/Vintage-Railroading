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
}
