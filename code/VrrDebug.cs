using System;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

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
    /// Output goes to its OWN file, "vrr-debug.log", in the game's standard Logs folder
    /// (GamePaths.Logs — the same place VS writes server-main.log etc.), instead of the
    /// shared game log. Each line is timestamped and tagged with the side (SRV/CLI) that
    /// emitted it, since both server and client route through here. The file is opened in
    /// append mode and truncated ONCE per process the first time logging is enabled, so a
    /// session starts clean but multiple toggles within a session keep appending.
    ///
    /// Usage:
    ///   VrrDebug.Log(api, "EntityTrain advance segId={0} dist={1:0.0}", seg, dist);
    /// The "[vrr]" prefix is added automatically. Pass the entity/world/core API you have
    /// on hand; any of them resolves to a side tag.
    /// </summary>
    public static class VrrDebug
    {
        /// <summary>Master switch. Off by default. Flipped by /vrrdebug.</summary>
        public static bool Enabled = false;

        private const string FileName = "vrr-debug.log";

        // Guards concurrent writes — server ticks and client render frames can both log.
        private static readonly object _lock = new object();

        // Resolved once: full path to the log file inside the game's Logs directory.
        private static string? _path;
        // Whether we've truncated the file this process yet (fresh start per launch).
        private static bool _truncated;

        /// <summary>Log a formatted debug line, but only when debug mode is enabled.</summary>
        public static void Log(ICoreAPI api, string message, params object?[] args)
        {
            if (!Enabled) return;
            Write(SideTag(api?.Side), Format(message, args));
        }

        /// <summary>Overload for code that only has a World reference.</summary>
        public static void Log(IWorldAccessor world, string message, params object?[] args)
        {
            if (!Enabled) return;
            Write(SideTag(world?.Side), Format(message, args));
        }

        /// <summary>
        /// Log an ERROR/WARNING to the file ALWAYS — independent of the <see cref="Enabled"/>
        /// toggle. Use this for genuine failure conditions (network save/sync errors, data
        /// loss) that must never be silently dropped just because verbose debug is off. Still
        /// file-only (never console), and still crash-safe. Prefixed so it's greppable.
        /// </summary>
        public static void LogError(ICoreAPI api, string message, params object?[] args)
        {
            Write(SideTag(api?.Side), "ERROR " + Format(message, args));
        }

        private static void Write(string side, string text)
        {
            try
            {
                string path = ResolvePath();
                if (path == null) return;

                lock (_lock)
                {
                    // First write of the process: start the file fresh so each launch is
                    // self-contained and old sessions don't pile up unbounded.
                    if (!_truncated)
                    {
                        File.WriteAllText(path,
                            "=== Vintage Railroading debug log — session started " +
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" + Environment.NewLine);
                        _truncated = true;
                    }

                    string line = DateTime.Now.ToString("HH:mm:ss.fff") +
                        " [" + side + "] [vrr] " + text + Environment.NewLine;
                    File.AppendAllText(path, line);
                }
            }
            catch
            {
                // Logging must never crash a tick or render frame. Swallow any IO error
                // (file locked, path unwritable, etc.) silently.
            }
        }

        private static string ResolvePath()
        {
            if (_path != null) return _path;
            try
            {
                // GamePaths.Logs is the standard VS logs directory (where server-main.log
                // etc. live). Falls back to the current directory if unavailable.
                string dir = GamePaths.Logs;
                if (string.IsNullOrEmpty(dir)) dir = ".";
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, FileName);
            }
            catch
            {
                _path = FileName; // last resort: relative path
            }
            return _path;
        }

        private static string SideTag(EnumAppSide? side)
        {
            if (side == EnumAppSide.Server) return "SRV";
            if (side == EnumAppSide.Client) return "CLI";
            return "???";
        }

        private static string Format(string message, object?[] args)
        {
            if (args == null || args.Length == 0) return message;
            try { return string.Format(message, args); }
            catch { return message; } // never let a bad format string crash a tick
        }
    }
}
