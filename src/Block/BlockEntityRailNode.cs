using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageRailroading.Render;
using GVec = VintageRailroading.Track.Vec3d;
using HermiteCurve = VintageRailroading.Track.HermiteCurve;
using TrackSegment = VintageRailroading.Track.TrackSegment;
using TrackGauge = VintageRailroading.Track.TrackGauge;

namespace VintageRailroading.Blocks
{
    /// <summary>
    /// Layer 1a block entity. Owns ONE track segment's curve data and persists
    /// it to the savegame. To prove persistence visually it redraws the rail as
    /// a particle trail every couple of seconds from its STORED data — so if the
    /// trail reappears after a save/reload, persistence works. (Layer 1b will
    /// replace the particles with a real mesh in OnTesselation.)
    ///
    /// Persisted fields: start xyz, start tangent xyz, end xyz, end tangent xyz,
    /// and the gauge id. The HermiteCurve/TrackSegment are rebuilt from those.
    /// </summary>
    public class BlockEntityRailNode : BlockEntity
    {
        // Raw persisted curve definition.
        private double _sx, _sy, _sz;     // start point
        private double _tsx, _tsy, _tsz;  // start tangent
        private double _ex, _ey, _ez;     // end point
        private double _tex, _tey, _tez;  // end tangent
        private string _gaugeId = "standard";
        private bool _hasCurve;

        // Rebuilt (not persisted) — derived from the above.
        private TrackSegment? _segment;

        // Frozen station arrays for the tesselation thread (built on main thread).
        private RailMeshBuilder.Station[]? _railStations;
        private RailMeshBuilder.Station[]? _tieStations;

        // Cached base cube mesh (client only).
        private MeshData? _baseMesh;
        // Cached tie-textured cube mesh (client only) — the base mesh with its UVs
        // retargeted from the 'rail' atlas tile to the 'tie' atlas tile. Null until
        // built, and stays null (falls back to rail) if the tie texture can't resolve.
        private MeshData? _tieMesh;
        private ICoreClientAPI? _capi;

        /// <summary>Called after placement to give this node its finished curve.
        /// World-space args; we store them and mark dirty for saving.</summary>
        public void SetCurve(GVec start, GVec startTangent, GVec end, GVec endTangent, string gaugeId)
        {
            _sx = start.X; _sy = start.Y; _sz = start.Z;
            _tsx = startTangent.X; _tsy = startTangent.Y; _tsz = startTangent.Z;
            _ex = end.X; _ey = end.Y; _ez = end.Z;
            _tex = endTangent.X; _tey = endTangent.Y; _tez = endTangent.Z;
            _gaugeId = gaugeId;
            _hasCurve = true;
            RebuildSegment();
            MarkDirty(true);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            _capi = api as ICoreClientAPI;
            RebuildSegment(); // Api is set now; FromTreeAttributes already ran.
            // Redraw the persisted trail every 2s as the persistence proof.
            RegisterGameTickListener(OnTick, 2000);
        }

        private void RebuildSegment()
        {
            if (!_hasCurve) return;
            TrackGauge gauge = GaugeFromId(_gaugeId);
            var curve = new HermiteCurve(
                new GVec(_sx, _sy, _sz),
                new GVec(_tsx, _tsy, _tsz),
                new GVec(_ex, _ey, _ez),
                new GVec(_tex, _tey, _tez));
            _segment = new TrackSegment(1, gauge, curve);

            // Freeze station arrays for the tesselation thread.
            _railStations = RailMeshBuilder.BuildStations(_segment, RailMeshBuilder.RailStep);
            _tieStations  = RailMeshBuilder.BuildStations(_segment, RailMeshBuilder.TieStep);

            // If we're on the client and already initialized, force a retesselate.
            if (Api != null && Api.Side == EnumAppSide.Client)
            {
                MarkDirty(true);
            }
        }

        private void OnTick(float dt)
        {
            // Particle trail disabled now that OnTesselation renders a real mesh.
            // Kept as a no-op hook in case we want a fallback toggle later.
            return;
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (_railStations == null || _tieStations == null || Block == null)
                return false; // no curve yet — let the default cube render

            // Build + cache the base cube mesh once (client). Uses OUR block's
            // own texture (the 'rail' tile via blocktype 'all'), so no crash.
            if (_baseMesh == null)
            {
                tessThreadTesselator.TesselateBlock(Block, out _baseMesh);
            }
            if (_baseMesh == null) return false;

            // Build the tie mesh once: clone the rail mesh and retarget its UVs from
            // the 'rail' atlas tile onto the 'tie' atlas tile. This reuses the proven
            // cube mesh (no second tesselation that could NRE on a missing texture).
            // If the tie texture can't resolve, _tieMesh stays null and ties fall back
            // to the rail texture — no crash, just the previous look.
            if (_tieMesh == null)
            {
                _tieMesh = BuildTieMesh(_baseMesh);
            }

            double gaugeWidth = GaugeFromId(_gaugeId).WidthMeters;
            RailMeshBuilder.Emit(mesher, _baseMesh, Pos, gaugeWidth, _railStations, _tieStations, _tieMesh);

            return true; // we drew it; skip the default cube
        }

