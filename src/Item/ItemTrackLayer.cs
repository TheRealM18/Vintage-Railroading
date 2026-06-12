using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageRailroading.Track;
using GVec = VintageRailroading.Track.Vec3d;

namespace VintageRailroading.Items
{
    /// <summary>
    /// Survival-ready track laying. Hold this tool and:
    ///   1. Right-click a block where track should START  -> point A is set.
    ///   2. Right-click a block where track should END     -> a segment is laid
    ///      from A to B, then B AUTOMATICALLY becomes the start of the next segment
    ///      so you can keep clicking to chain track along the ground.
    /// Sneak (shift) + right-click clears the pending point and stops the chain.
    ///
    /// Each click's tangent direction comes from the player's look-yaw at that click,
    /// so curves follow the way you're facing. All the real work (grade, snapping,
    /// tangent inheritance, network registration, block placement, client sync) is
    /// delegated to VintageRailroadingModSystem.LaySegment so this behaves EXACTLY like
    /// the /vrrnode command — just driven by clicks instead.
    /// </summary>
    public class ItemTrackLayer : Item
    {
        // Pending start point per player (server-side only). The item instance is
        // shared across players, so we must key by player UID, not a single field.
        private readonly Dictionary<string, GVec> _pendingByPlayer = new Dictionary<string, GVec>();
        private readonly Dictionary<string, double> _pendingYawByPlayer = new Dictionary<string, double>();

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity,
            BlockSelection blockSel, EntitySelection entitySel, bool firstEvent,
            ref EnumHandHandling handling)
        {
            if (blockSel == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            // Server does the work; client just reports handled so the arm swings.
            if (api.Side == EnumAppSide.Server)
            {
                TryLay(byEntity, blockSel);
            }
            handling = EnumHandHandling.Handled;
        }

        private void TryLay(EntityAgent byEntity, BlockSelection blockSel)
        {
            var sapi = api as ICoreServerAPI;
            if (sapi == null) return;
            if (!(byEntity is EntityPlayer ep) || !(ep.Player is IServerPlayer sp)) return;
            string uid = sp.PlayerUID;

            // Sneak-click clears the pending point and ends the chain.
            if (byEntity.Controls.Sneak)
            {
                _pendingByPlayer.Remove(uid);
                _pendingYawByPlayer.Remove(uid);
                SendMsg(sp, "Track layer reset — chain cleared.");
                return;
            }

            // Click point: centered horizontally on the exact hit X/Z so track follows
            // where you aim, and placed on TOP of the clicked block (y+1) so the track
            // rides the ground surface. Snapping (in LaySegment) will fuse this onto a
            // nearby existing node if there is one, which is what makes chaining join up.
            var bp = blockSel.Position;
            var hit = blockSel.HitPosition;
            double cx = bp.X + (hit != null ? hit.X : 0.5);
            double cz = bp.Z + (hit != null ? hit.Z : 0.5);
            double cy = bp.Y + 1.0;
            var click = new GVec(cx, cy, cz);
            double yaw = byEntity.Pos.Yaw;

            var mod = sapi.ModLoader.GetModSystem<VintageRailroadingModSystem>();
            if (mod == null) { SendMsg(sp, "Track layer: mod system missing."); return; }

            if (!_pendingByPlayer.TryGetValue(uid, out var startPos))
            {
                // First click — set point A.
                _pendingByPlayer[uid] = click;
                _pendingYawByPlayer[uid] = yaw;
                SendMsg(sp, "Track START set. Right-click where it should END to lay a segment. Sneak-click to cancel.");
                return;
            }

            double startYaw = _pendingYawByPlayer.TryGetValue(uid, out var sy) ? sy : yaw;
            var result = mod.LaySegment(sapi, startPos, startYaw, click, yaw);

            if (!result.Ok)
            {
                // Keep the pending start so the player can re-aim the END without
                // re-clicking the start (e.g. "too close" just needs a farther click).
                SendMsg(sp, result.Message);
                return;
            }

            // Chain: the segment END becomes the next START, so continued clicks lay
            // continuous track. Snapping will fuse the next segment onto this node.
            _pendingByPlayer[uid] = result.EndPos;
            _pendingYawByPlayer[uid] = yaw;
            SendMsg(sp, result.Message + " (chaining — click again to continue, sneak-click to stop)");
        }

        private void SendMsg(IServerPlayer sp, string msg)
        {
            sp.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);
        }
    }
}
