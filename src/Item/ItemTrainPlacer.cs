using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageRailroading.Entities;
using VintageRailroading.Track;

namespace VintageRailroading.Items
{
    /// <summary>
    /// Right-click on/near track to place a train at THAT point — the segment
    /// nearest the clicked block, at the closest distance along it. Fixes the
    /// "always spawns on the same segment" problem of the command, which guessed
    /// nearest-to-player.
    /// </summary>
    public class ItemTrainPlacer : Item
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity,
            BlockSelection blockSel, EntitySelection entitySel, bool firstEvent,
            ref EnumHandHandling handling)
        {
            // Need a block target to anchor placement.
            if (blockSel == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            // Server does the spawn; client just reports handled.
            if (api.Side == EnumAppSide.Server)
            {
                TrySpawn(byEntity, blockSel);
            }

            handling = EnumHandHandling.Handled;
        }

        private void TrySpawn(EntityAgent byEntity, BlockSelection blockSel)
        {
            var sapi = api as ICoreServerAPI;
            if (sapi == null) return;

            var mgr = sapi.ModLoader.GetModSystem<TrackNetworkManager>();
            var network = mgr?.Network;
            if (network == null || network.Segments.Count == 0)
            {
                SendMsg(byEntity, "No track network — lay some rail first.");
                return;
            }

            // The clicked point in world space.
            var click = blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5);

            // Find the segment + distance whose closest sampled point is nearest
            // the click. HORIZONTAL distance only — the curve Y (node height) can
            // differ from the clicked block center by ~1m, which would otherwise
            // wrongly trip the "too far" check even when clicking right on a rail.
            long bestSeg = 0; double bestDist = 0; double bestD2 = double.MaxValue;
            foreach (var segData in network.Segments)
            {
                var geom = network.BuildGeometry(segData.Id);
                if (geom == null) continue;
                int samples = Math.Max(4, (int)(geom.Length / 0.5));
                for (int i = 0; i <= samples; i++)
                {
                    double s = geom.Length * i / samples;
                    var p = geom.PositionAtDistance(s);
                    double dx = p.X - click.X;
                    double dz = p.Z - click.Z;
                    double d2 = dx * dx + dz * dz; // horizontal only
                    if (d2 < bestD2) { bestD2 = d2; bestSeg = segData.Id; bestDist = s; }
                }
            }

            if (bestSeg == 0) { SendMsg(byEntity, "Could not find a track point near there."); return; }

            // Only place if the click was reasonably close to the track (4 blocks).
            if (bestD2 > 9.0) { SendMsg(byEntity, "Aim closer to a rail to place a train (nearest rail is " + Math.Sqrt(bestD2).ToString("0.0") + "m away)."); return; }

            // Which entity to spawn is data-driven: each placer itemtype declares
            // `attributes.entityCode` (e.g. "train", "coalcart"). This lets MANY train
            // types share this one ItemTrainPlacer class — no new C# class per car.
            // Defaults to "train" so the original placer keeps working unchanged.
            string entityCode = Attributes?["entityCode"]?.AsString("train") ?? "train";
            var etype = sapi.World.GetEntityType(new AssetLocation("vintagerailroading:" + entityCode));
            if (etype == null) { SendMsg(byEntity, $"entity type '{entityCode}' missing — check entities/{entityCode}.json and its code."); return; }

            var entity = sapi.ClassRegistry.CreateEntity(etype);
            var train = entity as EntityTrain;
            if (train == null) { SendMsg(byEntity, $"'{entityCode}' is not an EntityTrain (needs class: EntityTrain to ride rails)."); return; }

            // Set a position before spawn, then place on the network segment.
            var startGeom = network.BuildGeometry(bestSeg);
            if (startGeom != null)
            {
                var sp = startGeom.PositionAtDistance(bestDist);
                train.Pos.SetPos(sp.X, sp.Y, sp.Z);
            }
            sapi.World.SpawnEntity(train);
            train.PlaceOnSegment(network, bestSeg, bestDist);

            SendMsg(byEntity, $"Placed '{entityCode}' on segment #{bestSeg} at {bestDist:0.0}m. Sit in it (right-click) and drive with W/S, or /vrrcouple it behind a loco.");
        }

        private void SendMsg(EntityAgent byEntity, string msg)
        {
            if (byEntity is EntityPlayer ep && ep.Player is IServerPlayer sp)
            {
                sp.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);
            }
        }
    }
}
