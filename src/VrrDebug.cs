using Vintagestory.API.Common;

namespace VintageRailroading
{
    /// <summary>
    /// Central debug logging for Vintage Railroading.
    ///
    /// All diagnostic logging in the mod routes through here instead of calling
    /// api.Logger / World.Logger directly. When <see cref="Enabled"/> is false (the
    /// default) every call is a cheap no-op, so debug logging can live permanently in the
    /// hot paths (ticks, render frames) without costing anything in normal play. Toggle it
    /// at runtime with the <c>/vrrdebug</c> command — no recompile, no log spam by default.
    ///
    /// Usage:
    ///   VrrDebug.Log(api, "EntityTrain advance segId={0} dist={1:0.0}", seg, dist);
    /// The "[vrr]" prefix is added automatically. Pass the entity/world/core API you have
    /// on hand; any of them resolves to a logger.
    /// </summary>
    public static class VrrDebug
    {
        /// <summary>Master switch. Off by default. Flipped by /vrrdebug.</summary>
        public static bool Enabled = false;

        /// <summary>Log a formatted debug line, but only when debug mode is enabled.</summary>
        public static void Log(ICoreAPI api, string message, params object[] args)
        {
            if (!Enabled || api?.Logger == null) return;
            api.Logger.Notification("[vrr] " + Format(message, args));
        }

        /// <summary>Overload for code that only has a World reference.</summary>
        public static void Log(IWorldAccessor world, string message, params object[] args)
        {
            if (!Enabled || world?.Logger == null) return;
            world.Logger.Notification("[vrr] " + Format(message, args));
        }

        private static string Format(string message, object[] args)
        {
            if (args == null || args.Length == 0) return message;
            try { return string.Format(message, args); }
            catch { return message; } // never let a bad format string crash a tick
        }
    }
}
