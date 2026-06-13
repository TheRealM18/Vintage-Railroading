using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VintageRailroading.Entities
{
    /// <summary>
    /// Gives any entity a generic storage inventory (accepts ALL items) with a GUI.
    /// Right-click the entity with an empty hand to open it.
    ///
    /// Inventory: an InventoryGeneric with plain ItemSlots, persisted in the entity's
    /// WatchedAttributes tree ("vrrgeneralinv") so cargo survives save/reload.
    ///
    /// Interaction: this behavior overrides OnInteract and, on an empty-handed
    /// (non-sneak) right click, opens the storage and sets
    /// handled = EnumHandling.PreventSubsequent. Because this behavior is listed BEFORE
    /// creaturecarrier in the entity JSON, that stops creaturecarrier from running on the
    /// same interaction, so the player opens the inventory instead of sitting down.
    /// Sneak + empty hand falls through, allowing the seat to mount as normal.
    ///
    /// GUI: opened via the vanilla GuiDialogBlockEntityInventory using the verified ctor
    ///   (string title, InventoryBase inv, BlockPos pos, int cols, ICoreClientAPI capi).
    /// The inventory is Open()'d for the player on both sides so slot moves sync through
    /// the engine's standard inventory networking (no custom packet plumbing).
    ///
    /// Defaults: title = "Storage", slots = 16, columns = 8.
    ///
    /// JSON: add to the entity's client AND server behaviors as
    ///   { "code": "genericstorage" }
    /// and place it BEFORE the creaturecarrier behavior if you want inventory to take
    /// priority over sitting on non-sneak right-click.
    /// </summary>
    public class EntityBehaviorGenericStorage : EntityBehavior
    {
        // ── Constants ────────────────────────────────────────────────────────────
        private const string InvKey      = "vrrgeneralinv";
        private const string GuiTitle    = "Storage";
        private const int    DefaultSlots = 8;
        private const int    GuiColumns   = 4;

        // ── State ─────────────────────────────────────────────────────────────────
        private InventoryGeneric _inv;
        private GuiDialog        _dialog;

        // ── Construction ──────────────────────────────────────────────────────────
        public EntityBehaviorGenericStorage(Entity entity) : base(entity) { }

        public InventoryGeneric Inventory    => _inv;
        public override string  PropertyName() => "genericstorage";

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            // Build the inventory with plain ItemSlots (accepts everything).
            _inv = new InventoryGeneric(
                DefaultSlots,
                InvKey + "-" + entity.EntityId,
                entity.Api,
                (id, inv) => new ItemSlot(inv)   // no filter — any item accepted
            );

            // Restore persisted contents if they exist.
            var tree = entity.WatchedAttributes.GetTreeAttribute(InvKey);
            if (tree != null) _inv.FromTreeAttributes(tree);
            _inv.ResolveBlocksOrItems();

            _inv.SlotModified += OnSlotModified;
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (_dialog != null && _dialog.IsOpened()) _dialog.TryClose();
            base.OnEntityDespawn(despawn);
        }

        // ── Persistence ───────────────────────────────────────────────────────────

        private void OnSlotModified(int slotId)
        {
            if (entity.Api.Side != EnumAppSide.Server) return;

            var tree = new TreeAttribute();
            _inv.ToTreeAttributes(tree);
            entity.WatchedAttributes[InvKey] = tree;
            entity.WatchedAttributes.MarkPathDirty(InvKey);
        }

        // ── Interaction ───────────────────────────────────────────────────────────

        /// <summary>
        /// Open the generic storage for a player. Returns true if handled.
        ///
        /// Server side: registers the player so slot edits sync.
        /// Client side: opens (or toggles) the GUI dialog.
        /// </summary>
        public bool OpenFor(IPlayer player)
        {
            if (player == null || _inv == null) return false;

            if (entity.Api.Side == EnumAppSide.Server)
            {
                _inv.Open(player);
                return true;
            }

            var capi = entity.Api as ICoreClientAPI;
            if (capi == null) return false;

            // Toggle: close if already open.
            if (_dialog != null && _dialog.IsOpened())
            {
                _dialog.TryClose();
                return true;
            }

            _inv.Open(player);

            // GuiDialogBlockEntityInventory needs a BlockPos for network routing;
            // we use the block position directly under the entity.
            BlockPos pos = entity.ServerPos.AsBlockPos;
            _dialog = new GuiDialogBlockEntityInventory(GuiTitle, _inv, pos, GuiColumns, capi);
            _dialog.OnClosed += () => _inv.Close(player);
            _dialog.TryOpen();
            return true;
        }

        // ── Utility ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Drop all stored items into the world. Call this when the entity is
        /// destroyed so the player doesn't lose their cargo. Server-side only.
        /// </summary>
        public void DropContents()
        {
            if (entity.Api.Side != EnumAppSide.Server || _inv == null) return;
            _inv.DropAll(entity.ServerPos.XYZ);
        }
    }
}