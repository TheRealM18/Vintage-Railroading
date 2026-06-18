using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// A large-capacity liquid slot for a tank, sized in LITRES. Modeled directly on
    /// Vintage Engineering's ItemSlotLargeLiquid (FlexibleGames/VintageEngineering,
    /// code/VintageEngineering/API/ItemSlotLargeLiquid.cs): it extends VANILLA
    /// ItemSlotLiquidOnly (Vintagestory.GameContent) — the class VS already ships for
    /// liquid-only slots — and raises MaxSlotStackSize to capacityLitres * ItemsPerLitre
    /// so one slot holds a whole tank of one liquid.
    ///
    /// IMPORTANT: this is NOT named "ItemSlotLiquidOnly" — that name belongs to the
    /// vanilla class we inherit; reusing it would collide. We call it ItemSlotLiquidTank.
    ///
    /// Vanilla ItemSlotLiquidOnly already restricts the slot to pourable liquids (anything
    /// with WaterTightContainableProps), so accepting "any liquid" is simply the default —
    /// no token list or custom filter needed (unlike the wood slot).
    /// </summary>
    public class ItemSlotLiquidTank : ItemSlotLiquidOnly
    {
        public ItemSlotLiquidTank(InventoryBase inventory, float capacityLitres)
            : base(inventory, capacityLitres)
        {
            // Default cap assuming 100 items/litre (the common ItemsPerLitre). The exact
            // cap is recomputed per-liquid in GetRemainingSlotSpace once a liquid is known.
            MaxSlotStackSize = (int)(capacityLitres * 100);
        }

        /// <summary>True if the stack is a pourable liquid (has WaterTightContainableProps).
        /// Same test Vintage Engineering uses.</summary>
        public static bool IsLiquid(ItemStack stack)
        {
            if (stack?.Collectible == null) return false;
            return BlockLiquidContainerBase.GetContainableProps(stack) != null;
        }

        /// <summary>Recompute the slot's max units from the ACTUAL liquid's ItemsPerLitre so
        /// capacity is correct in litres regardless of the liquid. Mirrors VintageEngineering's
        /// ItemSlotLargeLiquid.GetRemainingSlotSpace.</summary>
        public override int GetRemainingSlotSpace(ItemStack forItemstack)
        {
            var props = BlockLiquidContainerBase.GetContainableProps(forItemstack);
            if (props != null)
            {
                int want = (int)(CapacityLitres * props.ItemsPerLitre);
                if (MaxSlotStackSize != want) MaxSlotStackSize = want;
                return MaxSlotStackSize - StackSize;
            }
            return MaxSlotStackSize;
        }
    }

    /// <summary>
    /// Gives a train/cargo entity (a fluid car / tanker) a LIQUID-ONLY tank with a GUI.
    /// Right-click the car with an empty hand to open it; drop a filled liquid container
    /// (bucket, jug, etc.) into the liquid slot to fill the tank, or take liquid back out.
    ///
    /// Tank: a single-slot InventoryGeneric whose slot is ItemSlotLiquidTank, sized in
    /// LITRES from JSON `capacityLitres`. Persisted in the entity's WatchedAttributes tree
    /// ("vrrfluidinv") so the contents survive save/reload — exactly like the wood car.
    ///
    /// Interaction: this behavior overrides OnInteract and, on an empty-handed (non-sneak)
    /// right click, opens the tank and sets handled = EnumHandling.PreventSubsequent so a
    /// seatable car opens the tank instead of seating the player. Sneak + empty hand falls
    /// through (lets a seat mount, if the entity has one). A seatless EntityCargo has no
    /// seat to compete with, so it simply opens.
    ///
    /// GUI: opened via the vanilla GuiDialogBlockEntityInventory, same as the wood car.
    /// One column is enough for a single tank slot.
    ///
    /// JSON: add to the entity's client AND server behaviors as
    ///   { "code": "fluidstorage", "capacityLitres": 200 }
    /// and (on a seatable car) place it BEFORE the creaturecarrier behavior.
    /// </summary>
    public class EntityBehaviorFluidStorage : EntityBehavior
    {
        private InventoryGeneric _inv;
        private GuiDialog _dialog;
        private float _capacityLitres = 200f;

        public EntityBehaviorFluidStorage(Entity entity) : base(entity) { }

        public InventoryGeneric Inventory => _inv;
        public override string PropertyName() => "fluidstorage";

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            _capacityLitres = attributes?["capacityLitres"].AsFloat(200f) ?? 200f;

            // A fluid car is one big tank: a single liquid slot. (If you ever want
            // multiple compartments, raise the slot count and give each its own capacity.)
            _inv = new InventoryGeneric(1, "vrrfluid-" + entity.EntityId, entity.Api,
                (id, inv) => new ItemSlotLiquidTank(inv, _capacityLitres));

            var tree = entity.WatchedAttributes.GetTreeAttribute("vrrfluidinv");
            if (tree != null) _inv.FromTreeAttributes(tree);
            _inv.ResolveBlocksOrItems();

            _inv.SlotModified += OnSlotModified;
        }

        private void OnSlotModified(int slotId)
        {
            if (entity.Api.Side != EnumAppSide.Server) return;
            var tree = new TreeAttribute();
            _inv.ToTreeAttributes(tree);
            entity.WatchedAttributes["vrrfluidinv"] = tree;
            entity.WatchedAttributes.MarkPathDirty("vrrfluidinv");
        }

        /// <summary>
        /// Empty-hand right-click opens the tank. On a seatable car this prevents the seat
        /// from also handling the click (PreventSubsequent); on a seatless cargo car it
        /// simply opens. Holding an item, or sneaking, falls through untouched.
        /// </summary>
        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition,
            EnumInteractMode mode, ref EnumHandling handled)
        {
            if (mode != EnumInteractMode.Interact)
            {
                base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
                return;
            }

            var eplr = byEntity as EntityPlayer;
            if (eplr?.Player == null)
            {
                base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
                return;
            }

            bool emptyHand = itemslot?.Itemstack == null;
            bool sneaking  = byEntity.Controls.Sneak;

            if (sneaking || !emptyHand)
            {
                base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
                return;
            }

            if (OpenFor(eplr.Player))
            {
                handled = EnumHandling.PreventSubsequent;
                return;
            }

            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
        }

        /// <summary>Open the fluid tank for a player. Returns true if handled.</summary>
        public bool OpenFor(IPlayer player)
        {
            if (player == null || _inv == null) return false;

            // Server: register the player as having the inventory open so edits sync.
            if (entity.Api.Side == EnumAppSide.Server)
            {
                _inv.Open(player);
                return true;
            }

            // Client: open (or toggle) the dialog. Bind to the block position under the
            // entity — GuiDialogBlockEntityInventory needs a BlockPos for sync routing.
            var capi = entity.Api as ICoreClientAPI;
            if (capi == null) return false;

            if (_dialog != null && _dialog.IsOpened()) { _dialog.TryClose(); return true; }

            _inv.Open(player);
            BlockPos pos = entity.ServerPos.AsBlockPos;
            // One column — a single tank slot. Title shows capacity for clarity.
            _dialog = new GuiDialogBlockEntityInventory(
                "Fluid Tank (" + (int)_capacityLitres + "L)", _inv, pos, 1, capi);
            _dialog.OnClosed += () => { _inv.Close(player); };
            _dialog.TryOpen();
            return true;
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (_dialog != null && _dialog.IsOpened()) _dialog.TryClose();
            base.OnEntityDespawn(despawn);
        }

        /// <summary>Drop the tank's liquid container stack into the world (call when the car
        /// is removed so the cargo isn't lost). Server-side.
        ///
        /// NOTE: this drops the liquid as its itemstack form. Loose liquids in the world
        /// behave like any dropped item; if you'd rather SPILL the liquid (puddle/destroy)
        /// on pickup, replace DropAll with your own handling here.</summary>
        public void DropContents()
        {
            if (entity.Api.Side != EnumAppSide.Server || _inv == null) return;
            // Drop slot-by-slot so we can log and so a stuck DropAll can't silently void
            // cargo. Spawn each stack at the entity position.
            int dropped = 0;
            var pos = entity.ServerPos.XYZ;
            foreach (var slot in _inv)
            {
                if (slot?.Itemstack == null) continue;
                entity.World.SpawnItemEntity(slot.Itemstack.Clone(), pos);
                dropped += slot.Itemstack.StackSize;
                slot.Itemstack = null;
                slot.MarkDirty();
            }
            VrrDebug.Log(entity.Api, "DropContents: dropped {0} item(s) from {1}", dropped, GetType().Name);
        }
    }
}