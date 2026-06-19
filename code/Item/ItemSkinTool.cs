using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using VintageRailroading.Entities;

namespace VintageRailroading.Items
{
    /// <summary>
    /// The Paint Tool — a handheld item that swaps the cosmetic SKIN of a rail vehicle.
    ///
    /// Usage:
    ///   * Right-click a rail vehicle (loco or cargo car) -> cycles to its NEXT skin.
    ///   * Sneak + right-click                            -> cycles to the PREVIOUS skin.
    ///
    /// The model never changes — only the body texture — and the choice persists and syncs
    /// to all players (handled by EntityBehaviorSkinnable on the vehicle). Vehicles with no
    /// skins, or only one, simply report that there's nothing to change.
    ///
    /// This is the discoverable, intentional way to reskin: previously skin-cycling was only
    /// reachable via a sneak gesture on an empty hand, which clashed with opening storage and
    /// mounting. A dedicated tool keeps those interactions clean and makes reskinning obvious.
    /// </summary>
    public class ItemSkinTool : Item
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity,
            BlockSelection blockSel, EntitySelection entitySel, bool firstEvent,
            ref EnumHandHandling handling)
        {
            // Server performs the reskin; client reports handled so the arm swings.
            if (api.Side == EnumAppSide.Server)
            {
                TrySkin(byEntity, entitySel);
            }
            handling = EnumHandHandling.Handled;
        }

        private void TrySkin(EntityAgent byEntity, EntitySelection? entitySel)
        {
            var sapi = api as ICoreServerAPI;
            if (sapi == null) return;
            if (!(byEntity is EntityPlayer ep) || !(ep.Player is IServerPlayer sp)) return;

            var target = entitySel?.Entity;
            var skin = target?.GetBehavior<EntityBehaviorSkinnable>();
            if (skin == null)
            {
                Msg(sp, "Paint Tool: aim at a rail vehicle to change its skin.");
                return;
            }

            if (skin.SkinCount < 2)
            {
                Msg(sp, "Paint Tool: this vehicle has no alternate skins.");
                return;
            }

            // Sneak cycles backwards, otherwise forwards.
            bool back = byEntity.Controls?.Sneak == true;
            if (back) skin.CyclePrev();
            else skin.CycleSkin();

            Msg(sp, "Skin: " + skin.ActiveSkinName);
        }

        private static void Msg(IServerPlayer sp, string text)
        {
            sp.SendMessage(GlobalConstants.GeneralChatGroup, text, EnumChatType.Notification);
        }
    }
}