        /// <summary>Clone the rail-textured cube mesh and remap its UVs onto the 'tie'
        /// texture's atlas tile, so ties render with the wood texture. Returns null if
        /// the client API or either atlas tile is unavailable (caller falls back to the
        /// rail mesh). Runs on the tesselation thread; only reads immutable atlas data.</summary>
        private MeshData? BuildTieMesh(MeshData baseMesh)
        {
            // NOTE (unverified VS API — verify against your local VintagestoryAPI.dll):
            //   * CompositeTexture.Baked.TextureSubId
            //   * ICoreClientAPI.BlockTextureAtlas.Positions[subId] -> TextureAtlasPosition
            //   * TextureAtlasPosition.x1/y1/x2/y2  (float atlas UV bounds)
            //   * MeshData.Uv  (float[] interleaved u,v)  and  MeshData.Clone()
            // These are the standard names as of recent VS, but if the build fails it
            // will be on ONE of these lines. The whole method is wrapped so any miss
            // degrades to null -> ties keep the rail texture (no crash, prior look).
            try
            {
                if (_capi == null || Block == null) return null;
                if (Block.Textures == null) return null;
                if (!Block.Textures.TryGetValue("rail", out var railCt) &&
                    !Block.Textures.TryGetValue("all", out railCt)) return null;
                if (!Block.Textures.TryGetValue("tie", out var tieCt)) return null;
                if (railCt?.Baked == null || tieCt?.Baked == null) return null;

                var positions = _capi.BlockTextureAtlas.Positions;
                int railId = railCt.Baked.TextureSubId;
                int tieId = tieCt.Baked.TextureSubId;
                if (railId < 0 || tieId < 0 ||
                    railId >= positions.Length || tieId >= positions.Length) return null;
                var railPos = positions[railId];
                var tiePos = positions[tieId];
                if (railPos == null || tiePos == null) return null;

                float railW = railPos.x2 - railPos.x1;
                float railH = railPos.y2 - railPos.y1;
                float tieW = tiePos.x2 - tiePos.x1;
                float tieH = tiePos.y2 - tiePos.y1;
                if (railW <= 0 || railH <= 0) return null;

                var clone = baseMesh.Clone();
                var uv = clone.Uv;
                if (uv == null) return null;
                // Convert each UV from its fractional position within the rail tile to
                // the equivalent spot in the tie tile.
                for (int i = 0; i + 1 < uv.Length; i += 2)
                {
                    float fu = (uv[i]     - railPos.x1) / railW;
                    float fv = (uv[i + 1] - railPos.y1) / railH;
                    uv[i]     = tiePos.x1 + fu * tieW;
                    uv[i + 1] = tiePos.y1 + fv * tieH;
                }
                return clone;
            }
            catch
            {
                return null; // any API mismatch -> safe fallback to rail texture
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool("vrr_has", _hasCurve);
            if (!_hasCurve) return;
            tree.SetDouble("vrr_sx", _sx); tree.SetDouble("vrr_sy", _sy); tree.SetDouble("vrr_sz", _sz);
            tree.SetDouble("vrr_tsx", _tsx); tree.SetDouble("vrr_tsy", _tsy); tree.SetDouble("vrr_tsz", _tsz);
            tree.SetDouble("vrr_ex", _ex); tree.SetDouble("vrr_ey", _ey); tree.SetDouble("vrr_ez", _ez);
            tree.SetDouble("vrr_tex", _tex); tree.SetDouble("vrr_tey", _tey); tree.SetDouble("vrr_tez", _tez);
            tree.SetString("vrr_gauge", _gaugeId ?? "standard");
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
        {
            base.FromTreeAttributes(tree, world);
            // NOTE: Api is NOT set yet here — only read primitive data, no world calls.
            _hasCurve = tree.GetBool("vrr_has", false);
            if (!_hasCurve) return;
            _sx = tree.GetDouble("vrr_sx"); _sy = tree.GetDouble("vrr_sy"); _sz = tree.GetDouble("vrr_sz");
            _tsx = tree.GetDouble("vrr_tsx"); _tsy = tree.GetDouble("vrr_tsy"); _tsz = tree.GetDouble("vrr_tsz");
            _ex = tree.GetDouble("vrr_ex"); _ey = tree.GetDouble("vrr_ey"); _ez = tree.GetDouble("vrr_ez");
            _tex = tree.GetDouble("vrr_tex"); _tey = tree.GetDouble("vrr_tey"); _tez = tree.GetDouble("vrr_tez");
            _gaugeId = tree.GetString("vrr_gauge", "standard");

            // Rebuild the segment + stations from the freshly synced data. Safe
            // without Api (pure math, no world calls). This is what makes the
            // CLIENT have stations to render — without it OnTesselation sees null
            // and falls back to the plain cube.
            RebuildSegment();

            // If the client already has its Api (data arrived after Initialize),
            // force a retesselate so the rails show up immediately.
            if (Api != null && Api.Side == EnumAppSide.Client)
            {
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        private static TrackGauge GaugeFromId(string id) => id switch
        {
            "narrow"   => TrackGauge.Narrow,
            "metre"    => TrackGauge.Metre,
            "broad"    => TrackGauge.Broad,
            _          => TrackGauge.Standard,
        };
    }
}
