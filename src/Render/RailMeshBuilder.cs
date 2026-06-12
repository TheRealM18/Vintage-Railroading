using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using GVec = VintageRailroading.Track.Vec3d;
using TrackSegment = VintageRailroading.Track.TrackSegment;

namespace VintageRailroading.Render
{
    /// <summary>
    /// Adds rail + tie meshes along a track centerline by tiling a single base
    /// block mesh, using the engine's own idiom (see vssurvivalmod BEIngotMold):
    /// build the base mesh once, then for each station add it to the mesher with
    /// a per-station Mat4f transform.
    ///
    /// THREAD SAFETY: called from OnTesselation (tess thread). Reads only the
    /// frozen Station[] and the base mesh; touches no mutable BE state.
    /// Transform math validated in tests/proto_transform.py.
    /// </summary>
    public static class RailMeshBuilder
    {
        public struct Station
        {
            public double Px, Py, Pz;   // world centerline position
            public double Hx, Hy, Hz;   // full 3D heading (Hy gives the grade pitch)
        }

        public const double RailStep = 0.5;
        public const double TieStep  = 1.5;

        // scale of the base 1x1x1 block for each piece (blocks)
        private const float RailW = 0.10f, RailH = 0.12f, RailL = 0.55f;
        private const float TieW  = 0.22f, TieH  = 0.08f;

        /// <summary>Precompute stations (MAIN thread). Frozen array, tess-safe.</summary>
        public static Station[] BuildStations(TrackSegment seg, double step)
        {
            int n = Math.Max(2, (int)(seg.Length / step));
            var arr = new Station[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double s = seg.Length * i / n;
                GVec p = seg.PositionAtDistance(s);
                GVec h = seg.HeadingAtDistance(s);
                arr[i] = new Station { Px = p.X, Py = p.Y, Pz = p.Z, Hx = h.X, Hy = h.Y, Hz = h.Z };
            }
            return arr;
        }

        /// <summary>
        /// Emit the rails and ties into the mesher. railMesh is a 1x1x1 cube mesh
        /// (already textured with the rail texture) centered on the block. tieMesh, if
        /// non-null, textures the ties (e.g. with the wood-tie texture); when null the
        /// ties reuse railMesh so existing behavior is unchanged. anchor is the BE
        /// block origin.
        /// </summary>
        public static void Emit(ITerrainMeshPool mesher, MeshData railMesh, BlockPos anchor,
                                double gaugeWidth, Station[] rails, Station[] ties,
                                MeshData tieMesh = null)
        {
            var tieBase = tieMesh ?? railMesh;
            double half = gaugeWidth * 0.5;

            // Rails: two thin boxes offset +/- half, dense stations.
            foreach (var st in rails)
            {
                float yaw = (float)Math.Atan2(st.Hx, st.Hz);
                double len = Math.Sqrt(st.Hx * st.Hx + st.Hz * st.Hz);
                // pitch up/down along the grade (rotate about the lateral axis)
                float pitch = (float)Math.Atan2(st.Hy, len);
                double lx = (len < 1e-9) ? 0 : (st.Hz / len);   // (Hz, -Hx) normalized = lateral
                double lz = (len < 1e-9) ? 0 : (-st.Hx / len);

                for (int side = -1; side <= 1; side += 2)
                {
                    double cx = st.Px + lx * side * half;
                    double cy = st.Py;
                    double cz = st.Pz + lz * side * half;
                    AddPiece(mesher, railMesh, anchor, cx, cy, cz, yaw, pitch, RailW, RailH, RailL, 0.06f);
                }
            }

            // Ties: one flat wide box per sparse station, spanning the gauge.
            float tieSpan = (float)(gaugeWidth + 0.30);
            foreach (var st in ties)
            {
                float yaw = (float)Math.Atan2(st.Hx, st.Hz);
                double len = Math.Sqrt(st.Hx * st.Hx + st.Hz * st.Hz);
                float pitch = (float)Math.Atan2(st.Hy, len);
                AddPiece(mesher, tieBase, anchor, st.Px, st.Py, st.Pz, yaw, pitch, tieSpan, TieH, TieW, 0.0f);
            }
        }

        // Build a Mat4f that places a unit cube as a (w,h,l) box at world (cx,cy,cz),
        // rotated yaw about Y then pitch about the local X (lateral) axis so the piece
        // follows the track grade. Expressed relative to the anchor block.
        private static void AddPiece(ITerrainMeshPool mesher, MeshData baseMesh, BlockPos anchor,
                                     double cx, double cy, double cz, float yaw, float pitch,
                                     float w, float h, float l, float yOff)
        {
            float tx = (float)(cx - anchor.X);
            float ty = (float)(cy - anchor.Y) + yOff;
            float tz = (float)(cz - anchor.Z);

            float[] m = Mat4f.Create();
            Mat4f.Translate(m, m, tx, ty, tz);
            Mat4f.RotateY(m, m, yaw);
            // Pitch about local X: with forward along +Z, -pitch tips the +Z end UP
            // when climbing (Hy>0). If grades look inverted in-game, flip this sign.
            Mat4f.RotateX(m, m, -pitch);
            // scale a centered unit cube to the box dimensions
            Mat4f.Scale(m, m, new float[] { w, h, l });
            // center the cube on its own origin (cube spans 0..1, shift to -0.5..0.5)
            Mat4f.Translate(m, m, -0.5f, 0f, -0.5f);

            mesher.AddMeshData(baseMesh, m);
        }
    }
}
