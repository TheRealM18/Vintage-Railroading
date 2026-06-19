using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using VintageRailroading.Entities;

namespace VintageRailroading.Items
{
    /// <summary>
    /// The Coupler — a durability tool that links and unlinks rail vehicles, replacing the
    /// old /vrrcouple and /vrruncouple chat commands.
    ///
    /// Usage:
    ///   * Right-click a rail vehicle (loco or cargo car) -> SELECTS it as the leader.
    ///   * Right-click a second rail vehicle             -> COUPLES it behind the first
    ///     (second becomes the follower), costs 1 durability, and clears the selection.
    ///   * Sneak + right-click a coupled vehicle          -> UNCOUPLES it from its leader,
    ///     costs 1 durability.
    ///   * Right-click empty air / a block                -> clears any pending selection.
    ///
    /// Tiers: copper / tinbronze / iron / steel itemtypes share this class and differ only
    /// in `durability` (set in their JSON), exactly like vanilla tool tiers. Higher tiers
    /// last longer. When durability hits 0 the tool breaks like any other.
    ///
    /// The actual leader/follower fields live on IRailVehicle (LeaderEntityId, CouplingGap),
    /// so this works on EntityTrain and EntityCargo alike.
    /// </summary>
    public class ItemCouplingTool : Item
    {
        // Pending "first selected" vehicle per player (server-side). The item instance is
        // shared across players, so key by player UID rather than a single field.
        private readonly Dictionary<string, long> _selectedByPlayer = new Dictionary<string, long>();

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity,
            BlockSelection blockSel, EntitySelection entitySel, bool firstEvent,
            ref EnumHandHandling handling)
        {
            // Server does the work; client just reports handled so the arm swings.
            if (api.Side == EnumAppSide.Server)
            {
                TryCouple(slot, byEntity, entitySel);
            }
            handling = EnumHandHandling.Handled;
        }

        private void TryCouple(ItemSlot slot, EntityAgent byEntity, EntitySelection entitySel)
        {
            var sapi = api as ICoreServerAPI;
            if (sapi == null) return;
            if (!(byEntity is EntityPlayer ep) || !(ep.Player is IServerPlayer sp)) return;
            string uid = sp.PlayerUID;

            // Clicked something that isn't a rail vehicle (air, block, animal): clear and bail.
            var targetEnt = entitySel?.Entity;
            var rail = targetEnt as IRailVehicle;
            if (rail == null || targetEnt == null)
            {
                if (_selectedByPlayer.Remove(uid))
                    Msg(sp, "Coupler: selection cleared.");
                return;
            }

            // SNEAK + click on a coupled vehicle -> uncouple it.
            if (byEntity.Controls.Sneak)
            {
                if (rail.LeaderEntityId != 0)
                {
                    long was = rail.LeaderEntityId;
                    rail.LeaderEntityId = 0;
                    rail.Speed = 0;
                    DamageTool(slot, byEntity);
                    Msg(sp, $"Coupler: uncoupled #{targetEnt.EntityId} from leader #{was}.");
                }
                else
                {
                    Msg(sp, "Coupler: that vehicle isn't coupled to anything.");
                }
                _selectedByPlayer.Remove(uid);
                return;
            }

            // No selection yet -> select this vehicle as the leader.
            if (!_selectedByPlayer.TryGetValue(uid, out long firstId))
            {
                _selectedByPlayer[uid] = targetEnt.EntityId;
                Msg(sp, $"Coupler: selected #{targetEnt.EntityId} as leader. Right-click another vehicle to couple it behind.");
                return;
            }

            // Clicked the SAME vehicle twice -> just re-confirm selection.
            if (firstId == targetEnt.EntityId)
            {
                Msg(sp, "Coupler: that's the same vehicle. Right-click a DIFFERENT one to couple.");
                return;
            }

            // Resolve the previously-selected leader entity.
            var leaderEnt = sapi.World.GetEntityById(firstId);
            var leader = leaderEnt as IRailVehicle;
            if (leader == null || !leaderEnt.Alive)
            {
                // Leader is gone — treat this click as a fresh selection instead.
                _selectedByPlayer[uid] = targetEnt.EntityId;
                Msg(sp, $"Coupler: previous selection was lost; selected #{targetEnt.EntityId} instead.");
                return;
            }

            // Couple: the just-clicked vehicle (rail) becomes the FOLLOWER of the leader.
            var follower = rail;
            var followerEnt = targetEnt;
            follower.LeaderEntityId = leaderEnt.EntityId;

            // Gap = current spacing, clamped to a sane range so they don't jump on couple.
            double gap = followerEnt.Pos.XYZ.DistanceTo(leaderEnt.Pos.XYZ);
            if (gap < 1.0 || gap > 20.0) gap = 6.0;
            follower.CouplingGap = gap;

            DamageTool(slot, byEntity);
            _selectedByPlayer.Remove(uid);
            Msg(sp, $"Coupler: coupled #{followerEnt.EntityId} behind #{leaderEnt.EntityId} at {gap:0.0}m. Drive the leader to pull it.");
        }

        /// <summary>Consume one point of durability and break the tool at zero.</summary>
        private void DamageTool(ItemSlot slot, EntityAgent byEntity)
        {
            if (slot?.Itemstack == null) return;
            // VERIFY: standard CollectibleObject.DamageItem(IWorldAccessor, Entity, ItemSlot, int)
            // signature for 1.22. If the build complains, check the arg order against your API.
            DamageItem(byEntity.World, byEntity, slot, 1);
            slot.MarkDirty();
        }

        private void Msg(IServerPlayer sp, string text)
        {
            sp.SendMessage(GlobalConstants.GeneralChatGroup, text, EnumChatType.Notification);
        }
    }
}
