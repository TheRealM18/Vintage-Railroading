using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// A slot that only accepts FUEL — any item whose CombustibleProps has a positive
    /// BurnDuration (coal, charcoal, firewood, peat, and any modded fuel). Mod-agnostic:
    /// it inspects combustible properties rather than hard-coded item codes.
    /// </summary>
    public class ItemSlotFuelOnly : ItemSlot
    {
        public ItemSlotFuelOnly(InventoryBase inventory) : base(inventory) { }

        public static bool IsFuel(ItemStack? stack)
        {
            if (stack?.Collectible == null) return false;
            var cp = stack.Collectible.CombustibleProps;
            return cp != null && cp.BurnDuration > 0;
        }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            return base.CanHold(sourceSlot) && IsFuel(sourceSlot?.Itemstack);
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return base.CanTakeFrom(sourceSlot, priority) && IsFuel(sourceSlot?.Itemstack);
        }
    }

    /// <summary>
    /// Gives a train entity (the coal cart) a FUEL-ONLY storage inventory with a GUI.
    /// Right-click the cart with an empty hand to open it.
    ///
    /// Inventory: an InventoryGeneric whose slots are ItemSlotFuelOnly, persisted in the
    /// entity's WatchedAttributes tree ("vrrfuelinv") so cargo survives save/reload.
    ///
    /// GUI: opened via the vanilla GuiDialogBlockEntityInventory using the verified ctor
    ///   (string title, InventoryBase inv, BlockPos pos, int cols, ICoreClientAPI capi).
    /// The inventory is Open()'d for the player on both sides so slot moves sync through
    /// the engine's standard inventory networking (no custom packet plumbing).
    ///
    /// JSON: add to the entity's client AND server behaviors as
    ///   { "code": "fuelstorage", "quantitySlots": 16 }
    /// </summary>
    public class EntityBehaviorFuelStorage : EntityBehavior
    {
        private InventoryGeneric _inv = null!;
        private GuiDialog? _dialog;
        private int _slots = 16;

        public EntityBehaviorFuelStorage(Entity entity) : base(entity) { }

        public InventoryGeneric Inventory => _inv;
        public override string PropertyName() => "fuelstorage";

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            _slots = attributes?["quantitySlots"].AsInt(16) ?? 16;

            _inv = new InventoryGeneric(_slots, "vrrfuel-" + entity.EntityId, entity.Api,
                (id, inv) => new ItemSlotFuelOnly(inv));

            var tree = entity.WatchedAttributes.GetTreeAttribute("vrrfuelinv");
            VrrDebug.Log(entity.Api, "FuelStorage.Initialize: tree {0} on load (entity {1})",
                tree != null ? "PRESENT" : "NULL", entity.EntityId);
            if (tree != null) _inv.FromTreeAttributes(tree);
            _inv.ResolveBlocksOrItems();

            _inv.SlotModified += OnSlotModified;
        }

        private void OnSlotModified(int slotId)
        {
            if (entity.Api.Side != EnumAppSide.Server) return;
            VrrDebug.Log(entity.Api, "FuelStorage.OnSlotModified slot={0} -> writing tree (entity {1})", slotId, entity.EntityId);
            var tree = new TreeAttribute();
            _inv.ToTreeAttributes(tree);
            entity.WatchedAttributes["vrrfuelinv"] = tree;
            entity.WatchedAttributes.MarkPathDirty("vrrfuelinv");
        }

        /// <summary>Open the fuel storage for a player (called from EntityTrain.OnInteract).
        /// Returns true if handled.</summary>
        public bool OpenFor(IPlayer player)
        {
            if (player == null || _inv == null) return false;

            // Server: register the player as having the inventory open so slot edits sync.
            if (entity.Api.Side == EnumAppSide.Server)
            {
                // Register THIS inventory object in the player's open-inventory network so
                // the engine's standard slot-move packets operate on _inv server-side.
                // (_inv.Open alone does not wire up that packet routing for an entity.)
                player.InventoryManager.OpenInventory(_inv);
                return true;
            }

            // Client: open (or toggle) the dialog. Bind to the block position under the
            // entity — GuiDialogBlockEntityInventory needs a BlockPos for sync routing.
            var capi = entity.Api as ICoreClientAPI;
            if (capi == null) return false;

            if (_dialog != null && _dialog.IsOpened()) { _dialog.TryClose(); return true; }

            _inv.Open(player);
            BlockPos pos = entity.Pos.AsBlockPos;
            _dialog = new GuiDialogBlockEntityInventory("Fuel Storage", _inv, pos, 8, capi);
            _dialog.OnClosed += () => { _inv.Close(player); };
            _dialog.TryOpen();
            return true;
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (_dialog != null && _dialog.IsOpened()) _dialog.TryClose();
            base.OnEntityDespawn(despawn);
        }

        /// <summary>Drop all stored fuel into the world (call when the cart is removed so
        /// the cargo isn't lost). Server-side.</summary>
        public void DropContents()
        {
            if (entity.Api.Side != EnumAppSide.Server || _inv == null) return;
            // Drop slot-by-slot so we can log and so a stuck DropAll can't silently void
            // cargo. Spawn each stack at the entity position.
            int dropped = 0;
            var pos = entity.Pos.XYZ;
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